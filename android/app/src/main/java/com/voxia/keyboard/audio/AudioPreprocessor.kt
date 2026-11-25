package com.voxia.keyboard.audio

import kotlin.math.abs
import kotlin.math.max
import kotlin.math.min
import kotlin.math.roundToInt
import kotlin.math.sqrt

/**
 * Prepara a gravação (PCM 16 bits mono) antes de ir ao Whisper, como na versão Windows:
 * tira o ronco grave (filtro passa-alta), corta o silêncio do começo e do fim, encurta pausas
 * muito longas e aumenta o volume de gravações baixas. A fala nunca é atenuada nem cortada:
 * quando algo parece arriscado, o áudio original volta intacto.
 */
object AudioPreprocessor {
    class Result(
        val pcm: ByteArray,
        val changed: Boolean,
        val originalSeconds: Double,
        val newSeconds: Double,
        val gain: Double,
        val note: String,
        /** Sem fala nenhuma: não deve ser enviado ao Whisper (ele inventaria palavras). */
        val silent: Boolean
    )

    private const val FRAME_MS = 20
    private const val HANG_FRAMES = 12          // 240 ms de folga em volta de qualquer fala
    private const val EDGE_PAD_FRAMES = 8       // mais 160 ms antes da primeira / depois da última fala
    private const val LONG_PAUSE_FRAMES = 125   // pausas acima de 2,5 s são encurtadas
    private const val KEPT_PAUSE_FRAMES = 30    // ... mantendo 0,6 s de cada lado do corte
    private const val HIGH_PASS_HZ = 80.0
    private const val TARGET_RMS = 3000.0
    private const val QUIET_RMS = 2200.0
    private const val MAX_GAIN = 4.0
    private const val MAX_PEAK = 30000.0
    private const val MIN_SPEECH_RMS = 150.0    // abaixo disso é ruído ambiente, não fala
    private const val MIN_SPEECH_FRAMES = 12    // 240 ms de energia de fala no mínimo

    fun process(pcm: ByteArray, sampleRate: Int): Result {
        fun untouched(note: String, seconds: Double = 0.0, silent: Boolean = false) =
            Result(pcm, false, seconds, seconds, 1.0, note, silent)

        if (pcm.size < 4 || sampleRate < 8000) return untouched("curto demais")
        val n = pcm.size / 2
        val originalSeconds = n / sampleRate.toDouble()
        if (originalSeconds < 0.6) return untouched("menos de 0,6 s", originalSeconds)

        // 1. Amostras + filtro passa-alta de 1ª ordem (tira ronco e nível DC)
        val x = FloatArray(n)
        val rc = 1.0 / (2.0 * Math.PI * HIGH_PASS_HZ)
        val dt = 1.0 / sampleRate
        val a = rc / (rc + dt)
        var prevIn = 0.0
        var prevOut = 0.0
        for (i in 0 until n) {
            val s = ((pcm[2 * i].toInt() and 0xFF) or (pcm[2 * i + 1].toInt() shl 8)).toShort().toDouble()
            val y = a * (prevOut + s - prevIn)
            prevIn = s
            prevOut = y
            x[i] = y.toFloat()
        }

        // 2. Energia por quadro
        val frameLen = sampleRate * FRAME_MS / 1000
        val frames = (n + frameLen - 1) / frameLen
        val rms = DoubleArray(frames)
        for (f in 0 until frames) {
            val s0 = f * frameLen
            val s1 = min(n, s0 + frameLen)
            var sum = 0.0
            for (i in s0 until s1) sum += x[i].toDouble() * x[i]
            rms[f] = sqrt(sum / max(1, s1 - s0))
        }

        val sorted = rms.sortedArray()
        val noise = sorted[(frames * 0.10).toInt()]
        val loud = sorted[min(frames - 1, (frames * 0.90).toInt())]
        if (loud < 60.0) return untouched("gravação praticamente muda", originalSeconds, silent = true)

        // Sem fala de verdade (só ruído do ambiente), o Whisper inventa palavras: não enviar
        val speechLevel = max(MIN_SPEECH_RMS, noise * 3.0)
        val speechFrames = rms.count { it > speechLevel }
        if (speechFrames < MIN_SPEECH_FRAMES) return untouched("nenhuma fala detectada", originalSeconds, silent = true)

        var thr = max(noise * 3.0, 100.0)
        thr = min(thr, loud * 0.25)
        thr = max(thr, 60.0)

        // 3. Quadros de fala, alargados para nunca cortar começo ou fim suave de palavra
        val active = BooleanArray(frames)
        for (f in 0 until frames) {
            if (rms[f] > thr) {
                for (k in max(0, f - HANG_FRAMES)..min(frames - 1, f + HANG_FRAMES)) active[k] = true
            }
        }
        var first = active.indexOfFirst { it }
        var last = active.indexOfLast { it }
        if (first < 0) return untouched("nenhuma fala detectada", originalSeconds)
        first = max(0, first - EDGE_PAD_FRAMES)
        last = min(frames - 1, last + EDGE_PAD_FRAMES)

        // 4. Trechos mantidos (pausas internas longas são encurtadas)
        val ranges = mutableListOf<IntArray>()
        var segStart = first
        var f2 = first
        while (f2 <= last) {
            if (active[f2]) { f2++; continue }
            val runStart = f2
            while (f2 <= last && !active[f2]) f2++
            if (f2 - runStart > LONG_PAUSE_FRAMES) {
                ranges.add(intArrayOf(segStart, runStart + KEPT_PAUSE_FRAMES - 1))
                segStart = f2 - KEPT_PAUSE_FRAMES
            }
        }
        ranges.add(intArrayOf(segStart, last))

        // 5. Ganho para gravações baixas (nunca atenua)
        var sumSq = 0.0
        var cnt = 0L
        var peak = 0.0
        for (r in ranges) {
            for (f in r[0]..r[1]) {
                if (!active[f]) continue
                val s0 = f * frameLen
                val s1 = min(n, s0 + frameLen)
                for (i in s0 until s1) {
                    val v = x[i].toDouble()
                    sumSq += v * v; cnt++
                    if (abs(v) > peak) peak = abs(v)
                }
            }
        }
        val speechRms = if (cnt > 0) sqrt(sumSq / cnt) else 0.0
        var gain = 1.0
        if (speechRms > 0 && speechRms < QUIET_RMS) {
            gain = min(MAX_GAIN, TARGET_RMS / speechRms)
            if (peak * gain > MAX_PEAK) gain = MAX_PEAK / peak
            if (gain < 1.05) gain = 1.0
        }

        // 6. Saída
        var outSamples = 0
        for (r in ranges) outSamples += max(0, min(n, (r[1] + 1) * frameLen) - r[0] * frameLen)
        if (outSamples < sampleRate * 0.3) return untouched("resultado curto demais", originalSeconds)

        val output = ByteArray(outSamples * 2)
        var pos = 0
        for (r in ranges) {
            val s0 = r[0] * frameLen
            val s1 = min(n, (r[1] + 1) * frameLen)
            for (i in s0 until s1) {
                val v = (x[i] * gain).coerceIn(-32768.0, 32767.0)
                val sv = v.roundToInt()
                output[pos++] = (sv and 0xFF).toByte()
                output[pos++] = ((sv shr 8) and 0xFF).toByte()
            }
        }

        return Result(
            output, true, originalSeconds, outSamples / sampleRate.toDouble(), gain,
            String.format(java.util.Locale.US, "limiar %.0f, ruído %.0f, fala rms %.0f", thr, noise, speechRms),
            silent = false
        )
    }
}
