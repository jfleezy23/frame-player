# Frame Player v1.7.0 Release Note

This note documents the `v1.7.0` release. It is the maintainer-facing summary of what is new on top of `v1.6.0`, what remains intentionally deferred, and what validation evidence backs this release.

## What Is New In v1.7.0

- Timeline right-click loop control now ships across the reviewed playback surface:
  - the main timeline exposes `Set Position A Here`, `Set Position B Here`, `Loop Playback`, and `Save Loop As Clip...`
  - compare mode exposes the same right-click loop and clip actions on the Primary and Compare timelines
- Frames-first marker gating remains the contract:
  - `B` cannot be set before `A`
  - marker-dependent actions stay blocked until exact frame identity is available
  - pending markers can still drive time-bounded loop playback while the engine upgrades restart points to exact frames
- Loop playback restart is hardened:
  - repeated wraps now survive the real corpus `.mov` path instead of choking after the first pass
  - full-media loop playback with no A/B markers still wraps at end-of-media instead of stopping after one pass
- Regression coverage expanded again:
  - hidden-window regression now proves repeated loop wraps instead of a single restart
  - full-media no-marker loop playback is covered by an app-driven regression check
  - the visible timeline context-menu verifier remains available at `scripts/ui_loop_visible_test.py`

## What Stays Deferred

- Clip export still ships as one MP4 clip at a time.
- No batch export, alternate container chooser, codec chooser, or comparison-render movie ships in `v1.7.0`.
- Unsupported `.ts` corpus entries remain intentionally outside the supported trim/export regression surface.

## Runtime And CI Truth

- Product version: `v1.7.0`
- Current published clean-runner bootstrap asset: `v1.5.0`
- `Runtime\manifests\win-x64\runtime-manifest.json` remains pinned to the verified runtime bundle because the playback DLL payload itself did not change for this release.

## Validation Evidence

Green validation captured for this release:

- PR `#20` merged with green GitHub checks:
  - Windows CI
  - SonarCloud Code Analysis
  - CodeQL
  - dependency review
- Targeted packaged `.mov` regression:
  - file tested: `Audio_Video_Sync_23,98_HEVC_1080p-by_PhotoJoseph.mov`
  - checks run: `58`
  - pass / warning / fail: `56 / 2 / 0`
  - includes passing:
    - `ui-loop-full-media-playback-wrap`
    - `ui-loop-main-playback-multiwrap`
    - `ui-loop-export-main`
- Supported full corpus, default backend:
  - files tested: `9`
  - checks run: `458`
  - pass / warning / fail: `445 / 13 / 0`
- Supported full corpus, forced CPU backend:
  - files tested: `9`
  - checks run: `458`
  - pass / warning / fail: `445 / 13 / 0`
- Visible-window verification:
  - `scripts/ui_loop_visible_test.py` against the corpus `.mov`
  - result: `success=true`, `observed_wraps=3`, `playback_stayed_active=true`

Known non-blocking warnings remain the intentional frames-first pre-index ones:

- `seek-to-time-before-index-ready`
- `ui-pre-index-click-seek`

## Release Guidance

- Treat `v1.7.0` as the feature release that makes timeline right-click loop/clipping control part of the shipped workflow.
- Keep `Properties\AssemblyInfo.cs` as the canonical product-version source.
- Keep the app-driven regression suite as the source of truth for loop/export validation; the Sonar coverage exclusions intentionally reflect that desktop-path testing model.
- Release assets for this cut are expected to include:
  - `FramePlayer-CustomFFmpeg-1.7.0.zip`
  - the usual `bin\TestDrop` package output
