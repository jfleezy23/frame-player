# Frame Player

Frame Player is a frame-first desktop video review tool for exact stepping, seeking, compare review, loop review, and export workflows.

## Downloads

| Release track | Platform | Current release | Download |
| --- | --- | --- | --- |
| Windows stable | Windows 10 or later | `v1.8.4` | [Frame Player v1.8.4](https://github.com/jfleezy23/frame-player/releases/tag/v1.8.4) |
| macOS Preview | Apple Silicon, macOS 13 or later | `0.1.0` | [Frame Player v1.8.4 unified downloads](https://github.com/jfleezy23/frame-player/releases/tag/v1.8.4) |

The Windows WPF app remains the stable release line. The Apple Silicon macOS Preview is an Avalonia app built to mirror the Windows review workflow while using native macOS window and menu chrome.

## Highlights

- Exact frame stepping and frame jumps based on decoded display-order frame identity.
- Single-pane and two-pane compare review for original vs processed media.
- Pane-local and shared transport controls with timeline scrubbing and frame entry.
- A/B loop playback with clip export and side-by-side compare export.
- Audio playback when supported, with video-only fallback for silent or unsupported audio.
- Recent files, diagnostics export, Video Info, About, and Help surfaces.
- Bundled pinned FFmpeg runtimes; no Homebrew or external FFmpeg install required for released apps.

## Screenshots

These screenshots are captured from the actual Windows stable app and macOS Preview app. They show the current empty-state layouts; loaded-video screenshots should replace or supplement them when clean corpus-backed captures are available.

| Windows stable | macOS Preview |
| --- | --- |
| ![Windows stable single-pane empty state](docs/assets/screenshots/windows-main.png) | ![macOS Preview single-pane empty state](docs/assets/screenshots/macos-main.png) |
| ![Windows stable two-pane compare empty state](docs/assets/screenshots/windows-compare.png) | ![macOS Preview two-pane compare empty state](docs/assets/screenshots/macos-compare.png) |

## Documentation

- [Wiki](https://github.com/jfleezy23/frame-player/wiki): user guide, screenshots, shortcuts, troubleshooting, build notes, validation, and security notes.
- [All releases](https://github.com/jfleezy23/frame-player/releases): Windows stable and macOS Preview downloads.
- [macOS Preview release note](docs/release-macos-preview-0.1.0.md): notarization, validation evidence, and current preview limitations.
- [Release checklist](docs/release-checklist.md): release validation steps.
- [Security policy](SECURITY.md): supported versions and reporting process.
- [Third-party notices](THIRD_PARTY_NOTICES.md): FFmpeg and runtime licensing notes.

## Build From Source

Windows stable:

```powershell
.\scripts\Build-FramePlayer.ps1
```

macOS Preview:

```bash
dotnet build src/FramePlayer.Mac/FramePlayer.Mac.csproj -c Release
dotnet test tests/FramePlayer.Mac.Tests/FramePlayer.Mac.Tests.csproj -c Release --filter "Category!=ReleaseCandidate"
```

See the [Wiki build guide](https://github.com/jfleezy23/frame-player/wiki/Build-From-Source) for runtime staging, corpus validation, signing, and notarization details.

## Release Tracks

- Windows stable remains `v1.8.4` and keeps the existing WPF source path, build path, runtime bootstrap, tests, and release process.
- macOS Preview `0.1.0` is Apple Silicon only and is not yet a declaration that Avalonia replaces the Windows WPF app.
- Current public downloads are linked from the unified [Frame Player v1.8.4 release page](https://github.com/jfleezy23/frame-player/releases/tag/v1.8.4).

## License

Frame Player source is licensed under [MIT](LICENSE). Third-party runtime and FFmpeg notices are documented in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
