# Speakly Next-Wave Competitive Roadmap

## Summary
Research basis for this roadmap:
- Wispr Flow official features: https://wisprflow.ai/features
- Superwhisper official docs: https://superwhisper.com/docs/modes/super and https://superwhisper.com/docs/get-started/transcribe-history
- Aqua official site: https://aquavoice.com/
- Microsoft voice typing / voice access: https://support.microsoft.com/en-us/windows/use-voice-typing-to-talk-instead-of-type-on-your-pc-fec94565-c4bd-329d-e59a-af033fa5689f and https://support.microsoft.com/en-us/topic/voice-access-command-list-dac0f091-87ce-454d-8d57-bef38d3d8563

Chosen product direction:
- Lane: Windows desktop pro
- Top gap to close: live editing while speaking
- Privacy bar: stay with selected-text / clipboard-only context, no deep active-editor scraping
- Model strategy: cloud-first
- Rollout: true in-field live editing, but profile opt-in first

## Competitive Findings
Speakly is already strong on:
- profiles and per-app switching
- post-process refinement and context toggles
- history retry / reprocess
- overlay visibility and diagnostics
- basic spoken edit commands

Speakly is still behind current leaders on:
- true live rewriting while speaking
- richer command grammar
- developer-specific dictation quality
- automatic personalization
- trust polish around live-state explanation and app compatibility

Explicitly deferred for this roadmap:
- team/shared assets and enterprise admin
- system audio / meeting / file-transcription workflows
- local/offline model stack
- deep app-context capture via accessibility APIs
- mobile / cross-device sync

## Key Changes
### 1. Add a true live composition engine
- Introduce a new per-profile setting: `Live editing`
  - `Off`
  - `In-field live edit`
- Default for existing profiles: `Off`
- Default for new profiles: `Off`
- When enabled, Speakly creates a live composition session after capture starts and updates only the current dictated region inside the target app as partial STT results evolve
- Final stop behavior commits the last live version and closes the composition session
- If live replacement fails, Speakly falls back to current end-of-utterance insertion and surfaces the fallback in overlay/history

Behavior decisions:
- Live editing is only attempted for profiles whose target apps are explicitly opted in
- The live session owns only the text inserted during the current utterance; it must not rewrite earlier unrelated document content
- If the user moves focus, changes selection unexpectedly, or the target app no longer matches the captured target, cancel live replacement and fall back safely
- Overlay must show `LIVE`, `LIVE FALLBACK`, or similar explicit status

### 2. Upgrade live correction behavior
- Extend the pipeline so partial transcript updates support:
  - backtracking within the active utterance
  - filler removal during live composition
  - live punctuation shaping
  - spoken numbered-list shaping when the dictated pattern is unambiguous
- Add a second command layer for live utterance correction:
  - keep current command set
  - add live-only utterance repair phrases such as `actually`, `no, make that`, and `scratch that` for the active composition region
- Keep commands focused on text editing and live dictation cleanup

### 3. Add a developer-quality dictation pack
- Add a profile-level `Writing domain` or `Accuracy profile` setting:
  - `General`
  - `Developer`
  - `Support`
  - `Custom`
- `Developer` must bias:
  - identifiers and symbol preservation
  - CLI command fidelity
  - camelCase / snake_case / acronyms
  - technical vocabulary retention
- Implement this without deep editor scraping:
  - use profile app match, selected text, clipboard, personal dictionary, and mode-specific prompt shaping
- Add a lightweight replacements/hotwords layer:
  - user-approved replacements for repeated technical terms
  - app/profile-scoped term boosting
  - one-click promote from correction suggestions

### 4. Strengthen automatic personalization
- Keep current manual suggestion flow, but add a higher-confidence auto-learning queue:
  - repeated user correction patterns become pending suggestions automatically
  - user can approve once for global or profile scope
- Add profile style memory:
  - optional per-profile bias learned from accepted refinements
  - bounded only to tone/format preference, not freeform content generation
- Add protected terms:
  - terms that must never be normalized away by refinement or live cleanup

### 5. Add trust and compatibility polish for premium feel
- Add live-edit compatibility state per profile:
  - `Unknown`
  - `Works well`
  - `Fallback recommended`
- Track live-edit success/fallback/failure in history and telemetry
- Add secure-field / unsafe-target suppression:
  - do not attempt live editing in obvious password or secure entry contexts
  - fall back to no insertion or one-shot insertion based on explicit product rule
- Add a compatibility/debug surface in Docs:
  - what live editing does
  - when it falls back
  - which apps are known-good or known-risky

## Public APIs / Settings / Types
- New profile-scoped setting: `Live editing` with `Off` and `In-field live edit`
- New profile-scoped setting: `Accuracy profile` with `General`, `Developer`, `Support`, `Custom`
- New profile-scoped compatibility state for live editing
- New history/telemetry fields:
  - whether session used live editing
  - whether it fell back
  - live target app
  - live replacement attempt count
- New docs/onboarding content for:
  - live editing behavior
  - safe rollout guidance
  - compatibility expectations

## Test Plan
Core scenarios:
- live dictation in Notepad with partial updates visible in-field
- live dictation with mid-utterance correction such as `actually`
- fallback when focus changes mid-utterance
- fallback when target app refuses replacement
- profile with live editing `Off` stays on current end-of-utterance behavior
- developer profile preserves identifiers and CLI strings better than general profile
- repeated accepted corrections generate pending personalization suggestions
- secure-field or unsupported target does not corrupt text

Acceptance criteria:
- live editing updates only the active utterance region
- fallback never duplicates text or deletes unrelated text
- overlay and history always reveal whether live editing or fallback occurred
- developer profile produces visibly better technical fidelity than general profile
- personalization remains reviewable and does not silently mutate user language beyond approved rules

## Assumptions and Deferrals
- This roadmap is intentionally not for team features, enterprise admin, mobile sync, meetings/files, local models, or deep accessibility-based editor reading
- Cloud providers remain the primary model path
- Selected text and clipboard remain the strongest allowed context sources
- The next product win condition is: Speakly feels faster, cleaner, and more reliable during the act of speaking than current competitors on Windows
