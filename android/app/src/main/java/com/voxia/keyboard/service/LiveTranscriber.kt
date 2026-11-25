package com.voxia.keyboard.service

import android.util.Log
import com.voxia.keyboard.api.GroqClient
import com.voxia.keyboard.audio.AudioChunker
import com.voxia.keyboard.audio.AudioPreprocessor
import com.voxia.keyboard.audio.AudioRecorder
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.currentCoroutineContext
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch
import kotlinx.coroutines.withTimeoutOrNull

/**
 * Transcrição durante a fala: enquanto a pessoa ainda dita, cada bloco de ~15 a 27 s é cortado
 * numa pausa (mesma regra do ditado inteiro) e já vai para o Whisper. Ao parar, só falta o
 * último trecho, então ditados longos ficam prontos quase na hora.
 *
 * A precisão é a mesma do envio inteiro: os blocos saem nos mesmos pontos de corte e cada um
 * recebe como prompt o texto dos anteriores. Se um bloco falhar (rede), a transcrição ao vivo
 * é abandonada e o ditado inteiro segue pelo caminho normal, com reenvio se preciso.
 */
class LiveTranscriber(
    private val groq: GroqClient,
    private val recorder: AudioRecorder,
    private val apiKey: String,
    private val vocabulary: String,
    private val fieldContext: String
) {
    class Partial(val committedSamples: Int, val text: String)

    @Volatile private var stopRequested = false
    @Volatile private var failed = false
    private var job: Job? = null
    private var committedSamples = 0
    private val text = StringBuilder()

    fun start(scope: CoroutineScope) {
        job = scope.launch(Dispatchers.IO) {
            try {
                loop()
            } catch (e: kotlinx.coroutines.CancellationException) {
                throw e
            } catch (e: Exception) {
                Log.e("LiveTranscriber", "Erro na transcrição ao vivo; o ditado irá inteiro ao parar.", e)
                failed = true
            }
        }
    }

    private suspend fun loop() {
        val sampleRate = AudioRecorder.SAMPLE_RATE
        val window = (AudioChunker.LIVE_WINDOW_SECONDS * sampleRate).toInt()
        while (currentCoroutineContext().isActive && !stopRequested && !failed) {
            delay(POLL_MS)
            if (stopRequested) break
            val available = recorder.recordedSamples()
            if (available - committedSamples < window) continue
            val pcm = recorder.readRecorded(committedSamples, available) ?: continue
            val cut = AudioChunker.firstCut(pcm, sampleRate) ?: continue

            val chunk = pcm.copyOfRange(0, cut * 2)
            val prep = AudioPreprocessor.process(chunk, sampleRate)
            // Bloco do meio do ditado só é pulado se estiver praticamente mudo: fala baixa
            // (celular afastado) também vai para o Whisper
            if (!prep.mute) {
                val promptContext = if (text.isEmpty()) fieldContext else text.toString()
                val result = groq.transcribeLiveChunk(if (prep.changed) prep.pcm else chunk, apiKey, vocabulary, promptContext)
                if (result.isFailure) {
                    Log.w("LiveTranscriber", "Bloco ao vivo falhou; o ditado irá inteiro ao parar: ${result.exceptionOrNull()?.message}")
                    failed = true
                    break
                }
                val piece = result.getOrDefault("").trim()
                if (piece.isNotEmpty()) {
                    if (text.isNotEmpty()) text.append(' ')
                    text.append(piece)
                }
            }
            committedSamples += cut
            Log.d("LiveTranscriber", "Bloco ao vivo pronto (${cut / sampleRate} s); ${text.length} caracteres até aqui.")
        }
    }

    /**
     * Para de cortar blocos e espera o que estiver em andamento. Devolve o que já foi transcrito,
     * ou null quando nada foi adiantado (ou houve falha) e o ditado deve ir inteiro.
     */
    suspend fun finish(): Partial? {
        stopRequested = true
        val running = job
        job = null
        // Rede travada num bloco: não deixa a pessoa esperando; o ditado vai inteiro
        if (running != null && withTimeoutOrNull(FINISH_TIMEOUT_MS) { running.join() } == null) {
            Log.w("LiveTranscriber", "Bloco ao vivo demorou demais; o ditado irá inteiro.")
            running.cancel()
            failed = true
        }
        if (failed || committedSamples == 0) return null
        return Partial(committedSamples, text.toString())
    }

    fun cancel() {
        stopRequested = true
        job?.cancel()
        job = null
    }

    private companion object {
        const val POLL_MS = 1000L
        const val FINISH_TIMEOUT_MS = 20_000L
    }
}
