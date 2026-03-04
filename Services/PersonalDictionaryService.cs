using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Speakly.Config;

namespace Speakly.Services
{
    public static class PersonalDictionaryService
    {
        private static readonly Regex CandidateTokenRegex =
            new Regex(@"\b[\p{L}][\p{L}\p{M}\p{N}_'\-]{2,}\b", RegexOptions.Compiled);

        public static List<string> ParseTerms(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return new List<string>();
            }

            return input
                .Split(new[] { ',', ';', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(NormalizeTerm)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static string SerializeTerms(IEnumerable<string>? terms)
        {
            if (terms == null)
            {
                return string.Empty;
            }

            return string.Join(Environment.NewLine, terms
                .Select(NormalizeTerm)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase));
        }

        public static IReadOnlyList<string> GetCombinedTermsForActiveProfile(AppConfig config, int maxTerms = 64)
        {
            var active = ConfigManager.GetActiveProfile();
            return GetCombinedTerms(config, active, maxTerms);
        }

        public static IReadOnlyList<string> GetCombinedTerms(AppConfig config, AppProfile? profile, int maxTerms = 64)
        {
            var list = new List<string>();
            if (config.PersonalDictionaryGlobal != null)
            {
                list.AddRange(config.PersonalDictionaryGlobal);
            }

            if (profile?.DictionaryTerms != null)
            {
                list.AddRange(profile.DictionaryTerms);
            }

            return list
                .Select(NormalizeTerm)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(1, maxTerms))
                .ToList();
        }

        public static string BuildSttHintPrompt(AppConfig config, AppProfile? profile = null, int maxTerms = 40)
        {
            var terms = GetCombinedTerms(config, profile ?? ConfigManager.GetActiveProfile(), maxTerms);
            if (terms.Count == 0)
            {
                return string.Empty;
            }

            return $"Preferred vocabulary (keep exact spelling/casing): {string.Join(", ", terms)}";
        }

        public static string ApplyCorrections(string text, IEnumerable<string>? terms, out int replacements)
        {
            replacements = 0;
            if (string.IsNullOrWhiteSpace(text) || terms == null)
            {
                return text;
            }

            string result = text;
            int replacementCounter = 0;
            foreach (var term in terms
                .Select(NormalizeTerm)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(t => t.Length))
            {
                var pattern = $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(term)}(?![\p{{L}}\p{{N}}_])";
                result = Regex.Replace(result, pattern, match =>
                {
                    if (string.Equals(match.Value, term, StringComparison.Ordinal))
                    {
                        return match.Value;
                    }

                    replacementCounter++;
                    return term;
                }, RegexOptions.IgnoreCase);
            }

            replacements = replacementCounter;
            return result;
        }

        public static IReadOnlyList<string> ExtractCandidateTerms(
            string? transcript,
            IEnumerable<string>? knownTerms,
            int maxCandidates = 12)
        {
            if (string.IsNullOrWhiteSpace(transcript))
            {
                return Array.Empty<string>();
            }

            var known = new HashSet<string>(
                (knownTerms ?? Enumerable.Empty<string>())
                    .Select(NormalizeTerm)
                    .Where(t => !string.IsNullOrWhiteSpace(t)),
                StringComparer.OrdinalIgnoreCase);

            var candidates = new List<string>();
            foreach (Match match in CandidateTokenRegex.Matches(transcript))
            {
                var token = NormalizeTerm(match.Value);
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                if (known.Contains(token))
                {
                    continue;
                }

                bool hasUpper = token.Any(char.IsUpper);
                bool hasDigit = token.Any(char.IsDigit);
                bool hasPunctuation = token.Contains('-') || token.Contains('\'');
                if (!hasUpper && !hasDigit && !hasPunctuation)
                {
                    continue;
                }

                if (candidates.Any(x => string.Equals(x, token, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                candidates.Add(token);
                if (candidates.Count >= Math.Max(1, maxCandidates))
                {
                    break;
                }
            }

            return candidates;
        }

        private static string NormalizeTerm(string? value)
        {
            return (value ?? string.Empty).Trim();
        }
    }
}
