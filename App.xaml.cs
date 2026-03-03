using System.Linq;
using System.Media;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using Speakly.Config;
using Speakly.Services;
using Speakly.ViewModels;

namespace Speakly
{
    public partial class App : Application
    {
        private enum SessionState
        {
            Idle,
            Recording,
            Transcribing,
            Refining,
            Error
        }

        private static readonly string[] BaseThemeDictionaryUris =
        {
            "/Themes/LightTheme.xaml",
            "/Themes/DarkTheme.xaml"
        };

        public static MainViewModel ViewModel { get; private set; } = null!;

        private GlobalHotkeyService? _hotkeyService;
        private TrayIconService? _trayService;
        private IAudioRecorder? _recorder;
        private ITranscriber? _transcriber;
        private ITextRefiner? _refiner;
        private FloatingOverlay? _overlay;
        private SoundPlayer? _startSound;
        private SoundPlayer? _stopSound;
        private bool _isToggleRecording = false; // Track toggle-record state
        private readonly object _sessionLock = new();
        private SessionState _sessionState = SessionState.Idle;
        private IntPtr _lastActiveWindow = IntPtr.Zero;
        private System.IO.MemoryStream? _audioBuffer; // For debug records
        // Serializes text insertions: only one InsertText may run at a time to prevent
        // concurrent SendInput calls from interleaving keystrokes in the target window.
        private readonly System.Threading.SemaphoreSlim _insertionGate = new System.Threading.SemaphoreSlim(1, 1);
        // Accumulates every inserted utterance for the current PTT/Toggle session so
        // the clipboard always holds the FULL session text, not just the last utterance.
        private readonly System.Text.StringBuilder _sessionText = new();
        // Whether at least one utterance has already been typed into the target window
        // this session — used to prepend a space before subsequent utterances.
        private bool _sessionHasInserted = false;
        private DateTime _recordingStartedUtc;
        private DateTime _transcribingStartedUtc;
        private int _recordMs;
        private int _transcribeMs;
        private int _refineMs;
        private int _insertMs;
        private readonly object _audioChunkLock = new();
        private readonly List<byte[]> _sessionAudioChunks = new();
        private bool _finalTranscriptionProcessed;
        private bool _sttFailoverAttempted;
        private AppProfile? _activeSessionProfile;
        private string _failoverFromProvider = string.Empty;
        private string _failoverToProvider = string.Empty;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            ConfigManager.Load();
            SetTheme(ConfigManager.Config.Theme);

            try
            {
                var startUri = new Uri("pack://application:,,,/Resources/start_feedback.wav");
                var startStream = Application.GetResourceStream(startUri);
                if (startStream != null) { _startSound = new SoundPlayer(startStream.Stream); _startSound.Load(); }
                
                var stopUri = new Uri("pack://application:,,,/Resources/stop_feedback.wav");
                var stopStream = Application.GetResourceStream(stopUri);
                if (stopStream != null) { _stopSound = new SoundPlayer(stopStream.Stream); _stopSound.Load(); }
            }
            catch { /* Ignore sound load errors */ }

            // Initialize Services
            _hotkeyService = new GlobalHotkeyService();
            _hotkeyService.KeyDown += OnPTTPressed;
            _hotkeyService.KeyUp += OnPTTReleased;

            _recorder = new NAudioRecorder();
            _recorder.AudioDataAvailable += OnAudioDataAvailable;

            InitializeTranscriptionAndRefinement();

            ViewModel = new MainViewModel();
            ViewModel.RunHealthChecks();

            if (!ConfigManager.Config.FirstRunCompleted)
            {
                var onboarding = new OnboardingWindow(ViewModel);
                var onboardingResult = onboarding.ShowDialog();
                if (onboardingResult != true || !ConfigManager.Config.FirstRunCompleted)
                {
                    Shutdown();
                    return;
                }
            }

            MainWindow = new MainWindow();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            _trayService = new TrayIconService(MainWindow);
            
            // Set window icon for the taskbar/window chrome.
            try
            {
                var iconUri = new Uri("pack://application:,,,/Resources/Speakly_new_logo.ico");
                MainWindow.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(iconUri);
            }
            catch { }
            
            if (ConfigManager.Config.ShowOverlay)
            {
                _overlay = new FloatingOverlay();
                _overlay.Show();
                _overlay.EnsureVisibleOnScreen();
            }

            MainWindow.Show();
        }

        private void InitializeTranscriptionAndRefinement()
        {
            // STT
            _transcriber?.Dispose();
            _transcriber = TranscriberFactory.CreateTranscriber(ConfigManager.Config.SttModel);
            _transcriber.TranscriptionReceived += OnTranscriptionReceived;
            _transcriber.ErrorReceived += OnTranscriberError;

            // Refinement
            _refiner = TextRefinerFactory.CreateRefiner(ConfigManager.Config.RefinementModel);
        }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private bool IsModifierPressed(int vKey)
        {
            return (GetAsyncKeyState(vKey) & 0x8000) != 0;
        }

