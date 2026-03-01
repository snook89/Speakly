using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Speakly
{
    public partial class FloatingOverlay : Window
    {
        // ── Timer ──────────────────────────────────────────
        private readonly DispatcherTimer _timer = new();
        private DateTime _recordStart;
        private bool _isRecording;

        // ── Waveform ───────────────────────────────────────
        private readonly DispatcherTimer _waveTimer = new();
        private readonly List<Rectangle> _bars = new();
        private float _audioLevel = 0f;          // 0.0 – 1.0, updated externally
        private readonly Random _rng = new();
        private const int BAR_COUNT = 22;

        // ── Accent colour (changes with status) ───────────
        private Brush _accentBrush = Brushes.Aqua;

        public FloatingOverlay()
        {
            InitializeComponent();

            // Default position or restored position
            if (!double.IsNaN(Config.ConfigManager.Config.OverlayLeft) && !double.IsNaN(Config.ConfigManager.Config.OverlayTop))
            {
                Left = Config.ConfigManager.Config.OverlayLeft;
                Top = Config.ConfigManager.Config.OverlayTop;
            }
            else
            {
                Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
                Top = 50;
            }

            if (!double.IsNaN(Config.ConfigManager.Config.OverlayWidth)) Width = Config.ConfigManager.Config.OverlayWidth;
            if (!double.IsNaN(Config.ConfigManager.Config.OverlayHeight)) Height = Config.ConfigManager.Config.OverlayHeight;
            
            Closing += FloatingOverlay_Closing;

            // Build waveform bars
            BuildWaveBars();

            // Timer that updates the clock every second
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += (_, _) => UpdateTimerDisplay();

            // Timer that animates the waveform bars every 60 ms
            _waveTimer.Interval = TimeSpan.FromMilliseconds(60);
            _waveTimer.Tick += (_, _) => AnimateWave();
            _waveTimer.Start();
        }

        // ── Drag support (window has no chrome, so we do it manually) ──
        private void Container_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void FloatingOverlay_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            Config.ConfigManager.Config.OverlayLeft = this.Left;
            Config.ConfigManager.Config.OverlayTop = this.Top;
            Config.ConfigManager.Config.OverlayWidth = this.Width;
            Config.ConfigManager.Config.OverlayHeight = this.Height;
            Config.ConfigManager.Save();
        }

        // ── Build waveform bar rectangles ─────────────────
        private void BuildWaveBars()
        {
            WaveCanvas.Children.Clear();
            _bars.Clear();

            double canvasW = 260 - 28; // approx inner width after padding
            double gap      = canvasW / (BAR_COUNT * 2.0);

            for (int i = 0; i < BAR_COUNT; i++)
            {
                var bar = new Rectangle
                {
                    Width        = gap,
                    Height       = 4,
                    Fill         = new SolidColorBrush(Color.FromArgb(180, 0, 210, 255)),
                    RadiusX      = 2,
                    RadiusY      = 2,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Canvas.SetLeft(bar, i * gap * 2);
                Canvas.SetTop(bar, 12);   // centre initially
                WaveCanvas.Children.Add(bar);
                _bars.Add(bar);
            }
        }

        // ── Wave animation ────────────────────────────────
        private void AnimateWave()
        {
            double canvasH = WaveCanvas.ActualHeight > 0 ? WaveCanvas.ActualHeight : 28;
            double maxBarH = canvasH * 0.9;
            double minBarH = 3;

            for (int i = 0; i < _bars.Count; i++)
            {
                double target;
                if (_isRecording)
                {
                    // Sine wave envelope + random spike driven by audio level
                    double phase = (DateTime.Now.Millisecond / 1000.0) * Math.PI * 2 + i * 0.4;
                    double sineVal = (Math.Sin(phase) + 1) / 2;       // 0-1
                    double randomJitter = _rng.NextDouble() * 0.4;
                    // Boost the influence of _audioLevel
                    double combined = (sineVal * 0.2 + randomJitter * 0.8) * _audioLevel
                                    + sineVal * 0.1;  // idle breathing (reduced while recording)
                    target = minBarH + combined * (maxBarH - minBarH);
                }
                else
                {
                    // Gentle idle pulse
                    double phase = (DateTime.Now.Millisecond / 1000.0) * Math.PI * 2 + i * 0.5;
                    target = minBarH + ((Math.Sin(phase) + 1) / 2) * 5;
                }

                // Lerp towards target for smooth animation
                double current = _bars[i].Height;
                _bars[i].Height = current + (target - current) * 0.35;
                Canvas.SetTop(_bars[i], (canvasH - _bars[i].Height) / 2);
            }
        }

        // ── Timer display ─────────────────────────────────
        private void UpdateTimerDisplay()
        {
            var elapsed = DateTime.Now - _recordStart;
            TimerText.Text = elapsed.ToString(@"m\:ss");
        }

        // ── Public API called from App.xaml.cs ───────────
        public void SetStatus(string status, Brush? color = null)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = status.ToUpper();
                if (color != null)
                {
                    _accentBrush = color;
                    StatusIndicator.Fill = color;
                    // Recolour wave bars
                    foreach (var bar in _bars)
                    {
                        if (bar.Fill is SolidColorBrush scb)
                        {
                            var c = ((SolidColorBrush)color).Color;
                            bar.Fill = new SolidColorBrush(Color.FromArgb(180, c.R, c.G, c.B));
                        }
                    }
                }

                // Manage timer & recording flag
                if (status.Equals("RECORDING", StringComparison.OrdinalIgnoreCase))
                {
                    _isRecording = true;
                    _recordStart = DateTime.Now;
                    _timer.Start();
                }
                else
                {
                    _isRecording = false;
                    _timer.Stop();
                    if (!status.Equals("READY", StringComparison.OrdinalIgnoreCase))
                        TimerText.Text = "";    // clear while processing
                    else
                        TimerText.Text = "";    // clear on ready
                }
            });
        }

        /// <summary>
        /// Call this from the audio pipeline to push the current RMS energy level (0-1).
        /// </summary>
        public void UpdateAudioLevel(float level)
        {
            _audioLevel = Math.Clamp(level, 0f, 1f);
        }
    }
}
