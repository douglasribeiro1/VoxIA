package com.voxia.keyboard.data

import android.util.Log
import java.text.Normalizer
import java.util.Locale

data class TopicHeading(val isTitle: Boolean, val text: String)

/**
 * Comandos de voz que viram títulos:
 *   "abrir tópico exame físico fechar tópico"            -> EXAME FÍSICO (em linha própria, texto abaixo)
 *   "abrir título enfermaria ... traço ... fechar título" -> TÍTULO EM CAIXA ALTA com " - ", sem ponto final
 * Os títulos são trocados por marcadores [[Tn]] antes da LLM (que não consegue alterá-los)
 * e escritos de volta depois. Sem o comando de fechar, nada é alterado.
 */
object TopicFormatter {
    private const val MAX_TOPIC_WORDS = 16
    private const val MAX_TITLE_WORDS = 30

    private val wordRegex = Regex("""[\p{L}\p{N}]+""")
    private val markerRegex = Regex("""\s*\[\[\s*T\s*(\d+)\s*\]\]\s*""", RegexOption.IGNORE_CASE)
    private val dashWordRegex = Regex("""(?<![\p{L}\p{N}])(?:tra[çc]o|h[íi]fen)(?![\p{L}\p{N}])""", RegexOption.IGNORE_CASE)
    private val ptBr = Locale("pt", "BR")

    private class Tok(val start: Int, val end: Int, val norm: String)

    fun hasMarkers(text: String?): Boolean = !text.isNullOrEmpty() && markerRegex.containsMatchIn(text)

    /** Verdadeiro quando, além dos marcadores, não sobrou texto real (nada a enviar para a LLM). */
    fun isOnlyMarkers(text: String?): Boolean {
        if (text.isNullOrEmpty()) return true
        val rest = markerRegex.replace(text, " ")
        return !Regex("""[\p{L}\p{N}]""").containsMatchIn(rest)
    }

    /**
     * Troca cada "abrir tópico/título ... fechar tópico/título" por um marcador [[Tn]].
     * Devolve o texto inalterado quando não existe nenhum par completo.
     */
    fun extractMarkers(text: String, headings: MutableList<TopicHeading>): String {
        if (text.isEmpty()) return text

        val toks = wordRegex.findAll(text).map { Tok(it.range.first, it.range.last + 1, normalize(it.value)) }.toList()
        if (toks.size < 4) return text

        val sb = StringBuilder()
        var copiedUpTo = 0
        var i = 0
        while (i < toks.size) {
            val open = matchOpen(toks, i)
            if (open == null) { i++; continue }
            val openLen = open.first
            val isTitle = open.second

            val j = i + openLen
            val maxWords = if (isTitle) MAX_TITLE_WORDS else MAX_TOPIC_WORDS
            var closeAt = -1
            var closeLen = 0
            var k = j + 1
            while (k < toks.size && k <= j + maxWords) {
                val cl = matchClose(toks, k)
                if (cl > 0) { closeAt = k; closeLen = cl; break }
                k++
            }
            if (closeAt < 0) { i++; continue }

            val raw = text.substring(toks[j].start, toks[closeAt - 1].end)
            val heading = cleanHeading(raw)
            if (heading.isEmpty()) { i++; continue }

            var before = text.substring(copiedUpTo, toks[i].start)
            before = before.trimEnd(' ', '\t', '\r', '\n', ',', ';', ':')
            sb.append(before)

            headings.add(TopicHeading(isTitle, heading))
            sb.append(" [[T").append(headings.size - 1).append("]] ")

            // pula o comando de fechar e a pontuação que o Whisper possa ter deixado logo depois
            var afterClose = toks[closeAt + closeLen - 1].end
            while (afterClose < text.length && (text[afterClose].isWhitespace() || ".,;:!?".indexOf(text[afterClose]) >= 0)) {
                afterClose++
            }
            copiedUpTo = afterClose
            i = closeAt + closeLen
        }

        if (headings.isEmpty()) return text

        sb.append(text.substring(copiedUpTo))
        return sb.toString().trim()
    }

