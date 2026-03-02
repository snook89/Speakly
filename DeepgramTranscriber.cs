using System;
using System.Collections.Generic;
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
        private const string Version = "1.6.0-HandshakeDiagnostics";

        // Accumulates is_final=true segments until speech_final=true signals a natural
        // utterance boundary, or until the stream closes (Metadata flush).
        // This prevents each chunk of a long sentence from being refined/inserted separately.
        private readonly System.Text.StringBuilder _accumulator = new();

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
            _webSocket.Options.SetRequestHeader("Authorization", $"Token {apiKey}");
            _webSocket.Options.CollectHttpResponseDetails = true;

            string url = BuildDeepgramWebSocketUrl(config);

            _cts = new CancellationTokenSource();
            _finalResultTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            try
            {
                Logger.Log($"Connecting to Deepgram ({Version}): {url}");
                _accumulator.Clear(); // reset any leftovers from a previous session
                await _webSocket.ConnectAsync(new Uri(url), _cts.Token);
                
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
                string handshakeInfo = GetHandshakeFailureDetails();
                string diagnosticInfo = await DiagnosticProbeAsync(apiKey);
                var details = new List<string>();
                if (!string.IsNullOrWhiteSpace(handshakeInfo)) details.Add(handshakeInfo);
                if (!string.IsNullOrWhiteSpace(diagnosticInfo)) details.Add(diagnosticInfo);

                string errorMessage = details.Count > 0
                    ? $"[v{Version}] Deepgram Error: {string.Join(" | ", details)} (Original: {ex.Message})"
                    : $"[v{Version}] Failed to connect to Deepgram: {ex.Message}";

                ErrorReceived?.Invoke(this, errorMessage);
                await CleanupAsync();
            }
        }

        private static string BuildDeepgramWebSocketUrl(AppConfig config)
        {
            var selectedModel = string.IsNullOrWhiteSpace(config.DeepgramModel)
                ? "nova-3"
                : config.DeepgramModel.Trim();

            var query = new List<string>
            {
                "encoding=linear16",
                $"sample_rate={config.SampleRate.ToString(CultureInfo.InvariantCulture)}",
                $"channels={config.Channels.ToString(CultureInfo.InvariantCulture)}",
                $"model={Uri.EscapeDataString(selectedModel)}",
                "interim_results=true",
                "smart_format=true",
                "endpointing=300"
            };

            var resolvedLanguage = ResolveLanguageForStreaming(config.Language, selectedModel);
            if (!string.IsNullOrWhiteSpace(resolvedLanguage))
            {
                query.Add($"language={Uri.EscapeDataString(resolvedLanguage)}");
            }

            return $"wss://api.deepgram.com/v1/listen?{string.Join("&", query)}";
        }

        private static string ResolveLanguageForStreaming(string? configuredLanguage, string? model)
        {
            var value = configuredLanguage?.Trim();
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;

            if (string.Equals(value, "layout", StringComparison.OrdinalIgnoreCase))
            {
                return InputLanguageResolver.ResolveCurrentLanguageCode("en");
            }

            if (string.Equals(value, "auto", StringComparison.OrdinalIgnoreCase))
            {
                return SupportsMultilingualStreaming(model) ? "multi" : string.Empty;
            }

            return value.ToLowerInvariant();
        }

        private static bool SupportsMultilingualStreaming(string? model)
        {
            if (string.IsNullOrWhiteSpace(model)) return false;
            var normalizedModel = model.Trim().ToLowerInvariant();
            return normalizedModel.StartsWith("nova-3") || normalizedModel.StartsWith("nova-2");
        }

        private string GetHandshakeFailureDetails()
        {
            if (_webSocket == null) return string.Empty;

            var details = new List<string>();

            if ((int)_webSocket.HttpStatusCode > 0)
            {
                details.Add($"Handshake HTTP {(int)_webSocket.HttpStatusCode}");
            }

            var headers = _webSocket.HttpResponseHeaders;
            if (headers != null)
            {
                if (headers.TryGetValue("dg-error", out var dgErrors))
                {
                    details.Add($"dg-error: {string.Join(", ", dgErrors)}");
                }

                if (headers.TryGetValue("x-dg-request-id", out var requestIds))
                {
                    details.Add($"request-id: {string.Join(", ", requestIds)}");
                }
            }

            return string.Join("; ", details);
        }