        private string ResolveOverlayLanguageDisplay()
        {
            var configuredLanguage = ConfigManager.Config.Language?.Trim();
            if (string.IsNullOrWhiteSpace(configuredLanguage)) return "EN";

            if (string.Equals(configuredLanguage, "layout", StringComparison.OrdinalIgnoreCase))
            {
                return InputLanguageResolver.ResolveCurrentLanguageCode("en").ToUpperInvariant();
            }

            if (string.Equals(configuredLanguage, "auto", StringComparison.OrdinalIgnoreCase))
            {
                return "AUTO";
            }

            return configuredLanguage.ToUpperInvariant();
        }

        private string ResolveEffectiveInputLanguageCode()
        {
            var configuredLanguage = ConfigManager.Config.Language?.Trim();
            if (string.IsNullOrWhiteSpace(configuredLanguage)) return "en";

            if (string.Equals(configuredLanguage, "layout", StringComparison.OrdinalIgnoreCase))
            {
                return InputLanguageResolver.ResolveCurrentLanguageCode("en").ToLowerInvariant();
            }

            if (string.Equals(configuredLanguage, "auto", StringComparison.OrdinalIgnoreCase))
            {
                return "multi";
            }

            return configuredLanguage.ToLowerInvariant();
        }

        private void AutoAdjustRefinementPromptForLanguage()
        {
            try
            {
                string inputLanguage = ResolveEffectiveInputLanguageCode();
                if ((inputLanguage == "en" || inputLanguage.StartsWith("en-", StringComparison.OrdinalIgnoreCase)) &&
                    RefinementPromptLibrary.IsUkrainianPreset(ConfigManager.Config.RefinementPrompt))
                {
                    ConfigManager.Config.RefinementPrompt = RefinementPromptLibrary.General;
                    Logger.Log("Auto-switched refinement prompt from Ukrainian preset to General due to EN input language.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogException("AutoAdjustRefinementPromptForLanguage", ex);
            }
        }

        private bool IsHotkeyMatch(string configStr, HotkeyEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(configStr)) return false;

            var parts = configStr.Split('+');
            string mainKeyStr = parts[^1].Trim();
            
            if (!Enum.TryParse<Key>(mainKeyStr, true, out var mainKey)) return false;
            
            if (e.Key != mainKey && e.SystemKey != mainKey) return false;

            bool needsCtrl = parts.Contains("Ctrl", StringComparer.OrdinalIgnoreCase);
            bool needsAlt = parts.Contains("Alt", StringComparer.OrdinalIgnoreCase);
            bool needsShift = parts.Contains("Shift", StringComparer.OrdinalIgnoreCase);
            bool needsWin = parts.Contains("Win", StringComparer.OrdinalIgnoreCase);

            bool isCtrlPressed = IsModifierPressed(0x11); // VK_CONTROL
            bool isAltPressed = IsModifierPressed(0x12);  // VK_MENU
            bool isShiftPressed = IsModifierPressed(0x10); // VK_SHIFT
            bool isWinPressed = IsModifierPressed(0x5B) || IsModifierPressed(0x5C); // VK_LWIN, VK_RWIN

            return needsCtrl == isCtrlPressed &&
                   needsAlt == isAltPressed &&
                   needsShift == isShiftPressed &&
                   needsWin == isWinPressed;
        }

        private async void OnPTTPressed(object? sender, HotkeyEventArgs e)
        {
            // --- Push-to-Talk (hold) ---
            if (IsHotkeyMatch(ConfigManager.Config.PttHotkey, e))
            {
                if (_recorder != null && TryEnterRecording())
                {
                    BeginSessionTiming();
                    _lastActiveWindow = TextInserter.GetForegroundWindow();
                    PrepareSessionContext();
                    Logger.Log($"PTT Hotkey Pressed. Captured active window: {_lastActiveWindow}");
                    _sessionText.Clear();
                    _sessionHasInserted = false;
                    AutoAdjustRefinementPromptForLanguage();
                    _overlay?.SetActiveLanguage(ResolveOverlayLanguageDisplay());
                    _overlay?.SetStatus("RECORDING", Brushes.Red);
                    _startSound?.Play();
                    
                    // Start connection in background
                    var connectTask = _transcriber != null ? _transcriber.ConnectAsync() : Task.CompletedTask;
                    
                    if (ConfigManager.Config.SaveDebugRecords) _audioBuffer = new System.IO.MemoryStream();
                    _recorder.StartRecording();
                    
                    // Ensure connection is up eventually (though streaming starts immediately via buffer)
                    await connectTask;
                    return;
                }
            }

            // --- Toggle Record ---
            if (IsHotkeyMatch(ConfigManager.Config.RecordHotkey, e))
            {
                if (!_isToggleRecording)
                {
                    if (!TryEnterRecording()) return;
                    BeginSessionTiming();

                    _lastActiveWindow = TextInserter.GetForegroundWindow();
                    PrepareSessionContext();
                    Logger.Log($"Toggle Recording Started. Captured active window: {_lastActiveWindow}");
                    _sessionText.Clear();
                    _sessionHasInserted = false;
                    AutoAdjustRefinementPromptForLanguage();
                    _overlay?.SetActiveLanguage(ResolveOverlayLanguageDisplay());
                    _isToggleRecording = true;
                    _overlay?.SetStatus("RECORDING", Brushes.OrangeRed);
                    _startSound?.Play();

                    // Start connection in background
                    var connectTask = _transcriber != null ? _transcriber.ConnectAsync() : Task.CompletedTask;
                    
                    if (ConfigManager.Config.SaveDebugRecords) _audioBuffer = new System.IO.MemoryStream();
                    _recorder?.StartRecording();

                    // Ensure connection is up eventually
                    await connectTask;
                }
                else
                {
                    _isToggleRecording = false;
                    await StopRecordingAsync();
                }
            }
        }

        private async void OnPTTReleased(object? sender, HotkeyEventArgs e)
        {
            // Only PTT (hold) stops on key-up — not the toggle key
            string pttConfig = ConfigManager.Config.PttHotkey;
            if (!string.IsNullOrWhiteSpace(pttConfig))
            {
                var parts = pttConfig.Split('+');
                string mainKeyStr = parts[^1].Trim();
                
                if (Enum.TryParse<Key>(mainKeyStr, true, out var mainKey))
                {
                    if ((e.Key == mainKey || e.SystemKey == mainKey) && _recorder != null && _recorder.IsRecording && !_isToggleRecording)
                    {
                        await StopRecordingAsync();
                    }
                }
            }
        }

        private async Task StopRecordingAsync()
        {
            if (_recorder == null || !TryEnterTranscribing()) return;
            Logger.Log("Stopping recording.");
            _overlay?.SetStatus("TRANSCRIBING", Brushes.Yellow);
            _stopSound?.Play();
            _recorder.StopRecording();
            _recordMs = (int)Math.Max(0, (DateTime.UtcNow - _recordingStartedUtc).TotalMilliseconds);
            _transcribingStartedUtc = DateTime.UtcNow;

            try
            {
                if (_transcriber != null)
                {
                    Logger.Log("Finalizing transcriber stream.");
                    await _transcriber.FinishStreamAsync();

                    await _transcriber.WaitForFinalResultAsync();

                    Logger.Log("Disconnecting transcriber.");
                    await _transcriber.DisconnectAsync();
                }
            }
            catch (Exception ex)
            {
                Logger.LogException("StopRecordingAsync", ex);
            }
            finally
            {
                if (_audioBuffer != null)
                {
                    SaveDebugRecord();
                    _audioBuffer.Dispose();
                    _audioBuffer = null;
                }

                // Do NOT set READY here – OnTranscriptionReceived handles the final READY
                // state (and toast). Setting it here would cause RECORDING→TRANSCRIBING→READY
                // to fire before REFINING, producing the wrong sequence.
            }
        }

        private void SaveDebugRecord()
        {
            if (_audioBuffer == null || _audioBuffer.Length == 0) return;

            try
            {
                string recordsDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Records");
                if (!System.IO.Directory.Exists(recordsDir)) System.IO.Directory.CreateDirectory(recordsDir);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string filePath = System.IO.Path.Combine(recordsDir, $"record_{timestamp}.wav");

                _audioBuffer.Position = 0;
                using (var waveFileWriter = new NAudio.Wave.WaveFileWriter(filePath, new NAudio.Wave.WaveFormat(ConfigManager.Config.SampleRate, 16, ConfigManager.Config.Channels)))
                {
                    _audioBuffer.CopyTo(waveFileWriter);
                }
                Logger.Log($"Debug record saved to: {filePath}");
            }
            catch (Exception ex)
            {
                Logger.LogException("SaveDebugRecord", ex);
            }
        }

        private async void OnAudioDataAvailable(object? sender, byte[] data)
        {
            // Feed waveform level (PCM 16-bit LE → RMS)
            if (data.Length >= 2)
            {
                double sumSq = 0;
                int sampleCount = data.Length / 2;
                for (int i = 0; i < data.Length - 1; i += 2)
                {
                    short sample = (short)(data[i] | (data[i + 1] << 8));
                    double norm = sample / 32768.0;
                    sumSq += norm * norm;
                }
                float rms = (float)Math.Sqrt(sumSq / sampleCount);
                // Scale aggressively so quiet voices still animate
                // Increased multiplier from 6f to 12f for better reactivity
                _overlay?.UpdateAudioLevel(Math.Min(rms * 12f, 1f));
            }

            if (_transcriber != null && _transcriber.IsConnected)
            {
                await _transcriber.SendAudioAsync(data);
            }

            CaptureSessionAudio(data);

            if (_audioBuffer != null)
            {
                _audioBuffer.Write(data, 0, data.Length);
            }
        }

        private async void OnTranscriptionReceived(object? sender, TranscriptionEventArgs e)
        {
            if (!e.IsFinal || string.IsNullOrWhiteSpace(e.Text) || _finalTranscriptionProcessed) return;
            Logger.Log($"App received transcription: isFinal={e.IsFinal}, Text='{e.Text}'");
            await HandleFinalTranscriptionAsync(e.Text, ConfigManager.Config.SttModel, ResolveActiveSttModel());
        }

        private void CaptureSessionAudio(byte[] data)
        {
            if (data.Length == 0) return;

            var copy = new byte[data.Length];
            Buffer.BlockCopy(data, 0, copy, 0, data.Length);
            lock (_audioChunkLock)
            {
                _sessionAudioChunks.Add(copy);
            }
        }

        private List<byte[]> SnapshotSessionAudio()
        {
            lock (_audioChunkLock)
            {
                return _sessionAudioChunks.Select(chunk =>
                {
                    var copy = new byte[chunk.Length];
                    Buffer.BlockCopy(chunk, 0, copy, 0, chunk.Length);
                    return copy;
                }).ToList();
            }
        }

        private async void OnTranscriberError(object? sender, string error)
        {
            if (_finalTranscriptionProcessed) return;

            var errorCode = ErrorClassifier.Classify(error);
            Logger.Log($"Transcriber error classified as '{errorCode}': {error}");

            var failoverSucceeded = await TryRunSttFailoverAsync(errorCode);
            if (failoverSucceeded) return;

            HandleSessionFailure(error, errorCode);
        }

        private async Task HandleFinalTranscriptionAsync(string originalText, string sttProvider, string sttModel)
        {
            if (_finalTranscriptionProcessed) return;
            _finalTranscriptionProcessed = true;

            string textToInsert = originalText;
            _transcribeMs = (int)Math.Max(0, (DateTime.UtcNow - _transcribingStartedUtc).TotalMilliseconds);

            if (_refiner != null && ConfigManager.Config.EnableRefinement)
            {
                SetSessionState(SessionState.Refining);
                string activeRefinementModel = ConfigManager.Config.RefinementModel switch
                {
                    "Cerebras" => ConfigManager.Config.CerebrasRefinementModel,
                    "OpenRouter" => ConfigManager.Config.OpenRouterRefinementModel,
                    _ => ConfigManager.Config.OpenAIRefinementModel
                };
                Logger.Log($"Refining text using {ConfigManager.Config.RefinementModel} (model={activeRefinementModel})");
                _overlay?.SetStatus("REFINING", Brushes.Cyan);
                var swRefine = Stopwatch.StartNew();
                textToInsert = await _refiner.RefineTextAsync(originalText, ConfigManager.Config.RefinementPrompt);
                swRefine.Stop();
                _refineMs = (int)swRefine.ElapsedMilliseconds;
                Logger.Log($"Refinement complete: '{textToInsert}'");
            }
            else
            {
                _refineMs = 0;
            }

            Logger.Log($"Inserting text into window {_lastActiveWindow}: '{textToInsert}'");
            var toType = _sessionHasInserted ? " " + textToInsert : textToInsert;
            InsertResult insertResult = new InsertResult { Success = false, Method = "Unknown", ErrorCode = "NotExecuted" };
            await _insertionGate.WaitAsync();
            try
            {
                var swInsert = Stopwatch.StartNew();
                insertResult = await Task.Run(() => TextInserter.InsertText(toType, _lastActiveWindow));
                swInsert.Stop();
                _insertMs = (int)swInsert.ElapsedMilliseconds;
            }
            finally
            {
                _insertionGate.Release();
            }
            _sessionHasInserted = true;

            if (ConfigManager.Config.CopyToClipboard)
            {
                if (_sessionText.Length > 0) _sessionText.Append(' ');
                _sessionText.Append(textToInsert);
                var fullSessionText = _sessionText.ToString();
                Dispatcher.Invoke(() => Clipboard.SetText(fullSessionText));
                Logger.Log($"Copied full session text to clipboard (length={fullSessionText.Length}).");
            }

            HistoryManager.AddEntry(
                original: originalText,
                refined: textToInsert,
                sttProvider: sttProvider,
                sttModel: sttModel,
                refinementProvider: ConfigManager.Config.EnableRefinement ? ConfigManager.Config.RefinementModel : "Disabled",
                refinementModel: ConfigManager.Config.EnableRefinement ? ResolveActiveRefinementModel() : string.Empty,
                recordMs: _recordMs,
                transcribeMs: _transcribeMs,
                refineMs: _refineMs,
                insertMs: _insertMs,
                succeeded: true,
                errorCode: string.Empty,
                insertionMethod: insertResult.Method,
                profileId: _activeSessionProfile?.Id ?? string.Empty,
                profileName: _activeSessionProfile?.Name ?? "Default",
                failoverAttempted: _sttFailoverAttempted,
                failoverFrom: _failoverFromProvider,
                failoverTo: _failoverToProvider,
                finalProviderUsed: sttProvider);

            StatisticsManager.RecordSession(new SessionMetricEntry
            {
                Timestamp = DateTime.Now,
                SttProvider = sttProvider,
                SttModel = sttModel,
                RefinementProvider = ConfigManager.Config.EnableRefinement ? ConfigManager.Config.RefinementModel : "Disabled",
                RefinementModel = ConfigManager.Config.EnableRefinement ? ResolveActiveRefinementModel() : string.Empty,
                RecordMs = _recordMs,
                TranscribeMs = _transcribeMs,
                RefineMs = _refineMs,
                InsertMs = _insertMs,
                Succeeded = true,
                ErrorCode = string.Empty,
                ProfileId = _activeSessionProfile?.Id ?? string.Empty,
                ProfileName = _activeSessionProfile?.Name ?? "Default",
                FailoverAttempted = _sttFailoverAttempted,
                FailoverFrom = _failoverFromProvider,
                FailoverTo = _failoverToProvider,
                FinalProviderUsed = sttProvider
            });

            Dispatcher.Invoke(() =>
            {
                var vm = MainWindow.DataContext as MainViewModel;
                vm?.HistoryEntries.Insert(0, new HistoryEntry
                {
                    Timestamp = DateTime.Now,
                    OriginalText = originalText,
                    RefinedText = textToInsert,
                    SttProvider = sttProvider,
                    SttModel = sttModel,
                    RefinementProvider = ConfigManager.Config.EnableRefinement ? ConfigManager.Config.RefinementModel : "Disabled",
                    RefinementModel = ConfigManager.Config.EnableRefinement ? ResolveActiveRefinementModel() : string.Empty,
                    RecordMs = _recordMs,
                    TranscribeMs = _transcribeMs,
                    RefineMs = _refineMs,
                    InsertMs = _insertMs,
                    Succeeded = true,
                    InsertionMethod = insertResult.Method,
                    ProfileId = _activeSessionProfile?.Id ?? string.Empty,
                    ProfileName = _activeSessionProfile?.Name ?? "Default",
                    FailoverAttempted = _sttFailoverAttempted,
                    FailoverFrom = _failoverFromProvider,
                    FailoverTo = _failoverToProvider,
                    FinalProviderUsed = sttProvider
                });
                vm?.SetLastInsertionStatus(insertResult.Method, insertResult.Success, insertResult.ErrorCode);
            });

            _overlay?.SetStatus("READY", Brushes.Aqua);
            SetSessionState(SessionState.Idle);
        }

        private async Task<bool> TryRunSttFailoverAsync(string errorCode)
        {
            if (_sttFailoverAttempted || _finalTranscriptionProcessed) return false;
            if (!ConfigManager.Config.EnableSttFailover) return false;
            if (!ErrorClassifier.IsTransient(errorCode)) return false;

            var fallbackProvider = ResolveFallbackProvider();
            if (string.IsNullOrWhiteSpace(fallbackProvider)) return false;

            var audioSnapshot = SnapshotSessionAudio();
            if (audioSnapshot.Count == 0) return false;

            _sttFailoverAttempted = true;
            _failoverFromProvider = ConfigManager.Config.SttModel;
            _failoverToProvider = fallbackProvider;
            _overlay?.SetStatus($"FAILOVER:{fallbackProvider}", Brushes.Orange);
            Logger.Log($"Attempting STT failover to {fallbackProvider}.");

            ITranscriber? fallback = null;
            try
            {
                var finalTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                var errorTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

                fallback = TranscriberFactory.CreateTranscriber(fallbackProvider);
                fallback.TranscriptionReceived += (_, e) =>
                {
                    if (e.IsFinal && !string.IsNullOrWhiteSpace(e.Text))
                        finalTcs.TrySetResult(e.Text);
                };
                fallback.ErrorReceived += (_, e) => errorTcs.TrySetResult(e);

                await fallback.ConnectAsync();

                foreach (var chunk in audioSnapshot)
                    await fallback.SendAudioAsync(chunk);

                await fallback.FinishStreamAsync();
                await fallback.WaitForFinalResultAsync();
                await fallback.DisconnectAsync();

                var completed = await Task.WhenAny(finalTcs.Task, errorTcs.Task, Task.Delay(1500));
                if (completed == finalTcs.Task)
                {
                    var finalText = await finalTcs.Task;
                    await HandleFinalTranscriptionAsync(finalText, fallbackProvider, ResolveSttModelForProvider(fallbackProvider));
                    return true;
                }

                if (completed == errorTcs.Task)
                    Logger.Log($"Fallback provider {fallbackProvider} failed: {await errorTcs.Task}");
            }
            catch (Exception ex)
            {
                Logger.LogException("TryRunSttFailoverAsync", ex);
            }
            finally
            {
                fallback?.Dispose();
            }

            return false;
        }

        private void HandleSessionFailure(string error, string errorCode)
        {
            SetSessionState(SessionState.Error);
            _overlay?.SetStatus("ERROR", Brushes.OrangeRed);

            HistoryManager.AddEntry(
                original: string.Empty,
                refined: string.Empty,
                sttProvider: ConfigManager.Config.SttModel,
                sttModel: ResolveActiveSttModel(),
                refinementProvider: ConfigManager.Config.EnableRefinement ? ConfigManager.Config.RefinementModel : "Disabled",
                refinementModel: ConfigManager.Config.EnableRefinement ? ResolveActiveRefinementModel() : string.Empty,
                recordMs: _recordMs,
                transcribeMs: _transcribeMs,
                refineMs: _refineMs,
                insertMs: _insertMs,
                succeeded: false,
                errorCode: errorCode,
                insertionMethod: "None",
                profileId: _activeSessionProfile?.Id ?? string.Empty,
                profileName: _activeSessionProfile?.Name ?? "Default",
                failoverAttempted: _sttFailoverAttempted,
                failoverFrom: _failoverFromProvider,
                failoverTo: _failoverToProvider,
                finalProviderUsed: string.Empty);

            StatisticsManager.RecordSession(new SessionMetricEntry
            {
                Timestamp = DateTime.Now,
                SttProvider = ConfigManager.Config.SttModel,
                SttModel = ResolveActiveSttModel(),
                RefinementProvider = ConfigManager.Config.EnableRefinement ? ConfigManager.Config.RefinementModel : "Disabled",
                RefinementModel = ConfigManager.Config.EnableRefinement ? ResolveActiveRefinementModel() : string.Empty,
                RecordMs = _recordMs,
                TranscribeMs = _transcribeMs,
                RefineMs = _refineMs,
                InsertMs = _insertMs,
                Succeeded = false,
                ErrorCode = errorCode,
                ProfileId = _activeSessionProfile?.Id ?? string.Empty,
                ProfileName = _activeSessionProfile?.Name ?? "Default",
                FailoverAttempted = _sttFailoverAttempted,
                FailoverFrom = _failoverFromProvider,
                FailoverTo = _failoverToProvider
            });

            Dispatcher.Invoke(() =>
            {
                var vm = MainWindow.DataContext as MainViewModel;
                vm?.SetLastInsertionStatus("N/A", false, errorCode);
                MessageBox.Show($"Transcription Error: {error}", "Speakly Error", MessageBoxButton.OK, MessageBoxImage.Error);
            });

            SetSessionState(SessionState.Idle);
        }

        private string ResolveFallbackProvider()
        {
            var current = ConfigManager.Config.SttModel?.Trim() ?? string.Empty;
            var configuredOrder = ConfigManager.Config.SttFailoverOrder ?? new List<string>();

            foreach (var candidate in configuredOrder)
            {
                if (string.IsNullOrWhiteSpace(candidate)) continue;
                if (string.Equals(candidate, current, StringComparison.OrdinalIgnoreCase)) continue;
                if (!HasApiKeyForSttProvider(candidate)) continue;
                return candidate;
            }

            foreach (var candidate in new[] { "Deepgram", "OpenAI", "OpenRouter" })
            {
                if (string.Equals(candidate, current, StringComparison.OrdinalIgnoreCase)) continue;
                if (!HasApiKeyForSttProvider(candidate)) continue;
                return candidate;
            }

            return string.Empty;
        }

        private static bool HasApiKeyForSttProvider(string provider)
        {
            return provider.Trim().ToLowerInvariant() switch
            {
                "deepgram" => !string.IsNullOrWhiteSpace(ConfigManager.Config.DeepgramApiKey),
                "openai" => !string.IsNullOrWhiteSpace(ConfigManager.Config.OpenAIApiKey),
                "openrouter" => !string.IsNullOrWhiteSpace(ConfigManager.Config.OpenRouterApiKey),
                _ => false
            };
        }

        public async Task ToggleRecordingFromOverlayAsync()
        {
            if (_recorder == null) return;

            if (_recorder.IsRecording)
            {
                _isToggleRecording = false;
                await StopRecordingAsync();
                return;
            }

            if (!TryEnterRecording()) return;
            BeginSessionTiming();

            _lastActiveWindow = TextInserter.GetForegroundWindow();
            PrepareSessionContext();
            Logger.Log($"Overlay Recording Started. Captured active window: {_lastActiveWindow}");
            _sessionText.Clear();
            _sessionHasInserted = false;
            AutoAdjustRefinementPromptForLanguage();
            _overlay?.SetActiveLanguage(ResolveOverlayLanguageDisplay());
            _isToggleRecording = true;
            _overlay?.SetStatus("RECORDING", Brushes.OrangeRed);
            _startSound?.Play();

            var connectTask = _transcriber != null ? _transcriber.ConnectAsync() : Task.CompletedTask;

            if (ConfigManager.Config.SaveDebugRecords)
            {
                _audioBuffer = new System.IO.MemoryStream();
            }

            _recorder.StartRecording();
            await connectTask;
        }

        public async Task StopRecordingFromOverlayAsync()
        {
            if (_recorder == null || !_recorder.IsRecording) return;
            _isToggleRecording = false;
            await StopRecordingAsync();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _hotkeyService?.Dispose();
            _trayService?.Dispose();
            _recorder?.Dispose();
            _transcriber?.Dispose();
            _overlay?.Close();
            _startSound?.Dispose();
            _stopSound?.Dispose();
            
            Logger.Log("Application exiting. Forcefully terminating process.");
            base.OnExit(e);
            
            // Hard exit to ensure no "ghost" processes remain from background threads or hidden windows
            System.Environment.Exit(0);
        }

        public static string AppVersion { get; } =
            System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

        public static void SetTheme(string themeName)
        {
            bool isLight = string.Equals(themeName, "Light", StringComparison.OrdinalIgnoreCase);
            var appTheme = isLight ? ApplicationTheme.Light : ApplicationTheme.Dark;
            var merged = Application.Current.Resources.MergedDictionaries;

            // Replace the ThemesDictionary with a fresh instance.
            // Setting .Theme on the existing instance does NOT re-initialize nav pane brushes.
            // A new object recreates every brush key from scratch for the requested theme.
            for (int i = 0; i < merged.Count; i++)
            {
                if (merged[i] is Wpf.Ui.Markup.ThemesDictionary)
                {
                    merged[i] = new Wpf.Ui.Markup.ThemesDictionary { Theme = appTheme };
                    break;
                }
            }

            // Sync ApplicationThemeManager's internal state and update window DWM attributes.
            // WindowBackdropType.None prevents Mica backdrop from being applied – Mica inherits
            // the OS system theme, so it would stay dark-rendered even when the app switches to
            // Light, causing the NavigationView pane to appear black.
            ApplicationThemeManager.Apply(appTheme, Wpf.Ui.Controls.WindowBackdropType.None, updateAccent: true);

            // Explicitly flip the Win32 DWMWA_USE_IMMERSIVE_DARK_MODE attribute on the main
            // window so the title bar and NavigationView pane chrome match the app theme.
            var mainWindow = Application.Current.MainWindow;
            if (mainWindow != null)
            {
                if (isLight)
                    WindowBackgroundManager.RemoveDarkThemeFromWindow(mainWindow);
                else
                    WindowBackgroundManager.ApplyDarkThemeToWindow(mainWindow);
            }

            // Swap the custom overlay/capture brush file (Dark vs Light colours)
            string fileName = isLight ? "LightTheme.xaml" : "DarkTheme.xaml";
            string dictUri = $"pack://application:,,,/Themes/{fileName}";
            try
            {
                var overlayDict = new ResourceDictionary { Source = new Uri(dictUri, UriKind.Absolute) };
                for (int i = merged.Count - 1; i >= 0; i--)
                {
                    var src = merged[i].Source?.ToString() ?? string.Empty;
                    if (IsBaseThemeDictionary(src))
                        merged.RemoveAt(i);
                }
                merged.Add(overlayDict);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Overlay theme load failed: {ex.Message}");
            }

            SetOverlaySkin(ConfigManager.Config.OverlaySkin);
        }

        public static void SetOverlaySkin(string skinName)
        {
            var merged = Application.Current.Resources.MergedDictionaries;
            var normalizedSkin = NormalizeOverlaySkinName(skinName);
            string dictUri = $"pack://application:,,,/Themes/OverlaySkins/{normalizedSkin}.xaml";

            try
            {
                var skinDict = new ResourceDictionary { Source = new Uri(dictUri, UriKind.Absolute) };
                for (int i = merged.Count - 1; i >= 0; i--)
                {
                    var src = merged[i].Source?.ToString() ?? string.Empty;
                    if (src.Contains("/Themes/OverlaySkins/", StringComparison.OrdinalIgnoreCase))
                        merged.RemoveAt(i);
                }

                merged.Add(skinDict);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Overlay skin load failed: {ex.Message}");
            }

            // Push new brush values to the overlay immediately (manual bindings break DynamicResource)
            if (Application.Current is App app)
                app._overlay?.RefreshSkin();
        }

        public static void SetOverlayVisible(bool visible)
        {
            if (Application.Current is not App app) return;

            if (visible)
            {
                if (app._overlay == null || !app._overlay.IsLoaded)
                {
                    app._overlay = new FloatingOverlay();
                    app._overlay.Show();
                }
                else if (!app._overlay.IsVisible)
                {
                    app._overlay.Show();
                }

                app._overlay.EnsureVisibleOnScreen();
                app._overlay.Topmost = true;
            }
            else
            {
                app._overlay?.Close();
                app._overlay = null;
            }
        }

        public static void RecoverOverlayPosition()
        {
            if (Application.Current is not App app) return;

            if (app._overlay == null || !app._overlay.IsLoaded)
            {
                app._overlay = new FloatingOverlay();
                app._overlay.Show();
            }
            else if (!app._overlay.IsVisible)
            {
                app._overlay.Show();
            }

            app._overlay.EnsureVisibleOnScreen();
            app._overlay.Topmost = true;
            app._overlay.Activate();
        }

        private static bool IsBaseThemeDictionary(string source)
        {
            return BaseThemeDictionaryUris.Any(
                uri => source.Contains(uri, StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeOverlaySkinName(string? skinName)
        {
            if (string.IsNullOrWhiteSpace(skinName)) return "Lavender";

            if (string.Equals(skinName, "Midnight", StringComparison.OrdinalIgnoreCase)) return "Midnight";
            if (string.Equals(skinName, "Sakura", StringComparison.OrdinalIgnoreCase)) return "Sakura";
            if (string.Equals(skinName, "Forest", StringComparison.OrdinalIgnoreCase)) return "Forest";
            if (string.Equals(skinName, "Ember", StringComparison.OrdinalIgnoreCase)) return "Ember";

            return "Lavender";
        }

        private void BeginSessionTiming()
        {
            _recordingStartedUtc = DateTime.UtcNow;
            _transcribingStartedUtc = _recordingStartedUtc;
            _recordMs = 0;
            _transcribeMs = 0;
            _refineMs = 0;
            _insertMs = 0;
            _finalTranscriptionProcessed = false;
            _sttFailoverAttempted = false;
            _failoverFromProvider = string.Empty;
            _failoverToProvider = string.Empty;
            lock (_audioChunkLock)
            {
                _sessionAudioChunks.Clear();
            }
        }

        private void PrepareSessionContext()
        {
            try
            {
                _activeSessionProfile = ProfileResolverService.ResolveForForegroundWindow(_lastActiveWindow);
                ConfigManager.SetActiveProfile(_activeSessionProfile.Id);
                ConfigManager.EnsureProfileSyncToLegacyFields(_activeSessionProfile);
                InitializeTranscriptionAndRefinement();

                Dispatcher.Invoke(() =>
                {
                    var match = ViewModel?.Profiles.FirstOrDefault(p =>
                        string.Equals(p.Id, _activeSessionProfile.Id, StringComparison.OrdinalIgnoreCase));
                    if (match != null && ViewModel != null)
                    {
                        ViewModel.SelectedProfile = match;
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.LogException("PrepareSessionContext", ex);
                _activeSessionProfile = ConfigManager.GetActiveProfile();
            }
        }

        private static string ResolveActiveSttModel()
        {
            return ResolveSttModelForProvider(ConfigManager.Config.SttModel);
        }

        private static string ResolveSttModelForProvider(string provider)
        {
            return provider switch
            {
                "OpenAI" => ConfigManager.Config.OpenAISttModel,
                "Deepgram" => ConfigManager.Config.DeepgramModel,
                "OpenRouter" => ConfigManager.Config.OpenRouterSttModel,
                _ => string.Empty
            };
        }

        private static string ResolveActiveRefinementModel()
        {
            return ConfigManager.Config.RefinementModel switch
            {
                "OpenRouter" => ConfigManager.Config.OpenRouterRefinementModel,
                "Cerebras" => ConfigManager.Config.CerebrasRefinementModel,
                "OpenAI" => ConfigManager.Config.OpenAIRefinementModel,
                _ => string.Empty
            };
        }

        private bool TryEnterRecording()
        {
            lock (_sessionLock)
            {
                if (_sessionState != SessionState.Idle) return false;
                _sessionState = SessionState.Recording;
                return true;
            }
        }

        private bool TryEnterTranscribing()
        {
            lock (_sessionLock)
            {
                if (_sessionState != SessionState.Recording && _sessionState != SessionState.Refining && _sessionState != SessionState.Transcribing)
                    return false;
                _sessionState = SessionState.Transcribing;
                return true;
            }
        }

        private void SetSessionState(SessionState state)
        {
            lock (_sessionLock)
            {
                _sessionState = state;
            }
        }
    }
}
