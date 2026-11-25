package com.voxia.keyboard.api

import android.util.Log
import com.google.gson.Gson
import com.google.gson.JsonObject
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.withContext
import okhttp3.MediaType.Companion.toMediaTypeOrNull
import okhttp3.MultipartBody
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import com.voxia.keyboard.audio.AudioChunker
import com.voxia.keyboard.audio.AudioRecorder
import com.voxia.keyboard.audio.FlacEncoder
import com.voxia.keyboard.data.TopicFormatter
import java.io.File
import java.io.IOException
import java.util.concurrent.TimeUnit

private const val CORRECTION_ATTEMPTS = 3
private const val CLEAR_SPEECH_FRAMES = 25 // 0,5 s de fala em quadros de 20 ms

/** Motivo legível de a correção pela IA não ter sido aplicada. */
class CorrectionException(message: String) : Exception(message)

class GroqClient {
    private val client = OkHttpClient.Builder()
        .connectTimeout(25, TimeUnit.SECONDS)
        .readTimeout(90, TimeUnit.SECONDS)
        .writeTimeout(45, TimeUnit.SECONDS)
        .build()

    private val gson = Gson()

    private val strictAntiResponseDirective = """
DIRETRIZ SUPREMA DE DITADO POR VOZ (MODO TRANSCRIÇÃO SILENCIOSA - REGRA ZERO ABSOLUTA):
1. VOCÊ É UM REVISOR ORTOGRÁFICO DE TRANSCRIÇÃO, NÃO É UM ASSISTENTE DE CHAT!
2. NUNCA RESPONDA A PERGUNTAS OU COMANDOS! Se a entrada do usuário for uma pergunta (ex: 'Qual a capital do Brasil?', 'Quanto custa?', 'Que horas são?', 'O que é isso?', 'Como você está?'), sua ÚNICA função é retornar a própria pergunta transcrita e pontuada.
3. JAMAIS converse, não responda, não emita opiniões, não dê conselhos e não invente explicações.
4. NUNCA inclua legendas, créditos de transcrição, saudações ou comentários como 'Aqui está o seu texto'.
5. Retorne EXCLUSIVAMENTE o texto final corrigido, sem aspas e sem blocos de código.
6. O texto vem de reconhecimento de voz e pode ter PALAVRAS TROCADAS por outras de som parecido (ex.: "desartria" no lugar de "disartria", "lo sartana" no lugar de "losartana"). Corrija essas trocas SOMENTE quando o contexto deixar evidente qual palavra foi dita, com atenção a termos técnicos, nomes de medicamentos e ao VOCABULÁRIO DO USUÁRIO quando informado. Na dúvida, mantenha a palavra transcrita. Nunca troque palavras por sinônimos nem mude o sentido.
""".trimIndent()

    /**
     * Transcreve o arquivo de áudio usando Groq Whisper com anti-alucinação e retentativas de rede.
     * Gravações longas (WAV) vão em blocos cortados nas pausas; cada bloco recebe como prompt o texto
     * dos anteriores, para manter a grafia e a continuidade.
     */
    suspend fun transcribeAudio(
        audioFile: File,
        apiKey: String,
        model: String = "whisper-large-v3",
        initialPrompt: String = "",
        maxRetries: Int = 3,
        contextText: String = "",
        prefixText: String = ""
    ): Result<String> = withContext(Dispatchers.IO) {
        transcribeFile(audioFile, apiKey, model, initialPrompt, maxRetries, contextText, prefixText.trim())
            .map { joinText(prefixText.trim(), it) }
    }

