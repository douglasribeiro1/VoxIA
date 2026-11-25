using System;
using System.Text.RegularExpressions;

namespace VoxIA.Services
{
    /// <summary>
    /// Comando falado "apaga isso": descarta a frase dita logo antes dele (desde o último ponto,
    /// interrogação, exclamação, quebra de linha ou título), junto com o próprio comando.
    ///   "Febre de 39 graus. Apaga isso. Febre de 38 graus." -> "Febre de 38 graus."
    /// É feito pelo aplicativo, antes da IA, para o resultado ser sempre previsível. Funciona só
    /// dentro do ditado atual. Mesma lógica do Android (VoiceCommands.kt).
    /// </summary>
    public static class VoiceCommands
    {
        private static readonly Regex DeleteCommand = new Regex(
            @"(?<![\p{L}\p{N}])(?:apag(?:a|ue|ar)|risc(?:a|ar|que))\s+(?:isso|isto|essa frase|esta frase|a [úuÚU]ltima frase)(?![\p{L}\p{N}])[\s.,;:!?…]*",
            RegexOptions.IgnoreCase);

        private static readonly Regex TrailingPunctuation = new Regex(@"[\s.,;:!?…\-–—]+$");

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
                int boundary = LastBoundary(before);
                string kept = boundary < 0 ? "" : before.Substring(0, boundary + 1).TrimEnd(' ', '\t');
                string rest = result.Substring(m.Index + m.Length).TrimStart(' ', '\t');
                if (kept.Length == 0) result = rest;
                else if (rest.Length == 0) result = kept;
                else if (kept.EndsWith("\n")) result = kept + rest;
                else result = kept + " " + rest;
            }
            return result.Trim();
        }

        /// <summary>Índice do último fim de frase (., !, ?, quebra de linha ou fim de marcador de título "]]").</summary>
        private static int LastBoundary(string s)
        {
            for (int i = s.Length - 1; i >= 0; i--)
            {
                char c = s[i];
                if (c == '.' || c == '!' || c == '?' || c == '\n') return i;
                if (c == ']' && i > 0 && s[i - 1] == ']') return i;
            }
            return -1;
        }
    }
}
