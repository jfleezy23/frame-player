# Frame Player

Frame Player is a frame-first desktop video review tool for exact stepping, seeking, compare review, loop review, and export workflows.

## Downloads

| Release track | Platform | Current release | Download |
| --- | --- | --- | --- |
| Windows stable | Windows 10 or later | `v1.8.4` | [Frame Player v1.8.4](https://github.com/jfleezy23/frame-player/releases/tag/v1.8.4) |
| Unified Avalonia Preview | Windows x64 and Apple Silicon macOS | `0.2.0` | [Frame Player Unified Preview 0.2.0](https://github.com/jfleezy23/frame-player/releases/tag/unified-preview-0.2.0) |

The Windows WPF app remains the stable release line. Unified Avalonia Preview `0.2.0` is the current synchronized Windows/macOS preview. The earlier split macOS Preview `0.1.1` and Windows Avalonia Preview `0.1.0` releases are superseded and kept only for historical validation.

## Highlights

- Exact frame stepping and frame jumps based on decoded display-order frame identity.
- Single-pane and two-pane compare review for original vs processed media.
- Pane-local and shared transport controls with timeline scrubbing and frame entry.
- A/B loop playback with clip export and side-by-side compare export.
- Audio playback when supported, with video-only fallback for silent or unsupported audio.
- Recent files, diagnostics export, Video Info, About, and Help surfaces.
- Bundled pinned FFmpeg runtimes; no Homebrew or external FFmpeg install required for released apps.

## Screenshots

These screenshots are captured from the actual Windows stable app and the Avalonia macOS preview surface. They show the current empty-state layouts; loaded-video screenshots should replace or supplement them when clean corpus-backed captures are available.

| Windows stable | Avalonia macOS preview |
| --- | --- |
| ![Windows stable single-pane empty state](docs/assets/screenshots/windows-main.png) | ![Avalonia macOS preview single-pane empty state](docs/assets/screenshots/macos-main.png) |
| ![Windows stable two-pane compare empty state](docs/assets/screenshots/windows-compare.png) | ![Avalonia macOS preview two-pane compare empty state](docs/assets/screenshots/macos-compare.png) |

## Documentation

- [Wiki](https://github.com/jfleezy23/frame-player/wiki): user guide, screenshots, shortcuts, troubleshooting, build notes, validation, and security notes.
- [All releases](https://github.com/jfleezy23/frame-player/releases): Windows stable and current Unified Avalonia Preview downloads.
- [Unified Preview 0.2.0 release note](docs/unified-preview-0.2.0.md): synchronized Windows/macOS artifacts, validation evidence, and release gates.
- [macOS Preview 0.1.1 release note](docs/release-macos-preview-0.1.1.md): historical split-preview notarization and validation evidence.
- [Windows Avalonia Preview 0.1.0 release note](docs/release-avalonia-windows-preview-0.1.0.md): historical split-preview validation evidence.
- [Release checklist](docs/release-checklist.md): release validation steps.
- [Security policy](SECURITY.md): supported versions and reporting process.
- [Third-party notices](THIRD_PARTY_NOTICES.md): FFmpeg and runtime licensing notes.

## Build From Source

Windows stable:

```powershell
.\scripts\Build-FramePlayer.ps1
```

Unified Avalonia Preview:

```bash
dotnet build src/FramePlayer.Avalonia/FramePlayer.Avalonia.csproj -c Release
dotnet test tests/FramePlayer.Avalonia.Tests/FramePlayer.Avalonia.Tests.csproj -c Release
```

See the [Wiki build guide](https://github.com/jfleezy23/frame-player/wiki/Build-From-Source) for runtime staging, corpus validation, signing, and notarization details.

## Release Tracks

- Windows stable remains `v1.8.4` and keeps the existing WPF source path, build path, runtime bootstrap, tests, and release process.
- Unified Avalonia Preview `0.2.0` is the current synchronized Windows/macOS app path under `src/FramePlayer.Avalonia`.
- The old macOS Preview `0.1.1` and Windows Avalonia Preview `0.1.0` tracks are superseded by Unified Avalonia Preview `0.2.0`.
- Current preview downloads are linked from the unified [Frame Player Unified Preview 0.2.0 release page](https://github.com/jfleezy23/frame-player/releases/tag/unified-preview-0.2.0).

## License

Frame Player source is licensed under [MIT](LICENSE). Third-party runtime and FFmpeg notices are documented in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
