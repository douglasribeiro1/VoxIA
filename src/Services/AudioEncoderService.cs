using System;
using System.IO;
using System.Text;

namespace VoxIA.Services
{
    public static class AudioEncoderService
    {
        public static string GetMimeType(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return "audio/wav";
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            switch (ext)
            {
                case ".flac": return "audio/flac";
                case ".mp3": return "audio/mp3";
                case ".m4a": return "audio/m4a";
                case ".mp4": return "audio/mp4";
                case ".ogg": return "audio/ogg";
                case ".webm": return "audio/webm";
                default: return "audio/wav";
            }
        }

        public static void SaveAudioFile(byte[] pcmData, string filePath, uint sampleRate = 16000)
        {
            if (pcmData == null) pcmData = new byte[0];

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext == ".flac")
            {
                try
                {
                    byte[] flacBytes = EncodePcmToFlac(pcmData, (int)sampleRate, 1, 16);
                    File.WriteAllBytes(filePath, flacBytes);
                    return;
                }
                catch (Exception ex)
                {
                    Logger.LogError("Error encoding FLAC, falling back to WAV format", ex);
                    string wavFallback = Path.ChangeExtension(filePath, ".wav");
                    SavePcmWavFile(pcmData, wavFallback, sampleRate);
                    return;
                }
            }

            SavePcmWavFile(pcmData, filePath, sampleRate);
        }

