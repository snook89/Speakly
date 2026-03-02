using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
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

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

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
            MainWindow = new MainWindow();
            _trayService = new TrayIconService(MainWindow);
            
            // Set window icon via PNG (WPF can decode PNG natively)
            try
            {
                var iconUri = new Uri("pack://application:,,,/Resources/speakly.png");
                MainWindow.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(iconUri);
            }
            catch { }
            
            if (ConfigManager.Config.ShowOverlay)
            {
                _overlay = new FloatingOverlay();
                _overlay.Show();
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
                if (_recorder != null && !_recorder.IsRecording)
                {
                    _lastActiveWindow = TextInserter.GetForegroundWindow();
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
                    _lastActiveWindow = TextInserter.GetForegroundWindow();
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
            if (_recorder == null || !_recorder.IsRecording) return;
            Logger.Log("Stopping recording.");
            _overlay?.SetStatus("TRANSCRIBING", Brushes.Yellow);
            _stopSound?.Play();
            _recorder.StopRecording();

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

            if (_audioBuffer != null)
            {
                _audioBuffer.Write(data, 0, data.Length);
            }
        }

        private async void OnTranscriptionReceived(object? sender, TranscriptionEventArgs e)
        {
            Logger.Log($"App received transcription: isFinal={e.IsFinal}, Text='{e.Text}'");
            if (e.IsFinal)
            {
                string textToInsert = e.Text;

                if (_refiner != null && ConfigManager.Config.EnableRefinement)
                {
                    string activeRefinementModel = ConfigManager.Config.RefinementModel switch
                    {
                        "Cerebras" => ConfigManager.Config.CerebrasRefinementModel,
                        "OpenRouter" => ConfigManager.Config.OpenRouterRefinementModel,
                        _ => ConfigManager.Config.OpenAIRefinementModel
                    };
                    Logger.Log($"Refining text using {ConfigManager.Config.RefinementModel} (model={activeRefinementModel})");
                    _overlay?.SetStatus("REFINING", Brushes.Cyan);
                    textToInsert = await _refiner.RefineTextAsync(e.Text, ConfigManager.Config.RefinementPrompt);
                    Logger.Log($"Refinement complete: '{textToInsert}'");
                }

                Logger.Log($"Inserting text into window {_lastActiveWindow}: '{textToInsert}'");
                // Prepend a space between utterances so they don't run together in the target window.
                var toType = _sessionHasInserted ? " " + textToInsert : textToInsert;
                await _insertionGate.WaitAsync();
                try
                {
                    await Task.Run(() => TextInserter.InsertText(toType, _lastActiveWindow));
                }
                finally
                {
                    _insertionGate.Release();
                }
                _sessionHasInserted = true;

                if (ConfigManager.Config.CopyToClipboard)
                {
                    // Append a space separator between utterances (matching what gets typed
                    // into the target window), then copy the FULL session text so the
                    // clipboard always holds everything spoken — not just the last chunk.
                    if (_sessionText.Length > 0) _sessionText.Append(' ');
                    _sessionText.Append(textToInsert);
                    var fullSessionText = _sessionText.ToString();
                    Dispatcher.Invoke(() => System.Windows.Clipboard.SetText(fullSessionText));
                    Logger.Log($"Copied full session text to clipboard (length={fullSessionText.Length}).");
                }

                HistoryManager.AddEntry(e.Text, textToInsert);
                
                // Sync to ViewModel
                Dispatcher.Invoke(() => {
                    var vm = MainWindow.DataContext as MainViewModel;
                    vm?.HistoryEntries.Insert(0, new HistoryEntry { 
                        Timestamp = DateTime.Now, 
                        OriginalText = e.Text, 
                        RefinedText = textToInsert 
                    });
                });

                _overlay?.SetStatus("READY", Brushes.Aqua);
            }
        }

        private void OnTranscriberError(object? sender, string error)
        {
            _overlay?.SetStatus("ERROR", Brushes.OrangeRed);
            Dispatcher.Invoke(() => {
                MessageBox.Show($"Transcription Error: {error}", "Speakly Error", MessageBoxButton.OK, MessageBoxImage.Error);
            });
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

            _lastActiveWindow = TextInserter.GetForegroundWindow();
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
            }
            else
            {
                app._overlay?.Close();
                app._overlay = null;
            }
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
    }
}
