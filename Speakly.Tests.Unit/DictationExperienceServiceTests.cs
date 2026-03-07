using Speakly.Config;
using Speakly.Services;
using Xunit;

namespace Speakly.Tests.Unit
{
    public class DictationExperienceServiceTests
    {
        [Theory]
        [InlineData("message", DictationExperienceService.MessageMode)]
        [InlineData("EMAIL", DictationExperienceService.EmailMode)]
        [InlineData("", DictationExperienceService.PlainDictationMode)]
        [InlineData("unknown", DictationExperienceService.PlainDictationMode)]
        public void NormalizeMode_ReturnsSupportedValue(string input, string expected)
        {
            Assert.Equal(expected, DictationExperienceService.NormalizeMode(input));
        }

        [Fact]
        public void GetNextMode_CyclesThroughSupportedModes()
        {
            Assert.Equal(
                DictationExperienceService.MessageMode,
                DictationExperienceService.GetNextMode(DictationExperienceService.PlainDictationMode));
            Assert.Equal(
                DictationExperienceService.PlainDictationMode,
                DictationExperienceService.GetNextMode(DictationExperienceService.CustomMode));
        }

        [Fact]
        public void DescribeMode_ReturnsModeSpecificGuidance()
        {
            Assert.Contains("chat-style", DictationExperienceService.DescribeMode(DictationExperienceService.MessageMode));
            Assert.Contains("custom prompt", DictationExperienceService.DescribeMode(DictationExperienceService.CustomMode));
        }

        [Theory]
        [InlineData("casual", DictationExperienceService.StylePresetCasual)]
        [InlineData("FORMAL", DictationExperienceService.StylePresetFormal)]
        [InlineData("", DictationExperienceService.StylePresetNeutral)]
        [InlineData("unknown", DictationExperienceService.StylePresetNeutral)]
        public void NormalizeStylePreset_ReturnsSupportedValue(string input, string expected)
        {
            Assert.Equal(expected, DictationExperienceService.NormalizeStylePreset(input));
        }

        [Theory]
        [InlineData("mixed", DictationExperienceService.VoiceCommandModeMixed)]
        [InlineData("commands only", DictationExperienceService.VoiceCommandModeCommandsOnly)]
        [InlineData("", DictationExperienceService.VoiceCommandModeMixed)]
        public void NormalizeVoiceCommandMode_ReturnsSupportedValue(string input, string expected)
        {
            Assert.Equal(expected, DictationExperienceService.NormalizeVoiceCommandMode(input));
        }

        [Theory]
        [InlineData("conservative", DictationExperienceService.ContextualRefinementModeConservative)]
        [InlineData("Aggressive Context Rewrite", DictationExperienceService.ContextualRefinementModeAggressiveRewrite)]
        [InlineData("", DictationExperienceService.ContextualRefinementModeAggressiveRewrite)]
        public void NormalizeContextualRefinementMode_ReturnsSupportedValue(string input, string expected)
        {
            Assert.Equal(expected, DictationExperienceService.NormalizeContextualRefinementMode(input));
        }

