using Speakly.Services;
using Xunit;

namespace Speakly.Tests.Unit
{
    public class RefinementRequestTests
    {
        [Fact]
        public void Create_UsesAggressiveModeWhenRequested()
        {
            var request = RefinementRequest.Create(
                "text",
                "prompt",
                "model",
                DictationExperienceService.ContextualRefinementModeAggressiveRewrite);

            Assert.True(request.AggressiveContextRewrite);
            Assert.Equal("text", request.Text);
            Assert.Equal("prompt", request.Prompt);
            Assert.Equal("model", request.Model);
        }

        [Fact]
        public void Create_UsesConservativeModeWhenRequested()
        {
            var request = RefinementRequest.Create(
                "text",
                "prompt",
                "model",
                DictationExperienceService.ContextualRefinementModeConservative);

            Assert.False(request.AggressiveContextRewrite);
        }
    }
}
