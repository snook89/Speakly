using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Speakly.Config;

namespace Speakly.Services
{
    public enum VoiceCommandKind
    {
        None,
        DeleteThat,
        ScratchThat,
        UndoThat,
        SelectThat,
        Backspace,
        PressEnter,
        Tab,
        InsertSpace
    }

    public sealed class VoiceCommandMatch
    {
        public static VoiceCommandMatch None { get; } = new VoiceCommandMatch();

        public bool IsMatch { get; init; }
        public bool SuppressTranscript { get; init; }
        public VoiceCommandKind Kind { get; init; }
        public string DisplayName { get; init; } = string.Empty;
        public string SpokenPhrase { get; init; } = string.Empty;
    }

    public static class DictationExperienceService
    {
        public const string PlainDictationMode = "Plain Dictation";
        public const string MessageMode = "Message";
        public const string EmailMode = "Email";
        public const string NotesMode = "Notes";
        public const string CodeMode = "Code";
        public const string CustomMode = "Custom";
        public const string StylePresetNeutral = "Neutral";
        public const string StylePresetCasual = "Casual";
        public const string StylePresetFormal = "Formal";
        public const string StylePresetCustom = "Custom";

        public const string VoiceCommandModeMixed = "Mixed";
        public const string VoiceCommandModeDictationOnly = "Dictation only";
        public const string VoiceCommandModeCommandsOnly = "Commands only";
        public const string ContextualRefinementModeConservative = "Conservative";
        public const string ContextualRefinementModeAggressiveRewrite = "Aggressive Context Rewrite";

        private static readonly IReadOnlyList<string> SupportedModes = new[]
        {
            PlainDictationMode,
            MessageMode,
            EmailMode,
            NotesMode,
            CodeMode,
            CustomMode
        };

        private static readonly IReadOnlyList<string> SupportedVoiceCommandModes = new[]
        {
            VoiceCommandModeMixed,
            VoiceCommandModeDictationOnly,
            VoiceCommandModeCommandsOnly
        };

        private static readonly IReadOnlyList<string> SupportedContextualRefinementModes = new[]
        {
            ContextualRefinementModeConservative,
            ContextualRefinementModeAggressiveRewrite
        };

        private static readonly IReadOnlyList<string> SupportedStylePresets = new[]
        {
            StylePresetNeutral,
            StylePresetCasual,
            StylePresetFormal,
            StylePresetCustom
        };

        private static readonly IReadOnlyDictionary<string, VoiceCommandMatch> VoiceCommands =
            new Dictionary<string, VoiceCommandMatch>(StringComparer.OrdinalIgnoreCase)
            {
                ["delete that"] = new VoiceCommandMatch { IsMatch = true, SuppressTranscript = true, Kind = VoiceCommandKind.DeleteThat, DisplayName = "Delete That", SpokenPhrase = "delete that" },
                ["scratch that"] = new VoiceCommandMatch { IsMatch = true, SuppressTranscript = true, Kind = VoiceCommandKind.ScratchThat, DisplayName = "Scratch That", SpokenPhrase = "scratch that" },
                ["undo that"] = new VoiceCommandMatch { IsMatch = true, SuppressTranscript = true, Kind = VoiceCommandKind.UndoThat, DisplayName = "Undo That", SpokenPhrase = "undo that" },
                ["select that"] = new VoiceCommandMatch { IsMatch = true, SuppressTranscript = true, Kind = VoiceCommandKind.SelectThat, DisplayName = "Select That", SpokenPhrase = "select that" },
                ["backspace"] = new VoiceCommandMatch { IsMatch = true, SuppressTranscript = true, Kind = VoiceCommandKind.Backspace, DisplayName = "Backspace", SpokenPhrase = "backspace" },
                ["press enter"] = new VoiceCommandMatch { IsMatch = true, SuppressTranscript = true, Kind = VoiceCommandKind.PressEnter, DisplayName = "Press Enter", SpokenPhrase = "press enter" },
                ["enter"] = new VoiceCommandMatch { IsMatch = true, SuppressTranscript = true, Kind = VoiceCommandKind.PressEnter, DisplayName = "Press Enter", SpokenPhrase = "enter" },
                ["tab"] = new VoiceCommandMatch { IsMatch = true, SuppressTranscript = true, Kind = VoiceCommandKind.Tab, DisplayName = "Tab", SpokenPhrase = "tab" },
                ["insert space"] = new VoiceCommandMatch { IsMatch = true, SuppressTranscript = true, Kind = VoiceCommandKind.InsertSpace, DisplayName = "Insert Space", SpokenPhrase = "insert space" },
                ["space"] = new VoiceCommandMatch { IsMatch = true, SuppressTranscript = true, Kind = VoiceCommandKind.InsertSpace, DisplayName = "Insert Space", SpokenPhrase = "space" }
            };

