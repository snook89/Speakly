using System;

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

        public static string CoerceToEditOnlyOutput(string originalText, string? refinedText)
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

        private static string NormalizeWhitespace(string value)
        {
            return string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
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
