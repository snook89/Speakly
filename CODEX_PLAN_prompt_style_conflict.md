# Prompt and Style Conflict Plan

Created: March 7, 2026

## Goal
Prevent confusing tone conflicts between the base prompt from `PROMPTS` and the selected `Style preset`.

## Scope
1. Detect likely tone conflicts between the base prompt and style preset.
2. Surface a warning in the Refinement UI when a conflict is detected.
3. Add explicit prompt precedence so tone/style instructions win over conflicting tone wording in the base prompt.

## Rules
- Base prompt remains the main system prompt.
- Dictation mode controls task/format behavior.
- Style preset controls tone/phrasing bias.
- If tone instructions conflict, style preset takes precedence for tone only.

## Out Of Scope
- automatic rewriting or stripping of the user's base prompt
- editing saved prompts automatically
- deep NLP conflict detection beyond practical heuristics
