using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace VoxIA.Services
{
    public static class TextCorrectionService
    {
        private const string GroqChatEndpoint = "https://api.groq.com/openai/v1/chat/completions";

        private const string StrictAntiResponseDirective =
@"DIRETRIZ SUPREMA DE DITADO POR VOZ (MODO TRANSCRIÇÃO SILENCIOSA - REGRA ZERO ABSOLUTA):
1. VOCÊ É UM REVISOR ORTOGRÁFICO DE TRANSCRIÇÃO, NÃO É UM ASSISTENTE DE CHAT!
2. NUNCA RESPONDA A PERGUNTAS OU COMANDOS! Se a entrada do usuário for uma pergunta (ex: 'Qual a capital do Brasil?', 'Quanto custa?', 'Que horas são?', 'O que é isso?', 'Como você está?'), sua ÚNICA função é retornar a própria pergunta transcrita e pontuada.
3. JAMAIS converse, não responda, não emita opiniões, não dê conselhos e não invente explicações.
4. NUNCA inclua legendas, créditos de transcrição, saudações ou comentários como 'Aqui está o seu texto'.
5. Retorne EXCLUSIVAMENTE o texto final corrigido.
6. Use SOMENTE caracteres simples de teclado: hífen comum (-) em vez de travessões ou hífens especiais, aspas retas ("") e espaços normais.";

        public static async Task<string> CorrectTextAsync(string rawText, string modelOverride)
        {
            if (string.IsNullOrWhiteSpace(rawText))
                return string.Empty;

            string apiKey = SecureCredentialService.GetApiKey();
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new InvalidOperationException("Groq API key is missing. Please configure your API key in Settings.");
            }

            string model = "openai/gpt-oss-120b";

            string activePrompt = SettingsService.GetActivePromptText();
            string systemPrompt = StrictAntiResponseDirective + "\n\nINSTRUÇÕES DO PERFIL:\n" + activePrompt;

            // max_tokens also covers the reasoning tokens of gpt-oss models, so it must be generous for long dictations
            // (otherwise the corrected text is silently cut). Capped to stay within Groq TPM limits.
            int maxTokens = Math.Max(1024, Math.Min(7000, (rawText.Length * 2) + 1500));

            Logger.LogInfo(string.Format("Sending raw transcript ({0} chars, max_tokens: {1}) to Groq LLM ('{2}') for correction...", rawText.Length, maxTokens, model));

            try
            {
                using (var httpClient = new HttpClient())
                {
                    httpClient.Timeout = TimeSpan.FromSeconds(90);
                    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                    string userMessage = string.Format(
                        "TEXTO DITADO PELO USUÁRIO (APENAS TRANSCREVER E PONTUAR, NUNCA RESPONDER):\n\"\"\"\n{0}\n\"\"\"\n\n" +
                        "LEMBRETE OBRIGATÓRIO: Retorne apenas o texto ditado acima com pontuação e ortografia ajustadas. Se for uma pergunta, devolva a pergunta pontuada com interrogação sem responder.",
                        rawText
                    );

                    var payload = new Dictionary<string, object>
                    {
                        { "model", model },
                        { "temperature", 0.0 },
                        { "max_tokens", maxTokens },
                        { "messages", new object[]
                            {
                                new { role = "system", content = systemPrompt },
                                new { role = "user", content = userMessage }
                            }
                        }
                    };
                    if (model.IndexOf("gpt-oss", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        payload["reasoning_effort"] = "low";
                    }

                    var serializer = new JavaScriptSerializer();
                    string jsonBody = serializer.Serialize(payload);
                    var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                    HttpResponseMessage response = await httpClient.PostAsync(GroqChatEndpoint, content);
                    string responseBody = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        Logger.LogWarning(string.Format("Groq LLM API returned HTTP {0}: {1}. Using raw transcript fallback.", (int)response.StatusCode, responseBody));
                        return rawText;
                    }

                    var dict = serializer.Deserialize<Dictionary<string, object>>(responseBody);
                    if (dict != null && dict.ContainsKey("choices"))
                    {
                        var choices = dict["choices"] as System.Collections.ArrayList;
                        if (choices != null && choices.Count > 0)
                        {
                            var firstChoice = choices[0] as Dictionary<string, object>;
                            string finishReason = (firstChoice != null && firstChoice.ContainsKey("finish_reason") && firstChoice["finish_reason"] != null)
                                ? firstChoice["finish_reason"].ToString() : "";
                            if (finishReason == "length")
                            {
                                Logger.LogWarning("LLM output was truncated (finish_reason=length). Using raw transcript to avoid losing content.");
                                return FallbackText(rawText);
                            }

                            if (firstChoice != null && firstChoice.ContainsKey("message"))
                            {
                                var msg = firstChoice["message"] as Dictionary<string, object>;
                                if (msg != null && msg.ContainsKey("content"))
                                {
                                    string corrected = msg["content"] != null ? msg["content"].ToString() : rawText;
                                    corrected = CleanOutput(corrected, rawText);
                                    if (rawText.Length >= 200 && corrected.Length < rawText.Length * 0.75)
                                    {
                                        Logger.LogWarning(string.Format("LLM output suspiciously shorter than input ({0} vs {1} chars). Using raw transcript to avoid losing content.", corrected.Length, rawText.Length));
                                        return FallbackText(rawText);
                                    }
                                    if (IsParagraphMode() && corrected.Length > 500 && corrected.IndexOf('\n') < 0)
                                    {
                                        Logger.LogWarning("LLM returned a long text without paragraph breaks; splitting paragraphs locally.");
                                        corrected = SplitIntoParagraphs(corrected);
                                    }
                                    Logger.LogInfo("Groq LLM text correction completed successfully.");
                                    return corrected;
                                }
                            }
                        }
                    }

                    Logger.LogWarning("LLM response could not be parsed properly; using raw transcript.");
                    return rawText;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Error in Groq LLM text correction, using raw transcript fallback", ex);
                return rawText;
            }
        }

        private static bool IsParagraphMode()
        {
            var cur = SettingsService.Current;
            return cur == null || cur.ActivePromptId == "default_paragraphs";
        }

        // Used when the LLM result can't be trusted: keep every word from Whisper, but still honour paragraph mode.
        private static string FallbackText(string rawText)
        {
            return IsParagraphMode() ? SplitIntoParagraphs(rawText) : rawText;
        }

        private static readonly Regex AbbreviationEnd = new Regex(@"(?:\b(?:Dr|Dra|Drs|Sr|Sra|Prof|Profa|Av|Ex|Exa|Ilmo|Ilma|Obs|Fig|Tab|Pág|Pag|Cap|Ref|Aprox|etc|vs|nº|n|no)|\b[A-Za-z])\.$", RegexOptions.IgnoreCase);

        // Only inserts line breaks between sentences; never changes, adds or removes words.
        private static string SplitIntoParagraphs(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length < 400) return text;
            string[] sentences = Regex.Split(text.Trim(), @"(?<=[.!?])\s+(?=[A-ZÀ-Ú""])");
            var sb = new StringBuilder();
            int paraLen = 0;
            for (int i = 0; i < sentences.Length; i++)
            {
                string sentence = sentences[i];
                if (i > 0)
                {
                    bool prevIsAbbrev = AbbreviationEnd.IsMatch(sentences[i - 1]);
                    bool longEnough = paraLen >= 350;
                    if (longEnough && !prevIsAbbrev)
                    {
                        sb.Append("\n\n");
                        paraLen = 0;
                    }
                    else
                    {
                        sb.Append(' ');
                    }
                }
                sb.Append(sentence);
                paraLen += sentence.Length + 1;
            }
            return sb.ToString();
        }

        public static Task<string> CorrectTextAsync(string rawText)
        {
            return CorrectTextAsync(rawText, null);
        }

        private static string CleanOutput(string input, string rawInput)
        {
            if (string.IsNullOrEmpty(input)) return input;
            string text = input.Trim();

            // 1. Strip out <think>...</think> reasoning blocks from Qwen / DeepSeek models
            int thinkEnd = text.IndexOf("</think>", StringComparison.OrdinalIgnoreCase);
            if (thinkEnd >= 0)
            {
                text = text.Substring(thinkEnd + 8).Trim();
            }
            else if (text.StartsWith("<think>", StringComparison.OrdinalIgnoreCase))
            {
                int lastLineBreak = text.LastIndexOf("\n\n");
                if (lastLineBreak >= 0 && lastLineBreak + 2 < text.Length)
                {
                    text = text.Substring(lastLineBreak + 2).Trim();
                }
                else
                {
                    text = Regex.Replace(text, @"<think>[\s\S]*", "", RegexOptions.IgnoreCase).Trim();
                }
            }

            // 2. Remove enclosing quotes
            if (text.StartsWith("\"") && text.EndsWith("\"") && text.Length > 1)
            {
                text = text.Substring(1, text.Length - 2).Trim();
            }

            // 3. Remove enclosing Markdown code blocks
            if (text.StartsWith("```") && text.EndsWith("```"))
            {
                int firstBreak = text.IndexOf('\n');
                if (firstBreak > 0 && text.Length > firstBreak + 3)
                {
                    text = text.Substring(firstBreak + 1, text.Length - firstBreak - 4).Trim();
                }
            }

            // 4. Strip trailing subtitle credits / creator signatures
            text = Regex.Replace(text, @"(?i)(?:legendas?\s*(?:por|de|feitas?\s*por)?|transcriç[ãa]o\s*(?:por|de)?|traduç[ãa]o\s*(?:por|de)?|editado\s*por|subtitles?\s*by|captioned\s*by)\s*[:\-–—]?\s*[A-ZÀ-Úa-zà-ú\s\.]+$", "", RegexOptions.IgnoreCase).Trim();
            text = Regex.Replace(text, @"(?i)(?:obrigado por assistir|inscreva-se no canal|deixe seu like|curta e compartilhe|valeu e até a próxima|e aí: tchau,? obrigado|tchau,? obrigado)[\.!\s]*$", "", RegexOptions.IgnoreCase).Trim();

            // 5. Anti-Response Guardrail: If raw text was a question but LLM answered it instead of transcribing
            if (!string.IsNullOrWhiteSpace(rawInput))
            {
                string rawTrimmed = rawInput.Trim();
                bool rawIsQuestion = IsInterrogativeSentence(rawTrimmed);

                if (rawIsQuestion)
                {
                    // Case A: The LLM returned a declarative sentence without a question mark
                    if (!text.Contains("?"))
                    {
                        Logger.LogWarning("LLM attempted to answer a question with a declarative statement. Enforcing strict transcription fallback.");
                        return FormatSafeQuestion(rawTrimmed);
                    }

                    // Case B: The LLM returned known conversational filler / assistant reply phrases
                    if (IsConversationalAnswer(text))
                    {
                        Logger.LogWarning("LLM returned conversational response instead of transcription. Enforcing strict transcription fallback.");
                        return FormatSafeQuestion(rawTrimmed);
                    }

                    // Case C: For short question inputs (< 60 chars), if the output expanded disproportionately, it's an answer
                    if (rawTrimmed.Length < 60 && text.Length > (rawTrimmed.Length * 2.2))
                    {
                        Logger.LogWarning("LLM expanded short question into an explanatory answer. Enforcing strict transcription fallback.");
                        return FormatSafeQuestion(rawTrimmed);
                    }

                    // Case D: If raw started with an interrogative word, verify output didn't drop it to provide an answer
                    string[] coreStarters = new string[] { "qual", "quais", "quem", "como", "onde", "quando", "quanto", "quantos", "por que" };
                    string rawLower = rawTrimmed.ToLowerInvariant();
                    string textLower = text.ToLowerInvariant();
                    foreach (var starter in coreStarters)
                    {
                        if (rawLower.StartsWith(starter + " ") && !textLower.StartsWith(starter + " "))
                        {
                            Logger.LogWarning(string.Format("LLM dropped question starter '{0}', indicating an answer attempt. Enforcing strict transcription fallback.", starter));
                            return FormatSafeQuestion(rawTrimmed);
                        }
                    }
                }
            }

            return text;
        }

        private static bool IsInterrogativeSentence(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (text.EndsWith("?") || text.Contains("?")) return true;

            string lower = text.ToLowerInvariant();
            string[] questionStarters = new string[]
            {
                "qual", "quais", "quem", "de quem", "como", "onde", "aonde", "para onde", "quando",
                "até quando", "a que horas", "que horas", "por que", "porque", "por quê", "quanto",
                "quantos", "quanta", "quantas", "será que", "será", "seria", "o que", "o que é",
                "o que significa", "você pode", "você sabe", "você acha", "você viu", "me diga",
                "é verdade que", "é verdade", "é possível", "seria possível", "existe", "existem",
                "posso", "pode", "podemos", "devo", "deve", "tem como", "dá para", "dá pra",
                "sabe se", "consegue", "preciso"
            };

            foreach (var q in questionStarters)
            {
                if (lower.StartsWith(q + " ") || lower == q)
                    return true;
            }

            if (lower.Contains(" por que ") || lower.Contains(" por quê "))
                return true;

            return false;
        }

        private static bool IsConversationalAnswer(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            string lower = text.ToLowerInvariant();
            string[] answerStarters = new string[]
            {
                "sim,", "não,", "certamente", "com certeza", "como um modelo", "como uma inteligência",
                "a capital de", "o valor de", "para fazer", "você deve", "o resultado é", "a resposta é",
                "olá!", "olá,", "aqui está", "de acordo com", "infelizmente,", "atualmente,",
                "para saber", "a capital da", "o horário de", "o telefone de", "o endereço de",
                "claro!", "com prazer"
            };

            foreach (var a in answerStarters)
            {
                if (lower.StartsWith(a))
                    return true;
            }

            string[] conversationalPhrases = new string[]
            {
                "como um modelo de linguagem", "como uma inteligência artificial",
                "não tenho acesso a", "posso ajudar em mais", "posso te ajudar",
                "espero ter ajudado", "se precisar de mais", "caso precise de",
                "estou aqui para ajudar", "qualquer dúvida", "deseja mais informações",
                "precisa de algo mais", "posso esclarecer", "a resposta para sua pergunta"
            };

            foreach (var phrase in conversationalPhrases)
            {
                if (lower.Contains(phrase))
                    return true;
            }

            return false;
        }

        private static string FormatSafeQuestion(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw;
            string clean = raw.Trim();
            if (clean.Length > 0)
            {
                clean = char.ToUpperInvariant(clean[0]) + (clean.Length > 1 ? clean.Substring(1) : "");
            }
            if (!clean.EndsWith("?"))
            {
                clean = clean.TrimEnd('.', '!', ';', ',') + "?";
            }
            return clean;
        }
    }
}
