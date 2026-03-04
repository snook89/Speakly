using Speakly.Config;
using Speakly.Services;
using Xunit;

namespace Speakly.Tests.Unit
{
    public class ManagedAudioProcessorTests
    {
        [Fact]
        public void NoiseGate_CutsLowLevelSignal()
        {
            var config = ConfigManager.Config;
            bool oldAgc = config.AutoMicGainEnabled;
            bool oldNorm = config.DynamicNormalizationEnabled;
            bool oldGate = config.NoiseGateEnabled;
            int oldGateDb = config.NoiseGateThresholdDb;

            try
            {
                config.AutoMicGainEnabled = false;
                config.DynamicNormalizationEnabled = false;
                config.NoiseGateEnabled = true;
                config.NoiseGateThresholdDb = -20;

                var processor = new ManagedAudioProcessor();
                var input = BuildConstantPcm16(sampleCount: 800, sampleValue: 500); // about -36 dBFS
                var output = processor.Process(input, out var stats);

                Assert.Equal(input.Length, output.Length);
                Assert.True(stats.ProcessedRms < 0.001f);
            }
            finally
            {
                config.AutoMicGainEnabled = oldAgc;
                config.DynamicNormalizationEnabled = oldNorm;
                config.NoiseGateEnabled = oldGate;
                config.NoiseGateThresholdDb = oldGateDb;
            }
        }

        private static byte[] BuildConstantPcm16(int sampleCount, short sampleValue)
        {
            var bytes = new byte[sampleCount * 2];
            for (int i = 0; i < bytes.Length - 1; i += 2)
            {
                bytes[i] = (byte)(sampleValue & 0xFF);
                bytes[i + 1] = (byte)((sampleValue >> 8) & 0xFF);
            }

            return bytes;
        }
    }
}
