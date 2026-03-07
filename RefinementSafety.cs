using System;
using System.Collections.Generic;
using System.Linq;

namespace Speakly.Services
{
    public static class RefinementSafety
    {
        private static readonly string[] AssistantFallbackMarkers =
        {
            "i don't understand",
            "i do not understand",
            "i understand that",
            "i can assist",
            "i'm doing well",
            "i am doing well",
            "thanks for asking",
            "how can i help",
            "functioning as expected",
            "please provide",
            "please clarify",
            "as an ai",
            "я не розумію",
            "я не можу",
            "я розумію, що",
            "будь ласка, надайте",
            "не бачу тексту",
            "не розумію, що ви",
            "чи маєте",
            "бажаєте",
            "готовий текст",
            "для редагування",
            "почати від початку",
            "не понимаю",
            "пожалуйста, предоставьте"
        };

        private static readonly string[] QuestionIntentMarkers =
        {
            "чи ",
            "can you",
            "would you",
            "do you want",
            "бажаєте",
            "маєте",
            "готовий текст",
            "please"
        };

        private static readonly string[] NegationMarkers =
        {
            "no",
            "not",
            "never",
            "none",
            "nothing",
            "nobody",
            "nowhere",
            "cannot",
            "can't",
            "won't",
            "don't",
            "doesn't",
            "didn't",
            "isn't",
            "aren't",
            "wasn't",
            "weren't",
            "shouldn't",
            "couldn't",
            "wouldn't"
        };

        private static readonly string[] TimeAnchors =
        {
            "today",
            "tomorrow",
            "tonight",
            "yesterday",
            "monday",
            "tuesday",
            "wednesday",
            "thursday",
            "friday",
            "saturday",
            "sunday",
            "morning",
            "afternoon",
            "evening"
        };

        private const string HardGuardPrompt =
            "Mandatory constraints for this refinement task:\n" +
            "- You are an editor, not a conversational assistant.\n" +
            "- Never ask questions, never provide help text, and never explain.\n" +
            "- Do not add disclaimers or meta commentary.\n" +
            "- Preserve all substantive content from the transcript; do not summarize, omit, or shorten it.\n" +
            "- If the transcript is unclear, noisy, or nonsensical, return the original input unchanged.\n" +
            "- Return only the final refined transcript text.";

        public static string BuildSafeSystemPrompt(string userPrompt)
        {
            var cleanedUserPrompt = string.IsNullOrWhiteSpace(userPrompt)
                ? string.Empty
                : userPrompt.Trim();

            if (string.IsNullOrWhiteSpace(cleanedUserPrompt))
            {
                return HardGuardPrompt;
            }

            return cleanedUserPrompt + "\n\n" + HardGuardPrompt;
        }

        public static string BuildRefinementUserMessage(string transcript)
        {
            return
                "Refine the following speech-to-text transcript according to the system rules. " +
                "Return ONLY the refined transcript text and nothing else.\n\n" +
                "Transcript:\n<<<\n" + transcript + "\n>>>";
        }

        public static string CoerceToEditOnlyOutput(string originalText, string? refinedText, bool aggressiveContextRewrite = false)
        {
            if (string.IsNullOrWhiteSpace(originalText))
            {
                return refinedText?.Trim() ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(refinedText))
            {
                return originalText;
            }

            var candidate = refinedText.Trim();
            var normalized = candidate.ToLowerInvariant();

            foreach (var marker in AssistantFallbackMarkers)
            {
                if (normalized.Contains(marker, StringComparison.Ordinal))
                {
                    return originalText;
                }
            }

            if (IsLikelyConversationalReply(originalText, candidate))
            {
                return originalText;
            }

            if (IsLikelyLossyShortening(originalText, candidate))
            {
                return originalText;
            }

            if (aggressiveContextRewrite && IsLikelyAggressiveContextDrift(originalText, candidate))
            {
                return originalText;
            }

            return candidate;
        }

