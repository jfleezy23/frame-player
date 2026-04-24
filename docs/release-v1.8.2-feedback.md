# Frame Player v1.8.2 Release Note

This note documents the `v1.8.2` release bump after `v1.8.1`. It keeps the v1.8.1 runtime and packaging model while applying focused feedback around compare review, audio insertion validation, and release documentation.

## What Changed In v1.8.2

- Compare-mode all-pane seeks now prefer exact indexed frame identity when both panes can resolve the requested presentation time to a real decoded frame.
- When exact compare frame timing is not available, the app reports the fallback in the notification/status area and uses presentation-time synchronization instead of showing a popup.
- Compare zoom is linked by default so zoom and pan changes mirror between panes. The `Link Zoom` control can be disabled for independent pane review.
- The official app-driven regression harness now generates a repo-local MP3 fixture outside the packaged app and passes it into the packaged run, so MP3 audio insertion is covered without shipping `ffmpeg-tools`.
- Audio coverage now includes generated audio-bearing H.264 `.mp4` and MJPEG `.avi` review-engine samples.
- `test_pattern_boundary.mp4` is back in the supported local corpus as a targeted short-clip boundary case. The local file currently has 10 indexed frames, so loop-coverage warnings on that clip are expected when correctness checks stay green.

## Runtime And CI Truth

- Product version: `v1.8.2`
- Framework-dependent app prerequisite: `.NET 10 Desktop Runtime for Windows`, linked from the repository README and available from Microsoft at `https://dotnet.microsoft.com/en-us/download/dotnet/10.0`.
- `Runtime\runtime-manifest.json` remains the playback-runtime integrity source of truth.
- `Runtime\export-runtime-manifest.json` remains the shipped export-runtime integrity source of truth.
- `Runtime\export-tools-manifest.json` remains the local/dev harness-tooling integrity source of truth and is not part of the shipped app output.
- The shipped app output continues to exclude `ffmpeg.exe`, `ffprobe.exe`, and an `ffmpeg-tools` directory.

## Validation Evidence

Local validation captured for this release bump:

- Compile validation:
  - command: `dotnet build .\FramePlayer.csproj -c Release -p:Platform=x64`
  - result: `Build succeeded`
- Unit/integration tests:
  - command: `dotnet test .\tests\FramePlayer.Core.Tests\FramePlayer.Core.Tests.csproj -c Release`
  - result: `27` passed, `0` failed
- Targeted packaged regression:
  - command: `scripts\Run-RegressionSuite.ps1` against the H.264 audio sample and `test_pattern_boundary.mp4`
  - checks run: `143`
  - pass / warning / fail: `138 / 5 / 0`
  - note: `ui-audio-insertion-mp3` passed for both files

Known non-blocking warnings from the targeted run were limited to pre-index timing honesty and expected short-clip loop coverage skips:

- `seek-to-time-before-index-ready`
- `ui-loop-main-playback-multiwrap-skipped`
- `ui-loop-full-media-playback-skipped`
- `ui-loop-pane-local-coverage-skipped`

## Release Guidance

- Treat `v1.8.2` as a feedback/release-bump patch on top of `v1.8.1`.
- Keep `Properties\AssemblyInfo.cs` and `src\FramePlayer.Controls\Properties\AssemblyInfo.cs` as the canonical product-version sources.
- Keep the app-driven regression suite as the source of truth for release-style playback/export validation.
- Release outputs for this cut should reflect product version `1.8.2` and match the validated verification output.
