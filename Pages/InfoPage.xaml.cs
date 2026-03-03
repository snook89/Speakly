using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace Speakly.Pages
{
    public partial class InfoPage : UserControl
    {
        private const string GitHubUrl = "https://github.com/snook89/Speakly";
        private const string ReleasesUrl = "https://github.com/snook89/Speakly/releases";

        public InfoPage()
        {
            InitializeComponent();
            Loaded += InfoPage_Loaded;
        }

        private void InfoPage_Loaded(object sender, RoutedEventArgs e)
        {
            VersionText.Text = $"v{App.GetDisplayVersion()}";
            BuildText.Text = $".NET 9 · WPF UI 4.2 · Windows 10/11 · Build {App.AppVersion}";
        }

        private void GitHubButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(GitHubUrl) { UseShellExecute = true });
            }
            catch { /* ignore if no browser */ }
        }

        private void ReleasesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(ReleasesUrl) { UseShellExecute = true });
            }
            catch { /* ignore if no browser */ }
        }

        private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
        {
            CheckUpdatesButton.IsEnabled = false;
            UpdateStatusText.Text = "Checking for updates...";

            try
            {
                string status = await App.CheckForUpdatesNowAsync();
                UpdateStatusText.Text = status;
                VersionText.Text = $"v{App.GetDisplayVersion()}";
            }
            catch
            {
                UpdateStatusText.Text = "Update check failed.";
            }
            finally
            {
                CheckUpdatesButton.IsEnabled = true;
            }
        }
    }
}
