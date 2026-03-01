using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Speakly.Config;

namespace Speakly.Services
{
    public class OpenAITranscriber : ITranscriber
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private MemoryStream? _audioBuffer;

        public event EventHandler<TranscriptionEventArgs>? TranscriptionReceived;
        public event EventHandler<string>? ErrorReceived;

        public bool IsConnected { get; private set; }

        public OpenAITranscriber()
        {
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
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

            var apiKey = ConfigManager.Config.OpenAIApiKey;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                ErrorReceived?.Invoke(this, "OpenAI API key is not configured.");
                return;
            }

            try
            {
                byte[] wavBytes = CreateWavHeader(_audioBuffer.ToArray());

                using var content = new MultipartFormDataContent();
                
                var fileContent = new ByteArrayContent(wavBytes);
                fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("audio/wav");
                content.Add(fileContent, "file", "audio.wav");
                
                content.Add(new StringContent(ConfigManager.Config.OpenAISttModel), "model");
                content.Add(new StringContent("en"), "language");
                
                var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/audio/transcriptions")
                {
                    Content = content
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                var response = await _httpClient.SendAsync(request);
                var responseString = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(responseString);
                    var text = doc.RootElement.GetProperty("text").GetString();
                    
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        TranscriptionReceived?.Invoke(this, new TranscriptionEventArgs(text, true));
                    }
                }
                else
                {
                    ErrorReceived?.Invoke(this, $"OpenAI API Error ({response.StatusCode}): {responseString}");
                }
            }
            catch (Exception ex)
            {
                ErrorReceived?.Invoke(this, $"Transcription Failed: {ex.Message}");
            }
            finally
            {
                _audioBuffer.SetLength(0);
            }
        }
        
        private byte[] CreateWavHeader(byte[] rawPcmData)
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            writer.Write("RIFF".ToCharArray());
            writer.Write(36 + rawPcmData.Length);
            writer.Write("WAVE".ToCharArray());
            writer.Write("fmt ".ToCharArray());
            writer.Write(16);
            writer.Write((short)1); // PCM
            writer.Write((short)1); // Channels (Mono)
            writer.Write(16000);    // Sample Rate
            writer.Write(16000 * 1 * 2); // Byte Rate
            writer.Write((short)(1 * 2)); // Block Align
            writer.Write((short)16); // Bits Per Sample
            writer.Write("data".ToCharArray());
            writer.Write(rawPcmData.Length);
            writer.Write(rawPcmData);

            return ms.ToArray();
        }

        public Task WaitForFinalResultAsync()
        {
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _audioBuffer?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
