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
import okhttp3.RequestBody.Companion.asRequestBody
import okhttp3.RequestBody.Companion.toRequestBody
import java.io.File
import java.io.IOException
import java.util.concurrent.TimeUnit

class GroqClient {
    private val client = OkHttpClient.Builder()
        .connectTimeout(25, TimeUnit.SECONDS)
        .readTimeout(45, TimeUnit.SECONDS)
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
""".trimIndent()

    /**
     * Transcreve o arquivo de áudio usando Groq Whisper com anti-alucinação e retentativas de rede
     */
    suspend fun transcribeAudio(
        audioFile: File,
        apiKey: String,
        model: String = "whisper-large-v3",
        initialPrompt: String = "",
        maxRetries: Int = 3
    ): Result<String> = withContext(Dispatchers.IO) {
        val trimmedKey = apiKey.trim()
        if (trimmedKey.isBlank()) {
            return@withContext Result.failure(Exception("Chave da Groq API não configurada."))
        }

        if (!audioFile.exists() || audioFile.length() < 100) {
            return@withContext Result.failure(Exception("Arquivo de áudio inválido ou vazio."))
        }

        val basePrompt = if (initialPrompt.isNotBlank()) {
            "Ditado por voz em português brasileiro sem legendas. Vocabulário: $initialPrompt"
        } else {
            "Ditado por voz em português brasileiro sem legendas."
        }

        var lastException: Exception = Exception("Erro desconhecido de transcrição")

        for (attempt in 1..maxRetries) {
            try {
                Log.d("GroqClient", "Tentativa $attempt de transcrição Whisper (${audioFile.name})...")

                val requestBodyBuilder = MultipartBody.Builder()
                    .setType(MultipartBody.FORM)
                    .addFormDataPart(
                        "file",
                        audioFile.name,
                        audioFile.asRequestBody("audio/m4a".toMediaTypeOrNull())
                    )
                    .addFormDataPart("model", model)
                    .addFormDataPart("language", "pt")
                    .addFormDataPart("temperature", "0.0")
                    .addFormDataPart("prompt", basePrompt)
                    .addFormDataPart("response_format", "json")

                val request = Request.Builder()
                    .url("https://api.groq.com/openai/v1/audio/transcriptions")
                    .addHeader("Authorization", "Bearer $trimmedKey")
                    .post(requestBodyBuilder.build())
                    .build()

                val response = client.newCall(request).execute()
                val bodyString = response.body?.string() ?: ""

                if (!response.isSuccessful) {
                    val errorDetail = try {
                        val errJson = gson.fromJson(bodyString, JsonObject::class.java)
                        errJson.getAsJsonObject("error")?.get("message")?.asString ?: bodyString
                    } catch (e: Exception) {
                        bodyString
                    }

                    // Se for erro de cliente (401, 400), não adianta tentar novamente
                    if (response.code in 400..499 && response.code != 429) {
                        Log.e("GroqClient", "Whisper HTTP Client Error ${response.code}: $errorDetail")
                        return@withContext Result.failure(Exception("Erro Whisper (${response.code}): $errorDetail"))
                    }

                    // Se for 429 ou 5xx (servidor ocupado/falha), retenta se houver tentativas
                    lastException = Exception("Erro Whisper (${response.code}): $errorDetail")
                    if (attempt < maxRetries) {
                        delay(attempt * 1000L)
                        continue
                    }
                    return@withContext Result.failure(lastException)
                }

                val json = gson.fromJson(bodyString, JsonObject::class.java)
                val rawText = json.get("text")?.asString ?: ""
                val sanitized = sanitizeWhisperText(rawText)
                val resultText = if (sanitized.isNotBlank()) sanitized else rawText.trim()
                return@withContext Result.success(resultText)

            } catch (e: IOException) {
                Log.w("GroqClient", "Oscilação de rede na tentativa $attempt: ${e.message}")
                lastException = Exception("Falha de conexão com a internet. O áudio foi preservado para reenvio.")
                if (attempt < maxRetries) {
                    delay(attempt * 1200L)
                }
            } catch (e: Exception) {
                Log.e("GroqClient", "Exceção inesperada no Whisper", e)
                lastException = e
                break
            }
        }

        Result.failure(lastException)
    }

    /**
     * Corrige e formata a transcrição usando Groq LLM com Guardrails anti-resposta
     */
    suspend fun correctText(
        rawText: String,
        apiKey: String,
        systemPrompt: String,
        model: String = "openai/gpt-oss-120b"
    ): Result<String> = withContext(Dispatchers.IO) {
        if (rawText.isBlank()) {
            return@withContext Result.success("")
        }

        val trimmedKey = apiKey.trim()
        if (systemPrompt.isBlank() || trimmedKey.isBlank()) {
            return@withContext Result.success(rawText)
        }

        try {
            val enforcedSystemPrompt = "$strictAntiResponseDirective\n\nINSTRUÇÕES DO PERFIL:\n$systemPrompt"
            val maxTokens = Math.max(1024, (rawText.length * 4) + 512)
            val userMessage = "TEXTO DITADO PELO USUÁRIO (APENAS TRANSCREVER E PONTUAR, NUNCA RESPONDER):\n\"\"\"\n$rawText\n\"\"\"\n\n" +
                "LEMBRETE OBRIGATÓRIO: Retorne apenas o texto ditado acima com pontuação e ortografia ajustadas. " +
                "Se for uma pergunta, devolva a pergunta pontuada com interrogação sem responder."

            val jsonBody = JsonObject().apply {
                addProperty("model", model)
                addProperty("temperature", 0.0)
                addProperty("max_tokens", maxTokens)

                val messages = com.google.gson.JsonArray().apply {
                    add(JsonObject().apply {
                        addProperty("role", "system")
                        addProperty("content", enforcedSystemPrompt)
                    })
                    add(JsonObject().apply {
                        addProperty("role", "user")
                        addProperty("content", userMessage)
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
                Log.w("GroqClient", "LLM Correction HTTP Error ${response.code}: $bodyString. Usando texto bruto.")
                return@withContext Result.success(rawText)
            }

            val json = gson.fromJson(bodyString, JsonObject::class.java)
            val choices = json.getAsJsonArray("choices")
            if (choices != null && choices.size() > 0) {
                val message = choices[0].asJsonObject.getAsJsonObject("message")
                val content = message?.get("content")?.asString ?: rawText
                val cleaned = cleanOutput(content, rawText)
                val finalText = if (cleaned.isNotBlank()) cleaned else rawText
                Result.success(finalText)
            } else {
                Result.success(rawText)
            }
        } catch (e: Exception) {
            Log.e("GroqClient", "Falha na correção de texto, usando fallback para transcrição bruta", e)
            Result.success(rawText)
        }
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