using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Speakly.Services;

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
        private string _activeModeDisplay = DictationExperienceService.PlainDictationMode;
        private string _activeContextDisplay = string.Empty;
        private string _activeContextTooltip = string.Empty;
        private string _currentStatus = "READY";
        private string _overlayStyle = "Rectangular";
        private bool _isApplyingCircleSize;
        private double _lastCircleSize = CircleMaxSize;

        private const string OverlayStyleRectangular = "Rectangular";
        private const string OverlayStyleCircle = "Circle";
        private const double CircleMinSize = 50;
        private const double CircleMaxSize = 62;

        // Resize edge detection in device-independent pixels
        private const int ResizeEdge = 8;
        private static readonly IntPtr HwndTopMost = new(-1);
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoActivate = 0x0010;

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
                EnsureTopmost();
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
            SizeChanged += (_, _) =>
            {
                EnforceCircleSizeBounds();
                QueuePersistBounds();
            };
            LocationChanged += (_, _) => QueuePersistBounds();

            SetOverlayStyle(Config.ConfigManager.Config.OverlayStyle);
            SetActiveMode(Config.ConfigManager.Config.DictationMode);
            ApplyVisualState("READY");
            SizeChanged += (_, _) => UpdateResponsiveLayout();
            Loaded += (_, _) =>
            {
                EnsureVisibleOnScreen();
                EnsureTopmost();
                QueuePersistBounds();
            };
            Container.SizeChanged += (_, _) => UpdateContainerClip();
            Loaded += (_, _) => UpdateContainerClip();
            Activated += (_, _) => EnsureTopmost();
            SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
            Closed += FloatingOverlay_Closed;
        }

        private void UpdateResponsiveLayout()
        {
            if (IsCircleStyle)
            {
                ToastBorder.Visibility = Visibility.Collapsed;
                _toastTimer.Stop();
                StatusStack.Visibility = Visibility.Collapsed;
                BadgeStack.Visibility = Visibility.Collapsed;
                WaveCanvas.Visibility = Visibility.Collapsed;
                TimerBadge.Visibility = Visibility.Collapsed;
                LanguageBadge.Visibility = Visibility.Collapsed;
                StatusText.Visibility = Visibility.Collapsed;
                SubStatusText.Visibility = Visibility.Collapsed;
                MicClusterScaleTransform.ScaleX = 1;
                MicClusterScaleTransform.ScaleY = 1;
                return;
            }

            double w = ActualWidth;
            double h = ActualHeight;
            StatusStack.Visibility = Visibility.Visible;
            BadgeStack.Visibility = Visibility.Visible;
            WaveCanvas.Visibility = Visibility.Visible;
            StatusText.Visibility = Visibility.Visible;
            SubStatusText.Visibility = Visibility.Visible;
            StatusText.FontSize = w >= 280 ? 13 : w >= 220 ? 12 : 11;
            SubStatusText.FontSize = w >= 280 ? 11 : 10;

            StatusText.Visibility = w < 130 ? Visibility.Collapsed : Visibility.Visible;
            SubStatusText.Visibility = w < 190 ? Visibility.Collapsed : Visibility.Visible;

            // Shrink mic cluster when overlay height approaches minimum to avoid visual clipping.
            double heightFactor = (h - 56.0) / 8.0; // 56 -> 0, 64 -> 1
            heightFactor = Math.Max(0, Math.Min(1, heightFactor));
            double micScale = 0.82 + (heightFactor * 0.18);
            if (w < 240)
            {
                micScale = Math.Min(micScale, 0.9);
            }

            MicClusterScaleTransform.ScaleX = micScale;
            MicClusterScaleTransform.ScaleY = micScale;

            // Timer is optional UI noise; keep language badge as the only right-side badge by default.
            TimerBadge.Visibility = Visibility.Collapsed;
            ContextBadge.Visibility = string.IsNullOrWhiteSpace(_activeContextDisplay) || w < 250
                ? Visibility.Collapsed
                : Visibility.Visible;
            ModeBadge.Visibility = w < 210 ? Visibility.Collapsed : Visibility.Visible;
            LanguageBadge.Visibility = (w >= 160 && _isRecording) ? Visibility.Visible : Visibility.Collapsed;
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

        private void OverlayContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            SyncOverlayMenuColors();
            RebuildModesMenu();
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

        private void EnforceCircleSizeBounds()
        {
            if (!IsCircleStyle || _isApplyingCircleSize)
            {
                return;
            }

            _isApplyingCircleSize = true;
            try
            {
                double width = IsFinite(Width) ? Width : _lastCircleSize;
                double height = IsFinite(Height) ? Height : _lastCircleSize;

                double rawTarget;
                bool growing = width > _lastCircleSize + 0.25 || height > _lastCircleSize + 0.25;
                bool shrinking = width < _lastCircleSize - 0.25 || height < _lastCircleSize - 0.25;

                if (growing)
                {
                    rawTarget = Math.Max(width, height);
                }
                else if (shrinking)
                {
                    rawTarget = Math.Min(width, height);
                }
                else
                {
                    rawTarget = Math.Min(width, height);
                }

                if (!IsFinite(rawTarget) || rawTarget <= 0)
                {
                    rawTarget = CircleMaxSize;
                }

                double target = Clamp(rawTarget, CircleMinSize, CircleMaxSize);
                _lastCircleSize = target;

                if (Math.Abs(Width - target) > 0.5)
                {
                    Width = target;
                }

                if (Math.Abs(Height - target) > 0.5)
                {
                    Height = target;
                }

                Container.CornerRadius = new CornerRadius(target / 2.0);
                UpdateCircleVisualMetrics(target);
            }
            finally
            {
                _isApplyingCircleSize = false;
            }
        }

        private void PersistBounds()
        {
            if (!IsFinite(Left) || !IsFinite(Top) || !IsFinite(Width) || !IsFinite(Height))
            {
                return;
            }

            Config.ConfigManager.Config.OverlayLeft = Left;
            Config.ConfigManager.Config.OverlayTop = Top;
            if (!IsCircleStyle)
            {
                Config.ConfigManager.Config.OverlayWidth = Width;
                Config.ConfigManager.Config.OverlayHeight = Height;
            }
            Config.ConfigManager.Save();
        }

        private void BuildWaveBars()
        {
            if (IsCircleStyle)
            {
                WaveCanvas.Children.Clear();
                _bars.Clear();
                return;
            }

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
            if (IsCircleStyle)
            {
                _audioLevel = 0f;
                return;
            }

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
                EnsureTopmost();

                string normalized = string.IsNullOrWhiteSpace(status)
                    ? "READY"
                    : status.Trim().ToUpperInvariant();
                bool isPttRecording = normalized.Equals("PTT_RECORDING", StringComparison.OrdinalIgnoreCase);
                bool isRecording = isPttRecording
                    || normalized.Equals("RECORDING", StringComparison.OrdinalIgnoreCase)
                    || normalized.Equals("NO_MIC_SIGNAL", StringComparison.OrdinalIgnoreCase);

                _currentStatus = normalized;

                if (isRecording)
                {
                    _isRecording = true;
                    _isProcessing = false;
                    _recordStart = DateTime.Now;
                    _timer.Start();
                    TimerText.Text = "0:00";
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

                if (!normalized.Equals("ERROR", StringComparison.OrdinalIgnoreCase))
                {
                    _toastTimer.Stop();
                    ToastBorder.Visibility = Visibility.Collapsed;
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

        public void SetActiveMode(string mode)
        {
            Dispatcher.Invoke(() =>
            {
                _activeModeDisplay = DictationExperienceService.NormalizeMode(mode);
                ModeText.Text = _activeModeDisplay;
                ApplyVisualState(_currentStatus);
                UpdateResponsiveLayout();
            });
        }

        public void SetContextSummary(string? contextSummary)
        {
            Dispatcher.Invoke(() =>
            {
                var normalized = contextSummary?.Trim() ?? string.Empty;
                _activeContextTooltip = normalized;
                _activeContextDisplay = BuildContextBadgeText(normalized);
                ContextText.Text = _activeContextDisplay;
                ContextBadge.ToolTip = string.IsNullOrWhiteSpace(normalized) ? null : normalized;
                ApplyVisualState(_currentStatus);
                UpdateResponsiveLayout();
            });
        }

        public void UpdateAudioLevel(float level)
        {
            _audioLevel = Math.Clamp(level, 0f, 1f);
        }

        public void SetOverlayStyle(string overlayStyle)
        {
            Dispatcher.Invoke(() =>
            {
                _overlayStyle = NormalizeOverlayStyleName(overlayStyle);
                ApplyOverlayStyleLayout();
                ApplyVisualState(_currentStatus);
                UpdateResponsiveLayout();
                EnsureVisibleOnScreen();
                QueuePersistBounds();
            });
        }

        /// <summary>Called after the overlay skin dictionary is swapped so DynamicResource-broken
        /// manually-set brushes get re-evaluated immediately.</summary>
        public void RefreshSkin()
        {
            Dispatcher.Invoke(() =>
            {
                ApplyVisualState(_currentStatus);
                UpdateContainerClip();
                BuildWaveBars(); // rebuild bars with new brush colour
            });
        }

        private void ApplyVisualState(string status)
        {
            bool isPttRecording = status.Equals("PTT_RECORDING", StringComparison.OrdinalIgnoreCase);
            bool isRecording = isPttRecording
                || status.Equals("RECORDING", StringComparison.OrdinalIgnoreCase)
                || status.Equals("NO_MIC_SIGNAL", StringComparison.OrdinalIgnoreCase);
            bool isProcessing = status.Equals("TRANSCRIBING", StringComparison.OrdinalIgnoreCase)
                || status.Equals("REFINING", StringComparison.OrdinalIgnoreCase);
            bool isError = status.Equals("ERROR", StringComparison.OrdinalIgnoreCase);

            if (status.Equals("READY", StringComparison.OrdinalIgnoreCase))
            {
                StatusText.Text = "Ready";
                SubStatusText.Text = BuildSubStatus("Hold hotkey to speak");
            }
            else if (isRecording)
            {
                if (status.Equals("NO_MIC_SIGNAL", StringComparison.OrdinalIgnoreCase))
                {
                    StatusText.Text = "No mic signal";
                    SubStatusText.Text = BuildSubStatus("Check mute or input device");
                }
                else
                {
                    StatusText.Text = "Listening...";
                    SubStatusText.Text = BuildSubStatus("Release to transcribe");
                }
            }
            else if (isProcessing)
            {
                StatusText.Text = "Transcribing...";
                SubStatusText.Text = BuildSubStatus("AI is processing");
            }
            else if (isError)
            {
                StatusText.Text = "Error";
                SubStatusText.Text = BuildSubStatus("Transcription error");
            }
            else
            {
                StatusText.Text = status;
                SubStatusText.Text = BuildSubStatus(string.Empty);
            }

            StatusText.Foreground = GetSkinBrush(
                "OverlaySkin.StatusForegroundBrush",
                "BrushTextPrimary",
                Color.FromRgb(0xE7, 0xE9, 0xEC));
            SubStatusText.Foreground = GetSkinBrush(
                "OverlaySkin.SubtleTextBrush",
                "BrushTextSecondary",
                Color.FromRgb(0xA9, 0xB0, 0xB7));

            if (isRecording)
            {
                if (IsCircleStyle)
                {
                    var activeColor = status.Equals("NO_MIC_SIGNAL", StringComparison.OrdinalIgnoreCase)
                        ? Color.FromRgb(0xF5, 0x9E, 0x0B)
                        : isPttRecording
                            ? Color.FromRgb(0x22, 0xC5, 0x5E)
                            : Color.FromRgb(0xEF, 0x44, 0x44);

                    Container.Background = GetSkinBrush(
                        "OverlaySkin.PillowIdleBrush",
                        "OverlayContainerBrush",
                        Color.FromArgb(0xE6, 0x11, 0x12, 0x14));
                    Container.BorderBrush = new SolidColorBrush(Color.FromArgb(220, activeColor.R, activeColor.G, activeColor.B));
                    MicBg.Background = new SolidColorBrush(Color.FromArgb(42, activeColor.R, activeColor.G, activeColor.B));
                    MicBg.BorderBrush = new SolidColorBrush(Color.FromArgb(230, activeColor.R, activeColor.G, activeColor.B));
                    MicRingTrack.Stroke = new SolidColorBrush(Color.FromArgb(120, activeColor.R, activeColor.G, activeColor.B));
                    MicRingActive.Stroke = new SolidColorBrush(activeColor);
                    MicIcon.Foreground = new SolidColorBrush(activeColor);
                    ApplyCircleGlow(activeColor);
                }
                else
                {
                    Container.Background = GetSkinBrush(
                        "OverlaySkin.PillowRecordingBrush",
                        "OverlayContainerBrush",
                        Color.FromArgb(0xE6, 0x11, 0x12, 0x14));
                    Container.BorderBrush = GetSkinBrush(
                        "OverlaySkin.PillowRecordingBorderBrush",
                        "BrushAccent",
                        Color.FromRgb(0x06, 0xB6, 0xD4));
                    MicBg.Background = GetSkinBrush(
                        "OverlaySkin.PillowRecordingBrush",
                        "BrushAccentSubtle",
                        Color.FromArgb(0x19, 0x06, 0xB6, 0xD4));
                    MicBg.BorderBrush = GetSkinBrush(
                        "OverlaySkin.PillowRecordingBorderBrush",
                        "BrushAccent",
                        Color.FromRgb(0x06, 0xB6, 0xD4));
                    MicRingTrack.Stroke = GetSkinBrush(
                        "OverlaySkin.PillowRecordingBorderBrush",
                        "BrushAccentSubtle",
                        Color.FromArgb(0xA0, 0x06, 0xB6, 0xD4));
                    MicRingActive.Stroke = GetSkinBrush(
                        "OverlaySkin.IconRecordingBrush",
                        "BrushAccent",
                        Color.FromRgb(0x06, 0xB6, 0xD4));
                    MicIcon.Foreground = GetSkinBrush(
                        "OverlaySkin.IconRecordingBrush",
                        "OverlaySkin.AccentBrush",
                        Color.FromRgb(0x06, 0xB6, 0xD4));
                    ApplyCircleGlow(null);
                }
                StartRingSpin(950);
                if (IsCircleStyle)
                {
                    StopPulse();
                }
                else
                {
                    StartPulse();
                }
            }
            else if (isProcessing)
            {
                Container.Background = GetSkinBrush(
                    "OverlaySkin.PillowProcessingBrush",
                    "OverlayContainerBrush",
                    Color.FromArgb(0xE6, 0x11, 0x12, 0x14));
                Container.BorderBrush = GetSkinBrush(
                    "OverlaySkin.PillowProcessingBorderBrush",
                    "BrushWarning",
                    Color.FromRgb(0xF5, 0x9E, 0x0B));
                MicBg.Background = GetSkinBrush(
                    "OverlaySkin.PillowProcessingBrush",
                    "BrushAccentSubtle",
                    Color.FromArgb(0x19, 0x06, 0xB6, 0xD4));
                MicBg.BorderBrush = GetSkinBrush(
                    "OverlaySkin.PillowProcessingBorderBrush",
                    "BrushBorderDefault",
                    Color.FromRgb(0x2A, 0x31, 0x3C));
                MicRingTrack.Stroke = GetSkinBrush(
                    "OverlaySkin.PillowProcessingBorderBrush",
                    "BrushWarning",
                    Color.FromArgb(0xAA, 0xF5, 0x9E, 0x0B));
                MicRingActive.Stroke = GetSkinBrush(
                    "OverlaySkin.IconProcessingBrush",
                    "BrushWarning",
                    Color.FromRgb(0xF5, 0x9E, 0x0B));
                MicIcon.Foreground = GetSkinBrush(
                    "OverlaySkin.IconProcessingBrush",
                    "BrushWarning",
                    Color.FromRgb(0xF5, 0x9E, 0x0B));
                ApplyCircleGlow(null);
                StartRingSpin(1400);
                StopPulse();
            }
            else if (isError)
            {
                Container.Background = GetSkinBrush(
                    "OverlaySkin.CancelBrush",
                    "BrushDangerSubtle",
                    Color.FromArgb(0x2E, 0xEF, 0x44, 0x44));
                Container.BorderBrush = GetSkinBrush(
                    "OverlaySkin.CancelBorderBrush",
                    "BrushDanger",
                    Color.FromRgb(0xEF, 0x44, 0x44));
                MicBg.Background = GetSkinBrush(
                    "OverlaySkin.CancelBrush",
                    "BrushDangerSubtle",
                    Color.FromArgb(0x2E, 0xEF, 0x44, 0x44));
                MicBg.BorderBrush = GetSkinBrush(
                    "OverlaySkin.CancelBorderBrush",
                    "BrushDanger",
                    Color.FromRgb(0xEF, 0x44, 0x44));
                MicRingTrack.Stroke = GetSkinBrush(
                    "OverlaySkin.CancelBorderBrush",
                    "BrushDangerSubtle",
                    Color.FromArgb(0xAA, 0xEF, 0x44, 0x44));
                MicRingActive.Stroke = GetSkinBrush(
                    "OverlaySkin.CancelForegroundBrush",
                    "BrushDanger",
                    Color.FromRgb(0xEF, 0x44, 0x44));
                MicIcon.Foreground = GetSkinBrush(
                    "OverlaySkin.CancelForegroundBrush",
                    "BrushDanger",
                    Color.FromRgb(0xEF, 0x44, 0x44));
                ApplyCircleGlow(null);
                StartRingSpin(1700);
                StopPulse();
            }
            else
            {
                Container.Background = GetSkinBrush(
                    "OverlaySkin.PillowIdleBrush",
                    "OverlayContainerBrush",
                    Color.FromArgb(0xE6, 0x11, 0x12, 0x14));
                Container.BorderBrush = GetSkinBrush(
                    "OverlaySkin.PillowIdleBorderBrush",
                    "BrushBorderDefault",
                    Color.FromRgb(0x2A, 0x31, 0x3C));
                MicBg.Background = GetSkinBrush(
                    "OverlaySkin.LanguageBadgeBrush",
                    "BrushAccentSubtle",
                    Color.FromArgb(0x19, 0x06, 0xB6, 0xD4));
                MicBg.BorderBrush = GetSkinBrush(
                    "OverlaySkin.PillowIdleBorderBrush",
                    "BrushBorderDefault",
                    Color.FromRgb(0x2A, 0x31, 0x3C));
                MicRingTrack.Stroke = GetSkinBrush(
                    "OverlaySkin.IconIdleBrush",
                    "BrushAccent",
                    Color.FromArgb(0x80, 0x06, 0xB6, 0xD4));
                MicRingActive.Stroke = GetSkinBrush(
                    "OverlaySkin.IconIdleBrush",
                    "OverlaySkin.AccentBrush",
                    Color.FromRgb(0x06, 0xB6, 0xD4));
                MicIcon.Foreground = GetSkinBrush(
                    "OverlaySkin.IconIdleBrush",
                    "OverlaySkin.AccentBrush",
                    Color.FromRgb(0x06, 0xB6, 0xD4));
                ApplyCircleGlow(null);
                StopRingSpin();
                StopPulse();
            }

            var waveBrush = BuildWaveBrush();
            foreach (var bar in _bars)
            {
                bar.Fill = waveBrush;
            }
        }

        private Brush BuildWaveBrush()
        {
            Brush? baseBrush = null;
            if (_isRecording)
            {
                baseBrush = GetBrushOrNull("OverlaySkin.AccentBrush")
                    ?? GetBrushOrNull("BrushAccent");
            }
            else if (_isProcessing)
            {
                baseBrush = GetBrushOrNull("OverlaySkin.IconProcessingBrush")
                    ?? GetBrushOrNull("BrushWarning");
            }
            else if (_currentStatus.Equals("ERROR", StringComparison.OrdinalIgnoreCase))
            {
                baseBrush = GetBrushOrNull("OverlaySkin.CancelForegroundBrush")
                    ?? GetBrushOrNull("BrushDanger");
            }

            baseBrush ??= GetBrushOrNull("OverlaySkin.AccentBrush")
                ?? GetBrushOrNull("BrushAccent");

            if (baseBrush is SolidColorBrush solid)
            {
                var c = solid.Color;
                return new SolidColorBrush(Color.FromArgb(170, c.R, c.G, c.B));
            }

            return new SolidColorBrush(Color.FromArgb(170, 6, 182, 212));
        }

        private bool IsCircleStyle =>
            string.Equals(_overlayStyle, OverlayStyleCircle, StringComparison.OrdinalIgnoreCase);

        private void ApplyOverlayStyleLayout()
        {
            if (IsCircleStyle)
            {
                ResizeMode = ResizeMode.CanResize;
                MinWidth = CircleMinSize;
                MaxWidth = CircleMaxSize;
                MinHeight = CircleMinSize;
                MaxHeight = CircleMaxSize;
                _lastCircleSize = Clamp(
                    IsFinite(Width) && IsFinite(Height) ? Math.Min(Width, Height) : CircleMaxSize,
                    CircleMinSize,
                    CircleMaxSize);
                EnforceCircleSizeBounds();

                Container.CornerRadius = new CornerRadius(Math.Min(Width, Height) / 2.0);
                Container.BorderThickness = new Thickness(1.2);

                MicColumn.Width = new GridLength(1, GridUnitType.Star);
                StatusColumn.Width = new GridLength(0);
                BadgesColumn.Width = new GridLength(0);
                Grid.SetColumn(MicCluster, 0);
                MicCluster.HorizontalAlignment = HorizontalAlignment.Center;
                MicCluster.Margin = new Thickness(0);
                UpdateCircleVisualMetrics(Math.Min(Width, Height));
            }
            else
            {
                ResizeMode = ResizeMode.CanResize;
                MinWidth = 280;
                MinHeight = 64;
                MaxWidth = 1800;
                MaxHeight = 600;

                double configuredWidth = Config.ConfigManager.Config.OverlayWidth;
                double configuredHeight = Config.ConfigManager.Config.OverlayHeight;
                Width = IsFinite(configuredWidth) && configuredWidth >= MinWidth ? configuredWidth : Math.Max(MinWidth, Width);
                Height = IsFinite(configuredHeight) && configuredHeight >= MinHeight ? configuredHeight : Math.Max(MinHeight, Height);

                Container.CornerRadius = new CornerRadius(32);
                Container.Padding = new Thickness(16, 10, 16, 10);
                Container.BorderThickness = new Thickness(1);

                MicColumn.Width = GridLength.Auto;
                StatusColumn.Width = new GridLength(1, GridUnitType.Star);
                BadgesColumn.Width = GridLength.Auto;
                Grid.SetColumn(MicCluster, 0);
                MicCluster.HorizontalAlignment = HorizontalAlignment.Left;
                MicCluster.Margin = new Thickness(0, 0, 12, 0);
                MicCluster.Width = 44;
                MicCluster.Height = 44;
                MicRingTrack.Width = 42;
                MicRingTrack.Height = 42;
                MicRingTrack.StrokeThickness = 1.2;
                MicRingActive.Width = 42;
                MicRingActive.Height = 42;
                MicRingActive.StrokeThickness = 2.2;
                MicRingRotate.CenterX = 21;
                MicRingRotate.CenterY = 21;
                MicBg.Width = 32;
                MicBg.Height = 32;
                MicBg.CornerRadius = new CornerRadius(16);
                MicBg.BorderThickness = new Thickness(1);
                MicIcon.FontSize = 16;
                MicRingActive.StrokeDashArray = new DoubleCollection { 3, 3 };
                _lastCircleSize = CircleMaxSize;
            }

            UpdateContainerClip();
            BuildWaveBars();
        }

        private static string NormalizeOverlayStyleName(string? overlayStyle)
        {
            return string.Equals(overlayStyle, OverlayStyleCircle, StringComparison.OrdinalIgnoreCase)
                ? OverlayStyleCircle
                : OverlayStyleRectangular;
        }

        private void ApplyCircleGlow(Color? glowColor)
        {
            if (!IsCircleStyle || glowColor is null)
            {
                MicBg.Effect = null;
                MicRingActive.Effect = null;
                return;
            }

            var color = glowColor.Value;
            double t = Clamp((Math.Min(Width, Height) - CircleMinSize) / Math.Max(1, CircleMaxSize - CircleMinSize), 0, 1);
            double bgBlur = Lerp(10, 18, t);
            double ringBlur = Lerp(8, 14, t);
            MicBg.Effect = new DropShadowEffect
            {
                Color = color,
                BlurRadius = bgBlur,
                ShadowDepth = 0,
                Opacity = 0.9
            };
            MicRingActive.Effect = new DropShadowEffect
            {
                Color = color,
                BlurRadius = ringBlur,
                ShadowDepth = 0,
                Opacity = 0.95
            };
        }

        private void UpdateCircleVisualMetrics(double size)
        {
            if (!IsCircleStyle)
            {
                return;
            }

            double t = Clamp((size - CircleMinSize) / Math.Max(1, CircleMaxSize - CircleMinSize), 0, 1);
            double padding = Lerp(5, 8, t);
            double cluster = Lerp(30, 38, t);
            double ring = cluster - 2;
            double iconHost = Lerp(22, 28, t);
            double iconSize = Lerp(12, 14, t);
            double trackThickness = Lerp(1.0, 1.2, t);
            double activeThickness = Lerp(1.5, 1.8, t);
            double dash = Lerp(2.2, 3.0, t);

            Container.Padding = new Thickness(padding);
            MicCluster.Width = cluster;
            MicCluster.Height = cluster;

            MicRingTrack.Width = ring;
            MicRingTrack.Height = ring;
            MicRingTrack.StrokeThickness = trackThickness;

            MicRingActive.Width = ring;
            MicRingActive.Height = ring;
            MicRingActive.StrokeThickness = activeThickness;
            MicRingActive.StrokeDashArray = new DoubleCollection { dash, dash };
            MicRingRotate.CenterX = ring / 2.0;
            MicRingRotate.CenterY = ring / 2.0;

            MicBg.Width = iconHost;
            MicBg.Height = iconHost;
            MicBg.CornerRadius = new CornerRadius(iconHost / 2.0);
            MicBg.BorderThickness = new Thickness(1);
            MicIcon.FontSize = iconSize;
        }

        private static double Lerp(double from, double to, double t)
        {
            return from + ((to - from) * Clamp(t, 0, 1));
        }

        private void StartPulse()
        {
            var pulse = new DoubleAnimation(1.0, 1.06, TimeSpan.FromMilliseconds(600))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };

            if (MicBg.RenderTransform is not ScaleTransform transform)
            {
                transform = new ScaleTransform(1, 1);
                MicBg.RenderTransform = transform;
                MicBg.RenderTransformOrigin = new Point(0.5, 0.5);
            }

            transform.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
            transform.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
        }

        private void StartRingSpin(double durationMs)
        {
            var spin = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(durationMs))
            {
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = null
            };
            MicRingRotate.BeginAnimation(RotateTransform.AngleProperty, spin);
        }

        private void StopRingSpin()
        {
            MicRingRotate.BeginAnimation(RotateTransform.AngleProperty, null);
            MicRingRotate.Angle = 0;
        }

        private void StopPulse()
        {
            if (MicBg.RenderTransform is not ScaleTransform transform) return;
            transform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            transform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            transform.ScaleX = 1;
            transform.ScaleY = 1;
        }

        private Brush GetBrush(string key, Color fallback)
        {
            return GetBrushOrNull(key) ?? new SolidColorBrush(fallback);
        }

        private Brush GetSkinBrush(string skinKey, string fallbackKey, Color fallback)
        {
            return GetBrushOrNull(skinKey)
                ?? GetBrushOrNull(fallbackKey)
                ?? new SolidColorBrush(fallback);
        }

        private Brush? GetBrushOrNull(string key)
        {
            return TryFindResource(key) as Brush;
        }

        private void ShowToast(string message)
        {
            if (IsCircleStyle)
            {
                _toastTimer.Stop();
                ToastBorder.Visibility = Visibility.Collapsed;
                return;
            }

            ToastText.Text = message;
            ToastBorder.Visibility = Visibility.Visible;
            _toastTimer.Stop();
            _toastTimer.Start();
        }

        private string BuildSubStatus(string baseText)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(baseText))
            {
                parts.Add(baseText);
            }

            if (!string.IsNullOrWhiteSpace(_activeModeDisplay))
            {
                parts.Add(_activeModeDisplay);
            }

            var contextStatus = BuildContextStatusText();
            if (!string.IsNullOrWhiteSpace(contextStatus))
            {
                parts.Add(contextStatus);
            }

            return string.Join(" | ", parts);
        }

        private string BuildContextStatusText()
        {
            if (string.IsNullOrWhiteSpace(_activeContextDisplay))
            {
                return string.Empty;
            }

            return $"Context: {BuildContextSourceText(_activeContextTooltip)}";
        }

        private static string BuildContextBadgeText(string? contextSummary)
        {
            var sources = GetContextSources(contextSummary);
            if (sources.Count == 0)
            {
                return string.Empty;
            }

            return $"CTX {string.Join("+", sources.Select(AbbreviateContextSource))}";
        }

        private static string BuildContextSourceText(string? contextSummary)
        {
            var sources = GetContextSources(contextSummary);
            return sources.Count == 0
                ? string.Empty
                : string.Join("+", sources);
        }

        private static List<string> GetContextSources(string? contextSummary)
        {
            var sources = new List<string>();
            if (string.IsNullOrWhiteSpace(contextSummary))
            {
                return sources;
            }

            if (contextSummary.Contains("App:", StringComparison.OrdinalIgnoreCase))
                sources.Add("App");
            if (contextSummary.Contains("Window:", StringComparison.OrdinalIgnoreCase))
                sources.Add("Window");
            if (contextSummary.Contains("Selected text:", StringComparison.OrdinalIgnoreCase))
                sources.Add("Selected");
            if (contextSummary.Contains("Clipboard:", StringComparison.OrdinalIgnoreCase))
                sources.Add("Clipboard");

            return sources;
        }

        private static string AbbreviateContextSource(string source)
        {
            return source switch
            {
                "Selected" => "Sel",
                "Clipboard" => "Clip",
                _ => source
            };
        }

        private void ToggleRefinementMenuItem_Click(object sender, RoutedEventArgs e)
        {
            App.ViewModel.ToggleRefinementQuickCommand.Execute(null);
        }

        private void NextModeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            App.ViewModel.CycleDictationModeCommand.Execute(null);
            SetActiveMode(App.ViewModel.DictationMode);
            ShowToast($"Mode: {App.ViewModel.DictationMode}");
        }

        private void NextProfileMenuItem_Click(object sender, RoutedEventArgs e)
        {
            App.ViewModel.CycleProfileCommand.Execute(null);
            SetActiveMode(App.ViewModel.DictationMode);
            ShowToast($"Profile: {App.ViewModel.ActiveProfileName}");
        }

        private void RebuildModesMenu()
        {
            ModesMenuItem.Items.Clear();
            var overlayMenuItemStyle = (Style)FindResource("OverlayMenuItemStyle");
            ModesMenuItem.ItemContainerStyle = overlayMenuItemStyle;

            foreach (var mode in App.ViewModel.AvailableDictationModes)
            {
                var item = new MenuItem
                {
                    Header = mode,
                    Tag = mode,
                    IsCheckable = true,
                    IsChecked = string.Equals(mode, App.ViewModel.DictationMode, StringComparison.OrdinalIgnoreCase),
                    Foreground = OverlayContextMenu.Foreground,
                    Background = OverlayContextMenu.Background,
                    BorderBrush = OverlayContextMenu.BorderBrush,
                    Style = overlayMenuItemStyle
                };

                item.Click += (_, _) =>
                {
                    App.ViewModel.SetDictationModeCommand.Execute(item.Tag?.ToString());
                    SetActiveMode(App.ViewModel.DictationMode);
                    ShowToast($"Mode: {App.ViewModel.DictationMode}");
                    RebuildModesMenu();
                };

                ModesMenuItem.Items.Add(item);
            }
        }

        private void SyncOverlayMenuColors()
        {
            var menuBackground = Container.Background
                ?? GetSkinBrush("OverlaySkin.PillowIdleBrush", "OverlayContainerBrush", Color.FromArgb(0xE6, 0x11, 0x12, 0x14));
            var menuBorder = Container.BorderBrush
                ?? GetSkinBrush("OverlaySkin.PillowIdleBorderBrush", "BrushBorderDefault", Color.FromRgb(0x2A, 0x31, 0x3C));
            var menuForeground = StatusText.Foreground
                ?? GetSkinBrush("OverlaySkin.StatusForegroundBrush", "BrushTextPrimary", Color.FromRgb(0xE7, 0xE9, 0xEC));

            OverlayContextMenu.Background = menuBackground;
            OverlayContextMenu.BorderBrush = menuBorder;
            OverlayContextMenu.Foreground = menuForeground;

            foreach (var menuItem in OverlayContextMenu.Items.OfType<MenuItem>())
            {
                menuItem.Background = menuBackground;
                menuItem.BorderBrush = Brushes.Transparent;
                menuItem.Foreground = menuForeground;
            }
        }

        private void UpdateContainerClip()
        {
            if (ContainerContent == null || Container == null) return;

            // Border.CornerRadius does not clip child visuals; apply an explicit rounded clip
            // so translucent content doesn't show square corners.
            double radius = Math.Max(0, Container.CornerRadius.TopLeft - 1.5);
            var rect = new Rect(0, 0, Container.ActualWidth, Container.ActualHeight);
            if (rect.Width <= 0 || rect.Height <= 0) return;

            ContainerContent.Clip = new RectangleGeometry(rect, radius, radius);
            Container.Clip = new RectangleGeometry(rect, radius + 1, radius + 1);
        }

        private void EnsureTopmost()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return;
            }

            _ = SetWindowPos(hwnd, HwndTopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        }

        public void EnsureVisibleOnScreen()
        {
            Dispatcher.Invoke(() =>
            {
                const double minOverlayWidth = 56;
                const double minOverlayHeight = 40;
                var workArea = GetNearestMonitorWorkArea();

                double defaultWidth = IsFinite(ActualWidth) && ActualWidth > 0 ? ActualWidth : 280;
                double defaultHeight = IsFinite(ActualHeight) && ActualHeight > 0 ? ActualHeight : 64;

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

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int X,
            int Y,
            int cx,
            int cy,
            uint uFlags);

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