        [Fact]
        public void BuildEffectivePrompt_IncludesModeInstructionAndContext()
        {
            var config = new AppConfig
            {
                DictationMode = DictationExperienceService.CodeMode,
                ContextualRefinementMode = DictationExperienceService.ContextualRefinementModeAggressiveRewrite,
                UseAppContextForRefinement = true,
                UseWindowTitleContextForRefinement = true,
                UseSelectedTextContextForRefinement = true,
                UseClipboardContextForRefinement = true
            };
            var profile = ConfigManager.BuildDefaultProfile(config);
            profile.DictationMode = DictationExperienceService.CodeMode;
            var target = new TargetWindowContext((System.IntPtr)123, 1, "Code", "main.cs - Visual Studio Code", System.DateTime.UtcNow);
            var supplementalContext = new RefinementContextSnapshot
            {
                SelectedText = "Refine this function name exactly as written.",
                ClipboardText = "Recent clipboard note"
            };

            var prompt = DictationExperienceService.BuildEffectivePrompt(config, profile, target, supplementalContext, out var contextSummary);

            Assert.Contains("Mode-specific instructions:", prompt);
            Assert.Contains("Preserve technical terms", prompt);
            Assert.Contains("How to use context:", prompt);
            Assert.Contains("Selected text is the strongest local context.", prompt);
            Assert.Contains("Clipboard text is secondary context.", prompt);
            Assert.Contains("Aggressive contextual rewrite mode is enabled.", prompt);
            Assert.Contains("expand vague references", prompt);
            Assert.Contains("you may rewrite shorthand or vague dictation into a fuller standalone sentence", prompt);
            Assert.Contains("Current context:", prompt);
            Assert.Contains("App: code", contextSummary);
            Assert.Contains("Window: main.cs - Visual Studio Code", contextSummary);
            Assert.Contains("Selected text: Refine this function name exactly as written.", contextSummary);
            Assert.Contains("Clipboard: Recent clipboard note", contextSummary);
            Assert.Contains("- Selected text:", prompt);
            Assert.Contains("Refine this function name exactly as written.", prompt);
            Assert.Contains("- Clipboard text:", prompt);
        }

        [Fact]
        public void BuildEffectivePrompt_OmitsDisabledSupplementalContext()
        {
            var config = new AppConfig
            {
                DictationMode = DictationExperienceService.PlainDictationMode,
                ContextualRefinementMode = DictationExperienceService.ContextualRefinementModeAggressiveRewrite,
                UseAppContextForRefinement = true,
                UseWindowTitleContextForRefinement = true,
                UseSelectedTextContextForRefinement = false,
                UseClipboardContextForRefinement = false
            };
            var target = new TargetWindowContext((System.IntPtr)321, 1, "notepad", "Notes", System.DateTime.UtcNow);
            var supplementalContext = new RefinementContextSnapshot
            {
                SelectedText = "Should not appear",
                ClipboardText = "Should also stay hidden"
            };

            var prompt = DictationExperienceService.BuildEffectivePrompt(config, null, target, supplementalContext, out var contextSummary);

            Assert.Contains("How to use context:", prompt);
            Assert.Contains("Current context:", prompt);
            Assert.Contains("App: notepad", contextSummary);
            Assert.Contains("Window: Notes", contextSummary);
            Assert.DoesNotContain("Selected text:", contextSummary);
            Assert.DoesNotContain("Clipboard:", contextSummary);
            Assert.DoesNotContain("Selected text is the strongest local context.", prompt);
            Assert.DoesNotContain("Clipboard text is secondary context.", prompt);
            Assert.DoesNotContain("Aggressive contextual rewrite mode is enabled.", prompt);
            Assert.DoesNotContain("Should not appear", prompt);
            Assert.DoesNotContain("Should also stay hidden", prompt);
        }

        [Fact]
        public void BuildEffectivePrompt_IncludesStylePresetInstructions()
        {
            var config = new AppConfig
            {
                DictationMode = DictationExperienceService.EmailMode,
                StylePreset = DictationExperienceService.StylePresetFormal
            };

            var prompt = DictationExperienceService.BuildEffectivePrompt(
                config,
                null,
                new TargetWindowContext((System.IntPtr)333, 1, "outlook", "Reply", System.DateTime.UtcNow),
                out _);

            Assert.Contains("Style instructions:", prompt);
            Assert.Contains("Style precedence:", prompt);
            Assert.Contains("polished, professional phrasing", prompt);
        }

