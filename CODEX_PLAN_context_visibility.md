# Context Visibility Implementation Plan

Created: March 7, 2026

Parent roadmap:
- [CODEX_PLAN_speakly_100_100.md](D:\Coding\Developmant\Speakly\Speakly\CODEX_PLAN_speakly_100_100.md)
- [CODEX_PLAN_speakly_100_100_STATUS.md](D:\Coding\Developmant\Speakly\Speakly\CODEX_PLAN_speakly_100_100_STATUS.md)

## Goal
Make contextual refinement visible during live use, not only after the fact in History.

## Scope
1. Add an overlay context badge that appears when contextual refinement actually used context.
2. Show the last-used context sources in overlay substatus text.
3. Surface context configuration and last-used context on the Home page.
4. Keep the visibility tied to real captured context, not just enabled settings.
5. Clear stale context when a new recording starts.

## Implementation Notes
- Use the existing `ContextSummary` string as the single source of truth for which sources were applied.
- Keep the overlay badge compact, based on abbreviated source names.
- Preserve exact context text in tooltip/home status rather than crowding the overlay.
- Do not change refinement behavior in this slice; only improve surfacing.

## Out Of Scope
- New context sources
- Privacy-policy copy changes
- History redesign
- Model/prompt tuning

## Verification
- Focused source inspection and existing no-restore test command where possible
- Manual smoke test:
  - selected text on
  - clipboard on
  - run one dictation with context and one without
  - confirm overlay badge and Home status update correctly
