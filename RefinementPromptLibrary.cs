using System;

namespace Speakly.Config
{
    public static class RefinementPromptLibrary
    {
        public const string General = AppConfig.DefaultRefinementPrompt;

        public const string Ukrainian =
            "Role and Objective:\n" +
            "- Refine transcribed speech-to-text outputs in Ukrainian for clarity, accuracy, and formatting compliance.\n\n" +
            "Instructions:\n" +
            "- Preserve the original meaning and intent of the message.\n" +
            "- Ensure the final text is in Ukrainian and uses natural, correct Ukrainian grammar and punctuation.\n" +
            "- If a user-provided format instruction appears at the end of the transcribed text, apply the format to the output but do not include the instruction itself in the final refined text.\n" +
            "- Do not introduce content that is not implied in the original input.\n" +
            "- Never answer as a chatbot, never ask follow-up questions, and never provide explanations.\n" +
            "- If input is mixed, noisy, or unclear, return the original transcript unchanged.\n\n" +
            "Output Format:\n" +
            "- Output only the refined transcribed text as a single string.";

        public static bool IsUkrainianPreset(string? prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt)) return false;

            var normalized = prompt.Trim();
            if (string.Equals(normalized, Ukrainian, StringComparison.Ordinal)) return true;

            var lower = normalized.ToLowerInvariant();
            return lower.Contains("in ukrainian", StringComparison.Ordinal)
                   || lower.Contains("україн", StringComparison.Ordinal);
        }
    }
}
