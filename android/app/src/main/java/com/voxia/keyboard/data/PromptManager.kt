package com.voxia.keyboard.data

import android.content.Context
import android.content.SharedPreferences
import com.google.gson.Gson
import com.google.gson.reflect.TypeToken

data class PromptProfile(
    val id: String,
    val title: String,
    val promptText: String,
    val isBuiltIn: Boolean = false,
    val updatedAt: String = ""
) {
    override fun toString(): String = title
}

class PromptManager(context: Context) {
    private val prefs: SharedPreferences = context.getSharedPreferences("voxia_prompts", Context.MODE_PRIVATE)
    private val gson = Gson()

    private val defaultPrompts = listOf(
        PromptProfile(
            id = "preset_default",
            title = "Padrão (Ortografia & Pontuação)",
            promptText = "Você é um assistente de correção e pontuação de texto ditado em português do Brasil. Sua tarefa é formatar o texto corrigindo pontuação, acentuação e pequenos deslizes de fala, preservando fielmente todas as palavras, termos técnicos e o sentido original.",
            isBuiltIn = true
        ),
        PromptProfile(
            id = "preset_strict",
            title = "Pontuação Rígida & Fluência",
            promptText = "Você é um corretor textual rigoroso. Ajuste pontuação, remova vícios de linguagem e hesitações (como 'ééé', 'tipo assim') mantendo o vocabulário original intacto.",
            isBuiltIn = true
        ),
        PromptProfile(
            id = "preset_medical",
            title = "Termos Médicos & Prontuário",
            promptText = "Você é um especialista em transcrição de laudos e prontuários médicos. Corrija termos anatômicos, nomes de medicamentos, dosagens e jargões clínicos com precisão cirúrgica sem alterar a estrutura do laudo.",
            isBuiltIn = true
        ),
        PromptProfile(
            id = "preset_direct",
            title = "Sem Alteração (Texto Bruto)",
            promptText = "",
            isBuiltIn = true
        )
    )

    fun getPrompts(): List<PromptProfile> {
        val json = prefs.getString("prompt_profiles_json", null)
        if (json.isNullOrBlank()) {
            return defaultPrompts
        }
        return try {
            val type = object : TypeToken<List<PromptProfile>>() {}.type
            val loaded: List<PromptProfile> = gson.fromJson(json, type)
            if (loaded.isEmpty()) defaultPrompts else loaded
        } catch (e: Exception) {
            defaultPrompts
        }
    }

    fun savePrompts(profiles: List<PromptProfile>) {
        val json = gson.toJson(profiles)
        prefs.edit().putString("prompt_profiles_json", json).apply()
    }

    fun getActivePromptId(): String {
        return prefs.getString("active_prompt_id", "preset_default") ?: "preset_default"
    }

    fun setActivePromptId(id: String) {
        prefs.edit().putString("active_prompt_id", id).apply()
    }

    fun getActivePrompt(): PromptProfile {
        val id = getActivePromptId()
        val all = getPrompts()
        return all.find { it.id == id } ?: all.firstOrNull() ?: defaultPrompts[0]
    }
}
