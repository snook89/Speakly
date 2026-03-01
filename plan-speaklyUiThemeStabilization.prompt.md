## Layout Reset (Modern UI Direction)

Current issue: even with better theming, the existing multi-tab form-first layout feels dated and visually heavy.

### Modern layout options

1. **Two-pane Settings Shell (Recommended)**
	- Left rail navigation (General, Audio, Transcription, Refinement, API Keys, History).
	- Right content pane with card sections, larger spacing, cleaner hierarchy.
	- Sticky top header with app title + quick actions (Save, Test APIs, Refresh Models).
	- Why better: familiar modern desktop pattern, fast scanning, less visual noise.

2. **Single-page Sectioned Dashboard**
	- One scrollable page with grouped cards and anchor links at the top.
	- Progressive disclosure for advanced fields (collapsed by default).
	- Why better: minimal navigation complexity, easier for first-time users.

3. **Wizard-like Setup + Advanced Settings**
	- First-run quick setup flow (API keys, mic, model), then advanced page.
	- Why better: excellent onboarding, but higher implementation complexity.

### Visual language upgrades

- Replace dense inline controls with grouped cards and consistent vertical rhythm.
- Increase whitespace and typography contrast for modern readability.
- Remove legacy-looking hard borders and use subtle elevation/sections.
- Keep interactions predictable: clear primary actions, quieter secondary actions.

### Preferred execution path

- Adopt Option 1 (Two-pane Settings Shell).
- Keep existing view model logic and commands; change layout/composition only in phase 1.
- After shell migration, apply WPF-UI-first theme stabilization from the plan below.

---

## Plan: WPF-UI First Theme Stabilization

Goal is to fix theme rendering by prioritizing consistency over custom visuals, while keeping WPF-UI in fixed dark mode (your decision) and making overlay status colors theme-semantic. The root issue from discovery is mixed styling ownership: WPF-UI semantic visuals plus many local custom overrides. This plan reduces local style overrides, aligns controls to WPF-UI behavior, and keeps your custom themes as token providers for app-specific surfaces and overlay statuses. It is scoped to UI structure and theming only, with minimal logic impact.

**UI ideas shortlist**
- Idea A (Minimal): Keep current tabs and layout; remove only high-conflict local brushes/templates to let WPF-UI state visuals render correctly.
- Idea B (Balanced): Keep layout, replace custom tab template and button look with WPF-UI-native styles, preserve branding in header/spacing/typography.
- Idea C (Structured): Same as B plus a dedicated app token map for overlay/status/info severities shared by all themes.

**Steps**
1. Baseline current theme behavior and conflicts by auditing local brush/template overrides in [MainWindow.xaml](MainWindow.xaml) and status color hard-coding in [App.xaml.cs](App.xaml.cs#L186-L385) and [FloatingOverlay.xaml.cs](FloatingOverlay.xaml.cs).
2. Normalize theme dictionaries in [Themes/DarkTheme.xaml](Themes/DarkTheme.xaml), [Themes/LightTheme.xaml](Themes/LightTheme.xaml), [Themes/MatrixTheme.xaml](Themes/MatrixTheme.xaml), and [Themes/OceanTheme.xaml](Themes/OceanTheme.xaml) so each defines the same complete token set for app surfaces and overlay statuses.
3. Refactor settings window styling to WPF-UI-first by removing or reducing local control-level background/foreground overrides and custom template conflicts in [MainWindow.xaml](MainWindow.xaml#L25-L462).
4. Keep WPF-UI fixed dark setup in [App.xaml](App.xaml#L7-L12), but ensure custom tokens never fight WPF-UI control state rendering; apply app tokens only where WPF-UI does not provide semantic state behavior.
5. Move overlay status colors to theme resources (Ready/Recording/Transcribing/Error) by replacing hard-coded brushes in [App.xaml.cs](App.xaml.cs#L186-L385) and consuming those resources in [FloatingOverlay.xaml](FloatingOverlay.xaml) and [FloatingOverlay.xaml.cs](FloatingOverlay.xaml.cs).
6. Validate startup and runtime theme switching flow in [App.xaml.cs](App.xaml.cs#L408-L449) to ensure dictionary replacement remains stable and no missing-key fallback paths are hit.
7. Polish contrast/accessibility consistency on key surfaces (Tab headers, input fields, warning/info bars, overlay pill) inside [MainWindow.xaml](MainWindow.xaml) and [FloatingOverlay.xaml](FloatingOverlay.xaml) without introducing new components.

**Verification**
- Build and launch checks: dotnet build -c Release, then run local and published executable startup checks.
- Manual UI checks per theme: Dark, Light, Matrix, Ocean.
- Interaction checks: hover/pressed/disabled states for buttons, combo boxes, text boxes, tab selection, context menu, and overlay state transitions.
- Regression checks: hotkey capture visuals, API key reveal controls, model list refresh info bar, overlay visibility and status transitions.

**Decisions**
- Direction: Option 2 (WPF-UI First).
- Theme mode: Keep WPF-UI fixed dark, custom themes handle app-level tokenization.
- Priority: Consistency over preserving current exact look.
- Overlay: Theme-semantic status colors across all themes.

DRAFT ready for approval; after approval, implementation can proceed in this exact order.
