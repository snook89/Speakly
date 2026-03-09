using Speakly.Services;

namespace Speakly.Tests.Unit
{
    public class UpdateReleaseNotesFormatterTests
    {
        [Fact]
        public void Parse_UsesMarkdownBulletsAndSummary()
        {
            var notes = UpdateReleaseNotesFormatter.Parse(
                "2.0.11",
                """
                Release 2.0.11 improves the update experience.

                - Show release notes in the update dialog
                - Play a custom update sound
                """,
                null);

            Assert.Equal("Release 2.0.11 improves the update experience.", notes.Summary);
            Assert.Contains("Show release notes in the update dialog", notes.Highlights);
            Assert.Contains("Play a custom update sound", notes.Highlights);
        }

        [Fact]
        public void Parse_FallsBackToHtmlListItems()
        {
            var notes = UpdateReleaseNotesFormatter.Parse(
                "2.0.11",
                null,
                "<p>Fresh polish for updates.</p><ul><li>Compact update card</li><li>Restart later flow</li></ul>");

            Assert.Equal("Fresh polish for updates.", notes.Summary);
            Assert.Contains("Compact update card", notes.Highlights);
            Assert.Contains("Restart later flow", notes.Highlights);
        }

        [Fact]
        public void Parse_ProvidesFallbackWhenNoNotesExist()
        {
            var notes = UpdateReleaseNotesFormatter.Parse("2.0.11", null, null);

            Assert.Equal("What's new in 2.0.11", notes.Summary);
            Assert.Single(notes.Highlights);
        }
    }
}
