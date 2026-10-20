package com.voxia.keyboard.audio

import android.util.Log
import java.io.File
import java.io.RandomAccessFile
import kotlin.math.sqrt

/**
 * Ditados longos são divididos nas pausas naturais em blocos menores que a janela de 30 s do Whisper.
 * O Whisper às vezes pula uma frase inteira quando decodifica um arquivo longo janela por janela;
 * um bloco que cabe numa única janela não sofre disso. Mesma lógica do VoxIA para Windows.
 */
object AudioChunker {
    private const val SPLIT_ABOVE_SECONDS = 30.0   // gravações mais curtas vão inteiras
    private const val MIN_CHUNK_SECONDS = 15.0
    private const val MAX_CHUNK_SECONDS = 27.0
    private const val MIN_TAIL_SECONDS = 4.0       // nunca deixa um último bloco minúsculo
    private const val FRAME_MS = 20
    private const val QUIET_WINDOW_FRAMES = 10     // corta no meio dos 200 ms mais silenciosos

    /** Posições (em amostras) onde o PCM 16 bits mono deve ser cortado. Vazio = não dividir. */
    fun findCuts(pcm: ByteArray, sampleRate: Int): List<Int> {
        val cuts = mutableListOf<Int>()
        if (sampleRate < 8000) return cuts
        val n = pcm.size / 2
        if (n < sampleRate * SPLIT_ABOVE_SECONDS) return cuts

        val frameLen = sampleRate * FRAME_MS / 1000
        val frames = n / frameLen
        val rms = DoubleArray(frames)
        for (f in 0 until frames) {
            var sum = 0.0
            val s0 = f * frameLen
            for (i in s0 until s0 + frameLen) {
                val s = ((pcm[2 * i].toInt() and 0xFF) or (pcm[2 * i + 1].toInt() shl 8)).toShort().toDouble()
                sum += s * s
            }
            rms[f] = sqrt(sum / frameLen)
        }

        val framesPerSec = 1000 / FRAME_MS
        val minF = (MIN_CHUNK_SECONDS * framesPerSec).toInt()
        val maxF = (MAX_CHUNK_SECONDS * framesPerSec).toInt()
        val tailF = (MIN_TAIL_SECONDS * framesPerSec).toInt()
        var start = 0

        while (frames - start > maxF) {
            val lo = start + minF
            val hi = minOf(start + maxF, frames - tailF) - QUIET_WINDOW_FRAMES
            if (hi <= lo) break

            // janela de 200 ms mais silenciosa; no empate prefere a mais tardia (blocos maiores, menos cortes)
            var windowSum = 0.0
            for (k in lo until lo + QUIET_WINDOW_FRAMES) windowSum += rms[k]
            var best = windowSum
            var bestAt = lo
            for (w in lo + 1..hi) {
                windowSum += rms[w + QUIET_WINDOW_FRAMES - 1] - rms[w - 1]
                if (windowSum <= best) { best = windowSum; bestAt = w }
            }

            val cutFrame = bestAt + QUIET_WINDOW_FRAMES / 2
            cuts.add(cutFrame * frameLen)
            start = cutFrame
        }
        return cuts
    }

    /**
     * Para um WAV gravado pelo AudioRecorder, grava gravacao.part01.wav, part02.wav ... ao lado dele.
     * Devolve lista vazia quando não precisa (ou não consegue) dividir; nesse caso usa-se o arquivo inteiro.
     */
    fun split(wavFile: File): List<File> {
        if (!wavFile.name.endsWith(".wav", ignoreCase = true)) return emptyList()
        return try {
            val sampleRate = AudioRecorder.SAMPLE_RATE
            if (wavFile.length() < AudioRecorder.WAV_HEADER_SIZE + sampleRate * 2 * SPLIT_ABOVE_SECONDS) return emptyList()
            val pcm = RandomAccessFile(wavFile, "r").use { raf ->
                val len = (raf.length() - AudioRecorder.WAV_HEADER_SIZE).toInt() and 1.inv()
                ByteArray(len).also {
                    raf.seek(AudioRecorder.WAV_HEADER_SIZE.toLong())
                    raf.readFully(it)
                }
            }
            val cuts = findCuts(pcm, sampleRate)
            if (cuts.isEmpty()) return emptyList()

            val bounds = listOf(0) + cuts + listOf(pcm.size / 2)
            val parts = mutableListOf<File>()
            val lengths = mutableListOf<String>()
            for (i in 0 until bounds.size - 1) {
                val s0 = bounds[i]
                val s1 = bounds[i + 1]
                val bytes = (s1 - s0) * 2
                val part = partFile(wavFile, i + 1)
                part.outputStream().use { out ->
                    out.write(AudioRecorder.buildWavHeader(bytes.toLong(), sampleRate))
                    out.write(pcm, s0 * 2, bytes)
                }
                parts.add(part)
                lengths.add(String.format(java.util.Locale.US, "%.1f", (s1 - s0) / sampleRate.toDouble()))
            }
            Log.i("AudioChunker", "Gravação dividida nas pausas em ${parts.size} blocos (${lengths.joinToString(" + ")} s).")
            parts
        } catch (e: Exception) {
            Log.e("AudioChunker", "Falha ao dividir a gravação; enviando inteira.", e)
            deleteParts(wavFile)
            emptyList()
        }
    }

