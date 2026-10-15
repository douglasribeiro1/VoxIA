using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace VoxIA.Services
{
    public class TopicHeading
    {
        public bool IsTitle;
        public string Text;
    }

    /// <summary>
    /// Voice commands that turn a spoken phrase into a heading:
    ///   "abrir tópico exame físico fechar tópico"      -> EXAME FÍSICO (own line, body below)
    ///   "abrir título enfermaria ... traço ... fechar título" -> TITLE IN CAPS with " - ", no final period
    /// Headings are replaced by markers before the LLM runs (so it can never alter them) and written
    /// back afterwards. Without a closing command nothing is changed.
    /// </summary>
    public static class TopicFormatter
    {
        private const int MaxTopicWords = 16;
        private const int MaxTitleWords = 30;

        private static readonly Regex WordRegex = new Regex(@"[\p{L}\p{N}]+", RegexOptions.Compiled);
        private static readonly Regex MarkerRegex = new Regex(@"\s*\[\[\s*T\s*(\d+)\s*\]\]\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex DashWordRegex = new Regex(@"(?<![\p{L}\p{N}])(?:tra[çc]o|h[íi]fen)(?![\p{L}\p{N}])", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private class Tok
        {
            public int Start;
            public int End;
            public string Norm;
        }

        public static bool HasMarkers(string text)
        {
            return !string.IsNullOrEmpty(text) && MarkerRegex.IsMatch(text);
        }

        /// <summary>True when, apart from markers, there is no real text left (nothing to send to the LLM).</summary>
        public static bool IsOnlyMarkers(string text)
        {
            if (string.IsNullOrEmpty(text)) return true;
            string rest = MarkerRegex.Replace(text, " ");
            return !Regex.IsMatch(rest, @"[\p{L}\p{N}]");
        }

        /// <summary>
        /// Replaces every complete "abrir tópico/título ... fechar tópico/título" with a marker [[Tn]].
        /// Returns the text unchanged when no complete pair exists.
        /// </summary>
        public static string ExtractMarkers(string text, List<TopicHeading> headings)
        {
            if (string.IsNullOrEmpty(text)) return text;

            var toks = new List<Tok>();
            foreach (Match m in WordRegex.Matches(text))
            {
                toks.Add(new Tok { Start = m.Index, End = m.Index + m.Length, Norm = Normalize(m.Value) });
            }
            if (toks.Count < 4) return text;

            var sb = new StringBuilder();
            int copiedUpTo = 0;
            int i = 0;
            while (i < toks.Count)
            {
                bool isTitle;
                int openLen = MatchOpen(toks, i, out isTitle);
                if (openLen == 0) { i++; continue; }

                int j = i + openLen;
                int maxWords = isTitle ? MaxTitleWords : MaxTopicWords;
                int closeAt = -1, closeLen = 0;
                for (int k = j + 1; k < toks.Count && k <= j + maxWords; k++)
                {
                    int cl = MatchClose(toks, k);
                    if (cl > 0) { closeAt = k; closeLen = cl; break; }
                }
                if (closeAt < 0) { i++; continue; }

                string raw = text.Substring(toks[j].Start, toks[closeAt - 1].End - toks[j].Start);
                string heading = CleanHeading(raw);
                if (heading.Length == 0) { i++; continue; }

                // text before the command
                string before = text.Substring(copiedUpTo, toks[i].Start - copiedUpTo);
                before = before.TrimEnd(' ', '\t', '\r', '\n', ',', ';', ':');
                sb.Append(before);

                headings.Add(new TopicHeading { IsTitle = isTitle, Text = heading });
                sb.Append(" [[T").Append(headings.Count - 1).Append("]] ");

                // skip the closing command and punctuation Whisper may have left right after it
                int afterClose = toks[closeAt + closeLen - 1].End;
                while (afterClose < text.Length && (char.IsWhiteSpace(text[afterClose]) || ".,;:!?".IndexOf(text[afterClose]) >= 0))
                    afterClose++;
                copiedUpTo = afterClose;
                i = closeAt + closeLen;
            }

            if (headings.Count == 0) return text;

            sb.Append(text.Substring(copiedUpTo));
            return sb.ToString().Trim();
        }

        /// <summary>
        /// Writes the headings back. If the LLM dropped or duplicated a marker, the deterministic
        /// fallback text (the same text without LLM processing) is used instead.
        /// </summary>
        public static string Restore(string text, List<TopicHeading> headings, string fallbackMarkedText, Func<string, string> headingTransform)
        {
            if (headings == null || headings.Count == 0) return text;
            if (!AllMarkersPresentOnce(text, headings.Count))
            {
                Logger.LogWarning("LLM output lost or duplicated a topic marker; using the unprocessed text for this dictation.");
                text = fallbackMarkedText;
                if (!AllMarkersPresentOnce(text, headings.Count))
                    return text;
            }

            var matches = MarkerRegex.Matches(text);
            var result = new StringBuilder();
            int cursor = 0;
            string pendingSeparator = null;   // what to put between the last heading and the next body text

            for (int m = 0; m < matches.Count; m++)
            {
                Match match = matches[m];
                string piece = text.Substring(cursor, match.Index - cursor).Trim();
                AppendPiece(result, piece, ref pendingSeparator);

                int idx = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                TopicHeading h = headings[idx];
                string headText = h.Text;
                if (headingTransform != null)
                {
                    string t = headingTransform(headText);
                    if (!string.IsNullOrWhiteSpace(t)) headText = t.Trim();
                }
                headText = headText.ToUpper(new CultureInfo("pt-BR")).Trim().TrimEnd('.', ',', ';', ':', '!', '?').Trim();

                if (result.Length > 0) result.Append("\n\n");
                result.Append(headText);
                pendingSeparator = h.IsTitle ? "\n\n" : "\n";
                cursor = match.Index + match.Length;
            }

            string tail = text.Substring(cursor).Trim();
            AppendPiece(result, tail, ref pendingSeparator);
            return result.ToString().Trim();
        }

        private static void AppendPiece(StringBuilder result, string piece, ref string pendingSeparator)
        {
            if (piece.Length == 0) return;
            if (pendingSeparator != null)
            {
                result.Append(pendingSeparator);
                if (char.IsLower(piece[0])) piece = char.ToUpper(piece[0], new CultureInfo("pt-BR")) + piece.Substring(1);
                pendingSeparator = null;
            }
            result.Append(piece);
        }

        private static bool AllMarkersPresentOnce(string text, int count)
        {
            if (string.IsNullOrEmpty(text)) return false;
            var seen = new int[count];
            foreach (Match m in MarkerRegex.Matches(text))
            {
                int idx;
                if (!int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out idx) || idx < 0 || idx >= count)
                    return false;
                seen[idx]++;
            }
            for (int i = 0; i < count; i++)
            {
                if (seen[i] != 1) return false;
            }
            return true;
        }

        private static int MatchOpen(List<Tok> toks, int i, out bool isTitle)
        {
            isTitle = false;
            string w = toks[i].Norm;
            if (w != "abrir" && w != "abre" && w != "abra" && w != "abri") return 0;

            int p = i + 1;
            if (p < toks.Count && toks[p].Norm == "o") p++;
            if (p >= toks.Count) return 0;
            string kind = toks[p].Norm;
            if (kind == "topico" || kind == "topicos") { isTitle = false; return p - i + 1; }
            if (kind == "titulo" || kind == "titulos") { isTitle = true; return p - i + 1; }
            return 0;
        }

        private static int MatchClose(List<Tok> toks, int k)
        {
            string w = toks[k].Norm;
            if (w != "fechar" && w != "fecha" && w != "feche") return 0;

            int p = k + 1;
            if (p < toks.Count && toks[p].Norm == "o") p++;
            if (p >= toks.Count) return 0;
            string kind = toks[p].Norm;
            if (kind == "topico" || kind == "topicos" || kind == "titulo" || kind == "titulos") return p - k + 1;
            return 0;
        }

        private static string CleanHeading(string raw)
        {
            string s = DashWordRegex.Replace(raw, " - ");
            s = Regex.Replace(s, @"[,.;:!?…]+", " ");
            s = Regex.Replace(s, @"\s+", " ").Trim();
            s = Regex.Replace(s, @"(\s*-\s*){2,}", " - ");
            s = s.Trim(' ', '-');
            return s;
        }

        private static string Normalize(string word)
        {
            string d = word.ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(d.Length);
            foreach (char c in d)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
