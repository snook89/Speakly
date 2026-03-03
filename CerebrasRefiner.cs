using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Speakly.Config;

namespace Speakly.Services
{
    public class CerebrasRefiner : ITextRefiner
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        public async Task<string> RefineTextAsync(string text, string prompt)
        {
            var config = ConfigManager.Config;
            var apiKey = config.CerebrasApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return text;
            }

            var maxRetries = Math.Clamp(config.CerebrasMaxRetries, 0, 6);
            var maxAttempts = 1 + maxRetries;
            var timeoutSeconds = Math.Clamp(config.CerebrasTimeoutSeconds, 10, 300);
            var baseDelayMs = Math.Clamp(config.CerebrasRetryBaseDelayMs, 100, 5000);
            var maxTokens = Math.Clamp(config.CerebrasMaxCompletionTokens, 16, 65536);

            var safePrompt = RefinementSafety.BuildSafeSystemPrompt(prompt);
            var userMessage = RefinementSafety.BuildRefinementUserMessage(text);
            var requestBody = new
            {
                model = config.CerebrasRefinementModel,
                messages = new[]
                {
                    new { role = "system", content = safePrompt },
                    new { role = "user", content = userMessage }
                },
                temperature = 0.3,
                max_completion_tokens = maxTokens
            };

            Exception? lastException = null;
            string? lastErrorSummary = null;

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.cerebras.ai/v1/chat/completions")
                {
                    Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                if (!string.IsNullOrWhiteSpace(config.CerebrasVersionPatch))
                {
                    request.Headers.TryAddWithoutValidation("X-Cerebras-Version-Patch", config.CerebrasVersionPatch.Trim());
                }

                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                try
                {
                    var response = await _httpClient.SendAsync(request, timeoutCts.Token);
                    var responseBody = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        var refinedText = ExtractRefinedText(responseBody);
                        if (string.IsNullOrWhiteSpace(refinedText))
                        {
                            lastErrorSummary = "empty refinement content";
                            Logger.Log("Cerebras refinement returned empty content; using original transcription.");
                            break;
                        }

                        return RefinementSafety.CoerceToEditOnlyOutput(text, refinedText);
                    }

                    var apiMessage = ExtractApiErrorMessage(responseBody);
                    lastErrorSummary = $"HTTP {(int)response.StatusCode} ({apiMessage})";

                    if (IsTransientStatus(response.StatusCode) && attempt < maxAttempts)
                    {
                        var retryDelay = ResolveRetryDelay(response.Headers, baseDelayMs, attempt);
                        Logger.Log($"Cerebras refinement transient failure {lastErrorSummary}; retrying attempt {attempt + 1}/{maxAttempts} after {retryDelay.TotalMilliseconds:0}ms.");
                        await Task.Delay(retryDelay);
                        continue;
                    }

                    Logger.Log($"Cerebras refinement failed: {lastErrorSummary}. Falling back to original text.");
                    break;
                }
                catch (OperationCanceledException ocex) when (timeoutCts.IsCancellationRequested)
                {
                    lastException = ocex;
                    lastErrorSummary = $"timeout after {timeoutSeconds}s";
                    if (attempt < maxAttempts)
                    {
                        var retryDelay = ResolveRetryDelay(null, baseDelayMs, attempt);
                        Logger.Log($"Cerebras refinement timeout on attempt {attempt}/{maxAttempts}; retrying after {retryDelay.TotalMilliseconds:0}ms.");
                        await Task.Delay(retryDelay);
                        continue;
                    }
                    break;
                }
                catch (HttpRequestException hre)
                {
                    lastException = hre;
                    lastErrorSummary = hre.Message;
                    if (attempt < maxAttempts)
                    {
                        var retryDelay = ResolveRetryDelay(null, baseDelayMs, attempt);
                        Logger.Log($"Cerebras refinement network error on attempt {attempt}/{maxAttempts}: {hre.Message}. Retrying after {retryDelay.TotalMilliseconds:0}ms.");
                        await Task.Delay(retryDelay);
                        continue;
                    }
                    break;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    lastErrorSummary = ex.Message;
                    Logger.LogException("CerebrasRefiner.RefineTextAsync", ex);
                    break;
                }
            }

            if (lastException != null)
            {
                Logger.Log($"Cerebras refinement fallback reason: {lastErrorSummary ?? lastException.Message}");
            }
            else if (!string.IsNullOrWhiteSpace(lastErrorSummary))
            {
                Logger.Log($"Cerebras refinement fallback reason: {lastErrorSummary}");
            }

            return text;
        }

        private static string? ExtractRefinedText(string responseBody)
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
            {
                return null;
            }

            var first = choices[0];
            if (!first.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!message.TryGetProperty("content", out var content))
            {
                return null;
            }

            if (content.ValueKind == JsonValueKind.String)
            {
                return content.GetString();
            }

            if (content.ValueKind == JsonValueKind.Array)
            {
                var parts = new List<string>();
                foreach (var item in content.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var part = item.GetString();
                        if (!string.IsNullOrWhiteSpace(part)) parts.Add(part.Trim());
                        continue;
                    }

                    if (item.ValueKind == JsonValueKind.Object &&
                        item.TryGetProperty("text", out var textPart) &&
                        textPart.ValueKind == JsonValueKind.String)
                    {
                        var part = textPart.GetString();
                        if (!string.IsNullOrWhiteSpace(part)) parts.Add(part.Trim());
                    }
                }

                return parts.Count == 0 ? null : string.Join(" ", parts);
            }

            return content.ToString();
        }

        private static bool IsTransientStatus(HttpStatusCode statusCode)
        {
            return statusCode == HttpStatusCode.RequestTimeout
                   || statusCode == (HttpStatusCode)429
                   || statusCode == HttpStatusCode.InternalServerError
                   || statusCode == HttpStatusCode.BadGateway
                   || statusCode == HttpStatusCode.ServiceUnavailable
                   || statusCode == HttpStatusCode.GatewayTimeout;
        }

        private static TimeSpan ResolveRetryDelay(HttpResponseHeaders? headers, int baseDelayMs, int attempt)
        {
            if (headers != null && headers.TryGetValues("Retry-After", out var values))
            {
                var raw = values.FirstOrDefault()?.Trim();
                if (!string.IsNullOrWhiteSpace(raw))
                {
                    if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0)
                    {
                        return TimeSpan.FromSeconds(Math.Min(seconds, 30));
                    }

                    if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var retryAt))
                    {
                        var delta = retryAt - DateTimeOffset.UtcNow;
                        if (delta > TimeSpan.Zero)
                        {
                            return delta > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : delta;
                        }
                    }
                }
            }

            var exponentialMs = baseDelayMs * Math.Pow(2, Math.Max(0, attempt - 1));
            var jitterMs = Random.Shared.Next(0, 250);
            return TimeSpan.FromMilliseconds(Math.Min(exponentialMs + jitterMs, 10000));
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
