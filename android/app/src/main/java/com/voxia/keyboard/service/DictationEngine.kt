package com.voxia.keyboard.service

import android.content.Context
import android.util.Log
import com.voxia.keyboard.api.GroqClient
import com.voxia.keyboard.audio.AudioChunker
import com.voxia.keyboard.audio.AudioPreprocessor
import com.voxia.keyboard.audio.AudioRecorder
import com.voxia.keyboard.data.DictionaryManager
import com.voxia.keyboard.data.PromptManager
import com.voxia.keyboard.data.TopicFormatter
import com.voxia.keyboard.data.TopicHeading
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.File

/**
 * Caminho único de um ditado, usado pelo teclado e pelo balão flutuante:
 * transcrição durante a fala (blocos longos) → tratamento do áudio restante → Whisper →
 * marcadores de título/tópico → correção pela LLM → dicionário → títulos.
 */
class DictationEngine(context: Context) {
    enum class Stage { TRANSCRIBING, CORRECTING }

    private val groqClient = GroqClient()
    private val promptManager = PromptManager(context)
    private val dictionaryManager = DictionaryManager(context)
    private var live: LiveTranscriber? = null

    /** Preenchido quando o último ditado saiu sem a correção da IA (ex.: limite de uso da Groq). */
    var lastCorrectionIssue: String? = null
        private set

    /** Chamar logo depois de o gravador começar: os blocos longos já vão sendo transcritos. */
    fun startLive(recorder: AudioRecorder, apiKey: String, contextText: String, scope: CoroutineScope) {
        cancelLive()
        live = LiveTranscriber(groqClient, recorder, apiKey, dictionaryManager.getWhisperPromptTerms(), contextText)
            .also { it.start(scope) }
    }

    fun cancelLive() {
        live?.cancel()
        live = null
    }

    /**
     * Chamar depois de parar o gravador. Junta o que já foi transcrito durante a fala, deixa no
     * arquivo só o áudio que falta e aplica o tratamento de áudio (filtro, silêncios, volume).
     * Devolve null quando não houve fala nenhuma (o arquivo é apagado).
     */
    suspend fun prepareRecording(file: File): File? = withContext(Dispatchers.IO) {
        val partial = live?.finish()
        live = null
        var audioRewritten = false
        try {
            var pcm = AudioChunker.readWavPcm(file)
            var prefix = ""
            if (partial != null) {
                prefix = partial.text.trim()
                pcm = pcm.copyOfRange(minOf(pcm.size, partial.committedSamples * 2), pcm.size)
            }

            val prep = AudioPreprocessor.process(pcm, AudioRecorder.SAMPLE_RATE)
            Log.d("VoxIA", "Tratamento do áudio: ${"%.1f".format(prep.originalSeconds)} s → ${"%.1f".format(prep.newSeconds)} s, ganho ${"%.2f".format(prep.gain)} (${prep.note}).")
            if (prep.silent && prefix.isEmpty()) {
                file.delete()
                return@withContext null
            }

            // O texto vai para o disco antes de o áudio ser cortado: assim nenhum dos dois se perde
            writePrefix(file, prefix)
            audioRewritten = true
            when {
                prep.silent -> AudioChunker.writeWav(file, ByteArray(0)) // só o prefixo vale
                prep.changed || partial != null -> AudioChunker.writeWav(file, if (prep.changed) prep.pcm else pcm)
            }
            file
        } catch (e: Exception) {
            // Na dúvida, o ditado inteiro segue sem tratamento: nunca perder a gravação
            Log.e("VoxIA", "Falha ao preparar a gravação; enviando como foi gravada.", e)
            if (!audioRewritten) prefixFile(file).delete()
            file
        }
    }

