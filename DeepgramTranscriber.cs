using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Speakly.Config;

namespace Speakly.Services
{
    public class DeepgramTranscriber : ITranscriber
    {
        private ClientWebSocket? _webSocket;
        private CancellationTokenSource? _cts;
        private readonly ConcurrentQueue<byte[]> _connectionBuffer = new();
        private TaskCompletionSource? _finalResultTcs;
        private bool _isFinishing = false;
        private const string Version = "1.5.0-HeaderAuthFix";

        public event EventHandler<TranscriptionEventArgs>? TranscriptionReceived;
        public event EventHandler<string>? ErrorReceived;

        public bool IsConnected => _webSocket?.State == WebSocketState.Open;

        public async Task ConnectAsync()
        {
            if (IsConnected) return;

            var config = ConfigManager.Config;
            var apiKey = config.DeepgramApiKey?.Trim();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                ErrorReceived?.Invoke(this, "Deepgram API key is not configured.");
                return;
            }

            _webSocket = new ClientWebSocket();

            // Build URL without auth — auth goes in the Authorization header
            string url = string.Format(CultureInfo.InvariantCulture,
                "wss://api.deepgram.com/v1/listen?encoding=linear16&sample_rate={0}&channels={1}&model={2}&language={3}&interim_results=true&smart_format=true&endpointing=300",
                config.SampleRate,
                config.Channels,
                config.DeepgramModel,
                config.Language);

