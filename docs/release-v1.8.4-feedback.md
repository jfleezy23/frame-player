# Frame Player v1.8.4 Release Note

This note documents the `v1.8.4` feedback release after `v1.8.3`. It keeps the v1.8.3 playback, export, runtime, and packaging surface intact while applying a focused two-pane compare UI cleanup.

## What Changed In v1.8.4

- Compare toolbar actions now use sync terminology:
  - `Sync Right to Left`
  - `Sync Left to Right`
- Compare status, tooltips, playback messages, and diagnostics now describe pane synchronization as sync instead of align/alignment.
- Each two-pane compare pane now has a single play/pause toggle instead of separate play and pause buttons.
- Each pane-local compare transport row now matches the main transport order:
  - previous frame
  - rewind 100 frames
  - play/pause
  - fast forward 100 frames
  - next frame
- Added focused regression coverage for compare UI wording, per-pane play/pause toggles, 100-frame controls, and transport ordering.

## Runtime And CI Truth

- Product version: `v1.8.4`
- Framework-dependent app prerequisite: `.NET 10 Desktop Runtime for Windows`, linked from the repository README and available from Microsoft at `https://dotnet.microsoft.com/en-us/download/dotnet/10.0`.
- `Runtime\runtime-manifest.json` remains the playback-runtime integrity source of truth.
- `Runtime\export-runtime-manifest.json` remains the shipped export-runtime integrity source of truth.
- `Runtime\export-tools-manifest.json` remains the local/dev harness-tooling integrity source of truth and is not part of the shipped app output.
- The shipped app output continues to exclude `ffmpeg.exe`, `ffprobe.exe`, and an `ffmpeg-tools` directory.

## Validation Evidence

Validated on 2026-05-01 with:

- Targeted compare UI tests:
  - command: `dotnet test .\tests\FramePlayer.Core.Tests\FramePlayer.Core.Tests.csproj -c Release --filter CompareUiFeedbackTests`
  - result: passed, 4 tests
- Unit/integration tests:
  - command: `dotnet test .\tests\FramePlayer.Core.Tests\FramePlayer.Core.Tests.csproj -c Release`
  - result: passed, 37 tests
- Repository harness syntax validation:
  - command: `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-RepoHarnessScripts.ps1`
  - result: passed
- Compile validation:
  - command: `dotnet build .\FramePlayer.csproj -c Release -p:Platform=x64`
  - result: passed, 0 warnings, 0 errors
- Release helper build:
  - command: `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-FramePlayer.ps1 -Configuration Release`
  - result: passed, 0 warnings, 0 errors
- Candidate review build:
  - command: `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-TestDrop.ps1 -Configuration Release -Platform x64 -OutputDirectory .\bin\Candidate-v1.8.4-feedback -IntermediateDirectory .\obj\Candidate-v1.8.4-feedback -ArtifactPath .\artifacts\candidates\FramePlayer-v1.8.4-feedback-candidate.zip`
  - output: `bin\Candidate-v1.8.4-feedback\FramePlayer.exe`
  - artifact: `artifacts\candidates\FramePlayer-v1.8.4-feedback-candidate.zip`
- Manual smoke:
  - launch the packaged app and confirm two-pane compare controls show sync wording plus pane-local 100-frame controls.
  - result: pending human review.

## Release Guidance

- Treat `v1.8.4` as a focused feedback patch on top of `v1.8.3`.
- Keep `Properties\AssemblyInfo.cs` and `src\FramePlayer.Controls\Properties\AssemblyInfo.cs` as the canonical product-version sources.
- Keep the app-driven regression suite as the source of truth for release-style playback/export validation.
- Release outputs for this cut should reflect product version `1.8.4` and match the validated verification output.
