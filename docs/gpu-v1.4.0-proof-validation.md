# GPU v1.4.0 Proof Validation Branch Note

This note exists only on `validation/gpu-v1.4.0-proof`. It documents the heavier proof tooling and the last-known green results that were intentionally kept out of `main` and out of the product/release merge branches.

## Branch-Only Tooling

The following harness/probe files are preserved here for future portability and release-validation work:

- `Diagnostics\DecodedFrameBudgetCoordinatorProbe.cs`
- `Diagnostics\DecodedFrameBudgetCoordinatorProbeCli.cs`
- `Diagnostics\DualPaneBudgetHarnessCli.cs`
- `Diagnostics\DualPaneBudgetHarnessModels.cs`
- `Diagnostics\DualPaneBudgetHarnessRunner.cs`
- `scripts\Run-DualPaneBudgetHarness.ps1`

These are not intended to ship on `main` as app startup surfaces. They stay here so future Linux, macOS, and ARM bring-up can reuse the same proof path without rebuilding the tooling from scratch.

## Corpus

- media root: `C:\Projects\Video Test Files`
- current maintained surface for this release line: `.avi`, `.mov`, `.m4v`, `.mp4`, `.mkv`, `.wmv`
- `.ts` intentionally remains out of the active release surface for `v1.4.0`

## Exact Commands

```powershell
.\scripts\Run-DualPaneBudgetHarness.ps1 -CorpusPath "C:\Projects\Video Test Files" -Output ".\artifacts\dual-pane-budget-harness\full-corpus-v1" -Configuration Release

.\scripts\Run-RegressionSuite.ps1 -Path "C:\Projects\Video Test Files" -Recurse -Output ".\artifacts\regression-suite\release-v1.4.0-auto" -Configuration Release

$env:FRAMEPLAYER_GPU_BACKEND="disabled"
.\scripts\Run-RegressionSuite.ps1 -Path "C:\Projects\Video Test Files" -Recurse -Output ".\artifacts\regression-suite\release-v1.4.0-cpu" -Configuration Release
Remove-Item Env:FRAMEPLAYER_GPU_BACKEND
```

## Last-Known Green Results

- full-corpus regression, auto mode:
  - files tested: `15`
  - checks run: `488`
  - pass / warning / fail: `453 / 35 / 0`
- full-corpus regression, forced CPU mode:
  - files tested: `15`
  - checks run: `488`
  - pass / warning / fail: `453 / 35 / 0`
- dual-pane real-media proof:
  - corpus files: `15`
  - pair runs: `51`
  - checks run: `5418`
  - pass / fail: `5418 / 0`
  - mixed proof pairs included real `ffmpeg-vulkan` + `ffmpeg-cpu` sessions

Observed budget classes from the real-media dual-pane proof:

- `Business16`: session `768 MiB`, pane `384 MiB`
- `Workstation32To64`: session `1536 MiB`, pane `768 MiB`
- `Workstation128Plus`: session `2048 MiB`, pane `1024 MiB`

Observed backend coverage from the corpus regressions:

- auto mode:
  - `SinglePaneGpu | ffmpeg-vulkan`: `9` files
  - `SinglePaneCpu | ffmpeg-cpu`: `6` files
- forced CPU mode:
  - `SinglePaneCpu | ffmpeg-cpu`: `15` files

Known non-blocking warnings from the last green run:

- pre-index seek operations can correctly land on time before absolute frame identity is available
- hidden-window UI playback is intentionally skipped for some audio-bearing corpus cases while engine-level playback and audio checks still run
