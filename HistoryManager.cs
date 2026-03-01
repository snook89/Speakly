using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Speakly.Services
{
    public class HistoryEntry
    {
        public DateTime Timestamp { get; set; }
        public string OriginalText { get; set; } = string.Empty;
        public string RefinedText { get; set; } = string.Empty;
    }

    public static class HistoryManager
    {
        private static readonly string HistoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "history.json");
        private static List<HistoryEntry> _history = new List<HistoryEntry>();

        public static void AddEntry(string original, string refined)
        {
            _history.Add(new HistoryEntry 
            { 
                Timestamp = DateTime.Now, 
                OriginalText = original, 
                RefinedText = refined 
            });

            // Keep the last 100 entries
            if (_history.Count > 100)
            {
                _history.RemoveAt(0);
            }

            Save();
        }

        public static IReadOnlyList<HistoryEntry> GetHistory()
        {
            if (_history.Count == 0)
            {
                Load();
            }
            return _history.AsReadOnly();
        }

        private static void Load()
        {
            try
            {
                if (File.Exists(HistoryPath))
                {
                    string json = File.ReadAllText(HistoryPath);
                    _history = JsonSerializer.Deserialize<List<HistoryEntry>>(json) ?? new List<HistoryEntry>();
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
                
                // Also append to a plain text log for easy reading as requested
                string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {(_history[^1].RefinedText)}";
                File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "history.log"), logEntry + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save history: {ex.Message}");
            }
        }
    }
}
