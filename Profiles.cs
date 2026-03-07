using System;
using System.Collections.Generic;
using System.Linq;
using Speakly.Services;

namespace Speakly.Config
{
    public class AppProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "Default";
        public List<string> ProcessNames { get; set; } = new();

        public string SttProvider { get; set; } = "Deepgram";
        public string SttModel { get; set; } = "nova-2";

        public bool RefinementEnabled { get; set; } = true;
        public string RefinementProvider { get; set; } = "OpenAI";
        public string RefinementModel { get; set; } = "gpt-4o-mini";
        public string RefinementPrompt { get; set; } = AppConfig.DefaultRefinementPrompt;
        public string PromptPresetName { get; set; } = string.Empty;
        public string DictationMode { get; set; } = DictationExperienceService.PlainDictationMode;
        public string StylePreset { get; set; } = DictationExperienceService.StylePresetNeutral;
        public string CustomStylePrompt { get; set; } = string.Empty;

        public string Language { get; set; } = "en";
        public bool CopyToClipboard { get; set; }
        public List<string> DictionaryTerms { get; set; } = new();
        public bool EnableVoiceCommands { get; set; } = true;
        public string VoiceCommandMode { get; set; } = DictationExperienceService.VoiceCommandModeMixed;
        public string ContextualRefinementMode { get; set; } = DictationExperienceService.ContextualRefinementModeAggressiveRewrite;
        public bool UseAppContextForRefinement { get; set; } = true;
        public bool UseWindowTitleContextForRefinement { get; set; }
        public bool UseSelectedTextContextForRefinement { get; set; }
        public bool UseClipboardContextForRefinement { get; set; }
        public bool EnableSnippets { get; set; } = true;
        public bool LearnFromRefinementCorrections { get; set; } = true;

        public bool EnableSttFailover { get; set; } = true;
        public List<string> SttFailoverOrder { get; set; } = new() { "Deepgram", "OpenAI", "OpenRouter" };
    }

    public static class ProfileHelpers
    {
        public static string NormalizeProcessName(string? processName)
        {
            if (string.IsNullOrWhiteSpace(processName)) return string.Empty;
            var normalized = processName.Trim().ToLowerInvariant();
            return normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? normalized[..^4]
                : normalized;
        }

        public static bool MatchesProcess(AppProfile profile, string processName)
        {
            var candidate = NormalizeProcessName(processName);
            if (string.IsNullOrWhiteSpace(candidate)) return false;
            return profile.ProcessNames.Any(p =>
                string.Equals(NormalizeProcessName(p), candidate, StringComparison.OrdinalIgnoreCase));
        }
    }
}
