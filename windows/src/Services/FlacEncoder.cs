using System;
using System.IO;

namespace VoxIA.Services
{
    /// <summary>
    /// Codificador FLAC sem perdas (mono, 16 bits) para enviar os blocos de áudio ao Whisper.
    /// O FLAC devolve exatamente as mesmas amostras, então a precisão não muda; só o arquivo fica
    /// menor e o envio mais rápido. Usa os preditores fixos do formato (ordens 0 a 4) com resíduo em
    /// código de Rice particionado; bloco que não comprime vai sem compressão (verbatim).
    /// Mesma lógica do Android (FlacEncoder.kt), conferida bit a bit com o ffmpeg.
    /// </summary>
    public static class FlacEncoder
    {
        private const int BlockSize = 4096;
        private const int MaxPartitionOrder = 6;
        private const int MaxRiceParam = 14; // 15 é o código de escape no método de 4 bits

        /// <summary><paramref name="pcm"/> = amostras 16 bits little-endian, mono.</summary>
        public static byte[] Encode(byte[] pcm, int sampleRate)
        {
            int total = pcm.Length / 2;
            int[] samples = new int[total];
            for (int i = 0; i < total; i++)
            {
                samples[i] = (short)(pcm[2 * i] | (pcm[2 * i + 1] << 8));
            }

            using (var output = new MemoryStream(pcm.Length / 2 + 1024))
            {
                output.Write(new byte[] { (byte)'f', (byte)'L', (byte)'a', (byte)'C' }, 0, 4);
                byte[] info = StreamInfo(total, sampleRate);
                output.Write(info, 0, info.Length);

                int offset = 0;
                int frameNumber = 0;
                while (offset < total)
                {
                    int size = Math.Min(BlockSize, total - offset);
                    byte[] frame = Frame(samples, offset, size, sampleRate, frameNumber++);
                    output.Write(frame, 0, frame.Length);
                    offset += size;
                }
                return output.ToArray();
            }
        }

        private static byte[] StreamInfo(int totalSamples, int sampleRate)
        {
            var w = new BitWriter();
            w.Write(1, 1);            // último bloco de metadados
            w.Write(0, 7);            // STREAMINFO
            w.Write(34, 24);          // tamanho
            w.Write(BlockSize, 16);
            w.Write(BlockSize, 16);
            w.Write(0, 24);           // tamanho mínimo de quadro: desconhecido
            w.Write(0, 24);           // tamanho máximo de quadro: desconhecido
            w.Write(sampleRate, 20);
            w.Write(0, 3);            // canais - 1
            w.Write(15, 5);           // bits por amostra - 1
            w.Write(totalSamples, 36);
            for (int i = 0; i < 16; i++) w.Write(0, 8); // MD5 não calculado (permitido pelo formato)
            return w.ToArray();
        }

        private static byte[] Frame(int[] samples, int offset, int size, int sampleRate, int frameNumber)
        {
            var w = new BitWriter();
            w.Write(0xFFF8, 16); // sincronismo + tamanho de bloco fixo
            int bsCode = size == BlockSize ? 12 : (size <= 256 ? 6 : 7);
            int srCode;
            switch (sampleRate)
            {
                case 8000: srCode = 4; break;
                case 16000: srCode = 5; break;
                case 22050: srCode = 6; break;
                case 24000: srCode = 7; break;
                case 32000: srCode = 8; break;
                case 44100: srCode = 9; break;
                case 48000: srCode = 10; break;
                default: srCode = 0; break; // lê do STREAMINFO
            }
            w.Write(bsCode, 4);
            w.Write(srCode, 4);
            w.Write(0, 4);  // mono
            w.Write(4, 3);  // 16 bits
            w.Write(0, 1);
            WriteUtf8(w, frameNumber);
            if (bsCode == 6) w.Write(size - 1, 8);
            if (bsCode == 7) w.Write(size - 1, 16);
            w.Write(Crc8(w.ToArray()), 8);

            WriteSubframe(w, samples, offset, size);
            w.AlignToByte();
            byte[] bytes = w.ToArray();
            int crc = Crc16(bytes);
            byte[] result = new byte[bytes.Length + 2];
            Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
            result[bytes.Length] = (byte)(crc >> 8);
            result[bytes.Length + 1] = (byte)crc;
            return result;
        }