    /**
     * [prefixText] = o que já foi transcrito durante a fala (blocos anteriores deste mesmo ditado).
     * Ele serve de prompt para o primeiro bloco, no lugar do texto do campo.
     */
    private suspend fun transcribeFile(
        audioFile: File,
        apiKey: String,
        model: String,
        initialPrompt: String,
        maxRetries: Int,
        contextText: String,
        prefixText: String
    ): Result<String> {
        val firstContext = if (prefixText.isNotBlank()) prefixText else contextText
        val trimmedKey = apiKey.trim()
        if (trimmedKey.isBlank()) {
            return Result.failure(Exception("Chave da Groq API não configurada."))
        }

        if (!audioFile.exists() || audioFile.length() < 100) {
            // Com prefixo, o resto do ditado já foi transcrito durante a fala
            if (prefixText.isNotBlank()) return Result.success("")
            return Result.failure(Exception("Arquivo de áudio inválido ou vazio."))
        }

        if (!AudioChunker.hasSpeech(AudioChunker.wavSpeechFrames(audioFile))) {
            Log.i("GroqClient", "Gravação sem fala detectável. Nada enviado ao Whisper.")
            return Result.success("")
        }

        val parts = AudioChunker.split(audioFile)
        try {
            if (parts.isEmpty()) {
                val prompt = buildWhisperPrompt(initialPrompt, firstContext)
                val text = sendToWhisper(audioFile, model, trimmedKey, prompt, maxRetries)
                    .getOrElse { return Result.failure(it) }
                if (isPromptEcho(text, firstContext) || isVocabularyEcho(text, prompt, AudioChunker.wavSpeechFrames(audioFile))) {
                    Log.i("GroqClient", "Whisper devolveu apenas palavras do prompt (sem fala real). Descartando.")
                    return Result.success("")
                }
                return Result.success(text)
            }

            val soFar = StringBuilder()
            for ((index, part) in parts.withIndex()) {
                val speech = AudioChunker.wavSpeechFrames(part)
                if (!AudioChunker.hasSpeech(speech)) {
                    Log.i("GroqClient", "Bloco ${index + 1}/${parts.size} sem fala. Ignorando.")
                    continue
                }
                val promptContext = if (soFar.isEmpty()) firstContext else joinText(prefixText, soFar.toString())
                val prompt = buildWhisperPrompt(initialPrompt, promptContext)
                val text = sendToWhisper(part, model, trimmedKey, prompt, maxRetries)
                    .getOrElse { return Result.failure(it) }
                if (isPromptEcho(text, promptContext) || isVocabularyEcho(text, prompt, speech)) {
                    Log.i("GroqClient", "Bloco ${index + 1}/${parts.size} só repetiu o prompt. Ignorando.")
                    continue
                }
                Log.d("GroqClient", "Whisper bloco ${index + 1}/${parts.size}: ${text.length} caracteres.")
                if (text.isNotBlank()) {
                    if (soFar.isNotEmpty()) soFar.append(' ')
                    soFar.append(text)
                }
            }
            return Result.success(soFar.toString())
        } finally {
            AudioChunker.deleteParts(audioFile)
        }
    }

    /**
     * Um bloco cortado numa pausa enquanto a pessoa ainda fala (ver LiveTranscriber).
     * Devolve "" quando o bloco não tem fala ou o Whisper só repetiu o prompt.
     */
    suspend fun transcribeLiveChunk(
        pcm: ByteArray,
        apiKey: String,
        vocabulary: String,
        promptContext: String,
        model: String = "whisper-large-v3"
    ): Result<String> = withContext(Dispatchers.IO) {
        val trimmedKey = apiKey.trim()
        if (trimmedKey.isBlank()) return@withContext Result.failure(Exception("Chave da Groq API não configurada."))
        val speech = AudioChunker.speechFrames(pcm, AudioRecorder.SAMPLE_RATE)
        if (!AudioChunker.hasSpeech(speech)) return@withContext Result.success("")
        val prompt = buildWhisperPrompt(vocabulary, promptContext)
        val flac = FlacEncoder.encode(pcm, AudioRecorder.SAMPLE_RATE)
        val text = sendAudio(flac, "bloco.flac", "audio/flac", model, trimmedKey, prompt, 3)
            .getOrElse { return@withContext Result.failure(it) }
        if (isPromptEcho(text, promptContext) || isVocabularyEcho(text, prompt, speech)) {
            Log.i("GroqClient", "Bloco ao vivo só repetiu o prompt. Ignorando.")
            return@withContext Result.success("")
        }
        Result.success(text)
    }

    private fun joinText(a: String, b: String): String = when {
        a.isBlank() -> b.trim()
        b.isBlank() -> a.trim()
        else -> a.trim() + " " + b.trim()
    }

