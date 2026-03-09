using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Speakly.Services;

namespace Speakly
{
    public partial class UpdateReadyDialog : Window
    {
        private readonly string? _releaseUrl;
        private MediaPlayer? _updateSoundPlayer;
        private string? _updateSoundTempPath;

        internal UpdateReadyDialog(string version, UpdateReleaseNotesContent notes, string? releaseUrl)
        {
            InitializeComponent();

            _releaseUrl = string.IsNullOrWhiteSpace(releaseUrl) ? null : releaseUrl.Trim();
            VersionTitleText.Text = $"Version {version} is ready";
            SummaryText.Text = notes.Summary;
            HighlightsList.ItemsSource = notes.Highlights ?? Array.Empty<string>();
            ReleaseLinkHintText.Text = string.IsNullOrWhiteSpace(_releaseUrl)
                ? "Release details are unavailable for this update package."
                : "If you want the full changelog, open the GitHub release page before restarting.";
            ViewReleaseButton.Visibility = string.IsNullOrWhiteSpace(_releaseUrl)
                ? Visibility.Collapsed
                : Visibility.Visible;

            Loaded += UpdateReadyDialog_Loaded;
            Closed += UpdateReadyDialog_Closed;
        }

        private void UpdateReadyDialog_Loaded(object sender, RoutedEventArgs e)
        {
            TryPlayUpdateSound();
        }

        private void UpdateReadyDialog_Closed(object? sender, EventArgs e)
        {
            try
            {
                _updateSoundPlayer?.Stop();
                _updateSoundPlayer?.Close();
            }
            catch
            {
            }

            _updateSoundPlayer = null;
            TryDeleteTempSound();
        }

        private void UpdateNowButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void LaterButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ViewReleaseButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_releaseUrl))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(_releaseUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Could not open the release page: {ex.Message}",
                    "Speakly",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void TryPlayUpdateSound()
        {
            try
            {
                var resourceUri = new Uri("pack://application:,,,/Resources/update.mp3");
                var resource = Application.GetResourceStream(resourceUri);
                if (resource?.Stream == null)
                {
                    return;
                }

                _updateSoundTempPath = Path.Combine(Path.GetTempPath(), $"speakly_update_{Guid.NewGuid():N}.mp3");
                using (var output = File.Create(_updateSoundTempPath))
                {
                    resource.Stream.CopyTo(output);
                }

                _updateSoundPlayer = new MediaPlayer();
                _updateSoundPlayer.Volume = 0.78;
                _updateSoundPlayer.MediaEnded += (_, _) =>
                {
                    try
                    {
                        _updateSoundPlayer?.Close();
                    }
                    catch
                    {
                    }

                    try
                    {
                        TryDeleteTempSound();
                    }
                    catch
                    {
                    }
                };
                _updateSoundPlayer.Open(new Uri(_updateSoundTempPath));
                _updateSoundPlayer.Play();
            }
            catch
            {
                // Ignore update sound failures; the dialog is still usable without audio.
            }
        }

        private void TryDeleteTempSound()
        {
            if (string.IsNullOrWhiteSpace(_updateSoundTempPath))
            {
                return;
            }

            try
            {
                if (File.Exists(_updateSoundTempPath))
                {
                    File.Delete(_updateSoundTempPath);
                }
            }
            catch
            {
            }

            _updateSoundTempPath = null;
        }
    }
}
