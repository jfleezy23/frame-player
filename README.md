# Frame Player

Frame Player is a frame-first desktop video review tool for exact stepping, seeking, compare review, loop review, and export workflows.

## Downloads

| Release track | Platform | Current release | Download |
| --- | --- | --- | --- |
| Stable (Unified) | Windows x64 and Apple Silicon macOS | `v2.0.0` | [Frame Player v2.0.0](https://github.com/jfleezy23/frame-player/releases/tag/v2.0.0) |

Frame Player `v2.0` is the new unified, cross-platform release built on Avalonia, fully superseding the legacy Windows WPF `v1.8.x` path.

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
- [All releases](https://github.com/jfleezy23/frame-player/releases): Current unified stable downloads and historical artifacts.
- [Unified Preview 0.3.2 release note](docs/unified-preview-0.3.2.md): synchronized Windows/macOS artifacts, validation evidence, and release gates.
- [macOS Preview 0.1.1 release note](docs/release-macos-preview-0.1.1.md): historical split-preview notarization and validation evidence.
- [Windows Avalonia Preview 0.1.0 release note](docs/release-avalonia-windows-preview-0.1.0.md): historical split-preview validation evidence.
- [Release checklist](docs/release-checklist.md): release validation steps.
- [Security policy](SECURITY.md): supported versions and reporting process.
- [Third-party notices](THIRD_PARTY_NOTICES.md): FFmpeg and runtime licensing notes.

## Build From Source

```bash
dotnet build src/FramePlayer.Avalonia/FramePlayer.Avalonia.csproj -c Release
dotnet test tests/FramePlayer.Avalonia.Tests/FramePlayer.Avalonia.Tests.csproj -c Release
```

See the [Wiki build guide](https://github.com/jfleezy23/frame-player/wiki/Build-From-Source) for runtime staging, corpus validation, signing, and notarization details.

## Release Tracks

- Frame Player `v2.0` is the single unified cross-platform path based on Avalonia.
- The legacy `v1.8.x` WPF application path has been officially deprecated and superseded.

## License

Frame Player source is licensed under [MIT](LICENSE). Third-party runtime and FFmpeg notices are documented in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
