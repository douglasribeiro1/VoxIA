package com.voxia.keyboard.service

import android.content.Context
import android.util.Log
import com.voxia.keyboard.api.GroqClient
import com.voxia.keyboard.data.DictionaryManager
import com.voxia.keyboard.data.PromptManager
import com.voxia.keyboard.data.TopicFormatter
import com.voxia.keyboard.data.TopicHeading
import java.io.File

/**
 * Caminho único de um ditado, usado pelo teclado e pelo balão flutuante:
 * Whisper (em blocos quando longo) → marcadores de título/tópico → correção pela LLM → dicionário → títulos.
 */
class DictationEngine(context: Context) {
    enum class Stage { TRANSCRIBING, CORRECTING }

    private val groqClient = GroqClient()
    private val promptManager = PromptManager(context)
    private val dictionaryManager = DictionaryManager(context)

    /** Preenchido quando o último ditado saiu sem a correção da IA (ex.: limite de uso da Groq). */
    var lastCorrectionIssue: String? = null
        private set

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
        Log.d("VoxIA", "Enviando para Whisper (${audioFile.length()} bytes, termos: $whisperTerms)...")

        val transcription = groqClient.transcribeAudio(
            audioFile = audioFile,
            apiKey = apiKey,
            initialPrompt = whisperTerms,
            contextText = contextText
        )
        val rawTranscript = transcription.getOrElse { return Result.failure(it) }
        Log.d("VoxIA", "Transcrição Whisper concluída: '$rawTranscript'")
        if (rawTranscript.isBlank()) return Result.success("")

        onStage(Stage.CORRECTING)
        val activePrompt = promptManager.getActivePrompt()

        // Comandos "abrir tópico ... fechar tópico" viram marcadores que a LLM não altera
        val headings = mutableListOf<TopicHeading>()
        val markedRaw = TopicFormatter.extractMarkers(rawTranscript, headings)

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
                paragraphMode = activePrompt.id == "default_paragraphs"
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
        Log.d("VoxIA", "Texto final formatado: '$finalText'")
        return Result.success(if (finalText.isNotBlank()) finalText else rawTranscript)
    }
}
