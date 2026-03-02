using System;

namespace Speakly.Services
{
    public static class ErrorClassifier
    {
        public static string Classify(string? error)
        {
            if (string.IsNullOrWhiteSpace(error)) return "unknown";

            var text = error.ToLowerInvariant();

            if (text.Contains("401") || text.Contains("unauthorized") || text.Contains("api key"))
                return "auth";
            if (text.Contains("403") || text.Contains("forbidden"))
                return "forbidden";
            if (text.Contains("429") || text.Contains("rate") || text.Contains("quota"))
                return "rate_limit";
            if (text.Contains("timeout") || text.Contains("timed out"))
                return "timeout";
            if (text.Contains("ssl") || text.Contains("socket") || text.Contains("network") || text.Contains("dns"))
                return "network";
            if (text.Contains("uipi") || text.Contains("elevated"))
                return "permission";

            return "unknown";
        }

        public static bool IsTransient(string code)
        {
            return string.Equals(code, "timeout", StringComparison.OrdinalIgnoreCase)
                || string.Equals(code, "network", StringComparison.OrdinalIgnoreCase)
                || string.Equals(code, "rate_limit", StringComparison.OrdinalIgnoreCase);
        }
    }
}
