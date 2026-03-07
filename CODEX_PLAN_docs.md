# Speakly Docs Section Plan

## Summary
Add a new `Docs` entry in the footer navigation above `Info`, backed by a single in-app `DocsPage` with its own internal topic navigation. Docs v1 will cover all major app areas with curated native WPF content, not markdown, and each topic will explain what the feature does, how it works, recommended default settings, concrete text examples, and direct links back to the relevant settings page.

## Key Changes
- Add `Docs` to the footer nav directly above `Info`.
- Register a new `DocsPage` in the existing page-routing map.
- Keep `Info` unchanged as the product/about/update page.
- Build one `DocsPage` with a left topic list and right scrollable content panel.
- Default topic is `Overview`.
- Cover `Overview`, `General`, `Hotkeys`, `Audio`, `Transcription`, `Refinement`, `API Keys`, `History`, and `Statistics`.
- Use a typed docs content model and static provider instead of markdown parsing.
- Each topic will include: overview, what it does, how it works, recommended defaults, examples, common mistakes/gotchas, and an optional `Open settings page` action.
- `Open settings page` actions should navigate to the existing app page and keep sidebar selection in sync.

## Interfaces / Types
- Add immutable docs types for topic metadata and section content.
- Include stable topic keys, titles, summaries, recommended defaults, examples, and optional target page tags.
- Add a small docs view/state model for selected topic and navigation actions.

## Test Plan
- Verify all required docs topics exist and have non-empty core content.
- Verify any topic with a target page tag maps to a valid routed page.
- Verify `Docs` appears above `Info` and opens the new page.
- Verify the default selected topic is `Overview`.
- Verify `Open settings page` routes to the correct existing page.

## Assumptions and Defaults
- No search in v1.
- Text-only examples in v1.
- Docs content is authored manually in code.
- `Info` remains separate from Docs.
- Topics without dedicated settings pages do not show an `Open settings page` button.
