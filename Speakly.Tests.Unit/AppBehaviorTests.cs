using Speakly;
using Speakly.Services;
using Xunit;

namespace Speakly.Tests.Unit
{
    public class AppBehaviorTests
    {
        [Theory]
        [InlineData("Light", "Light")]
        [InlineData("light", "Light")]
        [InlineData("Dark", "Dark")]
        [InlineData("", "Dark")]
        [InlineData("unknown", "Dark")]
        public void NormalizeThemeName_ReturnsSupportedTheme(string input, string expected)
        {
            Assert.Equal(expected, App.NormalizeThemeName(input));
        }

        [Fact]
        public void HasMeaningfulMicSignal_ReturnsFalseForSilentChunk()
        {
            var stats = new AudioProcessingStats(
                rawRms: 0.0005f,
                rawPeak: 0.004f,
                processedRms: 0.0007f,
                processedPeak: 0.005f,
                appliedGain: 1f,
                clippedSamples: 0);

            Assert.False(App.HasMeaningfulMicSignal(stats));
        }

        [Fact]
        public void HasMeaningfulMicSignal_ReturnsTrueForAudibleChunk()
        {
            var stats = new AudioProcessingStats(
                rawRms: 0.01f,
                rawPeak: 0.08f,
                processedRms: 0.012f,
                processedPeak: 0.09f,
                appliedGain: 1f,
                clippedSamples: 0);

            Assert.True(App.HasMeaningfulMicSignal(stats));
        }

        [Fact]
        public void EstimateAudioChunkDurationMs_CalculatesPcmDuration()
        {
            var durationMs = App.EstimateAudioChunkDurationMs(
                bytesLength: 3200,
                sampleRate: 16000,
                channels: 1);

            Assert.Equal(100, durationMs);
        }
    }
}
