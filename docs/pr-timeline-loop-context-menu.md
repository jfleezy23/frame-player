# Timeline Loop Context Menu Follow-up

## Summary

This change finishes the timeline-context looping and clipping pass and hardens the multi-wrap playback path that was still choking after the first loop on real corpus files.

## What Changed

- Added right-click timeline commands on the main timeline and compare timelines for:
  - `Set Position A Here`
  - `Set Position B Here`
  - `Loop Playback`
  - `Save Loop As Clip...`
- Enforced frames-first A/B ordering from the timeline menus so `B` cannot be set before `A`.
- Kept shared-loop behavior on the main timeline and pane-local behavior on the Primary and Compare timelines.
- Added startup `--open-file` support so visible UI automation can launch directly into a media file.
- Added a tracked visible UI verifier at `scripts/ui_loop_visible_test.py` that drives the real timeline context menu and proves repeated loop wraps.
- Expanded the hidden regression harness so loop playback must survive repeated wraps instead of just one.
- Preserved the no-marker `Loop Playback` path for full-media restart, so enabling loop without A/B markers still wraps at end-of-media instead of stopping after one pass.

## Root Cause

The first-wrap choke was real. After loop restart, playback could resume from a cache-landed loop-in frame without realigning the decoder state for forward playback. The app would continue to report `Playing`, but the frame clock could stay pinned at the loop-in frame on later wraps, especially on the corpus `.mov` path.

The fix realigns playback state before resumed playback when a seek was satisfied from cache, refreshes workspace state after loop restarts, and keeps the loop regression focused on repeated wraps rather than single-pass success.

## User Impact

- Right-click timeline loop control is now available where expected.
- Loop playback no longer dies after the first pass in the verified `.mov` repro.
- Clip export remains available from the timeline loop flow.
- Repeated loop behavior is now covered by both hidden regression and a real visible-window verifier.

## Validation

### Live Visible UI Verification

- `scripts/ui_loop_visible_test.py` against `Audio_Video_Sync_23,98_HEVC_1080p-by_PhotoJoseph.mov`
- Report: `artifacts/ui-loop-visible-test-fresh-desktop.json`
- Result: `success=true`, `observed_wraps=3`, `playback_stayed_active=true`

### Supported Full Corpus

- Default backend: `artifacts/regression-loop-export-full-corpus-20260414/regression-suite-report.json`
  - `9` supported files
  - `440` checks
  - `427` pass / `13` warning / `0` fail
- Forced CPU backend: `artifacts/regression-loop-export-full-corpus-20260414-cpu/regression-suite-report.json`
  - `9` supported files
  - `440` checks
  - `427` pass / `13` warning / `0` fail

### Targeted Audio-Bearing App Regression

- `artifacts/qa-loop-export-audio-manual/01-Audio_Video_Sync_23_98_HEVC_1080p-by_PhotoJoseph/regression-suite-report.json`
- `56` checks
- `54` pass / `2` warning / `0` fail
- Includes passing:
  - `ui-loop-main-playback-multiwrap`
  - `ui-loop-export-main`
  - `ui-loop-export-main-duration`

### Targeted Full-Media Loop Regression

- `artifacts/regression-full-media-loop-fix/01-Audio_Video_Sync_23_98_HEVC_1080p-by_PhotoJoseph/regression-suite-report.json`
- `58` checks
- `56` pass / `2` warning / `0` fail
- Includes passing:
  - `ui-loop-full-media-playback-wrap`
  - `ui-loop-main-playback-multiwrap`
  - `ui-loop-export-main`

## Remaining Warnings

The remaining warnings are still the intentional frames-first pre-index ones:

- `seek-to-time-before-index-ready`
- `ui-pre-index-click-seek`
