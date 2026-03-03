using Speakly.Config;
using Speakly.Services;
using System.IO;

namespace Speakly.Tests.Unit
{
    public class TelemetryManagerTests
    {
        [Fact]
        public void Track_WritesEventVisibleInSummary()
        {
            var telemetryDir = Path.Combine(Path.GetTempPath(), "speakly-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(telemetryDir);
            Environment.SetEnvironmentVariable("SPEAKLY_TELEMETRY_DIR", telemetryDir);

            ConfigManager.Config.TelemetryEnabled = true;
            ConfigManager.Config.TelemetryRedactionMode = "strict";
            try
            {
                var before = TelemetryManager.GetSummary(1).TotalEvents;
                TelemetryManager.Track(
                    name: "unit_test_event",
                    sessionId: Guid.NewGuid().ToString("N"),
                    operationId: Guid.NewGuid().ToString("N"),
                    data: new Dictionary<string, string> { ["text"] = "hello from unit test" });
                var after = TelemetryManager.GetSummary(1).TotalEvents;

                Assert.True(after >= before + 1);
            }
            finally
            {
                Environment.SetEnvironmentVariable("SPEAKLY_TELEMETRY_DIR", null);
                if (Directory.Exists(telemetryDir))
                {
                    Directory.Delete(telemetryDir, recursive: true);
                }
            }
        }
    }
}