        private class RicePlan
        {
            public int PartitionOrder;
            public int[] Params;
            public long Bits;
        }

        private static void WriteSubframe(BitWriter w, int[] x, int offset, int size)
        {
            int bestOrder = -1;
            long bestBits = 16L * size + 8; // verbatim
            int[] bestResidual = null;
            RicePlan bestPlan = null;

            for (int order = 0; order <= 4; order++)
            {
                if (size <= order) break;
                int[] res = Residual(x, offset, size, order);
                RicePlan plan = PlanRice(res, size, order);
                long bits = 8L + 16L * order + plan.Bits;
                if (bits < bestBits)
                {
                    bestBits = bits; bestOrder = order; bestResidual = res; bestPlan = plan;
                }
            }

            if (bestOrder < 0)
            {
                w.Write(0, 1); w.Write(1, 6); w.Write(0, 1); // VERBATIM
                for (int i = 0; i < size; i++) w.WriteSigned(x[offset + i], 16);
                return;
            }

            w.Write(0, 1); w.Write(8 + bestOrder, 6); w.Write(0, 1); // FIXED
            for (int i = 0; i < bestOrder; i++) w.WriteSigned(x[offset + i], 16);
            w.Write(0, 2); // resíduo em Rice de 4 bits
            w.Write(bestPlan.PartitionOrder, 4);
            int partitions = 1 << bestPlan.PartitionOrder;
            int perPartition = size >> bestPlan.PartitionOrder;
            int idx = 0;
            for (int p = 0; p < partitions; p++)
            {
                int count = p == 0 ? perPartition - bestOrder : perPartition;
                int k = bestPlan.Params[p];
                w.Write(k, 4);
                for (int j = 0; j < count; j++) w.WriteRice(bestResidual[idx++], k);
            }
        }

        /// <summary>Resíduo do preditor fixo; tem size - order valores.</summary>
        private static int[] Residual(int[] x, int o, int size, int order)
        {
            int[] r = new int[size - order];
            for (int i = order; i < size; i++)
            {
                int s = o + i;
                int v;
                switch (order)
                {
                    case 0: v = x[s]; break;
                    case 1: v = x[s] - x[s - 1]; break;
                    case 2: v = x[s] - 2 * x[s - 1] + x[s - 2]; break;
                    case 3: v = x[s] - 3 * x[s - 1] + 3 * x[s - 2] - x[s - 3]; break;
                    default: v = x[s] - 4 * x[s - 1] + 6 * x[s - 2] - 4 * x[s - 3] + x[s - 4]; break;
                }
                r[i - order] = v;
            }
            return r;
        }

        private static RicePlan PlanRice(int[] res, int size, int order)
        {
            RicePlan best = null;
            for (int po = 0; po <= MaxPartitionOrder; po++)
            {
                if (size % (1 << po) != 0) break;
                int perPartition = size >> po;
                if (perPartition <= order) break;
                int partitions = 1 << po;
                int[] parameters = new int[partitions];
                long bits = 6; // método + ordem de partição
                int idx = 0;
                for (int p = 0; p < partitions; p++)
                {
                    int count = p == 0 ? perPartition - order : perPartition;
                    long sum = 0;
                    for (int j = 0; j < count; j++) sum += ZigZag(res[idx + j]);
                    long cost;
                    parameters[p] = BestParam(res, idx, count, sum, out cost);
                    bits += 4 + cost;
                    idx += count;
                }
                if (best == null || bits < best.Bits)
                {
                    best = new RicePlan { PartitionOrder = po, Params = parameters, Bits = bits };
                }
            }
            return best;
        }

