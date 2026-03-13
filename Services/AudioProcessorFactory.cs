using Speakly.Config;

namespace Speakly.Services
{
    public static class AudioProcessorFactory
    {
        public const string StableEngine = "Stable";
        public const string WebRtcExperimentalEngine = "WebRTC Experimental";
        public const string EngineStable = StableEngine;
        public const string EngineWebRtcExperimental = WebRtcExperimentalEngine;

        public static string NormalizeEngine(string? engine)
        {
            return string.Equals(engine?.Trim(), WebRtcExperimentalEngine, StringComparison.OrdinalIgnoreCase)
                ? WebRtcExperimentalEngine
                : StableEngine;
        }

        public static IAudioFrameProcessor Create(string? engine)
        {
            return NormalizeEngine(engine) == WebRtcExperimentalEngine
                ? new WebRtcAudioProcessor()
                : new ManagedAudioProcessor();
        }

        public static IAudioFrameProcessor Create(AppConfig config)
        {
            if (config == null)
            {
                return new ManagedAudioProcessor();
            }

            var engine = NormalizeEngine(config.AudioProcessingEngine);
            if (engine == WebRtcExperimentalEngine && !SupportsWebRtc(config))
            {
                return new ManagedAudioProcessor();
            }

            return Create(engine);
        }

        public static bool SupportsWebRtc(AppConfig config)
        {
            if (config == null)
            {
                return false;
            }

            bool sampleRateSupported = config.SampleRate is 8000 or 16000 or 32000 or 48000;
            bool mono = config.Channels == 1;
            return sampleRateSupported && mono;
        }

        public static string DescribeEngineSelection(AppConfig config)
        {
            if (config == null)
            {
                return "Stable managed cleanup is active.";
            }

            var selected = NormalizeEngine(config.AudioProcessingEngine);
            if (selected != WebRtcExperimentalEngine)
            {
                return "Stable managed cleanup is active.";
            }

            if (SupportsWebRtc(config))
            {
                return "WebRTC Experimental is active.";
            }

            return "WebRTC Experimental requires mono at 8/16/32/48 kHz. Falling back to Stable.";
        }
    }
}
