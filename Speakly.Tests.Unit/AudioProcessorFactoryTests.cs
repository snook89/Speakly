using System;
using Speakly.Config;
using Speakly.Services;
using Xunit;

namespace Speakly.Tests.Unit
{
    public class AudioProcessorFactoryTests
    {
        [Fact]
        public void NormalizeAudioProcessingEngine_InvalidValueFallsBackToStable()
        {
            var normalized = AudioProcessorFactory.NormalizeEngine("not-a-real-engine");

            Assert.Equal(AudioProcessorFactory.StableEngine, normalized);
        }

        [Fact]
        public void Create_WebRtcSelectedAtSupportedMono16k_ReturnsWebRtcProcessor()
        {
            var config = new AppConfig
            {
                AudioProcessingEngine = AudioProcessorFactory.WebRtcExperimentalEngine,
                SampleRate = 16000,
                Channels = 1
            };

            var processor = AudioProcessorFactory.Create(config);

            Assert.IsType<WebRtcAudioProcessor>(processor);
        }

        [Fact]
        public void Create_WebRtcSelectedAtUnsupportedSampleRate_FallsBackToStableProcessor()
        {
            var config = new AppConfig
            {
                AudioProcessingEngine = AudioProcessorFactory.WebRtcExperimentalEngine,
                SampleRate = 44100,
                Channels = 1
            };

            var processor = AudioProcessorFactory.Create(config);

            Assert.IsType<ManagedAudioProcessor>(processor);
        }

        [Fact]
        public void Create_WebRtcSelectedAtStereoInput_FallsBackToStableProcessor()
        {
            var config = new AppConfig
            {
                AudioProcessingEngine = AudioProcessorFactory.WebRtcExperimentalEngine,
                SampleRate = 16000,
                Channels = 2
            };

            var processor = AudioProcessorFactory.Create(config);

            Assert.IsType<ManagedAudioProcessor>(processor);
        }

        [Fact]
        public void WebRtcProcessor_ProcessMono16kFrame_ProcessesWithoutThrowing()
        {
            var config = ConfigManager.Config;
            string oldEngine = config.AudioProcessingEngine;
            int oldSampleRate = config.SampleRate;
            int oldChannels = config.Channels;
            bool oldHighPass = config.WebRtcHighPassFilterEnabled;
            bool oldNoiseSuppression = config.WebRtcNoiseSuppressionEnabled;
            string oldNoiseSuppressionLevel = config.WebRtcNoiseSuppressionLevel;
            bool oldAgc = config.WebRtcAgcEnabled;
            int oldTargetLevel = config.WebRtcAgcTargetLevelDbfs;
            int oldCompression = config.WebRtcAgcCompressionGainDb;
            bool oldLimiter = config.WebRtcAgcLimiterEnabled;
            bool oldPreAmp = config.WebRtcPreAmpEnabled;
            float oldPreAmpGain = config.WebRtcPreAmpGainFactor;

            try
            {
                config.AudioProcessingEngine = AudioProcessorFactory.WebRtcExperimentalEngine;
                config.SampleRate = 16000;
                config.Channels = 1;
                config.WebRtcHighPassFilterEnabled = true;
                config.WebRtcNoiseSuppressionEnabled = true;
                config.WebRtcNoiseSuppressionLevel = "Moderate";
                config.WebRtcAgcEnabled = true;
                config.WebRtcAgcTargetLevelDbfs = -3;
                config.WebRtcAgcCompressionGainDb = 9;
                config.WebRtcAgcLimiterEnabled = true;
                config.WebRtcPreAmpEnabled = false;
                config.WebRtcPreAmpGainFactor = 1.0f;

                using var processor = new WebRtcAudioProcessor();
                var input = BuildSineWavePcm16(sampleCount: 160, amplitude: 12000);

                var output = processor.Process(input, out var stats);

                Assert.Equal(input.Length, output.Length);
                Assert.InRange(stats.RawPeak, 0.10f, 1.0f);
                Assert.InRange(stats.ProcessedPeak, 0.01f, 1.0f);
            }
            finally
            {
                config.AudioProcessingEngine = oldEngine;
                config.SampleRate = oldSampleRate;
                config.Channels = oldChannels;
                config.WebRtcHighPassFilterEnabled = oldHighPass;
                config.WebRtcNoiseSuppressionEnabled = oldNoiseSuppression;
                config.WebRtcNoiseSuppressionLevel = oldNoiseSuppressionLevel;
                config.WebRtcAgcEnabled = oldAgc;
                config.WebRtcAgcTargetLevelDbfs = oldTargetLevel;
                config.WebRtcAgcCompressionGainDb = oldCompression;
                config.WebRtcAgcLimiterEnabled = oldLimiter;
                config.WebRtcPreAmpEnabled = oldPreAmp;
                config.WebRtcPreAmpGainFactor = oldPreAmpGain;
            }
        }

        private static byte[] BuildSineWavePcm16(int sampleCount, short amplitude)
        {
            var bytes = new byte[sampleCount * 2];
            for (int i = 0; i < sampleCount; i++)
            {
                short sample = (short)(Math.Sin((2 * Math.PI * i) / 32.0) * amplitude);
                bytes[i * 2] = (byte)(sample & 0xFF);
                bytes[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
            }

            return bytes;
        }
    }
}