    /**
     * Escreve os títulos de volta. Se a LLM perdeu ou duplicou um marcador, usa o texto de
     * contingência (o mesmo texto sem processamento da LLM).
     */
    fun restore(
        textIn: String,
        headings: List<TopicHeading>,
        fallbackMarkedText: String,
        headingTransform: ((String) -> String)? = null
    ): String {
        if (headings.isEmpty()) return textIn
        var text = textIn
        if (!allMarkersPresentOnce(text, headings.size)) {
            Log.w("TopicFormatter", "A LLM perdeu ou duplicou um marcador de tópico; usando o texto sem processamento.")
            text = fallbackMarkedText
            if (!allMarkersPresentOnce(text, headings.size)) return text
        }

        val matches = markerRegex.findAll(text).toList()
        val result = StringBuilder()
        var cursor = 0
        var pendingSeparator: String? = null

        for (match in matches) {
            val piece = text.substring(cursor, match.range.first).trim()
            pendingSeparator = appendPiece(result, piece, pendingSeparator)

            val idx = match.groupValues[1].toInt()
            val h = headings[idx]
            var headText = h.text
            if (headingTransform != null) {
                val t = headingTransform(headText)
                if (t.isNotBlank()) headText = t.trim()
            }
            headText = headText.uppercase(ptBr).trim().trimEnd('.', ',', ';', ':', '!', '?').trim()

            if (result.isNotEmpty()) result.append("\n\n")
            result.append(headText)
            pendingSeparator = if (h.isTitle) "\n\n" else "\n"
            cursor = match.range.last + 1
        }

        val tail = text.substring(cursor).trim()
        appendPiece(result, tail, pendingSeparator)
        return result.toString().trim()
    }

    private fun appendPiece(result: StringBuilder, pieceIn: String, pendingSeparator: String?): String? {
        if (pieceIn.isEmpty()) return pendingSeparator
        var piece = pieceIn
        if (pendingSeparator != null) {
            result.append(pendingSeparator)
            if (piece[0].isLowerCase()) piece = piece.substring(0, 1).uppercase(ptBr) + piece.substring(1)
        }
        result.append(piece)
        return null
    }

    private fun allMarkersPresentOnce(text: String?, count: Int): Boolean {
        if (text.isNullOrEmpty()) return false
        val seen = IntArray(count)
        for (m in markerRegex.findAll(text)) {
            val idx = m.groupValues[1].toIntOrNull() ?: return false
            if (idx < 0 || idx >= count) return false
            seen[idx]++
        }
        return seen.all { it == 1 }
    }

    /** Devolve (quantidade de palavras do comando, éTítulo) ou null. */
    private fun matchOpen(toks: List<Tok>, i: Int): Pair<Int, Boolean>? {
        val w = toks[i].norm
        if (w != "abrir" && w != "abre" && w != "abra" && w != "abri") return null

        var p = i + 1
        if (p < toks.size && toks[p].norm == "o") p++
        if (p >= toks.size) return null
        return when (toks[p].norm) {
            "topico", "topicos" -> Pair(p - i + 1, false)
            "titulo", "titulos" -> Pair(p - i + 1, true)
            else -> null
        }
    }

    private fun matchClose(toks: List<Tok>, k: Int): Int {
        val w = toks[k].norm
        if (w != "fechar" && w != "fecha" && w != "feche") return 0

        var p = k + 1
        if (p < toks.size && toks[p].norm == "o") p++
        if (p >= toks.size) return 0
        return when (toks[p].norm) {
            "topico", "topicos", "titulo", "titulos" -> p - k + 1
            else -> 0
        }
    }

    private fun cleanHeading(raw: String): String {
        var s = dashWordRegex.replace(raw, " - ")
        s = Regex("""[,.;:!?…]+""").replace(s, " ")
        s = Regex("""\s+""").replace(s, " ").trim()
        s = Regex("""(\s*-\s*){2,}""").replace(s, " - ")
        s = s.trim(' ', '-')
        return s
    }

    private fun normalize(word: String): String {
        val d = Normalizer.normalize(word.lowercase(Locale.ROOT), Normalizer.Form.NFD)
        val sb = StringBuilder(d.length)
        for (c in d) {
            if (Character.getType(c) != Character.NON_SPACING_MARK.toInt()) sb.append(c)
        }
        return sb.toString()
    }
}