    /**
     * WAV vai comprimido em FLAC (sem perdas: as amostras são as mesmas, só o arquivo fica
     * menor e o envio mais rápido). Se a compressão falhar, vai o WAV original.
     */
    private suspend fun sendToWhisper(
        audioFile: File,
        model: String,
        apiKey: String,
        prompt: String,
        maxRetries: Int
    ): Result<String> {
        if (audioFile.name.endsWith(".wav", ignoreCase = true)) {
            try {
                val pcm = AudioChunker.readWavPcm(audioFile)
                val flac = FlacEncoder.encode(pcm, AudioRecorder.SAMPLE_RATE)
                Log.d("GroqClient", "Áudio comprimido para envio: ${audioFile.length()} → ${flac.size} bytes.")
                return sendAudio(flac, audioFile.nameWithoutExtension + ".flac", "audio/flac", model, apiKey, prompt, maxRetries)
            } catch (e: kotlinx.coroutines.CancellationException) {
                throw e
            } catch (e: Exception) {
                Log.w("GroqClient", "Falha ao comprimir em FLAC; enviando WAV.", e)
            }
        }
        val mediaType = if (audioFile.name.endsWith(".wav", ignoreCase = true)) "audio/wav" else "audio/m4a"
        return sendAudio(audioFile.readBytes(), audioFile.name, mediaType, model, apiKey, prompt, maxRetries)
    }

    private suspend fun sendAudio(
        audio: ByteArray,
        fileName: String,
        mediaType: String,
        model: String,
        apiKey: String,
        prompt: String,
        maxRetries: Int
    ): Result<String> {
        var lastException: Exception = Exception("Erro desconhecido de transcrição")

        for (attempt in 1..maxRetries) {
            try {
                Log.d("GroqClient", "Tentativa $attempt de transcrição Whisper ($fileName, ${audio.size} bytes)...")

                val requestBody = MultipartBody.Builder()
                    .setType(MultipartBody.FORM)
                    .addFormDataPart(
                        "file",
                        fileName,
                        audio.toRequestBody(mediaType.toMediaTypeOrNull())
                    )
                    .addFormDataPart("model", model)
                    .addFormDataPart("language", "pt")
                    .addFormDataPart("temperature", "0.0")
                    .addFormDataPart("prompt", prompt)
                    .addFormDataPart("response_format", "json")
                    .build()

                val request = Request.Builder()
                    .url("https://api.groq.com/openai/v1/audio/transcriptions")
                    .addHeader("Authorization", "Bearer $apiKey")
                    .post(requestBody)
                    .build()

                val response = client.newCall(request).execute()
                val bodyString = response.body?.string() ?: ""

                if (!response.isSuccessful) {
                    val errorDetail = try {
                        val errJson = gson.fromJson(bodyString, JsonObject::class.java)
                        errJson.getAsJsonObject("error")?.get("message")?.asString ?: bodyString
                    } catch (e: kotlinx.coroutines.CancellationException) {
                throw e
            } catch (e: Exception) {
                        bodyString
                    }

                    // Se for erro de cliente (401, 400), não adianta tentar novamente
                    if (response.code in 400..499 && response.code != 429) {
                        Log.e("GroqClient", "Whisper HTTP Client Error ${response.code}: $errorDetail")
                        return Result.failure(Exception("Erro Whisper (${response.code}): $errorDetail"))
                    }

                    // Se for 429 ou 5xx (servidor ocupado/falha), retenta se houver tentativas
                    lastException = Exception("Erro Whisper (${response.code}): $errorDetail")
                    if (attempt < maxRetries) {
                        delay(attempt * 1000L)
                        continue
                    }
                    return Result.failure(lastException)
                }

                val json = gson.fromJson(bodyString, JsonObject::class.java)
                val rawText = json.get("text")?.asString ?: ""
                val sanitized = sanitizeWhisperText(rawText)
                return Result.success(if (sanitized.isNotBlank()) sanitized else rawText.trim())

            } catch (e: IOException) {
                Log.w("GroqClient", "Oscilação de rede na tentativa $attempt: ${e.message}")
                lastException = Exception("Falha de conexão com a internet. O áudio foi preservado para reenvio.")
                if (attempt < maxRetries) {
                    delay(attempt * 1200L)
                }
            } catch (e: kotlinx.coroutines.CancellationException) {
                throw e
            } catch (e: Exception) {
                Log.e("GroqClient", "Exceção inesperada no Whisper", e)
                lastException = e
                break
            }
        }

        return Result.failure(lastException)
    }

