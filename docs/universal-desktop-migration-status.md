# Universal Desktop Migration Status

This checkpoint turns the existing WPF application into the reference host for a new dual-host solution instead of the only host in the repo.

## What Exists Now

- `FramePlayer.Universal.sln`
  - `src/FramePlayer.Core/FramePlayer.Core.csproj`
  - `src/FramePlayer.Media.FFmpeg/FramePlayer.Media.FFmpeg.csproj`
  - `src/FramePlayer.Host.Wpf/FramePlayer.Host.Wpf.csproj`
  - `src/FramePlayer.Host.Avalonia/FramePlayer.Host.Avalonia.csproj`
- `global.json` pinned to `.NET SDK 8.0.420`
- Existing root `FramePlayer.csproj` remains intact as the current shipping reference build.

## Shared Seams Added

### Audio abstraction

- `Core/Abstractions/IAudioOutput.cs`
- `Core/Abstractions/IAudioOutputFactory.cs`
- `Engines/FFmpeg/WinMmAudioOutputFactory.cs`
- `src/FramePlayer.Media.FFmpeg/SdlAudioOutput.cs`
- `src/FramePlayer.Media.FFmpeg/SdlAudioOutputFactory.cs`

`FfmpegAudioPlaybackSession` and `FfmpegReviewEngine` no longer hard-wire WinMM internally. The reference WPF path still defaults to WinMM, while the Avalonia preview host uses SDL-backed audio output.

### Host-controller extraction

- `Core/Hosting/ReviewWorkspaceHostController.cs`
- `Core/Hosting/ReviewWorkspaceViewState.cs`
- `Core/Hosting/PaneViewState.cs`
- `Core/Hosting/TransportCommandState.cs`
- `Core/Hosting/LoopCommandState.cs`
- `Core/Hosting/ExportCommandState.cs`
- `Core/Hosting/ReviewHostCapabilities.cs`

The current WPF host now consumes the host controller for transport state, playback messaging, startup-open dispatch, and recent-files menu state instead of recomputing all of that directly in `MainWindow.xaml.cs`.

### Core/Media decoupling cleanup

- `Core/Abstractions/IIndexedFrameTimeResolver.cs`

`ClipExportRequest` no longer depends directly on `FfmpegReviewEngine`. That removes one of the biggest blockers to a real `Core -> Media` dependency flow.

### RID-aware runtime packaging

- `Runtime/manifests/win-x64/runtime-manifest.json`
- `Runtime/manifests/win-x64/export-tools-manifest.json`
- `Services/RuntimeManifestService.cs`
- `Services/ExportToolsManifestService.cs`

The app no longer assumes a single embedded Windows runtime manifest. Runtime and export-tool validation now resolve by the current RID, and the Avalonia host declares `win-x64`, `osx-x64`, and `osx-arm64` publish identifiers even though only `win-x64` is bundled today.

## Avalonia Preview Host

`src/FramePlayer.Host.Avalonia` is a real desktop host scaffold, not just an empty shell.

Current preview surface:

- open supported media
- close current media
- play/pause
- frame step
- timeline seek
- set loop A/B
- loop playback restart with full-media fallback and pause/seek/play restarts
- save current loop as clip with pause-before-export, suggested clip names, and actionable status messages
- startup open-file handling
- recent files panel backed by a cross-platform file catalog
- display decoded frames via `AvaloniaFrameBufferPresenter`

This is still milestone-1 scaffolding, not parity with the existing WPF shell.

## Current Build Status

Validated locally on April 15, 2026:

- Root reference app:
  - `MSBuild FramePlayer.csproj /p:Configuration=Debug /p:Platform=x64`
  - result: success
- Shared core:
  - `dotnet build src/FramePlayer.Core/FramePlayer.Core.csproj -c Debug`
  - result: success
- Shared FFmpeg/media:
  - `dotnet build src/FramePlayer.Media.FFmpeg/FramePlayer.Media.FFmpeg.csproj -c Debug`
  - result: success
- New WPF host:
  - `dotnet build src/FramePlayer.Host.Wpf/FramePlayer.Host.Wpf.csproj -c Debug`
  - result: success
  - note: current warnings are Windows-specific API analyzer warnings for DPAPI and STA thread usage, which are valid for this Windows-only host
- Avalonia preview host:
  - `dotnet build src/FramePlayer.Host.Avalonia/FramePlayer.Host.Avalonia.csproj -c Debug`
  - result: success
- Avalonia validation workflow:
  - `.github/workflows/avalonia-host-validation.yml`
  - result: validates Avalonia engineering builds on Windows and macOS runners, including published output artifacts

## What Still Needs Another Pass

- move more command routing and loop/export rules out of `MainWindow.xaml.cs`
- replace DPAPI-backed WPF recent-files and diagnostics persistence with fully host-neutral shared store abstractions
- harden the Avalonia preview host with real parity coverage instead of manual preview-host wiring only
- add RID-aware publish/test packaging for the new hosts
- stage actual macOS FFmpeg runtime/tool bundles under the new RID manifest layout
- move compare-mode behavior into the shared host-controller/application layer after single-pane parity is proven
