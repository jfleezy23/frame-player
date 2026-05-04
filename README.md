# Frame Player

Frame Player is a frame-first desktop video review tool for exact stepping, seeking, compare review, loop review, and export workflows.

## Downloads

| Release track | Platform | Current release | Download |
| --- | --- | --- | --- |
| Windows stable | Windows 10 or later | `v1.8.4` | [Frame Player v1.8.4](https://github.com/jfleezy23/frame-player/releases/tag/v1.8.4) |
| Unified Avalonia Preview | Windows x64 and Apple Silicon macOS | `0.2.0` in progress | Branch: `codex/unified-avalonia-preview-0.2.0` |
| macOS Preview | Apple Silicon, macOS 13 or later | `0.1.1` | [Frame Player macOS Preview 0.1.1](https://github.com/jfleezy23/frame-player/releases/tag/macos-preview-0.1.1) |
| Windows Avalonia Preview | Windows 10 or later, x64 | `0.1.0` | [Frame Player v1.8.4 unified downloads](https://github.com/jfleezy23/frame-player/releases/tag/v1.8.4) |

The Windows WPF app remains the stable release line. The Apple Silicon macOS Preview and Windows Avalonia Preview are controlled preview builds for the cross-platform Avalonia direction.

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
- [All releases](https://github.com/jfleezy23/frame-player/releases): Windows stable, macOS Preview, and Windows Avalonia Preview downloads.
- [macOS Preview release note](docs/release-macos-preview-0.1.1.md): notarization, validation evidence, and current preview limitations.
- [Windows Avalonia Preview release note](docs/release-avalonia-windows-preview-0.1.0.md): validation evidence and current preview limitations.
- [Unified Preview 0.2.0 prep note](docs/unified-preview-0.2.0.md): synchronized Windows/macOS artifact names, validation commands, and release gates.
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

Windows Avalonia Preview:

```powershell
dotnet build src\FramePlayer.Desktop\FramePlayer.Desktop.csproj -c Release
dotnet test tests\FramePlayer.Desktop.Tests\FramePlayer.Desktop.Tests.csproj -c Release
```

Unified Avalonia Preview:

```bash
dotnet build src/FramePlayer.Avalonia/FramePlayer.Avalonia.csproj -c Release
dotnet test tests/FramePlayer.Avalonia.Tests/FramePlayer.Avalonia.Tests.csproj -c Release
```

See the [Wiki build guide](https://github.com/jfleezy23/frame-player/wiki/Build-From-Source) for runtime staging, corpus validation, signing, and notarization details.

## Release Tracks

- Windows stable remains `v1.8.4` and keeps the existing WPF source path, build path, runtime bootstrap, tests, and release process.
- Unified Avalonia Preview `0.2.0` is the in-progress synchronized Windows/macOS app path under `src/FramePlayer.Avalonia`.
- macOS Preview `0.1.1` is Apple Silicon only and is not yet a declaration that Avalonia replaces the Windows WPF app.
- Windows Avalonia Preview `0.1.0` is a separate ZIP preview under `src/FramePlayer.Desktop`; it does not replace the Windows WPF app.
- Current public downloads are linked from the unified [Frame Player v1.8.4 release page](https://github.com/jfleezy23/frame-player/releases/tag/v1.8.4).

## License

Frame Player source is licensed under [MIT](LICENSE). Third-party runtime and FFmpeg notices are documented in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
