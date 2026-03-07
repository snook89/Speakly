using Speakly.Config;
using Speakly.Services;
using Xunit;

namespace Speakly.Tests.Unit
{
    public class RefinementLearningServiceTests
    {
        [Fact]
        public void ExtractSuggestions_ReturnsDictionarySuggestion_ForProductNameCorrection()
        {
            var suggestions = RefinementLearningService.ExtractSuggestions(
                "openrouter works with notepad++",
                "OpenRouter works with Notepad++",
                new[] { "Speakly" },
                new SnippetEntry[0],
                maxSuggestions: 8);

            Assert.Contains(suggestions, suggestion =>
                suggestion.SuggestionType == "Dictionary" &&
                suggestion.SuggestedText == "OpenRouter");
            Assert.Contains(suggestions, suggestion =>
                suggestion.SuggestionType == "Dictionary" &&
                suggestion.SuggestedText == "Notepad++");
        }

        [Fact]
        public void ExtractSuggestions_ReturnsSnippetSuggestion_ForStructuralRewrite()
        {
            var suggestions = RefinementLearningService.ExtractSuggestions(
                "best regards john",
                "Best regards, John",
                new string[0],
                new SnippetEntry[0],
                maxSuggestions: 8);

            Assert.Contains(suggestions, suggestion =>
                suggestion.SuggestionType == "Snippet" &&
                suggestion.SourceText == "best regards john" &&
                suggestion.SuggestedText == "Best regards, John");
        }
    }
}
