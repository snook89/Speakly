using System;

namespace Speakly.Services
{
    public static class RefinementSafety
    {
        private static readonly string[] AssistantFallbackMarkers =
        {
            "i don't understand",
            "i do not understand",
            "i can assist",
            "please provide",
            "please clarify",
            "as an ai",
            "я не розумію",
            "я не можу",
            "будь ласка, надайте",
            "не бачу тексту",
            "не розумію, що ви",
            "не понимаю",
            "пожалуйста, предоставьте"
        };

        private const string HardGuardPrompt =
            "Mandatory constraints for this refinement task:\n" +
            "- You are an editor, not a conversational assistant.\n" +
            "- Never ask questions, never provide help text, and never explain.\n" +
            "- Do not add disclaimers or meta commentary.\n" +
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

            return candidate;
        }
    }
}
