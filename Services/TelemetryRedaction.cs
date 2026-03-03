using System;
using System.Security.Cryptography;
using System.Text;
using Speakly.Config;

namespace Speakly.Services
{
    public static class TelemetryRedaction
    {
        public static string RedactValue(string key, string value)
        {
            var mode = (ConfigManager.Config.TelemetryRedactionMode ?? "strict").Trim().ToLowerInvariant();
            if (mode == "off")
            {
                return value;
            }

            if (!ShouldRedact(key))
            {
                return value;
            }

            if (mode == "hash")
            {
                return BuildHashedRedaction(value);
            }

            return $"[redacted len={value.Length}]";
        }

        public static bool ShouldRedact(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;

            var normalized = key.Trim().ToLowerInvariant();
            return normalized.Contains("text")
                || normalized.Contains("prompt")
                || normalized.Contains("transcript")
                || normalized.Contains("content")
                || normalized.Contains("message");
        }

        private static string BuildHashedRedaction(string value)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
            var shortHash = Convert.ToHexString(hash)[..12].ToLowerInvariant();
            return $"[redacted len={value.Length} sha256={shortHash}]";
        }
    }
}
