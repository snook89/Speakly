using Speakly.Config;
using Speakly.Services;

namespace Speakly.Tests.Unit
{
    public class ElevenLabsProviderTests
    {
        [Fact]
        public void TranscriberFactory_ReturnsElevenLabsTranscriber()
        {
            var transcriber = TranscriberFactory.CreateTranscriber("ElevenLabs");

            Assert.IsType<ElevenLabsTranscriber>(transcriber);
        }

        [Fact]
        public void ResolveSttModel_ReturnsElevenLabsDefaultWhenUnset()
        {
            var resolved = ConfigManager.ResolveSttModel("ElevenLabs");

            Assert.Equal("scribe_v2_realtime", resolved);
        }

        [Fact]
        public void LegacyElevenLabsApiKey_MigratesPlaintextIntoRuntimeField()
        {
            var config = new AppConfig
            {
                ElevenLabsApiKey = string.Empty
            };

            config.LegacyElevenLabsApiKey = "test-elevenlabs-key";

            Assert.Equal("test-elevenlabs-key", config.ElevenLabsApiKey);
        }

        [Fact]
        public void HealthCheck_FlagsMissingElevenLabsKeyWhenSelected()
        {
            var config = new AppConfig
            {
                SttModel = "ElevenLabs",
                ElevenLabsApiKey = string.Empty,
                EnableSttFailover = false,
                EnableRefinement = false
            };

            var result = HealthCheckService.Run(config);

            Assert.Contains("Selected STT provider \"ElevenLabs\" is missing its API key.", result.Details);
        }

        [Fact]
        public void Protocol_ParsesSessionStartedMessage()
        {
            var message = ElevenLabsRealtimeProtocol.ParseMessage("{\"type\":\"session_started\"}");

            Assert.Equal("session_started", message.MessageType);
        }

        [Fact]
        public void Protocol_ParsesCommittedTranscriptMessage()
        {
            var message = ElevenLabsRealtimeProtocol.ParseMessage("{\"type\":\"committed_transcript\",\"text\":\"Testing one two three\"}");

            Assert.Equal("committed_transcript", message.MessageType);
            Assert.Equal("Testing one two three", message.Text);
        }

        [Fact]
        public void Protocol_BuildsCommitPayload()
        {
            var payload = ElevenLabsRealtimeProtocol.BuildInputAudioChunk([], 16000, commit: true);

            Assert.Contains("\"message_type\":\"input_audio_chunk\"", payload);
            Assert.Contains("\"commit\":true", payload);
            Assert.Contains("\"sample_rate\":16000", payload);
        }
    }
}
