package com.voxia.keyboard.data

import java.text.Normalizer

/**
 * Percebe quando o usuário corrige à mão uma palavra que o VoxIA acabou de escrever
 * (ex.: "desartria" → "disartria") para sugerir ensiná-la ao dicionário.
 *
 * Só conta como correção a troca de 1 a 3 palavras por 1 a 3 palavras parecidas na escrita,
 * dentro do trecho ditado. Reescrever a frase, apagar ou continuar escrevendo não gera sugestão.
 */
object CorrectionLearner {
    data class Correction(val original: String, val replacement: String)

    private const val MAX_WORDS = 3
    private const val MIN_SIMILARITY = 0.5
    private const val MAX_DIFF_WORDS = 200

    private val wordRegex = Regex("""[\p{L}\p{N}][\p{L}\p{N}'’\-]*""")

    /**
     * [before] = texto do campo logo depois de o VoxIA inserir o ditado, que ocupou
     * [insertStart, insertEnd); [after] = texto do campo agora.
     */
    fun find(before: String, after: String, insertStart: Int, insertEnd: Int): List<Correction> {
        if (before == after) return emptyList()

        // Recorta só a região que mudou (prefixo e sufixo em comum ficam de fora)
        var p = 0
        val maxP = minOf(before.length, after.length)
        while (p < maxP && before[p] == after[p]) p++
        var s = 0
        while (s < before.length - p && s < after.length - p &&
            before[before.length - 1 - s] == after[after.length - 1 - s]) s++

        // A mudança precisa cair dentro do que foi ditado
        val changeStart = p
        val changeEnd = before.length - s
        if (changeEnd < insertStart || changeStart > insertEnd) return emptyList()

        // Alarga até limites de palavra nos dois textos
        var b0 = p
        while (b0 > 0 && isWordChar(before[b0 - 1])) b0--
        var b1 = before.length - s
        while (b1 < before.length && isWordChar(before[b1])) b1++
        val a0 = b0
        val a1 = after.length - (before.length - b1)
        if (a1 < a0) return emptyList()

        val oldWords = wordRegex.findAll(before.substring(b0, b1)).map { it.value }.toList()
        val newWords = wordRegex.findAll(after.substring(a0, a1)).map { it.value }.toList()
        if (oldWords.isEmpty() || newWords.isEmpty()) return emptyList()
        if (oldWords.size > MAX_DIFF_WORDS || newWords.size > MAX_DIFF_WORDS) return emptyList()

        return hunks(oldWords, newWords).mapNotNull { (old, new) -> toCorrection(old, new) }.distinct()
    }

    private fun toCorrection(old: List<String>, new: List<String>): Correction? {
        if (old.isEmpty() || new.isEmpty() || old.size > MAX_WORDS || new.size > MAX_WORDS) return null
        val original = old.joinToString(" ")
        val replacement = new.joinToString(" ")
        if (original.equals(replacement, ignoreCase = true)) return null // só maiúscula/minúscula
        if (normalize(original) == normalize(replacement)) {
            // Só acento: vale a pena ensinar (ex.: "ceu" → "céu")
            return Correction(original, replacement)
        }
        if (similarity(normalize(original), normalize(replacement)) < MIN_SIMILARITY) return null
        return Correction(original, replacement)
    }

    /** Trechos trocados (palavras antigas → novas) num diff por palavras (LCS). */
    private fun hunks(a: List<String>, b: List<String>): List<Pair<List<String>, List<String>>> {
        val n = a.size
        val m = b.size
        val lcs = Array(n + 1) { IntArray(m + 1) }
        for (i in n - 1 downTo 0) for (j in m - 1 downTo 0) {
            lcs[i][j] = if (a[i] == b[j]) lcs[i + 1][j + 1] + 1 else maxOf(lcs[i + 1][j], lcs[i][j + 1])
        }
        val result = mutableListOf<Pair<List<String>, List<String>>>()
        var i = 0
        var j = 0
        val delA = mutableListOf<String>()
        val addB = mutableListOf<String>()
        fun flush() {
            if (delA.isNotEmpty() || addB.isNotEmpty()) result.add(delA.toList() to addB.toList())
            delA.clear(); addB.clear()
        }
        while (i < n || j < m) {
            when {
                i < n && j < m && a[i] == b[j] -> { flush(); i++; j++ }
                j < m && (i == n || lcs[i][j + 1] >= lcs[i + 1][j]) -> { addB.add(b[j]); j++ }
                else -> { delA.add(a[i]); i++ }
            }
        }
        flush()
        return result
    }

    private fun isWordChar(c: Char) = c.isLetterOrDigit() || c == '\'' || c == '’' || c == '-'

    private fun normalize(s: String): String =
        Normalizer.normalize(s.lowercase(), Normalizer.Form.NFD).replace(Regex("""\p{M}+"""), "")

    /** 1 - distância de Levenshtein / tamanho da maior palavra. */
    fun similarity(x: String, y: String): Double {
        if (x.isEmpty() && y.isEmpty()) return 1.0
        val prev = IntArray(y.length + 1) { it }
        val cur = IntArray(y.length + 1)
        for (i in 1..x.length) {
            cur[0] = i
            for (j in 1..y.length) {
                val cost = if (x[i - 1] == y[j - 1]) 0 else 1
                cur[j] = minOf(cur[j - 1] + 1, prev[j] + 1, prev[j - 1] + cost)
            }
            System.arraycopy(cur, 0, prev, 0, cur.size)
        }
        return 1.0 - prev[y.length].toDouble() / maxOf(x.length, y.length)
    }
}
