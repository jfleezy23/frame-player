# Universal Desktop Migration Plan

## Summary

- Do **not** treat this as a direct WPF-to-Avalonia UI swap. Treat it as a **dual-host migration**: keep WPF as the reference host while building a new Avalonia host over a strengthened shared core.
- The first practical cross-platform milestone should be **single-pane review parity** on **Windows + macOS**:
  - open/close
  - play/pause
  - seek to time/frame
  - frame step
  - A/B loop
  - looped playback
  - clip export
- Defer **two-pane compare**, **Linux support**, and **GPU-specific polish** until after single-pane parity is proven.
- Recommended foundation choice: move the solution to **SDK-style .NET 8 LTS**, then share `net8.0` libraries between WPF and Avalonia. Do **not** build the future around keeping `.NET Framework 4.8` alive.

## Architecture And Interfaces

- Split the repo into explicit layers:
  - `FramePlayer.Core` (`net8.0`): neutral models, session/workspace coordination, command/state logic, loop/export rules
  - `FramePlayer.Media.FFmpeg` (`net8.0`): FFmpeg engine, indexing, stepping, playback lifecycle, clip export execution
  - `FramePlayer.Host.Wpf` (`net8.0-windows`): current reference shell after migration from the existing WPF project
  - `FramePlayer.Host.Avalonia` (`net8.0`): new universal desktop shell
- Keep `IVideoReviewEngine`, `ReviewSessionCoordinator`, `ReviewWorkspaceCoordinator`, and `DecodedFrameBuffer` as the starting seams; do **not** widen them with UI types.
- Add a shell-neutral application layer above the coordinators:
  - `ReviewWorkspaceHostController`
  - `ReviewWorkspaceViewState`
  - `PaneViewState`
  - `TransportCommandState`
  - `LoopCommandState`
  - `ExportCommandState`
- Move command enablement and UI-facing behavior out of `MainWindow.xaml.cs` into that host controller:
  - active pane selection
  - transport routing
  - loop marker eligibility
  - export eligibility
  - status-bar text
  - startup open-file handling
  - recent-files behavior
- Keep frame presentation host-specific:
  - `DecodedFrameBuffer` remains the only engine-to-host frame handoff type
  - WPF keeps a `BitmapSource` adapter
  - Avalonia gets a `WriteableBitmap` adapter
  - no cross-platform library may expose WPF or Avalonia image types
- Replace Windows-only audio coupling with an explicit abstraction:
  - `IAudioOutput`
  - `IAudioOutputFactory`
  - keep `WinMmAudioOutput` behind the abstraction for the WPF reference host initially
  - add an `SdlAudioOutput` implementation for the Avalonia/cross-platform host path
- Split Windows-only native helpers out of the shared FFmpeg layer:
  - `FfmpegNativeHelpers` becomes platform-neutral where possible
  - Windows-only OS interop moves behind platform-specific helpers
- Packaging/runtime becomes RID-based instead of Windows-only:
  - per-RID native runtime manifests
  - per-RID FFmpeg asset bundles
  - Avalonia publish targets for Windows and macOS first
- Avalonia milestone 1 UI scope is fixed:
  - single main window
  - single video surface
  - single timeline
  - transport controls
  - loop controls
  - clip export flow
  - recent files
  - minimal preferences/status display
- Explicitly out of Avalonia milestone 1:
  - two-pane compare
  - compare timelines
  - GPU-specific settings/UI
  - porting the current WPF diagnostics harness 1:1

## Recommended PRs

1. `core/net8-foundation`
   - convert the solution to SDK-style .NET 8
   - create `Core`, `Media.FFmpeg`, and host project boundaries
   - keep behavior unchanged in WPF
2. `platform-audio-runtime-abstraction`
   - add `IAudioOutput`/`IAudioOutputFactory`
   - isolate WinMM
   - add SDL-based cross-platform audio output
   - introduce RID-based runtime/packaging manifests
3. `host-controller-extraction`
   - move command/state logic from `MainWindow.xaml.cs` into the shell-neutral host controller
   - keep WPF as a thinner host over the new controller
4. `avalonia-shell-bringup`
   - create the Avalonia app
   - wire single-pane playback/review over the shared controller/core
   - ship Windows + macOS engineering builds
5. `avalonia-loop-export-parity`
   - finish A/B loop, loop playback, and clip export parity in Avalonia
   - add macOS CI/package validation
   - declare the first universal desktop release candidate
6. `avalonia-compare-parity`
   - add two-pane compare to Avalonia
   - drive toward Windows parity with the new UI before widening platform scope again
   - keep Linux deferred until the Windows-first Avalonia parity pass is accepted

## Test Plan

- Preserve the current WPF regression harness as the reference oracle during migration.
- Add platform-neutral tests around the new host controller:
  - transport command enablement
  - loop marker ordering
  - pending-marker behavior
  - export eligibility
  - pane selection and routing
- Keep engine parity tests running unchanged while refactoring project boundaries.
- Add cross-host parity checks on the supported corpus for milestone 1 formats:
  - `.avi`
  - `.m4v`
  - `.mkv`
  - `.mov`
  - `.mp4`
  - `.wmv`
- Avalonia milestone 1 acceptance must prove, on Windows and macOS:
  - open/play/pause works
  - exact frame step works
  - seek-to-time and seek-to-frame stay correct
  - A/B markers obey frames-first ordering
  - loop playback survives repeated wraps
  - clip export creates an MP4 with duration matching the selected loop
- Keep `.ts` excluded unless the supported surface changes.
- Add packaging smoke checks for each shipped RID:
  - app launches
  - runtime manifest resolves
  - FFmpeg native assets load
  - audio output initializes
- Linux stays off the required release path until the Windows-first Avalonia parity pass is accepted and we explicitly choose to widen scope again.

## Assumptions And Defaults

- Avalonia is the chosen universal desktop UI path.
- Dual-host migration is mandatory; WPF remains the reference host until Avalonia milestone 1 is accepted.
- The solution moves to **.NET 8 LTS** before serious Avalonia work starts.
- First Avalonia release is **single-pane only**; compare mode is phase 2.
- First universal release targets **Windows x64** and **macOS**; Linux remains explicitly deferred until after Windows-first Avalonia parity is accepted.
- Cross-platform rendering starts with a **CPU-backed bitmap upload path**; GPU acceleration is a later optimization pass.
- SDL is the default cross-platform audio backend choice for the new host path; WinMM remains only as a temporary Windows implementation behind the new audio abstraction.
- The current “middle/translation layer” is treated as real seed infrastructure, but it is **not yet enough** on its own; the missing piece is a shell-neutral host controller that removes most of `MainWindow.xaml.cs` from the decision path.
