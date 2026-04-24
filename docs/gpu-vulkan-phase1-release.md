# GPU Vulkan Phase 1 Release Note (Historical v1.4.4)

This note documents the historical `v1.4.4` GPU Vulkan phase 1 release. The current release is documented in `docs\release-v1.8.2-feedback.md`.

## What Shipped

- Opportunistic Vulkan-backed FFmpeg decode for supported sessions, with strict CPU fallback when the runtime, codec, driver, or device path does not prove out.
- A visible `Playback > Use GPU Acceleration` toggle that persists under `%LocalAppData%\FramePlayer\preferences.txt` and applies to newly opened media.
- UI review/navigation polish on the WPF host:
  - live timeline scrubbing that lands paused on release
  - whole-media `Playback > Loop Playback`
  - a labeled pixel coordinate readout in the status bar
  - Inspector V2: a structured FFmpeg-backed `Video Info` window with pane context-menu access
  - modeless inspector windows so compare sessions can inspect both panes at once
  - compare-mode shared transport plus pane-local timeline and frame navigation
  - visible pending frame-number state until background indexing resolves absolute frame identity
  - patch follow-ups for fullscreen review sessions and compare pane navigation:
    - fullscreen now hides the entire status-bar container instead of leaving bottom chrome behind
    - pane-local frame input now uses the same ceiling-style fallback clamping as the main transport path
- A neutral frame-contract seam:
  - `DecodedFrameBuffer` is the engine-to-shell payload
  - `FramePresentedEventArgs` carries frame data plus exact `FrameDescriptor`
  - WPF `BitmapSource` creation stays in `Services\WpfFrameBufferPresenter`
- Pane-aware decoded-frame budgeting with three active bands:
  - `SinglePaneCpu`
  - `SinglePaneGpu`
  - `DualPaneBackendAware`
- Compare-mode participation from the same GPU/CPU backend selection and budget coordinator used by single-pane sessions.
- Updated self-built FFmpeg 8.1 runtime metadata and source-build notes for the Vulkan-capable runtime.
- Supported release-surface extensions for this line:
  - `.avi`
  - `.mov`
  - `.m4v`
  - `.mp4`
  - `.mkv`
  - `.wmv`

## What Did Not Ship

- No bundled Vulkan loader is shipped in the runtime archive. Actual GPU acceleration still depends on a working system Vulkan loader/driver.
- No zero-copy presentation path exists yet. The current GPU path still pays hardware decode, `av_hwframe_transfer_data()`, CPU-side BGRA conversion, and WPF presentation cost.
- No cross-platform UI port ships in this phase.
- `.ts` is intentionally not part of the active supported release surface for this line.

## Validation Evidence

The raw proof harnesses are preserved on the companion validation branch `validation/gpu-v1.4.4-proof`. The release branch keeps the summarized evidence here and in the normal regression tooling.

Last-known green validation runs:

- Full-corpus regression, auto mode:
  - files tested: `15`
  - checks run: `488`
  - pass / warning / fail: `453 / 35 / 0`
- Full-corpus regression, forced CPU mode:
  - files tested: `15`
  - checks run: `488`
  - pass / warning / fail: `453 / 35 / 0`
- Clean-runner bootstrap proof:
- the pinned runtime archive is published on the `v1.4.4` GitHub release as the versioned runtime bootstrap artifact
  - `Runtime\runtime-manifest.json` now points at that verified release asset with matching archive and DLL hashes
  - Windows CI restores the pinned runtime through `scripts\Ensure-DevRuntime.ps1` before build
- Dual-pane real-media proof, mixed backend capable:
  - corpus files: `15`
  - pair runs: `51`
  - checks run: `5418`
  - pass / fail: `5418 / 0`
  - mixed proof pairs included real `ffmpeg-vulkan` + `ffmpeg-cpu` sessions

Exact commands used for the current release validation set:

```powershell
.\scripts\Run-RegressionSuite.ps1 -Path "C:\Projects\Video Test Files" -Recurse -Output ".\artifacts\regression-suite\release-v1.4.4-auto" -Configuration Release

$env:FRAMEPLAYER_GPU_BACKEND="disabled"
.\scripts\Run-RegressionSuite.ps1 -Path "C:\Projects\Video Test Files" -Recurse -Output ".\artifacts\regression-suite\release-v1.4.4-cpu" -Configuration Release
Remove-Item Env:FRAMEPLAYER_GPU_BACKEND
```

The preserved dual-pane proof command remains available on the companion validation branch:

```powershell
.\scripts\Run-DualPaneBudgetHarness.ps1 -CorpusPath "C:\Projects\Video Test Files" -Output ".\artifacts\dual-pane-budget-harness\full-corpus-v1" -Configuration Release
```

Known non-blocking warnings from the regression corpus:

- pre-index seek operations can correctly land on time before absolute frame identity is available, and the UI now withholds a numeric frame claim until that identity resolves
- hidden-window UI playback is intentionally skipped for some audio-bearing corpus cases while engine-level playback and audio checks still run

## Release Guidance

- Treat `v1.4.4` as the current patch release that carries the verified clean-runner runtime bootstrap path forward while adding fullscreen/status-bar cleanup and compare-pane frame clamping fixes on top of the Inspector V2 surface.
- Keep `Properties\AssemblyInfo.cs` as the canonical version source and derive packaging/output names from the built executable version.
- Keep `docs\ffmpeg-8.1-build-notes.md` factual about runtime provenance and the now-verified clean-runner restore path.
- Preserve the proof harnesses and raw proof artifacts outside `main`; do not require the app startup path in `main` to carry harness-only CLI entry points.

## Next R&D Areas

- lower-overhead GPU startup/device reuse work
- zero-copy or GPU-native presentation research
- non-Windows and ARM validation using the preserved proof/tooling branch
