using Speakly;
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
    }
}
