# Speakly Safety-First Stabilization Pass

## Summary
The audit found four must-fix issues and a few smaller consistency gaps:

- History can overwrite prior sessions on the first save after restart.
- Clipboard-based insertion and selected-text capture can destroy non-text clipboard contents.
- Theme selection is effectively broken by a forced dark-theme override.
- Session start mutates the UI-selected profile and does too much work through the full profile setter path.

This pass should optimize for `Safety First` with `Moderate` UI change scope. It should fix the real bugs first, then add the minimum product-surface cleanup needed so the app stops being misleading. It should not do a large navigation redesign or a broad architecture rewrite yet.

## Key Changes

### 1. Fix real correctness and data-safety bugs
- **History persistence**
  - Add a one-time `EnsureLoaded()` guard in the history subsystem and call it from every mutating entrypoint, not just `GetHistory()`.
  - Ensure `AddEntry` and `SetPinned` never operate on an empty in-memory history when `history.json` already exists.
  - Keep retention/pinning behavior unchanged after load.

- **Clipboard preservation**
  - Replace text-only clipboard snapshot/restore with full `IDataObject` snapshot/restore in both insertion and selected-text capture paths.
  - If full restore fails, fall back safely with explicit logging and a user-visible failure code rather than silently degrading clipboard contents.
  - Keep the current clipboard-based insertion strategy; this pass fixes safety, not the whole insertion architecture.

- **Per-session refinement safety**
  - Stop reading aggressive/conservative rewrite mode from global config inside refiners.
  - Pass a session-scoped refinement request/context object so prompt composition, safety mode, and resolved profile all come from the same source of truth.
  - Keep current prompt behavior and safety heuristics; only fix the source-of-truth mismatch.

- **Theme handling**
  - Remove the hard override that forces non-dark themes back to dark during theme application.
  - Normalize only invalid theme values.
  - Persist theme changes only from explicit user actions, not from theme-application code.

- **Hotkey hook failure handling**
  - Validate hook registration at startup.
  - If the low-level hook fails, capture the Win32 error, surface it through health/status, and prevent the app from pretending hotkeys are working.

### 2. Remove misleading runtime/UI behavior
- **Runtime profile vs edited profile**
  - Separate `active session profile` from `currently selected/edited profile`.
  - Starting dictation must not call the heavy `SelectedProfile` setter path or trigger a full config/UI refresh.
  - Expose runtime profile as a read-only session indicator in Home/overlay/tray where needed.
  - Keep manual profile editing behavior intact.

- **Privacy and history controls**
  - Add a small `Privacy & History` section to General.
  - Expose `PrivacyMode` and `HistoryRetentionDays` directly in the UI because runtime/docs already depend on them.
  - Use existing runtime semantics; do not invent new privacy modes in this pass.

- **Default consistency**
  - Centralize default refinement-model selection so global config defaults and new-profile defaults cannot drift.
  - Use one source of truth for the default refinement model per provider.

### 3. Tight cleanup for trust and product consistency
- Update the Info page copy so it matches the real product: profiles, commands, context-aware rewrite, history recovery, Docs.
- Remove the duplicate `Next profile` affordance from Home and keep only one cycle control.
- Keep navigation structure unchanged in this pass.
  - No dedicated Profiles page yet.
- Keep README and in-app Docs aligned with the fixed behavior as part of the same implementation.

### 4. Explicitly defer larger follow-up work
- Defer parallel model refresh / `RefreshFromConfig()` decomposition to a later performance pass.
- Defer dedicated Profiles navigation/page redesign.
- Defer shutdown/lifecycle refactor around `Environment.Exit(0)` unless it becomes necessary while implementing the bug fixes above.

## Public Interfaces / Settings
- Add UI bindings for existing settings:
  - `PrivacyMode`
  - `HistoryRetentionDays`
- Introduce an internal session-scoped refinement request/context object so refiners stop reading safety mode from global config.
- Introduce an internal distinction between:
  - `active runtime profile`
  - `selected/edited profile`

## Test Plan
- **History**
  - Existing persisted history survives the first new `AddEntry` after app restart.
  - Pinning works before opening History UI.
  - Retention pruning still preserves pinned entries.

- **Clipboard**
  - Insertion restores clipboard text exactly when clipboard originally contained text.
  - Insertion preserves non-text formats through full snapshot/restore.
  - Selected-text capture does not destroy image/file/HTML clipboard content.

- **Refinement correctness**
  - If global mode is conservative and the resolved profile is aggressive, safety logic uses aggressive.
  - If global mode is aggressive and the resolved profile is conservative, safety logic uses conservative.

- **Theme**
  - Selecting a non-dark theme survives apply/save/reload.
  - Invalid theme values normalize without overwriting valid user choices.

- **Hotkeys**
  - Hook-registration failure produces a visible error/health state.
  - Normal startup still registers hotkeys and works unchanged.

- **Runtime profile separation**
  - Starting dictation in a mapped app uses the correct runtime profile without overwriting the currently edited profile in the UI.
  - Session start no longer triggers full config/profile editor churn.

- **UI consistency**
  - General page privacy/history controls round-trip correctly.
  - Info/README/Docs all describe the fixed product behavior consistently.
  - Home shows only one profile-cycling control.

## Assumptions and Defaults
- Priority is `Safety First`.
- UI change scope is `Moderate`, so small settings/UI cleanup is in scope, but no large navigation redesign.
- Privacy/history mismatch should be solved by adding controls, not trimming documentation.
- This pass should update `README.md` and in-app Docs together with the code changes.
- Large performance refactors are deferred until after the safety issues above are fixed.
