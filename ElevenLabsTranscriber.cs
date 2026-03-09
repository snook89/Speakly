using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Speakly.Config;

namespace Speakly.Services
{
    public sealed class ElevenLabsTranscriber : ITranscriber
    {
        private static readonly TimeSpan SessionStartTimeout = TimeSpan.FromSeconds(8);
        private static readonly TimeSpan FinalResultTimeout = TimeSpan.FromSeconds(4);
        private readonly ConcurrentQueue<byte[]> _connectionBuffer = new();
        private readonly SemaphoreSlim _sendGate = new(1, 1);
        private ClientWebSocket? _webSocket;
        private CancellationTokenSource? _cts;
        private TaskCompletionSource<bool>? _sessionStartedTcs;
        private TaskCompletionSource<bool>? _finalResultTcs;

        public event EventHandler<TranscriptionEventArgs>? TranscriptionReceived;
        public event EventHandler<string>? ErrorReceived;

        public bool IsConnected => _webSocket?.State == WebSocketState.Open;

        public async Task ConnectAsync()
        {
            if (IsConnected)
            {
                return;
            }

            var apiKey = ConfigManager.Config.ElevenLabsApiKey?.Trim();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                ErrorReceived?.Invoke(this, "ElevenLabs API key is not configured.");
                return;
            }

            _webSocket = new ClientWebSocket();
            _webSocket.Options.SetRequestHeader("xi-api-key", apiKey);
            _cts = new CancellationTokenSource();
            _sessionStartedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _finalResultTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            try
            {
                var socketUrl = ElevenLabsRealtimeProtocol.BuildSocketUrl(ConfigManager.Config);
                Logger.Log($"Connecting to ElevenLabs realtime STT: {socketUrl}");
                await _webSocket.ConnectAsync(new Uri(socketUrl), _cts.Token);
                _ = Task.Run(ReceiveLoopAsync, _cts.Token);

                var completed = await Task.WhenAny(_sessionStartedTcs.Task, Task.Delay(SessionStartTimeout, _cts.Token));
                if (completed != _sessionStartedTcs.Task || !_sessionStartedTcs.Task.Result)
                {
                    throw new TimeoutException("Timed out waiting for ElevenLabs session_started.");
                }

                while (_connectionBuffer.TryDequeue(out var buffered))
                {
                    await SendChunkAsync(buffered, commit: false, _cts.Token);
                }
            }
            catch (Exception ex)
            {
                ErrorReceived?.Invoke(this, $"Failed to connect to ElevenLabs realtime STT: {ex.Message}");
                await CleanupAsync();
            }
        }

        public async Task DisconnectAsync()
        {
            if (_webSocket != null)
            {
                try
                {
                    if (_webSocket.State == WebSocketState.Open)
                    {
                        await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnect", CancellationToken.None);
                    }
                    else if (_webSocket.State == WebSocketState.CloseReceived)
                    {
                        await _webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Client disconnect", CancellationToken.None);
                    }
                }
                catch (WebSocketException ex) when (IsBenignCloseHandshakeRace(ex))
                {
                    Logger.Log("ElevenLabs realtime STT closed the WebSocket before the close handshake completed.");
                }
                catch (Exception ex)
                {
                    Logger.LogException("ElevenLabsDisconnectAsync", ex);
                }
            }

            await CleanupAsync();
        }

        public Task SendAudioAsync(byte[] buffer)
        {
            if (buffer == null || buffer.Length == 0)
            {
                return Task.CompletedTask;
            }

            if (_webSocket == null || _webSocket.State == WebSocketState.Connecting)
            {
                _connectionBuffer.Enqueue(buffer);
                return Task.CompletedTask;
            }

            if (!IsConnected || _cts == null)
            {
                return Task.CompletedTask;
            }

            return SendChunkAsync(buffer, commit: false, _cts.Token);
        }

