# History Recovery Implementation Plan

Created: March 7, 2026

Parent roadmap:
- [CODEX_PLAN_speakly_100_100.md](D:\Coding\Developmant\Speakly\Speakly\CODEX_PLAN_speakly_100_100.md)
- [CODEX_PLAN_speakly_100_100_STATUS.md](D:\Coding\Developmant\Speakly\Speakly\CODEX_PLAN_speakly_100_100_STATUS.md)

## Goal
Finish the remaining history-focused roadmap slice by making History better for recovery and comparison, not just logging.

## Scope
1. Add richer filtering by action and pin state.
2. Keep success/fail filtering and existing search/provider/profile filters.
3. Add source-entry metadata for `Retry insert` and `Reprocess`.
4. Show a compare-before-after block for recovery entries.
5. Keep current retry/reprocess/pin/copy actions intact.

## Implementation Notes
- Use explicit source-entry fields rather than trying to infer relationships later.
- `HistoryRetry` entries should point back to the original source entry even if text is unchanged.
- `HistoryReprocess` entries should show the previous refined text so users can compare the old and new output.
- Keep the UI lightweight: compact filters plus inline compare block inside each card.

## Out Of Scope
- retained-audio reprocessing
- full diff rendering
- cross-session history analytics
- export features

## Verification
- focused unit tests for history-entry comparison helpers
- local build/test once restore state is available