    /**
     * Corrige e formata a transcrição usando Groq LLM com Guardrails anti-resposta
     */
    suspend fun correctText(
        rawText: String,
        apiKey: String,
        systemPrompt: String,
        model: String = "openai/gpt-oss-120b",
        contextText: String = "",
        paragraphMode: Boolean = false,
        vocabulary: String = ""
    ): Result<String> = withContext(Dispatchers.IO) {
        if (rawText.isBlank()) {
            return@withContext Result.success("")
        }

        val trimmedKey = apiKey.trim()
        if (systemPrompt.isBlank() || trimmedKey.isBlank()) {
            return@withContext Result.success(rawText)
        }

        val enforcedSystemPrompt = "$strictAntiResponseDirective\n\nINSTRUÇÕES DO PERFIL:\n$systemPrompt"
        var maxTokens = Math.max(1024, Math.min(7000, (rawText.length * 2) + 1500))
        var lastIssue = "falha desconhecida"

        // Falha na correção não pode virar texto bruto em silêncio: tenta de novo nos erros
        // passageiros (limite de uso, servidor, rede) e, se não der, devolve o motivo.
        for (attempt in 1..CORRECTION_ATTEMPTS) {
            try {
                val jsonBody = JsonObject().apply {
                    addProperty("model", model)
                    addProperty("temperature", 0.0)
                    addProperty("max_tokens", maxTokens)
                    if (model.contains("gpt-oss", ignoreCase = true)) {
                        addProperty("reasoning_effort", "low")
                    }

                    val messages = com.google.gson.JsonArray().apply {
                        add(JsonObject().apply {
                            addProperty("role", "system")
                            addProperty("content", enforcedSystemPrompt)
                        })
                        add(JsonObject().apply {
                            addProperty("role", "user")
                            addProperty("content", buildUserMessage(rawText, contextText, paragraphMode, vocabulary))
                        })
                    }
                    add("messages", messages)
                }

                val request = Request.Builder()
                    .url("https://api.groq.com/openai/v1/chat/completions")
                    .addHeader("Authorization", "Bearer $trimmedKey")
                    .addHeader("Content-Type", "application/json")
                    .post(jsonBody.toString().toRequestBody("application/json".toMediaTypeOrNull()))
                    .build()

                val response = client.newCall(request).execute()
                val bodyString = response.body?.string() ?: ""

                if (!response.isSuccessful) {
                    Log.w("GroqClient", "LLM Correction HTTP Error ${response.code} (tentativa $attempt): $bodyString")
                    lastIssue = when (response.code) {
                        429 -> "limite de uso da Groq atingido"
                        401, 403 -> "chave da Groq recusada"
                        in 500..599 -> "servidor da Groq instável"
                        else -> "erro ${response.code} da Groq"
                    }
                    if (response.code == 429 || response.code >= 500) {
                        delay(retryDelayMs(response.header("retry-after"), attempt))
                        continue
                    }
                    break
                }

                val json = gson.fromJson(bodyString, JsonObject::class.java)
                val choices = json.getAsJsonArray("choices")
                if (choices == null || choices.size() == 0) {
                    lastIssue = "resposta vazia da IA"
                    continue
                }
                val firstChoice = choices[0].asJsonObject
                val finishReason = firstChoice.get("finish_reason")?.takeIf { !it.isJsonNull }?.asString
                if (finishReason == "length") {
                    // O raciocínio do modelo também consome max_tokens: tenta de novo com folga maior
                    Log.w("GroqClient", "Saída da LLM truncada (max_tokens=$maxTokens). Tentando com mais espaço.")
                    lastIssue = "resposta da IA cortada"
                    maxTokens = Math.min(16000, maxTokens * 2)
                    continue
                }
                val message = firstChoice.getAsJsonObject("message")
                val content = message?.get("content")?.takeIf { !it.isJsonNull }?.asString ?: ""
                if (content.isBlank()) {
                    lastIssue = "resposta vazia da IA"
                    continue
                }
                val cleaned = cleanOutput(content, rawText)
                if (echoesContext(cleaned, rawText, contextText)) {
                    Log.w("GroqClient", "Saída da LLM repetiu o contexto do campo. Usando texto bruto.")
                    return@withContext Result.failure(CorrectionException("a IA repetiu o texto do campo"))
                }
                if (rawText.length >= 200 && cleaned.length < rawText.length * 0.75) {
                    Log.w("GroqClient", "Saída da LLM muito menor que a entrada (${cleaned.length} vs ${rawText.length}). Usando texto bruto.")
                    return@withContext Result.failure(CorrectionException("a IA encurtou demais o texto"))
                }
                return@withContext Result.success(if (cleaned.isNotBlank()) cleaned else rawText)
            } catch (e: kotlinx.coroutines.CancellationException) {
                throw e
            } catch (e: Exception) {
                Log.e("GroqClient", "Falha na correção de texto (tentativa $attempt)", e)
                lastIssue = if (e is java.io.InterruptedIOException) "a IA demorou demais" else "falha de conexão"
                if (attempt < CORRECTION_ATTEMPTS) delay(retryDelayMs(null, attempt))
            }
        }
        Result.failure(CorrectionException(lastIssue))
    }

