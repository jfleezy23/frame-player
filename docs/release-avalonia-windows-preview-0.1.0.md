# Frame Player Avalonia Windows Preview 0.1.0 Release Note

This note documents the controlled Windows Avalonia Preview release. Windows stable remains the WPF `v1.8.4` release line and is not changed by this preview.

- Release: [Frame Player Avalonia Windows Preview 0.1.0](https://github.com/jfleezy23/frame-player/releases/tag/avalonia-windows-preview-0.1.0)
- Unified download page: [Frame Player v1.8.4](https://github.com/jfleezy23/frame-player/releases/tag/v1.8.4)
- Artifact: `FramePlayer-Desktop-Windows-x64-avalonia-windows-preview-0.1.0.zip`
- SHA256: `7e3f19e2f16dd752e6424d6679405f63a490b78a822c729e3280e1f50c87469b`

## What Changed

- Added `src/FramePlayer.Desktop` as a Windows Avalonia preview shell with native Avalonia menus, app icon staging, Windows-style layout rails, transport controls, status blocks, disabled command states, recent-file isolation, diagnostics, export wiring, and compare review surfaces.
- Packaged the Desktop preview as a self-contained Windows x64 ZIP so testers do not need a separate .NET runtime install.
- Staged FFmpeg playback DLLs beside `FramePlayer.Desktop.exe` and FFmpeg export runtime DLLs under `ffmpeg-export`.
- Kept the existing Windows WPF app as the stable `v1.8.4` release path.

## Release Track

- Windows stable remains `v1.8.4`.
- Windows Avalonia preview tag: `avalonia-windows-preview-0.1.0`, targeting commit `d4bcaaaa22742d5cf56d4d10b2e722176f4d7faa`.
- This is not a declaration that Avalonia replaces the Windows WPF app.
- The preview intentionally keeps Windows Avalonia work under `src/FramePlayer.Desktop` and `tests/FramePlayer.Desktop.Tests`.

## Validation Evidence

Validated locally and in GitHub on 2026-05-04:

- Build:
  - command: `dotnet build src\FramePlayer.Desktop\FramePlayer.Desktop.csproj -c Release`
  - result: passed, 0 warnings, 0 errors
- Desktop preview tests:
  - command: `dotnet test tests\FramePlayer.Desktop.Tests\FramePlayer.Desktop.Tests.csproj -c Release`
  - result: passed, 30 tests
- Core tests:
  - command: `dotnet test tests\FramePlayer.Core.Tests\FramePlayer.Core.Tests.csproj -c Release`
  - result: passed, 37 tests
- Stable WPF build guard:
  - command: `dotnet build FramePlayer.csproj -c Release -p:Platform=x64`
  - result: passed, 0 warnings, 0 errors
- Packaged preview:
  - command: `dotnet publish src\FramePlayer.Desktop\FramePlayer.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false`
  - result: passed
  - ZIP contents verified for `FramePlayer.Desktop.exe`, app icon, playback DLLs, export runtime DLLs, runtime manifests, license, and third-party notices
  - packaged launch smoke: passed
- GitHub checks on PR #60:
  - Windows CI: passed
  - macOS Avalonia CI: passed
  - Dependency Review: passed
  - CodeQL: passed
  - SonarCloud: passed, 0 new issues and 0 security hotspots
  - `@codex review`: no major issues on the final requested review pass
- GitHub checks on the preview tag:
  - Windows CI: passed
  - Desktop Packaging Helper: passed, produced workflow artifacts only

## Current Limitations

- Audible Windows audio output is not implemented in the shared `src/FramePlayer.Engine.FFmpeg` path yet. Media with audio streams has Play/Play-Pause gated in this preview; frame stepping, seeking, compare review, and timeline inspection remain available.
- This is a ZIP preview, not the stable Windows WPF ZIP/release path.
- Manual tester validation should continue before treating this as a replacement app.

## Release Guidance

- Announce this as a controlled Windows Avalonia Preview.
- Keep the GitHub release marked as a prerelease.
- Attach the Windows x64 preview ZIP and SHA256 file to both the preview release and the unified current-download release page.
- Do not rename, remove, or replace the stable Windows WPF `v1.8.4` artifact.
- Treat the next release step as tester feedback and a separate macOS UI parity PR for the new Windows Avalonia shell polish.
