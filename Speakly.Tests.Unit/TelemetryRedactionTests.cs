using Speakly.Config;
using Speakly.Services;

namespace Speakly.Tests.Unit
{
    public class TelemetryRedactionTests
    {
        [Fact]
        public void RedactValue_StrictMode_RemovesSensitiveText()
        {
            ConfigManager.Config.TelemetryRedactionMode = "strict";
            var value = TelemetryRedaction.RedactValue("original_text", "hello world");
            Assert.Contains("[redacted", value);
            Assert.DoesNotContain("hello world", value);
        }

        [Fact]
        public void RedactValue_HashMode_AddsDigest()
        {
            ConfigManager.Config.TelemetryRedactionMode = "hash";
            var value = TelemetryRedaction.RedactValue("prompt", "please refine this");
            Assert.Contains("sha256=", value);
            Assert.DoesNotContain("please refine this", value);
        }

        [Fact]
        public void RedactValue_OffMode_PreservesPayload()
        {
            ConfigManager.Config.TelemetryRedactionMode = "off";
            const string payload = "keep this plain text";
            var value = TelemetryRedaction.RedactValue("message", payload);
            Assert.Equal(payload, value);
        }
    }
}
