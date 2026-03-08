using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using Speakly.Docs;

namespace Speakly.Pages
{
    public partial class DocsPage : UserControl, INotifyPropertyChanged
    {
        private DocsTopic _selectedTopic;

        public DocsPage()
        {
            InitializeComponent();

            Topics = DocsCatalog.Topics;
            _selectedTopic = DocsCatalog.Topics[0];
            DataContext = this;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public IReadOnlyList<DocsTopic> Topics { get; }

        public DocsTopic SelectedTopic
        {
            get => _selectedTopic;
            set
            {
                if (ReferenceEquals(_selectedTopic, value) || value is null)
                {
                    return;
                }

                _selectedTopic = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelectedTopicTargetPage));
                OnPropertyChanged(nameof(OpenTargetPageButtonText));
                OnPropertyChanged(nameof(SelectedTopicSummaryLine));
                OnPropertyChanged(nameof(SelectedTopicLinks));
                OnPropertyChanged(nameof(HasSelectedTopicLinks));
                TopicScrollViewer?.ScrollToHome();
            }
        }

        public bool HasSelectedTopicTargetPage => SelectedTopic.HasTargetPage;

        public string OpenTargetPageButtonText =>
            SelectedTopic.HasTargetPage
                ? $"Open {SelectedTopic.TargetPageTag} page"
                : "No settings page";

        public string SelectedTopicSummaryLine =>
            SelectedTopic.HasTargetPage
                ? $"Read the docs here, then jump straight into the {SelectedTopic.TargetPageTag} page to apply the recommended setup."
                : "This topic is informational only and does not map to a dedicated settings page.";

        public IReadOnlyList<DocsLink> SelectedTopicLinks => SelectedTopic.Links ?? System.Array.Empty<DocsLink>();

        public bool HasSelectedTopicLinks => SelectedTopicLinks.Count > 0;

        private void OpenTargetPageButton_Click(object sender, RoutedEventArgs e)
        {
            if (!SelectedTopic.HasTargetPage)
            {
                return;
            }

            if (Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.NavigateToSection(SelectedTopic.TargetPageTag!);
            }
        }

        private void OpenDocsLinkButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string url || string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(
                    $"Could not open link.\n\n{url}\n\n{ex.Message}",
                    "Open Link Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
