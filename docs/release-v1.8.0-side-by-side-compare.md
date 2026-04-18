# Frame Player v1.8.0 Release Note

This note documents the `v1.8.0` release. It is the maintainer-facing summary of what is new on top of `v1.7.1`, what remains intentionally deferred, and what validation evidence backs this release.

## What Is New In v1.8.0

- Two-pane compare review can now render directly to a merged side-by-side MP4:
  - `Playback > Export Side-by-Side Compare...` opens a compare-only export flow
  - the same compare export entrypoint is also available from both compare-pane context menus
- Audio replacement is now available for the single-pane MVP path:
  - `Audio Insertion > Replace Audio Track...` stays available only for loaded H.264 `.mp4` review sources
  - replacement audio accepts `.wav` or `.mp3`, replaces any existing source audio, keeps the source video duration fixed, and always writes a new `.mp4`
  - two-pane compare keeps the command visible but disabled with an explicit tooltip because audio insertion is intentionally single-pane only
- Compare export supports two review-aligned modes in one dialog:
  - `Loop` exports each pane's own pane-local A/B review range and pads the shorter pane with black video at the tail
  - `Whole Video` exports both full files and preserves the current compare alignment by adding black lead-in to the earlier pane
- Review zoom is now pane-local and export-aware:
  - mouse-wheel zoom and paused left-drag pan apply independently per pane
  - zoom survives play, pause, seek, and frame-step until `Playback > Reset Zoom` or the pane context-menu reset action is used
  - reviewed loop export and side-by-side compare export now render the pane crop that the reviewer actually chose instead of always exporting the uncropped full frame
- Audio ownership is now explicit:
  - the export dialog lets the reviewer choose `Primary` or `Compare` as the output audio source
  - if the chosen pane has no audio stream, the export still succeeds as a silent video-only MP4
  - the export flow does not silently fall back to the other pane's audio
- Export quality stays on the reviewed raster:
  - pane aspect ratios are preserved
  - height matching uses padding instead of downscaling
  - encoder-safe even sizing pads up instead of shrinking or cropping
- Release baselining moved forward:
  - the WPF app now ships on the `.NET 10` baseline used by the current branch

## What Stays Deferred

- Side-by-side compare export remains fixed to MP4/H.264 output on Windows for this release line.
- Audio insertion is intentionally limited to single-pane H.264 `.mp4` sources and always keeps the original full-frame picture; zoom does not affect audio insertion output.
- No batch export, alternate layout chooser, container chooser, or codec chooser ships in `v1.8.0`.
- If a full-resolution side-by-side canvas cannot be encoded safely within the fixed MP4/H.264 contract, the export fails clearly instead of reducing resolution.

## Runtime And CI Truth

- Product version: `v1.8.0`
- Current pinned clean-runner runtime bootstrap asset: `v1.5.0`
- `Runtime\runtime-manifest.json` remains pinned to the verified runtime bundle because the playback DLL payload itself did not change for this release.

## Validation Evidence

Green validation captured for this release:

- Local compile validation:
  - tool: .NET SDK `10.0.202`
  - command: `dotnet build FramePlayer.csproj -c Release -p:Platform=x64`
  - result: `Build succeeded`
- Packaged release validation:
  - command: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Build-TestDrop.ps1 -Configuration Release -Platform x64`
  - result: packaged `bin\TestDrop` output plus `artifacts\FramePlayer-CustomFFmpeg-1.8.0.zip`
- Full supported regression corpus:
  - command: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Run-RegressionSuite.ps1 -Path "C:\Projects\Video Test Files" -Recurse -Output ".\artifacts\regression-suite\release-v1.8-full-r4" -Configuration Release`
  - files tested: `17`
  - checks run: `998`
  - pass / warning / fail: `970 / 28 / 0`
- Additional manual regression focus for the MVP-finish slice:
  - audio insertion with `.wav` and `.mp3` replacements on H.264 `.mp4`
  - disabled-state and tooltip coverage for two-pane compare plus unsupported sources
  - paused zoom/pan persistence in single-pane and two-pane review
  - crop-aware loop export and crop-aware side-by-side compare export validation

Known non-blocking warnings remain the intentional frames-first pre-index and tiny-clip skips:

- `seek-to-time-before-index-ready`
- `ui-pre-index-click-seek`
- tiny-clip timeline or loop coverage skips where the corpus sample cannot support the scenario meaningfully

## Release Guidance

- Treat `v1.8.0` as the feature release that turns compare-mode review into a real side-by-side export workflow without allowing silent quality reduction.
- Keep `Properties\AssemblyInfo.cs` as the canonical product-version source.
- Keep the app-driven regression suite as the source of truth for compare export validation; the release was gated on the clean full-corpus result above.
- Release assets for this cut are expected to include:
  - `FramePlayer-CustomFFmpeg-1.8.0.zip`
  - the usual `bin\TestDrop` package output
