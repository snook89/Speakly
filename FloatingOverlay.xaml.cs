using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Speakly
{
    public partial class FloatingOverlay : Window
    {
        private readonly DispatcherTimer _timer = new();
        private readonly DispatcherTimer _waveTimer = new();
        private readonly DispatcherTimer _toastTimer = new();
        private readonly DispatcherTimer _saveSizeTimer = new();
        private readonly List<Rectangle> _bars = new();
        private readonly Random _rng = new();

        private DateTime _recordStart;
        private bool _isRecording;
        private bool _isProcessing;
        private float _audioLevel;
        private string _activeLanguageCode = "EN";
        private string _currentStatus = "READY";

        // Resize edge detection in device-independent pixels
        private const int ResizeEdge = 8;

        public FloatingOverlay()
        {
            InitializeComponent();

            if (!double.IsNaN(Config.ConfigManager.Config.OverlayLeft) && !double.IsNaN(Config.ConfigManager.Config.OverlayTop))
            {
                Left = Config.ConfigManager.Config.OverlayLeft;
                Top  = Config.ConfigManager.Config.OverlayTop;
            }
            else
            {
                Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
                Top  = 50;
            }

            if (!double.IsNaN(Config.ConfigManager.Config.OverlayWidth))  Width  = Config.ConfigManager.Config.OverlayWidth;
            if (!double.IsNaN(Config.ConfigManager.Config.OverlayHeight)) Height = Config.ConfigManager.Config.OverlayHeight;

            Closing += FloatingOverlay_Closing;
            SourceInitialized += (_, _) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                HwndSource.FromHwnd(hwnd)?.AddHook(WndProcHook);
            };
            WaveCanvas.SizeChanged += (_, _) => BuildWaveBars();

            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += (_, _) => UpdateTimerDisplay();

            _waveTimer.Interval = TimeSpan.FromMilliseconds(50);
            _waveTimer.Tick += (_, _) => AnimateWave();
            _waveTimer.Start();

            _toastTimer.Interval = TimeSpan.FromSeconds(2.8);
            _toastTimer.Tick += (_, _) =>
            {
                _toastTimer.Stop();
                ToastBorder.Visibility = Visibility.Collapsed;
            };

            // Debounced size persistence — saves 1 s after the user stops resizing
            _saveSizeTimer.Interval = TimeSpan.FromSeconds(1);
            _saveSizeTimer.Tick += (_, _) =>
            {
                _saveSizeTimer.Stop();
                Config.ConfigManager.Config.OverlayWidth  = Width;
                Config.ConfigManager.Config.OverlayHeight = Height;
                Config.ConfigManager.Save();
            };
            SizeChanged += (_, _) => { _saveSizeTimer.Stop(); _saveSizeTimer.Start(); };

            BuildWaveBars();
            ApplyVisualState("READY");
            SizeChanged += (_, _) => UpdateResponsiveLayout();
            Loaded += (_, _) => EnsureVisibleOnScreen();
            Container.SizeChanged += (_, _) => UpdateContainerClip();
            Loaded += (_, _) => UpdateContainerClip();
        }

        // ── Responsive label/badge breakpoints ────────────────────────────────
        // ≥180 → full,  120–179 → short,  <120 → icon only
        // LanguageBadge hidden <160,  TimerBadge hidden <130
        private static readonly Dictionary<string, string> ShortStatus = new(StringComparer.OrdinalIgnoreCase)
        {
            { "READY",        "RDY"  },
            { "RECORDING",    "REC"  },
            { "TRANSCRIBING", "TRNS" },
            { "REFINING",     "REF"  },
            { "ERROR",        "ERR"  },
        };

        private void UpdateResponsiveLayout()
        {
            double w = ActualWidth;

            // Font scales down as window shrinks
            double fontSize = w >= 200 ? 12 : w >= 140 ? 10 : w >= 100 ? 8.5 : 7;
            StatusText.FontSize = fontSize;

            // Status text visibility / content
            if (w < 100)
            {
                StatusText.Visibility = Visibility.Collapsed;
            }
            else
            {
                StatusText.Visibility = Visibility.Visible;
                StatusText.Text = w < 180
                    ? (ShortStatus.TryGetValue(_currentStatus, out var s) ? s : _currentStatus[..Math.Min(4, _currentStatus.Length)])
                    : _currentStatus;
            }

            // Badges — only relevant when recording
            if (_isRecording || _isProcessing)
            {
                LanguageBadge.Visibility = (w >= 160 && _isRecording) ? Visibility.Visible : Visibility.Collapsed;
                TimerBadge.Visibility    = (w >= 130 && _isRecording) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        // ── All-direction resize via WM_NCHITTEST ─────────────────────────────
        private const int WM_NCHITTEST  = 0x0084;
        private const int HTCAPTION     = 2;
        private const int HTLEFT        = 10;
        private const int HTRIGHT       = 11;
        private const int HTTOP         = 12;
        private const int HTTOPLEFT     = 13;
        private const int HTTOPRIGHT    = 14;
        private const int HTBOTTOM      = 15;
        private const int HTBOTTOMLEFT  = 16;
        private const int HTBOTTOMRIGHT = 17;

        private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WM_NCHITTEST) return IntPtr.Zero;

            // Extract signed screen coordinates from packed lParam
            int screenX = unchecked((short)(lParam.ToInt64() & 0xFFFF));
            int screenY = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));

            var rel = PointFromScreen(new Point(screenX, screenY));
            double x = rel.X;
            double y = rel.Y;
            double w = ActualWidth;
            double h = ActualHeight;
            double e = ResizeEdge;

            bool left   = x <= e;
            bool right  = x >= w - e;
            bool top    = y <= e;
            bool bottom = y >= h - e;

            int hit = 0;
            if      (left  && top)    hit = HTTOPLEFT;
            else if (right && top)    hit = HTTOPRIGHT;
            else if (left  && bottom) hit = HTBOTTOMLEFT;
            else if (right && bottom) hit = HTBOTTOMRIGHT;
            else if (left)            hit = HTLEFT;
            else if (right)           hit = HTRIGHT;
            else if (top)             hit = HTTOP;
            else if (bottom)          hit = HTBOTTOM;

            if (hit != 0)
            {
                handled = true;
                return new IntPtr(hit);
            }

            return IntPtr.Zero;
        }

        private void Container_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;

            // Don't steal the click when user is near a window edge — WndProc handles resize there.
            var pos = e.GetPosition(this);
            bool nearEdge = pos.X <= ResizeEdge || pos.X >= ActualWidth  - ResizeEdge
                         || pos.Y <= ResizeEdge || pos.Y >= ActualHeight - ResizeEdge;
            if (!nearEdge)
                DragMove();
        }

        private async void StartStopMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current is App app)
            {
                await app.ToggleRecordingFromOverlayAsync();
            }
        }

        private void SettingsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var mainWindow = Application.Current.MainWindow;
            if (mainWindow == null) return;

            if (!mainWindow.IsVisible)
            {
                mainWindow.Show();
            }

            if (mainWindow.WindowState == WindowState.Minimized)
            {
                mainWindow.WindowState = WindowState.Normal;
            }

            mainWindow.Activate();
        }

        private void HideMenuItem_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        private void FloatingOverlay_Closing(object? sender, CancelEventArgs e)
        {
            Config.ConfigManager.Config.OverlayLeft = Left;
            Config.ConfigManager.Config.OverlayTop = Top;
            Config.ConfigManager.Config.OverlayWidth = Width;
            Config.ConfigManager.Config.OverlayHeight = Height;
            Config.ConfigManager.Save();
        }

        private void BuildWaveBars()
        {
            WaveCanvas.Children.Clear();
            _bars.Clear();

            double canvasWidth = WaveCanvas.ActualWidth;
            if (canvasWidth <= 0)
            {
                canvasWidth = Math.Max(56, ActualWidth - 24);
            }

            int barCount = GetResponsiveBarCount(canvasWidth);
            double gap = canvasWidth / (barCount * 2.0);
            double width = Math.Max(2, gap);
            var brush = BuildWaveBrush();

            for (int i = 0; i < barCount; i++)
            {
                var bar = new Rectangle
                {
                    Width = width,
                    Height = 3.5,
                    RadiusX = 1.8,
                    RadiusY = 1.8,
                    Fill = brush
                };

                Canvas.SetLeft(bar, i * gap * 2);
                Canvas.SetTop(bar, 9);

                WaveCanvas.Children.Add(bar);
                _bars.Add(bar);
            }
        }

        private void AnimateWave()
        {
            if (_bars.Count == 0)
            {
                BuildWaveBars();
                return;
            }

            double canvasHeight = WaveCanvas.ActualHeight > 0 ? WaveCanvas.ActualHeight : 60;
            double maxBarHeight = canvasHeight * 0.88;
            double minBarHeight = 3;
            double staticIdleHeight = 4;

            // Boost quiet audio so bars react visibly even at low input levels.
            // sqrt curve: 0.2 input → 0.59 boosted; 0.05 input → 0.33 boosted.
            double boostedLevel = Math.Sqrt(Math.Min(_audioLevel * 2.8f, 1f));

            long ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            for (int i = 0; i < _bars.Count; i++)
            {
                double targetHeight;
                if (_isRecording)
                {
                    // Independent random spike per bar + smooth sinusoidal profile
                    double spike   = _rng.NextDouble() * 0.55;
                    double profile = 0.45 + 0.55 * Math.Abs(Math.Sin(i * 0.62 + ms / 160.0));
                    double energy  = boostedLevel * (0.5 + spike) * profile;
                    targetHeight   = minBarHeight + energy * (maxBarHeight - minBarHeight);
                }
                else if (_isProcessing)
                {
                    double pulse = 0.38 + 0.38 * Math.Abs(Math.Sin(ms / 110.0 + i * 0.52));
                    targetHeight = minBarHeight + pulse * (maxBarHeight - minBarHeight) * 0.55;
                }
                else
                {
                    targetHeight = staticIdleHeight;
                }

                // Fast lerp toward target so bars snap quickly
                double current = _bars[i].Height;
                _bars[i].Height = current + (targetHeight - current) * 0.65;
                Canvas.SetTop(_bars[i], (canvasHeight - _bars[i].Height) / 2.0);
            }

            if (!_isRecording)
            {
                _audioLevel = 0f;
            }
        }

        private void UpdateTimerDisplay()
        {
            var elapsed = DateTime.Now - _recordStart;
            TimerText.Text = elapsed.ToString(@"m\:ss");
        }

        public void SetStatus(string status, Brush? color = null)
        {
            Dispatcher.Invoke(() =>
            {
                if (!IsVisible)
                {
                    Show();
                    EnsureVisibleOnScreen();
                }

                string normalized = string.IsNullOrWhiteSpace(status)
                    ? "READY"
                    : status.Trim().ToUpperInvariant();

                // Only TRANSCRIBING or REFINING counts as "was processing"—not RECORDING.
                // This prevents a spurious toast when recording stops before transcription arrives.
                bool wasProcessing =
                    _currentStatus.Equals("TRANSCRIBING", StringComparison.OrdinalIgnoreCase) ||
                    _currentStatus.Equals("REFINING", StringComparison.OrdinalIgnoreCase);

                _currentStatus = normalized;
                StatusText.Text = normalized;

                if (normalized.Equals("RECORDING", StringComparison.OrdinalIgnoreCase))
                {
                    _isRecording = true;
                    _isProcessing = false;
                    _recordStart = DateTime.Now;
                    _timer.Start();
                    TimerText.Text = "0:00";
                    TimerBadge.Visibility = Visibility.Visible;
                    LanguageText.Text = _activeLanguageCode;
                    LanguageBadge.Visibility = Visibility.Visible;
                }
                else
                {
                    _isRecording = false;
                    _timer.Stop();
                    TimerBadge.Visibility = Visibility.Collapsed;
                    LanguageBadge.Visibility = Visibility.Collapsed;
                    _isProcessing = normalized.Equals("TRANSCRIBING", StringComparison.OrdinalIgnoreCase) ||
                                    normalized.Equals("REFINING", StringComparison.OrdinalIgnoreCase);
                }

                if (normalized.Equals("ERROR", StringComparison.OrdinalIgnoreCase))
                {
                    ShowToast("Transcription error");
                }

                StartStopMenuItem.Header = _isRecording ? "Stop Recording" : "Start Recording";

                ApplyVisualState(normalized);
                UpdateResponsiveLayout();
            });
        }

        public void SetActiveLanguage(string languageCode)
        {
            Dispatcher.Invoke(() =>
            {
                var normalized = string.IsNullOrWhiteSpace(languageCode)
                    ? "EN"
                    : languageCode.Trim().ToUpperInvariant();

                if (normalized.Length > 8)
                {
                    normalized = normalized[..8];
                }

                _activeLanguageCode = normalized;
                if (_isRecording)
                {
                    LanguageText.Text = _activeLanguageCode;
                    LanguageBadge.Visibility = Visibility.Visible;
                }
            });
        }

        public void UpdateAudioLevel(float level)
        {
            _audioLevel = Math.Clamp(level, 0f, 1f);
        }

        /// <summary>Called after the overlay skin dictionary is swapped so DynamicResource-broken
        /// manually-set brushes get re-evaluated immediately.</summary>
        public void RefreshSkin()
        {
            Dispatcher.Invoke(() =>
            {
                ApplyVisualState(_currentStatus);
                BuildWaveBars(); // rebuild bars with new brush colour
            });
        }

        private void ApplyVisualState(string status)
        {
            bool isRecording = status.Equals("RECORDING", StringComparison.OrdinalIgnoreCase);
            bool isProcessing = status.Equals("TRANSCRIBING", StringComparison.OrdinalIgnoreCase)
                || status.Equals("REFINING", StringComparison.OrdinalIgnoreCase);

            string backgroundKey = isRecording
                ? "OverlaySkin.PillowRecordingBrush"
                : isProcessing
                    ? "OverlaySkin.PillowProcessingBrush"
                    : "OverlaySkin.PillowIdleBrush";

            string borderKey = isRecording
                ? "OverlaySkin.PillowRecordingBorderBrush"
                : isProcessing
                    ? "OverlaySkin.PillowProcessingBorderBrush"
                    : "OverlaySkin.PillowIdleBorderBrush";

            if (TryFindResource(backgroundKey) is Brush background)
            {
                Container.Background = background;
            }

            if (TryFindResource(borderKey) is Brush border)
            {
                Container.BorderBrush = border;
            }

            var waveBrush = BuildWaveBrush();
            foreach (var bar in _bars)
            {
                bar.Fill = waveBrush;
            }
        }

        private Brush BuildWaveBrush()
        {
            var resourceKey = _isRecording ? "OverlaySkin.IconRecordingBrush" : "OverlaySkin.IconProcessingBrush";
            var baseBrush = TryFindResource(resourceKey) as Brush ?? TryFindResource("OverlaySkin.AccentBrush") as Brush;

            if (baseBrush is SolidColorBrush solid)
            {
                var c = solid.Color;
                return new SolidColorBrush(Color.FromArgb(190, c.R, c.G, c.B));
            }

            return new SolidColorBrush(Color.FromArgb(190, 90, 120, 220));
        }

        private void ShowToast(string message)
        {
            ToastText.Text = message;
            ToastBorder.Visibility = Visibility.Visible;
            _toastTimer.Stop();
            _toastTimer.Start();
        }

        private void UpdateContainerClip()
        {
            if (ContainerContent == null || Container == null) return;

            // Border.CornerRadius does not clip child visuals; apply an explicit rounded clip
            // so translucent content doesn't show square corners.
            const double radius = 20.5;
            var rect = new Rect(0, 0, Container.ActualWidth, Container.ActualHeight);
            if (rect.Width <= 0 || rect.Height <= 0) return;

            ContainerContent.Clip = new RectangleGeometry(rect, radius, radius);
        }

        public void EnsureVisibleOnScreen()
        {
            Dispatcher.Invoke(() =>
            {
                double screenLeft = SystemParameters.VirtualScreenLeft;
                double screenTop = SystemParameters.VirtualScreenTop;
                double screenRight = screenLeft + SystemParameters.VirtualScreenWidth;
                double screenBottom = screenTop + SystemParameters.VirtualScreenHeight;

                const double visibleMargin = 56;
                const double minOverlayWidth = 56;
                const double minOverlayHeight = 40;

                if (Width < minOverlayWidth) Width = minOverlayWidth;
                if (Height < minOverlayHeight) Height = minOverlayHeight;

                if (Width > SystemParameters.VirtualScreenWidth)
                    Width = SystemParameters.VirtualScreenWidth;
                if (Height > SystemParameters.VirtualScreenHeight)
                    Height = SystemParameters.VirtualScreenHeight;

                double minLeft = screenLeft - Width + visibleMargin;
                double maxLeft = screenRight - visibleMargin;
                double minTop = screenTop;
                double maxTop = screenBottom - visibleMargin;

                Left = Math.Max(minLeft, Math.Min(Left, maxLeft));
                Top = Math.Max(minTop, Math.Min(Top, maxTop));
            });
        }

        private static int GetResponsiveBarCount(double canvasWidth)
        {
            if (canvasWidth <= 80) return 6;
            if (canvasWidth <= 120) return 8;
            if (canvasWidth <= 180) return 12;
            if (canvasWidth <= 260) return 16;
            if (canvasWidth <= 360) return 20;
            return 24;
        }
    }
}
