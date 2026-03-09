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

        public sealed record ElevenLabsSubscriptionInfo(
            bool Success,
            string StatusMessage,
            string Plan,
            long? Used,
            long? Limit)
        {
            public long? Remaining => Used.HasValue && Limit.HasValue
                ? Math.Max(0, Limit.Value - Used.Value)
                : null;
        }

        public sealed record ProviderBalanceCardInfo(
            bool Supported,
            bool Success,
            string Badge,
            string PrimaryLabel,
            string PrimaryValue,
            string SecondaryLabel,
            string SecondaryValue,
            string StatusMessage);

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

        public static async Task<ElevenLabsSubscriptionInfo> GetElevenLabsSubscriptionAsync(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return new ElevenLabsSubscriptionInfo(false, "Add an ElevenLabs API key to load balance.", "Unavailable", null, null);
            }

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "https://api.elevenlabs.io/v1/user/subscription");
                request.Headers.Add("xi-api-key", apiKey);

                var response = await _client.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    return ParseElevenLabsSubscriptionResponse(body);
                }

                var error = ExtractApiErrorMessage(body);
                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden ||
                    response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    return new ElevenLabsSubscriptionInfo(
                        false,
                        $"Balance unavailable for this API key scope ({(int)response.StatusCode}).",
                        "Unavailable",
                        null,
                        null);
                }

                return new ElevenLabsSubscriptionInfo(
                    false,
                    $"Balance request failed: {(int)response.StatusCode} ({error})",
                    "Unavailable",
                    null,
                    null);
            }
            catch (Exception ex)
            {
                return new ElevenLabsSubscriptionInfo(
                    false,
                    $"Balance request failed: {ex.Message}",
                    "Unavailable",
                    null,
                    null);
            }
        }

        public static async Task<ProviderBalanceCardInfo> GetElevenLabsBalanceCardAsync(string apiKey)
        {
            var info = await GetElevenLabsSubscriptionAsync(apiKey);
            return new ProviderBalanceCardInfo(
                Supported: true,
                Success: info.Success,
                Badge: string.IsNullOrWhiteSpace(info.Plan) ? "Unavailable" : info.Plan,
                PrimaryLabel: "Total",
                PrimaryValue: FormatCredits(info.Limit),
                SecondaryLabel: "Remaining",
                SecondaryValue: FormatCredits(info.Remaining),
                StatusMessage: info.StatusMessage);
        }

        public static async Task<ProviderBalanceCardInfo> GetOpenRouterBalanceCardAsync(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return new ProviderBalanceCardInfo(true, false, "Unavailable", "Total", "N/A", "Remaining", "N/A", "Add an OpenRouter API key to load balance.");
            }

            try
            {
                using var creditsRequest = new HttpRequestMessage(HttpMethod.Get, "https://openrouter.ai/api/v1/credits");
                creditsRequest.Headers.Add("Authorization", $"Bearer {apiKey}");
                creditsRequest.Headers.TryAddWithoutValidation("HTTP-Referer", "https://speakly.app");
                creditsRequest.Headers.TryAddWithoutValidation("X-Title", "Speakly");

                var creditsResponse = await _client.SendAsync(creditsRequest);
                var creditsBody = await creditsResponse.Content.ReadAsStringAsync();
                if (creditsResponse.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(creditsBody);
                    var data = doc.RootElement.GetProperty("data");
                    var totalCredits = GetDecimal(data, "total_credits");
                    var totalUsage = GetDecimal(data, "total_usage");
                    var remaining = totalCredits.HasValue && totalUsage.HasValue
                        ? Math.Max(0, totalCredits.Value - totalUsage.Value)
                        : (decimal?)null;

                    return new ProviderBalanceCardInfo(
                        Supported: true,
                        Success: true,
                        Badge: "OpenRouter",
                        PrimaryLabel: "Total",
                        PrimaryValue: FormatCurrency(totalCredits),
                        SecondaryLabel: "Remaining",
                        SecondaryValue: FormatCurrency(remaining),
                        StatusMessage: "Balance loaded.");
                }

                using var keyRequest = new HttpRequestMessage(HttpMethod.Get, "https://openrouter.ai/api/v1/key");
                keyRequest.Headers.Add("Authorization", $"Bearer {apiKey}");
                keyRequest.Headers.TryAddWithoutValidation("HTTP-Referer", "https://speakly.app");
                keyRequest.Headers.TryAddWithoutValidation("X-Title", "Speakly");

                var keyResponse = await _client.SendAsync(keyRequest);
                var keyBody = await keyResponse.Content.ReadAsStringAsync();
                if (keyResponse.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(keyBody);
                    var data = doc.RootElement.GetProperty("data");
                    var label = GetString(data, "label") ?? "Key";
                    var limit = GetDecimal(data, "limit");
                    var remaining = GetDecimal(data, "limit_remaining");

                    return new ProviderBalanceCardInfo(
                        Supported: true,
                        Success: true,
                        Badge: label,
                        PrimaryLabel: "Limit",
                        PrimaryValue: limit.HasValue ? FormatCurrency(limit) : "Unlimited",
                        SecondaryLabel: "Remaining",
                        SecondaryValue: FormatCurrency(remaining),
                        StatusMessage: "Key balance loaded.");
                }

                return new ProviderBalanceCardInfo(
                    Supported: true,
                    Success: false,
                    Badge: "Unavailable",
                    PrimaryLabel: "Total",
                    PrimaryValue: "N/A",
                    SecondaryLabel: "Remaining",
                    SecondaryValue: "N/A",
                    StatusMessage: $"Balance request failed: {(int)creditsResponse.StatusCode} ({ExtractApiErrorMessage(creditsBody)})");
            }
            catch (Exception ex)
            {
                return new ProviderBalanceCardInfo(true, false, "Unavailable", "Total", "N/A", "Remaining", "N/A", $"Balance request failed: {ex.Message}");
            }
        }

        public static async Task<ProviderBalanceCardInfo> GetDeepgramBalanceCardAsync(string apiKey, string? baseUrl = null)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return new ProviderBalanceCardInfo(true, false, "Unavailable", "Total", "N/A", "Balances", "N/A", "Add a Deepgram API key to load balance.");
            }

            try
            {
                var normalizedBase = NormalizeHttpBaseUrl(baseUrl, "https://api.deepgram.com");
                using var projectsRequest = new HttpRequestMessage(HttpMethod.Get, $"{normalizedBase}/v1/projects");
                projectsRequest.Headers.Add("Authorization", $"Token {apiKey}");
                var projectsResponse = await _client.SendAsync(projectsRequest);
                var projectsBody = await projectsResponse.Content.ReadAsStringAsync();
                if (!projectsResponse.IsSuccessStatusCode)
                {
                    return new ProviderBalanceCardInfo(true, false, "Unavailable", "Total", "N/A", "Balances", "N/A", $"Balance request failed: {(int)projectsResponse.StatusCode} ({ExtractApiErrorMessage(projectsBody)})");
                }

                using var projectsDoc = JsonDocument.Parse(projectsBody);
                if (!projectsDoc.RootElement.TryGetProperty("projects", out var projects) ||
                    projects.ValueKind != JsonValueKind.Array ||
                    projects.GetArrayLength() == 0)
                {
                    return new ProviderBalanceCardInfo(true, false, "Unavailable", "Total", "N/A", "Balances", "N/A", "No Deepgram projects were returned for this key.");
                }

                var project = projects[0];
                var projectId = GetString(project, "project_id");
                var projectName = GetString(project, "name") ?? "Project";
                if (string.IsNullOrWhiteSpace(projectId))
                {
                    return new ProviderBalanceCardInfo(true, false, projectName, "Total", "N/A", "Balances", "N/A", "Deepgram project response did not include a project ID.");
                }

                using var balancesRequest = new HttpRequestMessage(HttpMethod.Get, $"{normalizedBase}/v1/projects/{projectId}/balances");
                balancesRequest.Headers.Add("Authorization", $"Token {apiKey}");
                var balancesResponse = await _client.SendAsync(balancesRequest);
                var balancesBody = await balancesResponse.Content.ReadAsStringAsync();
                if (!balancesResponse.IsSuccessStatusCode)
                {
                    return new ProviderBalanceCardInfo(true, false, projectName, "Total", "N/A", "Balances", "N/A", $"Balance request failed: {(int)balancesResponse.StatusCode} ({ExtractApiErrorMessage(balancesBody)})");
                }

                using var balancesDoc = JsonDocument.Parse(balancesBody);
                if (!balancesDoc.RootElement.TryGetProperty("balances", out var balances) ||
                    balances.ValueKind != JsonValueKind.Array)
                {
                    return new ProviderBalanceCardInfo(true, false, projectName, "Total", "N/A", "Balances", "N/A", "Deepgram balance response did not include a balances list.");
                }

                decimal total = 0;
                int count = 0;
                string units = "usd";
                foreach (var balance in balances.EnumerateArray())
                {
                    var amount = GetDecimal(balance, "amount");
                    if (amount.HasValue)
                    {
                        total += amount.Value;
                    }

                    units = GetString(balance, "units") ?? units;
                    count++;
                }

                return new ProviderBalanceCardInfo(
                    Supported: true,
                    Success: true,
                    Badge: projectName,
                    PrimaryLabel: "Total",
                    PrimaryValue: FormatAmount(total, units),
                    SecondaryLabel: "Balances",
                    SecondaryValue: count.ToString("N0"),
                    StatusMessage: count > 0 ? "Balance loaded." : "No active Deepgram balances were returned.");
            }
            catch (Exception ex)
            {
                return new ProviderBalanceCardInfo(true, false, "Unavailable", "Total", "N/A", "Balances", "N/A", $"Balance request failed: {ex.Message}");
            }
        }

        internal static ElevenLabsSubscriptionInfo ParseElevenLabsSubscriptionResponse(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return new ElevenLabsSubscriptionInfo(false, "Balance endpoint returned an empty response.", "Unavailable", null, null);
            }

            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                var plan = GetString(root, "tier")
                    ?? GetString(root, "plan_name")
                    ?? GetString(root, "subscription_tier")
                    ?? "Unknown";

                var used = GetInt64(root, "character_count")
                    ?? GetInt64(root, "used_credits")
                    ?? GetInt64(root, "credits_used");

                var limit = GetInt64(root, "character_limit")
                    ?? GetInt64(root, "credit_limit")
                    ?? GetInt64(root, "credits_limit");

                var status = used.HasValue && limit.HasValue
                    ? "Balance loaded."
                    : "Subscription loaded. Balance totals were not returned by this key.";

                return new ElevenLabsSubscriptionInfo(true, status, plan, used, limit);
            }
            catch (Exception ex)
            {
                return new ElevenLabsSubscriptionInfo(false, $"Could not parse ElevenLabs subscription data: {ex.Message}", "Unavailable", null, null);
            }
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

        private static string? GetString(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var result = value.GetString();
            return string.IsNullOrWhiteSpace(result) ? null : result.Trim();
        }

        private static long? GetInt64(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var numericValue))
            {
                return numericValue;
            }

            if (value.ValueKind == JsonValueKind.String &&
                long.TryParse(value.GetString(), out var parsedValue))
            {
                return parsedValue;
            }

            return null;
        }

        private static decimal? GetDecimal(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var numericValue))
            {
                return numericValue;
            }

            if (value.ValueKind == JsonValueKind.String &&
                decimal.TryParse(value.GetString(), out var parsedValue))
            {
                return parsedValue;
            }

            return null;
        }

        private static string FormatCredits(long? amount) =>
            amount.HasValue ? $"{amount.Value:N0} credits" : "N/A";

        private static string FormatCurrency(decimal? amount) =>
            amount.HasValue ? $"${amount.Value:N2}" : "N/A";

        private static string FormatAmount(decimal amount, string? units)
        {
            var normalizedUnits = units?.Trim().ToLowerInvariant();
            if (normalizedUnits == "usd")
            {
                return $"${amount:N2}";
            }

            if (string.IsNullOrWhiteSpace(normalizedUnits))
            {
                return amount.ToString("N2");
            }

            return $"{amount:N2} {normalizedUnits.ToUpperInvariant()}";
        }
    }
}