            _cts = new CancellationTokenSource();
            _isFinishing = false;
            _finalResultTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            try
            {
                Logger.Log($"Connecting to Deepgram ({Version}): {url.Replace(apiKey, "REDACTED")}");

                // Create a fresh HttpClient per connection so DefaultRequestHeaders are clean.
                // Passing it as HttpMessageInvoker is the reliable way to send the Authorization
                // header during the WebSocket upgrade in .NET 9.
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Token", apiKey);

                await _webSocket.ConnectAsync(new Uri(url), httpClient, _cts.Token);
                
                // Flush buffer
                while (_connectionBuffer.TryDequeue(out var bufferedData))
                {
                    await _webSocket.SendAsync(new ArraySegment<byte>(bufferedData), WebSocketMessageType.Binary, true, _cts.Token);
                }
                
                _ = Task.Run(ReceiveLoopAsync, _cts.Token);
                Logger.Log("Deepgram connected successfully.");
            }
            catch (Exception ex)
            {
                string diagnosticInfo = await DiagnosticProbeAsync(apiKey);
                string errorMessage = !string.IsNullOrWhiteSpace(diagnosticInfo)
                    ? $"[v{Version}] Deepgram Error: {diagnosticInfo} (Original: {ex.Message})"
                    : $"[v{Version}] Failed to connect to Deepgram: {ex.Message}";

                ErrorReceived?.Invoke(this, errorMessage);
                await CleanupAsync();
            }
        }

private async Task<string> DiagnosticProbeAsync(string apiKey)
        {
            try
            {
                using var httpClient = new HttpClient();
                using var authCheck = new HttpRequestMessage(HttpMethod.Get, "https://api.deepgram.com/v1/projects");
                authCheck.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Token", apiKey);

                using var authResponse = await httpClient.SendAsync(authCheck);
                if (authResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    return "API Key Rejected (401 Unauthorized). Please check your key in Settings.";
                if (authResponse.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    return "API Key Forbidden (403). Your key may be restricted or expired.";

                // Auth is fine — the WebSocket connect itself failed for another reason
                return string.Empty;
            }
            catch (Exception ex)
            {
                return $"Diagnostic failed: {ex.Message}";
            }
        }

        public async Task DisconnectAsync()
        {
            if (_webSocket != null && _webSocket.State == WebSocketState.Open)
            {
                try
                {
                    Logger.Log("Closing Deepgram WebSocket connection.");
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client initiated disconnect", CancellationToken.None);
                }
                catch (Exception ex) { Logger.LogException("DisconnectAsync", ex); }
            }
            await CleanupAsync();
        }

        public async Task SendAudioAsync(byte[] buffer)
        {
            if (_webSocket == null || _webSocket.State == WebSocketState.Connecting)
            {
                _connectionBuffer.Enqueue(buffer);
                return;
            }

            if (!IsConnected || _cts == null) return;

            try
            {
                await _webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Binary, true, _cts.Token);
            }
            catch (Exception ex)
            {
                ErrorReceived?.Invoke(this, $"Failed to send audio to Deepgram: {ex.Message}");
                await CleanupAsync();
            }
        }

        public async Task FinishStreamAsync()
        {
            if (!IsConnected || _webSocket == null || _cts == null) 
            {
                _finalResultTcs?.TrySetResult();
                return;
            }

            try
            {
                _isFinishing = true;
                Logger.Log("Sending CloseStream to Deepgram.");
                var closeMessage = Encoding.UTF8.GetBytes("{\"type\":\"CloseStream\"}");
                await _webSocket.SendAsync(new ArraySegment<byte>(closeMessage), WebSocketMessageType.Text, true, _cts.Token);
            }
            catch (Exception ex)
            {
                ErrorReceived?.Invoke(this, $"Failed to finalize stream: {ex.Message}");
                await CleanupAsync();
            }
        }

        public async Task WaitForFinalResultAsync()
        {
            if (_finalResultTcs != null)
            {
                // Timeout after 5 seconds to prevent getting stuck
                var timeoutTask = Task.Delay(5000);
                var completedTask = await Task.WhenAny(_finalResultTcs.Task, timeoutTask);
                if (completedTask == timeoutTask)
                {
                    Logger.Log("WARNING: WaitForFinalResultAsync timed out.");
                }
            }
        }

        private async Task ReceiveLoopAsync()
        {
            var buffer = new byte[8192];

            while (_webSocket?.State == WebSocketState.Open && _cts != null && !_cts.Token.IsCancellationRequested)
            {
                try
                {
                    using var ms = new MemoryStream();
                    WebSocketReceiveResult result;

                    do
                    {
                        result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            _finalResultTcs?.TrySetResult();
                            await CleanupAsync();
                            return;
                        }

                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var jsonStr = Encoding.UTF8.GetString(ms.ToArray());
                        ProcessDeepgramResponse(jsonStr);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    ErrorReceived?.Invoke(this, $"Deepgram Receive Loop Error: {ex.Message}");
                    _finalResultTcs?.TrySetResult();
                    break;
                }
            }
            _finalResultTcs?.TrySetResult();
        }

        private void ProcessDeepgramResponse(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                
                if (root.TryGetProperty("type", out var typeElement) && typeElement.GetString() == "Results")
                {
                    bool isFinal = root.GetProperty("is_final").GetBoolean();
                    var channel = root.GetProperty("channel");
                    var alternatives = channel.GetProperty("alternatives");
                    
                    if (alternatives.GetArrayLength() > 0)
                    {
                        var bestAlternative = alternatives[0];
                        string? transcript = bestAlternative.GetProperty("transcript").GetString();

                        if (!string.IsNullOrWhiteSpace(transcript))
                        {
                            Logger.Log($"Deepgram Transcription Arrived (isFinal={isFinal}): {transcript}");
                            TranscriptionReceived?.Invoke(this, new TranscriptionEventArgs(transcript, isFinal));
                        }
                    }

                    if (isFinal && _isFinishing)
                    {
                        // Signal that we've received the last final result after closing stream
                        _finalResultTcs?.TrySetResult();
                    }
                }
                else if (root.TryGetProperty("type", out var type) && type.GetString() == "Metadata")
                {
                    // Metadata usually arrives at the very end after CloseStream
                    _finalResultTcs?.TrySetResult();
                }
            }
            catch (Exception ex)
            {
                 Logger.LogException("ProcessDeepgramResponse", ex);
                 ErrorReceived?.Invoke(this, $"Deepgram Parser Error: {ex.Message}");
            }
        }

        private async Task CleanupAsync()
        {
            _finalResultTcs?.TrySetResult();
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }

            if (_webSocket != null)
            {
                _webSocket.Dispose();
                _webSocket = null;
            }
            
            await Task.CompletedTask;
        }

        public void Dispose()
        {
            _ = CleanupAsync();
            GC.SuppressFinalize(this);
        }
    }
}
