using System;
using System.Globalization;
using System.Text;

namespace VoxIA.Services
{
    /// <summary>
    /// Normaliza o texto final para sistemas legados (prontuários antigos, campos ANSI / Windows-1252).
    /// O LLM costuma devolver caracteres tipográficos Unicode (hífen inseparável U+2011, travessão U+2014,
    /// aspas curvas, espaço fino U+202F etc.) que aparecem como "quadrado" em apps antigos.
    /// Acentos do português (á, ç, õ...) são preservados.
    /// </summary>
    public static class TextSanitizer
    {
        private static readonly Encoding Ansi1252 = Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

        public static string NormalizeForLegacyApps(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            // Acentos decompostos (a + ´) viram caracteres únicos (á)
            string text;
            try { text = input.Normalize(NormalizationForm.FormC); }
            catch (ArgumentException) { text = input; }
            var sb = new StringBuilder(text.Length);

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                switch (c)
                {
                    // Hífens, traços e sinais de menos -> hífen ASCII
                    case '\u2010': // hyphen
                    case '\u2011': // non-breaking hyphen (muito comum no gpt-oss)
                    case '\u2012': // figure dash
                    case '\u2013': // en dash
                    case '\u2014': // em dash
                    case '\u2015': // horizontal bar
                    case '\u2043': // hyphen bullet
                    case '\u2212': // minus sign
                    case '\uFE58':
                    case '\uFE63':
                    case '\uFF0D':
                        sb.Append('-');
                        break;

                    // Espaços especiais -> espaço comum
                    case '\u00A0':
                    case '\u2000': case '\u2001': case '\u2002': case '\u2003': case '\u2004': case '\u2005':
                    case '\u2006': case '\u2007': case '\u2008': case '\u2009': case '\u200A':
                    case '\u202F':
                    case '\u205F':
                    case '\u3000':
                        sb.Append(' ');
                        break;

                    // Caracteres invisíveis -> removidos
                    case '\u00AD': // soft hyphen
                    case '\u200B': case '\u200C': case '\u200D': case '\u200E': case '\u200F':
                    case '\u2060':
                    case '\uFEFF':
                        break;

                    // Aspas tipográficas -> aspas retas
                    case '\u2018': case '\u2019': case '\u201A': case '\u201B': case '\u2032':
                        sb.Append('\'');
                        break;
                    case '\u201C': case '\u201D': case '\u201E': case '\u201F': case '\u2033':
                        sb.Append('"');
                        break;

                    case '\u2026': sb.Append("..."); break;
                    case '\u2022': case '\u2023': case '\u25CF': case '\u25E6': sb.Append('-'); break;
                    case '\u2192': sb.Append("->"); break;
                    case '\u2190': sb.Append("<-"); break;
                    case '\u2264': sb.Append("<="); break;
                    case '\u2265': sb.Append(">="); break;
                    case '\u2248': sb.Append('~'); break;
                    case '\u2044': case '\u2215': sb.Append('/'); break;

                    // Quebras de linha: apps Win32 antigos exigem CRLF
                    case '\u2028': case '\u2029': case '\u0085':
                        sb.Append("\r\n");
                        break;
                    case '\r':
                        sb.Append("\r\n");
                        if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                        break;
                    case '\n':
                        sb.Append("\r\n");
                        break;

                    default:
                        AppendLegacySafe(sb, c);
                        break;
                }
            }

            return sb.ToString();
        }

        private static void AppendLegacySafe(StringBuilder sb, char c)
        {
            // Emojis / caracteres fora do BMP (pares substitutos) não existem em sistemas legados
            if (char.IsSurrogate(c)) return;

            if (c < 0x80 || IsAnsiEncodable(c.ToString()))
            {
                sb.Append(c);
                return;
            }

            // Fallback: decomposição de compatibilidade (ex.: "ﬁ" -> "fi", "½" já é ANSI),
            // mantendo apenas o que o Windows-1252 representa. Emojis e símbolos exóticos são descartados.
            string decomposed = c.ToString().Normalize(NormalizationForm.FormKD);
            foreach (char d in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(d) == UnicodeCategory.NonSpacingMark) continue;
                if (d < 0x80 || IsAnsiEncodable(d.ToString()))
                    sb.Append(d);
            }
        }

        private static bool IsAnsiEncodable(string s)
        {
            try
            {
                Ansi1252.GetBytes(s);
                return true;
            }
            catch (EncoderFallbackException)
            {
                return false;
            }
        }
    }
}
