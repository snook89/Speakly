using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Speakly.Config;
using Xunit;

namespace Speakly.Tests.Unit
{
    public class SnippetLibraryManagerTests
    {
        [Fact]
        public void Load_MigratesLegacySnippets_ToAppDataFile()
        {
            var root = Path.Combine(Path.GetTempPath(), "speakly-snippets-test", Guid.NewGuid().ToString("N"));
            var appPath = Path.Combine(root, "appdata", "snippets.json");
            var legacyPath = Path.Combine(root, "legacy", "snippets.json");

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
                var legacyEntries = new List<SnippetEntry>
                {
                    new SnippetEntry { Trigger = "brb", Replacement = "be right back" }
                };
                File.WriteAllText(legacyPath, JsonSerializer.Serialize(legacyEntries));

                using var appScope = new EnvVarScope("SPEAKLY_SNIPPETS_PATH", appPath);
                using var legacyScope = new EnvVarScope("SPEAKLY_SNIPPETS_LEGACY_PATH", legacyPath);

                var loaded = SnippetLibraryManager.Load();

                Assert.Contains(loaded, entry => string.Equals(entry.Trigger, "brb", StringComparison.OrdinalIgnoreCase));
                Assert.True(File.Exists(appPath));
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
        public void Apply_ReplacesKnownTriggersIgnoringCase()
        {
            var result = SnippetLibraryManager.Apply(
                "Please send best regards to the team.",
                new[]
                {
                    new SnippetEntry { Trigger = "best regards", Replacement = "Best regards," }
                },
                out var replacements);

            Assert.Equal("Please send Best regards, to the team.", result);
            Assert.Equal(1, replacements);
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