        public static IReadOnlyList<string> GetAvailableModes() => SupportedModes;

        public static IReadOnlyList<string> GetAvailableVoiceCommandModes() => SupportedVoiceCommandModes;

        public static IReadOnlyList<string> GetAvailableContextualRefinementModes() => SupportedContextualRefinementModes;

        public static IReadOnlyList<string> GetAvailableStylePresets() => SupportedStylePresets;

        public static string NormalizeMode(string? mode)
        {
            var normalized = mode?.Trim() ?? string.Empty;
            return SupportedModes.FirstOrDefault(candidate =>
                       string.Equals(candidate, normalized, StringComparison.OrdinalIgnoreCase))
                   ?? PlainDictationMode;
        }

        public static string GetNextMode(string? currentMode)
        {
            var normalized = NormalizeMode(currentMode);
            var index = SupportedModes
                .Select((mode, position) => new { mode, position })
                .FirstOrDefault(entry => string.Equals(entry.mode, normalized, StringComparison.OrdinalIgnoreCase))
                ?.position ?? 0;

            return SupportedModes[(index + 1) % SupportedModes.Count];
        }

        public static string DescribeMode(string? mode)
        {
            return NormalizeMode(mode) switch
            {
                MessageMode => "Short conversational replies and chat-style cleanup.",
                EmailMode => "Structured email output with cleaner punctuation and paragraphs.",
                NotesMode => "Structured notes with headings, bullets, and compact phrasing.",
                CodeMode => "Literal handling for identifiers, symbols, paths, and technical wording.",
                CustomMode => "Uses your custom prompt as the primary instruction set.",
                _ => "General dictation cleanup without a task-specific format bias."
            };
        }

        public static string NormalizeStylePreset(string? preset)
        {
            var normalized = preset?.Trim() ?? string.Empty;
            return SupportedStylePresets.FirstOrDefault(candidate =>
                       string.Equals(candidate, normalized, StringComparison.OrdinalIgnoreCase))
                   ?? StylePresetNeutral;
        }

        public static string DescribeStylePreset(string? preset)
        {
            return NormalizeStylePreset(preset) switch
            {
                StylePresetCasual => "Natural, relaxed phrasing with lighter formality.",
                StylePresetFormal => "Polished, professional phrasing with stronger formality.",
                StylePresetCustom => "Uses your custom style instructions in addition to the selected mode.",
                _ => "Balanced default tone with no extra style bias."
            };
        }

        public static string NormalizeVoiceCommandMode(string? mode)
        {
            var normalized = mode?.Trim() ?? string.Empty;
            return SupportedVoiceCommandModes.FirstOrDefault(candidate =>
                       string.Equals(candidate, normalized, StringComparison.OrdinalIgnoreCase))
                   ?? VoiceCommandModeMixed;
        }

        public static string NormalizeContextualRefinementMode(string? mode)
        {
            var normalized = mode?.Trim() ?? string.Empty;
            return SupportedContextualRefinementModes.FirstOrDefault(candidate =>
                       string.Equals(candidate, normalized, StringComparison.OrdinalIgnoreCase))
                   ?? ContextualRefinementModeAggressiveRewrite;
        }

        public static string DescribeContextUsage(AppConfig config)
        {
            var parts = new List<string>();
            if (config.UseAppContextForRefinement) parts.Add("App");
            if (config.UseWindowTitleContextForRefinement) parts.Add("Window");
            if (config.UseSelectedTextContextForRefinement) parts.Add("Selected Text");
            if (config.UseClipboardContextForRefinement) parts.Add("Clipboard");
            return parts.Count == 0 ? "Context: Off" : $"Context: {string.Join(" + ", parts)}";
        }

        public static string DescribeContextualRefinementMode(string? mode)
        {
            var normalized = NormalizeContextualRefinementMode(mode);
            return normalized == ContextualRefinementModeAggressiveRewrite
                ? "Context Mode: Aggressive"
                : "Context Mode: Conservative";
        }

        public static string DescribeStyleSummary(string? preset, string? customStylePrompt = null)
        {
            return $"Style: {NormalizeStylePreset(preset)}";
        }

