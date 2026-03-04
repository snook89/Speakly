using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Speakly.Config;

namespace Speakly.Services
{
    /// <summary>
    /// Transcribes audio using OpenRouter's OpenAI-compatible /v1/audio/transcriptions endpoint.
    /// Supports any whisper-class model available on OpenRouter (e.g. openai/whisper-large-v3).
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
                model = "openai/whisper-large-v3";

            try
            {
                int sampleRate = ConfigManager.Config.SampleRate;
                int channels   = ConfigManager.Config.Channels;
                byte[] wavBytes = CreateWavHeader(_audioBuffer.ToArray(), sampleRate, channels);

                using var content = new MultipartFormDataContent();

                var fileContent = new ByteArrayContent(wavBytes);
                fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("audio/wav");
                content.Add(fileContent, "file", "audio.wav");
                content.Add(new StringContent(model), "model");

                var configuredLanguage = ConfigManager.Config.Language?.Trim();
                if (string.Equals(configuredLanguage, "layout", StringComparison.OrdinalIgnoreCase))
                {
                    content.Add(new StringContent(InputLanguageResolver.ResolveCurrentLanguageCode("en")), "language");
                }
                else if (!string.IsNullOrWhiteSpace(configuredLanguage) &&
                         !string.Equals(configuredLanguage, "auto", StringComparison.OrdinalIgnoreCase))
                {
                    content.Add(new StringContent(configuredLanguage), "language");
                }

                var dictionaryPrompt = PersonalDictionaryService.BuildSttHintPrompt(ConfigManager.Config, maxTerms: 40);
                if (!string.IsNullOrWhiteSpace(dictionaryPrompt))
                {
                    content.Add(new StringContent(dictionaryPrompt), "prompt");
                }

                var request = new HttpRequestMessage(HttpMethod.Post,
                    "https://openrouter.ai/api/v1/audio/transcriptions")
                {
                    Content = content
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
                    using var doc = JsonDocument.Parse(responseString);
                    var text = doc.RootElement.GetProperty("text").GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                        TranscriptionReceived?.Invoke(this, new TranscriptionEventArgs(text, true));
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