    /** Espera antes de nova tentativa: respeita o retry-after da Groq até 8 s. */
    private fun retryDelayMs(retryAfter: String?, attempt: Int): Long {
        val seconds = retryAfter?.toDoubleOrNull()
        return if (seconds != null) (seconds * 1000).toLong().coerceIn(500, 8000) else 1500L * attempt
    }

    /**
     * O Whisper se apoia no FINAL do prompt, então o texto já escrito no campo vai por último.
     */
    private fun buildWhisperPrompt(vocabulary: String, contextText: String): String {
        val base = if (vocabulary.isNotBlank()) {
            "Ditado por voz em português brasileiro sem legendas. Vocabulário: $vocabulary"
        } else {
            "Ditado por voz em português brasileiro sem legendas."
        }
        if (contextText.isBlank()) return base
        var tail = if (contextText.length > 220) contextText.substring(contextText.length - 220) else contextText
        val firstSpace = tail.indexOf(' ')
        if (contextText.length > 220 && firstSpace in 1..39) tail = tail.substring(firstSpace + 1)
        return "$base $tail"
    }

    private fun isPromptEcho(transcript: String, contextText: String): Boolean {
        if (transcript.isBlank() || contextText.isBlank()) return false
        val t = transcript.trim().trimEnd('.', '!', '?', ' ')
        if (t.length < 30) return false
        return contextText.contains(t, ignoreCase = true)
    }

    /**
     * Fala real não sai só com palavras do prompt. Uma resposta curta feita apenas delas
     * ("VoxIA.", "Disartria.") com pouca fala no áudio é o Whisper repetindo o vocabulário diante
     * de ruído. Com fala clara (meio segundo ou mais) a palavra foi dita de verdade e fica.
     */
    private fun isVocabularyEcho(transcript: String, prompt: String, speechFrames: Int): Boolean {
        if (speechFrames >= CLEAR_SPEECH_FRAMES) return false
        val words = Regex("[\\p{L}\\p{N}]+")
        val spoken = words.findAll(transcript.lowercase()).map { it.value }.toList()
        if (spoken.isEmpty() || spoken.size > 4) return false
        val promptWords = words.findAll(prompt.lowercase()).map { it.value }.toSet()
        return spoken.all { it in promptWords }
    }

    private fun echoesContext(corrected: String, rawText: String, contextText: String): Boolean {
        if (contextText.isBlank() || corrected.isEmpty()) return false
        val probe = if (contextText.length > 40) contextText.substring(contextText.length - 40) else contextText
        return corrected.contains(probe, ignoreCase = true) && !rawText.contains(probe, ignoreCase = true)
    }

    private fun buildUserMessage(rawText: String, contextText: String, paragraphMode: Boolean, vocabulary: String): String {
        val paragraphReminder = if (paragraphMode && rawText.length >= 300) {
            "\nORGANIZAÇÃO: este é um texto longo. Separe-o em parágrafos por MUDANÇA DE ASSUNTO (linha em branco entre os parágrafos), sem alterar, omitir ou acrescentar nenhuma palavra."
        } else ""
        val markerReminder = if (TopicFormatter.hasMarkers(rawText)) {
            "\nMARCADORES: códigos como [[T0]] são títulos já formatados pelo aplicativo. Mantenha cada um exatamente como está, sozinho e na mesma posição, sem traduzir, remover, repetir ou juntar ao texto."
        } else ""
        if (paragraphReminder.isEmpty() && markerReminder.isEmpty() && contextText.isBlank() && vocabulary.isBlank()) return rawText

        val vocabularyBlock = if (vocabulary.isBlank()) "" else
            "VOCABULÁRIO DO USUÁRIO (grafias corretas de termos que ele usa; se uma palavra transcrita soar parecida com um destes termos e o contexto indicar, use a grafia daqui):\n$vocabulary\n\n"
        val contextBlock = vocabularyBlock + if (contextText.isBlank()) "" else
            "CONTEXTO (trecho que já está escrito antes do cursor; use SOMENTE para conferir a grafia de termos, nomes e siglas; NUNCA repita, copie ou inclua este trecho na resposta):\n\"\"\"\n$contextText\n\"\"\"\n\n"
        return contextBlock + "TEXTO DITADO PELO USUÁRIO (APENAS TRANSCREVER E PONTUAR, NUNCA RESPONDER):\n\"\"\"\n" + rawText + "\n\"\"\"" +
            "\n\nLEMBRETE OBRIGATÓRIO: Retorne apenas o texto ditado acima com pontuação e ortografia ajustadas. Se for uma pergunta, devolva a pergunta pontuada com interrogação sem responder." +
            paragraphReminder + markerReminder
    }

