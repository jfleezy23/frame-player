# User Guide

## Main Review Window

The main window is organized around a video surface, timeline, transport controls, frame entry, loop status, and status bar. The Windows stable app and macOS Preview are intended to behave the same in the review body.

Key surfaces:

- Current video header: shows the active file state.
- Video surface: displays decoded frames or the empty-state prompt.
- Timeline: supports live scrubbing and lands paused on release.
- Transport controls: previous frame, rewind, play/pause, fast forward, next frame, and full screen.
- Frame entry: jumps directly to a 1-based frame number.
- Status bar: shows playback state, frame, timecode, runtime/cache state, and pointer coordinates.

## Playback And Stepping

- Space toggles play/pause.
- Left and Right step one frame while paused.
- Holding Left or Right repeats frame stepping.
- Rewind and fast-forward controls move by time on the shared transport.
- Pane-local controls in compare mode include single-frame and 100-frame navigation.

When audio output is active, playback uses the audio clock. Frame stepping and seek/jump operations remain decode/index based.

## Video Info And Diagnostics

Use `Help > Video Info` or the video pane context menu to inspect FFmpeg-reported media metadata. Use `File > Export Diagnostics` when reporting a problem; diagnostics are local files and are intended to redact absolute paths where practical.

## Full Screen

Use the full-screen button or the platform shortcut:

- Windows: `F11` or `Alt+Enter`
- macOS: native full-screen window control or the app menu equivalent
