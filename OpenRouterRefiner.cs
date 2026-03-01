using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Speakly.Config;

namespace Speakly.Services
{
    public class OpenRouterRefiner : ITextRefiner
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        public OpenRouterRefiner()
        {
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public async Task<string> RefineTextAsync(string text, string prompt)
        {
            var apiKey = ConfigManager.Config.OpenRouterApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return text;
            }

            try
            {
                var safePrompt = RefinementSafety.BuildSafeSystemPrompt(prompt);
                var userMessage = RefinementSafety.BuildRefinementUserMessage(text);
                var requestBody = new
                {
                    model = ConfigManager.Config.OpenRouterRefinementModel,
                    messages = new[]
                    {
                        new { role = "system", content = safePrompt },
                        new { role = "user", content = userMessage }
                    },
                    temperature = 0.3
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var request = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions")
                {
                    Content = content
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                request.Headers.Add("HTTP-Referer", "https://github.com/speakly"); // Optional for OpenRouter
                request.Headers.Add("X-Title", "Speakly App");

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

                    return RefinementSafety.CoerceToEditOnlyOutput(text, refinedText);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OpenRouter Refinement Error: {ex.Message}");
            }

            return text;
        }
    }
}