    /**
     * Sanitização de alucinações comuns do Whisper
     */
    fun sanitizeWhisperText(input: String): String {
        if (input.isBlank()) return ""
        var text = input.trim()

        // 1. Remove marcadores entre colchetes/parênteses
        text = text.replace(Regex("""\[[\s\S]*?\]"""), "").trim()
        text = text.replace(Regex("""\((?:m[úu]sica|risos|aplausos|sil[êe]ncio|ru[íi]do|legendas?[^\)]*)\)""", RegexOption.IGNORE_CASE), "").trim()

        // 2. Filtra alucinações puras de silêncio
        val lower = text.lowercase().trim(' ', '\t', '\r', '\n', '.', ',', '!', '?', ':', ';', '"', '\'')
        val silenceHallucinations = setOf(
            "tchau obrigado", "tchau, obrigado", "e ai tchau obrigado", "e aí tchau obrigado", "e aí: tchau, obrigado",
            "obrigado por assistir", "muito obrigado por assistir", "obrigado por assistir a este vídeo",
            "inscreva-se no canal", "deixe seu like", "curta e compartilhe", "valeu tchau", "tchau tchau",
            "legendas pela comunidade amara.org", "amara.org", "subtitles by", "legendado por", "transcrito por"
        )

        if (silenceHallucinations.contains(lower)) {
            return ""
        }

        // 3. Remove créditos finais de legenda
        text = text.replace(Regex("""(?i)(?:legendas?\s*(?:por|de|feitas?\s*por)?|transcriç[ãa]o\s*(?:por|de)?|traduç[ãa]o\s*(?:por|de)?|editado\s*por|subtitles?\s*by|captioned\s*by)\s*[:\-–—]?\s*[A-ZÀ-Úa-zà-ú\s\.]+$"""), "").trim()

        // 4. Remove frases finais de vídeos/YouTube
        text = text.replace(Regex("""(?i)(?:obrigado por assistir|inscreva-se no canal|deixe seu like|curta e compartilhe|valeu e até a próxima|e aí: tchau,? obrigado|tchau,? obrigado)[\.!\s]*$"""), "").trim()

        return text
    }

