using System.Collections.Generic;
using System.ComponentModel;
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

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
