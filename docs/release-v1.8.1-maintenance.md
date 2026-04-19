# Frame Player v1.8.1 Release Note

This note documents the `v1.8.1` release. It is a maintenance release on top of `v1.8.0` that keeps the existing frame-first review behavior and export feature set while tightening release validation, aligning the active supported review surface, and hardening the shipped runtime surface.

## What Changed In v1.8.1

- No new playback, seek, frame-step, or export semantics were introduced for this release.
- Release validation was rerun on the full supported local corpus before cutting the release.
- The repository-carried packaged regression harness now honors `-Recurse` when `-CorpusPath` is used, so the default full-corpus path no longer silently truncates nested media coverage.
- `.mov` is no longer part of the active supported review surface or the default repo-carried validation corpus.
- The shipped app output now carries the DLL-only `ffmpeg-export` runtime for probe/export work.

## What Stays The Same

- The current product surface still includes the `v1.8.0` compare export, loop export, audio insertion, zoom/pan, and inspector work.
- Frame-first correctness remains the gating rule: pending absolute frame identity before background index readiness is still surfaced honestly instead of inventing a frame number.
- No new synchronous work was added to the decode, presentation, or playback hot paths as part of this release prep.

## Runtime And CI Truth

- Product version: `v1.8.1`
- Current pinned clean-runner runtime bootstrap asset: `v1.5.0`
- `Runtime\runtime-manifest.json` remains the playback-runtime integrity source of truth.
- `Runtime\export-runtime-manifest.json` remains the shipped export-runtime integrity source of truth.
- `Runtime\export-tools-manifest.json` remains the local/dev harness-tooling integrity source of truth and is not part of the shipped app output.

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
  - command: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Run-ReviewEngine-ManualTests.ps1 -Path "C:\Projects\Video Test Files" -Recurse -Configuration Release -Output ".\artifacts\review-engine-manual-tests-full-no-mov-20260418"`
  - files tested: `9`
  - result rows emitted: `72`
  - pass / warning / fail / advisory: `52 / 2 / 0 / 18`
- Full packaged regression corpus:
  - command: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Run-RegressionSuite.ps1 -CorpusPath "C:\Projects\Video Test Files" -Recurse -MaxCorpusFiles 0 -Configuration Release -Output ".\artifacts\regression-suite-no-mov-20260418"`
  - files tested: `10`
  - note: the run includes the repo-carried `dist\Frame Player\sample-test.mp4` plus the `9` supported files from the external local corpus
  - checks run: `750`
  - pass / warning / fail: `732 / 18 / 0`

Known non-blocking warnings remain limited to the intentional frames-first pending-index cases and expected packaged-coverage skips:

- `seek-to-time-before-index-ready`
- `ui-pre-index-click-seek`
- `ui-audio-insertion-mp3` packaged-coverage skips because the shipped app output does not carry local/dev CLI tooling
- short-clip loop coverage skips where the corpus sample does not have enough indexed frames to make the scenario meaningful

## Release Guidance

- Treat `v1.8.1` as a maintenance release that preserves the current user-facing behavior while making release validation more trustworthy.
- Treat the DLL-only export host/runtime cut as a packaging and hardening change, not a new user-facing feature line.
- Keep `Properties\AssemblyInfo.cs` as the canonical product-version source.
- Keep the app-driven regression suite as the source of truth for release-style playback/export validation.
- Release outputs for this cut should reflect product version `1.8.1` and match the validated verification output.
