using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Speakly.Docs
{
    public sealed record DocsExample(string Scenario, string SpokenInput, string Result, string WhyItHelps);

    public sealed record DocsSection(string Title, string Body);

    public sealed record DocsTopic(
        string Key,
        string Title,
        string Summary,
        string? TargetPageTag,
        IReadOnlyList<DocsSection> Sections,
        IReadOnlyList<string> RecommendedDefaults,
        IReadOnlyList<DocsExample> Examples,
        IReadOnlyList<string> Gotchas)
    {
        public bool HasTargetPage => !string.IsNullOrWhiteSpace(TargetPageTag);
    }

    public static class DocsCatalog
    {
        public static readonly IReadOnlyList<string> RequiredTopicKeys = new ReadOnlyCollection<string>(new[]
        {
            "overview",
            "general",
            "hotkeys",
            "audio",
            "transcription",
            "refinement",
            "api-keys",
            "history",
            "statistics"
        });

        public static readonly IReadOnlyList<DocsTopic> Topics = new ReadOnlyCollection<DocsTopic>(new[]
        {
            new DocsTopic(
                "overview",
                "Overview",
                "Speakly captures your voice, turns it into text, optionally refines it with AI, and inserts the final result into the active app.",
                "Home",
                new[]
                {
                    new DocsSection(
                        "What it does",
                        "Speakly is built for fast keyboard-first dictation on Windows. You trigger recording with a hotkey, stop when you are done speaking, and Speakly sends the recording through speech-to-text. If AI refinement is enabled, the transcript is cleaned up before insertion."),
                    new DocsSection(
                        "How it works",
                        "The normal pipeline is: start recording, capture microphone audio, transcribe, optionally refine, apply post-processing like snippets or commands, then insert the final text into the target app. History keeps the original and refined text so you can retry insertion or reprocess without re-recording."),
                    new DocsSection(
                        "First-run checklist",
                        "Set your push-to-talk hotkey first, confirm the correct microphone is selected, test a transcription model that matches your latency budget, then enable refinement only after plain transcription feels reliable.")
                },
                new[]
                {
                    "Pick a comfortable push-to-talk hotkey before tuning anything else.",
                    "Verify microphone input and transcription quality in plain dictation before turning on aggressive AI behavior.",
                    "Keep the floating overlay enabled until your workflow feels stable."
                },
                new[]
                {
                    new DocsExample(
                        "Basic dictation flow",
                        "Press your record hotkey, say: project update is ready for review period",
                        "Speakly records, transcribes, optionally refines punctuation, then inserts: Project update is ready for review.",
                        "This is the default loop most users run all day."),
                    new DocsExample(
                        "Recovering from a bad insert",
                        "After a result inserts badly, open History and choose Retry Insert or Reprocess.",
                        "Speakly reuses the stored text instead of asking you to speak again.",
                        "History is the fastest recovery path when the original recording was fine but the output was not.")
                },
                new[]
                {
                    "If the microphone or hotkey is wrong, every later refinement setting will feel unreliable.",
                    "Refinement improves text quality, but it cannot fully rescue a badly captured recording."
                }),
            new DocsTopic(
                "general",
                "General",
                "General settings control how Speakly behaves on your desktop: overlay visibility, tray behavior, clipboard handling, edit commands, and local telemetry.",
                "General",
                new[]
                {
                    new DocsSection(
                        "What it does",
                        "General settings handle the app shell and safety rails around dictation. This is where you decide whether Speakly lives in the tray, mirrors output to the clipboard, or responds to edit commands like delete that and press enter."),
                    new DocsSection(
                        "How it works",
                        "These settings change the experience around transcription rather than the model pipeline itself. They affect visibility, post-insert convenience, and recovery behavior, especially when the active app is not focused consistently."),
                    new DocsSection(
                        "When to change defaults",
                        "Most users only need to touch overlay visibility, tray behavior, voice commands, and clipboard copy. Telemetry and deferred paste are power-user settings and should stay conservative unless you know why you need them.")
                },
                new[]
                {
                    "Keep the floating overlay on while learning the app.",
                    "Enable voice edit commands only if you actively want spoken phrases like \"delete that\" to execute actions.",
                    "Turn on copy-to-clipboard if you often dictate into apps that miss the direct insert."
                },
                new[]
                {
                    new DocsExample(
                        "Clipboard fallback",
                        "Dictate a sentence while the target app briefly loses focus.",
                        "If Copy result to clipboard is enabled, you can still paste the final text manually with Ctrl+V.",
                        "This reduces frustration when direct insertion misses."),
                    new DocsExample(
                        "Voice edit command",
                        "Say: delete that",
                        "With voice commands enabled in mixed mode, Speakly sends a delete action instead of inserting the words \"delete that\".",
                        "This is useful for short live corrections without touching the keyboard.")
                },
                new[]
                {
                    "If voice commands are enabled accidentally, normal dictation can trigger editing actions you meant as text.",
                    "Deferred paste is helpful only when you understand the focus handoff behavior."
                }),
            new DocsTopic(
                "hotkeys",
                "Hotkeys",
                "Hotkeys define how quickly you can start and stop dictation. A stable binding matters more than any model tweak.",
                "Hotkeys",
                new[]
                {
                    new DocsSection(
                        "What it does",
                        "Hotkeys let you trigger recording without leaving the keyboard. The best setup feels invisible: hold or tap, speak, release, and keep typing."),
                    new DocsSection(
                        "How it works",
                        "Speakly registers global shortcuts so you can start dictation from any app. The chosen hotkey has to avoid collisions with your editor, browser, or OS shortcuts or the experience will feel random."),
                    new DocsSection(
                        "Choosing a good binding",
                        "Prefer combinations that are easy to press repeatedly but uncommon in your daily apps. If you already rely on many shortcuts, choose something clearly dedicated to dictation.")
                },
                new[]
                {
                    "Choose a hotkey you can reach without breaking typing flow.",
                    "Avoid common editor shortcuts like Ctrl+C, Ctrl+V, Ctrl+S, or plain function keys already used elsewhere.",
                    "Retest the hotkey in your main apps after changing it."
                },
                new[]
                {
                    new DocsExample(
                        "Reliable push-to-talk",
                        "Bind capture to a shortcut you do not use elsewhere, then test it in your browser, editor, and chat app.",
                        "Recording starts and stops consistently regardless of which app is active.",
                        "This prevents random failures that feel like transcription issues but are really shortcut conflicts."),
                    new DocsExample(
                        "Conflict symptom",
                        "Use a shortcut already claimed by your editor.",
                        "Sometimes the editor action fires, sometimes Speakly records, and the workflow feels broken.",
                        "Hotkey conflicts are one of the fastest ways to make the app feel unreliable.")
                },
                new[]
                {
                    "Do not diagnose transcription quality until the hotkey itself is dependable.",
                    "A memorable shortcut is better than a clever one if you use dictation hundreds of times a day."
                }),
            new DocsTopic(
                "audio",
                "Audio",
                "Audio settings control which microphone Speakly listens to and how stable the captured recording is before transcription starts.",
                "Audio",
                new[]
                {
                    new DocsSection(
                        "What it does",
                        "Audio settings decide the source quality going into the pipeline. Good transcription starts with clean input from the correct microphone."),
                    new DocsSection(
                        "How it works",
                        "Speakly records from the selected Windows input device, packages that audio, and sends it to the configured speech-to-text provider. If the wrong mic is selected or your signal is noisy, every downstream stage gets weaker input."),
                    new DocsSection(
                        "Troubleshooting capture quality",
                        "Before changing AI models, check microphone selection, Windows input level, room noise, headset routing, and whether another app is monopolizing the device.")
                },
                new[]
                {
                    "Use the microphone that is physically closest to you and has consistent gain.",
                    "Test in a normal speaking voice instead of compensating by shouting.",
                    "Change transcription or refinement models only after the raw capture sounds clean."
                },
                new[]
                {
                    new DocsExample(
                        "Wrong device selected",
                        "You speak into your headset, but the laptop microphone is selected.",
                        "Transcription quality drops and room noise appears in the result.",
                        "Model changes will not fix the wrong input source."),
                    new DocsExample(
                        "Clean input baseline",
                        "Select the correct headset mic and test a short dictation in a quiet room.",
                        "The raw transcript is already close to what you said before refinement runs.",
                        "This gives refinement something worth polishing instead of guessing.")
                },
                new[]
                {
                    "Bad audio cannot be fully repaired later by prompts or context settings.",
                    "Frequent STT mistakes are often microphone or environment issues first, model issues second."
                }),
            new DocsTopic(
                "transcription",
                "Transcription",
                "Transcription turns recorded audio into text. This page is where you balance speed, quality, provider choice, and failover behavior.",
                "Transcription",
                new[]
                {
                    new DocsSection(
                        "What it does",
                        "Speech-to-text is the first language step in Speakly. It produces the raw transcript that refinement later cleans up or rewrites."),
                    new DocsSection(
                        "How it works",
                        "After recording stops, Speakly sends audio to the selected STT provider and model. The provider returns text, and that text becomes the source material for snippets, refinement, history, and insertion."),
                    new DocsSection(
                        "Latency versus quality",
                        "Faster models reduce wait time but can be less accurate in noisy rooms, with accents, or around technical vocabulary. Slower or stronger models can improve fidelity but will increase turnaround time.")
                },
                new[]
                {
                    "Start with the provider-model pair that feels fastest while still preserving your wording accurately.",
                    "Only enable failover if transient provider errors are a real problem in your environment.",
                    "Evaluate STT quality before blaming refinement for bad output."
                },
                new[]
                {
                    new DocsExample(
                        "Fast general dictation",
                        "Use a low-latency STT model for chat messages and short notes.",
                        "You get a result quickly, with enough accuracy that refinement only needs light cleanup.",
                        "This is usually the best daily-driving setup."),
                    new DocsExample(
                        "Technical vocabulary",
                        "Dictate package names, file paths, or code identifiers.",
                        "A stronger STT model often preserves the words better, which keeps refinement from having to infer too much.",
                        "Technical workflows are more sensitive to raw transcription errors.")
                },
                new[]
                {
                    "If the raw transcript is wrong, refinement may preserve the wrong meaning very confidently.",
                    "Provider failover is a resilience feature, not a substitute for choosing a good primary model."
                }),
            new DocsTopic(
                "refinement",
                "Refinement",
                "Refinement is the AI stage that cleans up transcripts, applies task mode and style intent, and optionally uses surrounding context to rewrite vague speech into a clearer final result.",
                "Refinement",
                new[]
                {
                    new DocsSection(
                        "What it does",
                        "Refinement can correct punctuation, smooth grammar, preserve technical formatting, or aggressively rewrite ambiguous speech into a cleaner standalone sentence depending on your selected mode, style preset, prompt, and context settings."),
                    new DocsSection(
                        "How it works",
                        "Speakly builds the final refinement prompt in layers: base prompt from your prompt library, dictation mode instructions, style preset instructions, and optional context instructions. If prompt tone conflicts with style preset tone, the style preset wins for tone and the UI warns you about the conflict."),
                    new DocsSection(
                        "Context-aware rewrite",
                        "Selected text, clipboard text, app name, and window title can be attached as context. In aggressive mode, the refiner is allowed to expand shorthand like \"it\" or \"there\" using that context, but a safety guard blocks obvious polarity flips, time-anchor substitutions, and unrelated low-overlap rewrites.")
                },
                new[]
                {
                    "Use Plain Dictation plus Neutral style as the safest default.",
                    "Switch to Aggressive Context Rewrite only when selected text or clipboard context really represents the thing you are replying to or editing.",
                    "Keep tone out of custom prompts when you want the style preset to control tone cleanly."
                },
                new[]
                {
                    new DocsExample(
                        "Mode plus style",
                        "Dictation mode: Email. Style preset: Formal. Speech: can you send the final invoice tomorrow question mark",
                        "Refinement can produce: Can you send the final invoice tomorrow?",
                        "Mode shapes the task and style shapes the tone; both layer on top of your base prompt."),
                    new DocsExample(
                        "Context rewrite that should work",
                        "Selected text: Can you send me the final invoice by Friday? Speech: Yes, I will send it tomorrow.",
                        "Aggressive context rewrite can produce: Yes, I will send the final invoice tomorrow.",
                        "This is the intended use case for contextual rewrite."),
                    new DocsExample(
                        "Context rewrite that should stay conservative",
                        "Clipboard: The patch will be available tomorrow morning. Speech: Tell him it is available today.",
                        "The safety guard should keep the result close to: Tell him that the patch is available today.",
                        "Context should clarify nouns, not silently replace your spoken time anchor.")
                },
                new[]
                {
                    "Refinement cannot fully fix poor audio or a badly wrong raw transcript.",
                    "If your custom base prompt already hardcodes a tone, style presets can only override it partially unless you keep the prompt neutral.",
                    "Aggressive mode is powerful, but it is best when context is directly relevant to what you are saying."
                }),
            new DocsTopic(
                "api-keys",
                "API Keys",
                "API Keys let Speakly authenticate with STT and refinement providers. Correct setup here is required before provider-specific features can work.",
                "API Keys",
                new[]
                {
                    new DocsSection(
                        "What it does",
                        "Provider keys tell Speakly which external services it is allowed to call. Without the correct key, the provider cannot transcribe or refine your text."),
                    new DocsSection(
                        "How it works",
                        "Speakly stores your provider credentials locally and uses them only for the services you select. Changing a key does not change your provider selection automatically; it just makes that provider available to the app."),
                    new DocsSection(
                        "Validating setup",
                        "The quickest validation is to save the key, choose the matching provider in Transcription or Refinement, then run a short live test and confirm the request succeeds without fallback.")
                },
                new[]
                {
                    "Add keys only for the providers you actively want to use.",
                    "After saving a new key, run a short end-to-end dictation test instead of assuming the setup worked.",
                    "When a provider fails immediately, verify the key, provider selection, and network access before changing prompts."
                },
                new[]
                {
                    new DocsExample(
                        "Refinement provider validation",
                        "Add an OpenRouter key, select OpenRouter in Refinement, then dictate a short sentence with refinement enabled.",
                        "If the request succeeds and History shows refinement ran, the key and provider path are working.",
                        "A live test is faster than trying to infer setup health from the settings page alone."),
                    new DocsExample(
                        "Wrong provider assumption",
                        "You add one provider key but leave another provider selected.",
                        "Requests continue failing until you switch the active provider or add the matching key.",
                        "Keys enable services; they do not silently reconfigure your workflow.")
                },
                new[]
                {
                    "A valid key for one provider does not help another provider.",
                    "Provider failures can look like model bugs when the real issue is authentication."
                }),
            new DocsTopic(
                "history",
                "History",
                "History is your recovery layer. It stores the transcript, refined output, context snapshot, and recovery actions so you can fix bad results without speaking again.",
                "History",
                new[]
                {
                    new DocsSection(
                        "What it does",
                        "History keeps completed entries with the original transcript, refined text, context information, timings, and recovery actions like Copy Refined, Copy Original, Retry Insert, Reprocess, and Pin."),
                    new DocsSection(
                        "How it works",
                        "Every finished dictation is written to history after the pipeline completes. Recovery actions reuse the stored text path, not the original microphone recording, so they are fast and privacy-friendly."),
                    new DocsSection(
                        "When to use it",
                        "Use Retry Insert when the generated text is correct but insertion failed. Use Reprocess when the stored transcript is acceptable but you want another refinement pass, another provider, or another prompt configuration.")
                },
                new[]
                {
                    "Use pinning for results you want to compare against future prompt or model changes.",
                    "Reach for Retry Insert before re-speaking when the text itself already looks correct.",
                    "Use compare information to learn whether a reprocess actually improved the output."
                },
                new[]
                {
                    new DocsExample(
                        "Insertion failure",
                        "The refined text is correct, but it never appeared in your target app.",
                        "Open History and choose Retry Insert.",
                        "This is faster than dictating the same sentence again."),
                    new DocsExample(
                        "Reprocess after changing refinement settings",
                        "You switch to a different refinement model and want to see whether the new output is better.",
                        "Use Reprocess on the stored entry and compare the before and after text blocks.",
                        "History is the safest place to test prompt and model changes without repeating yourself.")
                },
                new[]
                {
                    "History reprocessing is text-based; it does not replay retained audio.",
                    "If the original raw transcript is very wrong, reprocessing may not help much because it starts from the stored text."
                }),
            new DocsTopic(
                "statistics",
                "Statistics",
                "Statistics summarizes how Speakly is performing locally so you can spot latency trends, provider issues, and workflow changes over time.",
                "Statistics",
                new[]
                {
                    new DocsSection(
                        "What it does",
                        "Statistics shows local metrics about recording, transcription, refinement, insertion, and usage patterns. It helps you understand whether the app is getting faster, slower, or less reliable."),
                    new DocsSection(
                        "How it works",
                        "Metrics are derived from completed sessions and local telemetry. They are meant for operational awareness, not as a perfect audit log. If redaction or telemetry settings are conservative, some detail will be intentionally limited."),
                    new DocsSection(
                        "How to use it",
                        "Look for trends instead of obsessing over a single run. If transcription time suddenly spikes or one provider starts failing more often, statistics will usually reveal the pattern before it becomes obvious in daily use.")
                },
                new[]
                {
                    "Use statistics to compare workflows over time, not to judge one isolated result.",
                    "Keep telemetry local and conservative unless you have a specific debugging reason to collect more detail.",
                    "Correlate statistics with history entries when diagnosing a workflow problem."
                },
                new[]
                {
                    new DocsExample(
                        "Latency diagnosis",
                        "You feel Speakly has become slower over the last few days.",
                        "Statistics shows whether recording, transcription, refinement, or insertion time is actually increasing.",
                        "This narrows the problem before you start changing providers."),
                    new DocsExample(
                        "Provider reliability check",
                        "One provider seems flaky during live use.",
                        "Statistics can confirm whether failures are clustered around that provider or whether the issue is broader.",
                        "It is easier to make provider decisions from trend data than from memory.")
                },
                new[]
                {
                    "Statistics are only as useful as the local telemetry you allow Speakly to keep.",
                    "A metrics dashboard does not replace reading the actual history entry when you need exact text details."
                })
        });

        public static DocsTopic? FindByKey(string? key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            return Topics.FirstOrDefault(topic => string.Equals(topic.Key, key, StringComparison.OrdinalIgnoreCase));
        }
    }
}
