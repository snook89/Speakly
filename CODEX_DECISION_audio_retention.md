# Audio Retention Decision

Date: March 7, 2026

Related roadmap files:
- [CODEX_PLAN_speakly_100_100.md](D:\Coding\Developmant\Speakly\Speakly\CODEX_PLAN_speakly_100_100.md)
- [CODEX_PLAN_speakly_100_100_STATUS.md](D:\Coding\Developmant\Speakly\Speakly\CODEX_PLAN_speakly_100_100_STATUS.md)

## Decision
Defer retained-audio reprocessing for now.

## Reasoning
- Speakly now has strong text-only recovery through:
  - `Retry insert`
  - `Reprocess with current mode`
  - copy original/refined actions
  - pinned history
  - before/after comparison for recovery entries
- Retaining audio introduces a real privacy cost, not just implementation work.
- It would also require:
  - explicit user consent
  - retention duration controls
  - deletion workflows
  - additional UI copy and privacy messaging
  - storage/error handling for audio lifecycle
- That complexity is not justified until text-only recovery proves insufficient in real usage.

## Current Product Position
- Supported recovery path: text-only history recovery
- Not supported: replaying or reprocessing retained audio

## Revisit Conditions
Re-open this only if one or more of these become true:
- users frequently need to recover from bad STT without a usable original transcript
- there is a strong product case for model-upgrade reprocessing from original audio
- privacy UX and retention policy are designed first, not bolted on afterward

## If Reopened Later
Start with a new markdown plan before implementation that covers:
- privacy toggle wording
- retention period
- deletion controls
- storage location and limits
- history UI for audio-backed entries
- failure modes and cleanup behavior
