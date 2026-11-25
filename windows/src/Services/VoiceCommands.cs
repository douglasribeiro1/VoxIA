using System;
using System.Text.RegularExpressions;

namespace VoxIA.Services
{
    /// <summary>
    /// Comando falado "apaga isso": descarta a frase dita logo antes dele (desde o último ponto,
    /// interrogação, exclamação, quebra de linha ou título), junto com o próprio comando.
    ///   "Febre de 39 graus. Apaga isso. Febre de 38 graus." -> "Febre de 38 graus."
    /// Travas de segurança (em ditados longos o Whisper às vezes escreve muito texto sem nenhum ponto,
    /// e "até o último ponto" podia virar "o ditado inteiro"):
    ///  - nunca apaga mais de MaxDeleteWords palavras;
    ///  - se a frase anterior for maior que isso, apaga só até a última vírgula; se nem assim couber,
    ///    não apaga nada (só some o próprio comando);
    ///  - o comando só vale isolado, seguido de pausa (pontuação) ou no fim do ditado.
    /// É feito pelo aplicativo, antes da IA, para o resultado ser sempre previsível. Funciona só
    /// dentro do ditado atual. Mesma lógica do Android (VoiceCommands.kt).
    /// </summary>
    public static class VoiceCommands
    {
        public const int MaxDeleteWords = 30;

        private static readonly Regex DeleteCommand = new Regex(
            @"(?<![\p{L}\p{N}])(?:apag(?:a|ue|ar)|risc(?:a|ar|que))\s+(?:isso|isto|essa frase|esta frase|a [úuÚU]ltima frase)(?=\s*(?:[.,;:!?…]|$))[\s.,;:!?…]*",
            RegexOptions.IgnoreCase);

        private static readonly Regex TrailingPunctuation = new Regex(@"[\s.,;:!?…\-–—]+$");
        private static readonly Regex Word = new Regex(@"[\p{L}\p{N}]+");

        public static bool HasDeleteCommand(string text)
        {
            return !string.IsNullOrEmpty(text) && DeleteCommand.IsMatch(text);
        }

        public static string ApplyDeletes(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string result = text;
            for (int guard = 0; guard < 50; guard++)
            {
                Match m = DeleteCommand.Match(result);
                if (!m.Success) break;
                string before = TrailingPunctuation.Replace(result.Substring(0, m.Index), "");
                int cut = DeletionStart(before);
                string kept = before.Substring(0, cut).TrimEnd(' ', '\t');
                string rest = result.Substring(m.Index + m.Length).TrimStart(' ', '\t');
                if (rest.Length == 0) kept = kept.TrimEnd(',', ';', ':', ' ');
                if (kept.Length == 0) result = rest;
                else if (rest.Length == 0) result = kept;
                else if (kept.EndsWith("\n")) result = kept + rest;
                else result = kept + " " + rest;
            }
            return result.Trim();
        }

        /// <summary>Onde começa o trecho a apagar (before.Length = não apagar nada).</summary>
        private static int DeletionStart(string before)
        {
            int sentenceStart = LastIndexOf(before, true) + 1;
            if (Words(before.Substring(sentenceStart)) <= MaxDeleteWords) return sentenceStart;
            int clauseStart = LastIndexOf(before, false) + 1;
            if (clauseStart > sentenceStart && Words(before.Substring(clauseStart)) <= MaxDeleteWords) return clauseStart;
            return before.Length;
        }

        private static int Words(string s)
        {
            return Word.Matches(s).Count;
        }

        /// <summary>
        /// Último fim de frase (., !, ?, quebra de linha ou fim de marcador de título "]]"); com
        /// sentenceEnd = false, também vírgula, ponto e vírgula e dois-pontos. -1 = nenhum.
        /// </summary>
        private static int LastIndexOf(string s, bool sentenceEnd)
        {
            for (int i = s.Length - 1; i >= 0; i--)
            {
                char c = s[i];
                if (c == '.' || c == '!' || c == '?' || c == '\n') return i;
                if (c == ']' && i > 0 && s[i - 1] == ']') return i;
                if (!sentenceEnd && (c == ',' || c == ';' || c == ':')) return i;
            }
            return -1;
        }
    }
}
