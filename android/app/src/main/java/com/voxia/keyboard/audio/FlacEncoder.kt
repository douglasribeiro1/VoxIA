package com.voxia.keyboard.audio

import java.io.ByteArrayOutputStream

/**
 * Codificador FLAC sem perdas (mono, 16 bits) para enviar o áudio ao Whisper.
 *
 * O FLAC devolve exatamente as mesmas amostras do WAV, então a precisão da transcrição não muda;
 * só o arquivo fica menor (fala costuma cair para 45–60 % do WAV), o que acelera o envio no 4G.
 * Usa os preditores fixos do formato (ordens 0 a 4) com resíduo em código de Rice particionado;
 * se um bloco não comprimir, ele vai sem compressão (verbatim).
 */
object FlacEncoder {
    private const val BLOCK_SIZE = 4096
    private const val MAX_PARTITION_ORDER = 6
    private const val MAX_RICE_PARAM = 14 // 15 é o código de escape no método de 4 bits

    /** [pcm] = amostras 16 bits little-endian, mono. */
    fun encode(pcm: ByteArray, sampleRate: Int): ByteArray {
        val total = pcm.size / 2
        val samples = IntArray(total) { i ->
            ((pcm[2 * i].toInt() and 0xFF) or (pcm[2 * i + 1].toInt() shl 8)).toShort().toInt()
        }
        return encode(samples, sampleRate)
    }

    fun encode(samples: IntArray, sampleRate: Int): ByteArray {
        val out = ByteArrayOutputStream(samples.size + 1024)
        out.write(byteArrayOf('f'.code.toByte(), 'L'.code.toByte(), 'a'.code.toByte(), 'C'.code.toByte()))
        out.write(streamInfo(samples.size, sampleRate))

        var offset = 0
        var frameNumber = 0
        while (offset < samples.size) {
            val size = minOf(BLOCK_SIZE, samples.size - offset)
            out.write(frame(samples, offset, size, sampleRate, frameNumber++))
            offset += size
        }
        return out.toByteArray()
    }

    private fun streamInfo(totalSamples: Int, sampleRate: Int): ByteArray {
        val w = BitWriter()
        w.write(1, 1)            // último bloco de metadados
        w.write(0, 7)            // STREAMINFO
        w.write(34, 24)          // tamanho
        w.write(BLOCK_SIZE.toLong(), 16)
        w.write(BLOCK_SIZE.toLong(), 16)
        w.write(0, 24)           // tamanho mínimo de quadro: desconhecido
        w.write(0, 24)           // tamanho máximo de quadro: desconhecido
        w.write(sampleRate.toLong(), 20)
        w.write(0, 3)            // canais - 1
        w.write(15, 5)           // bits por amostra - 1
        w.write(totalSamples.toLong(), 36)
        for (i in 0 until 16) w.write(0, 8) // MD5 não calculado (permitido pelo formato)
        return w.toByteArray()
    }

    private fun frame(samples: IntArray, offset: Int, size: Int, sampleRate: Int, frameNumber: Int): ByteArray {
        val w = BitWriter()
        w.write(0xFFF8, 16) // sincronismo + tamanho de bloco fixo
        val bsCode = when {
            size == BLOCK_SIZE -> 12            // 4096
            size <= 256 -> 6                    // 8 bits no fim do cabeçalho
            else -> 7                           // 16 bits no fim do cabeçalho
        }
        val srCode = when (sampleRate) {
            8000 -> 4; 16000 -> 5; 22050 -> 6; 24000 -> 7; 32000 -> 8; 44100 -> 9; 48000 -> 10
            else -> 0 // lê do STREAMINFO
        }
        w.write(bsCode.toLong(), 4)
        w.write(srCode.toLong(), 4)
        w.write(0, 4)  // mono
        w.write(4, 3)  // 16 bits
        w.write(0, 1)
        writeUtf8(w, frameNumber.toLong())
        if (bsCode == 6) w.write((size - 1).toLong(), 8)
        if (bsCode == 7) w.write((size - 1).toLong(), 16)
        w.write(crc8(w.toByteArray()).toLong(), 8)

        writeSubframe(w, samples, offset, size)
        w.alignToByte()
        val bytes = w.toByteArray()
        val crc = crc16(bytes)
        val outFrame = bytes.copyOf(bytes.size + 2)
        outFrame[bytes.size] = (crc shr 8).toByte()
        outFrame[bytes.size + 1] = crc.toByte()
        return outFrame
    }