        public static byte[] EncodePcmToFlac(byte[] pcmData, int sampleRate = 16000, int channels = 1, int bitsPerSample = 16)
        {
            if (pcmData == null || pcmData.Length == 0)
                return new byte[0];

            int totalSamples = pcmData.Length / (channels * (bitsPerSample / 8));
            int blockSize = 4096;

            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                // 1. "fLaC" stream marker
                bw.Write(Encoding.ASCII.GetBytes("fLaC"));

                // 2. STREAMINFO metadata block (mandatory, 34 bytes data + 4 bytes header)
                bw.Write((byte)0x80);
                bw.Write((byte)0x00);
                bw.Write((byte)0x00);
                bw.Write((byte)0x22);

                WriteBigEndian16(bw, (ushort)blockSize);
                WriteBigEndian16(bw, (ushort)blockSize);
                bw.Write(new byte[6]);

                ulong streamInfoFields = 0;
                streamInfoFields |= ((ulong)sampleRate & 0xFFFFF) << 44;
                streamInfoFields |= ((ulong)(channels - 1) & 0x07) << 41;
                streamInfoFields |= ((ulong)(bitsPerSample - 1) & 0x1F) << 36;
                streamInfoFields |= ((ulong)totalSamples & 0xFFFFFFFFF);
                WriteBigEndian64(bw, streamInfoFields);

                bw.Write(new byte[16]);

                // 3. Audio Frames
                short[] samples = new short[totalSamples];
                for (int i = 0; i < totalSamples; i++)
                {
                    int offset = i * 2;
                    samples[i] = (short)(pcmData[offset] | (pcmData[offset + 1] << 8));
                }

                int sampleIndex = 0;
                int frameNumber = 0;

                while (sampleIndex < totalSamples)
                {
                    int currentBlockSize = Math.Min(blockSize, totalSamples - sampleIndex);
                    WriteFrame(bw, samples, sampleIndex, currentBlockSize, sampleRate, frameNumber++);
                    sampleIndex += currentBlockSize;
                }

                return ms.ToArray();
            }
        }

        private static void WriteFrame(BinaryWriter bw, short[] samples, int offset, int blockSize, int sampleRate, int frameNumber)
        {
            using (var frameMs = new MemoryStream())
            using (var frameBw = new BinaryWriter(frameMs))
            {
                // Sync code (14 bits: 11111111111110) + Reserved (0) + Blocking strategy (0 = fixed) => 0xFFF8
                WriteBigEndian16(frameBw, 0xFFF8);

                byte bsCode = (blockSize == 4096) ? (byte)0x0C : (byte)0x06;
                byte srCode = (sampleRate == 16000) ? (byte)0x08 : (byte)0x04;
                frameBw.Write((byte)((bsCode << 4) | (srCode & 0x0F)));
                frameBw.Write((byte)0x08); // Mono, 16-bit
                WriteUtf8UInt32(frameBw, (uint)frameNumber);

                if (bsCode == 0x06)
                {
                    frameBw.Write((byte)(blockSize - 1));
                }

                byte[] headerBytes = frameMs.ToArray();
                byte headerCrc = ComputeCrc8(headerBytes);
                frameBw.Write(headerCrc);

                // Subframe Header: Verbatim subframe type
                frameBw.Write((byte)0x02);

                // Verbatim audio samples (big-endian 16-bit)
                for (int i = 0; i < blockSize; i++)
                {
                    short sample = samples[offset + i];
                    WriteBigEndian16(frameBw, (ushort)sample);
                }

                // Frame Footer: CRC-16 of entire frame
                byte[] frameData = frameMs.ToArray();
                ushort frameCrc16 = ComputeCrc16(frameData);
                WriteBigEndian16(frameBw, frameCrc16);

                bw.Write(frameMs.ToArray());
            }
        }

        public static void SavePcmWavFile(byte[] pcmData, string filePath, uint sampleRate)
        {
            if (pcmData == null) pcmData = new byte[0];
            uint subChunk2Size = (uint)pcmData.Length;
            uint chunkSize = (uint)(36 + subChunk2Size);
            ushort numChannels = 1;
            ushort bitsPerSample = 16;
            uint byteRate = (uint)(sampleRate * numChannels * (bitsPerSample / 8));
            ushort blockAlign = (ushort)(numChannels * (bitsPerSample / 8));

            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write(Encoding.ASCII.GetBytes("RIFF"));
                bw.Write(chunkSize);
                bw.Write(Encoding.ASCII.GetBytes("WAVE"));

                bw.Write(Encoding.ASCII.GetBytes("fmt "));
                bw.Write((uint)16);
                bw.Write((ushort)1);
                bw.Write(numChannels);
                bw.Write(sampleRate);
                bw.Write(byteRate);
                bw.Write(blockAlign);
                bw.Write(bitsPerSample);

                bw.Write(Encoding.ASCII.GetBytes("data"));
                bw.Write(subChunk2Size);
                bw.Write(pcmData);

                bw.Flush();
            }
        }

        private static void WriteBigEndian16(BinaryWriter bw, ushort val)
        {
            bw.Write((byte)((val >> 8) & 0xFF));
            bw.Write((byte)(val & 0xFF));
        }

        private static void WriteBigEndian64(BinaryWriter bw, ulong val)
        {
            for (int i = 7; i >= 0; i--)
            {
                bw.Write((byte)((val >> (i * 8)) & 0xFF));
            }
        }

        private static void WriteUtf8UInt32(BinaryWriter bw, uint val)
        {
            if (val < 0x80)
            {
                bw.Write((byte)val);
            }
            else if (val < 0x800)
            {
                bw.Write((byte)(0xC0 | (val >> 6)));
                bw.Write((byte)(0x80 | (val & 0x3F)));
            }
            else
            {
                bw.Write((byte)(0xE0 | (val >> 12)));
                bw.Write((byte)(0x80 | ((val >> 6) & 0x3F)));
                bw.Write((byte)(0x80 | (val & 0x3F)));
            }
        }

        private static byte ComputeCrc8(byte[] data)
        {
            byte crc = 0;
            for (int i = 0; i < data.Length; i++)
            {
                crc ^= data[i];
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 0x80) != 0)
                        crc = (byte)((crc << 1) ^ 0x07);
                    else
                        crc <<= 1;
                }
            }
            return crc;
        }

        private static ushort ComputeCrc16(byte[] data)
        {
            ushort crc = 0;
            for (int i = 0; i < data.Length; i++)
            {
                crc ^= (ushort)(data[i] << 8);
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 0x8000) != 0)
                        crc = (ushort)((crc << 1) ^ 0x8005);
                    else
                        crc <<= 1;
                }
            }
            return crc;
        }
    }
}
