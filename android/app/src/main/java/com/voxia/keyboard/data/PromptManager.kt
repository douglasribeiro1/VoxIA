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
            // As regras contra responder perguntas e inventar conteúdo ficam no GroqClient (valem
            // para todos os modos). Aqui fica só o que muda de um modo para o outro.
            promptText = "MODO PARÁGRAFOS:\n- Corrija ortografia, acentuação, letras maiúsculas e pontuação.\n- Separe o texto em parágrafos quando houver mudança de assunto, com uma linha em branco entre eles. Um ditado curto pode ficar num parágrafo só.\n- Mantenha todas as palavras ditadas, na mesma ordem. A única troca permitida é corrigir palavras que o reconhecimento de voz trocou por outras de som parecido.\n- Não resuma, não reescreva as frases, não crie títulos nem listas e não acrescente nada.",
            isBuiltIn = true
        ),
        PromptProfile(
            id = "default_single_line",
            title = "Texto Contínuo / Linha Única",
            promptText = "MODO LINHA ÚNICA:\n- Corrija ortografia, acentuação, letras maiúsculas e pontuação.\n- Todo o texto fica em UMA ÚNICA LINHA contínua: nenhuma quebra de linha, nenhum parágrafo, nenhuma lista.\n- Mantenha todas as palavras ditadas, na mesma ordem. A única troca permitida é corrigir palavras que o reconhecimento de voz trocou por outras de som parecido.\n- Não resuma, não reescreva as frases e não acrescente nada.",
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
            if (loaded.isEmpty()) defaultPrompts else {
                // Os perfis prontos ficam salvos junto com os da pessoa; usa sempre o texto atual deles,
                // para uma versão nova do app corrigir as instruções sem a pessoa precisar fazer nada.
                val builtIns = defaultPrompts.associateBy { it.id }
                loaded.map { p -> builtIns[p.id]?.takeIf { p.isBuiltIn }?.let { p.copy(title = it.title, promptText = it.promptText) } ?: p }
            }
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
