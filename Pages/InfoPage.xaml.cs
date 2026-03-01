using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace Speakly.Pages
{
    public partial class InfoPage : UserControl
    {
        private const string GitHubUrl = "https://github.com/goxl/Speakly";

        public InfoPage()
        {
            InitializeComponent();
            Loaded += InfoPage_Loaded;
        }

        private void InfoPage_Loaded(object sender, RoutedEventArgs e)
        {
            VersionText.Text = $"Version {App.AppVersion}";
        }

        private void GitHubButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(GitHubUrl) { UseShellExecute = true });
            }
            catch { /* ignore if no browser */ }
        }
    }
}
