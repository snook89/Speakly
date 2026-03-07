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
    }
}