        [Fact]
        public void BuildEffectivePrompt_UsesCustomStyleInstructionsWhenSelected()
        {
            var config = new AppConfig
            {
                DictationMode = DictationExperienceService.MessageMode,
                StylePreset = DictationExperienceService.StylePresetCustom,
                CustomStylePrompt = "Keep it crisp, friendly, and avoid exclamation marks."
            };

            var prompt = DictationExperienceService.BuildEffectivePrompt(
                config,
                null,
                new TargetWindowContext((System.IntPtr)444, 1, "notepad", "Draft", System.DateTime.UtcNow),
                out _);

            Assert.Contains("Style instructions:", prompt);
            Assert.Contains("Style precedence:", prompt);
            Assert.Contains("Keep it crisp, friendly, and avoid exclamation marks.", prompt);
        }

        [Fact]
        public void GetPromptStyleConflictWarning_DetectsFormalPromptAgainstCasualStyle()
        {
            var warning = DictationExperienceService.GetPromptStyleConflictWarning(
                "Use a professional tone and professional phrasing.",
                DictationExperienceService.StylePresetCasual);

            Assert.Contains("Style preset is Casual", warning);
            Assert.Contains("prioritize the style preset", warning);
        }

        [Fact]
        public void GetPromptStyleConflictWarning_DetectsCustomStyleAgainstPromptTone()
        {
            var warning = DictationExperienceService.GetPromptStyleConflictWarning(
                "Use a formal tone.",
                DictationExperienceService.StylePresetCustom,
                "Keep it relaxed and friendly.");

            Assert.Contains("base prompt already appears to contain tone instructions", warning);
        }

        [Fact]
        public void GetPromptStyleConflictWarning_ReturnsEmptyForNeutralStyle()
        {
            var warning = DictationExperienceService.GetPromptStyleConflictWarning(
                "Use a professional tone.",
                DictationExperienceService.StylePresetNeutral);

            Assert.Equal(string.Empty, warning);
        }

        [Fact]
        public void BuildEffectivePrompt_ConservativeModeSkipsAggressiveRewriteInstructions()
        {
            var config = new AppConfig
            {
                DictationMode = DictationExperienceService.PlainDictationMode,
                ContextualRefinementMode = DictationExperienceService.ContextualRefinementModeConservative,
                UseSelectedTextContextForRefinement = true
            };
            var supplementalContext = new RefinementContextSnapshot
            {
                SelectedText = "Can you send me the final invoice by Friday?"
            };
            var target = new TargetWindowContext((System.IntPtr)222, 1, "notepad", "Draft", System.DateTime.UtcNow);

            var prompt = DictationExperienceService.BuildEffectivePrompt(config, null, target, supplementalContext, out _);

            Assert.Contains("Selected text is the strongest local context.", prompt);
            Assert.DoesNotContain("Aggressive contextual rewrite mode is enabled.", prompt);
            Assert.DoesNotContain("expand vague references", prompt);
            Assert.Contains("do not rewrite the transcript into a different message", prompt);
        }

        [Theory]
        [InlineData("delete that", VoiceCommandKind.DeleteThat)]
        [InlineData("Delete that.", VoiceCommandKind.DeleteThat)]
        [InlineData("press enter", VoiceCommandKind.PressEnter)]
        [InlineData("tab", VoiceCommandKind.Tab)]
        public void MatchVoiceCommand_RecognizesKnownCommands(string transcript, VoiceCommandKind expected)
        {
            var match = DictationExperienceService.MatchVoiceCommand(
                transcript,
                enabled: true,
                DictationExperienceService.VoiceCommandModeMixed);

            Assert.True(match.IsMatch);
            Assert.Equal(expected, match.Kind);
            Assert.True(match.SuppressTranscript);
        }

        [Fact]
        public void MatchVoiceCommand_InCommandsOnlyModeSuppressesUnknownTranscript()
        {
            var match = DictationExperienceService.MatchVoiceCommand(
                "hello world",
                enabled: true,
                DictationExperienceService.VoiceCommandModeCommandsOnly);

            Assert.False(match.IsMatch);
            Assert.True(match.SuppressTranscript);
            Assert.Equal("No command recognized", match.DisplayName);
        }
    }
}
