using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Speakly.Config;
using Speakly.ViewModels;

namespace Speakly
{
    public partial class OnboardingWindow : Window
    {
        private enum HotkeyCaptureTarget
        {
            None,
            Ptt,
            Toggle
        }

        private readonly MainViewModel _vm;
        private int _currentStep;
        private HotkeyCaptureTarget _captureTarget = HotkeyCaptureTarget.None;

        private static readonly Brush ActiveBadgeBrush = new SolidColorBrush(Color.FromRgb(31, 41, 55));
        private static readonly Brush InactiveBadgeBrush = new SolidColorBrush(Color.FromRgb(75, 85, 99));
        private static readonly Brush DefaultInputBorderBrush = new SolidColorBrush(Color.FromRgb(148, 163, 184));
        private static readonly Brush CaptureInputBorderBrush = new SolidColorBrush(Color.FromRgb(220, 38, 38));

        public OnboardingWindow(MainViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            DataContext = vm;
            _currentStep = 0;
            UpdateStep();
            UpdateButtons();
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep > 0)
                _currentStep--;
            UpdateStep();
            UpdateButtons();
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep < 3)
                _currentStep++;
            UpdateStep();
            UpdateButtons();
        }

        private void Finish_Click(object sender, RoutedEventArgs e)
        {
            ConfigManager.Config.DeepgramApiKey = DeepgramKeyBox.Password.Trim();
            ConfigManager.Config.OpenAIApiKey = OpenAIKeyBox.Password.Trim();
            ConfigManager.Config.CerebrasApiKey = CerebrasKeyBox.Password.Trim();
            ConfigManager.Config.OpenRouterApiKey = OpenRouterKeyBox.Password.Trim();

            if (string.IsNullOrWhiteSpace(ConfigManager.Config.DeepgramApiKey)
                && string.IsNullOrWhiteSpace(ConfigManager.Config.OpenAIApiKey)
                && string.IsNullOrWhiteSpace(ConfigManager.Config.CerebrasApiKey)
                && string.IsNullOrWhiteSpace(ConfigManager.Config.OpenRouterApiKey))
            {
                ValidationText.Text = "Add at least one API key before finishing.";
                _currentStep = 1;
                UpdateStep();
                UpdateButtons();
                return;
            }

            ConfigManager.Config.FirstRunCompleted = true;
            ConfigManager.Save();
            DialogResult = true;
            Close();
        }

        private void UpdateButtons()
        {
            BackButton.IsEnabled = _currentStep > 0;
            NextButton.IsEnabled = _currentStep < 3;
            FinishButton.IsEnabled = _currentStep == 3;
        }

        private void UpdateStep()
        {
            WelcomePanel.Visibility = _currentStep == 0 ? Visibility.Visible : Visibility.Collapsed;
            ApiKeysPanel.Visibility = _currentStep == 1 ? Visibility.Visible : Visibility.Collapsed;
            AudioPanel.Visibility = _currentStep == 2 ? Visibility.Visible : Visibility.Collapsed;
            FinishPanel.Visibility = _currentStep == 3 ? Visibility.Visible : Visibility.Collapsed;

            if (_currentStep != 2 && _captureTarget != HotkeyCaptureTarget.None)
            {
                EndHotkeyCapture(string.Empty);
            }

            WelcomeBadge.Background = _currentStep == 0 ? ActiveBadgeBrush : InactiveBadgeBrush;
            ApiKeysBadge.Background = _currentStep == 1 ? ActiveBadgeBrush : InactiveBadgeBrush;
            AudioBadge.Background = _currentStep == 2 ? ActiveBadgeBrush : InactiveBadgeBrush;
            FinishBadge.Background = _currentStep == 3 ? ActiveBadgeBrush : InactiveBadgeBrush;

            WelcomeBadge.BorderBrush = _currentStep == 0 ? ActiveBadgeBrush : InactiveBadgeBrush;
            ApiKeysBadge.BorderBrush = _currentStep == 1 ? ActiveBadgeBrush : InactiveBadgeBrush;
            AudioBadge.BorderBrush = _currentStep == 2 ? ActiveBadgeBrush : InactiveBadgeBrush;
            FinishBadge.BorderBrush = _currentStep == 3 ? ActiveBadgeBrush : InactiveBadgeBrush;
        }

        private void StepBadge_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button) return;
            if (!int.TryParse(button.Tag?.ToString(), out var step)) return;
            _currentStep = Math.Max(0, Math.Min(3, step));
            UpdateStep();
            UpdateButtons();
        }

        private void HotkeyBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;

            if (sender == PttHotkeyBox)
            {
                BeginHotkeyCapture(HotkeyCaptureTarget.Ptt);
                return;
            }

            if (sender == RecordHotkeyBox)
            {
                BeginHotkeyCapture(HotkeyCaptureTarget.Toggle);
            }
        }

        private void BeginHotkeyCapture(HotkeyCaptureTarget target)
        {
            _captureTarget = target;
            HotkeyCaptureHint.Text = "Recording... press your hotkey combination now.";
            HotkeyCaptureHint.Foreground = CaptureInputBorderBrush;
            UpdateHotkeyCaptureVisuals();
            Keyboard.Focus(this);
        }

        private void EndHotkeyCapture(string statusText)
        {
            _captureTarget = HotkeyCaptureTarget.None;
            UpdateHotkeyCaptureVisuals();

            if (string.IsNullOrWhiteSpace(statusText))
            {
                HotkeyCaptureHint.Text = "Click a hotkey field, then press a key combination (Ctrl/Alt/Shift/Win supported).";
                HotkeyCaptureHint.Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105));
            }
            else
            {
                HotkeyCaptureHint.Text = statusText;
                HotkeyCaptureHint.Foreground = CaptureInputBorderBrush;
            }
        }

        private void UpdateHotkeyCaptureVisuals()
        {
            SetHotkeyBoxVisualState(PttHotkeyBox, _captureTarget == HotkeyCaptureTarget.Ptt);
            SetHotkeyBoxVisualState(RecordHotkeyBox, _captureTarget == HotkeyCaptureTarget.Toggle);
        }

        private static void SetHotkeyBoxVisualState(TextBox box, bool isCaptureArmed)
        {
            box.BorderBrush = isCaptureArmed ? CaptureInputBorderBrush : DefaultInputBorderBrush;
            box.BorderThickness = isCaptureArmed ? new Thickness(2) : new Thickness(1);
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_captureTarget == HotkeyCaptureTarget.None) return;

            e.Handled = true;

            if (e.Key == Key.Escape)
            {
                EndHotkeyCapture("Hotkey capture cancelled.");
                return;
            }

            if (IsModifierOnlyKey(e)) return;

            Key mainKey = e.Key == Key.System ? e.SystemKey : e.Key;
            var modifiers = new List<string>();
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers.Add("Ctrl");
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers.Add("Alt");
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers.Add("Shift");
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers.Add("Win");

            string combo = modifiers.Count > 0
                ? $"{string.Join("+", modifiers)}+{mainKey}"
                : mainKey.ToString();

            if (_captureTarget == HotkeyCaptureTarget.Ptt)
            {
                if (string.Equals(combo, _vm.RecordHotkey, StringComparison.OrdinalIgnoreCase))
                {
                    EndHotkeyCapture("PTT and Toggle hotkeys must be different.");
                    return;
                }

                _vm.PttHotkey = combo;
                PttHotkeyBox.Text = combo;
            }
            else
            {
                if (string.Equals(combo, _vm.PttHotkey, StringComparison.OrdinalIgnoreCase))
                {
                    EndHotkeyCapture("PTT and Toggle hotkeys must be different.");
                    return;
                }

                _vm.RecordHotkey = combo;
                RecordHotkeyBox.Text = combo;
            }

            EndHotkeyCapture($"Captured: {combo}");
        }

        private static bool IsModifierOnlyKey(KeyEventArgs e)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            return key == Key.LeftCtrl || key == Key.RightCtrl ||
                   key == Key.LeftAlt || key == Key.RightAlt ||
                   key == Key.LeftShift || key == Key.RightShift ||
                   key == Key.LWin || key == Key.RWin;
        }
    }
}