        public async Task FinishStreamAsync()
        {
            if (!IsConnected || _webSocket == null || _cts == null)
            {
                _finalResultTcs?.TrySetResult(true);
                return;
            }

            try
            {
                await SendChunkAsync(Array.Empty<byte>(), commit: true, _cts.Token);
                Logger.Log("Commit sent to ElevenLabs realtime STT; awaiting final transcript.");
            }
            catch (Exception ex)
            {
                ErrorReceived?.Invoke(this, $"Failed to finalize ElevenLabs realtime STT stream: {ex.Message}");
                await CleanupAsync();
            }
        }

        public async Task WaitForFinalResultAsync()
        {
            if (_finalResultTcs == null)
            {
                return;
            }

            var completed = await Task.WhenAny(_finalResultTcs.Task, Task.Delay(FinalResultTimeout));
            if (completed != _finalResultTcs.Task)
            {
                Logger.Log($"WARNING: ElevenLabs realtime STT final result timed out after {FinalResultTimeout.TotalMilliseconds:0} ms.");
            }
        }

        private async Task SendChunkAsync(byte[] buffer, bool commit, CancellationToken cancellationToken)
        {
            if (_webSocket == null)
            {
                return;
            }

            var payload = ElevenLabsRealtimeProtocol.BuildInputAudioChunk(buffer, ConfigManager.Config.SampleRate, commit);
            var bytes = Encoding.UTF8.GetBytes(payload);

            await _sendGate.WaitAsync(cancellationToken);
            try
            {
                await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
            }
            finally
            {
                _sendGate.Release();
            }
        }

        private async Task ReceiveLoopAsync()
        {
            var receiveBuffer = new byte[8192];

            while (_webSocket?.State == WebSocketState.Open && _cts != null && !_cts.Token.IsCancellationRequested)
            {
                try
                {
                    var builder = new StringBuilder();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(receiveBuffer), _cts.Token);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            _finalResultTcs?.TrySetResult(true);
                            await CleanupAsync();
                            return;
                        }

                        builder.Append(Encoding.UTF8.GetString(receiveBuffer, 0, result.Count));
                    } while (!result.EndOfMessage);

                    if (result.MessageType != WebSocketMessageType.Text)
                    {
                        continue;
                    }

                    var message = ElevenLabsRealtimeProtocol.ParseMessage(builder.ToString());
                    switch (message.MessageType)
                    {
                        case "session_started":
                            _sessionStartedTcs?.TrySetResult(true);
                            Logger.Log("ElevenLabs realtime STT session started.");
                            break;
                        case "partial_transcript":
                            if (!string.IsNullOrWhiteSpace(message.Text))
                            {
                                TranscriptionReceived?.Invoke(this, new TranscriptionEventArgs(message.Text, false));
                            }
                            break;
                        case "committed_transcript":
                            if (!string.IsNullOrWhiteSpace(message.Text))
                            {
                                Logger.Log($"ElevenLabs committed transcript received: {message.Text}");
                                TranscriptionReceived?.Invoke(this, new TranscriptionEventArgs(message.Text, true));
                            }
                            _finalResultTcs?.TrySetResult(true);
                            break;
                        case "error":
                            ErrorReceived?.Invoke(this, $"ElevenLabs realtime STT error: {message.Error}");
                            _finalResultTcs?.TrySetResult(true);
                            break;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    ErrorReceived?.Invoke(this, $"ElevenLabs realtime receive error: {ex.Message}");
                    _finalResultTcs?.TrySetResult(true);
                    break;
                }
            }

            _finalResultTcs?.TrySetResult(true);
        }

        private async Task CleanupAsync()
        {
            _sessionStartedTcs?.TrySetResult(false);
            _finalResultTcs?.TrySetResult(true);

            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }

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

        private static bool IsBenignCloseHandshakeRace(WebSocketException ex)
        {
            if (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
            {
                return true;
            }

            return ex.Message?.IndexOf("without completing the close handshake", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public void Dispose()
        {
            _ = CleanupAsync();
            GC.SuppressFinalize(this);
        }
    }
}