    /**
     * Limpa o texto final da LLM e aplica o guardrail anti-resposta
     */
    private fun cleanOutput(input: String, rawInput: String): String {
        if (input.isBlank()) return rawInput
        var text = input.trim()

        // 1. Remove blocos <think>...</think> de modelos de raciocínio
        val thinkEnd = text.indexOf("</think>", ignoreCase = true)
        if (thinkEnd >= 0) {
            text = text.substring(thinkEnd + 8).trim()
        } else if (text.startsWith("<think>", ignoreCase = true)) {
            val lastBreak = text.lastIndexOf("\n\n")
            if (lastBreak >= 0 && lastBreak + 2 < text.length) {
                text = text.substring(lastBreak + 2).trim()
            }
        }

        // 2. Remove aspas externas (inclusive o delimitador """ usado na mensagem do usuário)
        if (text.startsWith("\"\"\"") && text.endsWith("\"\"\"") && text.length > 6) {
            text = text.substring(3, text.length - 3).trim()
        }
        if (text.startsWith("\"") && text.endsWith("\"") && text.length > 1) {
            text = text.substring(1, text.length - 1).trim()
        }

        // 3. Remove blocos markdown de código
        if (text.startsWith("```") && text.endsWith("```")) {
            val firstBreak = text.indexOf('\n')
            if (firstBreak > 0 && text.length > firstBreak + 3) {
                text = text.substring(firstBreak + 1, text.length - 3).trim()
            }
        }

        // 4. Remove créditos e encerramentos
        text = text.replace(Regex("""(?i)(?:legendas?\s*(?:por|de|feitas?\s*por)?|transcriç[ãa]o\s*(?:por|de)?|traduç[ãa]o\s*(?:por|de)?|editado\s*por|subtitles?\s*by|captioned\s*by)\s*[:\-–—]?\s*[A-ZÀ-Úa-zà-ú\s\.]+$"""), "").trim()
        text = text.replace(Regex("""(?i)(?:obrigado por assistir|inscreva-se no canal|deixe seu like|curta e compartilhe|valeu e até a próxima|e aí: tchau,? obrigado|tchau,? obrigado)[\.!\s]*$"""), "").trim()

        // 5. Guardrail Anti-Resposta: Se a fala original era uma pergunta, a IA NÃO pode responder como chatbot
        if (rawInput.isNotBlank()) {
            val rawTrimmed = rawInput.trim()
            if (isInterrogativeSentence(rawTrimmed)) {
                val rawLower = rawTrimmed.lowercase()
                val textLower = text.lowercase()

                val answeredWithStatement = !text.contains("?")
                val soundsLikeChatbot = isConversationalAnswer(text)
                val expandedShortQuestion = rawTrimmed.length < 60 && text.length > rawTrimmed.length * 2.2
                val droppedQuestionWord = coreQuestionWords.any {
                    rawLower.startsWith("$it ") && !textLower.startsWith("$it ")
                }

                if (answeredWithStatement || soundsLikeChatbot || expandedShortQuestion || droppedQuestionWord) {
                    Log.w("GroqClient", "LLM tentou responder a pergunta em vez de transcrever. Aplicando fallback seguro.")
                    return formatSafeQuestion(rawTrimmed)
                }
            }
        }

        return if (text.isNotBlank()) text else rawInput
    }

    private val coreQuestionWords = listOf(
        "qual", "quais", "quem", "como", "onde", "quando", "quanto", "quantos", "por que"
    )

    // Só palavras que quase sempre iniciam pergunta: "pode", "preciso", "como", "quando" etc.
    // também iniciam afirmações ("Quando chegar, me liga."), e nesses casos o Whisper já põe "?".
    private val unambiguousQuestionStarters = listOf(
        "qual", "quais", "quem", "de quem", "aonde", "para onde", "a que horas", "que horas",
        "por que", "por quê", "quantos", "quantas", "será que", "o que", "você pode", "você sabe",
        "você acha", "você viu", "é verdade que", "seria possível", "tem como", "dá para", "dá pra",
        "sabe se"
    )

    private fun isInterrogativeSentence(text: String): Boolean {
        if (text.isBlank()) return false
        if (text.contains("?")) return true

        val lower = text.lowercase()
        return unambiguousQuestionStarters.any { lower.startsWith("$it ") || lower == it }
    }

    private fun isConversationalAnswer(text: String): Boolean {
        if (text.isBlank()) return false
        val lower = text.lowercase()
        val answerStarters = listOf(
            "sim,", "não,", "certamente", "com certeza", "como um modelo", "como uma inteligência",
            "a capital de", "a capital da", "o valor de", "para fazer", "você deve", "o resultado é",
            "a resposta é", "olá!", "olá,", "aqui está", "de acordo com", "infelizmente,", "atualmente,",
            "para saber", "o horário de", "o telefone de", "o endereço de", "claro!", "com prazer"
        )
        if (answerStarters.any { lower.startsWith(it) }) return true

        val conversationalPhrases = listOf(
            "como um modelo de linguagem", "como uma inteligência artificial",
            "não tenho acesso a", "posso ajudar em mais", "posso te ajudar",
            "espero ter ajudado", "se precisar de mais", "caso precise de",
            "estou aqui para ajudar", "qualquer dúvida", "deseja mais informações",
            "precisa de algo mais", "posso esclarecer", "a resposta para sua pergunta"
        )
        return conversationalPhrases.any { lower.contains(it) }
    }

    private fun formatSafeQuestion(raw: String): String {
        if (raw.isBlank()) return raw
        var clean = raw.trim()
        if (clean.isNotEmpty()) {
            clean = clean.replaceFirstChar { it.uppercase() }
        }
        if (!clean.contains("?")) {
            clean = clean.trimEnd('.', '!', ';', ',') + "?"
        }
        return clean
    }
}