    private fun writeSubframe(w: BitWriter, x: IntArray, offset: Int, size: Int) {
        var bestOrder = -1
        var bestBits = 16L * size + 8 // verbatim
        var bestResidual: IntArray? = null
        var bestPlan: RicePlan? = null

        for (order in 0..4) {
            if (size <= order) break
            val res = residual(x, offset, size, order)
            val plan = planRice(res, size, order)
            val bits = 8L + 16L * order + plan.bits
            if (bits < bestBits) {
                bestBits = bits; bestOrder = order; bestResidual = res; bestPlan = plan
            }
        }

        if (bestOrder < 0) {
            w.write(0, 1); w.write(1, 6); w.write(0, 1) // VERBATIM
            for (i in 0 until size) w.writeSigned(x[offset + i], 16)
            return
        }

        w.write(0, 1); w.write((8 + bestOrder).toLong(), 6); w.write(0, 1) // FIXED
        for (i in 0 until bestOrder) w.writeSigned(x[offset + i], 16)
        val res = bestResidual!!
        val plan = bestPlan!!
        w.write(0, 2) // resíduo em Rice de 4 bits
        w.write(plan.partitionOrder.toLong(), 4)
        val partitions = 1 shl plan.partitionOrder
        val perPartition = size shr plan.partitionOrder
        var idx = 0
        for (p in 0 until partitions) {
            val count = if (p == 0) perPartition - bestOrder else perPartition
            val k = plan.params[p]
            w.write(k.toLong(), 4)
            for (j in 0 until count) w.writeRice(res[idx++], k)
        }
    }

    /** Resíduo do preditor fixo; tem size - order valores. */
    private fun residual(x: IntArray, o: Int, size: Int, order: Int): IntArray {
        val r = IntArray(size - order)
        for (i in order until size) {
            val s = o + i
            r[i - order] = when (order) {
                0 -> x[s]
                1 -> x[s] - x[s - 1]
                2 -> x[s] - 2 * x[s - 1] + x[s - 2]
                3 -> x[s] - 3 * x[s - 1] + 3 * x[s - 2] - x[s - 3]
                else -> x[s] - 4 * x[s - 1] + 6 * x[s - 2] - 4 * x[s - 3] + x[s - 4]
            }
        }
        return r
    }

    private class RicePlan(val partitionOrder: Int, val params: IntArray, val bits: Long)

    private fun planRice(res: IntArray, size: Int, order: Int): RicePlan {
        var best: RicePlan? = null
        for (po in 0..MAX_PARTITION_ORDER) {
            if (size % (1 shl po) != 0) break
            val perPartition = size shr po
            if (perPartition <= order) break
            val partitions = 1 shl po
            val params = IntArray(partitions)
            var bits = 6L // método + ordem de partição
            var idx = 0
            for (p in 0 until partitions) {
                val count = if (p == 0) perPartition - order else perPartition
                var sum = 0L
                for (j in 0 until count) sum += zigzag(res[idx + j])
                val (k, cost) = bestParam(res, idx, count, sum)
                params[p] = k
                bits += 4 + cost
                idx += count
            }
            if (best == null || bits < best.bits) best = RicePlan(po, params, bits)
        }
        return best!!
    }