private async Task<string> DiagnosticProbeAsync(string apiKey)
        {
            try
            {
                using var httpClient = new HttpClient();
                using var authCheck = new HttpRequestMessage(HttpMethod.Get, "https://api.deepgram.com/v1/auth/token");
                authCheck.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Token", apiKey);

                using var authResponse = await httpClient.SendAsync(authCheck);
                if (authResponse.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    return "API Key Rejected (401 Unauthorized). Please check your key in Settings.";
                if (authResponse.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    return "API Key Forbidden (403). Your key may be restricted or expired.";

                if (!authResponse.IsSuccessStatusCode)
                    return $"Auth probe failed with HTTP {(int)authResponse.StatusCode}.";

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
                    bool isFinal    = root.GetProperty("is_final").GetBoolean();
                    bool speechFinal = root.TryGetProperty("speech_final", out var sfProp) && sfProp.GetBoolean();

                    var channel      = root.GetProperty("channel");
                    var alternatives = channel.GetProperty("alternatives");
                    
                    if (alternatives.GetArrayLength() > 0)
                    {
                        var bestAlternative = alternatives[0];
                        string? transcript  = bestAlternative.GetProperty("transcript").GetString();

                        if (!string.IsNullOrWhiteSpace(transcript))
                        {
                            if (!isFinal)
                            {
                                // ── Interim result ─────────────────────────────────────────────
                                // Show accumulated text + current interim so the overlay / logs
                                // reflect the full in-progress sentence, not just the new chunk.
                                var displayText = _accumulator.Length > 0
                                    ? _accumulator.ToString().TrimEnd() + " " + transcript
                                    : transcript;

                                Logger.Log($"Deepgram Transcription Arrived (isFinal=False): {transcript}");
                                TranscriptionReceived?.Invoke(this,
                                    new TranscriptionEventArgs(displayText, false));
                            }
                            else
                            {
                                // ── Final chunk ────────────────────────────────────────────────
                                // Accumulate until the speaker actually pauses (speech_final=true).
                                if (_accumulator.Length > 0)
                                    _accumulator.Append(' ');
                                _accumulator.Append(transcript.Trim());

                                Logger.Log($"Deepgram Transcription Arrived (isFinal=True, speechFinal={speechFinal}): {transcript}");

                                if (speechFinal)
                                {
                                    // Natural utterance boundary — emit the complete sentence.
                                    var fullUtterance = _accumulator.ToString();
                                    _accumulator.Clear();
                                    Logger.Log($"speech_final=True → emitting full utterance: '{fullUtterance}'");
                                    TranscriptionReceived?.Invoke(this,
                                        new TranscriptionEventArgs(fullUtterance, true));
                                }
                                // else: keep accumulating — speech_final will arrive later
                            }
                        }
                    }

                    // The old trigger (isFinal && _isFinishing) is removed.
                    // The Metadata handler below is the authoritative signal for stream end;
                    // it flushes any remaining accumulated text before signalling completion.
                }
                else if (root.TryGetProperty("type", out var type2) && type2.GetString() == "Metadata")
                {
                    // Stream has fully closed — flush anything left in the accumulator.
                    if (_accumulator.Length > 0)
                    {
                        var remaining = _accumulator.ToString();
                        _accumulator.Clear();
                        Logger.Log($"Stream closed — flushing accumulated text: '{remaining}'");
                        TranscriptionReceived?.Invoke(this,
                            new TranscriptionEventArgs(remaining, true));
                    }
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
