using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Speakly.Config;
using Speakly.Services;

namespace Speakly.Pages
{
    public partial class ApiKeysPage : UserControl
    {
        private static readonly Color GreenGlow = Color.FromRgb(0x22, 0xC5, 0x5E);
        private static readonly Color RedGlow   = Color.FromRgb(0xEF, 0x44, 0x44);

        public ApiKeysPage()
        {
            InitializeComponent();
            DataContext = App.ViewModel;
            TraceDebug("Constructor initialized.");

            // Loaded fires for every new instance; also wire ApiTestCompleted here.
            Loaded += (_, _) =>
            {
                TraceDebug("Loaded event fired.");
                App.ViewModel.ApiTestCompleted += OnApiTestCompleted;
                PopulatePasswordBoxes();
            };

            // Unloaded: detach ApiTestCompleted AND unsubscribe all PasswordChanged handlers
            // before the PasswordBox controls tear down their internal SecureString.
            // If the handlers were still subscribed during teardown, PasswordBox.PasswordChanged
            // would fire with "" and silently overwrite ConfigManager.Config with empty strings,
            // causing the fields to appear blank the next time the page is opened.
            Unloaded += (_, _) =>
            {
                TraceDebug("Unloaded event fired.");
                App.ViewModel.ApiTestCompleted -= OnApiTestCompleted;

                DeepgramPwdBox.PasswordChanged   -= DeepgramPwdBox_PasswordChanged;
                ElevenLabsPwdBox.PasswordChanged -= ElevenLabsPwdBox_PasswordChanged;
                OpenAIPwdBox.PasswordChanged     -= OpenAIPwdBox_PasswordChanged;
                OpenRouterPwdBox.PasswordChanged -= OpenRouterPwdBox_PasswordChanged;
                CerebrasPwdBox.PasswordChanged   -= CerebrasPwdBox_PasswordChanged;
            };

            // IsVisibleChanged covers the case where WPF UI caches the instance
            // and re-shows it without recreating it (Visible=false → true).
            IsVisibleChanged += (_, e) =>
            {
                TraceDebug($"IsVisibleChanged: {e.NewValue}");
                if ((bool)e.NewValue) PopulatePasswordBoxes();
            };
        }

