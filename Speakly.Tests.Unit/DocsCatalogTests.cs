using System.Linq;
using Speakly.Docs;

namespace Speakly.Tests.Unit
{
    public class DocsCatalogTests
    {
        [Fact]
        public void Topics_ContainAllRequiredV1Sections_InExpectedOrder()
        {
            var keys = DocsCatalog.Topics.Select(topic => topic.Key).ToArray();

            Assert.Equal(DocsCatalog.RequiredTopicKeys, keys);
            Assert.Equal("overview", keys[0]);
            Assert.DoesNotContain("docs", keys);
        }

        [Fact]
        public void Topics_HaveSummaryDefaultsExamplesAndGotchas()
        {
            foreach (var topic in DocsCatalog.Topics)
            {
                Assert.False(string.IsNullOrWhiteSpace(topic.Title));
                Assert.False(string.IsNullOrWhiteSpace(topic.Summary));
                Assert.NotEmpty(topic.Sections);
                Assert.All(topic.Sections, section =>
                {
                    Assert.False(string.IsNullOrWhiteSpace(section.Title));
                    Assert.False(string.IsNullOrWhiteSpace(section.Body));
                });
                Assert.NotEmpty(topic.RecommendedDefaults);
                Assert.NotEmpty(topic.Examples);
                Assert.All(topic.Examples, example =>
                {
                    Assert.False(string.IsNullOrWhiteSpace(example.Scenario));
                    Assert.False(string.IsNullOrWhiteSpace(example.SpokenInput));
                    Assert.False(string.IsNullOrWhiteSpace(example.Result));
                    Assert.False(string.IsNullOrWhiteSpace(example.WhyItHelps));
                });
                Assert.NotEmpty(topic.Gotchas);
            }
        }

        [Fact]
        public void Topics_TargetPageTags_MapToRegisteredMainWindowSections()
        {
            var registeredSections = MainWindow.RegisteredSections;

            foreach (var topic in DocsCatalog.Topics.Where(topic => topic.HasTargetPage))
            {
                Assert.Contains(topic.TargetPageTag!, registeredSections);
            }
        }

        [Fact]
        public void Docs_ExplicitlyDescribeCriticalBehavior()
        {
            var combinedText = string.Join(
                "\n",
                DocsCatalog.Topics.SelectMany(topic => new[]
                {
                    topic.Title,
                    topic.Summary,
                    string.Join("\n", topic.Sections.Select(section => $"{section.Title} {section.Body}")),
                    string.Join("\n", topic.RecommendedDefaults),
                    string.Join("\n", topic.Examples.Select(example => $"{example.Scenario} {example.SpokenInput} {example.Result} {example.WhyItHelps}")),
                    string.Join("\n", topic.Gotchas),
                    string.Join("\n", (topic.Links ?? []).Select(link => $"{link.Label} {link.Url} {link.Description}"))
                }));

            Assert.Contains("process name", combinedText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("delete that", combinedText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("style preset wins", combinedText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Aggressive", combinedText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("No history", combinedText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("keyboard hook", combinedText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("session profile", combinedText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("cloud.cerebras.ai", combinedText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("console.deepgram.com/signup", combinedText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("elevenlabs.io/app/sign-up", combinedText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("API Keys section", combinedText, StringComparison.OrdinalIgnoreCase);
        }
    }
}
