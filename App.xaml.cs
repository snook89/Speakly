using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Speakly.Config;
using Speakly.Services;
using Speakly.ViewModels;

namespace Speakly
{
    public partial class App : Application
    {
        private GlobalHotkeyService? _hotkeyService;
        private TrayIconService? _trayService;
        private IAudioRecorder? _recorder;
        private ITranscriber? _transcriber;
        private ITextRefiner? _refiner;
        private FloatingOverlay? _overlay;
        private SoundPlayer? _startSound;
        private SoundPlayer? _stopSound;
        private bool _isToggleRecording = false; // Track toggle-record state
        private bool _finalTranscriptReceivedInCurrentSession = false;
        private IntPtr _lastActiveWindow = IntPtr.Zero;
        private System.IO.MemoryStream? _audioBuffer; // For debug records

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

            MainWindow = new MainWindow();
            _trayService = new TrayIconService(MainWindow);
            
            // Set window icon via PNG (WPF can decode PNG natively)
            try
            {
                var iconUri = new Uri("pack://application:,,,/Resources/speakly.png");
                MainWindow.Icon = System.Windows.Media.Imaging.BitmapFrame.Create(iconUri);
            }
            catch { }
            
            _overlay = new FloatingOverlay();
            _overlay.Show();

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
                    _finalTranscriptReceivedInCurrentSession = false;
                    Logger.Log($"PTT Hotkey Pressed. Captured active window: {_lastActiveWindow}");
                    
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
                    _finalTranscriptReceivedInCurrentSession = false;
                    Logger.Log($"Toggle Recording Started. Captured active window: {_lastActiveWindow}");
                    
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

                // If no final transcript arrived, always recover overlay state.
                if (!_finalTranscriptReceivedInCurrentSession)
                {
                    _overlay?.SetStatus("READY", Brushes.Aqua);
                }
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
                _finalTranscriptReceivedInCurrentSession = true;
                string textToInsert = e.Text;

                if (_refiner != null)
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
                TextInserter.InsertText(textToInsert, _lastActiveWindow);
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

        public static void SetTheme(string themeName)
        {
            string fileName = "DarkTheme.xaml";
            if (themeName.Equals("Light", StringComparison.OrdinalIgnoreCase)) fileName = "LightTheme.xaml";
            else if (themeName.Equals("Matrix", StringComparison.OrdinalIgnoreCase)) fileName = "MatrixTheme.xaml";
            else if (themeName.Equals("Ocean", StringComparison.OrdinalIgnoreCase)) fileName = "OceanTheme.xaml";
            string dictUri = $"pack://application:,,,/Themes/{fileName}";

            try
            {
                var dict = new ResourceDictionary { Source = new Uri(dictUri, UriKind.Absolute) };
                if (Application.Current.Resources.MergedDictionaries.Count > 0)
                    Application.Current.Resources.MergedDictionaries[0] = dict;
                else
                    Application.Current.Resources.MergedDictionaries.Add(dict);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Theme load failed: {ex.Message}");
            }
        }
    }
}
