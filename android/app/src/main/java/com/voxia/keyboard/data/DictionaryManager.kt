package com.voxia.keyboard.data

import android.content.Context
import android.content.SharedPreferences
import com.google.gson.Gson
import com.google.gson.reflect.TypeToken
import java.util.UUID
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

    private val defaultEntries = listOf(
        DictionaryEntry(
            id = "dict_sarah",
            original = "hospital Sara",
            replacement = "hospital Sarah",
            isVocabularyOnly = false,
            createdAt = "2026-08-31"
        ),
        DictionaryEntry(
            id = "dict_sarah_single",
            original = "Sara",
            replacement = "Sarah",
            isVocabularyOnly = false,
            createdAt = "2026-08-31"
        ),
        DictionaryEntry(
            id = "dict_voxia",
            original = "Vox IA",
            replacement = "VoxIA",
            isVocabularyOnly = false,
            createdAt = "2026-08-31"
        ),
        DictionaryEntry(
            id = "dict_disartria",
            original = "desartria",
            replacement = "disartria",
            isVocabularyOnly = false,
            createdAt = "2026-08-31"
        )
    )

    fun getEntries(): List<DictionaryEntry> {
        val json = prefs.getString("dictionary_entries_json", null)
        if (json.isNullOrBlank()) {
            saveEntries(defaultEntries)
            return defaultEntries
        }
        return try {
            val type = object : TypeToken<List<DictionaryEntry>>() {}.type
            val loaded: List<DictionaryEntry> = gson.fromJson(json, type)
            if (loaded.isEmpty()) defaultEntries else loaded
        } catch (e: Exception) {
            defaultEntries
        }
    }

    fun saveEntries(entries: List<DictionaryEntry>) {
        val json = gson.toJson(entries)
        prefs.edit().putString("dictionary_entries_json", json).apply()
    }

    fun addOrUpdateEntry(
        id: String?,
        original: String,
        replacement: String,
        isVocabularyOnly: Boolean
    ): DictionaryEntry {
        val all = getEntries().toMutableList()
        val origClean = original.trim()
        val replClean = replacement.trim()

        val existingIndex = if (id != null) {
            all.indexOfFirst { it.id == id }
        } else {
            all.indexOfFirst { it.original.equals(origClean, ignoreCase = true) }
        }

        val entry = DictionaryEntry(
            id = if (existingIndex >= 0) all[existingIndex].id else (id ?: ("dict_" + UUID.randomUUID().toString().replace("-", "").take(12))),
            original = origClean,
            replacement = replClean,
            isVocabularyOnly = isVocabularyOnly,
            createdAt = System.currentTimeMillis().toString()
        )

        if (existingIndex >= 0) {
            all[existingIndex] = entry
        } else {
            all.add(0, entry) // adiciona no início
        }

        saveEntries(all)
        return entry
    }

    fun deleteEntry(id: String): Boolean {
        val all = getEntries().toMutableList()
        val removed = all.removeAll { it.id == id }
        if (removed) {
            saveEntries(all)
        }
        return removed
    }

    /**
     * Aplica as substituições do dicionário com Case-Matching inteligente
     * Ordena por tamanho decrescente para substituir frases compostas antes de palavras isoladas
     */
    fun applyReplacements(inputText: String): String {
        var text = inputText
        val entries = getEntries()
            .filter { !it.isVocabularyOnly && it.original.isNotBlank() }
            .sortedByDescending { it.original.length }

        for (entry in entries) {
            val orig = entry.original.trim()
            val repl = entry.replacement.trim()
            if (orig.isEmpty() || repl.isEmpty()) continue

            // Regex com boundary de palavra
            val regexPattern = "\\b" + Pattern.quote(orig) + "\\b"
            val pattern = Pattern.compile(regexPattern, Pattern.CASE_INSENSITIVE or Pattern.UNICODE_CASE)
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