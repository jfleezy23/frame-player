# Frame Player v1.8.1 Release Note

This note documents the `v1.8.1` release. It is a maintenance release on top of `v1.8.0` that keeps the existing frame-first review behavior and export feature set while tightening release validation and repo-carried harness correctness.

## What Changed In v1.8.1

- No new playback, seek, frame-step, or export semantics were introduced for this release.
- Release validation was rerun on the full supported local corpus before cutting the release.
- The repository-carried packaged regression harness now honors `-Recurse` when `-CorpusPath` is used, so the default full-corpus path no longer silently truncates nested media coverage.

## What Stays The Same

- The current product surface still includes the `v1.8.0` compare export, loop export, audio insertion, zoom/pan, and inspector work.
- Frame-first correctness remains the gating rule: pending absolute frame identity before background index readiness is still surfaced honestly instead of inventing a frame number.
- No new synchronous work was added to the decode, presentation, or playback hot paths as part of this release prep.

## Runtime And CI Truth

- Product version: `v1.8.1`
- Current pinned clean-runner runtime bootstrap asset: `v1.5.0`
- `Runtime\runtime-manifest.json` and `Runtime\export-tools-manifest.json` remain the runtime and tooling integrity sources of truth.

## Validation Evidence

Green validation captured for this release:

- Local compile validation:
  - tool: .NET SDK `10.0.202`
  - command: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Build-FramePlayer.ps1 -Configuration Release`
  - result: `Build succeeded`
- Repository harness syntax validation:
  - command: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Test-RepoHarnessScripts.ps1`
  - result: `Repository harness scripts are present and syntactically valid.`
- Full review-engine manual sweep:
  - command: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Run-ReviewEngine-ManualTests.ps1 -Path "C:\Projects\Video Test Files" -Recurse -Configuration Release -Output ".\artifacts\review-engine-manual-tests-full"`
  - files tested: `17`
  - result rows emitted: `136`
  - pass / warning / fail / advisory: `93 / 9 / 0 / 34`
- Full packaged regression corpus:
  - command: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Run-RegressionSuite.ps1 -CorpusPath "C:\Projects\Video Test Files" -Recurse -MaxCorpusFiles 0 -Configuration Release -Output ".\artifacts\regression-suite-full"`
  - files tested: `18`
  - note: the run includes the repo-carried `dist\Frame Player\sample-test.mp4` plus the `17` supported files from the external local corpus
  - checks run: `1358`
  - pass / warning / fail: `1329 / 29 / 0`

Known non-blocking warnings remain the intentional frames-first pending-index and tiny-clip coverage skips:

- `seek-to-time-before-index-ready`
- `ui-pre-index-click-seek`
- short-clip loop coverage skips where the corpus sample does not have enough indexed frames to make the scenario meaningful

## Release Guidance

- Treat `v1.8.1` as a maintenance release that preserves the current user-facing behavior while making release validation more trustworthy.
- Keep `Properties\AssemblyInfo.cs` as the canonical product-version source.
- Keep the app-driven regression suite as the source of truth for packaged playback/export validation.
- Release assets for this cut are expected to include:
  - `FramePlayer-CustomFFmpeg-1.8.1.zip`
  - the usual `bin\TestDrop` package output
