package com.voxia.keyboard.data

/**
 * Comando falado "apaga isso": descarta a frase dita logo antes dele (desde o último ponto,
 * interrogação, exclamação, quebra de linha ou título), junto com o próprio comando.
 *
 *   "Febre de 39 graus. Apaga isso. Febre de 38 graus." → "Febre de 38 graus."
 *
 * Travas de segurança (em ditados longos o Whisper às vezes escreve muito texto sem nenhum ponto,
 * e "até o último ponto" podia virar "o ditado inteiro"):
 *  - nunca apaga mais de [MAX_DELETE_WORDS] palavras;
 *  - se a frase anterior for maior que isso, apaga só até a última vírgula; se nem assim couber,
 *    não apaga nada (só some o próprio comando);
 *  - o comando só vale isolado, seguido de pausa (pontuação) ou no fim do ditado: "pediu para
 *    apagar isso do sistema" é texto normal.
 *
 * É feito pelo aplicativo, antes da IA, para o resultado ser sempre previsível. Funciona só
 * dentro do ditado atual (não apaga o que já estava escrito no campo). Mesma lógica do Windows.
 */
object VoiceCommands {
    const val MAX_DELETE_WORDS = 30

    private val deleteCommand = Regex(
        """(?<![\p{L}\p{N}])(?:apag(?:a|ue|ar)|risc(?:a|ar|que))\s+(?:isso|isto|essa frase|esta frase|a [úuÚU]ltima frase)(?=\s*(?:[.,;:!?…]|$))[\s.,;:!?…]*""",
        setOf(RegexOption.IGNORE_CASE)
    )
    private val trailingPunctuation = Regex("""[\s.,;:!?…\-–—]+$""")
    private val word = Regex("""[\p{L}\p{N}]+""")

    fun hasDeleteCommand(text: String): Boolean = deleteCommand.containsMatchIn(text)

    fun applyDeletes(text: String): String {
        var result = text
        var guard = 0
        while (guard++ < 50) {
            val m = deleteCommand.find(result) ?: break
            val before = result.substring(0, m.range.first).replace(trailingPunctuation, "")
            val cut = deletionStart(before)
            var kept = before.substring(0, cut).trimEnd(' ', '\t')
            val rest = result.substring(m.range.last + 1).trimStart(' ', '\t')
            if (rest.isEmpty()) kept = kept.trimEnd(',', ';', ':', ' ')
            result = when {
                kept.isEmpty() -> rest
                rest.isEmpty() -> kept
                kept.endsWith("\n") -> kept + rest
                else -> "$kept $rest"
            }
        }
        return result.trim()
    }

    /** Onde começa o trecho a apagar em [before] (before.length = não apagar nada). */
    private fun deletionStart(before: String): Int {
        val sentenceStart = lastIndexOf(before, sentenceEnd = true) + 1
        if (words(before.substring(sentenceStart)) <= MAX_DELETE_WORDS) return sentenceStart
        val clauseStart = lastIndexOf(before, sentenceEnd = false) + 1
        if (clauseStart > sentenceStart && words(before.substring(clauseStart)) <= MAX_DELETE_WORDS) return clauseStart
        return before.length
    }

    private fun words(s: String): Int = word.findAll(s).count()

    /**
     * Último fim de frase (., !, ?, quebra de linha ou fim de marcador de título "]]"); com
     * sentenceEnd = false, também vírgula, ponto e vírgula e dois-pontos. -1 = nenhum.
     */
    private fun lastIndexOf(s: String, sentenceEnd: Boolean): Int {
        for (i in s.length - 1 downTo 0) {
            val c = s[i]
            if (c == '.' || c == '!' || c == '?' || c == '\n') return i
            if (c == ']' && i > 0 && s[i - 1] == ']') return i
            if (!sentenceEnd && (c == ',' || c == ';' || c == ':')) return i
        }
        return -1
    }
}
