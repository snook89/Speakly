using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using Speakly.Services;

namespace Speakly.Pages
{
    public partial class StatisticsPage : UserControl
    {
        private sealed class ProviderRow
        {
            public string Provider { get; set; } = string.Empty;
            public int Sessions { get; set; }
            public int Successes { get; set; }
            public string SuccessRateText { get; set; } = "0%";
            public string AvgLatencyText { get; set; } = "0 ms";
        }

        public StatisticsPage()
        {
            InitializeComponent();
            DataContext = App.ViewModel;
            Loaded += (_, _) =>
            {
                RangeCombo.SelectedIndex = 1; // Last 7 days
                RefreshSummary();
            };
        }

        private void RangeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RefreshSummary();
        }

        private void RefreshSummary()
        {
            if (RangeCombo.SelectedItem is not ComboBoxItem selected || selected.Tag == null) return;
            int days = int.TryParse(selected.Tag.ToString(), out var parsed) ? parsed : 7;

            var summary = StatisticsManager.GetSummary(days);
            SummaryText.Text =
                $"Sessions: {summary.TotalSessions} | Successful: {summary.SuccessfulSessions} | Success rate: {summary.SuccessRate:0.0}% | Avg total latency: {summary.AverageTotalLatencyMs} ms";

            LatencyText.Text =
                $"Record: {summary.AverageRecordMs} ms | Transcribe: {summary.AverageTranscribeMs} ms | Refine: {summary.AverageRefineMs} ms | Insert: {summary.AverageInsertMs} ms";
            FailoverText.Text = $"Failover sessions: {summary.FailoverSessions} | Failover rate: {summary.FailoverRate:0.0}%";

            ErrorText.Text = summary.ErrorCounts.Count == 0
                ? "No errors in selected range."
                : string.Join(" | ", summary.ErrorCounts.Select(kv => $"{kv.Key}: {kv.Value}"));

            var rows = summary.ByProvider
                .Select(x => new ProviderRow
                {
                    Provider = x.Provider,
                    Sessions = x.Sessions,
                    Successes = x.Successes,
                    SuccessRateText = $"{x.SuccessRate:0.0}%",
                    AvgLatencyText = $"{x.AvgLatencyMs} ms"
                })
                .Cast<object>()
                .ToList();

            ProviderList.ItemsSource = rows;
        }
    }
}
