using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Speakly.Config;

namespace Speakly.Services
{
    public sealed class CorrectionSuggestionEntry
    {
        public string SourceText { get; init; } = string.Empty;
        public string SuggestedText { get; init; } = string.Empty;
        public string SuggestionType { get; init; } = "Snippet";
        public string Reason { get; init; } = string.Empty;

        public bool CanAddToDictionary => string.Equals(SuggestionType, "Dictionary", StringComparison.OrdinalIgnoreCase);
        public bool CanSaveAsSnippet => !string.IsNullOrWhiteSpace(SourceText)
            && !string.IsNullOrWhiteSpace(SuggestedText)
            && !string.Equals(SourceText, SuggestedText, StringComparison.Ordinal);
        public string DisplayLabel => $"{SourceText} -> {SuggestedText}";
        public string DisplayMeta => string.IsNullOrWhiteSpace(Reason) ? SuggestionType : $"{SuggestionType} | {Reason}";
    }

    public static class RefinementLearningService
    {
        private static readonly Regex TokenRegex =
            new Regex(@"(?<![\p{L}\p{M}\p{N}_])[\p{L}\p{M}\p{N}][\p{L}\p{M}\p{N}_'+\-\.\+]*(?![\p{L}\p{M}\p{N}_])", RegexOptions.Compiled);

        public static IReadOnlyList<CorrectionSuggestionEntry> ExtractSuggestions(
            string? sourceText,
            string? refinedText,
            IEnumerable<string>? knownDictionaryTerms,
            IEnumerable<SnippetEntry>? existingSnippets,
            int maxSuggestions = 8)
        {
            if (string.IsNullOrWhiteSpace(sourceText) || string.IsNullOrWhiteSpace(refinedText))
            {
                return Array.Empty<CorrectionSuggestionEntry>();
            }

            var normalizedSource = sourceText.Trim();
            var normalizedRefined = refinedText.Trim();
            if (string.Equals(normalizedSource, normalizedRefined, StringComparison.Ordinal))
            {
                return Array.Empty<CorrectionSuggestionEntry>();
            }

            var suggestions = new List<CorrectionSuggestionEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var knownTerms = new HashSet<string>(
                (knownDictionaryTerms ?? Enumerable.Empty<string>())
                    .Where(term => !string.IsNullOrWhiteSpace(term))
                    .Select(term => term.Trim()),
                StringComparer.OrdinalIgnoreCase);
            var existingSnippetPairs = new HashSet<string>(
                (existingSnippets ?? Enumerable.Empty<SnippetEntry>())
                    .Where(entry => !string.IsNullOrWhiteSpace(entry.Trigger) && !string.IsNullOrWhiteSpace(entry.Replacement))
                    .Select(entry => BuildKey(entry.Trigger, entry.Replacement)),
                StringComparer.OrdinalIgnoreCase);

            AddDictionarySuggestions(normalizedSource, normalizedRefined, knownTerms, suggestions, seen, maxSuggestions);
            AddSnippetSuggestion(normalizedSource, normalizedRefined, existingSnippetPairs, suggestions, seen, maxSuggestions);

            return suggestions;
        }

        private static void AddDictionarySuggestions(
            string sourceText,
            string refinedText,
            HashSet<string> knownTerms,
            List<CorrectionSuggestionEntry> suggestions,
            HashSet<string> seen,
            int maxSuggestions)
        {
            var sourceTokens = TokenRegex.Matches(sourceText).Select(match => match.Value).ToList();
            var refinedTokens = TokenRegex.Matches(refinedText).Select(match => match.Value).ToList();
            if (sourceTokens.Count == 0 || sourceTokens.Count != refinedTokens.Count)
            {
                return;
            }

            for (int index = 0; index < sourceTokens.Count; index++)
            {
                if (suggestions.Count >= Math.Max(1, maxSuggestions))
                {
                    return;
                }

                var sourceToken = sourceTokens[index];
                var refinedToken = refinedTokens[index];
                if (string.Equals(sourceToken, refinedToken, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!LooksDictionaryWorthy(sourceToken, refinedToken))
                {
                    continue;
                }

                if (knownTerms.Contains(refinedToken))
                {
                    continue;
                }

                var key = BuildKey(sourceToken, refinedToken);
                if (!seen.Add(key))
                {
                    continue;
                }

                suggestions.Add(new CorrectionSuggestionEntry
                {
                    SourceText = sourceToken,
                    SuggestedText = refinedToken,
                    SuggestionType = "Dictionary",
                    Reason = "Refinement corrected a likely term or product name."
                });
            }
        }

        private static void AddSnippetSuggestion(
            string sourceText,
            string refinedText,
            HashSet<string> existingSnippetPairs,
            List<CorrectionSuggestionEntry> suggestions,
            HashSet<string> seen,
            int maxSuggestions)
        {
            if (suggestions.Count >= Math.Max(1, maxSuggestions))
            {
                return;
            }

            if (!LooksSnippetWorthy(sourceText, refinedText))
            {
                return;
            }

            var key = BuildKey(sourceText, refinedText);
            if (existingSnippetPairs.Contains(key) || !seen.Add(key))
            {
                return;
            }

            suggestions.Add(new CorrectionSuggestionEntry
            {
                SourceText = sourceText,
                SuggestedText = refinedText,
                SuggestionType = "Snippet",
                Reason = "Save this spoken phrase as a reusable expansion."
            });
        }

        private static bool LooksDictionaryWorthy(string sourceToken, string refinedToken)
        {
            bool hasDigit = refinedToken.Any(char.IsDigit);
            bool hasConnector = refinedToken.Any(ch => ch is '-' or '\'' or '+' or '_' or '.');
            bool hasInternalUpper = refinedToken.Skip(1).Any(char.IsUpper);
            bool allUpper = refinedToken.Length > 1 && refinedToken.All(ch => !char.IsLetter(ch) || char.IsUpper(ch));
            bool spellingChanged = !string.Equals(sourceToken, refinedToken, StringComparison.OrdinalIgnoreCase);

            return spellingChanged || hasDigit || hasConnector || hasInternalUpper || allUpper;
        }

        private static bool LooksSnippetWorthy(string sourceText, string refinedText)
        {
            if (sourceText.Contains(Environment.NewLine, StringComparison.Ordinal) ||
                refinedText.Contains(Environment.NewLine, StringComparison.Ordinal))
            {
                return false;
            }

            int sourceWordCount = CountWords(sourceText);
            int refinedWordCount = CountWords(refinedText);
            if (sourceWordCount == 0 || refinedWordCount == 0 || sourceWordCount > 8 || refinedWordCount > 8)
            {
                return false;
            }

            if (sourceText.Length > 120 || refinedText.Length > 140)
            {
                return false;
            }

            bool punctuationChanged = Regex.Replace(sourceText, @"[\p{L}\p{M}\p{N}\s]", string.Empty)
                != Regex.Replace(refinedText, @"[\p{L}\p{M}\p{N}\s]", string.Empty);
            bool structureChanged = sourceWordCount != refinedWordCount;
            bool casingOnly = string.Equals(sourceText, refinedText, StringComparison.OrdinalIgnoreCase);

            return !casingOnly && (punctuationChanged || structureChanged);
        }

        private static int CountWords(string value)
        {
            return TokenRegex.Matches(value).Count;
        }

        private static string BuildKey(string source, string target)
        {
            return $"{source.Trim()} => {target.Trim()}";
        }
    }
}
