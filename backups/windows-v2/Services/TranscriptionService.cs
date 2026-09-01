using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace VoxIA.Services
{
    public static class TranscriptionService
    {
        private const string GroqTranscriptionEndpoint = "https://api.groq.com/openai/v1/audio/transcriptions";

        public static async Task<string> TranscribeAudioAsync(string audioFilePath, string modelOverride)
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
                        string text = dict["text"] != null ? dict["text"].ToString() : string.Empty;
                        Logger.LogInfo(string.Format("Groq Whisper transcription success ({0} chars).", text.Length));
                        return text;
                    }
                    else
                    {
                        throw new InvalidOperationException(string.Format("Invalid JSON response structure from Groq Whisper API: {0}", responseBody));
                    }
                }
            }
        }

        public static Task<string> TranscribeAudioAsync(string audioFilePath)
        {
            return TranscribeAudioAsync(audioFilePath, null);
        }
    }
}
