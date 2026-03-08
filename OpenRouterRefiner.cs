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
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        public async Task<string> RefineTextAsync(RefinementRequest request)
        {
            var apiKey = ConfigManager.Config.OpenRouterApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return request.Text;
            }

            try
            {
                var safePrompt = RefinementSafety.BuildSafeSystemPrompt(request.Prompt);
                var userMessage = RefinementSafety.BuildRefinementUserMessage(request.Text);
                var requestBody = new
                {
                    model = string.IsNullOrWhiteSpace(request.Model) ? ConfigManager.Config.OpenRouterRefinementModel : request.Model,
                    messages = new[]
                    {
                        new { role = "system", content = safePrompt },
                        new { role = "user", content = userMessage }
                    },
                    temperature = 0.3
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions")
                {
                    Content = content
                };
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                httpRequest.Headers.Add("HTTP-Referer", "https://github.com/speakly"); // Optional for OpenRouter
                httpRequest.Headers.Add("X-Title", "Speakly App");

                var response = await _httpClient.SendAsync(httpRequest);
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
                        request.Text,
                        refinedText,
                        aggressiveContextRewrite: request.AggressiveContextRewrite);
                    if (!string.Equals(safeRefined, refinedText?.Trim(), StringComparison.Ordinal))
                    {
                        Logger.Log("OpenRouter refinement output rejected by safety guard; using original transcription.");
                    }

                    return safeRefined;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OpenRouter Refinement Error: {ex.Message}");
            }

            return request.Text;
        }
    }
}
