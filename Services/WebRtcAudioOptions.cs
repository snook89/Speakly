namespace Speakly.Services
{
    public static class WebRtcAudioOptions
    {
        public const string NoiseSuppressionLow = "Low";
        public const string NoiseSuppressionModerate = "Moderate";
        public const string NoiseSuppressionHigh = "High";
        public const string NoiseSuppressionVeryHigh = "VeryHigh";

        public const string GainControlAdaptiveDigital = "AdaptiveDigital";
        public const string GainControlFixedDigital = "FixedDigital";

        public static string NormalizeNoiseSuppressionLevel(string? value)
        {
            return value?.Trim() switch
            {
                NoiseSuppressionLow => NoiseSuppressionLow,
                NoiseSuppressionModerate => NoiseSuppressionModerate,
                NoiseSuppressionVeryHigh => NoiseSuppressionVeryHigh,
                _ => NoiseSuppressionHigh
            };
        }

        public static string NormalizeGainControlMode(string? value)
        {
            return value?.Trim() switch
            {
                GainControlFixedDigital => GainControlFixedDigital,
                _ => GainControlAdaptiveDigital
            };
        }
    }
}
