using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace VoxIA.Services
{
    public static class TranscriptionService
    {
        private const string GroqTranscriptionEndpoint = "https://api.groq.com/openai/v1/audio/transcriptions";

        private const string BasePrompt = "Ditado por voz em português brasileiro sem legendas.";

        private const int ClearSpeechFrames = 25; // 0,5 s de fala em quadros de 20 ms

        public static Task<string> TranscribeAudioAsync(string audioFilePath, string modelOverride, string contextText)
        {
            return TranscribeAudioAsync(audioFilePath, modelOverride, contextText, null, -1);
        }

        /// <summary>
        /// <paramref name="prefixText"/> = o que já foi transcrito durante a fala (blocos anteriores
        /// deste ditado); ele serve de prompt no lugar do texto do campo. Devolve só o texto deste arquivo.
        /// <paramref name="speechFrames"/> = quadros de 20 ms com fala (-1 = não medido).
        /// </summary>
        public static async Task<string> TranscribeAudioAsync(string audioFilePath, string modelOverride, string contextText, string prefixText, int speechFrames)
        {
            string vocabulary = DictionaryService.GetVocabularyTerms();
            string firstContext = string.IsNullOrWhiteSpace(prefixText) ? contextText : prefixText.Trim();

            string apiKey = SecureCredentialService.GetApiKey();
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new InvalidOperationException("Groq API key is missing. Please configure your API key in Settings.");
            }

            if (!File.Exists(audioFilePath))
            {
                throw new FileNotFoundException(string.Format("Audio file not found: {0}", audioFilePath));
            }

            string model = "whisper-large-v3";

            Logger.LogInfo(string.Format("Initiating Groq Whisper transcription with model '{0}' for file: {1}", model, Path.GetFileName(audioFilePath)));

            List<string> parts = AudioChunker.GetParts(audioFilePath);
            if (parts.Count == 0)
            {
                string prompt = BuildPrompt(vocabulary, firstContext);
                string rawText = await SendToWhisperAsync(audioFilePath, model, apiKey, prompt);
                string cleaned = SanitizeWhisperText(rawText);
                if (IsPromptEcho(cleaned, firstContext) || IsVocabularyEcho(cleaned, vocabulary, speechFrames))
                {
                    Logger.LogInfo("Whisper returned only the context or vocabulary it was given (no real speech). Discarding.");
                    return string.Empty;
                }
                Logger.LogInfo(string.Format("Groq Whisper transcription success (raw: {0} chars, cleaned: {1} chars).", rawText.Length, cleaned.Length));
                return cleaned;
            }

            // Blocks are sent one after the other; each one gets the end of the text so far
            // as its prompt, so sentences and punctuation continue naturally across the cut.
            var sb = new StringBuilder();
            for (int i = 0; i < parts.Count; i++)
            {
                string soFar = sb.ToString();
                string promptContext = soFar.Length == 0 ? firstContext : JoinText(prefixText, soFar);
                string prompt = BuildPrompt(vocabulary, promptContext);
                string piece = StripSoundTags(await SendToWhisperAsync(parts[i], model, apiKey, prompt));
                if (IsPromptEcho(piece, promptContext))
                {
                    Logger.LogInfo(string.Format("Whisper block {0}/{1} only repeated its prompt; ignored.", i + 1, parts.Count));
                    continue;
                }
                Logger.LogInfo(string.Format("Whisper block {0}/{1}: {2} chars.", i + 1, parts.Count, piece.Length));
                if (piece.Length == 0) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(piece);
            }

            string joined = sb.ToString();
            string result = SanitizeWhisperText(joined);
            Logger.LogInfo(string.Format("Groq Whisper transcription success ({0} blocks, raw: {1} chars, cleaned: {2} chars).", parts.Count, joined.Length, result.Length));
            return result;
        }

        /// <summary>
        /// Um bloco cortado numa pausa enquanto a pessoa ainda fala (ver LiveTranscriber). Vai em FLAC
        /// (sem perdas). Devolve "" quando o Whisper só repetiu o prompt.
        /// </summary>
        public static async Task<string> TranscribeLiveChunkAsync(byte[] pcm, int sampleRate, string promptContext, int speechFrames)
        {
            string apiKey = SecureCredentialService.GetApiKey();
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new InvalidOperationException("Groq API key is missing. Please configure your API key in Settings.");
            }
            string vocabulary = DictionaryService.GetVocabularyTerms();
            string prompt = BuildPrompt(vocabulary, promptContext);
            byte[] flac = FlacEncoder.Encode(pcm, sampleRate);
            string piece = StripSoundTags(await SendBytesToWhisperAsync(flac, "bloco.flac", "audio/flac", "whisper-large-v3", apiKey, prompt));
            piece = SanitizeWhisperText(piece);
            if (IsPromptEcho(piece, promptContext) || IsVocabularyEcho(piece, vocabulary, speechFrames))
            {
                Logger.LogInfo("Live block only repeated its prompt; ignored.");
                return string.Empty;
            }
            return piece;
        }

        public static string JoinText(string a, string b)
        {
            a = (a ?? "").Trim();
            b = (b ?? "").Trim();
            if (a.Length == 0) return b;
            if (b.Length == 0) return a;
            return a + " " + b;
        }

        private static Task<string> SendToWhisperAsync(string audioFilePath, string model, string apiKey, string prompt)
        {
            byte[] fileBytes = File.ReadAllBytes(audioFilePath);
            return SendBytesToWhisperAsync(fileBytes, Path.GetFileName(audioFilePath), AudioEncoderService.GetMimeType(audioFilePath), model, apiKey, prompt);
        }

        private static async Task<string> SendBytesToWhisperAsync(byte[] fileBytes, string fileName, string mimeType, string model, string apiKey, string prompt)
        {
            using (var httpClient = new HttpClient())
            {
                httpClient.Timeout = TimeSpan.FromSeconds(120);
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                using (var content = new MultipartFormDataContent())
                {
                    var byteArrayContent = new ByteArrayContent(fileBytes);
                    byteArrayContent.Headers.ContentType = MediaTypeHeaderValue.Parse(mimeType);

                    content.Add(byteArrayContent, "file", fileName);
                    content.Add(new StringContent(model), "model");
                    content.Add(new StringContent("pt"), "language");
                    content.Add(new StringContent("0.0"), "temperature");
                    // Guiding prompt to suppress subtitle / YouTube end-card hallucinations
                    content.Add(new StringContent(prompt), "prompt");

                    HttpResponseMessage response = await httpClient.PostAsync(GroqTranscriptionEndpoint, content);
                    string responseBody = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        Logger.LogError(string.Format("Groq Whisper API error (HTTP {0}): {1}", (int)response.StatusCode, responseBody));
                        throw new HttpRequestException(string.Format("Groq Whisper API returned HTTP {0}: {1}", (int)response.StatusCode, responseBody));
                    }

                    var serializer = new JavaScriptSerializer();
                    var dict = serializer.Deserialize<Dictionary<string, object>>(responseBody);

                    if (dict != null && dict.ContainsKey("text"))
                    {
                        return dict["text"] != null ? dict["text"].ToString() : string.Empty;
                    }
                    throw new InvalidOperationException(string.Format("Invalid JSON response structure from Groq Whisper API: {0}", responseBody));
                }
            }
        }

        private static string StripSoundTags(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            string text = Regex.Replace(input.Trim(), @"\[[\s\S]*?\]", "", RegexOptions.IgnoreCase).Trim();
            text = Regex.Replace(text, @"\((?:m[úu]sica|risos|aplausos|sil[êe]ncio|ru[íi]do|legendas?[^\)]*)\)", "", RegexOptions.IgnoreCase).Trim();
            return text;
        }

        public static Task<string> TranscribeAudioAsync(string audioFilePath, string modelOverride)
        {
            return TranscribeAudioAsync(audioFilePath, modelOverride, null);
        }

        public static Task<string> TranscribeAudioAsync(string audioFilePath)
        {
            return TranscribeAudioAsync(audioFilePath, null, null);
        }

        // Whisper conditions on the end of the prompt, so the text already written goes last.
        // The dictionary terms come before it, so Whisper prefers their spelling for similar sounds.
        private static string BuildPrompt(string vocabulary, string contextText)
        {
            string basePrompt = string.IsNullOrWhiteSpace(vocabulary) ? BasePrompt : BasePrompt + " Vocabulário: " + vocabulary;
            if (string.IsNullOrWhiteSpace(contextText)) return basePrompt;
            string tail = contextText.Length > 220 ? contextText.Substring(contextText.Length - 220) : contextText;
            int firstSpace = tail.IndexOf(' ');
            if (contextText.Length > 220 && firstSpace > 0 && firstSpace < 40) tail = tail.Substring(firstSpace + 1);
            return basePrompt + " " + tail;
        }

        /// <summary>
        /// Fala real não sai só com termos do vocabulário. Uma resposta curta feita apenas deles
        /// ("Disartria.") com pouca fala no áudio é o Whisper repetindo o prompt diante de ruído.
        /// Com fala clara (meio segundo ou mais), ou sem medida (-1), a palavra fica.
        /// </summary>
        private static bool IsVocabularyEcho(string transcript, string vocabulary, int speechFrames)
        {
            if (speechFrames < 0 || speechFrames >= ClearSpeechFrames) return false;
            if (string.IsNullOrWhiteSpace(transcript) || string.IsNullOrWhiteSpace(vocabulary)) return false;
            var words = new Regex(@"[\p{L}\p{N}]+");
            var spoken = new List<string>();
            foreach (Match m in words.Matches(transcript.ToLowerInvariant())) spoken.Add(m.Value);
            if (spoken.Count == 0 || spoken.Count > 4) return false;
            var known = new HashSet<string>();
            foreach (Match m in words.Matches((BasePrompt + " " + vocabulary).ToLowerInvariant())) known.Add(m.Value);
            foreach (string w in spoken)
            {
                if (!known.Contains(w)) return false;
            }
            return true;
        }

        private static bool IsPromptEcho(string transcript, string contextText)
        {
            if (string.IsNullOrWhiteSpace(transcript) || string.IsNullOrWhiteSpace(contextText)) return false;
            string t = transcript.Trim().TrimEnd('.', '!', '?', ' ');
            if (t.Length < 30) return false;
            return contextText.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static string SanitizeWhisperText(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            string text = input.Trim();

            // 1. Remove bracketed/parenthetical sound descriptors e.g. [Música], (risos), [silêncio], [Legendas]
            text = Regex.Replace(text, @"\[[\s\S]*?\]", "", RegexOptions.IgnoreCase).Trim();
            text = Regex.Replace(text, @"\((?:m[úu]sica|risos|aplausos|sil[êe]ncio|ru[íi]do|legendas?[^\)]*)\)", "", RegexOptions.IgnoreCase).Trim();

            // 2. Check for pure silence hallucination phrases
            string lower = text.ToLowerInvariant().Trim(" \t\r\n.,!?:;\"'".ToCharArray());
            string[] silenceHallucinations = new string[]
            {
                "tchau obrigado", "tchau, obrigado", "e ai tchau obrigado", "e aí tchau obrigado", "e aí: tchau, obrigado",
                "obrigado por assistir", "muito obrigado por assistir", "obrigado por assistir a este vídeo",
                "inscreva-se no canal", "deixe seu like", "curta e compartilhe", "valeu tchau", "tchau tchau",
                "legendas pela comunidade amara.org", "amara.org", "subtitles by", "legendado por", "transcrito por",
                "obrigado", "muito obrigado", "valeu", "tchau"
            };

            foreach (var h in silenceHallucinations)
            {
                if (lower == h || lower == (h + ".") || lower == (h + "!"))
                {
                    Logger.LogInfo(string.Format("Filtered pure silence hallucination: \"{0}\"", text));
                    return string.Empty;
                }
            }

            // 3. Strip trailing subtitle credits e.g. "Legendas por: Fulano de Tal", "Transcrição: Maria"
            text = Regex.Replace(text, @"(?i)(?:legendas?\s*(?:por|de|feitas?\s*por)?|transcriç[ãa]o\s*(?:por|de)?|traduç[ãa]o\s*(?:por|de)?|editado\s*por|subtitles?\s*by|captioned\s*by)\s*[:\-–—]?\s*[A-ZÀ-Úa-zà-ú\s\.]+$", "", RegexOptions.IgnoreCase).Trim();

            // 4. Strip trailing YouTube/video ending phrases
            text = Regex.Replace(text, @"(?i)(?:obrigado por assistir|inscreva-se no canal|deixe seu like|curta e compartilhe|valeu e até a próxima|e aí: tchau,? obrigado|tchau,? obrigado)[\.!\s]*$", "", RegexOptions.IgnoreCase).Trim();

            return text;
        }
    }
}
