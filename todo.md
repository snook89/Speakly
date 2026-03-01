# Speakly Development Todo

## UI Refinement
- [x] **Refine API Test Button**: 
    - Rename to "Test Keys"
    - Style it like the "SAVE SETTINGS" button (blue/accent, bold)
    - Ensure it's only in the API Keys tab
- [x] **Audio Settings Tab**:
    - Add fields for Sample Rate (Hz), Chunk Size (ms/bytes), and Channels.
    - Add descriptive hints for optimal settings.
- [x] **Debug Records**:
    - Add "Save Debug Records" checkbox.

## Features & Logic
- [x] **Config Updates**: Added `SampleRate`, `Channels`, `ChunkSize`, and `SaveDebugRecords`.
- [x] **Recording Management**: 
    - Timestamped saving to "Records" folder implemented.
    - Temporary buffer cleanup ensured.
- [x] **Audio Parameter Integration**: Parameters passed to `NAudioRecorder` and `DeepgramTranscriber`.

## Deep Investigation: Text Insertion
- [x] **Investigation**: Identified Win11 throttling and UIPI as likely causes.
- [x] **Implementation**: Added 2ms inter-character delay and elevation warning.

## Review
- [x] Final build successful.
- [x] UI matches user requirements.
