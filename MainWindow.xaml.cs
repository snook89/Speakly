using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Speakly.Config;
using Speakly.ViewModels;

namespace Speakly
{
    public partial class MainWindow : Window
    {
        private bool _isRecordingPtt = false;
        private bool _isRecordingRec = false;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        // Populate PasswordBoxes from config on load (WPF PasswordBox can't data-bind)
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            DeepgramPwdBox.Password = ConfigManager.Config.DeepgramApiKey;
            OpenAIPwdBox.Password = ConfigManager.Config.OpenAIApiKey;
            OpenRouterPwdBox.Password = ConfigManager.Config.OpenRouterApiKey;
            CerebrasPwdBox.Password = ConfigManager.Config.CerebrasApiKey;
            
            // Restore Window Bounds
            if (!double.IsNaN(ConfigManager.Config.MainWindowLeft)) Left = ConfigManager.Config.MainWindowLeft;
            if (!double.IsNaN(ConfigManager.Config.MainWindowTop)) Top = ConfigManager.Config.MainWindowTop;
            if (!double.IsNaN(ConfigManager.Config.MainWindowWidth)) Width = ConfigManager.Config.MainWindowWidth;
            if (!double.IsNaN(ConfigManager.Config.MainWindowHeight)) Height = ConfigManager.Config.MainWindowHeight;
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // Save Window Bounds
            ConfigManager.Config.MainWindowLeft = this.Left;
            ConfigManager.Config.MainWindowTop = this.Top;
            ConfigManager.Config.MainWindowWidth = this.Width;
            ConfigManager.Config.MainWindowHeight = this.Height;
            ConfigManager.Save();
        }

        // Toggle API key visibility on "Show/Hide" button click
        private void RevealApiKey_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            string tag = btn.Tag?.ToString() ?? "";

            PasswordBox? pwdBox = null;
            TextBox? txtBox = null;

            switch (tag)
            {
                case "Deepgram":
                    pwdBox = DeepgramPwdBox; txtBox = DeepgramTxtBox; break;
                case "OpenAI":
                    pwdBox = OpenAIPwdBox; txtBox = OpenAITxtBox; break;
                case "OpenRouter":
                    pwdBox = OpenRouterPwdBox; txtBox = OpenRouterTxtBox; break;
                case "Cerebras":
                    pwdBox = CerebrasPwdBox; txtBox = CerebrasTxtBox; break;
            }

            if (pwdBox == null || txtBox == null) return;

            bool isHidden = (pwdBox.Visibility == Visibility.Visible);
            if (isHidden)
            {
                // Switch to visible TextBox
                txtBox.Text = pwdBox.Password;
                pwdBox.Visibility = Visibility.Collapsed;
                txtBox.Visibility = Visibility.Visible;
                btn.Content = "🙈 Hide";
            }
            else
            {
                // Switch back to PasswordBox
                pwdBox.Password = txtBox.Text;
                txtBox.Visibility = Visibility.Collapsed;
                pwdBox.Visibility = Visibility.Visible;
                btn.Content = "👁 Show";
            }
        }

        // Sync PasswordBox values back to the ViewModel/config before saving
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // If a field's PasswordBox is currently visible (not revealed), read from it
            if (DeepgramPwdBox.Visibility == Visibility.Visible)
                ConfigManager.Config.DeepgramApiKey = DeepgramPwdBox.Password;
            if (OpenAIPwdBox.Visibility == Visibility.Visible)
                ConfigManager.Config.OpenAIApiKey = OpenAIPwdBox.Password;
            if (OpenRouterPwdBox.Visibility == Visibility.Visible)
                ConfigManager.Config.OpenRouterApiKey = OpenRouterPwdBox.Password;
            if (CerebrasPwdBox.Visibility == Visibility.Visible)
                ConfigManager.Config.CerebrasApiKey = CerebrasPwdBox.Password;
            // If fields are revealed TextBoxes, their binding to the ViewModel already updated config
        }

        private void ToggleFavoriteModel_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;
            if (!string.Equals(vm.RefinementModel, "OpenRouter", StringComparison.OrdinalIgnoreCase)) return;

            string? modelId = null;

            if (sender is MenuItem menuItem)
            {
                modelId = menuItem.DataContext as string;

                if (string.IsNullOrWhiteSpace(modelId) && menuItem.Parent is ContextMenu contextMenu)
                {
                    if (contextMenu.PlacementTarget is ComboBoxItem comboBoxItem)
                    {
                        modelId = comboBoxItem.DataContext as string;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(modelId)) return;
            vm.ToggleOpenRouterFavorite(modelId);
        }

        private void SetPtt_Click(object sender, RoutedEventArgs e)
        {
            _isRecordingPtt = true;
            _isRecordingRec = false;
            PttHotkeyBox.Text = "Press a key combo...";
            PttHotkeyBox.Background = TryFindResource("AppCaptureBgBrush") as Brush ?? Brushes.DarkRed;
            this.PreviewKeyDown += MainWindow_PreviewKeyDown;
        }

        private void SetRec_Click(object sender, RoutedEventArgs e)
        {
            _isRecordingRec = true;
            _isRecordingPtt = false;
            RecordHotkeyBox.Text = "Press a key combo...";
            RecordHotkeyBox.Background = TryFindResource("AppCaptureBgBrush") as Brush ?? Brushes.DarkRed;
            this.PreviewKeyDown += MainWindow_PreviewKeyDown;
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!_isRecordingPtt && !_isRecordingRec) return;

            e.Handled = true;

            // Ignore modifier-only key presses until they press a "main" key
            if (e.Key == Key.LeftCtrl || e.Key == Key.RightCtrl ||
                e.Key == Key.LeftAlt || e.Key == Key.RightAlt ||
                e.Key == Key.LeftShift || e.Key == Key.RightShift ||
                e.Key == Key.LWin || e.Key == Key.RWin ||
                e.Key == Key.System && (e.SystemKey == Key.LeftAlt || e.SystemKey == Key.RightAlt))
            {
                return;
            }

            Key key = (e.Key == Key.System ? e.SystemKey : e.Key);
            
            var modifiers = new List<string>();
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers.Add("Ctrl");
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers.Add("Alt");
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers.Add("Shift");
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers.Add("Win");
            
            string keyStr = key.ToString();
            if (modifiers.Count > 0)
            {
                keyStr = string.Join("+", modifiers) + "+" + keyStr;
            }

            if (_isRecordingPtt)
            {
                PttHotkeyBox.Text = keyStr;
                PttHotkeyBox.ClearValue(Control.BackgroundProperty); 
                ConfigManager.Config.PttHotkey = keyStr;
                if (DataContext is MainViewModel vm) vm.PttHotkey = keyStr;
                _isRecordingPtt = false;
            }
            else if (_isRecordingRec)
            {
                RecordHotkeyBox.Text = keyStr;
                RecordHotkeyBox.ClearValue(Control.BackgroundProperty);
                ConfigManager.Config.RecordHotkey = keyStr;
                if (DataContext is MainViewModel vm) vm.RecordHotkey = keyStr;
                _isRecordingRec = false;
            }

            this.PreviewKeyDown -= MainWindow_PreviewKeyDown;
        }
    }
}