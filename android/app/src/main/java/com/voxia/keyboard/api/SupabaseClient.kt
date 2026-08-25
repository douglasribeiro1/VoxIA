package com.voxia.keyboard.api

import android.content.Context
import android.content.SharedPreferences
import android.util.Log
import com.google.gson.Gson
import com.google.gson.JsonArray
import com.google.gson.JsonObject
import com.voxia.keyboard.data.DictionaryEntry
import com.voxia.keyboard.data.DictionaryManager
import com.voxia.keyboard.data.PromptManager
import com.voxia.keyboard.data.PromptProfile
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.MediaType.Companion.toMediaTypeOrNull
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import java.util.concurrent.TimeUnit

class SupabaseClient(private val context: Context) {
    companion object {
        const val SUPABASE_URL = "https://hbtbykhhkrtbfnznnxqa.supabase.co"
        const val SUPABASE_KEY = "sb_publishable_1P6EmICwhLs8MmVKMOBdsA_pbjH2WBI"
    }

    private val prefs: SharedPreferences = context.getSharedPreferences("voxia_auth", Context.MODE_PRIVATE)
    private val client = OkHttpClient.Builder()
        .connectTimeout(20, TimeUnit.SECONDS)
        .readTimeout(20, TimeUnit.SECONDS)
        .build()

    private val gson = Gson()

    fun getAccessToken(): String? = prefs.getString("access_token", null)
    fun getUserEmail(): String? = prefs.getString("user_email", null)
    fun getUserId(): String? = prefs.getString("user_id", null)
    fun isLoggedIn(): Boolean = !getAccessToken().isNullOrBlank()

    fun logout() {
        prefs.edit().clear().apply()
    }

    /**
     * Autentica com e-mail e senha no Supabase Auth
     */
    suspend fun login(email: String, password: String): Result<String> = withContext(Dispatchers.IO) {
        try {
            val jsonBody = JsonObject().apply {
                addProperty("email", email.trim())
                addProperty("password", password)
            }

            val request = Request.Builder()
                .url("$SUPABASE_URL/auth/v1/token?grant_type=password")
                .addHeader("apikey", SUPABASE_KEY)
                .addHeader("Content-Type", "application/json")
                .post(jsonBody.toString().toRequestBody("application/json".toMediaTypeOrNull()))
                .build()

            val response = client.newCall(request).execute()
            val bodyString = response.body?.string() ?: ""

            if (!response.isSuccessful) {
                Log.e("SupabaseClient", "Login error ${response.code}: $bodyString")
                return@withContext Result.failure(Exception("Erro no Login (${response.code}): $bodyString"))
            }

            val json = gson.fromJson(bodyString, JsonObject::class.java)
            val accessToken = json.get("access_token")?.asString ?: ""
            val refreshToken = json.get("refresh_token")?.asString ?: ""
            val userObj = json.getAsJsonObject("user")
            val userId = userObj?.get("id")?.asString ?: ""

            prefs.edit()
                .putString("access_token", accessToken)
                .putString("refresh_token", refreshToken)
                .putString("user_email", email.trim())
                .putString("user_id", userId)
                .apply()

            Result.success(accessToken)
        } catch (e: Exception) {
            Log.e("SupabaseClient", "Falha de rede no login", e)
            Result.failure(e)
        }
    }

    /**
     * Sincroniza Prompts e Dicionário do Supabase para o armazenamento local
     */
    suspend fun syncFromCloud(): Result<Pair<Int, Int>> = withContext(Dispatchers.IO) {
        val token = getAccessToken()
        if (token.isNullOrBlank()) {
            return@withContext Result.failure(Exception("Usuário não autenticado"))
        }

        try {
            var promptCount = 0
            var dictCount = 0

            // 1. Sincronizar Prompts
            val promptRequest = Request.Builder()
                .url("$SUPABASE_URL/rest/v1/prompts?select=*")
                .addHeader("apikey", SUPABASE_KEY)
                .addHeader("Authorization", "Bearer $token")
                .get()
                .build()

            val promptResp = client.newCall(promptRequest).execute()
            if (promptResp.isSuccessful) {
                val promptBody = promptResp.body?.string() ?: "[]"
                val jsonArray = gson.fromJson(promptBody, JsonArray::class.java)
                if (jsonArray != null && jsonArray.size() > 0) {
                    val promptList = mutableListOf<PromptProfile>()
                    for (element in jsonArray) {
                        val obj = element.asJsonObject
                        val id = obj.get("id")?.asString ?: ""
                        val title = obj.get("title")?.asString ?: "Prompt"
                        val text = obj.get("prompt_text")?.asString ?: ""
                        val isBuiltIn = obj.get("is_builtin")?.asBoolean ?: false
                        val updated = obj.get("updated_at")?.asString ?: ""
                        promptList.add(PromptProfile(id, title, text, isBuiltIn, updated))
                    }
                    val promptMgr = PromptManager(context)
                    promptMgr.savePrompts(promptList)
                    promptCount = promptList.size
                }
            }

            // 2. Sincronizar Dicionário
            val dictRequest = Request.Builder()
                .url("$SUPABASE_URL/rest/v1/dictionary?select=*")
                .addHeader("apikey", SUPABASE_KEY)
                .addHeader("Authorization", "Bearer $token")
                .get()
                .build()

            val dictResp = client.newCall(dictRequest).execute()
            if (dictResp.isSuccessful) {
                val dictBody = dictResp.body?.string() ?: "[]"
                val jsonArray = gson.fromJson(dictBody, JsonArray::class.java)
                if (jsonArray != null && jsonArray.size() > 0) {
                    val dictList = mutableListOf<DictionaryEntry>()
                    for (element in jsonArray) {
                        val obj = element.asJsonObject
                        val id = obj.get("id")?.asString ?: ""
                        val orig = obj.get("original")?.asString ?: ""
                        val repl = obj.get("replacement")?.asString ?: ""
                        val vocabOnly = obj.get("is_vocabulary_only")?.asBoolean ?: false
                        val created = obj.get("created_at")?.asString ?: ""
                        dictList.add(DictionaryEntry(id, orig, repl, vocabOnly, created))
                    }
                    val dictMgr = DictionaryManager(context)
                    dictMgr.saveEntries(dictList)
                    dictCount = dictList.size
                }
            }

            Result.success(Pair(promptCount, dictCount))
        } catch (e: Exception) {
            Log.e("SupabaseClient", "Falha na sincronização", e)
            Result.failure(e)
        }
    }
}
