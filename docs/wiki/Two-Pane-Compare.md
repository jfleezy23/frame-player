# Two-Pane Compare

Two-pane compare mode is designed for original vs processed review. The left pane is the primary video and the right pane is the compare video.

## Open Compare Mode

1. Turn on `Two-Pane Compare`.
2. Load the primary video in the left pane.
3. Load the compare video in the right pane.
4. Click a pane to make it active for keyboard, menu, and context-menu commands. Pane-local buttons always operate their own pane.

## Controls

- When both videos are loaded, the main transport controls are the shared master controls and operate across both panes.
- Pane-local controls operate only on their own pane, independently of the other pane.
- Each pane has previous frame, 100-frame rewind, play/pause, 100-frame fast forward, next frame, timeline, and frame entry.
- The main loop status controls loop playback for both panes together; each pane retains its own loop range.
- `Sync Right to Left` aligns the right pane to the left pane.
- `Sync Left to Right` aligns the left pane to the right pane.
- Master transport commands do not align the two videos first. Use a Sync command when you want one explicit alignment.
- `Link zoom` mirrors zoom changes between panes.

## Focused Pane Behavior

Click a pane to focus it. The focused pane should receive pane-local Open Recent behavior, frame entry, context-menu actions, and compare navigation.

## Context Menus

Right-click a video pane for pane-specific actions such as Video Info and related review commands.
