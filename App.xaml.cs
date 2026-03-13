using System.Linq;
using System.Media;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Velopack;
using Velopack.Sources;
using Wpf.Ui.Appearance;
using Speakly.Config;
using Speakly.Services;
using Speakly.ViewModels;

namespace Speakly
{
    public partial class App : Application
    {
        private const string GitHubUpdateRepoUrl = "https://github.com/snook89/Speakly";

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
        private CancellationTokenSource? _pendingPttReleaseStopCts;
        private static readonly TimeSpan PttReleaseDebounce = TimeSpan.FromMilliseconds(120);
        private static readonly TimeSpan PttReleaseConfirmInterval = TimeSpan.FromMilliseconds(40);
        private const int PttReleaseConfirmChecks = 2;
        private readonly DispatcherTimer _overlayIdleTimer = new();
        private readonly DispatcherTimer _deferredPasteTimer = new();
        private DateTime _overlayLastActivityUtc = DateTime.UtcNow;
        private bool _overlayHiddenByIdle;
        private static readonly TimeSpan OverlayIdleHideAfter = TimeSpan.FromSeconds(90);
        private static readonly TimeSpan PostStopFinalResultGrace = TimeSpan.FromMilliseconds(350);
        private const string NoFinalResultErrorCode = "stt_no_final_result";
        private const string NoMicSignalErrorCode = "mic_no_signal";
        private const int NoMicSignalWarningThresholdMs = 900;
        private const int MeaningfulMicSignalThresholdMs = 180;
        private const float MicSignalRmsThreshold = 0.0035f;
        private const float MicSignalPeakThreshold = 0.0125f;
        private SingleInstanceManager? _singleInstanceManager;
        private readonly PendingTransferManager _pendingTransferManager = new();
        private IAudioFrameProcessor _audioProcessor = new ManagedAudioProcessor();
        private bool _deferredPasteApplyInProgress;
        private TargetWindowContext _targetWindowContext = TargetWindowContext.Empty;
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
        private string _lastInsertedBuffer = string.Empty;
        private DateTime _recordingStartedUtc;
        private DateTime _transcribingStartedUtc;
        private int _recordMs;
        private int _transcribeMs;
        private int _refineMs;
        private int _insertMs;
        private readonly object _audioChunkLock = new();
        private readonly List<byte[]> _sessionAudioChunks = new();
        private readonly object _finalSegmentLock = new();
        private readonly List<string> _finalTranscriptSegments = new();
        private readonly object _interimTranscriptLock = new();
        private string _latestInterimTranscript = string.Empty;
        private int _sessionMeaningfulSignalMs;
        private int _sessionSilenceMs;
        private bool _noMicSignalWarningShown;
        private bool _finalTranscriptionProcessed;
        private bool _stopRequested;
        private bool _sttFailoverAttempted;
        private string _sessionSttProvider = string.Empty;
        private string _sessionSttModel = string.Empty;
        private string _latestTranscriberErrorCode = string.Empty;
        private string _latestTranscriberErrorMessage = string.Empty;
        private AppProfile? _activeSessionProfile;
        private string _failoverFromProvider = string.Empty;
        private string _failoverToProvider = string.Empty;
        private string _activeSessionId = string.Empty;
        private string _activeOperationId = string.Empty;
        private bool _updateCheckStarted;
        private readonly System.Threading.SemaphoreSlim _updateCheckGate = new(1, 1);
        private readonly object _elevationPromptLock = new();
        private DateTime _lastElevationPromptUtc = DateTime.MinValue;
        private static readonly TimeSpan ElevationPromptCooldown = TimeSpan.FromSeconds(60);
        private bool _launchedFromWindowsStartup;
        private string _latestContextSummary = string.Empty;
        private const int DwmUseImmersiveDarkMode = 20;
        private const int DwmUseImmersiveDarkModeLegacy = 19;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

        public App()
        {
            VelopackApp.Build().Run();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _singleInstanceManager = new SingleInstanceManager();
            if (!_singleInstanceManager.TryAcquirePrimaryInstance())
            {
                SingleInstanceManager.SignalPrimaryInstance();
                Shutdown();
                return;
            }

            ConfigManager.Load();
            _launchedFromWindowsStartup = e.Args.Any(arg =>
                string.Equals(arg, StartupRegistrationService.StartupLaunchArgument, StringComparison.OrdinalIgnoreCase));
            if (!StartupRegistrationService.Reconcile(ConfigManager.Config.StartWithWindows, out var startupTaskStatus))
            {
                Logger.Log($"Startup task reconcile failed: {startupTaskStatus}");
            }
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

            _recorder = new NAudioRecorder();
            _recorder.AudioDataAvailable += OnAudioDataAvailable;

            InitializeTranscriptionAndRefinement();

            ViewModel = new MainViewModel();
            ViewModel.RunHealthChecks();
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            ViewModel.SetLastContextUsageStatus(BuildContextUsageStatus(_latestContextSummary));
            if (_hotkeyService.IsHookInstalled)
            {
                _hotkeyService.KeyDown += OnPTTPressed;
                _hotkeyService.KeyUp += OnPTTReleased;
            }
            else
            {
                ReportHotkeyHookInitializationFailure();
            }

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
                UpdateOverlayModeIndicator();
                UpdateOverlayContextIndicator(_latestContextSummary);
                _overlayHiddenByIdle = false;
            }

            SetOverlayStyle(ConfigManager.Config.OverlayStyle);

            if (_launchedFromWindowsStartup && ConfigManager.Config.MinimizeToTray)
            {
                MainWindow.WindowState = WindowState.Minimized;
            }

            MainWindow.Show();
            _singleInstanceManager.StartActivationListener(() =>
                Dispatcher.BeginInvoke(new Action(ActivateFromSecondaryInstance)));
            ConfigureOverlayIdleBehavior();
            ConfigureDeferredPasteBehavior();
            // Re-apply theme after main window handle exists so title bar chrome matches app theme.
            SetTheme(ConfigManager.Config.Theme);
            TelemetryManager.Track(
                name: "app_start",
                level: "info",
                result: "ok",
                data: new Dictionary<string, string>
                {
                    ["version"] = AppVersion,
                    ["theme"] = ConfigManager.Config.Theme,
                    ["stt_provider"] = ConfigManager.Config.SttModel,
                    ["refinement_provider"] = ConfigManager.Config.RefinementModel
                });

            _ = CheckForAppUpdatesAsync(userInitiated: false, includeStartupDelay: true);
        }

        public static Task<string> CheckForUpdatesNowAsync()
        {
            if (Current is App app)
                return app.CheckForAppUpdatesAsync(userInitiated: true, includeStartupDelay: false);

            return Task.FromResult("Application instance is unavailable.");
        }

        public static string GetDisplayVersion()
        {
            if (Current is App app)
                return app.ResolveDisplayVersion();

            return AppVersion;
        }

        public static Task RetryHistoryInsertAsync(HistoryEntry entry)
        {
            if (Current is App app)
            {
                return app.RetryHistoryInsertInternalAsync(entry);
            }

            return Task.CompletedTask;
        }

        public static Task ReprocessHistoryEntryAsync(HistoryEntry entry)
        {
            if (Current is App app)
            {
                return app.ReprocessHistoryEntryInternalAsync(entry);
            }

            return Task.CompletedTask;
        }

        private string ResolveDisplayVersion()
        {
            try
            {
                var source = new GithubSource(GitHubUpdateRepoUrl, accessToken: null, prerelease: false);
                var updateManager = new UpdateManager(source);
                var installedVersion = updateManager.CurrentVersion?.ToString();
                if (!string.IsNullOrWhiteSpace(installedVersion))
                    return installedVersion;
            }
            catch
            {
                // Fallback to assembly version when update metadata is unavailable.
            }

            return AppVersion;
        }

