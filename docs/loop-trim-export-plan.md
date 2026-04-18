# Loop-Driven Clip Export Plan

## Summary

- Goal: turn reviewed A/B loop ranges into saved clip exports, with main-window shared-loop export first and compare-mode per-pane export second.
- Implementation kickoff once we leave Plan Mode: save this note as `docs/loop-trim-export-plan.md` and open stacked branches `codex/loop-trim-export-00-plan`, `codex/loop-trim-export-01-export-tools`, `codex/loop-trim-export-02-main-window-export`, and `codex/loop-trim-export-03-compare-pane-export`.
- Use a separate pinned FFmpeg CLI bundle for export. Do not repurpose the current in-process runtime: the existing FFmpeg build disables programs, encoders, and muxers, so folding export into it would destabilize review playback.

## Key Changes

- Add an isolated export-tools bundle under something like `Runtime\ffmpeg-tools\` with manifest validation, `ffmpeg.exe`, `ffprobe.exe`, and required colocated dependencies; build it from the official FFmpeg `n8.1` source with a dedicated script and carry it into the release verification and install outputs.
- Add internal export types/service: `ClipExportRequest`, `ClipExportPlan`, `ClipExportResult`, and `ClipExportService`. Service responsibilities: resolve the reviewed source file, compute exact start and exclusive end boundaries from the current loop snapshot, launch FFmpeg, capture stdout/stderr, and return a structured result.
- Keep `IVideoReviewEngine` unchanged for phase 1. Boundary resolution should be an internal FFmpeg-aware helper that uses ready loop markers plus the global frame index when available. End boundary rule: prefer the next indexed frame timestamp after `loop-out`, otherwise use `loop-out time + PositionStep`, clamped to media duration.
- Main-window UX: add `Playback > Save Loop As Clip...`, enabled only when single-pane media is open and the shared A/B range has both markers, is not pending, and is not invalid. Export pauses playback, snapshots the active loop/file state, opens a `SaveFileDialog`, writes exact MP4 output, and logs actionable failures.
- Default export contract: frame-accurate MP4 re-encode via FFmpeg CLI, with trim arguments placed for accurate cutting, `H.264 + AAC`, and `+faststart`. No stream-copy fast path, no source overwrite, and no open-ended one-marker exports in v1.
- Phase 2 compare UX: add per-pane `Save Loop As Clip...` on pane context menus and route the shared command to the active pane when compare mode is on. Phase 2 exports only the selected pane’s pane-local loop; it does not render a side-by-side comparison video.

## Recommended PRs

- `codex/loop-trim-export-00-plan`: add `docs/loop-trim-export-plan.md` only. Purpose: get the spec reviewed before code starts.
- `codex/loop-trim-export-01-export-tools`: add the isolated FFmpeg CLI bootstrap/build/manifest path, packaging updates, third-party notice updates, and `ClipExportService` scaffolding with no visible UI.
- `codex/loop-trim-export-02-main-window-export`: add main-window/shared-loop export UI, exact boundary resolution, background execution/status messaging, and single-pane export smoke coverage.
- `codex/loop-trim-export-03-compare-pane-export`: add per-pane compare export, pane-targeted command routing, compare smoke coverage, and release-note/readme/test-note updates.

## Test Plan

- Tooling: build on clean Windows CI, validate export-tool manifest integrity, and verify the release/distribution outputs include the isolated tool bundle without changing current playback runtime validation.
- Main-window export: exact MP4 export succeeds for one audio-bearing clip and one video-only clip; command is blocked for missing media, pending markers, invalid ranges, incomplete A/B ranges, and compare mode during phase 1.
- Boundary sanity: exported file exists, FFprobe-reported duration matches the reviewed range within tolerance, and hidden-window smoke confirms the first/last landed frames stay inside the selected loop window.
- Review-path regression: run the existing loop smoke on default review settings and forced CPU review settings, then run new clip-export smoke on the same sample set.
- Compare phase: primary and compare pane exports use the correct source file and pane-local loop, remain independent, and never fall through to a combined side-by-side render.

## Assumptions

- First shipped export is one-at-a-time exact MP4 output only; presets and alternate containers/codecs are out of scope for this feature line.
- Phase 1 is single-pane/shared-loop only. If compare mode is enabled, the export command explains that compare export ships in phase 2.
- Phase 2 exports one selected pane at a time from its pane-local loop only; no batch export and no composed comparison movie.
- If boundary resolution cannot be kept internal cleanly, add the narrowest possible internal FFmpeg-only seam rather than widening `IVideoReviewEngine` prematurely.
