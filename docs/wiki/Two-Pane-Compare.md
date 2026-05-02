# Two-Pane Compare

Two-pane compare mode is designed for original vs processed review. The left pane is the primary video and the right pane is the compare video.

## Open Compare Mode

1. Turn on `Two-Pane Compare`.
2. Load the primary video in the left pane.
3. Load the compare video in the right pane.
4. Use the pane focus state to choose which pane receives pane-local commands.

## Controls

- Shared transport controls operate across panes when `All panes` is enabled and both videos are loaded.
- Pane-local controls operate on the focused pane.
- Each pane has previous frame, 100-frame rewind, play/pause, 100-frame fast forward, next frame, timeline, loop status, and frame entry.
- `Sync Right to Left` aligns the right pane to the left pane.
- `Sync Left to Right` aligns the left pane to the right pane.
- `Link zoom` mirrors zoom changes between panes.

## Focused Pane Behavior

Click a pane to focus it. The focused pane should receive pane-local Open Recent behavior, frame entry, context-menu actions, and compare navigation. On macOS, the right pane is expected to support focused `Open Recent` behavior like the Windows version.

## Context Menus

Right-click a video pane for pane-specific actions such as Video Info and related review commands. Menu colors should match the main menu treatment on the macOS Preview.
