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

        [Fact]
        public void SubscriptionParser_ExtractsPlanAndBalance()
        {
            var info = ApiTester.ParseElevenLabsSubscriptionResponse("""
                {
                  "tier": "Creator",
                  "character_count": 87,
                  "character_limit": 10000
                }
                """);

            Assert.True(info.Success);
            Assert.Equal("Creator", info.Plan);
            Assert.Equal(87, info.Used);
            Assert.Equal(10000, info.Limit);
            Assert.Equal(9913, info.Remaining);
        }

        [Fact]
        public void SubscriptionParser_HandlesMissingBalanceFields()
        {
            var info = ApiTester.ParseElevenLabsSubscriptionResponse("""
                {
                  "tier": "Starter"
                }
                """);

            Assert.True(info.Success);
            Assert.Equal("Starter", info.Plan);
            Assert.Null(info.Used);
            Assert.Null(info.Limit);
            Assert.Null(info.Remaining);
            Assert.Contains("Subscription loaded", info.StatusMessage);
        }
    }
}
