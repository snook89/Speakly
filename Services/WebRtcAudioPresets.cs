using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Speakly.Config;
using SoundFlow.Extensions.WebRtc.Apm;

namespace Speakly.Services
{
    public readonly record struct WebRtcAudioPresetDefinition(
        string Name,
        bool HighPassEnabled,
        bool NoiseSuppressionEnabled,
        NoiseSuppressionLevel NoiseSuppressionLevel,
        bool AgcEnabled,
        int AgcTargetLevelDbfs,
        int AgcCompressionGainDb,
        bool AgcLimiterEnabled,
        bool PreAmpEnabled,
        float PreAmpGainFactor);

    public static class WebRtcAudioPresets
    {
        public const string Balanced = "Balanced";
        public const string NoisyRoom = "Noisy Room";
        public const string KeyboardDesk = "Keyboard Desk";
        public const string SpeakerEcho = "Speaker / Echo";
        public const string BroadcastCloseMic = "Broadcast / Close Mic";
        public const string Custom = "Custom";

        private static readonly IReadOnlyList<WebRtcAudioPresetDefinition> Presets = new[]
        {
            new WebRtcAudioPresetDefinition(Balanced, true, true, NoiseSuppressionLevel.Moderate, true, -3, 9, true, false, 1.0f),
            new WebRtcAudioPresetDefinition(NoisyRoom, true, true, NoiseSuppressionLevel.VeryHigh, true, -3, 12, true, false, 1.0f),
            new WebRtcAudioPresetDefinition(KeyboardDesk, true, true, NoiseSuppressionLevel.High, true, -3, 9, true, false, 1.0f),
            new WebRtcAudioPresetDefinition(SpeakerEcho, true, true, NoiseSuppressionLevel.High, true, -6, 9, true, false, 1.0f),
            new WebRtcAudioPresetDefinition(BroadcastCloseMic, true, true, NoiseSuppressionLevel.Low, false, -3, 6, true, false, 1.0f)
        };

        public static ReadOnlyCollection<string> GetPresetNames()
        {
            return new ReadOnlyCollection<string>(Presets.Select(p => p.Name).Append(Custom).ToList());
        }

        public static string NormalizePreset(string? preset)
        {
            if (string.IsNullOrWhiteSpace(preset))
            {
                return Balanced;
            }

            var match = Presets.FirstOrDefault(p => string.Equals(p.Name, preset.Trim(), StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match.Name))
            {
                return match.Name;
            }

            return string.Equals(preset.Trim(), Custom, StringComparison.OrdinalIgnoreCase)
                ? Custom
                : Balanced;
        }

        public static WebRtcAudioPresetDefinition GetPreset(string? preset)
        {
            var normalized = NormalizePreset(preset);
            var match = Presets.FirstOrDefault(p => string.Equals(p.Name, normalized, StringComparison.OrdinalIgnoreCase));
            return string.IsNullOrWhiteSpace(match.Name) ? Presets[0] : match;
        }

        public static void ApplyPreset(AppConfig config, string? preset)
        {
            if (config == null) return;

            var definition = GetPreset(preset);
            config.AudioCleanupPreset = definition.Name;
            config.WebRtcHighPassFilterEnabled = definition.HighPassEnabled;
            config.WebRtcNoiseSuppressionEnabled = definition.NoiseSuppressionEnabled;
            config.WebRtcNoiseSuppressionLevel = definition.NoiseSuppressionLevel.ToString();
            config.WebRtcAgcEnabled = definition.AgcEnabled;
            config.WebRtcAgcTargetLevelDbfs = definition.AgcTargetLevelDbfs;
            config.WebRtcAgcCompressionGainDb = definition.AgcCompressionGainDb;
            config.WebRtcAgcLimiterEnabled = definition.AgcLimiterEnabled;
            config.WebRtcPreAmpEnabled = definition.PreAmpEnabled;
            config.WebRtcPreAmpGainFactor = definition.PreAmpGainFactor;
        }

        public static string InferPreset(AppConfig config)
        {
            if (config == null)
            {
                return Balanced;
            }

            foreach (var preset in Presets)
            {
                if (MatchesPreset(config, preset))
                {
                    return preset.Name;
                }
            }

            return Custom;
        }

        public static void NormalizeConfig(AppConfig config)
        {
            if (config == null) return;

            config.AudioCleanupPreset = NormalizePreset(config.AudioCleanupPreset);
            config.WebRtcNoiseSuppressionLevel = NormalizeNoiseSuppressionLevel(config.WebRtcNoiseSuppressionLevel).ToString();
            config.WebRtcAgcTargetLevelDbfs = Math.Clamp(config.WebRtcAgcTargetLevelDbfs, -31, 0);
            config.WebRtcAgcCompressionGainDb = Math.Clamp(config.WebRtcAgcCompressionGainDb, 0, 90);
            config.WebRtcPreAmpGainFactor = Math.Clamp(config.WebRtcPreAmpGainFactor, 0.5f, 4.0f);
        }

        public static NoiseSuppressionLevel NormalizeNoiseSuppressionLevel(string? level)
        {
            return Enum.TryParse<NoiseSuppressionLevel>(level, ignoreCase: true, out var parsed)
                ? parsed
                : NoiseSuppressionLevel.Moderate;
        }

        public static string BuildFeatureSummary(AppConfig config)
        {
            if (config == null)
            {
                return "Stable managed cleanup";
            }

            if (AudioProcessorFactory.NormalizeEngine(config.AudioProcessingEngine) != AudioProcessorFactory.WebRtcExperimentalEngine)
            {
                return "Stable managed cleanup";
            }

            var features = new List<string>();
            if (config.WebRtcHighPassFilterEnabled) features.Add("HPF");
            if (config.WebRtcNoiseSuppressionEnabled) features.Add($"NS {NormalizeNoiseSuppressionLevel(config.WebRtcNoiseSuppressionLevel)}");
            if (config.WebRtcAgcEnabled) features.Add("AGC");
            if (config.WebRtcAgcLimiterEnabled) features.Add("Limiter");
            if (config.WebRtcPreAmpEnabled) features.Add($"Preamp x{config.WebRtcPreAmpGainFactor:0.0}");
            features.Add($"Preset {InferPreset(config)}");
            return string.Join(" | ", features);
        }

        private static bool MatchesPreset(AppConfig config, WebRtcAudioPresetDefinition preset)
        {
            return config.WebRtcHighPassFilterEnabled == preset.HighPassEnabled
                   && config.WebRtcNoiseSuppressionEnabled == preset.NoiseSuppressionEnabled
                   && NormalizeNoiseSuppressionLevel(config.WebRtcNoiseSuppressionLevel) == preset.NoiseSuppressionLevel
                   && config.WebRtcAgcEnabled == preset.AgcEnabled
                   && config.WebRtcAgcTargetLevelDbfs == preset.AgcTargetLevelDbfs
                   && config.WebRtcAgcCompressionGainDb == preset.AgcCompressionGainDb
                   && config.WebRtcAgcLimiterEnabled == preset.AgcLimiterEnabled
                   && config.WebRtcPreAmpEnabled == preset.PreAmpEnabled
                   && Math.Abs(config.WebRtcPreAmpGainFactor - preset.PreAmpGainFactor) < 0.0001f;
        }
    }
}
