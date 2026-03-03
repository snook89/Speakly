# Speakly

**Speakly** is a Windows desktop application that lets you speak and have your words instantly transcribed and typed into any application — hands-free, system-wide, with optional AI-powered text refinement.

Built with WPF / .NET 9, it uses global hotkeys so it works while any other window is focused.

---

## Features

- **Global Hotkeys** — Push-to-talk (hold) and toggle-record (press once) modes work system-wide, regardless of which application has focus.
- **Multiple STT Backends**
  - [Deepgram](https://deepgram.com/) — nova-2, nova-3, nova-2-phone, nova-2-medical, whisper variants
  - [OpenAI Whisper](https://platform.openai.com/docs/guides/speech-to-text) — whisper-1
- **AI Text Refinement** — Optionally clean up transcribed text with a large language model before inserting it:
  - **OpenAI** — GPT-4o, GPT-4o-mini, GPT-4-turbo, GPT-3.5-turbo
  - **Cerebras** — LLaMA 3.1 (8B / 70B)
  - **OpenRouter** — Gemini 2.0 Flash, Claude 3.5 Sonnet, Grok-2, DeepSeek, and more
- **Auto Text Insertion** — Transcribed text is typed directly into the focused window using the Win32 `SendInput` API (clipboard-free).
- **Floating Overlay** — A small always-on-top status indicator shows when recording is active.
- **System Tray** — Minimizes to tray with quick access to window and exit.
- **Multiple Themes** — Dark, Light, Matrix, Ocean.
- **Audio Configuration** — Select input device, sample rate, channels, and chunk size.
- **Transcription History** — Browse and copy previous transcriptions.
- **API Key Tester** — Test all configured API keys from within the settings UI.
- **Debug Recording** — Optionally save audio recordings to disk for troubleshooting.
- **Language Support** — Configure transcription language.

---

## Screenshots

> *(Add screenshots here)*

---

## Requirements

- Windows 10/11
- [.NET 9 Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/9.0) (or self-contained build)
- At least one API key:
  - Deepgram API key (for Deepgram transcription)
  - OpenAI API key (for Whisper transcription or GPT refinement)
  - Cerebras API key (for Cerebras refinement)
  - OpenRouter API key (for OpenRouter refinement)

---

## Getting Started

### 1. Clone the repository

```bash
git clone https://github.com/YOUR_USERNAME/Speakly.git
cd Speakly
```

### 2. Build

```bash
dotnet build -c Release
```

Or open `Speakly.sln` in Visual Studio 2022+ and build from there.

### 3. Run

```bash
dotnet run
```

Or run the compiled `Speakly.exe` from the `bin/Release/net9.0-windows/` directory.

### 4. Configure API Keys

On first launch, open the **Settings** window and enter your API keys in the **API Keys** tab. Changes are saved automatically.

---

## Usage

| Action | Default Hotkey |
|--------|---------------|
| Push-to-talk (hold to record) | `Space` |
| Toggle record (press to start/stop) | `F9` |

Both hotkeys are fully configurable in Settings.

**Workflow:**
1. Press and hold the PTT key (or press toggle-record key once) in any application.
2. Speak.
3. Release (or press toggle-record again).
4. The transcribed (and optionally refined) text is typed into the focused window.

---

## Configuration

Settings are stored in `config.json` next to `Speakly.exe`. The Settings window exposes all options:

| Setting | Description |
|---------|-------------|
| STT Model | Transcription provider (Deepgram / OpenAI) |
| Refinement Model | AI text refiner (OpenAI / Cerebras / OpenRouter / None) |
| Refinement Prompt | Custom prompt for the AI refiner |
| Audio Device | Microphone input device |
| Sample Rate | Audio sample rate in Hz (default: 16000) |
| Channels | Mono (1) or stereo (2) |
| Chunk Size | Audio buffer chunk size in bytes |
| Language | Transcription language code (e.g. `en`) |
| Theme | UI color theme |
| Minimize to Tray | Hide to system tray on window close |
| Save Debug Records | Save raw audio recordings to disk |

---

## Architecture

```
Speakly/
├── App.xaml.cs              # Application entry point, hotkey & recording orchestration
├── MainWindow.xaml/.cs      # Settings window (tabbed UI)
├── FloatingOverlay.xaml/.cs # Always-on-top recording status indicator
├── MainViewModel.cs         # MVVM view model for settings
│
├── Services/
│   ├── GlobalHotkeyService.cs   # Win32 global hotkey registration
│   ├── TrayIconService.cs       # System tray icon management
│   ├── AudioRecorder.cs         # NAudio microphone capture
│   ├── TextInserter.cs          # Win32 SendInput text injection
│   ├── HistoryManager.cs        # Transcription history persistence
│   └── Logger.cs                # Debug logging
│
├── Transcription/
│   ├── ITranscriber.cs          # Transcription interface
│   ├── DeepgramTranscriber.cs   # Deepgram streaming WebSocket client
│   ├── OpenAITranscriber.cs     # OpenAI Whisper REST client
│   └── TranscriberFactory.cs    # Factory to select STT backend
│
├── Refinement/
│   ├── ITextRefiner.cs          # Text refinement interface
│   ├── OpenAIRefiner.cs         # GPT-based text refiner
│   ├── CerebrasRefiner.cs       # Cerebras LLaMA refiner
│   ├── OpenRouterRefiner.cs     # OpenRouter multi-model refiner
│   └── TextRefinerFactory.cs    # Factory to select refinement backend
│
├── Config/
│   └── ConfigManager.cs         # JSON config load/save (~AppData)
│
├── Themes/                      # WPF ResourceDictionary theme files
│   ├── DarkTheme.xaml
│   ├── LightTheme.xaml
│   ├── MatrixTheme.xaml
│   └── OceanTheme.xaml
│
└── Resources/                   # Icons, sounds
```

---

## Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| [NAudio](https://github.com/naudio/NAudio) | 2.2.1 | Audio capture |
| [Hardcodet.NotifyIcon.Wpf](https://github.com/hardcodet/wpf-notifyicon) | 2.0.0 | System tray icon |
| System.Text.Json | 9.0.2 | JSON config serialization |

---

## Contributing

Pull requests are welcome. For major changes, please open an issue first to discuss what you would like to change.

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/my-feature`)
3. Commit your changes (`git commit -m 'Add my feature'`)
4. Push to the branch (`git push origin feature/my-feature`)
5. Open a Pull Request

---

## License

[MIT](LICENSE)