        private void PopulatePasswordBoxes()
        {
            TraceDebug("PopulatePasswordBoxes start.");

            // Suppress PasswordChanged during programmatic fill so we don't
            // redundantly write back to config while reading from it.
            DeepgramPwdBox.PasswordChanged   -= DeepgramPwdBox_PasswordChanged;
            ElevenLabsPwdBox.PasswordChanged -= ElevenLabsPwdBox_PasswordChanged;
            OpenAIPwdBox.PasswordChanged     -= OpenAIPwdBox_PasswordChanged;
            OpenRouterPwdBox.PasswordChanged -= OpenRouterPwdBox_PasswordChanged;
            CerebrasPwdBox.PasswordChanged   -= CerebrasPwdBox_PasswordChanged;

            DeepgramPwdBox.Password   = ConfigManager.Config.DeepgramApiKey;
            ElevenLabsPwdBox.Password = ConfigManager.Config.ElevenLabsApiKey;
            OpenAIPwdBox.Password     = ConfigManager.Config.OpenAIApiKey;
            OpenRouterPwdBox.Password = ConfigManager.Config.OpenRouterApiKey;
            CerebrasPwdBox.Password   = ConfigManager.Config.CerebrasApiKey;

            TraceDebug($"Populate from config lengths: DG={DeepgramPwdBox.Password.Length}, EL={ElevenLabsPwdBox.Password.Length}, OA={OpenAIPwdBox.Password.Length}, OR={OpenRouterPwdBox.Password.Length}, CR={CerebrasPwdBox.Password.Length}");

            DeepgramTxtBox.Text   = ConfigManager.Config.DeepgramApiKey;
            ElevenLabsTxtBox.Text = ConfigManager.Config.ElevenLabsApiKey;
            OpenAITxtBox.Text     = ConfigManager.Config.OpenAIApiKey;
            OpenRouterTxtBox.Text = ConfigManager.Config.OpenRouterApiKey;
            CerebrasTxtBox.Text   = ConfigManager.Config.CerebrasApiKey;

            DeepgramTxtBox.Visibility   = Visibility.Collapsed;
            ElevenLabsTxtBox.Visibility = Visibility.Collapsed;
            OpenAITxtBox.Visibility     = Visibility.Collapsed;
            OpenRouterTxtBox.Visibility = Visibility.Collapsed;
            CerebrasTxtBox.Visibility   = Visibility.Collapsed;

            DeepgramPwdBox.Visibility   = Visibility.Visible;
            ElevenLabsPwdBox.Visibility = Visibility.Visible;
            OpenAIPwdBox.Visibility     = Visibility.Visible;
            OpenRouterPwdBox.Visibility = Visibility.Visible;
            CerebrasPwdBox.Visibility   = Visibility.Visible;

            DeepgramReveal.Content   = "Show";
            ElevenLabsReveal.Content = "Show";
            OpenAIReveal.Content     = "Show";
            OpenRouterReveal.Content = "Show";
            CerebrasReveal.Content   = "Show";

            DeepgramReveal.Icon   = new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.Eye24);
            ElevenLabsReveal.Icon = new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.Eye24);
            OpenAIReveal.Icon     = new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.Eye24);
            OpenRouterReveal.Icon = new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.Eye24);
            CerebrasReveal.Icon   = new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.Eye24);

            DeepgramPwdBox.PasswordChanged   += DeepgramPwdBox_PasswordChanged;
            ElevenLabsPwdBox.PasswordChanged += ElevenLabsPwdBox_PasswordChanged;
            OpenAIPwdBox.PasswordChanged     += OpenAIPwdBox_PasswordChanged;
            OpenRouterPwdBox.PasswordChanged += OpenRouterPwdBox_PasswordChanged;
            CerebrasPwdBox.PasswordChanged   += CerebrasPwdBox_PasswordChanged;

            TraceDebug("PopulatePasswordBoxes complete.");
        }

        private void OnApiTestCompleted(string dg, string el, string oa, string or, string cr)
        {
            Dispatcher.Invoke(() =>
            {
                FlashKeyGlow(DeepgramGlow,   DeepgramGlowFx,   dg.StartsWith("OK"));
                FlashKeyGlow(ElevenLabsGlow, ElevenLabsGlowFx, el.StartsWith("OK"));
                FlashKeyGlow(OpenAIGlow,     OpenAIGlowFx,     oa.StartsWith("OK"));
                FlashKeyGlow(OpenRouterGlow, OpenRouterGlowFx, or.StartsWith("OK"));
                FlashKeyGlow(CerebrasGlow,   CerebrasGlowFx,   cr.StartsWith("OK"));
            });
        }

        private static void FlashKeyGlow(Border glow, DropShadowEffect fx, bool success)
        {
            var color = success ? GreenGlow : RedGlow;
            glow.BorderBrush = new SolidColorBrush(color);
            fx.Color = color;

            var anim = new DoubleAnimationUsingKeyFrames();
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.15)),
                new CubicEase { EasingMode = EasingMode.EaseOut }));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.9))));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.2)),
                new CubicEase { EasingMode = EasingMode.EaseIn }));

            glow.BeginAnimation(OpacityProperty, anim);
        }

        // Keep config updated as user types; persistence is debounced automatically.
        private void DeepgramPwdBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            UpdateKeyFromPasswordBox(
                provider: "Deepgram",
                passwordBox: DeepgramPwdBox,
                revealTextBox: DeepgramTxtBox,
                currentValue: App.ViewModel.DeepgramApiKey,
                assign: value => App.ViewModel.DeepgramApiKey = value);
        }

        private void OpenAIPwdBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            UpdateKeyFromPasswordBox(
                provider: "OpenAI",
                passwordBox: OpenAIPwdBox,
                revealTextBox: OpenAITxtBox,
                currentValue: App.ViewModel.OpenAIApiKey,
                assign: value => App.ViewModel.OpenAIApiKey = value);
        }

        private void ElevenLabsPwdBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            UpdateKeyFromPasswordBox(
                provider: "ElevenLabs",
                passwordBox: ElevenLabsPwdBox,
                revealTextBox: ElevenLabsTxtBox,
                currentValue: App.ViewModel.ElevenLabsApiKey,
                assign: value => App.ViewModel.ElevenLabsApiKey = value);
        }

        private void OpenRouterPwdBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            UpdateKeyFromPasswordBox(
                provider: "OpenRouter",
                passwordBox: OpenRouterPwdBox,
                revealTextBox: OpenRouterTxtBox,
                currentValue: App.ViewModel.OpenRouterApiKey,
                assign: value => App.ViewModel.OpenRouterApiKey = value);
        }

        private void CerebrasPwdBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            UpdateKeyFromPasswordBox(
                provider: "Cerebras",
                passwordBox: CerebrasPwdBox,
                revealTextBox: CerebrasTxtBox,
                currentValue: App.ViewModel.CerebrasApiKey,
                assign: value => App.ViewModel.CerebrasApiKey = value);
        }

        private void DeepgramTxtBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateKeyFromRevealTextBox(
                provider: "Deepgram",
                passwordBox: DeepgramPwdBox,
                revealTextBox: DeepgramTxtBox,
                detach: () => DeepgramPwdBox.PasswordChanged -= DeepgramPwdBox_PasswordChanged,
                attach: () => DeepgramPwdBox.PasswordChanged += DeepgramPwdBox_PasswordChanged,
                assign: value => App.ViewModel.DeepgramApiKey = value);
        }

        private void OpenAITxtBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateKeyFromRevealTextBox(
                provider: "OpenAI",
                passwordBox: OpenAIPwdBox,
                revealTextBox: OpenAITxtBox,
                detach: () => OpenAIPwdBox.PasswordChanged -= OpenAIPwdBox_PasswordChanged,
                attach: () => OpenAIPwdBox.PasswordChanged += OpenAIPwdBox_PasswordChanged,
                assign: value => App.ViewModel.OpenAIApiKey = value);
        }

        private void ElevenLabsTxtBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateKeyFromRevealTextBox(
                provider: "ElevenLabs",
                passwordBox: ElevenLabsPwdBox,
                revealTextBox: ElevenLabsTxtBox,
                detach: () => ElevenLabsPwdBox.PasswordChanged -= ElevenLabsPwdBox_PasswordChanged,
                attach: () => ElevenLabsPwdBox.PasswordChanged += ElevenLabsPwdBox_PasswordChanged,
                assign: value => App.ViewModel.ElevenLabsApiKey = value);
        }

        private void OpenRouterTxtBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateKeyFromRevealTextBox(
                provider: "OpenRouter",
                passwordBox: OpenRouterPwdBox,
                revealTextBox: OpenRouterTxtBox,
                detach: () => OpenRouterPwdBox.PasswordChanged -= OpenRouterPwdBox_PasswordChanged,
                attach: () => OpenRouterPwdBox.PasswordChanged += OpenRouterPwdBox_PasswordChanged,
                assign: value => App.ViewModel.OpenRouterApiKey = value);
        }

        private void CerebrasTxtBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateKeyFromRevealTextBox(
                provider: "Cerebras",
                passwordBox: CerebrasPwdBox,
                revealTextBox: CerebrasTxtBox,
                detach: () => CerebrasPwdBox.PasswordChanged -= CerebrasPwdBox_PasswordChanged,
                attach: () => CerebrasPwdBox.PasswordChanged += CerebrasPwdBox_PasswordChanged,
                assign: value => App.ViewModel.CerebrasApiKey = value);
        }

        private void UpdateKeyFromPasswordBox(
            string provider,
            PasswordBox passwordBox,
            Wpf.Ui.Controls.TextBox revealTextBox,
            string currentValue,
            Action<string> assign)
        {
            if (!this.IsLoaded || !this.IsVisible)
            {
                TraceDebug($"{provider} PasswordBox change ignored due to unloaded/hidden state.");
                return;
            }

            if (revealTextBox.Visibility == Visibility.Visible)
            {
                TraceDebug($"{provider} PasswordBox change ignored because reveal mode is active.");
                return;
            }

            bool looksLikeTeardownClear =
                string.IsNullOrEmpty(passwordBox.Password) &&
                !string.IsNullOrEmpty(currentValue) &&
                revealTextBox.Visibility != Visibility.Visible &&
                !passwordBox.IsKeyboardFocusWithin &&
                !revealTextBox.IsKeyboardFocusWithin;

            if (looksLikeTeardownClear)
            {
                TraceDebug($"{provider} PasswordBox empty update ignored (suspected teardown clear). ExistingLength={currentValue.Length}");
                return;
            }

            assign(passwordBox.Password);
            TraceDebug($"{provider} key updated from PasswordBox. Length={passwordBox.Password.Length}");
        }

        private void UpdateKeyFromRevealTextBox(
            string provider,
            PasswordBox passwordBox,
            Wpf.Ui.Controls.TextBox revealTextBox,
            Action detach,
            Action attach,
            Action<string> assign)
        {
            if (!this.IsLoaded || !this.IsVisible || revealTextBox.Visibility != Visibility.Visible)
            {
                TraceDebug($"{provider} reveal TextBox change ignored due to state/visibility.");
                return;
            }

            detach();
            passwordBox.Password = revealTextBox.Text;
            attach();

            assign(revealTextBox.Text);
            TraceDebug($"{provider} key updated from reveal TextBox. Length={revealTextBox.Text.Length}");
        }

        private void RevealApiKey_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Wpf.Ui.Controls.Button btn) return;
            string tag = btn.Tag?.ToString() ?? "";
            TraceDebug($"Reveal toggle clicked for {tag}.");

            PasswordBox? pwdBox = null;
            System.Windows.Controls.TextBox? txtBox = null;

            switch (tag)
            {
                case "Deepgram":   pwdBox = DeepgramPwdBox;   txtBox = DeepgramTxtBox;   break;
                case "ElevenLabs": pwdBox = ElevenLabsPwdBox; txtBox = ElevenLabsTxtBox; break;
                case "OpenAI":     pwdBox = OpenAIPwdBox;     txtBox = OpenAITxtBox;     break;
                case "OpenRouter": pwdBox = OpenRouterPwdBox; txtBox = OpenRouterTxtBox; break;
                case "Cerebras":   pwdBox = CerebrasPwdBox;   txtBox = CerebrasTxtBox;   break;
            }

            if (pwdBox == null || txtBox == null) return;

            bool isHidden = pwdBox.Visibility == Visibility.Visible;
            if (isHidden)
            {
                txtBox.Text = pwdBox.Password;
                pwdBox.Visibility = Visibility.Collapsed;
                txtBox.Visibility = Visibility.Visible;
                btn.Content = "Hide";
                btn.Icon = new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.EyeOff24);
                TraceDebug($"Reveal mode enabled for {tag}.");
            }
            else
            {
                pwdBox.Password = txtBox.Text;
                txtBox.Visibility = Visibility.Collapsed;
                pwdBox.Visibility = Visibility.Visible;
                btn.Content = "Show";
                btn.Icon = new Wpf.Ui.Controls.SymbolIcon(Wpf.Ui.Controls.SymbolRegular.Eye24);
                TraceDebug($"Reveal mode disabled for {tag}.");
            }
        }

        private static void TraceDebug(string message)
        {
            Logger.Log($"[ApiKeysPage] {message}");
        }
    }
}
