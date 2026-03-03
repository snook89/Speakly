using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Speakly.Config
{
    /// <summary>A single named refinement prompt entry stored in prompts.json.</summary>
    public class PromptEntry
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("text")]
        public string Text { get; set; } = "";

        /// <summary>Built-in prompts are always present and cannot be deleted.</summary>
        [JsonPropertyName("is_built_in")]
        public bool IsBuiltIn { get; set; } = false;

        public override string ToString() => Name;
    }

    /// <summary>
    /// Manages the persistent prompt library stored in %AppData%\Speakly\prompts.json.
    /// Built-in prompts (General / Ukrainian) are seeded on first load and are always
    /// shown at the top of the list; user prompts follow.
    /// </summary>
    public static class PromptLibraryManager
    {
        private static readonly JsonSerializerOptions _jsonOptions =
            new JsonSerializerOptions { WriteIndented = true };

        // ── Built-in seed data ────────────────────────────────────────────────

        private static readonly PromptEntry[] BuiltInPrompts =
        {
            new PromptEntry
            {
                Name      = "General",
                IsBuiltIn = true,
                Text      = AppConfig.DefaultRefinementPrompt
            },
            new PromptEntry
            {
                Name      = "Ukrainian",
                IsBuiltIn = true,
                Text      =
                    "Role and Objective:\n" +
                    "- Refine transcribed speech-to-text outputs in Ukrainian for clarity, accuracy, and formatting compliance.\n\n" +
                    "Instructions:\n" +
                    "- Preserve the original meaning and intent of the message.\n" +
                    "- Ensure the final text is in Ukrainian and uses natural, correct Ukrainian grammar and punctuation.\n" +
                    "- If a user-provided format instruction appears at the end of the transcribed text, apply the format to the output but do not include the instruction itself in the final refined text.\n" +
                    "- Do not introduce content that is not implied in the original input.\n" +
                    "- Never answer as a chatbot, never ask follow-up questions, and never provide explanations.\n" +
                    "- If input is mixed, noisy, or unclear, return the original transcript unchanged.\n\n" +
                    "Output Format:\n" +
                    "- Output only the refined transcribed text as a single string."
            }
        };

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Loads all prompts.  Built-ins are always prepended/merged regardless of
        /// what is on disk.  Creates the file from defaults when it does not exist.
        /// </summary>
        public static List<PromptEntry> Load()
        {
            var promptsPath = ResolvePromptsPath();
            var legacyPromptsPath = ResolveLegacyPromptsPath();

            var appDataUserPrompts = LoadUserPromptsFromFile(promptsPath);
            var legacyUserPrompts = string.Equals(promptsPath, legacyPromptsPath, StringComparison.OrdinalIgnoreCase)
                ? new List<PromptEntry>()
                : LoadUserPromptsFromFile(legacyPromptsPath);

            // Prefer prompts from AppData. Pull in legacy prompts only when names don't already exist.
            var userPrompts = MergeUserPrompts(appDataUserPrompts, legacyUserPrompts);

            var result = BuiltInPrompts.ToList<PromptEntry>();
            result.AddRange(userPrompts);

            // Persist to ensure file location is normalized to AppData and built-ins stay current.
            SaveInternal(result);
            return result;
        }

        /// <summary>Adds a new user prompt or updates an existing one with the same name.</summary>
        public static List<PromptEntry> AddOrUpdate(List<PromptEntry> current, string name, string text)
        {
            if (string.IsNullOrWhiteSpace(name)) return current;

            // Prevent overwriting built-in names
            bool isBuiltInName = BuiltInPrompts.Any(b =>
                string.Equals(b.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

            if (isBuiltInName)
            {
                // Silently refuse to overwrite a built-in
                return current;
            }

            var list   = current.ToList();
            var existing = list.FirstOrDefault(p =>
                string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                existing.Text = text;
            }
            else
            {
                list.Add(new PromptEntry { Name = name.Trim(), Text = text, IsBuiltIn = false });
            }

            SaveInternal(list);
            return list;
        }

        /// <summary>Deletes a user-defined prompt by name. Built-in prompts are ignored.</summary>
        public static List<PromptEntry> Delete(List<PromptEntry> current, string name)
        {
            var list = current
                .Where(p => p.IsBuiltIn ||
                            !string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                .ToList();
            SaveInternal(list);
            return list;
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private static void SaveInternal(List<PromptEntry> prompts)
        {
            try
            {
                var promptsPath = ResolvePromptsPath();
                var directory = Path.GetDirectoryName(promptsPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = JsonSerializer.Serialize(prompts, _jsonOptions);
                File.WriteAllText(promptsPath, json);
            }
            catch { /* best-effort */ }
        }

        private static string ResolvePromptsPath()
        {
            var overridePath = Environment.GetEnvironmentVariable("SPEAKLY_PROMPTS_PATH");
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                return overridePath.Trim();
            }

            var appDataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Speakly");
            return Path.Combine(appDataDirectory, "prompts.json");
        }

        private static string ResolveLegacyPromptsPath()
        {
            var overridePath = Environment.GetEnvironmentVariable("SPEAKLY_PROMPTS_LEGACY_PATH");
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                return overridePath.Trim();
            }

            return Path.Combine(AppContext.BaseDirectory, "prompts.json");
        }

        private static List<PromptEntry> LoadUserPromptsFromFile(string path)
        {
            if (!File.Exists(path))
            {
                return new List<PromptEntry>();
            }

            try
            {
                var json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<List<PromptEntry>>(json) ?? new List<PromptEntry>();
                return loaded
                    .Where(p => !p.IsBuiltIn && !string.IsNullOrWhiteSpace(p.Name))
                    .ToList();
            }
            catch
            {
                // Corrupt file: ignore and continue with defaults/other sources.
                return new List<PromptEntry>();
            }
        }

        private static List<PromptEntry> MergeUserPrompts(
            List<PromptEntry> primary,
            List<PromptEntry> secondary)
        {
            var result = primary.ToList();
            var seen = new HashSet<string>(result.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);

            foreach (var entry in secondary)
            {
                if (seen.Contains(entry.Name))
                {
                    continue;
                }

                result.Add(new PromptEntry
                {
                    Name = entry.Name,
                    Text = entry.Text,
                    IsBuiltIn = false
                });
                seen.Add(entry.Name);
            }

            return result;
        }
    }
}
