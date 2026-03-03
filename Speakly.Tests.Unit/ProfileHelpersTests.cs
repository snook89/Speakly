using Speakly.Config;

namespace Speakly.Tests.Unit
{
    public class ProfileHelpersTests
    {
        [Theory]
        [InlineData("Code.exe", "code")]
        [InlineData("notepad", "notepad")]
        [InlineData("  CHROME.EXE  ", "chrome")]
        [InlineData("", "")]
        [InlineData("   ", "")]
        public void NormalizeProcessName_ProducesStableCanonicalValue(string input, string expected)
        {
            var actual = ProfileHelpers.NormalizeProcessName(input);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void MatchesProcess_ReturnsTrueWhenProcessMapped()
        {
            var profile = new AppProfile
            {
                ProcessNames = new List<string> { "code", "notepad.exe" }
            };

            Assert.True(ProfileHelpers.MatchesProcess(profile, "CODE.EXE"));
            Assert.True(ProfileHelpers.MatchesProcess(profile, "notepad"));
            Assert.False(ProfileHelpers.MatchesProcess(profile, "chrome"));
        }
    }
}
