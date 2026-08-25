package com.voxia.keyboard.data

import android.content.Context
import android.content.SharedPreferences
import com.google.gson.Gson
import com.google.gson.reflect.TypeToken
import java.util.regex.Matcher
import java.util.regex.Pattern


data class DictionaryEntry(
    val id: String,
    val original: String,
    val replacement: String,
    val isVocabularyOnly: Boolean = false,
    val createdAt: String = ""
)

class DictionaryManager(context: Context) {
    private val prefs: SharedPreferences = context.getSharedPreferences("voxia_dictionary", Context.MODE_PRIVATE)
    private val gson = Gson()

    fun getEntries(): List<DictionaryEntry> {
        val json = prefs.getString("dictionary_entries_json", null)
        if (json.isNullOrBlank()) return emptyList()
        return try {
            val type = object : TypeToken<List<DictionaryEntry>>() {}.type
            gson.fromJson(json, type) ?: emptyList()
        } catch (e: Exception) {
            emptyList()
        }
    }

    fun saveEntries(entries: List<DictionaryEntry>) {
        val json = gson.toJson(entries)
        prefs.edit().putString("dictionary_entries_json", json).apply()
    }

    /**
     * Aplica as substituições do dicionário com Case-Matching inteligente
     * (Preserva MAIÚSCULAS, Primeira Maiúscula ou minúsculas)
     */
    fun applyReplacements(inputText: String): String {
        var text = inputText
        val entries = getEntries().filter { !it.isVocabularyOnly && it.original.isNotBlank() }

        for (entry in entries) {
            val orig = entry.original.trim()
            val repl = entry.replacement.trim()
            if (orig.isEmpty() || repl.isEmpty()) continue

            // Regex com limite de palavras para evitar substituições parciais indevidas
            val pattern = Pattern.compile("\\b" + Pattern.quote(orig) + "\\b", Pattern.CASE_INSENSITIVE or Pattern.UNICODE_CASE)
            val matcher = pattern.matcher(text)

            val sb = StringBuffer()
            while (matcher.find()) {
                val matchedText = matcher.group()
                val adjustedReplacement = matchCase(matchedText, repl)
                matcher.appendReplacement(sb, Matcher.quoteReplacement(adjustedReplacement))
            }

            matcher.appendTail(sb)
            text = sb.toString()
        }

        return text
    }

    /**
     * Retorna a lista de termos técnicos para alimentar o prompt inicial do Whisper
     */
    fun getWhisperPromptTerms(): String {
        val allTerms = getEntries().map {
            if (it.isVocabularyOnly || it.replacement.isBlank()) it.original else it.replacement
        }.filter { it.isNotBlank() }.distinct()

        return if (allTerms.isEmpty()) "" else allTerms.joinToString(", ")
    }

    private fun matchCase(source: String, target: String): String {
        if (source.isEmpty() || target.isEmpty()) return target
        return when {
            source.all { !it.isLetter() || it.isUpperCase() } -> target.uppercase()
            source.first().isUpperCase() -> target.replaceFirstChar { it.uppercase() }
            else -> target.lowercase()
        }
    }
}

