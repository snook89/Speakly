using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Speakly.Helpers;

namespace Speakly.Pages
{
    public partial class RefinementPage : UserControl
    {
        private static readonly Color GreenGlow = Color.FromRgb(0x22, 0xC5, 0x5E);
        private static readonly Color RedGlow   = Color.FromRgb(0xEF, 0x44, 0x44);

        public RefinementPage()
        {
            InitializeComponent();
            DataContext = App.ViewModel;
            App.ViewModel.RefreshModelsCompleted += OnRefreshCompleted;
        }

        private void OnRefreshCompleted(bool success)
        {
            Dispatcher.Invoke(() =>
            {
                var color = success ? GreenGlow : RedGlow;
                ModelGlow.BorderBrush = new SolidColorBrush(color);
                ModelGlowFx.Color = color;

                var anim = new DoubleAnimationUsingKeyFrames();
                anim.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.15)),
                    new CubicEase { EasingMode = EasingMode.EaseOut }));
                anim.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.9))));
                anim.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.2)),
                    new CubicEase { EasingMode = EasingMode.EaseIn }));

                ModelGlow.BeginAnimation(OpacityProperty, anim);
            });
        }

        private void ModelCombo_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is ComboBox combo)
            {
                EditableModelComboHelper.HandleTextChanged(combo);
            }
        }

        private void ModelCombo_DropDownClosed(object sender, EventArgs e)
        {
            if (sender is ComboBox combo)
            {
                EditableModelComboHelper.ResetFilter(combo);
            }
        }

        private void ModelCombo_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not ComboBox combo) return;
            if (e.Key != Key.Home || Keyboard.Modifiers != ModifierKeys.Control) return;

            EditableModelComboHelper.ScrollDropDownToTop(combo);
            e.Handled = true;
        }

        private void JumpToTop_Click(object sender, RoutedEventArgs e)
        {
            EditableModelComboHelper.ScrollDropDownToTop(RefinementModelCombo);
        }
    }
}
