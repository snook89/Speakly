using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Speakly.Services
{
    public class SessionMetricEntry
    {
        public DateTime Timestamp { get; set; }
        public string SttProvider { get; set; } = string.Empty;
        public string SttModel { get; set; } = string.Empty;
        public string RefinementProvider { get; set; } = string.Empty;
        public string RefinementModel { get; set; } = string.Empty;
        public int RecordMs { get; set; }
        public int TranscribeMs { get; set; }
        public int RefineMs { get; set; }
        public int InsertMs { get; set; }
        public bool Succeeded { get; set; }
        public string ErrorCode { get; set; } = string.Empty;
        public string ProfileId { get; set; } = string.Empty;
        public string ProfileName { get; set; } = string.Empty;
        public bool FailoverAttempted { get; set; }
        public string FailoverFrom { get; set; } = string.Empty;
        public string FailoverTo { get; set; } = string.Empty;
        public string FinalProviderUsed { get; set; } = string.Empty;
    }

    public class ProviderStats
    {
        public string Provider { get; set; } = string.Empty;
        public int Sessions { get; set; }
        public int Successes { get; set; }
        public double SuccessRate { get; set; }
        public int AvgLatencyMs { get; set; }
    }

    public class StatisticsSummary
    {
        public int TotalSessions { get; set; }
        public int SuccessfulSessions { get; set; }
        public double SuccessRate { get; set; }
        public int AverageTotalLatencyMs { get; set; }
        public int AverageRecordMs { get; set; }
        public int AverageTranscribeMs { get; set; }
        public int AverageRefineMs { get; set; }
        public int AverageInsertMs { get; set; }
        public int FailoverSessions { get; set; }
        public double FailoverRate { get; set; }
        public Dictionary<string, int> ErrorCounts { get; set; } = new();
        public List<ProviderStats> ByProvider { get; set; } = new();
    }

    public static class StatisticsManager
    {
        private static readonly string MetricsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "metrics.json");
        private static readonly object LockObj = new();
        private static List<SessionMetricEntry> _metrics = new();

        public static void RecordSession(SessionMetricEntry metric)
        {
            lock (LockObj)
            {
                EnsureLoaded();
                _metrics.Add(metric);
                if (_metrics.Count > 5000)
                {
                    _metrics = _metrics.Skip(_metrics.Count - 5000).ToList();
                }
                Save();
            }
        }

        public static StatisticsSummary GetSummary(int daysBack)
        {
            lock (LockObj)
            {
                EnsureLoaded();
                var from = DateTime.Now.Date.AddDays(-(daysBack - 1));
                var entries = _metrics.Where(x => x.Timestamp >= from).ToList();
                if (entries.Count == 0) return new StatisticsSummary();

                int total = entries.Count;
                int success = entries.Count(x => x.Succeeded);
                int avgTotal = (int)entries.Average(x => x.RecordMs + x.TranscribeMs + x.RefineMs + x.InsertMs);

                var byProvider = entries
                    .GroupBy(x => string.IsNullOrWhiteSpace(x.SttProvider) ? "Unknown" : x.SttProvider)
                    .Select(g =>
                    {
                        var list = g.ToList();
                        int sess = list.Count;
                        int succ = list.Count(x => x.Succeeded);
                        return new ProviderStats
                        {
                            Provider = g.Key,
                            Sessions = sess,
                            Successes = succ,
                            SuccessRate = sess == 0 ? 0 : Math.Round((double)succ * 100.0 / sess, 1),
                            AvgLatencyMs = (int)list.Average(x => x.RecordMs + x.TranscribeMs + x.RefineMs + x.InsertMs)
                        };
                    })
                    .OrderByDescending(x => x.Sessions)
                    .ToList();

                return new StatisticsSummary
                {
                    TotalSessions = total,
                    SuccessfulSessions = success,
                    SuccessRate = Math.Round((double)success * 100.0 / total, 1),
                    AverageTotalLatencyMs = avgTotal,
                    AverageRecordMs = (int)entries.Average(x => x.RecordMs),
                    AverageTranscribeMs = (int)entries.Average(x => x.TranscribeMs),
                    AverageRefineMs = (int)entries.Average(x => x.RefineMs),
                    AverageInsertMs = (int)entries.Average(x => x.InsertMs),
                    FailoverSessions = entries.Count(x => x.FailoverAttempted),
                    FailoverRate = Math.Round(entries.Count(x => x.FailoverAttempted) * 100.0 / total, 1),
                    ErrorCounts = entries
                        .Where(x => !string.IsNullOrWhiteSpace(x.ErrorCode))
                        .GroupBy(x => x.ErrorCode)
                        .OrderByDescending(g => g.Count())
                        .ToDictionary(g => g.Key, g => g.Count()),
                    ByProvider = byProvider
                };
            }
        }

        private static void EnsureLoaded()
        {
            if (_metrics.Count > 0) return;
            try
            {
                if (File.Exists(MetricsPath))
                {
                    var json = File.ReadAllText(MetricsPath);
                    _metrics = JsonSerializer.Deserialize<List<SessionMetricEntry>>(json) ?? new List<SessionMetricEntry>();
                }
            }
            catch
            {
                _metrics = new List<SessionMetricEntry>();
            }
        }

        private static void Save()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(_metrics, options);
                File.WriteAllText(MetricsPath, json);
            }
            catch
            {
                // no-op
            }
        }
    }
}
