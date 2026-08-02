# Frame Player Coworker Beta Note

## What this build does well

- Single-pane review remains the primary workflow.
- Exact frame stepping and seek correctness remain the priority.
- Two-pane compare mode is available for side-by-side manual analysis.
- Compare alignment is frame-first:
  - if the source pane has exact frame identity, alignment uses that frame
  - if exact frame identity is not available yet, alignment falls back to presentation time and the UI says so explicitly

## Compare mode in one minute

1. Turn on `Two-Pane Compare`.
2. Load a video in the left pane and a second video in the right pane.
3. Use the top master controls when you want to operate both loaded panes together.
4. Use the pane-local `Back`, `Play`, `Pause`, and `Next` buttons when you want to act on one pane directly.
5. Use `Align Left to Right` or `Align Right to Left` for one-shot alignment.

## What to watch while testing

- The compare status line should say:
  - `same frame` or a signed frame delta when both panes have exact frame identity
  - `time-based only` when one or both panes do not currently have exact frame identity
- The `Last alignment` line should say whether the last align used:
  - `exact frame`
  - `time fallback`
- Pane badges should stay truthful:
  - exact frame numbers when known
  - `Frame pending` when exact frame identity is not yet available

## Known limitations that are intentional right now

- No continuous synchronization
- No drift correction
- No master playback clock
- No persisted compare layout or compare session restore
- Time fallback can occur during pre-index states, especially on heavier files

## Suggested beta smoke pass

- Single-pane: open, play, pause, seek, step backward, step forward
- Compare mode: load two panes, use pane-local controls independently, then confirm the top master controls affect both panes
- Run one exact-frame alignment case
- Run one truthful time-fallback case on a heavier file
