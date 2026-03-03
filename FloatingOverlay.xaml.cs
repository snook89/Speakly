using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;
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
        private readonly DispatcherTimer _saveBoundsTimer = new();
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

            // Debounced bounds persistence — saves after drag/resize settles.
            _saveBoundsTimer.Interval = TimeSpan.FromMilliseconds(500);
            _saveBoundsTimer.Tick += (_, _) =>
            {
                _saveBoundsTimer.Stop();
                PersistBounds();
            };
            SizeChanged += (_, _) => QueuePersistBounds();
            LocationChanged += (_, _) => QueuePersistBounds();

            BuildWaveBars();
            ApplyVisualState("READY");
            SizeChanged += (_, _) => UpdateResponsiveLayout();
            Loaded += (_, _) =>
            {
                EnsureVisibleOnScreen();
                QueuePersistBounds();
            };
            Container.SizeChanged += (_, _) => UpdateContainerClip();
            Loaded += (_, _) => UpdateContainerClip();
            SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
            Closed += FloatingOverlay_Closed;
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
            PersistBounds();
        }

        private void FloatingOverlay_Closed(object? sender, EventArgs e)
        {
            SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
            _saveBoundsTimer.Stop();
        }

        private void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                EnsureVisibleOnScreen();
                QueuePersistBounds();
            });
        }

        private void QueuePersistBounds()
        {
            _saveBoundsTimer.Stop();
            _saveBoundsTimer.Start();
        }

        private void PersistBounds()
        {
            if (!IsFinite(Left) || !IsFinite(Top) || !IsFinite(Width) || !IsFinite(Height))
            {
                return;
            }

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

        private void ToggleRefinementMenuItem_Click(object sender, RoutedEventArgs e)
        {
            App.ViewModel.ToggleRefinementQuickCommand.Execute(null);
        }

        private void NextProfileMenuItem_Click(object sender, RoutedEventArgs e)
        {
            App.ViewModel.CycleProfileCommand.Execute(null);
            ShowToast($"Profile: {App.ViewModel.ActiveProfileName}");
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
                const double minOverlayWidth = 56;
                const double minOverlayHeight = 40;
                var workArea = GetNearestMonitorWorkArea();

                double defaultWidth = IsFinite(ActualWidth) && ActualWidth > 0 ? ActualWidth : 420;
                double defaultHeight = IsFinite(ActualHeight) && ActualHeight > 0 ? ActualHeight : 96;

                if (!IsFinite(Width) || Width < minOverlayWidth)
                    Width = Math.Max(minOverlayWidth, defaultWidth);
                if (!IsFinite(Height) || Height < minOverlayHeight)
                    Height = Math.Max(minOverlayHeight, defaultHeight);

                Width = Math.Min(Width, Math.Max(minOverlayWidth, workArea.Width));
                Height = Math.Min(Height, Math.Max(minOverlayHeight, workArea.Height));

                if (!IsFinite(Left) || !IsFinite(Top))
                {
                    Left = workArea.Left + (workArea.Width - Width) / 2.0;
                    Top = workArea.Top + 50;
                }

                double minLeft = workArea.Left;
                double maxLeft = Math.Max(minLeft, workArea.Right - Width);
                double minTop = workArea.Top;
                double maxTop = Math.Max(minTop, workArea.Bottom - Height);

                Left = Clamp(Left, minLeft, maxLeft);
                Top = Clamp(Top, minTop, maxTop);
            });
        }

        private Rect GetNearestMonitorWorkArea()
        {
            double virtualLeft = SystemParameters.VirtualScreenLeft;
            double virtualTop = SystemParameters.VirtualScreenTop;
            double virtualWidth = Math.Max(1, SystemParameters.VirtualScreenWidth);
            double virtualHeight = Math.Max(1, SystemParameters.VirtualScreenHeight);
            var fallback = new Rect(virtualLeft, virtualTop, virtualWidth, virtualHeight);

            if (!IsFinite(Left) || !IsFinite(Top) || !IsFinite(Width) || !IsFinite(Height))
            {
                return fallback;
            }

            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget == null)
            {
                return fallback;
            }

            var toDevice = source.CompositionTarget.TransformToDevice;
            var fromDevice = source.CompositionTarget.TransformFromDevice;

            var topLeftPx = toDevice.Transform(new Point(Left, Top));
            var bottomRightPx = toDevice.Transform(new Point(Left + Width, Top + Height));
            var windowRectPx = new Rect(
                Math.Min(topLeftPx.X, bottomRightPx.X),
                Math.Min(topLeftPx.Y, bottomRightPx.Y),
                Math.Abs(bottomRightPx.X - topLeftPx.X),
                Math.Abs(bottomRightPx.Y - topLeftPx.Y));

            if (windowRectPx.Width <= 0 || windowRectPx.Height <= 0)
            {
                return fallback;
            }

            var rect = new RECT
            {
                Left = (int)Math.Floor(windowRectPx.Left),
                Top = (int)Math.Floor(windowRectPx.Top),
                Right = (int)Math.Ceiling(windowRectPx.Right),
                Bottom = (int)Math.Ceiling(windowRectPx.Bottom),
            };

            IntPtr monitor = MonitorFromRect(ref rect, MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero)
            {
                return fallback;
            }

            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(monitor, ref info))
            {
                return fallback;
            }

            var workTopLeftDip = fromDevice.Transform(new Point(info.rcWork.Left, info.rcWork.Top));
            var workBottomRightDip = fromDevice.Transform(new Point(info.rcWork.Right, info.rcWork.Bottom));

            var workRect = new Rect(
                Math.Min(workTopLeftDip.X, workBottomRightDip.X),
                Math.Min(workTopLeftDip.Y, workBottomRightDip.Y),
                Math.Abs(workBottomRightDip.X - workTopLeftDip.X),
                Math.Abs(workBottomRightDip.Y - workTopLeftDip.Y));

            return workRect.Width > 1 && workRect.Height > 1 ? workRect : fallback;
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        private static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromRect(ref RECT lprc, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
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