        public static string GetPromptStyleConflictWarning(string? basePrompt, string? stylePreset, string? customStylePrompt = null)
        {
            var normalizedStyle = NormalizeStylePreset(stylePreset);
            if (normalizedStyle == StylePresetNeutral)
            {
                return string.Empty;
            }

            var promptTone = DetectPromptTone(basePrompt);
            if (normalizedStyle == StylePresetFormal && promptTone == "casual")
            {
                return "Your base prompt looks casual, but the Style preset is Formal. Speakly will prioritize the style preset for tone.";
            }

            if (normalizedStyle == StylePresetCasual && promptTone == "formal")
            {
                return "Your base prompt looks formal/professional, but the Style preset is Casual. Speakly will prioritize the style preset for tone.";
            }

            if (normalizedStyle == StylePresetCustom &&
                !string.IsNullOrWhiteSpace(customStylePrompt) &&
                !string.IsNullOrWhiteSpace(promptTone))
            {
                return "Your base prompt already appears to contain tone instructions. If they disagree with your Custom style text, Speakly will prioritize the style preset for tone.";
            }

            return string.Empty;
        }

        public static string BuildEffectivePrompt(
            AppConfig config,
            AppProfile? profile,
            TargetWindowContext targetContext,
            RefinementContextSnapshot? supplementalContext,
            out string contextSummary)
        {
            var basePrompt = profile?.RefinementPrompt;
            if (string.IsNullOrWhiteSpace(basePrompt))
            {
                basePrompt = config.RefinementPrompt;
            }

            var mode = NormalizeMode(profile?.DictationMode ?? config.DictationMode);
            var contextualRefinementMode = NormalizeContextualRefinementMode(
                profile?.ContextualRefinementMode ?? config.ContextualRefinementMode);
            var stylePreset = NormalizeStylePreset(profile?.StylePreset ?? config.StylePreset);
            var customStylePrompt = (profile?.CustomStylePrompt ?? config.CustomStylePrompt ?? string.Empty).Trim();
            var builder = new StringBuilder(basePrompt?.Trim() ?? AppConfig.DefaultRefinementPrompt);

            var modeInstruction = BuildModeInstruction(mode);
            if (!string.IsNullOrWhiteSpace(modeInstruction))
            {
                builder.AppendLine();
                builder.AppendLine();
                builder.AppendLine("Mode-specific instructions:");
                builder.Append(modeInstruction);
            }

            var styleInstruction = BuildStyleInstruction(stylePreset, customStylePrompt);
            if (!string.IsNullOrWhiteSpace(styleInstruction))
            {
                builder.AppendLine();
                builder.AppendLine();
                builder.AppendLine("Style precedence:");
                builder.AppendLine("- If the base prompt above contains tone instructions that conflict with the selected style preset, follow the style preset for tone and keep the base prompt for all other behavior.");
                builder.AppendLine();
                builder.AppendLine("Style instructions:");
                builder.Append(styleInstruction);
            }

            contextSummary = BuildContextSummary(config, targetContext, supplementalContext);
            if (!string.IsNullOrWhiteSpace(contextSummary))
            {
                builder.AppendLine();
                builder.AppendLine();
                AppendContextGuidance(builder, config, supplementalContext, contextualRefinementMode);
                builder.AppendLine();
                builder.AppendLine("Current context:");
                AppendContextSection(builder, config, targetContext, supplementalContext);
            }

            return builder.ToString();
        }

        public static string BuildEffectivePrompt(AppConfig config, AppProfile? profile, TargetWindowContext targetContext, out string contextSummary)
        {
            return BuildEffectivePrompt(config, profile, targetContext, null, out contextSummary);
        }

        public static string BuildContextSummary(AppConfig config, TargetWindowContext targetContext, RefinementContextSnapshot? supplementalContext = null)
        {
            var parts = new List<string>();
            if (config.UseAppContextForRefinement && !string.IsNullOrWhiteSpace(targetContext.ProcessName))
            {
                parts.Add($"App: {ProfileHelpers.NormalizeProcessName(targetContext.ProcessName)}");
            }

            if (config.UseWindowTitleContextForRefinement && !string.IsNullOrWhiteSpace(targetContext.WindowTitle))
            {
                parts.Add($"Window: {targetContext.WindowTitle.Trim()}");
            }

            if (config.UseSelectedTextContextForRefinement && supplementalContext?.HasSelectedText == true)
            {
                parts.Add($"Selected text: {BuildInlineSummary(supplementalContext.SelectedText)}");
            }

            if (config.UseClipboardContextForRefinement && supplementalContext?.HasClipboardText == true)
            {
                parts.Add($"Clipboard: {BuildInlineSummary(supplementalContext.ClipboardText)}");
            }

            return string.Join(" | ", parts);
        }