    /**
     * Devolve o texto pronto para inserir, ou "" quando não houve fala.
     * Falha = problema de rede/API: quem chamou deve guardar o áudio para reenviar.
     */
    suspend fun process(
        audioFile: File,
        apiKey: String,
        contextText: String,
        onStage: (Stage) -> Unit = {}
    ): Result<String> {
        lastCorrectionIssue = null
        onStage(Stage.TRANSCRIBING)
        val whisperTerms = dictionaryManager.getWhisperPromptTerms()
        val prefix = readPrefix(audioFile)
        Log.d("VoxIA", "Enviando para Whisper (${audioFile.length()} bytes, ${prefix.length} caracteres já transcritos)...")

        val transcription = groqClient.transcribeAudio(
            audioFile = audioFile,
            apiKey = apiKey,
            initialPrompt = whisperTerms,
            contextText = contextText,
            prefixText = prefix
        )
        val rawTranscript = transcription.getOrElse { return Result.failure(it) }
        prefixFile(audioFile).delete()
        // O texto ditado nunca vai para o logcat; só o tamanho.
        Log.d("VoxIA", "Transcrição Whisper concluída: ${rawTranscript.length} caracteres.")
        if (rawTranscript.isBlank()) return Result.success("")

        onStage(Stage.CORRECTING)
        val activePrompt = promptManager.getActivePrompt()

        // Comandos "abrir tópico ... fechar tópico" viram marcadores que a LLM não altera
        val headings = mutableListOf<TopicHeading>()
        val markedRaw = TopicFormatter.extractMarkers(rawTranscript, headings)
        if (markedRaw.isBlank()) return Result.success("")

        val correctedText: String? = if (headings.isNotEmpty() && TopicFormatter.isOnlyMarkers(markedRaw)) {
            markedRaw
        } else {
            Log.d("VoxIA", "Enviando para LLM (openai/gpt-oss-120b) com prompt '${activePrompt.title}'...")
            groqClient.correctText(
                rawText = markedRaw,
                apiKey = apiKey,
                systemPrompt = activePrompt.promptText,
                model = "openai/gpt-oss-120b",
                contextText = contextText,
                paragraphMode = activePrompt.id == "default_paragraphs",
                vocabulary = whisperTerms.take(MAX_VOCABULARY_CHARS)
            ).getOrElse {
                lastCorrectionIssue = it.message ?: "falha desconhecida"
                Log.w("VoxIA", "Correção pela IA não aplicada: $lastCorrectionIssue")
                null
            }
        }

        val finalRawText = if (!correctedText.isNullOrBlank()) correctedText else markedRaw
        var finalText = dictionaryManager.applyReplacements(finalRawText)
        if (headings.isNotEmpty()) {
            finalText = TopicFormatter.restore(
                finalText,
                headings,
                dictionaryManager.applyReplacements(markedRaw)
            ) { dictionaryManager.applyReplacements(it) }
        }
        Log.d("VoxIA", "Texto final formatado: ${finalText.length} caracteres.")
        return Result.success(if (finalText.isNotBlank()) finalText else markedRaw)
    }

    companion object {
        private const val MAX_VOCABULARY_CHARS = 1500

        /** Texto já transcrito durante a fala, guardado ao lado do áudio para o reenvio também usá-lo. */
        private fun prefixFile(audioFile: File) = File(audioFile.parentFile, audioFile.name + ".prefix.txt")

        private fun writePrefix(audioFile: File, prefix: String) {
            val f = prefixFile(audioFile)
            if (prefix.isBlank()) f.delete() else f.writeText(prefix, Charsets.UTF_8)
        }

        private fun readPrefix(audioFile: File): String = try {
            prefixFile(audioFile).takeIf { it.exists() }?.readText(Charsets.UTF_8)?.trim().orEmpty()
        } catch (e: Exception) {
            ""
        }

        /** Apaga o áudio de um ditado e o texto parcial guardado junto. */
        fun discard(audioFile: File?) {
            if (audioFile == null) return
            try { prefixFile(audioFile).delete() } catch (ignored: Exception) {}
            try { audioFile.delete() } catch (ignored: Exception) {}
        }
    }
}
