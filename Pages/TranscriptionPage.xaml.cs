using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace Speakly.Pages
{
    public partial class TranscriptionPage : UserControl
    {
        private static readonly Color GreenGlow = Color.FromRgb(0x22, 0xC5, 0x5E);
        private static readonly Color RedGlow   = Color.FromRgb(0xEF, 0x44, 0x44);

        public TranscriptionPage()
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
    }
}
