using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Speakly.Config;
using Speakly.ViewModels;

namespace Speakly.Pages
{
    public partial class HotkeysPage : UserControl
    {
        private bool _isRecordingPtt = false;
        private bool _isRecordingRec = false;

        public HotkeysPage()
        {
            InitializeComponent();
            DataContext = App.ViewModel;
            ValidateHotkeys();
        }

        private void SetPtt_Click(object sender, RoutedEventArgs e)
        {
            _isRecordingPtt = true;
            _isRecordingRec = false;
            PttHotkeyBox.Text = "Press a key combo...";
            PttHotkeyBox.Background = TryFindResource("AppCaptureBgBrush") as Brush ?? Brushes.DarkRed;
            Window.GetWindow(this).PreviewKeyDown += Page_PreviewKeyDown;
        }

        private void SetRec_Click(object sender, RoutedEventArgs e)
        {
            _isRecordingRec = true;
            _isRecordingPtt = false;
            RecordHotkeyBox.Text = "Press a key combo...";
            RecordHotkeyBox.Background = TryFindResource("AppCaptureBgBrush") as Brush ?? Brushes.DarkRed;
            Window.GetWindow(this).PreviewKeyDown += Page_PreviewKeyDown;
        }

        private void Page_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!_isRecordingPtt && !_isRecordingRec) return;
            e.Handled = true;

            if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl ||
                e.Key == Key.LeftAlt || e.Key == Key.RightAlt ||
                e.Key == Key.LeftShift || e.Key == Key.RightShift ||
                e.Key == Key.LWin || e.Key == Key.RWin ||
                (e.Key == Key.System && (e.SystemKey == Key.LeftAlt || e.SystemKey == Key.RightAlt)))
                return;

            Key key = e.Key == Key.System ? e.SystemKey : e.Key;

            var modifiers = new List<string>();
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers.Add("Ctrl");
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))     modifiers.Add("Alt");
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))   modifiers.Add("Shift");
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers.Add("Win");

            string keyStr = modifiers.Count > 0
                ? string.Join("+", modifiers) + "+" + key.ToString()
                : key.ToString();

            var vm = DataContext as MainViewModel;

            if (_isRecordingPtt)
            {
                if (string.Equals(keyStr, ConfigManager.Config.RecordHotkey, System.StringComparison.OrdinalIgnoreCase))
                {
                    ShowValidation("PTT and Toggle hotkeys must be different.");
                    return;
                }

                PttHotkeyBox.Text = keyStr;
                PttHotkeyBox.ClearValue(BackgroundProperty);
                ConfigManager.Config.PttHotkey = keyStr;
                if (vm != null) vm.PttHotkey = keyStr;
                _isRecordingPtt = false;
            }
            else if (_isRecordingRec)
            {
                if (string.Equals(keyStr, ConfigManager.Config.PttHotkey, System.StringComparison.OrdinalIgnoreCase))
                {
                    ShowValidation("PTT and Toggle hotkeys must be different.");
                    return;
                }

                RecordHotkeyBox.Text = keyStr;
                RecordHotkeyBox.ClearValue(BackgroundProperty);
                ConfigManager.Config.RecordHotkey = keyStr;
                if (vm != null) vm.RecordHotkey = keyStr;
                _isRecordingRec = false;
            }

            Window.GetWindow(this).PreviewKeyDown -= Page_PreviewKeyDown;
            ValidateHotkeys();
        }

        private void ResetDefaults_Click(object sender, RoutedEventArgs e)
        {
            ConfigManager.Config.PttHotkey = "Space";
            ConfigManager.Config.RecordHotkey = "F9";

            if (DataContext is MainViewModel vm)
            {
                vm.PttHotkey = "Space";
                vm.RecordHotkey = "F9";
            }

            PttHotkeyBox.Text = "Space";
            RecordHotkeyBox.Text = "F9";
            ValidationText.Text = "Hotkeys reset to defaults.";
            ValidationText.Foreground = TryFindResource("SystemFillColorSuccessBrush") as Brush ?? Brushes.LightGreen;
        }

        private void ValidateHotkeys()
        {
            if (string.Equals(ConfigManager.Config.PttHotkey, ConfigManager.Config.RecordHotkey, System.StringComparison.OrdinalIgnoreCase))
            {
                ShowValidation("PTT and Toggle hotkeys must be different.");
                return;
            }

            ValidationText.Text = string.Empty;
        }

        private void ShowValidation(string message)
        {
            ValidationText.Text = message;
            ValidationText.Foreground = TryFindResource("SystemFillColorCautionBrush") as Brush ?? Brushes.OrangeRed;
        }
    }
}
