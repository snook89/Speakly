using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Speakly.Config;

namespace Speakly.Tests.Unit
{
    public class PromptLibraryManagerTests
    {
        [Fact]
        public void Load_MigratesLegacyUserPrompts_ToAppDataPromptsFile()
        {
            var root = Path.Combine(Path.GetTempPath(), "speakly-prompts-test", Guid.NewGuid().ToString("N"));
            var appPath = Path.Combine(root, "appdata", "prompts.json");
            var legacyPath = Path.Combine(root, "legacy", "prompts.json");

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
                var legacyPrompts = new List<PromptEntry>
                {
                    new PromptEntry { Name = "General", Text = "builtin", IsBuiltIn = true },
                    new PromptEntry { Name = "My Custom Prompt", Text = "Keep this prompt", IsBuiltIn = false }
                };
                File.WriteAllText(legacyPath, JsonSerializer.Serialize(legacyPrompts));

                using var appScope = new EnvVarScope("SPEAKLY_PROMPTS_PATH", appPath);
                using var legacyScope = new EnvVarScope("SPEAKLY_PROMPTS_LEGACY_PATH", legacyPath);

                var loaded = PromptLibraryManager.Load();

                Assert.Contains(loaded, p =>
                    string.Equals(p.Name, "My Custom Prompt", StringComparison.OrdinalIgnoreCase) &&
                    !p.IsBuiltIn);
                Assert.True(File.Exists(appPath));

                var persisted = JsonSerializer.Deserialize<List<PromptEntry>>(File.ReadAllText(appPath)) ?? new List<PromptEntry>();
                Assert.Contains(persisted, p =>
                    string.Equals(p.Name, "My Custom Prompt", StringComparison.OrdinalIgnoreCase) &&
                    !p.IsBuiltIn);
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        [Fact]
        public void Load_PrefersAppDataPrompt_WhenNameExistsInBothSources()
        {
            var root = Path.Combine(Path.GetTempPath(), "speakly-prompts-test", Guid.NewGuid().ToString("N"));
            var appPath = Path.Combine(root, "appdata", "prompts.json");
            var legacyPath = Path.Combine(root, "legacy", "prompts.json");

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(appPath)!);
                Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);

                var appPrompts = new List<PromptEntry>
                {
                    new PromptEntry { Name = "Shared Prompt", Text = "AppData text", IsBuiltIn = false }
                };
                var legacyPrompts = new List<PromptEntry>
                {
                    new PromptEntry { Name = "Shared Prompt", Text = "Legacy text", IsBuiltIn = false },
                    new PromptEntry { Name = "Legacy Only", Text = "Legacy entry", IsBuiltIn = false }
                };

                File.WriteAllText(appPath, JsonSerializer.Serialize(appPrompts));
                File.WriteAllText(legacyPath, JsonSerializer.Serialize(legacyPrompts));

                using var appScope = new EnvVarScope("SPEAKLY_PROMPTS_PATH", appPath);
                using var legacyScope = new EnvVarScope("SPEAKLY_PROMPTS_LEGACY_PATH", legacyPath);

                var loaded = PromptLibraryManager.Load();
                var shared = loaded.FirstOrDefault(p => string.Equals(p.Name, "Shared Prompt", StringComparison.OrdinalIgnoreCase));

                Assert.NotNull(shared);
                Assert.Equal("AppData text", shared!.Text);
                Assert.Contains(loaded, p => string.Equals(p.Name, "Legacy Only", StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        private sealed class EnvVarScope : IDisposable
        {
            private readonly string _key;
            private readonly string? _previousValue;

            public EnvVarScope(string key, string value)
            {
                _key = key;
                _previousValue = Environment.GetEnvironmentVariable(key);
                Environment.SetEnvironmentVariable(key, value);
            }

            public void Dispose()
            {
                Environment.SetEnvironmentVariable(_key, _previousValue);
            }
        }
    }
}