    /** Parâmetro de Rice com o menor custo exato em bits, testado em torno da estimativa pela média. */
    private fun bestParam(res: IntArray, start: Int, count: Int, sum: Long): Pair<Int, Long> {
        if (count == 0) return 0 to 0L
        val mean = sum / count
        var guess = 0
        while (guess < MAX_RICE_PARAM && (1L shl (guess + 1)) <= mean) guess++
        var bestK = guess
        var bestCost = Long.MAX_VALUE
        for (k in maxOf(0, guess - 1)..minOf(MAX_RICE_PARAM, guess + 1)) {
            var cost = 0L
            for (j in 0 until count) cost += (zigzag(res[start + j]) ushr k) + 1 + k
            if (cost < bestCost) { bestCost = cost; bestK = k }
        }
        return bestK to bestCost
    }

    private fun zigzag(v: Int): Long = if (v >= 0) 2L * v else -2L * v - 1

    private fun writeUtf8(w: BitWriter, value: Long) {
        when {
            value < 0x80 -> w.write(value, 8)
            value < 0x800 -> {
                w.write(0xC0 or (value shr 6).toInt(), 8)
                w.write(0x80 or (value and 0x3F).toInt(), 8)
            }
            value < 0x10000 -> {
                w.write(0xE0 or (value shr 12).toInt(), 8)
                w.write(0x80 or ((value shr 6) and 0x3F).toInt(), 8)
                w.write(0x80 or (value and 0x3F).toInt(), 8)
            }
            else -> {
                w.write(0xF0 or (value shr 18).toInt(), 8)
                w.write(0x80 or ((value shr 12) and 0x3F).toInt(), 8)
                w.write(0x80 or ((value shr 6) and 0x3F).toInt(), 8)
                w.write(0x80 or (value and 0x3F).toInt(), 8)
            }
        }
    }

    private fun crc8(data: ByteArray): Int {
        var crc = 0
        for (b in data) {
            crc = crc xor (b.toInt() and 0xFF)
            for (i in 0 until 8) crc = if (crc and 0x80 != 0) ((crc shl 1) xor 0x07) and 0xFF else (crc shl 1) and 0xFF
        }
        return crc
    }

    private fun crc16(data: ByteArray): Int {
        var crc = 0
        for (b in data) {
            crc = crc xor ((b.toInt() and 0xFF) shl 8)
            for (i in 0 until 8) crc = if (crc and 0x8000 != 0) ((crc shl 1) xor 0x8005) and 0xFFFF else (crc shl 1) and 0xFFFF
        }
        return crc
    }

    private class BitWriter {
        private val out = ByteArrayOutputStream()
        private var acc = 0L
        private var nbits = 0

        fun write(value: Int, bits: Int) = write(value.toLong(), bits)

        fun write(value: Long, bits: Int) {
            var remaining = bits
            while (remaining > 0) {
                val take = minOf(remaining, 32)
                remaining -= take
                val chunk = (value ushr remaining) and ((1L shl take) - 1)
                acc = (acc shl take) or chunk
                nbits += take
                while (nbits >= 8) {
                    nbits -= 8
                    out.write(((acc ushr nbits) and 0xFF).toInt())
                }
                acc = acc and ((1L shl nbits) - 1)
            }
        }

        fun writeSigned(value: Int, bits: Int) = write(value.toLong() and ((1L shl bits) - 1), bits)

        fun writeRice(value: Int, k: Int) {
            val u = if (value >= 0) 2L * value else -2L * value - 1
            var q = u ushr k
            while (q >= 32) { write(0, 32); q -= 32 }
            write(1L, q.toInt() + 1) // q zeros seguidos de 1
            if (k > 0) write(u and ((1L shl k) - 1), k)
        }

        fun alignToByte() { if (nbits > 0) write(0, 8 - nbits) }

        fun toByteArray(): ByteArray {
            val bytes = out.toByteArray()
            if (nbits == 0) return bytes
            return bytes + ((acc shl (8 - nbits)) and 0xFF).toByte()
        }
    }
}
