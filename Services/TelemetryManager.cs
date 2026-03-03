using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Speakly.Config;

namespace Speakly.Services
{
    public sealed class TelemetrySummary
    {
        public int TotalEvents { get; set; }
        public int ErrorEvents { get; set; }
        public int SessionStarts { get; set; }
        public int SessionEnds { get; set; }
        public double ErrorRatePercent { get; set; }
    }

    public sealed class TelemetryEvent
    {
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
        public string Name { get; set; } = string.Empty;
        public string Level { get; set; } = "info";
        public string SessionId { get; set; } = string.Empty;
        public string OperationId { get; set; } = string.Empty;
        public bool Success { get; set; } = true;
        public string Result { get; set; } = string.Empty;
        public string ErrorCode { get; set; } = string.Empty;
        public string ErrorClass { get; set; } = string.Empty;
        public int DurationMs { get; set; }
        public Dictionary<string, string> Data { get; set; } = new();
    }

    public static class TelemetryManager
    {
        private static readonly object LockObj = new();
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
        private static DateTime _lastRetentionSweepUtc = DateTime.MinValue;

        public static void Track(
            string name,
            string level = "info",
            bool success = true,
            string result = "",
            string sessionId = "",
            string operationId = "",
            string errorCode = "",
            string errorClass = "",
            int durationMs = 0,
            Dictionary<string, string>? data = null)
        {
            if (!ConfigManager.Config.TelemetryEnabled) return;
            if (!ShouldEmit(level, name)) return;

            try
            {
                lock (LockObj)
                {
                    EnsureStorageReady();
                    var entry = new TelemetryEvent
                    {
                        TimestampUtc = DateTime.UtcNow,
                        Name = name?.Trim() ?? string.Empty,
                        Level = string.IsNullOrWhiteSpace(level) ? "info" : level.Trim().ToLowerInvariant(),
                        SessionId = sessionId?.Trim() ?? string.Empty,
                        OperationId = operationId?.Trim() ?? string.Empty,
                        Success = success,
                        Result = result ?? string.Empty,
                        ErrorCode = errorCode ?? string.Empty,
                        ErrorClass = errorClass ?? string.Empty,
                        DurationMs = Math.Max(0, durationMs),
                        Data = SanitizeData(data)
                    };

                    RotateIfNeeded();

                    var telemetryPath = GetTelemetryPath();
                    var line = JsonSerializer.Serialize(entry, JsonOptions);
                    File.AppendAllText(telemetryPath, line + Environment.NewLine);
                }
            }
            catch
            {
                // Ignore telemetry persistence failures to avoid impacting app behavior.
            }
        }

        private static bool ShouldEmit(string level, string name)
        {
            var configured = (ConfigManager.Config.TelemetryLevel ?? "normal").Trim().ToLowerInvariant();
            var normalizedLevel = (level ?? "info").Trim().ToLowerInvariant();
            var normalizedName = (name ?? string.Empty).Trim().ToLowerInvariant();

            if (configured == "verbose") return true;

            if (configured == "minimal")
            {
                if (normalizedLevel is "error" or "warning") return true;

                return normalizedName is
                    "app_start" or
                    "app_exit" or
                    "session_start" or
                    "session_end" or
                    "transcriber_error" or
                    "failover_result";
            }

            // normal
            return normalizedLevel != "debug" && normalizedLevel != "trace";
        }

        public static TelemetrySummary GetSummary(int daysBack)
        {
            lock (LockObj)
            {
                var summary = new TelemetrySummary();
                var telemetryDirectory = GetTelemetryDirectory();
                if (!Directory.Exists(telemetryDirectory)) return summary;

                var startUtc = DateTime.UtcNow.Date.AddDays(-(Math.Max(1, daysBack) - 1));
                var allFiles = Directory.GetFiles(telemetryDirectory, "telemetry_events*.jsonl");
                foreach (var file in allFiles)
                {
                    foreach (var line in ReadLinesSafe(file))
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        try
                        {
                            var evt = JsonSerializer.Deserialize<TelemetryEvent>(line);
                            if (evt == null || evt.TimestampUtc < startUtc) continue;

                            summary.TotalEvents++;
                            if (!evt.Success || string.Equals(evt.Level, "error", StringComparison.OrdinalIgnoreCase))
                                summary.ErrorEvents++;
                            if (string.Equals(evt.Name, "session_start", StringComparison.OrdinalIgnoreCase))
                                summary.SessionStarts++;
                            if (string.Equals(evt.Name, "session_end", StringComparison.OrdinalIgnoreCase))
                                summary.SessionEnds++;
                        }
                        catch
                        {
                            // Ignore malformed records.
                        }
                    }
                }

                summary.ErrorRatePercent = summary.TotalEvents == 0
                    ? 0
                    : Math.Round(summary.ErrorEvents * 100.0 / summary.TotalEvents, 1);
                return summary;
            }
        }

        private static Dictionary<string, string> SanitizeData(Dictionary<string, string>? input)
        {
            var output = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (input == null || input.Count == 0) return output;

            foreach (var kv in input)
            {
                var safeKey = kv.Key?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(safeKey)) continue;
                var safeValue = kv.Value ?? string.Empty;
                output[safeKey] = TelemetryRedaction.RedactValue(safeKey, safeValue);
            }

            return output;
        }

        private static void EnsureStorageReady()
        {
            var telemetryDirectory = GetTelemetryDirectory();
            if (!Directory.Exists(telemetryDirectory))
            {
                Directory.CreateDirectory(telemetryDirectory);
            }

            var now = DateTime.UtcNow;
            if ((now - _lastRetentionSweepUtc).TotalMinutes < 15) return;

            _lastRetentionSweepUtc = now;
            PurgeOldFiles();
        }

        private static void RotateIfNeeded()
        {
            var maxMb = Math.Clamp(ConfigManager.Config.TelemetryMaxFileMb, 1, 512);
            long maxBytes = maxMb * 1024L * 1024L;
            var telemetryPath = GetTelemetryPath();
            if (!File.Exists(telemetryPath)) return;

            var fileInfo = new FileInfo(telemetryPath);
            if (fileInfo.Length <= maxBytes) return;

            var telemetryDirectory = GetTelemetryDirectory();
            var archived = Path.Combine(
                telemetryDirectory,
                $"telemetry_events_{DateTime.UtcNow:yyyyMMdd_HHmmss}.jsonl");
            File.Move(telemetryPath, archived, overwrite: true);
        }

        private static void PurgeOldFiles()
        {
            var telemetryDirectory = GetTelemetryDirectory();
            if (!Directory.Exists(telemetryDirectory)) return;

            var retentionDays = Math.Clamp(ConfigManager.Config.TelemetryRetentionDays, 1, 3650);
            var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
            foreach (var path in Directory.GetFiles(telemetryDirectory, "telemetry_events*.jsonl"))
            {
                try
                {
                    var info = new FileInfo(path);
                    if (info.LastWriteTimeUtc < cutoff)
                    {
                        File.Delete(path);
                    }
                }
                catch
                {
                    // Ignore retention deletion errors.
                }
            }
        }

        private static IEnumerable<string> ReadLinesSafe(string filePath)
        {
            try
            {
                return File.ReadLines(filePath).ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static string GetTelemetryDirectory()
        {
            var overridePath = Environment.GetEnvironmentVariable("SPEAKLY_TELEMETRY_DIR");
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                return overridePath.Trim();
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Speakly",
                "Telemetry");
        }

        private static string GetTelemetryPath()
        {
            return Path.Combine(GetTelemetryDirectory(), "telemetry_events.jsonl");
        }
    }
}
