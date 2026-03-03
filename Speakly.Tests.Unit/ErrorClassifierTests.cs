using Speakly.Services;

namespace Speakly.Tests.Unit
{
    public class ErrorClassifierTests
    {
        [Theory]
        [InlineData("401 unauthorized", "auth")]
        [InlineData("403 forbidden", "forbidden")]
        [InlineData("429 too many requests", "rate_limit")]
        [InlineData("gateway timeout 504", "server")]
        [InlineData("request timed out", "timeout")]
        [InlineData("socket dns failure", "network")]
        [InlineData("uipi elevated window", "permission")]
        [InlineData("something unexpected", "unknown")]
        public void Classify_MapsKnownErrorShapes(string input, string expected)
        {
            var actual = ErrorClassifier.Classify(input);
            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData("timeout", true)]
        [InlineData("network", true)]
        [InlineData("rate_limit", true)]
        [InlineData("server", true)]
        [InlineData("auth", false)]
        [InlineData("forbidden", false)]
        [InlineData("unknown", false)]
        public void IsTransient_ReturnsExpected(string code, bool expected)
        {
            var actual = ErrorClassifier.IsTransient(code);
            Assert.Equal(expected, actual);
        }
    }
}
