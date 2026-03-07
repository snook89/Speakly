using Speakly.Services;

namespace Speakly.Tests.Unit
{
    public class RefinementSafetyTests
    {
        [Fact]
        public void CoerceToEditOnlyOutput_RejectsSevereShortening()
        {
            var original = "Instead of just leaving it at that, it actually provided an alternative to the user. " +
                           "So it seems like this model is tuned to be less strict and more helpful in redirection. " +
                           "They also mentioned improved web search capability and I wanted to test that flow end to end.";
            var candidate = "Instead of just leaving it at that, it provided an alternative.";

            var result = RefinementSafety.CoerceToEditOnlyOutput(original, candidate);

            Assert.Equal(original, result);
        }

        [Fact]
        public void CoerceToEditOnlyOutput_AllowsNormalEditLength()
        {
            var original = "i want you to take a look at debug logs where is it located the debug logs is enabled in the app";
            var candidate = "I want you to take a look at debug logs. Where is it located? The debug logs are enabled in the app.";

            var result = RefinementSafety.CoerceToEditOnlyOutput(original, candidate);

            Assert.Equal(candidate, result);
        }

        [Fact]
        public void CoerceToEditOnlyOutput_AllowsUsefulAggressiveExpansion()
        {
            var original = "Yes. I will send it tomorrow.";
            var candidate = "Yes, I will send the final invoice tomorrow.";

            var result = RefinementSafety.CoerceToEditOnlyOutput(original, candidate, aggressiveContextRewrite: true);

            Assert.Equal(candidate, result);
        }

        [Fact]
        public void CoerceToEditOnlyOutput_RejectsAggressiveMeaningInversion()
        {
            var original = "Tell him it is available tomorrow.";
            var candidate = "Tell him no upgrade is available today.";

            var result = RefinementSafety.CoerceToEditOnlyOutput(original, candidate, aggressiveContextRewrite: true);

            Assert.Equal(original, result);
        }

        [Fact]
        public void CoerceToEditOnlyOutput_RejectsLowOverlapAggressiveRewrite()
        {
            var original = "It is already there.";
            var candidate = "The quarterly budget meeting was moved to Thursday afternoon.";

            var result = RefinementSafety.CoerceToEditOnlyOutput(original, candidate, aggressiveContextRewrite: true);

            Assert.Equal(original, result);
        }

        [Fact]
        public void BuildSafeSystemPrompt_IncludesNoSummarizeConstraint()
        {
            var prompt = RefinementSafety.BuildSafeSystemPrompt("Fix grammar.");

            Assert.Contains("do not summarize, omit, or shorten", prompt, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
