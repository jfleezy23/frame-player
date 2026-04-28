# Frame Player v1.8.3 Release Note

This note documents the `v1.8.3` feedback release after `v1.8.2`. It keeps the current two-pane compare layout intact while applying focused fixes for new-window workflow, compare timeline correctness, and small-clip decoded-cache behavior.

## What Changed In v1.8.3

- Added File > New Window and `Ctrl+N` to launch a second blank Frame Player instance without cloning the current media or compare session.
- Compare-mode shared timeline seeks now wait briefly for exact frame indexes when either pane is still indexing, then seek both panes by the resolved absolute frame number.
- Live shared timeline dragging in compare mode avoids timestamp fallback while exact frame indexes are still building; releasing the slider commits the latest target.
- Truly small clips can promote to a complete decoded-frame cache when their decoded frame set fits safely inside the pane budget.
- The cache status text now reports when a complete decoded cache is loaded, so tiny clips no longer look partially cached just because the back/ahead counts are relative to the current frame.
- Larger clips continue to use the global frame index plus bounded local decoded-frame windows instead of attempting to retain the whole decoded video.

## Runtime And CI Truth

- Product version: `v1.8.3`
- Framework-dependent app prerequisite: `.NET 10 Desktop Runtime for Windows`, linked from the repository README and available from Microsoft at `https://dotnet.microsoft.com/en-us/download/dotnet/10.0`.
- `Runtime\runtime-manifest.json` remains the playback-runtime integrity source of truth.
- `Runtime\export-runtime-manifest.json` remains the shipped export-runtime integrity source of truth.
- `Runtime\export-tools-manifest.json` remains the local/dev harness-tooling integrity source of truth and is not part of the shipped app output.
- The shipped app output continues to exclude `ffmpeg.exe`, `ffprobe.exe`, and an `ffmpeg-tools` directory.

## Validation Evidence

Validated on 2026-04-28 with:

- Compile validation:
  - command: `dotnet build .\FramePlayer.csproj -c Release -p:Platform=x64`
  - result: passed, 0 warnings, 0 errors
- Unit/integration tests:
  - command: `dotnet test .\tests\FramePlayer.Core.Tests\FramePlayer.Core.Tests.csproj -c Release`
  - result: passed, 33 tests
- Targeted packaged regression:
  - command: `powershell -NoProfile -ExecutionPolicy Bypass -Command "& { & '.\scripts\Run-RegressionSuite.ps1' -Path @('C:\Projects\Video Test Files\test_pattern_boundary.mp4','C:\Users\jflow\Downloads\RECORD_1.MP4') -Configuration Release -Output '.\artifacts\regression-suite-v1.8.3-feedback' }"`
  - result: passed, 145 checks, 140 pass, 5 coverage/correctness warnings, 0 failures
  - output: `artifacts\regression-suite-v1.8.3-feedback\regression-suite-summary.md`
  - packaged artifact: `artifacts\regression-builds\FramePlayer-RegressionBuild-76350748affd44b1be08b92670edc45b.zip`
- Candidate review build:
  - command: `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-TestDrop.ps1 -Configuration Release -Platform x64 -OutputDirectory .\bin\Candidate-v1.8.3-feedback -IntermediateDirectory .\obj\Candidate-v1.8.3-feedback -ArtifactPath .\artifacts\candidates\FramePlayer-v1.8.3-feedback-candidate.zip`
  - result: passed, 0 warnings, 0 errors
  - output: `bin\Candidate-v1.8.3-feedback\FramePlayer.exe`
  - artifact: `artifacts\candidates\FramePlayer-v1.8.3-feedback-candidate.zip`
- Manual smoke:
  - launch the packaged app, press `Ctrl+N`, and confirm a second blank Frame Player window opens.
  - result: not run in this validation pass; covered by launcher unit tests and menu/shortcut wiring review

## Release Guidance

- Treat `v1.8.3` as a focused feedback patch on top of `v1.8.2`.
- Keep `Properties\AssemblyInfo.cs` and `src\FramePlayer.Controls\Properties\AssemblyInfo.cs` as the canonical product-version sources.
- Keep the app-driven regression suite as the source of truth for release-style playback/export validation.
- Release outputs for this cut should reflect product version `1.8.3` and match the validated verification output.
