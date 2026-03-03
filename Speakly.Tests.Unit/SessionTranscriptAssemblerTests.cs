using Speakly.Services;

namespace Speakly.Tests.Unit
{
    public class SessionTranscriptAssemblerTests
    {
        [Fact]
        public void MergeFinalSegments_HandlesDuplicatesAndOverlap()
        {
            var merged = SessionTranscriptAssembler.MergeFinalSegments(new[]
            {
                "I want you to take a look at debug logs",
                "I want you to take a look at debug logs",
                "debug logs where is it located",
                "where is it located?"
            });

            Assert.Equal("I want you to take a look at debug logs where is it located?", merged);
        }

        [Fact]
        public void MergeFinalSegments_MergesPauseSeparatedUtterances()
        {
            var merged = SessionTranscriptAssembler.MergeFinalSegments(new[]
            {
                "So I read this text",
                "but this is what I actually get",
                "and I still hear beeps"
            });

            Assert.Equal("So I read this text but this is what I actually get and I still hear beeps", merged);
        }

        [Fact]
        public void MergeFinalSegments_IgnoresBlankValues()
        {
            var merged = SessionTranscriptAssembler.MergeFinalSegments(new[]
            {
                "  ",
                "\t",
                "Hello",
                " "
            });

            Assert.Equal("Hello", merged);
        }
    }
}
