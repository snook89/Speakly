using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Speakly.Config;

namespace Speakly.Services
{
    /// <summary>
    /// Transcribes audio via OpenRouter's chat completions endpoint using input_audio content.
    /// Supports models that accept audio input and produce text output.
    /// </summary>
    public class OpenRouterTranscriber : ITranscriber
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private MemoryStream? _audioBuffer;

        public event EventHandler<TranscriptionEventArgs>? TranscriptionReceived;
        public event EventHandler<string>? ErrorReceived;

        public bool IsConnected { get; private set; }

        public OpenRouterTranscriber()
        {
            _httpClient.Timeout = TimeSpan.FromSeconds(60);
        }

        public Task ConnectAsync()
        {
            _audioBuffer = new MemoryStream();
            IsConnected = true;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync()
        {
            IsConnected = false;
            _audioBuffer?.Dispose();
            _audioBuffer = null;
            return Task.CompletedTask;
        }

        public Task SendAudioAsync(byte[] buffer)
        {
            if (!IsConnected || _audioBuffer == null) return Task.CompletedTask;
            _audioBuffer.Write(buffer, 0, buffer.Length);
            return Task.CompletedTask;
        }

        public async Task FinishStreamAsync()
        {
            if (!IsConnected || _audioBuffer == null || _audioBuffer.Length == 0) return;

            var apiKey = ConfigManager.Config.OpenRouterApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                ErrorReceived?.Invoke(this, "OpenRouter API key is not configured.");
                return;
            }

            var model = ConfigManager.Config.OpenRouterSttModel?.Trim();
            if (string.IsNullOrWhiteSpace(model))
                model = "openai/gpt-audio-mini";

            try
            {
                int sampleRate = ConfigManager.Config.SampleRate;
                int channels   = ConfigManager.Config.Channels;
                byte[] wavBytes = CreateWavHeader(_audioBuffer.ToArray(), sampleRate, channels);

                var configuredLanguage = ConfigManager.Config.Language?.Trim();
                var resolvedLanguage = string.Empty;
                if (string.Equals(configuredLanguage, "layout", StringComparison.OrdinalIgnoreCase))
                    resolvedLanguage = InputLanguageResolver.ResolveCurrentLanguageCode("en");
                else if (!string.IsNullOrWhiteSpace(configuredLanguage) &&
                         !string.Equals(configuredLanguage, "auto", StringComparison.OrdinalIgnoreCase))
                    resolvedLanguage = configuredLanguage;

                var dictionaryPrompt = PersonalDictionaryService.BuildSttHintPrompt(ConfigManager.Config, maxTerms: 40);
                var instructionBuilder = new StringBuilder("Transcribe the provided audio into plain text. Return only the transcript.");
                if (!string.IsNullOrWhiteSpace(resolvedLanguage))
                    instructionBuilder.Append(" Expected language: ").Append(resolvedLanguage).Append('.');
                if (!string.IsNullOrWhiteSpace(dictionaryPrompt))
                    instructionBuilder.Append(" Prefer these terms when they are spoken exactly: ").Append(dictionaryPrompt);

                var payload = new
                {
                    model,
                    messages = new object[]
                    {
                        new
                        {
                            role = "user",
                            content = new object[]
                            {
                                new { type = "text", text = instructionBuilder.ToString() },
                                new
                                {
                                    type = "input_audio",
                                    input_audio = new
                                    {
                                        data = Convert.ToBase64String(wavBytes),
                                        format = "wav"
                                    }
                                }
                            }
                        }
                    },
                    temperature = 0
                };

                var request = new HttpRequestMessage(HttpMethod.Post,
                    "https://openrouter.ai/api/v1/chat/completions")
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
                };
                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", apiKey.Trim());
                // OpenRouter recommends including these headers
                request.Headers.TryAddWithoutValidation("HTTP-Referer", "https://speakly.app");
                request.Headers.TryAddWithoutValidation("X-Title", "Speakly");

                var response = await _httpClient.SendAsync(request);
                var responseString = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var text = ExtractTextFromChatCompletion(responseString);
                    if (!string.IsNullOrWhiteSpace(text))
                        TranscriptionReceived?.Invoke(this, new TranscriptionEventArgs(text, true));
                    else
                        ErrorReceived?.Invoke(this, "OpenRouter Transcription Failed: empty response text.");
                }
                else
                {
                    ErrorReceived?.Invoke(this,
                        $"OpenRouter Transcription Error ({response.StatusCode}): {responseString}");
                }
            }
            catch (Exception ex)
            {
                ErrorReceived?.Invoke(this, $"OpenRouter Transcription Failed: {ex.Message}");
            }
            finally
            {
                _audioBuffer.SetLength(0);
            }
        }

        private static string ExtractTextFromChatCompletion(string responseJson)
        {
            try
            {
                using var doc = JsonDocument.Parse(responseJson);
                var root = doc.RootElement;

                if (!root.TryGetProperty("choices", out var choices) ||
                    choices.ValueKind != JsonValueKind.Array ||
                    choices.GetArrayLength() == 0)
                {
                    return string.Empty;
                }

                var first = choices[0];
                if (!first.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object)
                {
                    return string.Empty;
                }

                if (!message.TryGetProperty("content", out var content))
                {
                    return string.Empty;
                }

                if (content.ValueKind == JsonValueKind.String)
                {
                    return content.GetString()?.Trim() ?? string.Empty;
                }

                if (content.ValueKind == JsonValueKind.Array)
                {
                    var builder = new StringBuilder();
                    foreach (var part in content.EnumerateArray())
                    {
                        if (part.ValueKind == JsonValueKind.String)
                        {
                            builder.Append(part.GetString());
                            continue;
                        }

                        if (part.ValueKind != JsonValueKind.Object) continue;
                        if (part.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.String)
                        {
                            builder.Append(textProp.GetString());
                        }
                    }

                    return builder.ToString().Trim();
                }
            }
            catch
            {
                // Ignore parse errors and return empty.
            }

            return string.Empty;
        }

        private byte[] CreateWavHeader(byte[] rawPcmData, int sampleRate, int channels)
        {
            using var ms     = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            writer.Write("RIFF".ToCharArray());
            writer.Write(36 + rawPcmData.Length);
            writer.Write("WAVE".ToCharArray());
            writer.Write("fmt ".ToCharArray());
            writer.Write(16);
            writer.Write((short)1);            // PCM
            writer.Write((short)channels);
            writer.Write(sampleRate);
            writer.Write(sampleRate * channels * 2);
            writer.Write((short)(channels * 2));
            writer.Write((short)16);           // 16-bit
            writer.Write("data".ToCharArray());
            writer.Write(rawPcmData.Length);
            writer.Write(rawPcmData);

            return ms.ToArray();
        }

        public Task WaitForFinalResultAsync() => Task.CompletedTask;

        public void Dispose()
        {
            _audioBuffer?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
