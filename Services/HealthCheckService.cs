using System;
using System.Collections.Generic;
using System.Linq;
using NAudio.Wave;
using Speakly.Config;

namespace Speakly.Services
{
    public sealed class HealthCheckResult
    {
        public string Summary { get; set; } = "Healthy";
        public string Details { get; set; } = string.Empty;
        public bool HasIssues { get; set; }
    }

    public static class HealthCheckService
    {
        public static HealthCheckResult Run(AppConfig config)
        {
            var issues = new List<string>();

            if (WaveInEvent.DeviceCount <= 0)
                issues.Add("No recording input device detected.");

            if (string.IsNullOrWhiteSpace(config.PttHotkey) || string.IsNullOrWhiteSpace(config.RecordHotkey))
                issues.Add("Hotkeys are not fully configured.");
            else if (string.Equals(config.PttHotkey, config.RecordHotkey, StringComparison.OrdinalIgnoreCase))
                issues.Add("PTT and Toggle hotkeys are the same.");

            if (!HasApiKeyForStt(config, config.SttModel))
                issues.Add($"Selected STT provider \"{config.SttModel}\" is missing its API key.");

            if (config.EnableSttFailover)
            {
                var usableFallback = config.SttFailoverOrder
                    .Where(p => !string.Equals(p, config.SttModel, StringComparison.OrdinalIgnoreCase))
                    .Any(p => HasApiKeyForStt(config, p));
                if (!usableFallback)
                    issues.Add("STT failover is enabled, but no fallback provider has a configured API key.");
            }

            if (config.EnableRefinement && !HasApiKeyForRefinement(config, config.RefinementModel))
                issues.Add($"Selected refinement provider \"{config.RefinementModel}\" is missing its API key.");

            if (config.OverlayWidth > 0 && config.OverlayWidth < 120)
                issues.Add("Overlay width is very small and may hide controls.");
            if (config.OverlayHeight > 0 && config.OverlayHeight < 40)
                issues.Add("Overlay height is very small and may hide controls.");

            return new HealthCheckResult
            {
                HasIssues = issues.Count > 0,
                Summary = issues.Count == 0 ? "Healthy: no startup risks detected." : $"Attention: {issues.Count} potential issue(s) detected.",
                Details = string.Join(Environment.NewLine, issues.Select(i => $"- {i}"))
            };
        }

        private static bool HasApiKeyForStt(AppConfig config, string provider)
        {
            return provider?.Trim().ToLowerInvariant() switch
            {
                "deepgram" => !string.IsNullOrWhiteSpace(config.DeepgramApiKey),
                "openai" => !string.IsNullOrWhiteSpace(config.OpenAIApiKey),
                "openrouter" => !string.IsNullOrWhiteSpace(config.OpenRouterApiKey),
                _ => false
            };
        }

        private static bool HasApiKeyForRefinement(AppConfig config, string provider)
        {
            return provider?.Trim().ToLowerInvariant() switch
            {
                "openai" => !string.IsNullOrWhiteSpace(config.OpenAIApiKey),
                "cerebras" => !string.IsNullOrWhiteSpace(config.CerebrasApiKey),
                "openrouter" => !string.IsNullOrWhiteSpace(config.OpenRouterApiKey),
                _ => false
            };
        }
    }
}
