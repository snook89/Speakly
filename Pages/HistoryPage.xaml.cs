using System.Windows.Controls;
using System.Windows.Data;
using System.ComponentModel;
using Speakly.Services;

namespace Speakly.Pages
{
    public partial class HistoryPage : UserControl
    {
        private ICollectionView? _historyView;

        public HistoryPage()
        {
            InitializeComponent();
            DataContext = App.ViewModel;
            Loaded += (_, _) =>
            {
                _historyView = CollectionViewSource.GetDefaultView(App.ViewModel.HistoryEntries);
                if (_historyView != null)
                {
                    _historyView.Filter = FilterHistory;
                }

                StatusFilterBox.SelectedIndex = 0;
                ProviderFilterBox.SelectedIndex = 0;
            };
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _historyView?.Refresh();
        }

        private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _historyView?.Refresh();
        }

        private bool FilterHistory(object item)
        {
            if (item is not HistoryEntry entry) return false;

            var search = SearchBox.Text?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(search))
            {
                bool match =
                    entry.OriginalText.Contains(search, System.StringComparison.OrdinalIgnoreCase) ||
                    entry.RefinedText.Contains(search, System.StringComparison.OrdinalIgnoreCase) ||
                    entry.SttProvider.Contains(search, System.StringComparison.OrdinalIgnoreCase);
                if (!match) return false;
            }

            var statusTag = (StatusFilterBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all";
            if (statusTag == "ok" && !entry.Succeeded) return false;
            if (statusTag == "fail" && entry.Succeeded) return false;

            var providerTag = (ProviderFilterBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all";
            if (!string.Equals(providerTag, "all", System.StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(providerTag, entry.SttProvider, System.StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }
    }
}