        public static VoiceCommandMatch MatchVoiceCommand(string transcript, bool enabled, string commandMode)
        {
            var normalizedMode = NormalizeVoiceCommandMode(commandMode);
            if (!enabled || string.Equals(normalizedMode, VoiceCommandModeDictationOnly, StringComparison.OrdinalIgnoreCase))
            {
                return VoiceCommandMatch.None;
            }

            var normalizedTranscript = NormalizeCommandTranscript(transcript);
            if (string.IsNullOrWhiteSpace(normalizedTranscript))
            {
                return VoiceCommandMatch.None;
            }

            if (VoiceCommands.TryGetValue(normalizedTranscript, out var match))
            {
                return match;
            }

            return string.Equals(normalizedMode, VoiceCommandModeCommandsOnly, StringComparison.OrdinalIgnoreCase)
                ? new VoiceCommandMatch
                {
                    IsMatch = false,
                    SuppressTranscript = true,
                    DisplayName = "No command recognized",
                    SpokenPhrase = transcript?.Trim() ?? string.Empty
                }
                : VoiceCommandMatch.None;
        }

        private static string BuildModeInstruction(string mode)
        {
            return NormalizeMode(mode) switch
            {
                MessageMode => "Format the result like a short natural message. Keep it concise and conversational. Avoid greetings or sign-offs unless they are clearly dictated.",
                EmailMode => "Format the result like a clean professional email. Preserve paragraphs, capitalization, and punctuation. Do not invent a subject line unless the user dictates one.",
                NotesMode => "Format the result like structured notes. Preserve headings, bullet-like phrasing, and line breaks when strongly implied by the transcript.",
                CodeMode => "Preserve technical terms, symbols, code identifiers, file paths, and formatting literally whenever possible. Prefer exactness over smoothing or paraphrasing.",
                CustomMode => "Respect the user's custom prompt above as the primary instruction set.",
                _ => "Prefer direct dictation cleanup only. Preserve wording closely and avoid stylistic rewrites beyond grammar, punctuation, and obvious transcription fixes."
            };
        }

        private static string BuildStyleInstruction(string stylePreset, string customStylePrompt)
        {
            return NormalizeStylePreset(stylePreset) switch
            {
                StylePresetCasual => "Keep the tone natural, warm, and conversational. Prefer simpler phrasing and avoid sounding stiff or overly corporate unless the user dictates it.",
                StylePresetFormal => "Use polished, professional phrasing. Prefer complete sentences, cleaner transitions, and a more formal tone without adding new content.",
                StylePresetCustom when !string.IsNullOrWhiteSpace(customStylePrompt) =>
                    $"Apply this style preference when refining the text: {customStylePrompt}",
                StylePresetCustom => "Use the user's custom style preference when provided, but do not invent one if it is blank.",
                _ => string.Empty
            };
        }

        private static string DetectPromptTone(string? prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return string.Empty;
            }

            var text = prompt.Trim();

            bool formal = ContainsAny(text,
                "professional tone",
                "formal tone",
                "professional style",
                "formal style",
                "polished tone",
                "businesslike",
                "professional phrasing",
                "formal phrasing");

            bool casual = ContainsAny(text,
                "casual tone",
                "conversational tone",
                "friendly tone",
                "relaxed tone",
                "warm tone",
                "informal tone",
                "casual style",
                "conversational style");

            if (formal == casual)
            {
                return string.Empty;
            }

            return formal ? "formal" : "casual";
        }

        private static bool ContainsAny(string text, params string[] values)
        {
            return values.Any(value => text.Contains(value, StringComparison.OrdinalIgnoreCase));
        }

        private static void AppendContextSection(
            StringBuilder builder,
            AppConfig config,
            TargetWindowContext targetContext,
            RefinementContextSnapshot? supplementalContext)
        {
            if (config.UseAppContextForRefinement && !string.IsNullOrWhiteSpace(targetContext.ProcessName))
            {
                builder.Append("- App: ");
                builder.AppendLine(ProfileHelpers.NormalizeProcessName(targetContext.ProcessName));
            }

            if (config.UseWindowTitleContextForRefinement && !string.IsNullOrWhiteSpace(targetContext.WindowTitle))
            {
                builder.Append("- Window: ");
                builder.AppendLine(targetContext.WindowTitle.Trim());
            }

            if (config.UseSelectedTextContextForRefinement && supplementalContext?.HasSelectedText == true)
            {
                AppendMultilineContextBlock(builder, "Selected text", supplementalContext.SelectedText);
            }

            if (config.UseClipboardContextForRefinement && supplementalContext?.HasClipboardText == true)
            {
                AppendMultilineContextBlock(builder, "Clipboard text", supplementalContext.ClipboardText);
            }
        }

