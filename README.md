# Frame Player

Frame Player is a frame-first cross-platform video review tool for exact stepping, seeking, compare review, loop review, and export workflows.

## Downloads

| Release track | Platform | Current release | Download |
| --- | --- | --- | --- |
| Stable (Unified) | Windows x64 and Apple Silicon macOS | `v2.0.0` | [Frame Player v2.0.0](https://github.com/jfleezy23/frame-player/releases/tag/v2.0.0) |

Frame Player `v2.0` is the universal Avalonia application for Windows x64 and Apple Silicon macOS.

## Highlights

- Exact frame stepping and frame jumps based on decoded display-order frame identity.
- Single-pane and two-pane compare review for original vs processed media.
- Pane-local and shared transport controls with timeline scrubbing and frame entry.
- A/B loop playback with clip export and side-by-side compare export.
- Audio playback when supported, with video-only fallback for silent or unsupported audio.
- Recent files, diagnostics export, Video Info, About, and Help surfaces.
- Bundled pinned FFmpeg runtimes; no Homebrew or external FFmpeg install required for released apps.

## Documentation

- [Wiki](https://github.com/jfleezy23/frame-player/wiki): user guide, shortcuts, troubleshooting, build notes, validation, and security notes.
- [All releases](https://github.com/jfleezy23/frame-player/releases): Current unified stable downloads and historical artifacts.
- [Release checklist](docs/release-checklist.md): release validation steps.
- [Security policy](SECURITY.md): supported versions and reporting process.
- [Third-party notices](THIRD_PARTY_NOTICES.md): FFmpeg and runtime licensing notes.

## Build From Source

```bash
dotnet build src/FramePlayer.Avalonia/FramePlayer.Avalonia.csproj -c Release
dotnet test tests/FramePlayer.Core.Tests/FramePlayer.Core.Tests.csproj -c Release
dotnet test tests/FramePlayer.Avalonia.Tests/FramePlayer.Avalonia.Tests.csproj -c Release --filter "Category!=ReleaseCandidate"
```

See the [Wiki build guide](https://github.com/jfleezy23/frame-player/wiki/Build-From-Source) for runtime staging, corpus validation, signing, and notarization details.

## Release Tracks

- Frame Player `v2.0` has one Avalonia codebase and one release line for both supported platforms.

## License

Frame Player source is licensed under [MIT](LICENSE). Third-party runtime and FFmpeg notices are documented in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