        private static bool IsLikelyConversationalReply(string original, string candidate)
        {
            var normalizedCandidate = candidate.ToLowerInvariant();
            var normalizedOriginal = original.ToLowerInvariant();

            bool candidateAsksQuestion = normalizedCandidate.Contains('?');
            bool originalAsksQuestion = normalizedOriginal.Contains('?');

            if (candidateAsksQuestion && !originalAsksQuestion)
            {
                foreach (var marker in QuestionIntentMarkers)
                {
                    if (normalizedCandidate.Contains(marker, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            if (candidateAsksQuestion && original.Length > 0 && candidate.Length > original.Length * 2.2)
            {
                return true;
            }

            return false;
        }

        private static bool IsLikelyLossyShortening(string original, string candidate)
        {
            var originalNorm = NormalizeWhitespace(original);
            var candidateNorm = NormalizeWhitespace(candidate);

            if (string.IsNullOrWhiteSpace(originalNorm) || string.IsNullOrWhiteSpace(candidateNorm))
            {
                return false;
            }

            int originalWords = CountWords(originalNorm);
            int candidateWords = CountWords(candidateNorm);
            if (originalWords == 0 || candidateWords == 0)
            {
                return false;
            }

            double wordRatio = (double)candidateWords / originalWords;
            double charRatio = (double)candidateNorm.Length / originalNorm.Length;

            // Strong signal: response is dramatically shorter than input.
            if (originalWords >= 40 && wordRatio <= 0.55)
            {
                return true;
            }

            if (originalNorm.Length >= 320 && charRatio <= 0.50)
            {
                return true;
            }

            // Likely cut-off output: notably shorter and no sentence terminator.
            if (originalWords >= 30 && wordRatio <= 0.70 && !EndsWithSentenceTerminator(candidateNorm))
            {
                return true;
            }

            return false;
        }

        private static bool IsLikelyAggressiveContextDrift(string original, string candidate)
        {
            var originalTokens = TokenizeWords(original);
            var candidateTokens = TokenizeWords(candidate);
            if (originalTokens.Count < 3 || candidateTokens.Count == 0)
            {
                return false;
            }

            var originalDistinct = new HashSet<string>(originalTokens, StringComparer.OrdinalIgnoreCase);
            var candidateDistinct = new HashSet<string>(candidateTokens, StringComparer.OrdinalIgnoreCase);
            int overlap = originalDistinct.Count(token => candidateDistinct.Contains(token));
            double retainedOriginal = originalDistinct.Count == 0 ? 0 : (double)overlap / originalDistinct.Count;
            double newCandidateContent = candidateDistinct.Count == 0 ? 0 : (double)(candidateDistinct.Count - overlap) / candidateDistinct.Count;

            if (candidateTokens.Count >= Math.Max(originalTokens.Count + 3, (int)Math.Ceiling(originalTokens.Count * 1.5)) &&
                retainedOriginal < 0.45 &&
                newCandidateContent > 0.55)
            {
                return true;
            }

            if (retainedOriginal < 0.34 && newCandidateContent > 0.60)
            {
                return true;
            }

            if (HasNegationShift(originalDistinct, candidateDistinct))
            {
                return true;
            }

            if (HasTimeAnchorShift(originalDistinct, candidateDistinct))
            {
                return true;
            }

            return false;
        }

        private static bool HasNegationShift(HashSet<string> originalDistinct, HashSet<string> candidateDistinct)
        {
            bool originalHasNegation = NegationMarkers.Any(originalDistinct.Contains);
            bool candidateHasNegation = NegationMarkers.Any(candidateDistinct.Contains);
            return originalHasNegation != candidateHasNegation;
        }

        private static bool HasTimeAnchorShift(HashSet<string> originalDistinct, HashSet<string> candidateDistinct)
        {
            var originalAnchors = TimeAnchors.Where(originalDistinct.Contains).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var candidateAnchors = TimeAnchors.Where(candidateDistinct.Contains).ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (originalAnchors.Count == 0 || candidateAnchors.Count == 0)
            {
                return false;
            }

            return !originalAnchors.Overlaps(candidateAnchors);
        }

        private static string NormalizeWhitespace(string value)
        {
            return string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        }

        private static List<string> TokenizeWords(string value)
        {
            var tokens = new List<string>();
            if (string.IsNullOrWhiteSpace(value))
            {
                return tokens;
            }

            foreach (var token in value
                         .ToLowerInvariant()
                         .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                var cleaned = new string(token.Where(char.IsLetterOrDigit).ToArray());
                if (!string.IsNullOrWhiteSpace(cleaned))
                {
                    tokens.Add(cleaned);
                }
            }

            return tokens;
        }

        private static int CountWords(string value)
        {
            return value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        }

        private static bool EndsWithSentenceTerminator(string value)
        {
            for (int i = value.Length - 1; i >= 0; i--)
            {
                var ch = value[i];
                if (char.IsWhiteSpace(ch) || ch == '"' || ch == '\'' || ch == ')' || ch == ']')
                {
                    continue;
                }

                return ch == '.' || ch == '!' || ch == '?' || ch == ':' || ch == ';';
            }

            return false;
        }
    }
}
