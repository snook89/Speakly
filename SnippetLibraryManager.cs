using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Speakly.Config
{
    public class SnippetEntry
    {
        [JsonPropertyName("trigger")]
        public string Trigger { get; set; } = "";

        [JsonPropertyName("replacement")]
        public string Replacement { get; set; } = "";

        public string DisplayLabel => $"{Trigger} -> {Replacement}";

        public override string ToString() => DisplayLabel;
    }

    public static class SnippetLibraryManager
    {
        private static readonly JsonSerializerOptions JsonOptions =
            new JsonSerializerOptions { WriteIndented = true };

        public static List<SnippetEntry> Load()
        {
            var snippetsPath = ResolveSnippetsPath();
            var legacySnippetsPath = ResolveLegacySnippetsPath();

            var appDataSnippets = LoadEntriesFromFile(snippetsPath);
            var legacySnippets = string.Equals(snippetsPath, legacySnippetsPath, StringComparison.OrdinalIgnoreCase)
                ? new List<SnippetEntry>()
                : LoadEntriesFromFile(legacySnippetsPath);

            var merged = MergeEntries(appDataSnippets, legacySnippets);
            SaveInternal(merged);
            return merged;
        }

        public static List<SnippetEntry> AddOrUpdate(List<SnippetEntry> current, string trigger, string replacement)
        {
            var normalizedTrigger = NormalizeValue(trigger);
            var normalizedReplacement = NormalizeValue(replacement);
            if (string.IsNullOrWhiteSpace(normalizedTrigger) || string.IsNullOrWhiteSpace(normalizedReplacement))
            {
                return current;
            }

            var list = current.ToList();
            var existing = list.FirstOrDefault(entry =>
                string.Equals(entry.Trigger, normalizedTrigger, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                existing.Replacement = normalizedReplacement;
            }
            else
            {
                list.Add(new SnippetEntry
                {
                    Trigger = normalizedTrigger,
                    Replacement = normalizedReplacement
                });
            }

            list = list
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Trigger) && !string.IsNullOrWhiteSpace(entry.Replacement))
                .OrderBy(entry => entry.Trigger, StringComparer.OrdinalIgnoreCase)
                .ToList();

            SaveInternal(list);
            return list;
        }

        public static List<SnippetEntry> Delete(List<SnippetEntry> current, string trigger)
        {
            var normalizedTrigger = NormalizeValue(trigger);
            var list = current
                .Where(entry => !string.Equals(entry.Trigger, normalizedTrigger, StringComparison.OrdinalIgnoreCase))
                .ToList();
            SaveInternal(list);
            return list;
        }

        public static string Apply(string text, IEnumerable<SnippetEntry>? snippets, out int replacements)
        {
            replacements = 0;
            if (string.IsNullOrWhiteSpace(text) || snippets == null)
            {
                return text;
            }

            string result = text;
            int replacementCounter = 0;
            foreach (var snippet in snippets
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Trigger) && !string.IsNullOrWhiteSpace(entry.Replacement))
                .OrderByDescending(entry => entry.Trigger.Length))
            {
                string pattern = $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(snippet.Trigger)}(?![\p{{L}}\p{{N}}_])";
                result = Regex.Replace(result, pattern, match =>
                {
                    if (string.Equals(match.Value, snippet.Replacement, StringComparison.Ordinal))
                    {
                        return match.Value;
                    }

                    replacementCounter++;
                    return snippet.Replacement;
                }, RegexOptions.IgnoreCase);
            }

            replacements = replacementCounter;
            return result;
        }

        private static void SaveInternal(List<SnippetEntry> snippets)
        {
            try
            {
                var snippetsPath = ResolveSnippetsPath();
                var directory = Path.GetDirectoryName(snippetsPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = JsonSerializer.Serialize(snippets, JsonOptions);
                File.WriteAllText(snippetsPath, json);
            }
            catch
            {
                // Best effort persistence only.
            }
        }

        private static string ResolveSnippetsPath()
        {
            var overridePath = Environment.GetEnvironmentVariable("SPEAKLY_SNIPPETS_PATH");
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                return overridePath.Trim();
            }

            var appDataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Speakly");
            return Path.Combine(appDataDirectory, "snippets.json");
        }

        private static string ResolveLegacySnippetsPath()
        {
            var overridePath = Environment.GetEnvironmentVariable("SPEAKLY_SNIPPETS_LEGACY_PATH");
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                return overridePath.Trim();
            }

            return Path.Combine(AppContext.BaseDirectory, "snippets.json");
        }

        private static List<SnippetEntry> LoadEntriesFromFile(string path)
        {
            if (!File.Exists(path))
            {
                return new List<SnippetEntry>();
            }

            try
            {
                var json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<List<SnippetEntry>>(json) ?? new List<SnippetEntry>();
                return loaded
                    .Where(entry => !string.IsNullOrWhiteSpace(entry.Trigger) && !string.IsNullOrWhiteSpace(entry.Replacement))
                    .Select(entry => new SnippetEntry
                    {
                        Trigger = NormalizeValue(entry.Trigger),
                        Replacement = NormalizeValue(entry.Replacement)
                    })
                    .Where(entry => !string.IsNullOrWhiteSpace(entry.Trigger) && !string.IsNullOrWhiteSpace(entry.Replacement))
                    .ToList();
            }
            catch
            {
                return new List<SnippetEntry>();
            }
        }

        private static List<SnippetEntry> MergeEntries(List<SnippetEntry> primary, List<SnippetEntry> secondary)
        {
            var result = primary.ToList();
            var seen = new HashSet<string>(result.Select(entry => entry.Trigger), StringComparer.OrdinalIgnoreCase);

            foreach (var entry in secondary)
            {
                if (seen.Contains(entry.Trigger))
                {
                    continue;
                }

                result.Add(new SnippetEntry
                {
                    Trigger = entry.Trigger,
                    Replacement = entry.Replacement
                });
                seen.Add(entry.Trigger);
            }

            return result
                .OrderBy(entry => entry.Trigger, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string NormalizeValue(string? value)
        {
            return (value ?? string.Empty).Trim();
        }
    }
}