        private static void AppendContextGuidance(
            StringBuilder builder,
            AppConfig config,
            RefinementContextSnapshot? supplementalContext,
            string contextualRefinementMode)
        {
            var normalizedMode = NormalizeContextualRefinementMode(contextualRefinementMode);
            bool hasEnabledStrongContext =
                (config.UseSelectedTextContextForRefinement && supplementalContext?.HasSelectedText == true) ||
                (config.UseClipboardContextForRefinement && supplementalContext?.HasClipboardText == true);

            builder.AppendLine("How to use context:");
            builder.AppendLine("- Use the current context to resolve ambiguous wording, references, and likely speech-recognition mistakes.");
            if (normalizedMode == ContextualRefinementModeAggressiveRewrite && hasEnabledStrongContext)
            {
                builder.AppendLine("- Keep the user's communicative intent, but you may rewrite shorthand or vague dictation into a fuller standalone sentence when the selected or clipboard context makes the intended meaning clear.");
            }
            else
            {
                builder.AppendLine("- Keep the user's intended meaning aligned with strong nearby context, but do not rewrite the transcript into a different message unless the context clearly indicates that the transcript meaning is wrong.");
            }

            if (config.UseAppContextForRefinement)
            {
                builder.AppendLine("- App context is a weak hint about domain and tone only.");
            }

            if (config.UseWindowTitleContextForRefinement)
            {
                builder.AppendLine("- Window title context is a weak hint about the active task or document.");
            }

            if (config.UseSelectedTextContextForRefinement && supplementalContext?.HasSelectedText == true)
            {
                builder.AppendLine("- Selected text is the strongest local context. Assume the user may be editing, continuing, or replying to that exact text. If the transcript conflicts with it due to likely recognition mistakes, prefer semantic consistency with the selected text.");
            }

            if (config.UseClipboardContextForRefinement && supplementalContext?.HasClipboardText == true)
            {
                builder.AppendLine("- Clipboard text is secondary context. Use it to maintain topic, terminology, and polarity when the dictated transcript is ambiguous or appears inconsistent.");
            }

            if (normalizedMode == ContextualRefinementModeAggressiveRewrite &&
                hasEnabledStrongContext)
            {
                builder.AppendLine("- Aggressive contextual rewrite mode is enabled.");
                builder.AppendLine("- In this mode, expand vague references such as it, this, that, there, he, she, they, or was moved into explicit wording grounded in the selected or clipboard text when confidence is high.");
                builder.AppendLine("- If the dictated transcript sounds like a short reply, correction, or follow-up to the selected or clipboard text, rewrite it into a clear standalone sentence that carries forward the concrete subject, action, timing, and polarity from that context.");
                builder.AppendLine("- Prefer making implicit references explicit over preserving shorthand wording.");
            }
        }

        private static void AppendMultilineContextBlock(StringBuilder builder, string label, string value)
        {
            builder.Append("- ");
            builder.Append(label);
            builder.AppendLine(":");

            foreach (var line in value.Replace("\r\n", "\n").Split('\n'))
            {
                builder.Append("  ");
                builder.AppendLine(line);
            }
        }

        private static string BuildInlineSummary(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = value.Replace("\r\n", " ").Replace('\n', ' ').Trim();
            return normalized.Length <= 90
                ? normalized
                : normalized[..90].TrimEnd() + "...";
        }

        private static string NormalizeCommandTranscript(string? transcript)
        {
            if (string.IsNullOrWhiteSpace(transcript))
            {
                return string.Empty;
            }

            var trimmed = transcript.Trim().ToLowerInvariant();
            var builder = new StringBuilder(trimmed.Length);
            bool previousWasSpace = false;
            foreach (var ch in trimmed)
            {
                if (char.IsLetterOrDigit(ch))
                {
                    builder.Append(ch);
                    previousWasSpace = false;
                    continue;
                }

                if (char.IsWhiteSpace(ch))
                {
                    if (!previousWasSpace)
                    {
                        builder.Append(' ');
                        previousWasSpace = true;
                    }
                }
            }

            return builder.ToString().Trim();
        }
    }
}
