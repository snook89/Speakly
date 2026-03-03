# Speakly

Speakly is a Windows desktop voice typing app for system-wide dictation.
Press a global hotkey, speak, and Speakly transcribes your audio and inserts text into the currently focused app.

Built with WPF on .NET 9.

![Speakly banner](Resources/Speakly_banner.png)

## Highlights

- Global hotkeys for both hold-to-talk and toggle recording.
- First-run onboarding wizard (API keys, hotkeys, audio device).
- Multiple STT providers: Deepgram, OpenAI, OpenRouter.
- Optional AI text refinement: OpenAI, OpenRouter, Cerebras.
- Dynamic model refresh from provider APIs, plus favorite model pinning.
- App profiles with automatic profile selection by foreground process name.
- Automatic STT failover for transient provider errors.
- Floating overlay (status, language badge, waveform, quick menu).
- System tray controls (settings, profiles, overlay recovery, quick toggles).
- History and statistics pages with latency, success/failure, and failover data.
- Health checks for startup readiness (keys, devices, hotkeys, failover).
- Primary `SendInput` text insertion with clipboard fallback for reliability.
- Structured local telemetry events with correlation IDs and configurable redaction.
- GitHub Actions CI pipeline with automated build + unit tests + coverage gate.

## Quality and Observability

- Unit-test project: `Speakly.Tests.Unit`.
- CI workflow: `.github/workflows/ci.yml`.
- Telemetry controls available in `General`:
  - `Enable Local Telemetry`
  - `Telemetry Level` (`minimal` / `normal` / `verbose`)
  - `Redaction Mode` (`strict` / `hash` / `off`)
  - Retention days and max file size
- Telemetry storage path:
  - `%AppData%\Speakly\Telemetry\telemetry_events*.jsonl`
- The Statistics page now includes telemetry event/error/session summary.

## Provider Support

| Area | Providers |
|------|-----------|
| Speech-to-Text | Deepgram, OpenAI, OpenRouter |
| Refinement | OpenAI, OpenRouter, Cerebras |

Notes:
- Model lists can be refreshed from provider APIs directly in the UI.
- Built-in default models are provided if refresh is unavailable.

## Requirements

- Windows 10/11.
- .NET 9 SDK (for building/running from source) or .NET 9 runtime (for published build).
- Internet access to provider APIs.
- API keys:
  - At least one STT key to transcribe (Deepgram/OpenAI/OpenRouter).
  - Optional refinement key (OpenAI/OpenRouter/Cerebras) if refinement is enabled.

## Quick Start

```bash
git clone https://github.com/snook89/Speakly.git
cd Speakly
dotnet restore
dotnet run --project Speakly.csproj
```

Or open `Speakly.sln` in Visual Studio and run the `Speakly` project.

## First Launch

On first run, Speakly opens an onboarding wizard:

1. Add API key(s).
2. Set push-to-talk and toggle hotkeys.
3. Pick your input device.
4. Finish setup.

## Default Hotkeys

| Action | Default |
|--------|---------|
| Push-to-talk (hold) | `Space` |
| Toggle record (start/stop) | `F9` |

Both are configurable and support modifiers (`Ctrl`, `Alt`, `Shift`, `Win`).

## How It Works

1. Start recording using PTT or toggle hotkey.
2. Speak while audio is captured.
3. Stop recording.
4. Speakly transcribes audio with the selected STT provider/model.
5. If enabled, Speakly refines the text with the selected LLM provider/model.
6. Final text is inserted into the target window (and optionally copied to clipboard).

## Configuration and Storage

Speakly stores data in a few locations:

- Main config: `%AppData%\Speakly\config.json`
- Debug logs: `%AppData%\Speakly\Logs\speakly_debug.log`
- Prompt library: `%AppData%\Speakly\prompts.json` (auto-migrated from legacy install-folder `prompts.json` on first launch)
- History: `history.json` and `history.log` (next to executable)
- Session metrics: `metrics.json` (next to executable)
- Optional debug recordings: `Records/` folder (next to executable)

Security notes:

- API keys are stored encrypted in config (`*_api_key_enc` fields).
- Legacy plaintext key fields are migration-only.

## Profiles and Failover

- Profiles hold STT/refinement/language behavior per context.
- Active profile can be switched from Home, tray, or overlay.
- Foreground-window process matching is supported for auto profile resolution.
- STT failover can automatically retry with fallback providers on transient errors.

## Build and Publish

Build:

```bash
dotnet build Speakly.csproj -c Release
```

Publish (example):

```bash
dotnet publish Speakly.csproj -c Release -r win-x64
```

## Project Structure

```text
Speakly/
├── App.xaml.cs                      # App lifecycle and recording/session orchestration
├── MainWindow.xaml/.cs              # Main settings shell (NavigationView)
├── OnboardingWindow.xaml/.cs        # First-run setup wizard
├── FloatingOverlay.xaml/.cs         # Always-on-top recording overlay
├── MainViewModel.cs                 # Core settings/state/viewmodel logic
├── ConfigManager.cs                 # Config load/save/migration/secret handling
├── Profiles.cs                      # App profile model and process matching helpers
├── DeepgramTranscriber.cs
├── OpenAITranscriber.cs
├── OpenRouterTranscriber.cs
├── OpenAIRefiner.cs
├── OpenRouterRefiner.cs
├── CerebrasRefiner.cs
├── TranscriberFactory.cs
├── TextRefinerFactory.cs
├── HistoryManager.cs
├── StatisticsManager.cs
├── TextInserter.cs
├── TrayIconService.cs
├── Services/
│   ├── ProfileResolverService.cs
│   └── HealthCheckService.cs
├── Pages/                           # Home, General, Hotkeys, Audio, Transcription,
│                                    # Refinement, API Keys, History, Statistics, Info
├── Themes/
└── Resources/
```

## Dependencies

- [NAudio](https://github.com/naudio/NAudio)
- [Hardcodet.NotifyIcon.Wpf](https://github.com/hardcodet/wpf-notifyicon)
- [WPF-UI](https://github.com/lepoco/wpfui)
- `System.Text.Json`

## License

[MIT](LICENSE)
