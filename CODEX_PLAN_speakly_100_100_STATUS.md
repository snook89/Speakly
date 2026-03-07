# Speakly 100/100 Roadmap Status

Last reviewed: March 7, 2026

Plan source:
- [CODEX_PLAN_speakly_100_100.md](D:\Coding\Developmant\Speakly\Speakly\CODEX_PLAN_speakly_100_100.md)

## Summary
The roadmap is partly implemented.

Completed or largely completed:
- correction-command layer
- task modes as a first-class feature
- context-aware refinement
- personalization basics
- context visibility polish
- advanced history recovery basics
- actionable history basics

Partially implemented:
- retained-audio reprocessing decision

## Status By Roadmap Item
### 1. Correction-command layer
Status: mostly done

Implemented:
- voice command recognition for `delete that`, `scratch that`, `undo that`, `select that`, `backspace`, `press enter`, `tab`, and `insert space`
- mixed / dictation only / commands only modes
- command execution through insertion logic
- command handling surfaced in history and overlay status

Key references:
- [Services/DictationExperienceService.cs](D:\Coding\Developmant\Speakly\Speakly\Services\DictationExperienceService.cs)
- [TextInserter.cs](D:\Coding\Developmant\Speakly\Speakly\TextInserter.cs)
- [Pages/GeneralPage.xaml](D:\Coding\Developmant\Speakly\Speakly\Pages\GeneralPage.xaml)
- [App.xaml.cs](D:\Coding\Developmant\Speakly\Speakly\App.xaml.cs)

### 2. Task modes as a first-class feature
Status: mostly done

Implemented:
- built-in modes exist: `Plain Dictation`, `Message`, `Email`, `Notes`, `Code`, `Custom`
- prompt behavior varies by mode
- mode is stored in config and profiles
- mode selector exists on Refinement page
- quick mode switching from Home
- quick mode switching from overlay
- quick mode switching from tray
- active mode surfaced more clearly on Home and in the overlay

Missing or incomplete:
- stronger product emphasis on mode over prompt editing
- onboarding references for mode-first workflow

Key references:
- [Services/DictationExperienceService.cs](D:\Coding\Developmant\Speakly\Speakly\Services\DictationExperienceService.cs)
- [Pages/RefinementPage.xaml](D:\Coding\Developmant\Speakly\Speakly\Pages\RefinementPage.xaml)
- [Pages/HomePage.xaml](D:\Coding\Developmant\Speakly\Speakly\Pages\HomePage.xaml)
- [MainViewModel.cs](D:\Coding\Developmant\Speakly\Speakly\MainViewModel.cs)

### 3. Context-aware refinement
Status: largely done

Implemented:
- active app name, window title, selected text, and clipboard toggles
- context capture plumbing
- aggressive vs conservative contextual rewrite behavior
- safety guard for aggressive rewrite drift
- context mode shown in history
- overlay context badge and live context source visibility
- Home page visibility for context configuration and last-used context

Missing or incomplete:
- privacy wording could still be polished further in UI copy

Key references:
- [Services/RefinementContextCaptureService.cs](D:\Coding\Developmant\Speakly\Speakly\Services\RefinementContextCaptureService.cs)
- [Services/DictationExperienceService.cs](D:\Coding\Developmant\Speakly\Speakly\Services\DictationExperienceService.cs)
- [RefinementSafety.cs](D:\Coding\Developmant\Speakly\Speakly\RefinementSafety.cs)
- [Pages/RefinementPage.xaml](D:\Coding\Developmant\Speakly\Speakly\Pages\RefinementPage.xaml)
- [FloatingOverlay.xaml](D:\Coding\Developmant\Speakly\Speakly\FloatingOverlay.xaml)
- [FloatingOverlay.xaml.cs](D:\Coding\Developmant\Speakly\Speakly\FloatingOverlay.xaml.cs)
- [Pages/HistoryPage.xaml](D:\Coding\Developmant\Speakly\Speakly\Pages\HistoryPage.xaml)

### 4. Improve personalization
Status: mostly done

Implemented:
- snippets library and snippet expansion
- correction suggestion extraction and review queue
- manual save-as-dictionary / save-as-snippet flow
- explicit `Learn from refinement corrections` toggle
- profile-backed style presets: `Neutral`, `Casual`, `Formal`, `Custom`
- custom style instructions wired into refinement prompt construction

Missing or incomplete:
- stronger profile-scoped personalization surface

Key references:
- [SnippetLibraryManager.cs](D:\Coding\Developmant\Speakly\Speakly\SnippetLibraryManager.cs)
- [Services/RefinementLearningService.cs](D:\Coding\Developmant\Speakly\Speakly\Services\RefinementLearningService.cs)
- [Services/DictationExperienceService.cs](D:\Coding\Developmant\Speakly\Speakly\Services\DictationExperienceService.cs)
- [Pages/RefinementPage.xaml](D:\Coding\Developmant\Speakly\Speakly\Pages\RefinementPage.xaml)
- [MainViewModel.cs](D:\Coding\Developmant\Speakly\Speakly\MainViewModel.cs)

### 5. Make history actionable
Status: mostly done, with retained-audio reprocessing still undecided

Implemented:
- `Retry insert`
- `Reprocess with current mode`
- `Copy original`
- `Copy refined`
- `Pin important entry`
- richer history filtering by action and pin state
- compare-before-after view for retry/reprocess entries
- source-entry linking for history recovery actions

Missing or incomplete:
- optional retained-audio reprocessing

Key references:
- [HistoryManager.cs](D:\Coding\Developmant\Speakly\Speakly\HistoryManager.cs)
- [Pages/HistoryPage.xaml](D:\Coding\Developmant\Speakly\Speakly\Pages\HistoryPage.xaml)
- [Pages/HistoryPage.xaml.cs](D:\Coding\Developmant\Speakly\Speakly\Pages\HistoryPage.xaml.cs)
- [App.xaml.cs](D:\Coding\Developmant\Speakly\Speakly\App.xaml.cs)

## Recommended Next Implementation
Next priority:
- retained-audio reprocessing decision

Recommended scope:
- decide whether retained-audio reprocessing is worth the privacy tradeoff
- if yes, add an explicit privacy toggle and retention policy
- if no, close the roadmap with text-only recovery as the supported path

Reason:
- text-only history recovery is now in place
- the only significant remaining roadmap question is whether audio retention belongs in the product at all