    private const val MIN_SPEECH_RMS = 250.0     // abaixo disso é silêncio/ruído de ambiente em qualquer celular
    private const val SPEECH_OVER_NOISE = 3.0     // fala fica bem acima do ruído de fundo da própria gravação
    private const val MIN_SPEECH_FRAMES = 12      // pelo menos ~240 ms somados de fala

    /**
     * Quantos quadros de 20 ms têm fala (bem acima do ruído de fundo). Silêncio enviado ao Whisper
     * volta como "alucinação" (palavras do vocabulário do prompt, "VoxIA", frases de vídeo).
     */
    fun speechFrames(pcm: ByteArray, sampleRate: Int, startByte: Int = 0, endByte: Int = pcm.size): Int {
        val frameLen = sampleRate * FRAME_MS / 1000
        val first = startByte / 2
        val frames = (endByte / 2 - first) / frameLen
        if (frames <= 0) return 0
        val rms = DoubleArray(frames)
        for (f in 0 until frames) {
            val s0 = first + f * frameLen
            var sum = 0.0
            var sumSq = 0.0
            for (i in s0 until s0 + frameLen) {
                val s = ((pcm[2 * i].toInt() and 0xFF) or (pcm[2 * i + 1].toInt() shl 8)).toShort().toDouble()
                sum += s
                sumSq += s * s
            }
            val mean = sum / frameLen // tira o deslocamento DC do microfone
            rms[f] = sqrt(maxOf(0.0, sumSq / frameLen - mean * mean))
        }
        val noise = rms.sortedArray()[(frames * 0.10).toInt()]
        val threshold = maxOf(MIN_SPEECH_RMS, noise * SPEECH_OVER_NOISE)
        return rms.count { it > threshold }
    }

    fun hasSpeech(frames: Int): Boolean = frames >= MIN_SPEECH_FRAMES

    /** speechFrames de um WAV do AudioRecorder; na dúvida (outro formato, erro de leitura) supõe que há fala. */
    fun wavSpeechFrames(wavFile: File): Int {
        if (!wavFile.name.endsWith(".wav", ignoreCase = true)) return Int.MAX_VALUE
        return try {
            val pcm = RandomAccessFile(wavFile, "r").use { raf ->
                val len = (raf.length() - AudioRecorder.WAV_HEADER_SIZE).toInt() and 1.inv()
                if (len <= 0) return 0
                ByteArray(len).also {
                    raf.seek(AudioRecorder.WAV_HEADER_SIZE.toLong())
                    raf.readFully(it)
                }
            }
            speechFrames(pcm, AudioRecorder.SAMPLE_RATE)
        } catch (e: Exception) {
            Log.w("AudioChunker", "Não foi possível medir a fala em ${wavFile.name}", e)
            Int.MAX_VALUE
        }
    }

    fun deleteParts(wavFile: File) {
        var i = 1
        while (i < 1000) {
            val p = partFile(wavFile, i)
            if (!p.exists()) break
            p.delete()
            i++
        }
    }

    private fun partFile(wavFile: File, index: Int): File =
        File(wavFile.parentFile, "${wavFile.nameWithoutExtension}.part${String.format(java.util.Locale.US, "%02d", index)}.wav")
}
