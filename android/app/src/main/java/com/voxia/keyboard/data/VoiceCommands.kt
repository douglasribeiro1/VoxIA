package com.voxia.keyboard.data

/**
 * Comando falado "apaga isso": descarta a frase dita logo antes dele (desde o último ponto,
 * interrogação, exclamação, quebra de linha ou título), junto com o próprio comando.
 *
 *   "Febre de 39 graus. Apaga isso. Febre de 38 graus." → "Febre de 38 graus."
 *
 * É feito pelo aplicativo, antes da IA, para o resultado ser sempre previsível. Funciona só
 * dentro do ditado atual (não apaga o que já estava escrito no campo). Mesma lógica do Windows.
 */
object VoiceCommands {
    private val deleteCommand = Regex(
        """(?<![\p{L}\p{N}])(?:apag(?:a|ue|ar)|risc(?:a|ar|que))\s+(?:isso|isto|essa frase|esta frase|a [úuÚU]ltima frase)(?![\p{L}\p{N}])[\s.,;:!?…]*""",
        setOf(RegexOption.IGNORE_CASE)
    )
    private val trailingPunctuation = Regex("""[\s.,;:!?…\-–—]+$""")

    fun hasDeleteCommand(text: String): Boolean = deleteCommand.containsMatchIn(text)

    fun applyDeletes(text: String): String {
        var result = text
        var guard = 0
        while (guard++ < 50) {
            val m = deleteCommand.find(result) ?: break
            val before = result.substring(0, m.range.first).replace(trailingPunctuation, "")
            val boundary = lastBoundary(before)
            val kept = if (boundary < 0) "" else before.substring(0, boundary + 1).trimEnd(' ', '\t')
            val rest = result.substring(m.range.last + 1).trimStart(' ', '\t')
            result = when {
                kept.isEmpty() -> rest
                rest.isEmpty() -> kept
                kept.endsWith("\n") -> kept + rest
                else -> "$kept $rest"
            }
        }
        return result.trim()
    }

    /** Índice do último fim de frase (., !, ?, quebra de linha ou fim de marcador de título "]]"). */
    private fun lastBoundary(s: String): Int {
        for (i in s.length - 1 downTo 0) {
            val c = s[i]
            if (c == '.' || c == '!' || c == '?' || c == '\n') return i
            if (c == ']' && i > 0 && s[i - 1] == ']') return i
        }
        return -1
    }
}
