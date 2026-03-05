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
        private const string DefaultDeepgramBaseUrl = "https://api.deepgram.com";
        private const string KeepAliveMessage = "{\"type\":\"KeepAlive\"}";
        private const string FinalizeMessage = "{\"type\":\"Finalize\"}";
        private const string CloseStreamMessage = "{\"type\":\"CloseStream\"}";
        private ClientWebSocket? _webSocket;
        private CancellationTokenSource? _cts;
        private readonly ConcurrentQueue<byte[]> _connectionBuffer = new();
        private readonly SemaphoreSlim _sendGate = new(1, 1);
        private TaskCompletionSource? _finalResultTcs;
        private Task? _keepAliveTask;
        private long _lastAudioSentTicksUtc;
        private int _hasSentAudio;
        private int _isFinishing;
        private const string Version = "1.7.0-Reliability";
        private static readonly TimeSpan FinalResultWaitTimeout = TimeSpan.FromSeconds(12);

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
            string httpBaseUrl = NormalizeDeepgramHttpBaseUrl(config.DeepgramApiBaseUrl);

            _cts = new CancellationTokenSource();
            _finalResultTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            try
            {
                Logger.Log($"Connecting to Deepgram ({Version}): {url}");
                _accumulator.Clear();
                Interlocked.Exchange(ref _hasSentAudio, 0);
                Interlocked.Exchange(ref _isFinishing, 0);
                Interlocked.Exchange(ref _lastAudioSentTicksUtc, 0);
                while (_connectionBuffer.TryDequeue(out _))
                {
                }
                await _webSocket.ConnectAsync(new Uri(url), _cts.Token);

                while (_connectionBuffer.TryDequeue(out var bufferedData))
                {
                    await SendWithGateAsync(new ArraySegment<byte>(bufferedData), WebSocketMessageType.Binary, _cts.Token);
                    Interlocked.Exchange(ref _hasSentAudio, 1);
                    Interlocked.Exchange(ref _lastAudioSentTicksUtc, DateTime.UtcNow.Ticks);
                }

                _keepAliveTask = Task.Run(() => KeepAliveLoopAsync(_cts.Token), _cts.Token);
                _ = Task.Run(ReceiveLoopAsync, _cts.Token);
                Logger.Log("Deepgram connected successfully.");
            }
            catch (Exception ex)
            {
                string handshakeInfo = GetHandshakeFailureDetails();
                string diagnosticInfo = await DiagnosticProbeAsync(apiKey, httpBaseUrl);
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
                "endpointing=300",
                "vad_events=true",
                "utterance_end_ms=1000"
            };

            var preferredTerms = PersonalDictionaryService.GetCombinedTermsForActiveProfile(config, maxTerms: 30);
            if (preferredTerms.Count > 0)
            {
                // Deepgram Nova-3/Flux reject legacy "keywords" and require "keyterm".
                // Keep backward compatibility for older models.
                if (SupportsKeytermHints(selectedModel))
                {
                    foreach (var term in preferredTerms)
                    {
                        var normalized = term?.Trim();
                        if (string.IsNullOrWhiteSpace(normalized)) continue;
                        query.Add($"keyterm={Uri.EscapeDataString(normalized)}");
                    }
                }
                else
                {
                    query.Add($"keywords={Uri.EscapeDataString(string.Join(",", preferredTerms))}");
                }
            }

            var resolvedLanguage = ResolveLanguageForStreaming(config.Language, selectedModel);
            if (!string.IsNullOrWhiteSpace(resolvedLanguage))
            {
                query.Add($"language={Uri.EscapeDataString(resolvedLanguage)}");
            }

            string wsBase = NormalizeDeepgramWebSocketBaseUrl(config.DeepgramApiBaseUrl);
            return $"{wsBase}/v1/listen?{string.Join("&", query)}";
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

            if (string.Equals(value, "multi", StringComparison.OrdinalIgnoreCase))
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

        private static bool SupportsKeytermHints(string? model)
        {
            if (string.IsNullOrWhiteSpace(model)) return false;
            var normalizedModel = model.Trim().ToLowerInvariant();
            return normalizedModel.StartsWith("nova-3") || normalizedModel.StartsWith("flux");
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

                if (headers.TryGetValue("dg-request-id", out var requestIds)
                    || headers.TryGetValue("x-dg-request-id", out requestIds))
                {
                    details.Add($"request-id: {string.Join(", ", requestIds)}");
                }
            }

            return string.Join("; ", details);
        }

        private static string NormalizeDeepgramHttpBaseUrl(string? configuredBaseUrl)
        {
            var value = configuredBaseUrl?.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                value = DefaultDeepgramBaseUrl;
            }

            if (!value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                && !value.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                value = "https://" + value;
            }

            return value.TrimEnd('/');
        }

        private static string NormalizeDeepgramWebSocketBaseUrl(string? configuredBaseUrl)
        {
            var httpBase = NormalizeDeepgramHttpBaseUrl(configuredBaseUrl);
            if (httpBase.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return "wss://" + httpBase.Substring("https://".Length);
            }

            if (httpBase.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                return "ws://" + httpBase.Substring("http://".Length);
            }

            return "wss://api.deepgram.com";
        }

        private async Task<string> DiagnosticProbeAsync(string apiKey, string httpBaseUrl)
        {
            try
            {
                using var httpClient = new HttpClient();
                using var authCheck = new HttpRequestMessage(HttpMethod.Get, $"{httpBaseUrl}/v1/auth/token");
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
            if (buffer == null || buffer.Length == 0)
            {
                return;
            }

            if (_webSocket == null || _webSocket.State == WebSocketState.Connecting)
            {
                _connectionBuffer.Enqueue(buffer);
                return;
            }

            if (!IsConnected || _cts == null) return;

            try
            {
                await SendWithGateAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Binary, _cts.Token);
                Interlocked.Exchange(ref _hasSentAudio, 1);
                Interlocked.Exchange(ref _lastAudioSentTicksUtc, DateTime.UtcNow.Ticks);
            }
            catch (Exception ex)
            {
                ErrorReceived?.Invoke(this, $"Failed to send audio to Deepgram: {ex.Message}");
                await CleanupAsync();
            }
        }

        public async Task FinishStreamAsync()
        {
            Interlocked.Exchange(ref _isFinishing, 1);

            if (!IsConnected || _webSocket == null || _cts == null) 
            {
                _finalResultTcs?.TrySetResult();
                return;
            }

            try
            {
                // Ask Deepgram to flush trailing audio and then close stream.
                await SendControlMessageAsync(FinalizeMessage, "Finalize", _cts.Token, reportErrors: false);
                Logger.Log("Sending CloseStream to Deepgram.");
                await SendControlMessageAsync(CloseStreamMessage, "CloseStream", _cts.Token, reportErrors: true);
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
                // Allow additional time for final segment flush on long utterances.
                var timeoutTask = Task.Delay(FinalResultWaitTimeout);
                var completedTask = await Task.WhenAny(_finalResultTcs.Task, timeoutTask);
                if (completedTask == timeoutTask)
                {
                    Logger.Log($"WARNING: WaitForFinalResultAsync timed out after {FinalResultWaitTimeout.TotalSeconds:0}s.");
                }
            }
        }

        private async Task KeepAliveLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(4), cancellationToken);

                    if (!IsConnected || _webSocket == null) continue;
                    if (Interlocked.CompareExchange(ref _isFinishing, 0, 0) == 1) continue;
                    if (Interlocked.CompareExchange(ref _hasSentAudio, 0, 0) == 0) continue;

                    var lastAudioTicks = Interlocked.Read(ref _lastAudioSentTicksUtc);
                    if (lastAudioTicks <= 0) continue;

                    var elapsed = DateTime.UtcNow - new DateTime(lastAudioTicks, DateTimeKind.Utc);
                    if (elapsed < TimeSpan.FromSeconds(3)) continue;

                    await SendControlMessageAsync(KeepAliveMessage, "KeepAlive", cancellationToken, reportErrors: false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Logger.LogException("DeepgramKeepAliveLoop", ex);
            }
        }

        private async Task SendControlMessageAsync(string json, string label, CancellationToken cancellationToken, bool reportErrors)
        {
            if (!IsConnected || _webSocket == null) return;

            try
            {
                var payload = Encoding.UTF8.GetBytes(json);
                await SendWithGateAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Text, cancellationToken);
            }
            catch (Exception ex)
            {
                if (reportErrors)
                {
                    ErrorReceived?.Invoke(this, $"Failed to send {label} to Deepgram: {ex.Message}");
                }
            }
        }

        private async Task SendWithGateAsync(ArraySegment<byte> payload, WebSocketMessageType messageType, CancellationToken cancellationToken)
        {
            if (_webSocket == null) return;

            await _sendGate.WaitAsync(cancellationToken);
            try
            {
                await _webSocket.SendAsync(payload, messageType, true, cancellationToken);
            }
            finally
            {
                _sendGate.Release();
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

                if (!root.TryGetProperty("type", out var typeElement))
                {
                    return;
                }

                var responseType = typeElement.GetString();
                if (responseType == "Results")
                {
                    bool isFinal = root.GetProperty("is_final").GetBoolean();
                    bool speechFinal = root.TryGetProperty("speech_final", out var sfProp) && sfProp.GetBoolean();

                    var channel = root.GetProperty("channel");
                    var alternatives = channel.GetProperty("alternatives");

                    if (alternatives.GetArrayLength() > 0)
                    {
                        var bestAlternative = alternatives[0];
                        string? transcript = bestAlternative.GetProperty("transcript").GetString();
                        var normalizedTranscript = transcript?.Trim();

                        if (!string.IsNullOrWhiteSpace(normalizedTranscript))
                        {
                            if (!isFinal)
                            {
                                var displayText = _accumulator.Length > 0
                                    ? _accumulator.ToString().TrimEnd() + " " + normalizedTranscript
                                    : normalizedTranscript;

                                Logger.Log($"Deepgram Transcription Arrived (isFinal=False): {normalizedTranscript}");
                                TranscriptionReceived?.Invoke(this,
                                    new TranscriptionEventArgs(displayText, false));

                                if (speechFinal)
                                {
                                    EmitSpeechFinalFromInterim(normalizedTranscript);
                                }
                            }
                            else
                            {
                                if (_accumulator.Length > 0)
                                {
                                    _accumulator.Append(' ');
                                }
                                _accumulator.Append(normalizedTranscript);

                                Logger.Log($"Deepgram Transcription Arrived (isFinal=True, speechFinal={speechFinal}): {normalizedTranscript}");

                                if (speechFinal)
                                {
                                    EmitAndClearAccumulator("speech_final");
                                }
                            }
                        }

                        if (speechFinal && string.IsNullOrWhiteSpace(normalizedTranscript))
                        {
                            EmitAndClearAccumulator("speech_final without transcript");
                        }
                    }

                }
                else if (responseType == "UtteranceEnd")
                {
                    EmitAndClearAccumulator("utterance_end");
                }
                else if (responseType == "SpeechStarted")
                {
                    Logger.Log("Deepgram speech started event received.");
                }
                else if (responseType == "Metadata")
                {
                    EmitAndClearAccumulator("metadata flush");
                    _finalResultTcs?.TrySetResult();
                }
            }
            catch (Exception ex)
            {
                 Logger.LogException("ProcessDeepgramResponse", ex);
                 ErrorReceived?.Invoke(this, $"Deepgram Parser Error: {ex.Message}");
            }
        }

        private void EmitSpeechFinalFromInterim(string interimTranscript)
        {
            var trimmedInterim = interimTranscript.Trim();
            if (string.IsNullOrWhiteSpace(trimmedInterim))
            {
                EmitAndClearAccumulator("speech_final interim empty");
                return;
            }

            var accumulated = _accumulator.ToString().Trim();
            string fullUtterance;
            if (string.IsNullOrWhiteSpace(accumulated))
            {
                fullUtterance = trimmedInterim;
            }
            else if (accumulated.EndsWith(trimmedInterim, StringComparison.OrdinalIgnoreCase))
            {
                fullUtterance = accumulated;
            }
            else
            {
                fullUtterance = $"{accumulated} {trimmedInterim}".Trim();
            }

            _accumulator.Clear();
            if (string.IsNullOrWhiteSpace(fullUtterance)) return;

            Logger.Log($"speech_final=True with interim result -> emitting full utterance: '{fullUtterance}'");
            TranscriptionReceived?.Invoke(this, new TranscriptionEventArgs(fullUtterance, true));
        }

        private void EmitAndClearAccumulator(string reason)
        {
            if (_accumulator.Length == 0) return;

            var remaining = _accumulator.ToString().Trim();
            _accumulator.Clear();
            if (string.IsNullOrWhiteSpace(remaining)) return;

            Logger.Log($"Deepgram final emission ({reason}): '{remaining}'");
            TranscriptionReceived?.Invoke(this, new TranscriptionEventArgs(remaining, true));
        }

        private async Task CleanupAsync()
        {
            _finalResultTcs?.TrySetResult();
            Interlocked.Exchange(ref _isFinishing, 0);
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }

            _keepAliveTask = null;
            while (_connectionBuffer.TryDequeue(out _))
            {
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
