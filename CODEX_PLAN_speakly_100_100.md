# Speakly 100/100 Roadmap

## Source
Recovered from local Codex session history on March 7, 2026 and saved to the repo so future work starts from a checked-in markdown plan.

Original session source:
- `C:\Users\gorno\.codex\sessions\2026\03\05\rollout-2026-03-05T14-15-42-019cc012-2ae0-7f20-9fd8-a00864ca76aa.jsonl`

This is the revised version you approved after removing the provider-expansion section.

## Summary
Keep the current provider set. Do **not** spend the next cycle on new STT/refinement providers.

The highest-value path now is product quality:
1. better live dictation flow
2. faster correction after insertion
3. smarter task modes
4. context-aware refinement
5. stronger personalization
6. actionable history and retry flows

This keeps Speakly focused on what users actually feel: fewer edits, fewer failures, and less friction.

## Priority Changes
### 1. Add a correction-command layer
Build a spoken command layer for post-dictation cleanup.

First command set:
- `delete that`
- `scratch that`
- `undo that`
- `select that`
- `backspace`
- `press enter`
- `tab`
- `insert space`

Behavior:
- command recognition runs after STT, before final insertion
- mixed mode by default: normal dictation plus recognized edit commands
- overlay shows when speech was interpreted as a command
- command execution is logged in history/telemetry as command actions, not transcript text

Public changes:
- new setting: `Enable voice edit commands`
- optional mode:
  - `Mixed`
  - `Dictation only`
  - `Commands only`

### 2. Add task modes as a first-class feature
Make `Mode` the main dictation concept, not raw prompt editing.

Ship built-in modes:
- `Plain Dictation`
- `Message`
- `Email`
- `Notes`
- `Code`
- `Custom`

Behavior:
- each mode defines default refinement behavior, formatting expectations, and command handling
- each profile can store its default mode
- quick switching from Home, overlay, and tray

Public changes:
- mode selector becomes visible on Home and Refinement pages
- advanced custom prompt remains available, but is secondary to mode selection
- onboarding should introduce modes early

### 3. Add context-aware refinement
Use optional context to improve final output quality.

Context sources:
- active app/process
- window title
- selected text
- clipboard text

Behavior:
- context is used only for refinement / formatting decisions
- raw STT remains provider-driven
- context use must be opt-in and visible
- history should record which context sources were applied

Public changes:
- new settings toggles for each context source
- overlay badge or status text when contextual refinement is active
- privacy language must be explicit in UI

### 4. Improve personalization
Make Speakly adapt to the user over time.

Add:
- learn-from-corrections pipeline
- app/profile-scoped dictionary growth
- spoken snippets / text expansions
- style presets per profile

First style presets:
- `Neutral`
- `Casual`
- `Formal`
- `Custom`

Behavior:
- manual corrections can be promoted to dictionary/snippet suggestions
- snippets are inserted after refinement, before final text insertion
- per-profile style preference influences refinement prompt/mode behavior

Public changes:
- new `Snippets` management UI
- new toggle: `Learn from corrections`
- profile settings gain `Style` preference

### 5. Make history actionable
Turn history from a passive log into a recovery surface.

Add actions:
- `Retry insert`
- `Reprocess with current mode`
- `Copy original`
- `Copy refined`
- `Pin important entry`

Optional later addition:
- retain audio for reprocessing behind an explicit privacy toggle

Behavior:
- text-only reprocess is available without saved audio
- retries should preserve metadata so the user can compare before/after
- history filters should support action/result states, not just provider/profile

Public changes:
- history cards get action buttons
- new setting: `Retain audio for reprocessing` if audio retention is added later

## Implementation Changes
### UX and shell
- Keep Speakly keyboard-first.
- Surface `Mode`, `Command status`, and `Context used` in the Home page and overlay.
- Reduce hidden behavior: if refinement, context, or commands altered the final result, show it clearly.

### App logic
- Add a post-STT pipeline stage order:
  1. raw transcript
  2. command detection/execution
  3. optional contextual refinement
  4. snippet/style transforms
  5. insertion
- Preserve original and transformed text separately for history and troubleshooting.

### Data/config
Add config support for:
- active mode
- per-profile default mode
- voice command enablement/mode
- context source toggles
- learn-from-corrections toggle
- snippet definitions
- profile style preference
- optional audio-retention policy for history reprocessing

## Test Plan
Core scenarios:
- normal short dictation into Notepad/VS Code/browser/chat app
- mixed dictation with spoken edit commands
- same text through multiple modes: plain, message, email, code
- refinement with and without context
- correction-driven dictionary/snippet suggestion flow
- history reprocess and retry insert flow
- privacy-off vs context-enabled behavior

Acceptance criteria:
- command phrases do not leak into inserted text when recognized as commands
- modes produce visibly better formatting for their intended use cases
- context improves output quality without surprising the user
- history actions work without corrupting original data
- personalization features improve repeat proper nouns / repeated phrases over time
- all new behavior remains understandable from overlay/history/status text

## Assumptions
- Current providers stay unchanged for this roadmap.
- Speakly remains a dictation app, not a full voice-control assistant.
- Privacy-sensitive features must remain opt-in and auditable in UI/history.
- Advanced prompt editing stays available, but no longer acts as the primary product surface.
