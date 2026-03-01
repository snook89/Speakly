using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Speakly.Config;

namespace Speakly.Services
{
    public class CerebrasRefiner : ITextRefiner
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        public CerebrasRefiner()
        {
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public async Task<string> RefineTextAsync(string text, string prompt)
        {
            var apiKey = ConfigManager.Config.CerebrasApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return text;
            }

            try
            {
                var requestBody = new
                {
                    model = ConfigManager.Config.CerebrasRefinementModel,
                    messages = new[]
                    {
                        new { role = "system", content = prompt },
                        new { role = "user", content = text }
                    },
                    temperature = 0.3
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.cerebras.ai/v1/chat/completions")
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

                    return refinedText?.Trim() ?? text;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Cerebras Refinement Error: {ex.Message}");
            }

            return text;
        }
    }
}
