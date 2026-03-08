using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Speakly.Services
{
    public class HistoryEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime Timestamp { get; set; }
        public string OriginalText { get; set; } = string.Empty;
        public string RefinedText { get; set; } = string.Empty;
        public string SttProvider { get; set; } = string.Empty;
        public string SttModel { get; set; } = string.Empty;
        public string RefinementProvider { get; set; } = string.Empty;
        public string RefinementModel { get; set; } = string.Empty;
        public int RecordMs { get; set; }
        public int TranscribeMs { get; set; }
        public int RefineMs { get; set; }
        public int InsertMs { get; set; }
        public bool Succeeded { get; set; } = true;
        public string ErrorCode { get; set; } = string.Empty;
        public string InsertionMethod { get; set; } = string.Empty;
        public string ProfileId { get; set; } = string.Empty;
        public string ProfileName { get; set; } = string.Empty;
        public bool FailoverAttempted { get; set; }
        public string FailoverFrom { get; set; } = string.Empty;
        public string FailoverTo { get; set; } = string.Empty;
        public string FinalProviderUsed { get; set; } = string.Empty;
        public bool Pinned { get; set; }
        public string DictationMode { get; set; } = DictationExperienceService.PlainDictationMode;
        public string ContextualRefinementMode { get; set; } = DictationExperienceService.ContextualRefinementModeAggressiveRewrite;
        public string ContextSummary { get; set; } = string.Empty;
        public bool WasVoiceCommand { get; set; }
        public string VoiceCommandName { get; set; } = string.Empty;
        public string ActionSource { get; set; } = "Live";
        public string SourceEntryId { get; set; } = string.Empty;
        public string SourceActionSource { get; set; } = string.Empty;
        public string SourceRefinedText { get; set; } = string.Empty;
        public DateTime? SourceTimestamp { get; set; }

        public string DisplayPrimaryLabel => WasVoiceCommand ? "VOICE COMMAND" : "REFINED TEXT";
        public string DisplayPrimaryText => WasVoiceCommand ? VoiceCommandName : RefinedText;
        public string DisplaySecondaryLabel => WasVoiceCommand ? "SPOKEN PHRASE" : "ORIGINAL TEXT";
        public string DisplaySecondaryText => OriginalText;
        public bool HasRefinedText => !string.IsNullOrWhiteSpace(RefinedText);
        public bool HasContextSummary => !string.IsNullOrWhiteSpace(ContextSummary);
        public bool CanReplayText => !string.IsNullOrWhiteSpace(RefinedText);
        public bool CanReprocess => !string.IsNullOrWhiteSpace(OriginalText);
        public string NormalizedActionSource => NormalizeActionSource(ActionSource);
        public bool IsRecoveryAction => NormalizedActionSource is "HistoryRetry" or "HistoryReprocess";
        public bool HasSourceComparison =>
            IsRecoveryAction &&
            (!string.IsNullOrWhiteSpace(SourceRefinedText) || SourceTimestamp.HasValue || !string.IsNullOrWhiteSpace(SourceEntryId));
        public string DisplayActionLabel => NormalizedActionSource switch
        {
            "HistoryRetry" => "History Retry",
            "HistoryReprocess" => "History Reprocess",
            _ => WasVoiceCommand ? "Voice Command" : "Live"
        };
        public string CompareLabel => NormalizedActionSource switch
        {
            "HistoryRetry" => "PREVIOUS INSERTED TEXT",
            "HistoryReprocess" => "PREVIOUS REFINED TEXT",
            _ => "SOURCE TEXT"
        };
        public string CompareSourceText => string.IsNullOrWhiteSpace(SourceRefinedText) ? "(No prior refined text saved)" : SourceRefinedText;
        public string CompareSummary
        {
            get
            {
                if (!HasSourceComparison)
                {
                    return string.Empty;
                }

                var actionText = NormalizedActionSource == "HistoryRetry"
                    ? "Retried insertion"
                    : "Reprocessed entry";
                var timeText = SourceTimestamp.HasValue ? $" from {SourceTimestamp.Value:HH:mm:ss}" : string.Empty;
                if (string.Equals(SourceRefinedText?.Trim(), RefinedText?.Trim(), StringComparison.Ordinal))
                {
                    return $"{actionText}{timeText}. Final text is unchanged.";
                }

                return $"{actionText}{timeText}. Final text changed.";
            }
        }

        public static string NormalizeActionSource(string? actionSource)
        {
            var normalized = actionSource?.Trim() ?? string.Empty;
            if (string.Equals(normalized, "HistoryRetry", StringComparison.OrdinalIgnoreCase))
            {
                return "HistoryRetry";
            }

            if (string.Equals(normalized, "HistoryReprocess", StringComparison.OrdinalIgnoreCase))
            {
                return "HistoryReprocess";
            }

            return "Live";
        }
    }

    public static class HistoryManager
    {
        private static readonly string HistoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "history.json");
        private static readonly object SyncLock = new();
        private static List<HistoryEntry> _history = new List<HistoryEntry>();
        private static bool _isLoaded;

        public static void AddEntry(HistoryEntry entry)
        {
            if (Config.ConfigManager.Config.PrivacyMode == "no_history" || entry == null)
            {
                return;
            }

            lock (SyncLock)
            {
                EnsureLoaded();

                if (string.IsNullOrWhiteSpace(entry.Id))
                {
                    entry.Id = Guid.NewGuid().ToString("N");
                }

                if (entry.Timestamp == default)
                {
                    entry.Timestamp = DateTime.Now;
                }

                entry.DictationMode = DictationExperienceService.NormalizeMode(entry.DictationMode);
                entry.ContextualRefinementMode = DictationExperienceService.NormalizeContextualRefinementMode(entry.ContextualRefinementMode);
                entry.ActionSource = HistoryEntry.NormalizeActionSource(entry.ActionSource);

                _history.Add(entry);

                if (_history.Count > 100)
                {
                    _history.RemoveAt(0);
                }

                PurgeByRetention();
                Save();
            }
        }

        public static void AddEntry(
            string original,
            string refined,
            string sttProvider = "",
            string sttModel = "",
            string refinementProvider = "",
            string refinementModel = "",
            int recordMs = 0,
            int transcribeMs = 0,
            int refineMs = 0,
            int insertMs = 0,
            bool succeeded = true,
            string errorCode = "",
            string insertionMethod = "",
            string profileId = "",
            string profileName = "",
            bool failoverAttempted = false,
            string failoverFrom = "",
            string failoverTo = "",
            string finalProviderUsed = "",
            string dictationMode = "",
            string contextualRefinementMode = "",
            string contextSummary = "",
            bool wasVoiceCommand = false,
            string voiceCommandName = "",
            string actionSource = "Live",
            string sourceEntryId = "",
            string sourceActionSource = "",
            string sourceRefinedText = "",
            DateTime? sourceTimestamp = null)
        {
            AddEntry(new HistoryEntry
            {
                Timestamp = DateTime.Now, 
                OriginalText = original, 
                RefinedText = refined,
                SttProvider = sttProvider,
                SttModel = sttModel,
                RefinementProvider = refinementProvider,
                RefinementModel = refinementModel,
                RecordMs = recordMs,
                TranscribeMs = transcribeMs,
                RefineMs = refineMs,
                InsertMs = insertMs,
                Succeeded = succeeded,
                ErrorCode = errorCode,
                InsertionMethod = insertionMethod,
                ProfileId = profileId,
                ProfileName = profileName,
                FailoverAttempted = failoverAttempted,
                FailoverFrom = failoverFrom,
                FailoverTo = failoverTo,
                FinalProviderUsed = finalProviderUsed,
                DictationMode = dictationMode,
                ContextualRefinementMode = contextualRefinementMode,
                ContextSummary = contextSummary,
                WasVoiceCommand = wasVoiceCommand,
                VoiceCommandName = voiceCommandName,
                ActionSource = actionSource,
                SourceEntryId = sourceEntryId,
                SourceActionSource = sourceActionSource,
                SourceRefinedText = sourceRefinedText,
                SourceTimestamp = sourceTimestamp
            });
        }

        public static IReadOnlyList<HistoryEntry> GetHistory()
        {
            lock (SyncLock)
            {
                EnsureLoaded();
                return _history.AsReadOnly();
            }
        }

        private static void EnsureLoaded()
        {
            if (_isLoaded)
            {
                return;
            }

            Load();
            _isLoaded = true;
        }

        private static void Load()
        {
            _history = new List<HistoryEntry>();
            try
            {
                if (File.Exists(HistoryPath))
                {
                    string json = File.ReadAllText(HistoryPath);
                    _history = JsonSerializer.Deserialize<List<HistoryEntry>>(json) ?? new List<HistoryEntry>();
                    foreach (var entry in _history)
                    {
                        if (string.IsNullOrWhiteSpace(entry.Id))
                        {
                            entry.Id = Guid.NewGuid().ToString("N");
                        }

                        entry.DictationMode = DictationExperienceService.NormalizeMode(entry.DictationMode);
                        entry.ContextualRefinementMode = DictationExperienceService.NormalizeContextualRefinementMode(entry.ContextualRefinementMode);
                        entry.ActionSource = HistoryEntry.NormalizeActionSource(entry.ActionSource);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load history: {ex.Message}");
                _history = new List<HistoryEntry>();
            }
        }

        private static void Save()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(_history, options);
                File.WriteAllText(HistoryPath, json);

                if (_history.Count > 0)
                {
                    var latest = _history[^1];
                    string logPayload = latest.WasVoiceCommand
                        ? $"[Command] {latest.VoiceCommandName}"
                        : latest.RefinedText;
                    string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {logPayload}";
                    File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "history.log"), logEntry + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save history: {ex.Message}");
            }
        }

        private static void PurgeByRetention()
        {
            int days = Math.Clamp(Config.ConfigManager.Config.HistoryRetentionDays, 1, 3650);
            var cutoff = DateTime.Now.AddDays(-days);
            _history = _history.Where(h => h.Timestamp >= cutoff || h.Pinned).ToList();
        }

        public static bool SetPinned(string id, bool pinned)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            lock (SyncLock)
            {
                EnsureLoaded();

                var entry = _history.FirstOrDefault(h => string.Equals(h.Id, id, StringComparison.OrdinalIgnoreCase));
                if (entry == null)
                {
                    return false;
                }

                entry.Pinned = pinned;
                Save();
                return true;
            }
        }
    }
}
