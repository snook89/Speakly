using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Speakly.Config;
using System.Linq;

namespace Speakly.Services
{
    public static class ApiTester
    {
        private static readonly HttpClient _client = new HttpClient();

        public static async Task<string> TestDeepgramAsync(string apiKey, string? baseUrl = null)
        {
            if (string.IsNullOrWhiteSpace(apiKey)) return "FAIL: No API Key provided";
            try
            {
                var normalizedBase = NormalizeHttpBaseUrl(baseUrl, "https://api.deepgram.com");
                var request = new HttpRequestMessage(HttpMethod.Get, $"{normalizedBase}/v1/projects");
                request.Headers.Add("Authorization", $"Token {apiKey}");
                var response = await _client.SendAsync(request);
                return response.IsSuccessStatusCode ? "OK: Connection Successful" : $"FAIL: {response.StatusCode}";
            }
            catch (Exception ex) { return $"FAIL: {ex.Message}"; }
        }

        private static string NormalizeHttpBaseUrl(string? baseUrl, string fallback)
        {
            var normalized = baseUrl?.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                normalized = fallback;
            }

            if (!normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
                !normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                normalized = "https://" + normalized;
            }

            return normalized.TrimEnd('/');
        }

        public static async Task<string> TestOpenAIAsync(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey)) return "FAIL: No API Key provided";
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
                request.Headers.Add("Authorization", $"Bearer {apiKey}");
                var response = await _client.SendAsync(request);
                return response.IsSuccessStatusCode ? "OK: Connection Successful" : $"FAIL: {response.StatusCode}";
            }
            catch (Exception ex) { return $"FAIL: {ex.Message}"; }
        }

        public static async Task<string> TestCerebrasAsync(string apiKey, string? preferredModel = null, int maxCompletionTokens = 64, string? versionPatch = null)
        {
            if (string.IsNullOrWhiteSpace(apiKey)) return "FAIL: No API Key provided";
            try
            {
                using var listRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.cerebras.ai/v1/models");
                listRequest.Headers.Add("Authorization", $"Bearer {apiKey}");
                if (!string.IsNullOrWhiteSpace(versionPatch))
                    listRequest.Headers.Add("X-Cerebras-Version-Patch", versionPatch.Trim());

                var listResponse = await _client.SendAsync(listRequest);
                var listBody = await listResponse.Content.ReadAsStringAsync();
                if (!listResponse.IsSuccessStatusCode)
                {
                    return $"FAIL: Model list {listResponse.StatusCode} ({ExtractApiErrorMessage(listBody)})";
                }

                var selectedModel = ResolveCerebrasTestModel(preferredModel, listBody);
                if (string.IsNullOrWhiteSpace(selectedModel))
                {
                    return "FAIL: Cerebras model list returned no usable model IDs.";
                }

                var payload = new
                {
                    model = selectedModel,
                    messages = new[]
                    {
                        new { role = "system", content = "Reply with exactly: OK" },
                        new { role = "user", content = "healthcheck" }
                    },
                    temperature = 0,
                    max_completion_tokens = Math.Clamp(maxCompletionTokens, 16, 1024)
                };

                using var invokeRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.cerebras.ai/v1/chat/completions");
                invokeRequest.Headers.Add("Authorization", $"Bearer {apiKey}");
                if (!string.IsNullOrWhiteSpace(versionPatch))
                    invokeRequest.Headers.Add("X-Cerebras-Version-Patch", versionPatch.Trim());
                invokeRequest.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                var invokeResponse = await _client.SendAsync(invokeRequest);
                var invokeBody = await invokeResponse.Content.ReadAsStringAsync();
                if (!invokeResponse.IsSuccessStatusCode)
                {
                    return $"FAIL: Chat {invokeResponse.StatusCode} ({ExtractApiErrorMessage(invokeBody)})";
                }

                return $"OK: Connection + chat completion successful ({selectedModel})";
            }
            catch (Exception ex) { return $"FAIL: {ex.Message}"; }
        }

        public static async Task<string> TestElevenLabsAsync(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey)) return "FAIL: No API Key provided";
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "https://api.elevenlabs.io/v1/models");
                request.Headers.Add("xi-api-key", apiKey);
                var response = await _client.SendAsync(request);
                if (response.IsSuccessStatusCode) return "OK: Connection Successful";

                var body = await response.Content.ReadAsStringAsync();
                return $"FAIL: {response.StatusCode} ({ExtractApiErrorMessage(body)})";
            }
            catch (Exception ex) { return $"FAIL: {ex.Message}"; }
        }

        public static async Task<string> TestOpenRouterAsync(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey)) return "FAIL: No API Key provided";
            try
            {
                var payload = new
                {
                    model = "openrouter/auto",
                    messages = new[]
                    {
                        new { role = "user", content = "ping" }
                    },
                    max_tokens = 1,
                    temperature = 0
                };

                var request = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions");
                request.Headers.Add("Authorization", $"Bearer {apiKey}");
                request.Headers.TryAddWithoutValidation("HTTP-Referer", "https://speakly.app");
                request.Headers.TryAddWithoutValidation("X-Title", "Speakly");
                request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                var response = await _client.SendAsync(request);
                if (response.IsSuccessStatusCode) return "OK: Connection Successful";

                var body = await response.Content.ReadAsStringAsync();
                return $"FAIL: {response.StatusCode} ({ExtractApiErrorMessage(body)})";
            }
            catch (Exception ex) { return $"FAIL: {ex.Message}"; }
        }

        private static string ResolveCerebrasTestModel(string? preferredModel, string modelsJson)
        {
            var normalizedPreferred = preferredModel?.Trim();
            if (string.IsNullOrWhiteSpace(modelsJson))
            {
                return normalizedPreferred ?? string.Empty;
            }

            try
            {
                using var document = JsonDocument.Parse(modelsJson);
                var root = document.RootElement;

                if (!root.TryGetProperty("data", out var dataArray) || dataArray.ValueKind != JsonValueKind.Array)
                {
                    return normalizedPreferred ?? string.Empty;
                }

                var modelIds = dataArray.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out _))
                    .Select(item => item.GetProperty("id").GetString())
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Select(id => id!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (!string.IsNullOrWhiteSpace(normalizedPreferred) &&
                    modelIds.Any(id => string.Equals(id, normalizedPreferred, StringComparison.OrdinalIgnoreCase)))
                {
                    return normalizedPreferred;
                }

                return modelIds.FirstOrDefault() ?? (normalizedPreferred ?? string.Empty);
            }
            catch
            {
                return normalizedPreferred ?? string.Empty;
            }
        }

        private static string ExtractApiErrorMessage(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return "empty response";
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (root.TryGetProperty("error", out var error))
                {
                    if (error.ValueKind == JsonValueKind.String)
                        return error.GetString() ?? "unknown error";
                    if (error.ValueKind == JsonValueKind.Object)
                    {
                        if (error.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String)
                            return msg.GetString() ?? "unknown error";
                        if (error.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String)
                            return code.GetString() ?? "unknown error";
                    }
                }

                if (root.TryGetProperty("message", out var directMessage) && directMessage.ValueKind == JsonValueKind.String)
                    return directMessage.GetString() ?? "unknown error";
            }
            catch
            {
                // ignore parse errors
            }

            var compact = body.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return compact.Length <= 180 ? compact : compact[..180] + "...";
        }
    }
}
