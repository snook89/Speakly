using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Speakly.Docs
{
    public sealed record DocsExample(string Scenario, string SpokenInput, string Result, string WhyItHelps);

    public sealed record DocsSection(string Title, string Body);

    public sealed record DocsLink(string Label, string Url, string Description);

    public sealed record DocsTopic(
        string Key,
        string Title,
        string Summary,
        string? TargetPageTag,
        IReadOnlyList<DocsSection> Sections,
        IReadOnlyList<string> RecommendedDefaults,
        IReadOnlyList<DocsExample> Examples,
        IReadOnlyList<string> Gotchas,
        IReadOnlyList<DocsLink>? Links = null)
    {
        public bool HasTargetPage => !string.IsNullOrWhiteSpace(TargetPageTag);

        public bool HasLinks => Links is { Count: > 0 };
    }

    public static class DocsCatalog
    {
        public static readonly IReadOnlyList<string> RequiredTopicKeys = new ReadOnlyCollection<string>(new[]
        {
            "overview",
            "profiles",
            "hotkeys",
            "audio",
            "transcription",
            "refinement",
            "api-keys",
            "general",
            "overlay-tray",
            "history",
            "statistics",
            "privacy-storage",
            "updates"
        });

        public static readonly IReadOnlyList<DocsTopic> Topics = new ReadOnlyCollection<DocsTopic>(new[]
        {
            new DocsTopic(
                "overview",
                "Overview",
                "Speakly captures your voice, turns it into text, optionally refines it with AI, and inserts the final result into the app you are already using.",
                "Home",
                new[]
                {
                    new DocsSection(
                        "What it does",
                        "Speakly is a Windows dictation tool built for keyboard-first workflows. You trigger recording with a hotkey, Speakly captures the microphone, transcribes the audio, optionally refines the text, and inserts the final result into the target app."),
                    new DocsSection(
                        "How it works",
                        "The normal pipeline is: resolve the target app and active profile, capture audio, transcribe, check for voice commands, optionally refine with prompt plus mode plus style plus context, apply snippets and personalization, then insert the final text. History, telemetry, and statistics record what happened so you can recover or troubleshoot later."),
                    new DocsSection(
                        "Best use scenarios",
                        "Speakly works best for fast writing in chat apps, email, editors, browsers, docs, terminals, and notes apps where keyboard plus voice is faster than either one alone."),
                    new DocsSection(
                        "First-run checklist",
                        "Set your hotkey first, verify the correct microphone, test plain transcription before turning on aggressive refinement, then map your main apps to profiles so each workflow gets the right provider, prompt, and mode automatically.")
                },
                new[]
                {
                    "Start with one reliable microphone and one reliable STT provider before layering on refinement.",
                    "Keep the floating overlay visible until the workflow feels trustworthy.",
                    "Set up profiles for your most common apps early so Speakly switches behavior automatically."
                },
                new[]
                {
                    new DocsExample(
                        "Basic dictation loop",
                        "Press the record hotkey and say: project update is ready for review period",
                        "Speakly records, transcribes, optionally refines punctuation, and inserts: Project update is ready for review.",
                        "This is the default loop most users will use all day."),
                    new DocsExample(
                        "Bad insert recovery",
                        "The inserted result is correct but went to the wrong place or failed.",
                        "Open History and use Retry Insert or Copy Refined instead of dictating again.",
                        "History is the fastest recovery surface when the text itself is already good.")
                },
                new[]
                {
                    "If the hotkey or microphone is wrong, every later model or prompt tweak will feel unreliable.",
                    "Refinement can improve wording, but it cannot fully rescue poor capture."
                }),
            new DocsTopic(
                "profiles",
                "Profiles",
                "Profiles let Speakly auto-switch STT, refinement, prompts, modes, styles, dictionary terms, and command behavior based on the focused app’s process name.",
                "Home",
                new[]
                {
                    new DocsSection(
                        "What it does",
                        "A profile stores a complete dictation setup for a workflow or application. You can have one profile for Telegram, another for Notepad++, another for Chrome, and each one can use a different provider, model, prompt, mode, style, dictionary, and context behavior."),
                    new DocsSection(
                        "How it works",
                        "When you start a dictation session, Speakly captures the foreground window, reads that process name, and checks profile mappings. If a profile contains that process name, Speakly activates that profile for the session. If no profile matches, Speakly falls back to the currently selected profile. The selected profile editor in Home can stay on one profile while the runtime session indicator shows a different resolved session profile."),
                    new DocsSection(
                        "What can live inside a profile",
                        "A profile can carry STT provider and model, refinement provider and model, custom prompt, dictation mode, style preset, context toggles, personal dictionary choices, and command behavior. That means a single process match can swap almost the entire writing workflow in one step."),
                    new DocsSection(
                        "Process matching rules",
                        "Matching is based on process name, not the profile display name. Telegram and telegram.exe are treated the same. If multiple profiles match the same process, the first matching profile in the saved profile list wins."),
                    new DocsSection(
                        "Best use scenarios",
                        "Profiles are best when you switch between different kinds of writing. For example: relaxed messages in Telegram, formal email wording in Outlook, and literal technical dictation in Notepad++ or VS Code.")
                },
                new[]
                {
                    "Map your most-used apps by process name so the right settings apply automatically on PTT.",
                    "Use one profile per distinct workflow, not one profile per tiny variation.",
                    "Check the Home page match status after editing process mappings."
                },
                new[]
                {
                    new DocsExample(
                        "Telegram versus Notepad++",
                        "Map telegram to a Message profile and notepad++ to a Code or Plain Dictation profile.",
                        "Pressing PTT in Telegram uses the Telegram profile. Pressing PTT in Notepad++ uses the Notepad++ profile.",
                        "This is how Speakly keeps one app conversational and another literal or technical."),
                    new DocsExample(
                        "No profile match",
                        "Press PTT in an app that is not listed in any profile process mappings.",
                        "Speakly uses the currently selected profile as the fallback.",
                        "This prevents dictation from failing just because an app is unmapped.")
                },
                new[]
                {
                    "Changing the selected profile in the UI does not override process matching for a future session if the focused app resolves to a different mapped profile.",
                    "A typo in the process mapping means the profile will never auto-switch."
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
                        "Hotkeys let you trigger recording without leaving the keyboard. Speakly supports hold-to-talk and toggle-record workflows."),
                    new DocsSection(
                        "How it works",
                        "Speakly registers global shortcuts so recording can start from any app. Hold-to-talk is best for short controlled bursts. Toggle-record is better for longer drafts or when holding a key is uncomfortable."),
                    new DocsSection(
                        "Failure behavior",
                        "If the low-level keyboard hook cannot be installed at startup, Speakly now reports that through health status instead of pretending hotkeys are available. In that state, push-to-talk will not start until the app is restarted and the hook installs correctly."),
                    new DocsSection(
                        "Choosing a good binding",
                        "Prefer combinations that are easy to press repeatedly but uncommon in your daily apps. If a hotkey conflicts with your editor or browser, the experience will feel random even though transcription is fine.")
                },
                new[]
                {
                    "Choose a hotkey you can reach without breaking typing flow.",
                    "Avoid common editor shortcuts like Ctrl+C, Ctrl+V, Ctrl+S, or plain function keys already used elsewhere.",
                    "Retest hotkeys in the apps you use most after changing them."
                },
                new[]
                {
                    new DocsExample(
                        "Reliable push-to-talk",
                        "Bind capture to a shortcut you do not use elsewhere, then test it in your browser, editor, and chat app.",
                        "Recording starts and stops consistently regardless of which app is active.",
                        "This prevents random failures that look like model issues but are really shortcut conflicts."),
                    new DocsExample(
                        "Conflict symptom",
                        "Use a shortcut already claimed by your editor.",
                        "Sometimes the editor action fires, sometimes Speakly records, and the workflow feels broken.",
                        "Hotkey conflicts are one of the fastest ways to make a dictation app feel unreliable.")
                },
                new[]
                {
                    "Do not diagnose transcription quality until the hotkey itself is dependable.",
                    "A memorable shortcut is better than a clever one if you dictate often."
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
                        "Speakly records from the selected Windows input device and streams or sends that audio to the active STT provider. Managed audio options can normalize volume, smooth gain, and reduce noise before the provider sees the signal. If Speakly never detects meaningful mic signal during a recording, it warns you and stops before wasting time on STT or failover."),
                    new DocsSection(
                        "Best use scenarios",
                        "Audio tuning matters most in noisy environments, with inconsistent microphones, or when you want lower-latency streaming behavior without clipped syllables."),
                    new DocsSection(
                        "Troubleshooting capture quality",
                        "Before changing AI models, check microphone selection, Windows input level, room noise, headset routing, and whether another app is monopolizing the device.")
                },
                new[]
                {
                    "Use the microphone physically closest to you and keep its gain stable.",
                    "Start with managed audio defaults before pushing extreme normalization values.",
                    "Only change STT or refinement models after the raw audio sounds clean.",
                    "If you see No mic signal, check mute state, input device selection, and Windows input volume before touching model settings."
                },
                new[]
                {
                    new DocsExample(
                        "Wrong device selected",
                        "You speak into your headset, but the laptop microphone is selected.",
                        "Transcription quality drops and room noise appears in the result.",
                        "Model changes will not fix the wrong input source."),
                    new DocsExample(
                        "Muted mic or dead signal",
                        "Hold PTT while the mic is muted or the wrong dead input is selected.",
                        "Speakly shows No mic signal and ends the session cleanly instead of hanging through STT.",
                        "This makes hardware or routing mistakes obvious before they look like provider failures."),
                    new DocsExample(
                        "Clean baseline",
                        "Select the correct headset mic and test a short dictation in a quiet room.",
                        "The raw transcript is already close to what you said before refinement runs.",
                        "This gives refinement something worth polishing instead of forcing it to guess.")
                },
                new[]
                {
                    "Bad audio cannot be fully repaired later by prompts or context settings.",
                    "Frequent STT mistakes are often microphone or environment issues first, model issues second."
                }),
            new DocsTopic(
                "transcription",
                "Transcription",
                "Transcription turns recorded audio into text. This is where you balance speed, quality, provider choice, failover behavior, and language handling.",
                "Transcription",
                new[]
                {
                    new DocsSection(
                        "What it does",
                        "Speech-to-text is the first language step in Speakly. It produces the raw transcript that refinement, snippets, history, and insertion all depend on."),
                    new DocsSection(
                        "How it works",
                        "After recording stops, Speakly sends audio to the active STT provider and model from the resolved session profile. That provider returns text, and Speakly uses that text as the base for commands, refinement, snippets, and history."),
                    new DocsSection(
                        "Providers, models, and refresh",
                        "Speakly supports Deepgram, OpenAI, and OpenRouter for STT. You can refresh model lists from provider APIs and pin favorites so your preferred models stay easy to reach."),
                    new DocsSection(
                        "Failover behavior",
                        "If transient STT errors occur and failover is enabled, Speakly can retry with another provider in the configured failover order. History and telemetry record when failover was attempted and which provider finally succeeded. No-mic-signal sessions stop before this stage because there is nothing useful to transcribe.")
                },
                new[]
                {
                    "Start with the fastest model that still preserves your wording accurately.",
                    "Enable failover if transient provider errors are a real problem in your environment.",
                    "Use personal dictionary terms for repeated names, jargon, and brand words instead of hoping the model learns them by chance."
                },
                new[]
                {
                    new DocsExample(
                        "Fast general dictation",
                        "Use a low-latency STT model for chat messages and short notes.",
                        "You get text quickly, with enough accuracy that refinement only needs light cleanup.",
                        "This is usually the best daily-driving setup."),
                    new DocsExample(
                        "Failover save",
                        "Your primary STT provider errors out during a session while failover is enabled.",
                        "Speakly retries using the next configured provider and still completes the session.",
                        "This keeps short outages from breaking your workflow.")
                },
                new[]
                {
                    "If the raw transcript is wrong, refinement may preserve the wrong meaning very confidently.",
                    "Failover is a resilience feature, not a substitute for choosing a good primary model."
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
                        "Refinement can correct punctuation, smooth grammar, preserve technical formatting, expand shorthand, and format output differently for email, messages, notes, code, or custom workflows."),
                    new DocsSection(
                        "How it works",
                        "Speakly builds the final refinement prompt in layers: base prompt from your prompt library, dictation mode instructions, style preset instructions, and optional context instructions. If prompt tone conflicts with style preset tone, the style preset wins for tone and the UI warns you about the conflict."),
                    new DocsSection(
                        "Modes, style, and prompts",
                        "Modes decide task shape. Style preset decides tone. Your base prompt sets general behavior. The safest prompt strategy is to keep the base prompt neutral and let mode plus style do the visible shaping."),
                    new DocsSection(
                        "Saved prompts, snippets, and learning",
                        "Prompt presets give you reusable base instructions. Snippets and approved correction learning help Speakly preserve repeated terms and preferred wording. Use prompts for high-level behavior, snippets for repeated fixed phrases, and dictionary or learned corrections for names, jargon, and spellings."),
                    new DocsSection(
                        "Context-aware rewrite",
                        "App name, window title, selected text, and clipboard can be attached as context. Aggressive mode expands vague replies into standalone text using context, while conservative mode keeps context as a lighter hint. A safety guard blocks obvious polarity flips, time-anchor swaps, and unrelated low-overlap rewrites.")
                },
                new[]
                {
                    "Use Plain Dictation plus Neutral style as the safest default.",
                    "Use Aggressive Context Rewrite only when selected or clipboard context really is the thing you are replying to or editing.",
                    "Keep tone out of custom prompts when you want style presets to work cleanly.",
                    "Use snippets and learned corrections for repeated wording instead of stuffing every rule into the prompt."
                },
                new[]
                {
                    new DocsExample(
                        "Mode plus style",
                        "Dictation mode: Email. Style preset: Formal. Speech: can you send the final invoice tomorrow question mark",
                        "Refinement can produce: Can you send the final invoice tomorrow?",
                        "Mode shapes the task and style shapes the tone; both layer on top of the base prompt."),
                    new DocsExample(
                        "Context rewrite that should work",
                        "Selected text: Can you send me the final invoice by Friday? Speech: Yes, I will send it tomorrow.",
                        "Aggressive context rewrite can produce: Yes, I will send the final invoice tomorrow.",
                        "This is the intended use case for contextual rewrite."),
                    new DocsExample(
                        "Context rewrite that should stay safe",
                        "Clipboard: The patch will be available tomorrow morning. Speech: Tell him it is available today.",
                        "The safety guard should keep the result close to: Tell him that the patch is available today.",
                        "Context should clarify nouns, not silently replace your spoken time anchor."),
                    new DocsExample(
                        "Prompt tone conflict",
                        "Base prompt says: use a professional tone. Style preset is Casual.",
                        "Speakly warns about the conflict, and the style preset wins for tone while the base prompt still provides the broader behavior.",
                        "This keeps custom prompts useful without letting tone instructions fight silently.")
                },
                new[]
                {
                    "Refinement cannot fully fix poor audio or a badly wrong raw transcript.",
                    "If your base prompt already hardcodes a tone, style presets can conflict with it.",
                    "Aggressive mode is powerful, but it is best when context is directly relevant.",
                    "Prompt presets are not the same as snippets; use the right layer for the job."
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
                        "Speakly stores provider credentials locally, keeps encrypted fields in config, and uses them only for the services you actively select. Changing a key does not silently switch your provider choice; it only makes that provider usable."),
                    new DocsSection(
                        "Getting a Cerebras key",
                        "Open the Cerebras cloud signup or login page, sign in, then navigate to the API Keys section in your account dashboard and generate a new API key. Paste that key into Speakly before selecting Cerebras for refinement."),
                    new DocsSection(
                        "Getting a Deepgram key",
                        "Open the Deepgram console signup page, create or sign in to your account, then open the API Keys area in the console and generate a new key for speech-to-text use. Paste that key into Speakly before selecting Deepgram for transcription."),
                    new DocsSection(
                        "Testing setup",
                        "The fastest validation is to save the key, choose the matching provider in Transcription or Refinement, then run a short live dictation and confirm the request succeeds without fallback.")
                },
                new[]
                {
                    "Add keys only for the providers you actually want to use.",
                    "Run a live test after saving a new key instead of assuming the setup worked.",
                    "If a provider fails immediately, verify both the key and the selected provider/model path."
                },
                new[]
                {
                    new DocsExample(
                        "Refinement provider validation",
                        "Add an OpenRouter key, select OpenRouter in Refinement, then dictate a short sentence with refinement enabled.",
                        "If the request succeeds and History shows refinement ran, the key and provider path are working.",
                        "A live test is faster than trying to infer health from the settings page alone."),
                    new DocsExample(
                        "Wrong provider assumption",
                        "You add one provider key but leave a different provider selected.",
                        "Requests continue failing until you switch the active provider or add the matching key.",
                        "Keys enable services; they do not silently reconfigure your workflow.")
                },
                new[]
                {
                    "A valid key for one provider does not help another provider.",
                    "Provider failures can look like model bugs when the real issue is authentication."
                },
                new[]
                {
                    new DocsLink(
                        "Open Cerebras signup / login",
                        "https://cloud.cerebras.ai/?utm_source=homepage",
                        "After signing in, open the API Keys section in the Cerebras dashboard and generate a new key for Speakly refinement."),
                    new DocsLink(
                        "Open Deepgram signup",
                        "https://console.deepgram.com/signup",
                        "After signing in, open the API Keys area in the Deepgram console and generate a new key for Speakly transcription.")
                }),
            new DocsTopic(
                "general",
                "General",
                "General settings control how Speakly behaves on your desktop: clipboard handling, deferred paste, startup, telemetry, debug logs, failover, and core behavioral safety rails.",
                "General",
                new[]
                {
                    new DocsSection(
                        "What it does",
                        "General settings are the shell around dictation. This is where you decide how Speakly behaves when insertion fails, whether it starts with Windows, how much telemetry to keep locally, and whether advanced safety or recovery features stay on."),
                    new DocsSection(
                        "How it works",
                        "These settings shape the app-level behavior around transcription rather than the language model output itself. They affect startup, recovery, observability, logging, and fallbacks."),
                    new DocsSection(
                        "Voice edit commands",
                        "General settings also control whether spoken edit commands are enabled and whether Speakly treats them in Mixed, Dictation only, or Commands only mode. Commands such as delete that, scratch that, undo that, select that, backspace, press enter, tab, and insert space are recognized after STT and before final insertion."),
                    new DocsSection(
                        "Startup, clipboard, and telemetry",
                        "This is also where you decide whether Speakly starts with Windows, whether clipboard and deferred paste fallbacks are allowed, how much local telemetry to keep, and whether debug logs are available when troubleshooting."),
                    new DocsSection(
                        "Best use scenarios",
                        "Change General settings when the app feels unreliable in the real desktop environment rather than when the text itself is wrong.")
                },
                new[]
                {
                    "Keep local telemetry on at a conservative level until the workflow is stable.",
                    "Leave STT failover enabled if availability matters more than strict provider consistency.",
                    "Use deferred paste only if you understand why focus loss is interrupting insertion.",
                    "Leave voice edit commands in Mixed mode unless you have a reason to force Dictation only or Commands only."
                },
                new[]
                {
                    new DocsExample(
                        "Deferred paste rescue",
                        "The target app regains focus after insertion initially failed.",
                        "Deferred paste can complete the paste when the app returns to the foreground.",
                        "This is useful in apps that steal focus briefly during transitions."),
                    new DocsExample(
                        "Debugging a flaky workflow",
                        "You see random failures in one application.",
                        "Enable debug logs and use history plus telemetry to see whether the problem is focus, provider, failover, or insertion.",
                        "General settings are where you turn on the tooling that explains what happened."),
                    new DocsExample(
                        "Voice edit command workflow",
                        "Say: hello world. Then say: delete that.",
                        "Speakly recognizes delete that as a command and removes the last inserted text instead of typing the words literally.",
                        "This is the intended cleanup workflow for quick corrections without touching the keyboard.")
                },
                new[]
                {
                    "Turning on every advanced setting at once makes troubleshooting harder, not easier.",
                    "Clipboard recovery can feel magical, but it still depends on the target app and focus timing.",
                    "If voice edit commands are off or set to Dictation only, command phrases will be treated as normal transcript text."
                }),
            new DocsTopic(
                "overlay-tray",
                "Overlay & Tray",
                "The overlay and tray keep Speakly usable while you stay in other apps. They surface status, quick actions, active mode, context, and recovery controls without forcing you back into the main window.",
                "General",
                new[]
                {
                    new DocsSection(
                        "What it does",
                        "The floating overlay shows recording, transcribing, refining, failover, and command status. It can also display language, active mode, and context usage. The tray menu exposes quick recovery and control actions when the main window is closed or minimized."),
                    new DocsSection(
                        "How it works",
                        "The overlay stays on top and updates live as the session moves through each stage. Tray actions let you recover the overlay, switch profiles, toggle refinement, and access settings even when Speakly is running mostly in the background."),
                    new DocsSection(
                        "What you can see live",
                        "The overlay can show active mode, context usage, command detection, provider progress, fallback state, and insertion status. It is the fastest place to confirm whether Speakly heard a command, used context, or fell back after an error."),
                    new DocsSection(
                        "Best use scenarios",
                        "Use the overlay if you want confidence while dictating. Use the tray if you prefer the settings window minimized most of the time or want quick control without reopening the full UI.")
                },
                new[]
                {
                    "Keep the overlay visible while learning the app, then decide later if auto-hide fits your desktop.",
                    "Use the tray for quick recovery and light control, not deep configuration.",
                    "If you care about context or command visibility, the overlay should stay enabled."
                },
                new[]
                {
                    new DocsExample(
                        "Command recognition",
                        "Say: delete that",
                        "The overlay shows a command status like CMD:Delete That instead of silently treating it as normal dictation.",
                        "This makes command interpretation auditable in real time."),
                    new DocsExample(
                        "Overlay recovery",
                        "The overlay gets hidden behind another workflow or auto-hide makes you lose track of it.",
                        "Use the tray menu to restore it immediately.",
                        "This is faster than hunting through window lists.")
                },
                new[]
                {
                    "If the overlay is off, you lose the easiest live explanation of what Speakly is doing.",
                    "A tray-only workflow is efficient for advanced users, but harder for first-time troubleshooting."
                }),
            new DocsTopic(
                "history",
                "History",
                "History is your recovery layer. It stores the transcript, refined output, context snapshot, timings, and recovery actions so you can fix bad results without speaking again.",
                "History",
                new[]
                {
                    new DocsSection(
                        "What it does",
                        "History keeps completed entries with original text, refined text, timings, context summary, provider information, command runs, and actions like Copy Refined, Copy Original, Retry Insert, Reprocess, and Pin."),
                    new DocsSection(
                        "How it works",
                        "Every finished dictation is written to history after the pipeline completes. Failed sessions such as no-mic-signal or no-final-result are also recorded so troubleshooting does not disappear. Recovery actions reuse the stored text path, not the original microphone recording, so they are fast and privacy-friendly."),
                    new DocsSection(
                        "Compare and filters",
                        "History can filter by provider, action, and pin state. Recovery entries preserve links back to their source entries so you can compare before and after results when reprocessing or retrying."),
                    new DocsSection(
                        "Commands and context visibility",
                        "History records command actions separately from normal transcript entries, and it keeps context summaries plus context mode so you can see whether a result came from selected text, clipboard text, or a specific rewrite strategy."),
                    new DocsSection(
                        "Best use scenarios",
                        "Use history when the words were basically fine but insertion, provider choice, prompt choice, or refinement output was not.")
                },
                new[]
                {
                    "Use pinning for entries you want to compare against future prompt or model changes.",
                    "Use Retry Insert before re-speaking when the text itself already looks correct.",
                    "Use Reprocess when the transcript is acceptable but you want another refinement path."
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
                    "If the raw transcript is very wrong, reprocessing may not help much because it starts from stored text."
                }),
            new DocsTopic(
                "statistics",
                "Statistics",
                "Statistics summarizes how Speakly is performing locally so you can spot latency trends, provider issues, failover patterns, and workflow changes over time.",
                "Statistics",
                new[]
                {
                    new DocsSection(
                        "What it does",
                        "Statistics shows local metrics about recording, transcription, refinement, insertion, provider success, errors, and telemetry rollups."),
                    new DocsSection(
                        "How it works",
                        "Metrics are derived from completed sessions and local telemetry. They are meant for trend analysis and troubleshooting, not as a perfect audit log."),
                    new DocsSection(
                        "Best use scenarios",
                        "Use statistics when the app feels slower, more error-prone, or more inconsistent than before and you want evidence before changing providers or prompts.")
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
                        "Statistics can confirm whether failures cluster around that provider or whether the issue is broader.",
                        "It is easier to make provider decisions from trend data than from memory.")
                },
                new[]
                {
                    "Statistics are only as useful as the local telemetry you allow Speakly to keep.",
                    "A metrics dashboard does not replace reading the actual history entry when you need exact text details."
                }),
            new DocsTopic(
                "privacy-storage",
                "Privacy & Storage",
                "Speakly keeps sensitive features visible and mostly local. This section explains what is stored, what is sent to providers, and where local files live.",
                "General",
                new[]
                {
                    new DocsSection(
                        "What it does",
                        "Privacy and storage settings explain which features use cloud providers, which data is stored locally, how telemetry is redacted, and how context features behave."),
                    new DocsSection(
                        "How it works",
                        "Audio and text are sent only to the providers you configure for STT or refinement. Locally, Speakly stores config, prompt library, telemetry, logs, and history. Context features are opt-in. Selected text and clipboard context are only used when their toggles are enabled."),
                    new DocsSection(
                        "Privacy mode and retention",
                        "General settings now expose Privacy Mode and History retention directly. Normal mode keeps local history and telemetry. No history disables saved dictation history. History retention controls how many days of unpinned entries stay on disk, while pinned entries are preserved."),
                    new DocsSection(
                        "What is stored locally",
                        "Config lives under AppData Speakly. Telemetry lives under AppData Speakly Telemetry. History and metrics live beside the executable. Prompt library, logs, and startup registration data are also local."),
                    new DocsSection(
                        "Best use scenarios",
                        "Use strict or hashed telemetry redaction when you want troubleshooting data without plain-text content leakage. Keep context toggles off unless the quality benefit is worth it for your workflow.")
                },
                new[]
                {
                    "Leave context sources off until you actually need them.",
                    "Use telemetry redaction unless you have a deliberate debugging reason to relax it.",
                    "Review where history and logs are stored if the machine is shared or managed."
                },
                new[]
                {
                    new DocsExample(
                        "Safe context posture",
                        "Enable only selected text context, leave clipboard and window title off.",
                        "Refinement gets the strongest local clue without broad passive context collection.",
                        "This is a good quality and privacy tradeoff for many users."),
                    new DocsExample(
                        "Troubleshooting with redaction",
                        "You want telemetry for debugging but do not want plain text stored.",
                        "Keep telemetry on with a redacted mode instead of turning logging off entirely.",
                        "This preserves observability without making local storage noisier than necessary.")
                },
                new[]
                {
                    "History is useful, but it does store text unless privacy mode disables it.",
                    "Cloud providers only know what you send them, so each enabled provider is part of the privacy surface."
                }),
            new DocsTopic(
                "updates",
                "Updates",
                "Speakly can check GitHub Releases for new versions, download update packages, and prompt for restart when a newer build is available.",
                "Info",
                new[]
                {
                    new DocsSection(
                        "What it does",
                        "Updates keep the installed app current without asking you to reinstall manually every time a release ships."),
                    new DocsSection(
                        "How it works",
                        "On startup, Speakly checks GitHub Releases. If a newer release exists and the install type supports self-update, it downloads the update in the background and prompts for restart when the package is ready."),
                    new DocsSection(
                        "Best use scenarios",
                        "Automatic updates are best when Speakly is installed through its normal setup flow. Local publish builds can run, but they may not support the full self-update path.")
                },
                new[]
                {
                    "Use the normal installed build if you want the update flow to work end-to-end.",
                    "Check the Info page when you want to confirm current version and update status.",
                    "Restart after an update download when you are ready to apply the new build."
                },
                new[]
                {
                    new DocsExample(
                        "Normal update",
                        "A newer GitHub release exists for your installed version.",
                        "Speakly downloads it and prompts for restart to apply.",
                        "This is the intended update path for non-dev installs."),
                    new DocsExample(
                        "Local publish build",
                        "You run a local build instead of the installed setup package.",
                        "Speakly may detect updates, but self-update can be unavailable depending on install type.",
                        "This is normal for development or portable-like workflows.")
                },
                new[]
                {
                    "If you are running a local publish build, auto-update behavior can differ from a normal installer-based install.",
                    "Checking for updates manually is useful when you want certainty before reporting a bug."
                }),
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
