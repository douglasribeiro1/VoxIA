package com.voxia.keyboard.api

import android.util.Log
import com.google.gson.Gson
import com.google.gson.JsonObject
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.MediaType.Companion.toMediaTypeOrNull
import okhttp3.MultipartBody
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.asRequestBody
import okhttp3.RequestBody.Companion.toRequestBody
import java.io.File
import java.util.concurrent.TimeUnit

class GroqClient {
    private val client = OkHttpClient.Builder()
        .connectTimeout(30, TimeUnit.SECONDS)
        .readTimeout(60, TimeUnit.SECONDS)
        .writeTimeout(60, TimeUnit.SECONDS)
        .build()

    private val gson = Gson()

    /**
     * Transcreve o arquivo de áudio usando Groq Whisper
     */
    suspend fun transcribeAudio(
        audioFile: File,
        apiKey: String,
        model: String = "whisper-large-v3-turbo",
        initialPrompt: String = ""
    ): Result<String> = withContext(Dispatchers.IO) {
        try {
            val requestBodyBuilder = MultipartBody.Builder()
                .setType(MultipartBody.FORM)
                .addFormDataPart(
                    "file",
                    audioFile.name,
                    audioFile.asRequestBody("audio/m4a".toMediaTypeOrNull())
                )
                .addFormDataPart("model", model)
                .addFormDataPart("language", "pt")
                .addFormDataPart("response_format", "json")

            if (initialPrompt.isNotBlank()) {
                requestBodyBuilder.addFormDataPart("prompt", initialPrompt)
            }

            val request = Request.Builder()
                .url("https://api.groq.com/openai/v1/audio/transcriptions")
                .addHeader("Authorization", "Bearer $apiKey")
                .post(requestBodyBuilder.build())
                .build()

            val response = client.newCall(request).execute()
            val bodyString = response.body?.string() ?: ""

            if (!response.isSuccessful) {
                Log.e("GroqClient", "Whisper HTTP Error ${response.code}: $bodyString")
                return@withContext Result.failure(Exception("Erro Whisper (${response.code}): $bodyString"))
            }

            val json = gson.fromJson(bodyString, JsonObject::class.java)
            val text = json.get("text")?.asString ?: ""
            Result.success(text.trim())
        } catch (e: Exception) {
            Log.e("GroqClient", "Falha na transcrição", e)
            Result.failure(e)
        }
    }

    /**
     * Corrige e formata a transcrição usando Groq Llama / GPT OSS
     */
    suspend fun correctText(
        rawText: String,
        apiKey: String,
        systemPrompt: String,
        model: String = "llama-3.3-70b-versatile"
    ): Result<String> = withContext(Dispatchers.IO) {
        if (systemPrompt.isBlank()) {
            return@withContext Result.success(rawText)
        }

        try {
            val enforcedSystemPrompt = "$systemPrompt\n\nInstrução estrita: Retorne APENAS o texto formatado final. Não inclua saudações, explicações, comentários adicionais nem aspas envolvendo o texto todo."

            val jsonBody = JsonObject().apply {
                addProperty("model", model)
                addProperty("temperature", 0.0)

                val messages = com.google.gson.JsonArray().apply {
                    add(JsonObject().apply {
                        addProperty("role", "system")
                        addProperty("content", enforcedSystemPrompt)
                    })
                    add(JsonObject().apply {
                        addProperty("role", "user")
                        addProperty("content", rawText)
                    })
                }
                add("messages", messages)
            }

            val request = Request.Builder()
                .url("https://api.groq.com/openai/v1/chat/completions")
                .addHeader("Authorization", "Bearer $apiKey")
                .addHeader("Content-Type", "application/json")
                .post(jsonBody.toString().toRequestBody("application/json".toMediaTypeOrNull()))
                .build()

            val response = client.newCall(request).execute()
            val bodyString = response.body?.string() ?: ""

            if (!response.isSuccessful) {
                Log.e("GroqClient", "LLM Correction HTTP Error ${response.code}: $bodyString")
                // Se falhar na correção, retorna ao menos a transcrição bruta
                return@withContext Result.success(rawText)
            }

            val json = gson.fromJson(bodyString, JsonObject::class.java)
            val choices = json.getAsJsonArray("choices")
            if (choices != null && choices.size() > 0) {
                val message = choices[0].asJsonObject.getAsJsonObject("message")
                val content = message?.get("content")?.asString ?: rawText
                Result.success(content.trim())
            } else {
                Result.success(rawText)
            }
        } catch (e: Exception) {
            Log.e("GroqClient", "Falha na correção de texto", e)
            Result.success(rawText)
        }
    }
}