        /// <summary>Parâmetro de Rice com o menor custo exato em bits, testado em torno da estimativa pela média.</summary>
        private static int BestParam(int[] res, int start, int count, long sum, out long bestCost)
        {
            bestCost = 0;
            if (count == 0) return 0;
            long mean = sum / count;
            int guess = 0;
            while (guess < MaxRiceParam && (1L << (guess + 1)) <= mean) guess++;
            int bestK = guess;
            bestCost = long.MaxValue;
            for (int k = Math.Max(0, guess - 1); k <= Math.Min(MaxRiceParam, guess + 1); k++)
            {
                long cost = 0;
                for (int j = 0; j < count; j++) cost += (ZigZag(res[start + j]) >> k) + 1 + k;
                if (cost < bestCost) { bestCost = cost; bestK = k; }
            }
            return bestK;
        }

        private static long ZigZag(int v)
        {
            return v >= 0 ? 2L * v : -2L * v - 1;
        }

        private static void WriteUtf8(BitWriter w, long value)
        {
            if (value < 0x80)
            {
                w.Write(value, 8);
            }
            else if (value < 0x800)
            {
                w.Write(0xC0 | (value >> 6), 8);
                w.Write(0x80 | (value & 0x3F), 8);
            }
            else if (value < 0x10000)
            {
                w.Write(0xE0 | (value >> 12), 8);
                w.Write(0x80 | ((value >> 6) & 0x3F), 8);
                w.Write(0x80 | (value & 0x3F), 8);
            }
            else
            {
                w.Write(0xF0 | (value >> 18), 8);
                w.Write(0x80 | ((value >> 12) & 0x3F), 8);
                w.Write(0x80 | ((value >> 6) & 0x3F), 8);
                w.Write(0x80 | (value & 0x3F), 8);
            }
        }

        private static int Crc8(byte[] data)
        {
            int crc = 0;
            foreach (byte b in data)
            {
                crc ^= b;
                for (int i = 0; i < 8; i++) crc = (crc & 0x80) != 0 ? ((crc << 1) ^ 0x07) & 0xFF : (crc << 1) & 0xFF;
            }
            return crc;
        }

        private static int Crc16(byte[] data)
        {
            int crc = 0;
            foreach (byte b in data)
            {
                crc ^= b << 8;
                for (int i = 0; i < 8; i++) crc = (crc & 0x8000) != 0 ? ((crc << 1) ^ 0x8005) & 0xFFFF : (crc << 1) & 0xFFFF;
            }
            return crc;
        }

        private class BitWriter
        {
            private readonly MemoryStream _out = new MemoryStream();
            private long _acc = 0;
            private int _nbits = 0;

            public void Write(long value, int bits)
            {
                int remaining = bits;
                while (remaining > 0)
                {
                    int take = Math.Min(remaining, 32);
                    remaining -= take;
                    long chunk = (long)((ulong)value >> remaining) & ((1L << take) - 1);
                    _acc = (_acc << take) | chunk;
                    _nbits += take;
                    while (_nbits >= 8)
                    {
                        _nbits -= 8;
                        _out.WriteByte((byte)((_acc >> _nbits) & 0xFF));
                    }
                    _acc &= (1L << _nbits) - 1;
                }
            }

            public void WriteSigned(int value, int bits)
            {
                Write((long)value & ((1L << bits) - 1), bits);
            }

            public void WriteRice(int value, int k)
            {
                long u = value >= 0 ? 2L * value : -2L * value - 1;
                long q = u >> k;
                while (q >= 32) { Write(0, 32); q -= 32; }
                Write(1, (int)q + 1); // q zeros seguidos de 1
                if (k > 0) Write(u & ((1L << k) - 1), k);
            }

            public void AlignToByte()
            {
                if (_nbits > 0) Write(0, 8 - _nbits);
            }

            public byte[] ToArray()
            {
                byte[] bytes = _out.ToArray();
                if (_nbits == 0) return bytes;
                byte[] result = new byte[bytes.Length + 1];
                Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
                result[bytes.Length] = (byte)((_acc << (8 - _nbits)) & 0xFF);
                return result;
            }
        }
    }
}
