using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Speakly.Config;
using Speakly.Services;
using Xunit;

namespace Speakly.Tests.Unit
{
    public class HistoryManagerTests : IDisposable
    {
        private readonly string _historyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "history.json");
        private readonly string _historyLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "history.log");
        private readonly string? _historyBackup;
        private readonly string? _historyLogBackup;
        private readonly string _originalPrivacyMode;
        private readonly int _originalRetentionDays;

        public HistoryManagerTests()
        {
            _historyBackup = File.Exists(_historyPath) ? File.ReadAllText(_historyPath) : null;
            _historyLogBackup = File.Exists(_historyLogPath) ? File.ReadAllText(_historyLogPath) : null;
            _originalPrivacyMode = ConfigManager.Config.PrivacyMode;
            _originalRetentionDays = ConfigManager.Config.HistoryRetentionDays;

            ConfigManager.Config.PrivacyMode = "normal";
            ConfigManager.Config.HistoryRetentionDays = 30;
            ResetHistoryManagerState();
        }

        [Fact]
        public void AddEntry_LoadsExistingHistoryBeforeFirstSave()
        {
            var existing = new List<HistoryEntry>
            {
                new HistoryEntry
                {
                    Id = "existing-entry",
                    Timestamp = DateTime.Now.AddMinutes(-5),
                    OriginalText = "Existing original",
                    RefinedText = "Existing refined"
                }
            };
            File.WriteAllText(_historyPath, JsonSerializer.Serialize(existing));
            ResetHistoryManagerState();

            HistoryManager.AddEntry(new HistoryEntry
            {
                Id = "new-entry",
                Timestamp = DateTime.Now,
                OriginalText = "New original",
                RefinedText = "New refined"
            });

            var persisted = JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(_historyPath));
            Assert.NotNull(persisted);
            Assert.Contains(persisted!, entry => entry.Id == "existing-entry");
            Assert.Contains(persisted!, entry => entry.Id == "new-entry");
        }

        [Fact]
        public void SetPinned_LoadsHistoryWithoutRequiringGetHistoryFirst()
        {
            var existing = new List<HistoryEntry>
            {
                new HistoryEntry
                {
                    Id = "pin-me",
                    Timestamp = DateTime.Now,
                    OriginalText = "Original",
                    RefinedText = "Refined"
                }
            };
            File.WriteAllText(_historyPath, JsonSerializer.Serialize(existing));
            ResetHistoryManagerState();

            var result = HistoryManager.SetPinned("pin-me", true);

            Assert.True(result);
            var persisted = JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(_historyPath));
            Assert.NotNull(persisted);
            Assert.True(persisted![0].Pinned);
        }

        [Fact]
        public void AddEntry_RetentionPrunesOldEntriesButKeepsPinnedOnes()
        {
            ConfigManager.Config.HistoryRetentionDays = 7;
            var existing = new List<HistoryEntry>
            {
                new HistoryEntry
                {
                    Id = "old-unpinned",
                    Timestamp = DateTime.Now.AddDays(-30),
                    OriginalText = "Old original",
                    RefinedText = "Old refined",
                    Pinned = false
                },
                new HistoryEntry
                {
                    Id = "old-pinned",
                    Timestamp = DateTime.Now.AddDays(-30),
                    OriginalText = "Pinned original",
                    RefinedText = "Pinned refined",
                    Pinned = true
                }
            };
            File.WriteAllText(_historyPath, JsonSerializer.Serialize(existing));
            ResetHistoryManagerState();

            HistoryManager.AddEntry(new HistoryEntry
            {
                Id = "fresh",
                Timestamp = DateTime.Now,
                OriginalText = "Fresh original",
                RefinedText = "Fresh refined"
            });

            var persisted = JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(_historyPath));
            Assert.NotNull(persisted);
            Assert.DoesNotContain(persisted!, entry => entry.Id == "old-unpinned");
            Assert.Contains(persisted!, entry => entry.Id == "old-pinned");
            Assert.Contains(persisted!, entry => entry.Id == "fresh");
        }

        public void Dispose()
        {
            ConfigManager.Config.PrivacyMode = _originalPrivacyMode;
            ConfigManager.Config.HistoryRetentionDays = _originalRetentionDays;

            if (_historyBackup == null)
            {
                if (File.Exists(_historyPath))
                {
                    File.Delete(_historyPath);
                }
            }
            else
            {
                File.WriteAllText(_historyPath, _historyBackup);
            }

            if (_historyLogBackup == null)
            {
                if (File.Exists(_historyLogPath))
                {
                    File.Delete(_historyLogPath);
                }
            }
            else
            {
                File.WriteAllText(_historyLogPath, _historyLogBackup);
            }

            ResetHistoryManagerState();
        }

        private static void ResetHistoryManagerState()
        {
            var type = typeof(HistoryManager);
            type.GetField("_history", BindingFlags.NonPublic | BindingFlags.Static)!
                .SetValue(null, new List<HistoryEntry>());
            type.GetField("_isLoaded", BindingFlags.NonPublic | BindingFlags.Static)!
                .SetValue(null, false);
        }
    }
}
