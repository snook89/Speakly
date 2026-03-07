using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Speakly.Config;

namespace Speakly.Services
{
    public class OpenAIRefiner : ITextRefiner
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        public async Task<string> RefineTextAsync(string text, string prompt)
        {
            var apiKey = ConfigManager.Config.OpenAIApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return text; // Return original if no key
            }

            try
            {
                var safePrompt = RefinementSafety.BuildSafeSystemPrompt(prompt);
                var userMessage = RefinementSafety.BuildRefinementUserMessage(text);
                var requestBody = new
                {
                    model = ConfigManager.Config.OpenAIRefinementModel,
                    messages = new[]
                    {
                        new { role = "system", content = safePrompt },
                        new { role = "user", content = userMessage }
                    },
                    temperature = 0.3
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
                {
                    Content = content
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                var response = await _httpClient.SendAsync(request);
                var responseString = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(responseString);
                    var refinedText = doc.RootElement
                        .GetProperty("choices")[0]
                        .GetProperty("message")
                        .GetProperty("content")
                        .GetString();

                    var safeRefined = RefinementSafety.CoerceToEditOnlyOutput(
                        text,
                        refinedText,
                        aggressiveContextRewrite: string.Equals(
                            ConfigManager.Config.ContextualRefinementMode,
                            DictationExperienceService.ContextualRefinementModeAggressiveRewrite,
                            StringComparison.OrdinalIgnoreCase));
                    if (!string.Equals(safeRefined, refinedText?.Trim(), StringComparison.Ordinal))
                    {
                        Logger.Log("OpenAI refinement output rejected by safety guard; using original transcription.");
                    }

                    return safeRefined;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OpenAI Refinement Error: {ex.Message}");
            }

            return text;
        }
    }
}