        private async Task RetryHistoryInsertInternalAsync(HistoryEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.RefinedText))
            {
                return;
            }

            var targetContext = TextInserter.CaptureForegroundWindowContext();
            UpdateOverlayContextIndicator(string.Empty);
            _overlay?.SetStatus("RETRY", Brushes.Orange);
            var swInsert = Stopwatch.StartNew();
            var insertResult = await Task.Run(() => TextInserter.InsertText(entry.RefinedText, targetContext));
            swInsert.Stop();
            if (insertResult.Success)
            {
                _lastInsertedBuffer = entry.RefinedText;
            }

            PublishHistoryEntry(new HistoryEntry
            {
                Timestamp = DateTime.Now,
                OriginalText = entry.OriginalText,
                RefinedText = entry.RefinedText,
                SttProvider = entry.SttProvider,
                SttModel = entry.SttModel,
                RefinementProvider = entry.RefinementProvider,
                RefinementModel = entry.RefinementModel,
                RecordMs = 0,
                TranscribeMs = 0,
                RefineMs = 0,
                InsertMs = (int)swInsert.ElapsedMilliseconds,
                Succeeded = insertResult.Success,
                InsertionMethod = $"HistoryRetry:{insertResult.Method}",
                ErrorCode = insertResult.ErrorCode,
                ProfileId = entry.ProfileId,
                ProfileName = entry.ProfileName,
                DictationMode = DictationExperienceService.NormalizeMode(entry.DictationMode),
                ContextualRefinementMode = DictationExperienceService.NormalizeContextualRefinementMode(entry.ContextualRefinementMode),
                ContextSummary = DictationExperienceService.BuildContextSummary(ConfigManager.Config, targetContext),
                ActionSource = "HistoryRetry",
                SourceEntryId = entry.Id,
                SourceActionSource = entry.ActionSource,
                SourceRefinedText = entry.RefinedText,
                SourceTimestamp = entry.Timestamp
            });
            _overlay?.SetStatus("READY", Brushes.Aqua);
        }

        private async Task ReprocessHistoryEntryInternalAsync(HistoryEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.OriginalText))
            {
                return;
            }

            var targetContext = TextInserter.CaptureForegroundWindowContext();
            var activeProfile = ConfigManager.GetActiveProfile();
            var dictionaryTerms = PersonalDictionaryService.GetCombinedTerms(
                ConfigManager.Config,
                activeProfile,
                maxTerms: 80);
            string correctedTranscript = PersonalDictionaryService.ApplyCorrections(
                entry.OriginalText,
                dictionaryTerms,
                out _);

            string activeMode = DictationExperienceService.NormalizeMode(activeProfile.DictationMode);
            string contextSummary;
            string textToInsert = correctedTranscript;
            string learnedCandidateText = correctedTranscript;
            int refineMs = 0;
            var refinementContext = CaptureSupplementalRefinementContext(targetContext);
            var refinementProvider = activeProfile.RefinementEnabled ? activeProfile.RefinementProvider : "Disabled";
            var refinementModel = activeProfile.RefinementEnabled
                ? ConfigManager.ResolveRefinementModel(activeProfile.RefinementProvider, activeProfile.RefinementModel)
                : string.Empty;
            var contextualMode = DictationExperienceService.NormalizeContextualRefinementMode(activeProfile.ContextualRefinementMode);
            _overlay?.SetStatus("REPROCESS", Brushes.Yellow);

            if (activeProfile.RefinementEnabled)
            {
                var refiner = TextRefinerFactory.CreateRefiner(activeProfile.RefinementProvider);
                var prompt = DictationExperienceService.BuildEffectivePrompt(
                    ConfigManager.Config,
                    activeProfile,
                    targetContext,
                    refinementContext,
                    out contextSummary);
                UpdateOverlayContextIndicator(contextSummary);
                try
                {
                    var swRefine = Stopwatch.StartNew();
                    textToInsert = await refiner.RefineTextAsync(
                        RefinementRequest.Create(
                            correctedTranscript,
                            prompt,
                            refinementModel,
                            contextualMode));
                    swRefine.Stop();
                    refineMs = (int)swRefine.ElapsedMilliseconds;
                    learnedCandidateText = textToInsert;
                }
                catch
                {
                    textToInsert = correctedTranscript;
                    learnedCandidateText = correctedTranscript;
                    refineMs = 0;
                }
            }
            else
            {
                contextSummary = DictationExperienceService.BuildContextSummary(ConfigManager.Config, targetContext, refinementContext);
                UpdateOverlayContextIndicator(string.Empty);
            }

            if (ConfigManager.Config.EnableSnippets)
            {
                textToInsert = SnippetLibraryManager.Apply(textToInsert, SnippetLibraryManager.Load(), out _);
            }

            var swInsert = Stopwatch.StartNew();
            var insertResult = await Task.Run(() => TextInserter.InsertText(textToInsert, targetContext));
            swInsert.Stop();
            if (insertResult.Success)
            {
                _lastInsertedBuffer = textToInsert;
            }

            PublishHistoryEntry(new HistoryEntry
            {
                Timestamp = DateTime.Now,
                OriginalText = entry.OriginalText,
                RefinedText = textToInsert,
                SttProvider = entry.SttProvider,
                SttModel = entry.SttModel,
                RefinementProvider = refinementProvider,
                RefinementModel = refinementModel,
                RecordMs = 0,
                TranscribeMs = 0,
                RefineMs = refineMs,
                InsertMs = (int)swInsert.ElapsedMilliseconds,
                Succeeded = insertResult.Success,
                InsertionMethod = $"HistoryReprocess:{insertResult.Method}",
                ErrorCode = insertResult.ErrorCode,
                ProfileId = activeProfile.Id,
                ProfileName = activeProfile.Name,
                DictationMode = activeMode,
                ContextualRefinementMode = contextualMode,
                ContextSummary = contextSummary,
                ActionSource = "HistoryReprocess",
                SourceEntryId = entry.Id,
                SourceActionSource = entry.ActionSource,
                SourceRefinedText = entry.RefinedText,
                SourceTimestamp = entry.Timestamp
            });
            _overlay?.SetStatus("READY", Brushes.Aqua);
        }

        private void ActivateFromSecondaryInstance()
        {
            try
            {
                if (MainWindow == null)
                {
                    return;
                }

                if (!MainWindow.IsVisible)
                {
                    MainWindow.Show();
                }

                if (MainWindow.WindowState == WindowState.Minimized)
                {
                    MainWindow.WindowState = WindowState.Normal;
                }

                MainWindow.Topmost = true;
                MainWindow.Activate();
                MainWindow.Topmost = false;
                MainWindow.Focus();

                if (ConfigManager.Config.ShowOverlay)
                {
                    EnsureOverlayVisibleInternal(activate: false);
                }

                MarkOverlayActivity(showOverlayIfNeeded: false);
            }
            catch (Exception ex)
            {
                Logger.LogException("ActivateFromSecondaryInstance", ex);
            }
        }

        private void CaptureTargetWindowContext(string triggerSource)
        {
            _targetWindowContext = TextInserter.CaptureForegroundWindowContext();
            _lastActiveWindow = _targetWindowContext.Handle;

            Logger.Log($"{triggerSource}. Captured target window: {_targetWindowContext}");
            ViewModel?.UpdateTargetProfileMatch(_targetWindowContext);
        }

        private void ConfigureOverlayIdleBehavior()
        {
            _overlayLastActivityUtc = DateTime.UtcNow;
            _overlayIdleTimer.Interval = TimeSpan.FromSeconds(5);
            _overlayIdleTimer.Tick += (_, _) => EvaluateOverlayIdlePolicy();
            _overlayIdleTimer.Start();
        }

        private void ConfigureDeferredPasteBehavior()
        {
            _deferredPasteTimer.Interval = TimeSpan.FromMilliseconds(350);
            _deferredPasteTimer.Tick += async (_, _) => await EvaluateDeferredPastePolicyAsync();
            _deferredPasteTimer.Start();
            RefreshPendingTransferStatus();
        }

        private async Task EvaluateDeferredPastePolicyAsync()
        {
            if (_deferredPasteApplyInProgress)
            {
                return;
            }

            try
            {
                var now = DateTime.UtcNow;
                var pending = _pendingTransferManager.GetActiveOrExpire(now, out var expired);
                if (expired != null)
                {
                    Logger.Log($"Deferred paste expired for {expired.TargetContext}.");
                    TelemetryManager.Track(
                        name: "deferred_paste_expired",
                        level: "warning",
                        success: false,
                        result: "expired",
                        errorCode: "deferred_paste_expired",
                        data: new Dictionary<string, string>
                        {
                            ["target_process"] = expired.TargetContext.ProcessName,
                            ["target_pid"] = expired.TargetContext.ProcessId.ToString(),
                            ["failure_code"] = expired.FailureCode
                        });
                    RefreshPendingTransferStatus();
                    return;
                }

                if (pending == null)
                {
                    RefreshPendingTransferStatus();
                    return;
                }

                if (!ConfigManager.Config.DeferredTargetPasteEnabled || IsSessionActive())
                {
                    RefreshPendingTransferStatus();
                    return;
                }

                var foreground = TextInserter.CaptureForegroundWindowContext();
                if (!PendingTransferManager.IsForegroundMatch(foreground, pending.TargetContext))
                {
                    RefreshPendingTransferStatus();
                    return;
                }

                _deferredPasteApplyInProgress = true;
                InsertResult deferredResult = new InsertResult
                {
                    Success = false,
                    Method = "DeferredNotExecuted",
                    ErrorCode = "deferred_not_executed"
                };
                await _insertionGate.WaitAsync();
                try
                {
                    deferredResult = await Task.Run(() => TextInserter.InsertText(pending.Text, foreground));
                }
                finally
                {
                    _insertionGate.Release();
                }

                _pendingTransferManager.TryConsume(pending.Id, out _);

                if (deferredResult.Success)
                {
                    Logger.Log($"Deferred paste applied successfully into {foreground}.");
                    TelemetryManager.Track(
                        name: "deferred_paste_applied",
                        level: "info",
                        result: deferredResult.Method,
                        operationId: pending.OperationId,
                        data: new Dictionary<string, string>
                        {
                            ["target_process"] = foreground.ProcessName,
                            ["target_pid"] = foreground.ProcessId.ToString(),
                            ["method"] = deferredResult.Method
                        });

                    Dispatcher.Invoke(() =>
                    {
                        var vm = MainWindow?.DataContext as MainViewModel ?? ViewModel;
                        vm?.SetLastInsertionStatus($"{deferredResult.Method}+Deferred", true, string.Empty);
                    });
                }
                else
                {
                    Logger.Log($"Deferred paste failed ({deferredResult.ErrorCode}) for {foreground}.");
                    TelemetryManager.Track(
                        name: "deferred_paste_failed",
                        level: "warning",
                        success: false,
                        result: deferredResult.Method,
                        operationId: pending.OperationId,
                        errorCode: deferredResult.ErrorCode,
                        errorClass: deferredResult.ErrorCode,
                        data: new Dictionary<string, string>
                        {
                            ["target_process"] = foreground.ProcessName,
                            ["target_pid"] = foreground.ProcessId.ToString()
                        });

                    Dispatcher.Invoke(() =>
                    {
                        var vm = MainWindow?.DataContext as MainViewModel ?? ViewModel;
                        vm?.SetLastInsertionStatus($"{deferredResult.Method}+Deferred", false, deferredResult.ErrorCode);
                    });

                    TryOfferOnDemandElevation(deferredResult, "deferred_paste_apply", foreground);
                }

                RefreshPendingTransferStatus();
            }
            catch (Exception ex)
            {
                Logger.LogException("EvaluateDeferredPastePolicyAsync", ex);
            }
            finally
            {
                _deferredPasteApplyInProgress = false;
            }
        }

        private void RefreshPendingTransferStatus()
        {
            try
            {
                var now = DateTime.UtcNow;
                var pending = _pendingTransferManager.GetActiveOrExpire(now, out var expired);
                if (expired != null)
                {
                    Logger.Log($"Deferred paste expired for {expired.TargetContext}.");
                }
                string status;
                if (pending == null)
                {
                    status = "No pending auto-paste.";
                }
                else if (!ConfigManager.Config.DeferredTargetPasteEnabled)
                {
                    status = $"Pending auto-paste paused for {pending.TargetDisplayName}.";
                }
                else
                {
                    status = $"Pending auto-paste: return to {pending.TargetDisplayName}.";
                }

                Dispatcher.Invoke(() =>
                {
                    var vm = MainWindow?.DataContext as MainViewModel ?? ViewModel;
                    vm?.SetPendingTransferStatus(status);
                });
            }
            catch (Exception ex)
            {
                Logger.LogException("RefreshPendingTransferStatus", ex);
            }
        }

        private bool ShouldQueueDeferredPaste(InsertionFailureReason reason)
        {
            return reason is InsertionFailureReason.FocusRestoreFailed
                or InsertionFailureReason.MissingTarget
                or InsertionFailureReason.TargetWindowUnavailable
                or InsertionFailureReason.InputBlockedByIntegrity;
        }

        private bool TryOfferOnDemandElevation(InsertResult insertResult, string source, TargetWindowContext targetContext)
        {
            if (insertResult.FailureReason != InsertionFailureReason.InputBlockedByIntegrity)
            {
                return false;
            }

            if (PrivilegeService.IsCurrentProcessElevated())
            {
                return false;
            }

            if (IsElevationPromptOnCooldown())
            {
                Logger.Log($"Skipping elevation prompt due to cooldown ({source}).");
                return false;
            }

            var targetName = string.IsNullOrWhiteSpace(targetContext.ProcessName)
                ? "the target app"
                : targetContext.ProcessName;

            Logger.Log($"Offering on-demand elevation ({source}, target={targetName}).");

            bool restartElevated = false;
            Dispatcher.Invoke(() =>
            {
                restartElevated = MessageBox.Show(
                    $"Windows blocked text insertion into {targetName} because it is running with higher privileges." + Environment.NewLine +
                    "Restart Speakly as administrator now?",
                    "Speakly Permission Required",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) == MessageBoxResult.Yes;
            });

            if (!restartElevated)
            {
                Logger.Log("User declined on-demand elevation prompt.");
                return false;
            }

            if (PrivilegeService.TryRestartElevated())
            {
                Logger.Log("On-demand elevation restart succeeded. Shutting down current instance.");
                Dispatcher.BeginInvoke(new Action(Shutdown));
                return true;
            }

            Dispatcher.Invoke(() =>
            {
                MessageBox.Show(
                    "Could not restart Speakly as administrator. You can continue using clipboard paste for this target.",
                    "Speakly Permission Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            });
            return false;
        }

        private bool IsElevationPromptOnCooldown()
        {
            lock (_elevationPromptLock)
            {
                var now = DateTime.UtcNow;
                if (now - _lastElevationPromptUtc < ElevationPromptCooldown)
                {
                    return true;
                }

                _lastElevationPromptUtc = now;
                return false;
            }
        }

        private bool TryQueueDeferredPaste(string textToInsert, InsertResult insertResult)
        {
            if (!ConfigManager.Config.DeferredTargetPasteEnabled)
            {
                return false;
            }

            if (!insertResult.ClipboardUpdated || !ShouldQueueDeferredPaste(insertResult.FailureReason))
            {
                return false;
            }

            if (!_targetWindowContext.IsValid || string.IsNullOrWhiteSpace(textToInsert))
            {
                return false;
            }

            var createdAt = DateTime.UtcNow;
            int ttlSeconds = Math.Clamp(ConfigManager.Config.DeferredTargetPasteTtlSeconds, 0, 604800);
            DateTime? expiresAtUtc = ttlSeconds > 0
                ? createdAt.AddSeconds(ttlSeconds)
                : null;
            var pending = new PendingTransfer(
                text: textToInsert,
                targetContext: _targetWindowContext,
                createdAtUtc: createdAt,
                expiresAtUtc: expiresAtUtc,
                failureCode: insertResult.ErrorCode,
                operationId: _activeOperationId);

            var replaced = _pendingTransferManager.Replace(pending);
            if (replaced != null)
            {
                Logger.Log($"Replaced deferred paste queued for {replaced.TargetContext} with new target {pending.TargetContext}.");
                TrackSessionEvent(
                    name: "deferred_paste_replaced",
                    level: "warning",
                    result: "replaced",
                    data: new Dictionary<string, string>
                    {
                        ["previous_target"] = replaced.TargetContext.ProcessName,
                        ["new_target"] = pending.TargetContext.ProcessName
                    });
            }

            Logger.Log($"Queued deferred paste for {pending.TargetContext} (ttl={(ttlSeconds <= 0 ? "infinite" : $"{ttlSeconds}s")}, reason={insertResult.ErrorCode}).");
            TrackSessionEvent(
                name: "deferred_paste_queued",
                level: "info",
                result: "queued",
                errorCode: insertResult.ErrorCode,
                errorClass: insertResult.ErrorCode,
                data: new Dictionary<string, string>
                {
                    ["target_process"] = pending.TargetContext.ProcessName,
                    ["target_pid"] = pending.TargetContext.ProcessId.ToString(),
                    ["ttl_seconds"] = ttlSeconds.ToString()
                });
            RefreshPendingTransferStatus();
            return true;
        }

        private void ClearDeferredPasteInternal(string reason)
        {
            var cleared = _pendingTransferManager.Clear();
            if (cleared == null)
            {
                RefreshPendingTransferStatus();
                return;
            }

            Logger.Log($"Cleared deferred paste for {cleared.TargetContext} (reason={reason}).");
            TelemetryManager.Track(
                name: "deferred_paste_cleared",
                level: "info",
                result: reason,
                operationId: cleared.OperationId,
                data: new Dictionary<string, string>
                {
                    ["target_process"] = cleared.TargetContext.ProcessName,
                    ["target_pid"] = cleared.TargetContext.ProcessId.ToString()
                });
            RefreshPendingTransferStatus();
        }

        private void MarkOverlayActivity(bool showOverlayIfNeeded)
        {
            _overlayLastActivityUtc = DateTime.UtcNow;
            if (showOverlayIfNeeded && ConfigManager.Config.ShowOverlay)
            {
                EnsureOverlayVisibleInternal(activate: false);
            }
        }

        private void EvaluateOverlayIdlePolicy()
        {
            if (!ConfigManager.Config.ShowOverlay)
            {
                _overlayHiddenByIdle = false;
                return;
            }

            if (!ConfigManager.Config.OverlayAutoHideEnabled)
            {
                if (_overlayHiddenByIdle)
                {
                    EnsureOverlayVisibleInternal(activate: false);
                }
                return;
            }

            // Keep overlay visible while settings window is open.
            if (MainWindow != null && MainWindow.IsVisible)
            {
                return;
            }

            if (IsSessionActive())
            {
                if (_overlayHiddenByIdle)
                {
                    EnsureOverlayVisibleInternal(activate: false);
                }
                return;
            }

            if (_overlay == null || !_overlay.IsVisible)
            {
                return;
            }

            if (DateTime.UtcNow - _overlayLastActivityUtc < OverlayIdleHideAfter)
            {
                return;
            }

            _overlay.Hide();
            _overlayHiddenByIdle = true;
        }

        private bool IsSessionActive()
        {
            lock (_sessionLock)
            {
                return _sessionState != SessionState.Idle;
            }
        }

        private void EnsureOverlayVisibleInternal(bool activate, bool forceClampToVisibleArea = false)
        {
            if (!ConfigManager.Config.ShowOverlay)
            {
                return;
            }

            bool wasShownByThisCall = false;

            if (_overlay == null || !_overlay.IsLoaded)
            {
                _overlay = new FloatingOverlay();
                _overlay.Show();
                UpdateOverlayModeIndicator();
                UpdateOverlayContextIndicator(_latestContextSummary);
                wasShownByThisCall = true;
            }
            else if (!_overlay.IsVisible)
            {
                _overlay.Show();
                UpdateOverlayModeIndicator();
                UpdateOverlayContextIndicator(_latestContextSummary);
                wasShownByThisCall = true;
            }

            // Preserve exact user placement while overlay is already visible.
            // Clamp only when newly shown or when an explicit recovery asks for it.
            if (forceClampToVisibleArea || wasShownByThisCall)
            {
                _overlay.EnsureVisibleOnScreen();
            }

            _overlay.Topmost = true;
            if (activate)
            {
                _overlay.Activate();
            }

            _overlayHiddenByIdle = false;
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.DictationMode))
            {
                UpdateOverlayModeIndicator();
            }
            else if (e.PropertyName == nameof(MainViewModel.EnableRefinement))
            {
                UpdateOverlayContextIndicator(_latestContextSummary);
            }
        }

        private void UpdateOverlayModeIndicator()
        {
            _overlay?.SetActiveMode(DictationExperienceService.NormalizeMode(ConfigManager.Config.DictationMode));
        }

        private void UpdateOverlayContextIndicator(string? contextSummary)
        {
            _latestContextSummary = contextSummary?.Trim() ?? string.Empty;
            _overlay?.SetContextSummary(_latestContextSummary);

            Dispatcher.Invoke(() =>
            {
                var vm = MainWindow?.DataContext as MainViewModel ?? ViewModel;
                vm?.SetLastContextUsageStatus(BuildContextUsageStatus(_latestContextSummary));
            });
        }

        private static string BuildContextUsageStatus(string? contextSummary)
        {
            if (!ConfigManager.Config.EnableRefinement)
            {
                return "Context used: refinement disabled.";
            }

            var normalized = contextSummary?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return "Context used: none.";
            }

            return $"Context used: {normalized}";
        }

        private async Task<string> CheckForAppUpdatesAsync(bool userInitiated, bool includeStartupDelay)
        {
            if (!userInitiated)
            {
                if (_updateCheckStarted)
                    return "Startup update check already completed.";

                _updateCheckStarted = true;
            }

            bool lockAcquired;
            if (userInitiated)
            {
                // If startup check is in-flight, wait a bit so the manual action can still complete.
                lockAcquired = await _updateCheckGate.WaitAsync(TimeSpan.FromSeconds(20));
            }
            else
            {
                lockAcquired = await _updateCheckGate.WaitAsync(0);
            }

            if (!lockAcquired)
                return "Another update check is still running. Please try again in a few seconds.";

            try
            {
                if (includeStartupDelay)
                    await Task.Delay(TimeSpan.FromSeconds(8));

                string? githubToken = Environment.GetEnvironmentVariable("SPEAKLY_GITHUB_TOKEN");
                var source = new GithubSource(GitHubUpdateRepoUrl, githubToken, prerelease: false);
                var updateManager = new UpdateManager(source);

                if (!updateManager.IsInstalled)
                {
                    Logger.Log("Skipping update check: app is not running from a Velopack installation.");
                    return "Auto-update works only for Speakly installed via Setup. This local publish build cannot self-update.";
                }

                var pending = updateManager.UpdatePendingRestart;
                if (pending != null)
                {
                    Logger.Log($"Applying pending update {pending.Version}.");
                    updateManager.ApplyUpdatesAndRestart(pending);
                    return $"Applying pending update {pending.Version}.";
                }

                var updates = await updateManager.CheckForUpdatesAsync();
                if (updates == null)
                {
                    Logger.Log("No app updates found.");
                    return "You're up to date.";
                }

                Logger.Log($"Update available: {updates.TargetFullRelease.Version}. Downloading package.");
                await updateManager.DownloadUpdatesAsync(updates);

                bool applyNow = false;
                await Dispatcher.InvokeAsync(() =>
                {
                    var release = updates.TargetFullRelease;
                    var notes = UpdateReleaseNotesFormatter.Parse(
                        release.Version.ToString(),
                        release.NotesMarkdown,
                        release.NotesHTML);
                    var releaseUrl = $"{GitHubUpdateRepoUrl}/releases/tag/v{release.Version}";
                    var dialog = new UpdateReadyDialog(release.Version.ToString(), notes, releaseUrl);
                    if (MainWindow != null)
                    {
                        dialog.Owner = MainWindow;
                    }

                    applyNow = dialog.ShowDialog() == true;
                });

                if (applyNow)
                {
                    updateManager.ApplyUpdatesAndRestart(updates.TargetFullRelease);
                    return $"Update {updates.TargetFullRelease.Version} is applying now.";
                }

                return $"Update {updates.TargetFullRelease.Version} downloaded. Restart later to apply.";
            }
            catch (Exception ex)
            {
                Logger.LogException("CheckForAppUpdatesAsync", ex);
                return $"Update check failed: {ex.Message}";
            }
            finally
            {
                _updateCheckGate.Release();
            }
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

        private static bool IsKeyCurrentlyDown(Key key)
        {
            var virtualKey = KeyInterop.VirtualKeyFromKey(key);
            if (virtualKey <= 0) return false;
            return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        }

        private void CancelPendingPttReleaseStop()
        {
            if (_pendingPttReleaseStopCts == null) return;

            try { _pendingPttReleaseStopCts.Cancel(); }
            catch { }
            finally
            {
                _pendingPttReleaseStopCts.Dispose();
                _pendingPttReleaseStopCts = null;
            }
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
                CancelPendingPttReleaseStop();
                MarkOverlayActivity(showOverlayIfNeeded: true);
                if (_recorder != null && TryEnterRecording())
                {
                    BeginSessionTiming();
                    CaptureTargetWindowContext("PTT Hotkey Pressed");
                    PrepareSessionContext();
                    _sessionText.Clear();
                    _sessionHasInserted = false;
                    ResetAudioProcessorForSession();
                    UpdateOverlayContextIndicator(string.Empty);
                    AutoAdjustRefinementPromptForLanguage();
                    _overlay?.SetActiveLanguage(ResolveOverlayLanguageDisplay());
                    _overlay?.SetStatus("PTT_RECORDING", Brushes.LimeGreen);
                    _startSound?.Play();
                    
                    // Start connection in background
                    var connectTask = _transcriber != null ? _transcriber.ConnectAsync() : Task.CompletedTask;
                    
                    if (ConfigManager.Config.SaveDebugRecords) _audioBuffer = new System.IO.MemoryStream();
                    _recorder.StartRecording();
                    
                    // Ensure connection is up eventually (though streaming starts immediately via buffer)
                    await connectTask;
                    TrackSessionEvent("transcriber_connect", result: "ok");
                    return;
                }
            }

            // --- Toggle Record ---
            if (IsHotkeyMatch(ConfigManager.Config.RecordHotkey, e))
            {
                MarkOverlayActivity(showOverlayIfNeeded: true);
                if (!_isToggleRecording)
                {
                    if (!TryEnterRecording()) return;
                    BeginSessionTiming();

                    CaptureTargetWindowContext("Toggle Recording Started");
                    PrepareSessionContext();
                    _sessionText.Clear();
                    _sessionHasInserted = false;
                    ResetAudioProcessorForSession();
                    UpdateOverlayContextIndicator(string.Empty);
                    AutoAdjustRefinementPromptForLanguage();
                    _overlay?.SetActiveLanguage(ResolveOverlayLanguageDisplay());
                    _isToggleRecording = true;
                    _overlay?.SetStatus("RECORDING", Brushes.Red);
                    _startSound?.Play();

                    // Start connection in background
                    var connectTask = _transcriber != null ? _transcriber.ConnectAsync() : Task.CompletedTask;
                    
                    if (ConfigManager.Config.SaveDebugRecords) _audioBuffer = new System.IO.MemoryStream();
                    _recorder?.StartRecording();

                    // Ensure connection is up eventually
                    await connectTask;
                    TrackSessionEvent("transcriber_connect", result: "ok");
                }
                else
                {
                    _isToggleRecording = false;
                    await StopRecordingAsync("toggle_hotkey");
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
                        CancelPendingPttReleaseStop();
                        var releaseCts = new CancellationTokenSource();
                        _pendingPttReleaseStopCts = releaseCts;

                        try
                        {
                            await Task.Delay(PttReleaseDebounce, releaseCts.Token);
                            if (!ReferenceEquals(_pendingPttReleaseStopCts, releaseCts))
                            {
                                return;
                            }

                            for (int i = 0; i < PttReleaseConfirmChecks; i++)
                            {
                                bool stillDown = IsKeyCurrentlyDown(mainKey) || (_hotkeyService?.IsKeyPressed(mainKey) ?? false);
                                if (stillDown)
                                {
                                    Logger.Log($"Ignoring transient PTT key-up for {mainKey}; key is still physically down.");
                                    return;
                                }

                                if (i < PttReleaseConfirmChecks - 1)
                                {
                                    await Task.Delay(PttReleaseConfirmInterval, releaseCts.Token);
                                }
                            }

                            MarkOverlayActivity(showOverlayIfNeeded: false);
                            await StopRecordingAsync("ptt_release");
                        }
                        catch (TaskCanceledException)
                        {
                            // A fresh key-down arrived before release was stable.
                        }
                        finally
                        {
                            if (ReferenceEquals(_pendingPttReleaseStopCts, releaseCts))
                                _pendingPttReleaseStopCts = null;
                            releaseCts.Dispose();
                        }
                    }
                }
            }
        }

        private async Task StopRecordingAsync(string reason)
        {
            if (_recorder == null) return;
            if (!_recorder.IsRecording) return;
            if (!TryEnterTranscribing()) return;

            _stopRequested = true;
            Logger.Log($"Stopping recording (reason={reason}).");
            TrackSessionEvent(
                "transcribing_start",
                data: new Dictionary<string, string>
                {
                    ["reason"] = reason
                });
            _recorder.StopRecording();
            _stopSound?.Play();
            _recordMs = (int)Math.Max(0, (DateTime.UtcNow - _recordingStartedUtc).TotalMilliseconds);
            _transcribingStartedUtc = DateTime.UtcNow;

            if (!SessionHasMeaningfulMicSignal())
            {
                Logger.Log("No meaningful microphone signal detected during recording; skipping transcription.");
                _latestTranscriberErrorCode = NoMicSignalErrorCode;
                _latestTranscriberErrorMessage = "No microphone signal detected.";
                _overlay?.SetStatus("NO_MIC_SIGNAL", Brushes.OrangeRed);

                try
                {
                    if (_transcriber != null)
                    {
                        await _transcriber.DisconnectAsync();
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogException("StopRecordingAsync_DisconnectAfterNoMicSignal", ex);
                }

                HandleSessionFailure(
                    "No microphone signal detected. Check mic mute, input device, or volume.",
                    NoMicSignalErrorCode);
                return;
            }

            _overlay?.SetStatus("TRANSCRIBING", Brushes.Yellow);

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
                _latestTranscriberErrorMessage = ex.Message;
                _latestTranscriberErrorCode = ErrorClassifier.Classify(ex.Message);
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

                await FinalizeSessionAfterStopAsync();
            }
        }

        private async Task FinalizeSessionAfterStopAsync()
        {
            await Task.Delay(PostStopFinalResultGrace);
            if (_finalTranscriptionProcessed) return;

            var mergedTranscript = BuildMergedFinalTranscript();
            if (!string.IsNullOrWhiteSpace(mergedTranscript))
            {
                Logger.Log($"Finalizing session from buffered segments (count={SnapshotFinalSegmentCount()}).");
                await HandleFinalTranscriptionAsync(mergedTranscript, ResolveSessionSttProvider(), ResolveSessionSttModel());
                return;
            }

            var interimFallback = SnapshotLatestInterimTranscript();
            if (!string.IsNullOrWhiteSpace(interimFallback))
            {
                if (LooksLikeHintOnlyTranscript(interimFallback))
                {
                    Logger.Log($"Ignoring interim fallback because it matches a configured hint term only: '{interimFallback}'.");
                }
                else
                {
                    Logger.Log("No final segments received; using latest interim transcript as fallback.");
                    await HandleFinalTranscriptionAsync(interimFallback, ResolveSessionSttProvider(), ResolveSessionSttModel());
                    return;
                }
            }

            await RecoverIfNoFinalResultAsync();
        }

        private async Task RecoverIfNoFinalResultAsync()
        {
            if (_finalTranscriptionProcessed)
            {
                return;
            }

            SessionState snapshot;
            lock (_sessionLock)
            {
                snapshot = _sessionState;
            }

            if (snapshot is not (SessionState.Transcribing or SessionState.Refining))
            {
                return;
            }

            var failoverCode = string.IsNullOrWhiteSpace(_latestTranscriberErrorCode)
                ? NoFinalResultErrorCode
                : _latestTranscriberErrorCode;

            var failoverSucceeded = await TryRunSttFailoverAsync(failoverCode);
            if (failoverSucceeded || _finalTranscriptionProcessed)
            {
                return;
            }

            Logger.Log("No final transcription callback received after stop; forcing session recovery.");
            TrackSessionEvent(
                name: "session_end",
                level: "warning",
                success: false,
                result: "no_final_result",
                errorCode: NoFinalResultErrorCode,
                errorClass: NoFinalResultErrorCode,
                durationMs: _recordMs + _transcribeMs + _refineMs + _insertMs,
                data: new Dictionary<string, string>
                {
                    ["stt_provider"] = ConfigManager.Config.SttModel,
                    ["stt_model"] = ResolveActiveSttModel(),
                    ["last_error_code"] = _latestTranscriberErrorCode,
                    ["last_error"] = _latestTranscriberErrorMessage
                });

            Dispatcher.Invoke(() =>
            {
                var vm = MainWindow.DataContext as MainViewModel;
                vm?.SetLastInsertionStatus("N/A", false, NoFinalResultErrorCode);
            });

            PublishHistoryEntry(BuildFailedSessionHistoryEntry(NoFinalResultErrorCode));

            _overlay?.SetStatus("READY", Brushes.Aqua);
            MarkOverlayActivity(showOverlayIfNeeded: false);
            SetSessionState(SessionState.Idle);
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
            var processed = _audioProcessor.Process(data, out var processingStats);
            _overlay?.UpdateAudioLevel(Math.Min(processingStats.ProcessedRms * 12f, 1f));
            UpdateMicSignalState(processed.Length, processingStats);

            if (_transcriber != null)
            {
                await _transcriber.SendAudioAsync(processed);
            }

            CaptureSessionAudio(processed);

            if (_audioBuffer != null)
            {
                _audioBuffer.Write(processed, 0, processed.Length);
            }
        }

        private void ResetAudioProcessorForSession()
        {
            _audioProcessor.Dispose();
            _audioProcessor = AudioProcessorFactory.Create(ConfigManager.Config);
            _audioProcessor.Reset();
        }

        private void UpdateMicSignalState(int processedBytesLength, AudioProcessingStats processingStats)
        {
            int chunkDurationMs = EstimateAudioChunkDurationMs(
                processedBytesLength,
                ConfigManager.Config.SampleRate,
                ConfigManager.Config.Channels);

            if (chunkDurationMs <= 0)
            {
                return;
            }

            if (HasMeaningfulMicSignal(processingStats))
            {
                _sessionMeaningfulSignalMs += chunkDurationMs;
                _sessionSilenceMs = 0;

                if (_noMicSignalWarningShown)
                {
                    _noMicSignalWarningShown = false;
                    _overlay?.SetStatus(GetActiveRecordingOverlayStatus(), _isToggleRecording ? Brushes.Red : Brushes.LimeGreen);
                }

                return;
            }

            _sessionSilenceMs += chunkDurationMs;
            if (_sessionMeaningfulSignalMs == 0 &&
                !_noMicSignalWarningShown &&
                _sessionSilenceMs >= NoMicSignalWarningThresholdMs)
            {
                _noMicSignalWarningShown = true;
                _overlay?.SetStatus("NO_MIC_SIGNAL", Brushes.OrangeRed);
            }
        }

        private bool SessionHasMeaningfulMicSignal()
        {
            return _sessionMeaningfulSignalMs >= MeaningfulMicSignalThresholdMs;
        }

        private string GetActiveRecordingOverlayStatus()
        {
            return _isToggleRecording ? "RECORDING" : "PTT_RECORDING";
        }

        public static bool HasMeaningfulMicSignal(AudioProcessingStats stats)
        {
            return stats.ProcessedRms >= MicSignalRmsThreshold || stats.RawPeak >= MicSignalPeakThreshold;
        }

        public static int EstimateAudioChunkDurationMs(int bytesLength, int sampleRate, int channels)
        {
            if (bytesLength <= 0 || sampleRate <= 0 || channels <= 0)
            {
                return 0;
            }

            double bytesPerSecond = sampleRate * channels * 2.0;
            if (bytesPerSecond <= 0)
            {
                return 0;
            }

            return (int)Math.Max(0, Math.Round(bytesLength * 1000.0 / bytesPerSecond));
        }

        private void OnTranscriptionReceived(object? sender, TranscriptionEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.Text) || _finalTranscriptionProcessed) return;

            if (!e.IsFinal)
            {
                BufferLatestInterimTranscript(e.Text);
                return;
            }

            BufferFinalTranscriptSegment(e.Text, ConfigManager.Config.SttModel, ResolveActiveSttModel());
            Logger.Log($"Buffered final transcription segment (stopRequested={_stopRequested}): '{e.Text}'");
            TrackSessionEvent("transcriber_final", data: new Dictionary<string, string>
            {
                ["text"] = e.Text
            });
        }

        private void BufferLatestInterimTranscript(string text)
        {
            var normalized = text?.Trim();
            if (string.IsNullOrWhiteSpace(normalized)) return;
            lock (_interimTranscriptLock)
            {
                _latestInterimTranscript = normalized;
            }
        }

        private string SnapshotLatestInterimTranscript()
        {
            lock (_interimTranscriptLock)
            {
                return _latestInterimTranscript;
            }
        }

        private bool LooksLikeHintOnlyTranscript(string text)
        {
            var normalized = text?.Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            var configuredTerms = PersonalDictionaryService.GetCombinedTerms(
                ConfigManager.Config,
                _activeSessionProfile,
                maxTerms: 80);

            return PersonalDictionaryService.ContainsExactTerm(configuredTerms, normalized);
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

        private void BufferFinalTranscriptSegment(string text, string provider, string model)
        {
            var normalized = text?.Trim();
            if (string.IsNullOrWhiteSpace(normalized)) return;

            lock (_finalSegmentLock)
            {
                _finalTranscriptSegments.Add(normalized);
                if (!string.IsNullOrWhiteSpace(provider))
                {
                    _sessionSttProvider = provider;
                }

                if (!string.IsNullOrWhiteSpace(model))
                {
                    _sessionSttModel = model;
                }
            }
        }

        private int SnapshotFinalSegmentCount()
        {
            lock (_finalSegmentLock)
            {
                return _finalTranscriptSegments.Count;
            }
        }

        private string BuildMergedFinalTranscript()
        {
            List<string> snapshot;
            lock (_finalSegmentLock)
            {
                snapshot = _finalTranscriptSegments.ToList();
            }

            return SessionTranscriptAssembler.MergeFinalSegments(snapshot);
        }

        private async void OnTranscriberError(object? sender, string error)
        {
            if (_finalTranscriptionProcessed) return;

            var errorCode = ErrorClassifier.Classify(error);
            _latestTranscriberErrorCode = errorCode;
            _latestTranscriberErrorMessage = error;
            Logger.Log($"Transcriber error classified as '{errorCode}': {error}");
            TrackSessionEvent(
                name: "transcriber_error",
                level: "error",
                success: false,
                result: "error",
                errorCode: errorCode,
                errorClass: errorCode,
                data: new Dictionary<string, string> { ["error"] = error });

            if (_recorder?.IsRecording == true && !_stopRequested)
            {
                Logger.Log("Transcriber error received while recording is active; deferring failover until explicit stop.");
                return;
            }

            var failoverSucceeded = await TryRunSttFailoverAsync(errorCode);
            if (failoverSucceeded) return;

            HandleSessionFailure(error, errorCode);
        }

        private async Task HandleFinalTranscriptionAsync(string originalText, string sttProvider, string sttModel)
        {
            if (_finalTranscriptionProcessed) return;
            _finalTranscriptionProcessed = true;

            var dictionaryTerms = PersonalDictionaryService.GetCombinedTerms(
                ConfigManager.Config,
                _activeSessionProfile,
                maxTerms: 80);
            string correctedTranscript = PersonalDictionaryService.ApplyCorrections(
                originalText,
                dictionaryTerms,
                out var correctionCount);
            if (correctionCount > 0)
            {
                Logger.Log($"Personal dictionary corrections applied: {correctionCount}.");
                TrackSessionEvent(
                    name: "dictionary_corrections_applied",
                    result: "ok",
                    data: new Dictionary<string, string>
                    {
                        ["count"] = correctionCount.ToString()
                    });
            }

            var suggestions = PersonalDictionaryService.ExtractCandidateTerms(
                correctedTranscript,
                dictionaryTerms,
                maxCandidates: 10);
            if (suggestions.Count > 0)
            {
                Dispatcher.Invoke(() =>
                {
                    var vm = MainWindow?.DataContext as MainViewModel ?? ViewModel;
                    vm?.AddDictionarySuggestions(suggestions);
                });
            }

            string activeMode = DictationExperienceService.NormalizeMode(_activeSessionProfile?.DictationMode ?? ConfigManager.Config.DictationMode);
            var sessionVoiceCommandsEnabled = _activeSessionProfile?.EnableVoiceCommands ?? ConfigManager.Config.EnableVoiceCommands;
            var sessionVoiceCommandMode = _activeSessionProfile?.VoiceCommandMode ?? ConfigManager.Config.VoiceCommandMode;
            var sessionRefinementEnabled = _activeSessionProfile?.RefinementEnabled ?? ConfigManager.Config.EnableRefinement;
            var sessionRefinementProvider = sessionRefinementEnabled ? ResolveSessionRefinementProvider() : "Disabled";
            var sessionRefinementModel = sessionRefinementEnabled ? ResolveSessionRefinementModel() : string.Empty;
            var sessionContextualMode = ResolveSessionContextualRefinementMode();
            var refinementContext = CaptureSupplementalRefinementContext(_targetWindowContext);
            string contextSummary = DictationExperienceService.BuildContextSummary(ConfigManager.Config, _targetWindowContext, refinementContext);
            var voiceCommand = DictationExperienceService.MatchVoiceCommand(
                correctedTranscript,
                sessionVoiceCommandsEnabled,
                sessionVoiceCommandMode);

            if (voiceCommand.IsMatch)
            {
                UpdateOverlayContextIndicator(string.Empty);
                await ExecuteVoiceCommandAsync(originalText, correctedTranscript, sttProvider, sttModel, activeMode, contextSummary, voiceCommand);
                return;
            }

            if (voiceCommand.SuppressTranscript)
            {
                UpdateOverlayContextIndicator(string.Empty);
                await RecordUnrecognizedCommandAttemptAsync(originalText, correctedTranscript, sttProvider, sttModel, activeMode, contextSummary);
                return;
            }

            string textToInsert = correctedTranscript;
            string learnedCandidateText = correctedTranscript;
            _transcribeMs = (int)Math.Max(0, (DateTime.UtcNow - _transcribingStartedUtc).TotalMilliseconds);
            bool refinementFallbackUsed = false;
            string refinementFallbackCode = string.Empty;
            string effectivePrompt = DictationExperienceService.BuildEffectivePrompt(
                ConfigManager.Config,
                _activeSessionProfile,
                _targetWindowContext,
                refinementContext,
                out contextSummary);

            UpdateOverlayContextIndicator(sessionRefinementEnabled ? contextSummary : string.Empty);

            if (_refiner != null && sessionRefinementEnabled)
            {
                SetSessionState(SessionState.Refining);
                TrackSessionEvent("refiner_start");
                Logger.Log($"Refining text using {sessionRefinementProvider} (model={sessionRefinementModel})");
                _overlay?.SetStatus("REFINING", Brushes.Cyan);
                var swRefine = Stopwatch.StartNew();
                try
                {
                    textToInsert = await _refiner.RefineTextAsync(
                        RefinementRequest.Create(
                            correctedTranscript,
                            effectivePrompt,
                            sessionRefinementModel,
                            sessionContextualMode));
                    learnedCandidateText = textToInsert;
                }
                catch (Exception ex)
                {
                    refinementFallbackUsed = true;
                    refinementFallbackCode = ErrorClassifier.Classify(ex.Message);
                    textToInsert = correctedTranscript;
                    learnedCandidateText = correctedTranscript;
                    Logger.LogException("HandleFinalTranscriptionAsync.Refinement", ex);
                    Logger.Log($"Refinement fallback engaged ({sessionRefinementProvider}, code={refinementFallbackCode}).");
                }
                swRefine.Stop();
                _refineMs = (int)swRefine.ElapsedMilliseconds;
                Logger.Log($"Refinement stats: inputChars={correctedTranscript.Length}, outputChars={textToInsert.Length}, inputWords={CountWords(correctedTranscript)}, outputWords={CountWords(textToInsert)}");
                Logger.Log($"Refinement complete: '{textToInsert}'");
                TrackSessionEvent(
                    name: "refiner_result",
                    success: !refinementFallbackUsed,
                    result: refinementFallbackUsed ? "fallback" : "ok",
                    errorCode: refinementFallbackUsed ? refinementFallbackCode : string.Empty,
                    errorClass: refinementFallbackUsed ? refinementFallbackCode : string.Empty,
                    durationMs: _refineMs);
            }
            else
            {
                _refineMs = 0;
            }

            if (ConfigManager.Config.EnableSnippets)
            {
                textToInsert = SnippetLibraryManager.Apply(textToInsert, SnippetLibraryManager.Load(), out var snippetReplacements);
                if (snippetReplacements > 0)
                {
                    Logger.Log($"Snippet expansions applied: {snippetReplacements}.");
                    TrackSessionEvent(
                        name: "snippets_applied",
                        result: "ok",
                        data: new Dictionary<string, string>
                        {
                            ["count"] = snippetReplacements.ToString()
                        });
                }
            }

            if (ConfigManager.Config.LearnFromRefinementCorrections)
            {
                var correctionSuggestions = RefinementLearningService.ExtractSuggestions(
                    correctedTranscript,
                    learnedCandidateText,
                    dictionaryTerms,
                    SnippetLibraryManager.Load(),
                    maxSuggestions: 6);
                if (correctionSuggestions.Count > 0)
                {
                    Dispatcher.Invoke(() =>
                    {
                        var vm = MainWindow?.DataContext as MainViewModel ?? ViewModel;
                        vm?.AddCorrectionSuggestions(correctionSuggestions);
                    });
                }
            }

            Logger.Log($"Inserting text into target {_targetWindowContext}: '{textToInsert}'");
            TrackSessionEvent("insert_attempt");
            var toType = _sessionHasInserted ? " " + textToInsert : textToInsert;
            InsertResult insertResult = new InsertResult { Success = false, Method = "Unknown", ErrorCode = "NotExecuted" };
            await _insertionGate.WaitAsync();
            try
            {
                var swInsert = Stopwatch.StartNew();
                insertResult = await Task.Run(() => TextInserter.InsertText(toType, _targetWindowContext));
                swInsert.Stop();
                _insertMs = (int)swInsert.ElapsedMilliseconds;
            }
            finally
            {
                _insertionGate.Release();
            }

            if (refinementFallbackUsed)
            {
                insertResult.Method = $"{insertResult.Method}+RefineFallback";
                if (string.IsNullOrWhiteSpace(insertResult.ErrorCode))
                    insertResult.ErrorCode = $"refine_{refinementFallbackCode}";
            }
            if (insertResult.Success)
            {
                _sessionHasInserted = true;
                _lastInsertedBuffer = toType;
            }

            bool clipboardHasLatestText = insertResult.ClipboardUpdated;
            if (ConfigManager.Config.CopyToClipboard)
            {
                if (_sessionText.Length > 0) _sessionText.Append(' ');
                _sessionText.Append(textToInsert);
                var fullSessionText = _sessionText.ToString();
                if (TrySetClipboardTextSafe(fullSessionText, out var clipboardError))
                {
                    clipboardHasLatestText = true;
                    Logger.Log($"Copied full session text to clipboard (length={fullSessionText.Length}).");
                }
                else
                {
                    Logger.Log($"Failed to copy session text to clipboard ({clipboardError}).");
                    TrackSessionEvent(
                        name: "clipboard_copy_failed",
                        level: "warning",
                        success: false,
                        result: "failed",
                        errorCode: clipboardError,
                        errorClass: clipboardError);
                }
            }

            if (!insertResult.Success)
            {
                if (!clipboardHasLatestText && TrySetClipboardTextSafe(textToInsert, out var recoveryClipboardError))
                {
                    clipboardHasLatestText = true;
                    insertResult.ClipboardUpdated = true;
                    Logger.Log("Insertion failed but recovery clipboard copy succeeded.");
                }

                bool restartTriggered = TryOfferOnDemandElevation(insertResult, "final_insert", _targetWindowContext);
                if (!restartTriggered && TryQueueDeferredPaste(toType, insertResult))
                {
                    insertResult.Method = $"{insertResult.Method}+DeferredQueued";
                }
                else if (!restartTriggered)
                {
                    var targetName = string.IsNullOrWhiteSpace(_targetWindowContext.ProcessName)
                        ? "target app"
                        : _targetWindowContext.ProcessName;
                    var recoveryMessage =
                        $"Speakly could not safely insert text into the original target ({targetName})." + Environment.NewLine +
                        (clipboardHasLatestText
                            ? "The latest result was copied to your clipboard." + Environment.NewLine +
                              "Focus the target app and press Ctrl+V to paste." + Environment.NewLine + Environment.NewLine
                            : "Clipboard recovery also failed. Please retry once after closing apps that lock clipboard access." + Environment.NewLine + Environment.NewLine) +
                        $"Reason: {insertResult.ErrorCode}";

                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show(
                            recoveryMessage,
                            "Speakly: Insertion Recovery",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    });
                }
            }

            TrackSessionEvent(
                name: "insert_result",
                success: insertResult.Success,
                result: insertResult.Method,
                errorCode: insertResult.ErrorCode,
                errorClass: insertResult.ErrorCode,
                durationMs: _insertMs);

            var historyEntry = new HistoryEntry
            {
                Timestamp = DateTime.Now,
                OriginalText = originalText,
                RefinedText = textToInsert,
                SttProvider = sttProvider,
                SttModel = sttModel,
                RefinementProvider = sessionRefinementProvider,
                RefinementModel = sessionRefinementModel,
                RecordMs = _recordMs,
                TranscribeMs = _transcribeMs,
                RefineMs = _refineMs,
                InsertMs = _insertMs,
                Succeeded = insertResult.Success,
                InsertionMethod = insertResult.Method,
                ErrorCode = insertResult.ErrorCode,
                ProfileId = _activeSessionProfile?.Id ?? string.Empty,
                ProfileName = _activeSessionProfile?.Name ?? "Default",
                FailoverAttempted = _sttFailoverAttempted,
                FailoverFrom = _failoverFromProvider,
                FailoverTo = _failoverToProvider,
                FinalProviderUsed = sttProvider,
                DictationMode = activeMode,
                ContextualRefinementMode = sessionContextualMode,
                ContextSummary = contextSummary,
                ActionSource = "Live"
            };
            PublishHistoryEntry(historyEntry);

            StatisticsManager.RecordSession(new SessionMetricEntry
            {
                Timestamp = DateTime.Now,
                SttProvider = sttProvider,
                SttModel = sttModel,
                RefinementProvider = sessionRefinementProvider,
                RefinementModel = sessionRefinementModel,
                RecordMs = _recordMs,
                TranscribeMs = _transcribeMs,
                RefineMs = _refineMs,
                InsertMs = _insertMs,
                Succeeded = insertResult.Success,
                ErrorCode = insertResult.Success ? string.Empty : insertResult.ErrorCode,
                ProfileId = _activeSessionProfile?.Id ?? string.Empty,
                ProfileName = _activeSessionProfile?.Name ?? "Default",
                FailoverAttempted = _sttFailoverAttempted,
                FailoverFrom = _failoverFromProvider,
                FailoverTo = _failoverToProvider,
                FinalProviderUsed = sttProvider
            });

            _overlay?.SetStatus("READY", Brushes.Aqua);
            MarkOverlayActivity(showOverlayIfNeeded: false);
            TrackSessionEvent(
                name: "session_end",
                result: insertResult.Success ? "success" : "insert_failed",
                durationMs: _recordMs + _transcribeMs + _refineMs + _insertMs,
                data: new Dictionary<string, string>
                {
                    ["stt_provider"] = sttProvider,
                    ["stt_model"] = sttModel,
                    ["refinement_provider"] = sessionRefinementProvider,
                    ["refinement_model"] = sessionRefinementModel,
                    ["insertion_method"] = insertResult.Method,
                    ["final_segment_count"] = SnapshotFinalSegmentCount().ToString()
                });
            SetSessionState(SessionState.Idle);
        }

        private async Task ExecuteVoiceCommandAsync(
            string originalText,
            string correctedTranscript,
            string sttProvider,
            string sttModel,
            string activeMode,
            string contextSummary,
            VoiceCommandMatch command)
        {
            SetSessionState(SessionState.Refining);
            _overlay?.SetStatus($"CMD:{command.DisplayName}", Brushes.Orange);
            TrackSessionEvent("voice_command_start", data: new Dictionary<string, string>
            {
                ["command"] = command.DisplayName
            });

            var swInsert = Stopwatch.StartNew();
            var selectionLength = command.Kind is VoiceCommandKind.DeleteThat or VoiceCommandKind.ScratchThat or VoiceCommandKind.SelectThat
                ? _lastInsertedBuffer.Length
                : 0;
            var insertResult = await Task.Run(() => TextInserter.ExecuteVoiceCommand(_targetWindowContext, command.Kind, selectionLength));
            swInsert.Stop();
            _recordMs = _recordMs == 0 ? (int)Math.Max(0, (DateTime.UtcNow - _recordingStartedUtc).TotalMilliseconds) : _recordMs;
            _transcribeMs = (int)Math.Max(0, (DateTime.UtcNow - _transcribingStartedUtc).TotalMilliseconds);
            _refineMs = 0;
            _insertMs = (int)swInsert.ElapsedMilliseconds;

            if (insertResult.Success && command.Kind is VoiceCommandKind.DeleteThat or VoiceCommandKind.ScratchThat)
            {
                _lastInsertedBuffer = string.Empty;
            }

            PublishHistoryEntry(new HistoryEntry
            {
                Timestamp = DateTime.Now,
                OriginalText = originalText,
                RefinedText = string.Empty,
                SttProvider = sttProvider,
                SttModel = sttModel,
                RefinementProvider = "Command",
                RefinementModel = string.Empty,
                RecordMs = _recordMs,
                TranscribeMs = _transcribeMs,
                RefineMs = 0,
                InsertMs = _insertMs,
                Succeeded = insertResult.Success,
                InsertionMethod = insertResult.Method,
                ErrorCode = insertResult.ErrorCode,
                ProfileId = _activeSessionProfile?.Id ?? string.Empty,
                ProfileName = _activeSessionProfile?.Name ?? "Default",
                DictationMode = activeMode,
                ContextualRefinementMode = DictationExperienceService.NormalizeContextualRefinementMode(ConfigManager.Config.ContextualRefinementMode),
                ContextSummary = contextSummary,
                WasVoiceCommand = true,
                VoiceCommandName = command.DisplayName,
                ActionSource = "Live"
            });

            TrackSessionEvent(
                name: "voice_command_result",
                success: insertResult.Success,
                result: command.DisplayName,
                errorCode: insertResult.ErrorCode,
                errorClass: insertResult.ErrorCode,
                durationMs: _insertMs);

            _overlay?.SetStatus("READY", Brushes.Aqua);
            MarkOverlayActivity(showOverlayIfNeeded: false);
            SetSessionState(SessionState.Idle);
        }

        private Task RecordUnrecognizedCommandAttemptAsync(
            string originalText,
            string correctedTranscript,
            string sttProvider,
            string sttModel,
            string activeMode,
            string contextSummary)
        {
            _transcribeMs = (int)Math.Max(0, (DateTime.UtcNow - _transcribingStartedUtc).TotalMilliseconds);
            _refineMs = 0;
            _insertMs = 0;
            PublishHistoryEntry(new HistoryEntry
            {
                Timestamp = DateTime.Now,
                OriginalText = originalText,
                RefinedText = string.Empty,
                SttProvider = sttProvider,
                SttModel = sttModel,
                RefinementProvider = "Command",
                RefinementModel = string.Empty,
                RecordMs = _recordMs,
                TranscribeMs = _transcribeMs,
                RefineMs = 0,
                InsertMs = 0,
                Succeeded = false,
                InsertionMethod = "CommandsOnly",
                ErrorCode = "command_not_recognized",
                ProfileId = _activeSessionProfile?.Id ?? string.Empty,
                ProfileName = _activeSessionProfile?.Name ?? "Default",
                DictationMode = activeMode,
                ContextualRefinementMode = DictationExperienceService.NormalizeContextualRefinementMode(ConfigManager.Config.ContextualRefinementMode),
                ContextSummary = contextSummary,
                WasVoiceCommand = true,
                VoiceCommandName = "No command recognized",
                ActionSource = "Live"
            });
            _overlay?.SetStatus("READY", Brushes.Aqua);
            MarkOverlayActivity(showOverlayIfNeeded: false);
            SetSessionState(SessionState.Idle);
            return Task.CompletedTask;
        }

        private void PublishHistoryEntry(HistoryEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            HistoryManager.AddEntry(entry);
            Dispatcher.Invoke(() =>
            {
                var vm = MainWindow?.DataContext as MainViewModel ?? ViewModel;
                vm?.HistoryEntries.Insert(0, entry);
                vm?.SetLastInsertionStatus(entry.InsertionMethod, entry.Succeeded, entry.ErrorCode);
            });
        }

        private HistoryEntry BuildFailedSessionHistoryEntry(string errorCode, string insertionMethod = "N/A")
        {
            var sessionRefinementEnabled = _activeSessionProfile?.RefinementEnabled ?? ConfigManager.Config.EnableRefinement;
            var sessionContextSummary = DictationExperienceService.BuildContextSummary(
                ConfigManager.Config,
                _targetWindowContext,
                RefinementContextSnapshot.Empty);

            return new HistoryEntry
            {
                Timestamp = DateTime.Now,
                OriginalText = string.Empty,
                RefinedText = string.Empty,
                SttProvider = ConfigManager.Config.SttModel,
                SttModel = ResolveActiveSttModel(),
                RefinementProvider = sessionRefinementEnabled ? ResolveSessionRefinementProvider() : "Disabled",
                RefinementModel = sessionRefinementEnabled ? ResolveSessionRefinementModel() : string.Empty,
                RecordMs = _recordMs,
                TranscribeMs = _transcribeMs,
                RefineMs = _refineMs,
                InsertMs = _insertMs,
                Succeeded = false,
                ErrorCode = errorCode,
                InsertionMethod = insertionMethod,
                ProfileId = _activeSessionProfile?.Id ?? string.Empty,
                ProfileName = _activeSessionProfile?.Name ?? "Default",
                FailoverAttempted = _sttFailoverAttempted,
                FailoverFrom = _failoverFromProvider,
                FailoverTo = _failoverToProvider,
                FinalProviderUsed = string.Empty,
                DictationMode = DictationExperienceService.NormalizeMode(_activeSessionProfile?.DictationMode ?? ConfigManager.Config.DictationMode),
                ContextualRefinementMode = ResolveSessionContextualRefinementMode(),
                ContextSummary = sessionContextSummary,
                ActionSource = "Live"
            };
        }

        private static bool TrySetClipboardTextSafe(string text, out string errorCode)
        {
            errorCode = string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                errorCode = "clipboard_empty_text";
                return false;
            }

            for (int attempt = 1; attempt <= 6; attempt++)
            {
                try
                {
                    Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(text));
                    return true;
                }
                catch (Exception ex)
                {
                    errorCode = ex.GetType().Name;
                    Thread.Sleep(40);
                }
            }

            if (string.IsNullOrWhiteSpace(errorCode))
            {
                errorCode = "clipboard_unavailable";
            }

            return false;
        }

        private static RefinementContextSnapshot CaptureSupplementalRefinementContext(TargetWindowContext targetContext)
        {
            if (!ConfigManager.Config.EnableRefinement)
            {
                return RefinementContextSnapshot.Empty;
            }

            if (!ConfigManager.Config.UseSelectedTextContextForRefinement &&
                !ConfigManager.Config.UseClipboardContextForRefinement)
            {
                return RefinementContextSnapshot.Empty;
            }

            try
            {
                return RefinementContextCaptureService.Capture(ConfigManager.Config, targetContext);
            }
            catch (Exception ex)
            {
                Logger.LogException("CaptureSupplementalRefinementContext", ex);
                return RefinementContextSnapshot.Empty;
            }
        }

        private async Task<bool> TryRunSttFailoverAsync(string errorCode)
        {
            if (_sttFailoverAttempted || _finalTranscriptionProcessed) return false;
            if (!ConfigManager.Config.EnableSttFailover) return false;
            var allowFailover = ErrorClassifier.IsTransient(errorCode)
                || string.Equals(errorCode, NoFinalResultErrorCode, StringComparison.OrdinalIgnoreCase);
            if (!allowFailover) return false;

            var fallbackProvider = ResolveFallbackProvider();
            if (string.IsNullOrWhiteSpace(fallbackProvider)) return false;

            var audioSnapshot = SnapshotSessionAudio();
            if (audioSnapshot.Count == 0) return false;

            _sttFailoverAttempted = true;
            _failoverFromProvider = ConfigManager.Config.SttModel;
            _failoverToProvider = fallbackProvider;
            _overlay?.SetStatus($"FAILOVER:{fallbackProvider}", Brushes.Orange);
            Logger.Log($"Attempting STT failover to {fallbackProvider}.");
            TrackSessionEvent(
                name: "failover_start",
                result: "attempt",
                data: new Dictionary<string, string>
                {
                    ["from_provider"] = _failoverFromProvider,
                    ["to_provider"] = _failoverToProvider
                });

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
                    TrackSessionEvent("failover_result", result: "success", data: new Dictionary<string, string>
                    {
                        ["provider"] = fallbackProvider
                    });
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

            TrackSessionEvent("failover_result", level: "warning", success: false, result: "failed", errorCode: errorCode, errorClass: errorCode);
            return false;
        }

        private void HandleSessionFailure(string error, string errorCode)
        {
            SetSessionState(SessionState.Error);
            if (string.Equals(errorCode, NoMicSignalErrorCode, StringComparison.OrdinalIgnoreCase))
            {
                _overlay?.SetStatus("NO_MIC_SIGNAL", Brushes.OrangeRed);
            }
            else
            {
                _overlay?.SetStatus("ERROR", Brushes.OrangeRed);
            }
            MarkOverlayActivity(showOverlayIfNeeded: false);

            PublishHistoryEntry(BuildFailedSessionHistoryEntry(errorCode));

            StatisticsManager.RecordSession(new SessionMetricEntry
            {
                Timestamp = DateTime.Now,
                SttProvider = ConfigManager.Config.SttModel,
                SttModel = ResolveActiveSttModel(),
                RefinementProvider = (_activeSessionProfile?.RefinementEnabled ?? ConfigManager.Config.EnableRefinement) ? ResolveSessionRefinementProvider() : "Disabled",
                RefinementModel = (_activeSessionProfile?.RefinementEnabled ?? ConfigManager.Config.EnableRefinement) ? ResolveSessionRefinementModel() : string.Empty,
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
                if (string.Equals(errorCode, NoMicSignalErrorCode, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                MessageBox.Show($"Transcription Error: {error}", "Speakly Error", MessageBoxButton.OK, MessageBoxImage.Error);
            });

            TrackSessionEvent(
                name: "session_end",
                level: "error",
                success: false,
                result: "failed",
                errorCode: errorCode,
                errorClass: errorCode,
                durationMs: _recordMs + _transcribeMs + _refineMs + _insertMs,
                data: new Dictionary<string, string>
                {
                    ["error"] = error,
                    ["stt_provider"] = ConfigManager.Config.SttModel,
                    ["stt_model"] = ResolveActiveSttModel()
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
                "elevenlabs" => !string.IsNullOrWhiteSpace(ConfigManager.Config.ElevenLabsApiKey),
                "openai" => !string.IsNullOrWhiteSpace(ConfigManager.Config.OpenAIApiKey),
                "openrouter" => !string.IsNullOrWhiteSpace(ConfigManager.Config.OpenRouterApiKey),
                _ => false
            };
        }

        public async Task ToggleRecordingFromOverlayAsync()
        {
            if (_recorder == null) return;
            MarkOverlayActivity(showOverlayIfNeeded: true);

            if (_recorder.IsRecording)
            {
                _isToggleRecording = false;
                await StopRecordingAsync("overlay_toggle");
                return;
            }

            if (!TryEnterRecording()) return;
            BeginSessionTiming();

            CaptureTargetWindowContext("Overlay Recording Started");
            PrepareSessionContext();
            _sessionText.Clear();
            _sessionHasInserted = false;
            ResetAudioProcessorForSession();
            AutoAdjustRefinementPromptForLanguage();
            _overlay?.SetActiveLanguage(ResolveOverlayLanguageDisplay());
            _isToggleRecording = true;
            _overlay?.SetStatus("RECORDING", Brushes.Red);
            _startSound?.Play();

            var connectTask = _transcriber != null ? _transcriber.ConnectAsync() : Task.CompletedTask;

            if (ConfigManager.Config.SaveDebugRecords)
            {
                _audioBuffer = new System.IO.MemoryStream();
            }

            _recorder.StartRecording();
            await connectTask;
            TrackSessionEvent("transcriber_connect", result: "ok");
        }

        public async Task StopRecordingFromOverlayAsync()
        {
            if (_recorder == null || !_recorder.IsRecording) return;
            _isToggleRecording = false;
            await StopRecordingAsync("overlay_stop");
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _overlayIdleTimer.Stop();
            _deferredPasteTimer.Stop();
            CancelPendingPttReleaseStop();
            _hotkeyService?.Dispose();
            _trayService?.Dispose();
            _singleInstanceManager?.Dispose();
            _recorder?.Dispose();
            _transcriber?.Dispose();
            _audioProcessor.Dispose();
            _overlay?.Close();
            _startSound?.Dispose();
            _stopSound?.Dispose();
            
            Logger.Log("Application exiting. Forcefully terminating process.");
            TelemetryManager.Track(
                name: "app_exit",
                level: "info",
                result: "ok",
                data: new Dictionary<string, string> { ["version"] = AppVersion });
            base.OnExit(e);
            
            // Hard exit to ensure no "ghost" processes remain from background threads or hidden windows
            System.Environment.Exit(0);
        }

        public static string AppVersion { get; } =
            System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "2.0.4";

        public static string NormalizeThemeName(string? themeName)
        {
            return string.Equals(themeName?.Trim(), "Light", StringComparison.OrdinalIgnoreCase)
                ? "Light"
                : "Dark";
        }

        public static void SetTheme(string themeName)
        {
            var normalizedTheme = NormalizeThemeName(themeName);
            var appTheme = string.Equals(normalizedTheme, "Light", StringComparison.OrdinalIgnoreCase)
                ? ApplicationTheme.Light
                : ApplicationTheme.Dark;
            var persistedTheme = ConfigManager.Config.Theme;
            if (!string.Equals(persistedTheme, normalizedTheme, StringComparison.OrdinalIgnoreCase))
            {
                ConfigManager.Config.Theme = normalizedTheme;
                if (!string.Equals(NormalizeThemeName(persistedTheme), normalizedTheme, StringComparison.OrdinalIgnoreCase))
                {
                    ConfigManager.Save();
                }
            }
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
            ApplicationAccentColorManager.Apply(
                Color.FromRgb(0x06, 0xB6, 0xD4),
                appTheme,
                false,
                false);

            // Explicitly flip the Win32 DWMWA_USE_IMMERSIVE_DARK_MODE attribute on the main
            // window so the title bar and NavigationView pane chrome match the app theme.
            var mainWindow = Application.Current.MainWindow;
            if (mainWindow != null)
            {
                ApplyImmersiveThemeToWindow(mainWindow, appTheme == ApplicationTheme.Dark);
            }

            // Swap the custom overlay/capture brush file (Dark vs Light colours)
            string fileName = appTheme == ApplicationTheme.Light ? "LightTheme.xaml" : "DarkTheme.xaml";
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

        public static void SetOverlayStyle(string styleName)
        {
            if (Application.Current is not App app)
            {
                return;
            }

            var normalizedStyle = NormalizeOverlayStyleName(styleName);
            ConfigManager.Config.OverlayStyle = normalizedStyle;
            app._overlay?.SetOverlayStyle(normalizedStyle);
        }

        public static void SetOverlayVisible(bool visible)
        {
            if (Application.Current is not App app) return;

            if (visible)
            {
                app.MarkOverlayActivity(showOverlayIfNeeded: true);
            }
            else
            {
                app._overlay?.Close();
                app._overlay = null;
                app._overlayHiddenByIdle = false;
            }
        }

        public static void RecoverOverlayPosition()
        {
            if (Application.Current is not App app) return;

            app.MarkOverlayActivity(showOverlayIfNeeded: true);
            app.EnsureOverlayVisibleInternal(activate: true, forceClampToVisibleArea: true);
        }

        public static void SetOverlayAutoHideEnabled(bool enabled)
        {
            if (Application.Current is not App app) return;

            ConfigManager.Config.OverlayAutoHideEnabled = enabled;
            app.MarkOverlayActivity(showOverlayIfNeeded: false);

            if (!enabled && ConfigManager.Config.ShowOverlay)
            {
                app.EnsureOverlayVisibleInternal(activate: false);
            }
        }

        public static void SetDeferredTargetPasteEnabled(bool enabled)
        {
            if (Application.Current is not App app) return;

            ConfigManager.Config.DeferredTargetPasteEnabled = enabled;
            app.RefreshPendingTransferStatus();
        }

        public static bool SetStartWithWindowsEnabled(bool enabled)
        {
            if (Application.Current is not App)
            {
                return false;
            }

            bool previous = ConfigManager.Config.StartWithWindows;
            ConfigManager.Config.StartWithWindows = enabled;

            if (StartupRegistrationService.Reconcile(enabled, out var status))
            {
                Logger.Log(status);
                ViewModel?.RunHealthChecks();
                return true;
            }

            ConfigManager.Config.StartWithWindows = previous;
            Logger.Log($"Failed to apply startup setting: {status}");
            MessageBox.Show(
                status,
                "Speakly Startup Setting",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            ViewModel?.RunHealthChecks();
            return false;
        }

        public static void ClearDeferredTargetPaste()
        {
            if (Application.Current is not App app) return;
            app.ClearDeferredPasteInternal("manual_clear");
        }

        private static bool IsBaseThemeDictionary(string source)
        {
            return BaseThemeDictionaryUris.Any(
                uri => source.Contains(uri, StringComparison.OrdinalIgnoreCase));
        }

        private static void ApplyImmersiveThemeToWindow(Window window, bool useDarkMode)
        {
            try
            {
                var handle = new WindowInteropHelper(window).Handle;
                if (handle == IntPtr.Zero)
                {
                    return;
                }

                var attributeValue = useDarkMode ? 1 : 0;
                var result = DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref attributeValue, sizeof(int));
                if (result != 0)
                {
                    DwmSetWindowAttribute(handle, DwmUseImmersiveDarkModeLegacy, ref attributeValue, sizeof(int));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Immersive theme apply failed: {ex.Message}");
            }
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

        private static string NormalizeOverlayStyleName(string? styleName)
        {
            if (string.Equals(styleName, "Circle", StringComparison.OrdinalIgnoreCase))
            {
                return "Circle";
            }

            return "Rectangular";
        }

        private void BeginSessionTiming()
        {
            _recordingStartedUtc = DateTime.UtcNow;
            _transcribingStartedUtc = _recordingStartedUtc;
            _activeSessionId = Guid.NewGuid().ToString("N");
            _activeOperationId = Guid.NewGuid().ToString("N");
            _recordMs = 0;
            _transcribeMs = 0;
            _refineMs = 0;
            _insertMs = 0;
            _finalTranscriptionProcessed = false;
            _stopRequested = false;
            _sttFailoverAttempted = false;
            _failoverFromProvider = string.Empty;
            _failoverToProvider = string.Empty;
            _latestTranscriberErrorCode = string.Empty;
            _latestTranscriberErrorMessage = string.Empty;
            lock (_audioChunkLock)
            {
                _sessionAudioChunks.Clear();
            }
            lock (_finalSegmentLock)
            {
                _finalTranscriptSegments.Clear();
                _sessionSttProvider = ConfigManager.Config.SttModel;
                _sessionSttModel = ResolveActiveSttModel();
            }
            lock (_interimTranscriptLock)
            {
                _latestInterimTranscript = string.Empty;
            }
            _sessionMeaningfulSignalMs = 0;
            _sessionSilenceMs = 0;
            _noMicSignalWarningShown = false;
            TrackSessionEvent("session_start", data: new Dictionary<string, string>
            {
                ["trigger_provider"] = ConfigManager.Config.SttModel,
                ["trigger_model"] = ResolveActiveSttModel()
            });
        }

        private void PrepareSessionContext()
        {
            try
            {
                var windowForProfile = _targetWindowContext.IsValid
                    ? _targetWindowContext.Handle
                    : _lastActiveWindow;
                _activeSessionProfile = ProfileResolverService.ResolveForForegroundWindow(windowForProfile);
                ConfigManager.SetActiveProfile(_activeSessionProfile.Id);
                ConfigManager.EnsureProfileSyncToLegacyFields(_activeSessionProfile);
                InitializeTranscriptionAndRefinement();
                lock (_finalSegmentLock)
                {
                    _sessionSttProvider = ConfigManager.Config.SttModel;
                    _sessionSttModel = ResolveActiveSttModel();
                }
                TrackSessionEvent("profile_resolved", data: new Dictionary<string, string>
                {
                    ["profile_id"] = _activeSessionProfile.Id,
                    ["profile_name"] = _activeSessionProfile.Name,
                    ["stt_provider"] = _activeSessionProfile.SttProvider,
                    ["refinement_provider"] = _activeSessionProfile.RefinementProvider
                });

                Dispatcher.Invoke(() =>
                {
                    if (ViewModel != null)
                    {
                        ViewModel.SetRuntimeActiveProfile(_activeSessionProfile);
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.LogException("PrepareSessionContext", ex);
                _activeSessionProfile = ConfigManager.GetActiveProfile();
                Dispatcher.Invoke(() => ViewModel?.SetRuntimeActiveProfile(_activeSessionProfile));
            }
        }

        private static string ResolveActiveSttModel()
        {
            return ResolveSttModelForProvider(ConfigManager.Config.SttModel);
        }

        private string ResolveSessionSttProvider()
        {
            lock (_finalSegmentLock)
            {
                return string.IsNullOrWhiteSpace(_sessionSttProvider)
                    ? ConfigManager.Config.SttModel
                    : _sessionSttProvider;
            }
        }

        private string ResolveSessionSttModel()
        {
            lock (_finalSegmentLock)
            {
                return string.IsNullOrWhiteSpace(_sessionSttModel)
                    ? ResolveActiveSttModel()
                    : _sessionSttModel;
            }
        }

        private static string ResolveSttModelForProvider(string provider)
        {
            return provider switch
            {
                "OpenAI" => ConfigManager.Config.OpenAISttModel,
                "Deepgram" => ConfigManager.Config.DeepgramModel,
                "ElevenLabs" => ConfigManager.Config.ElevenLabsSttModel,
                "OpenRouter" => ConfigManager.Config.OpenRouterSttModel,
                _ => string.Empty
            };
        }

        private static string ResolveActiveRefinementModel()
        {
            return ConfigManager.ResolveRefinementModel(
                ConfigManager.Config.RefinementModel,
                ConfigManager.Config.RefinementModel switch
                {
                    "OpenRouter" => ConfigManager.Config.OpenRouterRefinementModel,
                    "Cerebras" => ConfigManager.Config.CerebrasRefinementModel,
                    _ => ConfigManager.Config.OpenAIRefinementModel
                 });
        }

        private string ResolveSessionRefinementProvider()
        {
            return _activeSessionProfile?.RefinementProvider ?? ConfigManager.Config.RefinementModel;
        }

        private string ResolveSessionRefinementModel()
        {
            return _activeSessionProfile != null
                ? ConfigManager.ResolveRefinementModel(_activeSessionProfile.RefinementProvider, _activeSessionProfile.RefinementModel)
                : ResolveActiveRefinementModel();
        }

        private string ResolveSessionContextualRefinementMode()
        {
            return !string.IsNullOrWhiteSpace(_activeSessionProfile?.ContextualRefinementMode)
                ? DictationExperienceService.NormalizeContextualRefinementMode(_activeSessionProfile.ContextualRefinementMode)
                : DictationExperienceService.NormalizeContextualRefinementMode(ConfigManager.Config.ContextualRefinementMode);
        }

        private void ReportHotkeyHookInitializationFailure()
        {
            var errorCode = _hotkeyService?.HookInitializationErrorCode ?? 0;
            var errorToken = _hotkeyService?.HookInitializationError;
            var summary = "Hotkey hook failed to initialize. Push-to-talk is unavailable until Speakly is restarted or reconfigured.";
            var details = string.IsNullOrWhiteSpace(errorToken)
                ? "Keyboard hook registration returned no additional error token."
                : $"Keyboard hook error: {errorToken}";
            if (errorCode != 0)
            {
                details += $" (Win32: {errorCode})";
            }

            Logger.Log($"Hotkey hook initialization failed. {details}");
            ViewModel?.SetRuntimeHealthIssue(summary, details);
            TelemetryManager.Track(
                name: "keyboard_hook_init_failed",
                level: "error",
                result: "failed",
                errorCode: string.IsNullOrWhiteSpace(errorToken) ? "keyboard_hook_install_failed" : errorToken,
                data: new Dictionary<string, string>
                {
                    ["win32_error"] = errorCode.ToString()
                });
        }

        private static int CountWords(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return 0;
            }

            return value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
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
                if (_sessionState == SessionState.Idle && _recorder?.IsRecording == true)
                {
                    _sessionState = SessionState.Transcribing;
                    return true;
                }

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
            if (state == SessionState.Idle)
            {
                _activeSessionProfile = null;
                Dispatcher.Invoke(() => ViewModel?.SetRuntimeActiveProfile(null));
            }
            TrackSessionEvent("session_state", data: new Dictionary<string, string>
            {
                ["state"] = state.ToString()
            });
        }

        private void TrackSessionEvent(
            string name,
            string level = "info",
            bool success = true,
            string result = "",
            string errorCode = "",
            string errorClass = "",
            int durationMs = 0,
            Dictionary<string, string>? data = null)
        {
            TelemetryManager.Track(
                name: name,
                level: level,
                success: success,
                result: result,
                sessionId: _activeSessionId,
                operationId: _activeOperationId,
                errorCode: errorCode,
                errorClass: errorClass,
                durationMs: durationMs,
                data: data);
        }
    }
}
