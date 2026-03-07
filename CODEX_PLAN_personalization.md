# Personalization Implementation Plan

Created: March 7, 2026

Parent roadmap:
- [CODEX_PLAN_speakly_100_100.md](D:\Coding\Developmant\Speakly\Speakly\CODEX_PLAN_speakly_100_100.md)
- [CODEX_PLAN_speakly_100_100_STATUS.md](D:\Coding\Developmant\Speakly\Speakly\CODEX_PLAN_speakly_100_100_STATUS.md)

## Goal
Finish the missing personalization slice from the roadmap by making refinement behavior more profile-specific and easier to steer without editing the full system prompt.

## Scope
1. Keep the existing `Learn from refinement corrections` toggle and confirm it remains profile-backed.
2. Add style presets:
- `Neutral`
- `Casual`
- `Formal`
- `Custom`
3. Add profile-backed custom style instructions for the `Custom` preset.
4. Feed the selected style preset into refinement prompt construction.
5. Surface style controls clearly in the Refinement page.
6. Update tests for preset normalization and prompt construction.

## Implementation Notes
- Treat style preset as a lightweight instruction layer, separate from dictation mode.
- Dictation mode controls task shape.
- Style preset controls tone and phrasing bias.
- `Custom` style should append a user-authored style instruction block only when non-empty.
- Existing saved configs and profiles should migrate safely to `Neutral`.

## Out Of Scope
- Automatic style inference
- Multiple custom style presets
- Reworking the prompt-library feature
- Moving personalization controls to a separate page

## Verification
- Focused unit tests for prompt building and style normalization
- No full restore/build dependency on network if local packages are unavailable
