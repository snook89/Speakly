using Speakly.Services;
using Xunit;

namespace Speakly.Tests.Unit
{
    public class PersonalDictionaryServiceTests
    {
        [Fact]
        public void ParseTerms_DeduplicatesAndTrims()
        {
            var result = PersonalDictionaryService.ParseTerms(" Speakly ,\r\nOpenRouter\nspeakly ");
            Assert.Equal(2, result.Count);
            Assert.Contains("Speakly", result);
            Assert.Contains("OpenRouter", result);
        }

        [Fact]
        public void ApplyCorrections_NormalizesKnownTerms()
        {
            var corrected = PersonalDictionaryService.ApplyCorrections(
                "spEakly works with notepad++",
                new[] { "Speakly", "Notepad++" },
                out var replacements);

            Assert.Equal("Speakly works with Notepad++", corrected);
            Assert.Equal(2, replacements);
        }

        [Fact]
        public void ExtractCandidateTerms_ExcludesKnownTerms()
        {
            var candidates = PersonalDictionaryService.ExtractCandidateTerms(
                "I spoke with Speakly and JonSnow about GPT4.",
                new[] { "Speakly" },
                maxCandidates: 10);

            Assert.DoesNotContain("Speakly", candidates);
            Assert.Contains("JonSnow", candidates);
            Assert.Contains("GPT4", candidates);
        }
    }
}
