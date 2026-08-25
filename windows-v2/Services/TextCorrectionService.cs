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

            string systemPrompt = GetSystemPrompt(SettingsService.Current.SelectedPromptPreset, SettingsService.Current.CustomPromptText);

            // Dynamically calculate max_tokens to strictly respect Groq TPM rate limits (8000 TPM limit on free tier)
            int maxTokens = Math.Max(256, Math.Min(2048, (rawText.Length * 2) + 256));

            Logger.LogInfo(string.Format("Sending raw transcript ({0} chars, max_tokens: {1}) to Groq LLM ('{2}') for correction...", rawText.Length, maxTokens, model));

            try
            {
                using (var httpClient = new HttpClient())
                {
                    httpClient.Timeout = TimeSpan.FromSeconds(30);
                    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                    var payload = new
                    {
                        model = model,
                        temperature = 0.0,
                        max_tokens = maxTokens,
                        messages = new object[]
                        {
                            new { role = "system", content = systemPrompt },
                            new { role = "user", content = rawText }
                        }
                    };

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
                            if (firstChoice != null && firstChoice.ContainsKey("message"))
                            {
                                var msg = firstChoice["message"] as Dictionary<string, object>;
                                if (msg != null && msg.ContainsKey("content"))
                                {
                                    string corrected = msg["content"] != null ? msg["content"].ToString() : rawText;
                                    corrected = CleanOutput(corrected);
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

        public static Task<string> CorrectTextAsync(string rawText)
        {
            return CorrectTextAsync(rawText, null);
        }

        private static string GetSystemPrompt(string preset, string customPrompt)
        {
            return SettingsService.GetActivePromptText();
        }

        private static string CleanOutput(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            string text = input.Trim();

            // Strip out <think>...</think> reasoning blocks from Qwen / DeepSeek models
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

            if (text.StartsWith("\"") && text.EndsWith("\"") && text.Length > 1)
            {
                text = text.Substring(1, text.Length - 2).Trim();
            }

            if (text.StartsWith("```") && text.EndsWith("```"))
            {
                int firstBreak = text.IndexOf('\n');
                if (firstBreak > 0 && text.Length > firstBreak + 3)
                {
                    text = text.Substring(firstBreak + 1, text.Length - firstBreak - 4).Trim();
                }
            }

            return text;
        }
    }
}
