package com.voxia.keyboard.data

import android.content.Context
import android.content.SharedPreferences
import com.google.gson.Gson
import com.google.gson.reflect.TypeToken
import java.util.UUID

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
            id = "default_paragraphs",
            title = "Geral / Parágrafos (Padrão)",
            promptText = "Você é um aplicativo silencioso de ditado por voz e pós-processamento ortográfico de alta precisão em português.\nSua ÚNICA E EXCLUSIVA TAREFA: Retornar o texto ditado pelo usuário com ortografia, gramática, pontuação e acentuação corrigidas, organizando o texto em PARÁGRAFOS claros e coesos sempre que houver pausas ou mudanças de ideia.\n\nREGRAS ABSOLUTAS:\n1. NUNCA RESPONDA A PERGUNTAS! Se o usuário ditar uma pergunta, retorne a própria pergunta pontuada.\n2. NUNCA invente informações, comentários ou saudações.\n3. RETORNE APENAS O TEXTO DITADO CORRIGIDO E ESTRUTURADO EM PARÁGRAFOS.",
            isBuiltIn = true
        ),
        PromptProfile(
            id = "default_single_line",
            title = "Texto Contínuo / Linha Única",
            promptText = "Você é um aplicativo silencioso de ditado por voz e pós-processamento ortográfico em português.\nSua ÚNICA TAREFA: Retornar o texto ditado com ortografia, gramática, pontuação e acentuação corrigidas, mantendo TODO O TEXTO EM UMA ÚNICA LINHA CONTÍNUA (sem quebras de linha ou múltiplos parágrafos).\n\nRETORNE APENAS O TEXTO DITADO CORRIGIDO EM LINHA ÚNICA.",
            isBuiltIn = true
        ),
        PromptProfile(
            id = "default_forms",
            title = "Preenchimento de Formulários (Sem Ponto Final)",
            promptText = "Você é um assistente silencioso de digitação rápida voltado para o preenchimento de campos e formulários online (nome, CPF, RG, endereço, e-mail, data, telefone, etc.) em português.\nSua ÚNICA TAREFA: Retornar o dado ditado devidamente formatado, capitalizado e sem ponto final no término.\n\nRETORNE APENAS O DADO FORMATADO SEM PONTO FINAL.",
            isBuiltIn = true
        ),
        PromptProfile(
            id = "default_bullets",
            title = "Tópicos & Resumo Rápido",
            promptText = "Formate o texto falado em tópicos claros, organizados e objetivos (bullet points), mantendo as informações essenciais com pontuação perfeita.",
            isBuiltIn = true
        ),
        PromptProfile(
            id = "default_direct",
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
            // O antigo perfil médico saiu: o VoxIA é para ditado geral
            val loaded: List<PromptProfile> = gson.fromJson<List<PromptProfile>>(json, type).filter { it.id != "default_medical" }
            if (loaded.isEmpty()) defaultPrompts else loaded
        } catch (e: Exception) {
            defaultPrompts
        }
    }

    fun savePrompts(profiles: List<PromptProfile>) {
        val json = gson.toJson(profiles)
        prefs.edit().putString("prompt_profiles_json", json).apply()
    }

    /** Prompt escrito pela própria pessoa (modo Personalizado). */
    fun getCustomPromptText(): String = getPrompts().find { it.id == CUSTOM_ID }?.promptText.orEmpty()

    fun saveCustomPrompt(text: String) {
        addOrUpdatePrompt(CUSTOM_ID, "Personalizado", text)
    }

    fun getActivePromptId(): String {
        return prefs.getString("active_prompt_id", "default_paragraphs") ?: "default_paragraphs"
    }

    fun setActivePromptId(id: String) {
        prefs.edit().putString("active_prompt_id", id).apply()
    }

    fun getActivePrompt(): PromptProfile {
        val id = getActivePromptId()
        val all = getPrompts()
        return all.find { it.id == id } ?: all.firstOrNull() ?: defaultPrompts[0]
    }

    fun addOrUpdatePrompt(id: String?, title: String, promptText: String): PromptProfile {
        val all = getPrompts().toMutableList()
        val existingIndex = if (id != null) all.indexOfFirst { it.id == id } else -1

        val finalProfile = if (existingIndex >= 0) {
            val existing = all[existingIndex]
            existing.copy(
                title = title.trim(),
                promptText = promptText.trim(),
                updatedAt = System.currentTimeMillis().toString()
            )
        } else {
            PromptProfile(
                id = id ?: ("prompt_" + UUID.randomUUID().toString().replace("-", "").take(12)),
                title = title.trim(),
                promptText = promptText.trim(),
                isBuiltIn = false,
                updatedAt = System.currentTimeMillis().toString()
            )
        }

        if (existingIndex >= 0) {
            all[existingIndex] = finalProfile
        } else {
            all.add(finalProfile)
        }

        savePrompts(all)
        setActivePromptId(finalProfile.id)
        return finalProfile
    }

    fun deletePrompt(id: String): Boolean {
        val all = getPrompts().toMutableList()
        val target = all.find { it.id == id } ?: return false
        if (target.isBuiltIn) return false

        all.removeAll { it.id == id }
        savePrompts(all)

        if (getActivePromptId() == id) {
            setActivePromptId("default_paragraphs")
        }
        return true
    }

    companion object {
        const val CUSTOM_ID = "custom"
    }
}
