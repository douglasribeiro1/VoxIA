using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace VoxIA.Services
{
    public static class TranscriptionService
    {
        private const string GroqTranscriptionEndpoint = "https://api.groq.com/openai/v1/audio/transcriptions";

        private const string BasePrompt = "Ditado por voz em português brasileiro sem legendas.";

        public static async Task<string> TranscribeAudioAsync(string audioFilePath, string modelOverride, string contextText)
        {
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

            using (var httpClient = new HttpClient())
            {
                httpClient.Timeout = TimeSpan.FromSeconds(120);
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                using (var content = new MultipartFormDataContent())
                {
                    byte[] fileBytes = File.ReadAllBytes(audioFilePath);
                    var byteArrayContent = new ByteArrayContent(fileBytes);
                    string mimeType = AudioEncoderService.GetMimeType(audioFilePath);
                    byteArrayContent.Headers.ContentType = MediaTypeHeaderValue.Parse(mimeType);

                    content.Add(byteArrayContent, "file", Path.GetFileName(audioFilePath));
                    content.Add(new StringContent(model), "model");
                    content.Add(new StringContent("pt"), "language");
                    content.Add(new StringContent("0.0"), "temperature");
                    // Guiding prompt to suppress subtitle / YouTube end-card hallucinations
                    content.Add(new StringContent(BuildPrompt(contextText)), "prompt");

                    HttpResponseMessage response = await httpClient.PostAsync(GroqTranscriptionEndpoint, content);
                    string responseBody = await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        Logger.LogError(string.Format("Groq Whisper API error (HTTP {0}): {1}", (int)response.StatusCode, responseBody));
                        throw new HttpRequestException(string.Format("Groq Whisper API returned HTTP {0}: {1}", (int)response.StatusCode, responseBody));
                    }

                    var serializer = new JavaScriptSerializer();
                    var dict = serializer.Deserialize<System.Collections.Generic.Dictionary<string, object>>(responseBody);

                    if (dict != null && dict.ContainsKey("text"))
                    {
                        string rawText = dict["text"] != null ? dict["text"].ToString() : string.Empty;
                        string cleaned = SanitizeWhisperText(rawText);
                        if (IsPromptEcho(cleaned, contextText))
                        {
                            Logger.LogInfo("Whisper returned only the context it was given (no real speech). Discarding.");
                            return string.Empty;
                        }
                        Logger.LogInfo(string.Format("Groq Whisper transcription success (raw: {0} chars, cleaned: {1} chars).", rawText.Length, cleaned.Length));
                        return cleaned;
                    }
                    else
                    {
                        throw new InvalidOperationException(string.Format("Invalid JSON response structure from Groq Whisper API: {0}", responseBody));
                    }
                }
            }
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
        private static string BuildPrompt(string contextText)
        {
            if (string.IsNullOrWhiteSpace(contextText)) return BasePrompt;
            string tail = contextText.Length > 220 ? contextText.Substring(contextText.Length - 220) : contextText;
            int firstSpace = tail.IndexOf(' ');
            if (contextText.Length > 220 && firstSpace > 0 && firstSpace < 40) tail = tail.Substring(firstSpace + 1);
            return BasePrompt + " " + tail;
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
