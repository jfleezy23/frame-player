# Build From Source

The unified cross-platform application builds from `src\FramePlayer.Avalonia`. It also requires Rust/Cargo for release packaging; the Rust toolchain is pinned by `rust-toolchain.toml`. 

On Windows, restore the pinned playback and export runtimes before building or packaging:

```powershell
.\scripts\Ensure-DevRuntime.ps1
.\scripts\Ensure-DevExportRuntime.ps1 -Required
.\scripts\Build-RustFfmpegProbe.ps1
dotnet build .\src\FramePlayer.Avalonia\FramePlayer.Avalonia.csproj -c Release
dotnet test .\tests\FramePlayer.Avalonia.Tests\FramePlayer.Avalonia.Tests.csproj -c Release
.\scripts\Package-UnifiedWindowsPreview.ps1
```

On macOS, stage the pinned macOS FFmpeg runtime under `Runtime/macos/osx-arm64/ffmpeg`, make sure `cargo` is available, then run:

```bash
scripts/Build-RustFfmpegProbe.sh osx-arm64
dotnet build src/FramePlayer.Avalonia/FramePlayer.Avalonia.csproj -c Release
dotnet test tests/FramePlayer.Avalonia.Tests/FramePlayer.Avalonia.Tests.csproj -c Release
FRAMEPLAYER_MAC_CORPUS="Video Test Files" script/validate_macos_release_candidate.sh --corpus "Video Test Files"
```

Package a local signed release candidate:

```bash
PACKAGE_VERSION=2.0.0 script/package_unified_macos_release.sh --sign
codesign --verify --deep --verbose=2 "dist/Frame Player.app"
```

The package scripts build the first-party Rust FFmpeg native library and include it beside the Avalonia executable. Normal dev builds can run without the native library, but release packaging requires Rust/Cargo. The exact frame index builder, indexed decode-window helper, and BGRA frame converter can be forced with `FRAMEPLAYER_FFMPEG_INDEX_BUILDER=rust`, `FRAMEPLAYER_FFMPEG_DECODE_CORE=rust`, and `FRAMEPLAYER_FFMPEG_FRAME_CONVERTER=rust`; each can be bypassed with `managed` or left in fallback mode with `auto`.

Developer ID notarization is documented in [docs/macos-preview-release.md](https://github.com/jfleezy23/frame-player/blob/main/docs/macos-preview-release.md).

The legacy `src\FramePlayer.Mac`, `src\FramePlayer.Desktop`, and root WPF projects are officially superseded by the unified project.

## Runtime Notes

FFmpeg playback and export runtimes are pinned. Runtime binaries are staged locally and are not committed unless a later release explicitly changes that policy.
