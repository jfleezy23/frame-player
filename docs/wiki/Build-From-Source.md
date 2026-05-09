# Build From Source

## Windows Stable

The Windows stable app remains the WPF release line. This Wiki does not change the Windows source path, build path, runtime bootstrap, tests, or release process.

Recommended build:

```powershell
.\scripts\Build-FramePlayer.ps1
```

Direct build when the runtime is already staged:

```powershell
.\scripts\Ensure-DevRuntime.ps1
.\scripts\Ensure-DevExportTools.ps1
.\scripts\Ensure-DevExportRuntime.ps1
dotnet build .\FramePlayer.csproj -c Release -p:Platform=x64
```

## Unified Avalonia Preview

The current cross-platform preview builds from `src\FramePlayer.Avalonia`. It also requires Rust/Cargo for release packaging; the Rust toolchain is pinned by `rust-toolchain.toml`. On Windows, restore the pinned playback and export runtimes before building or packaging:

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
PACKAGE_VERSION=unified-preview-0.3.0 script/package_unified_macos_release.sh --sign
codesign --verify --deep --verbose=2 "dist/Frame Player.app"
```

The unified preview package scripts build the first-party Rust FFmpeg native library and include it beside the Avalonia executable. Normal dev builds can run without the native library, but release packaging requires Rust/Cargo. The exact frame index builder, indexed decode-window helper, and BGRA frame converter can be forced with `FRAMEPLAYER_FFMPEG_INDEX_BUILDER=rust`, `FRAMEPLAYER_FFMPEG_DECODE_CORE=rust`, and `FRAMEPLAYER_FFMPEG_FRAME_CONVERTER=rust`; each can be bypassed with `managed` or left in fallback mode with `auto`.

Developer ID notarization is documented in [docs/macos-preview-release.md](https://github.com/jfleezy23/frame-player/blob/main/docs/macos-preview-release.md).

The old `src\FramePlayer.Mac` and `src\FramePlayer.Desktop` split-preview projects are superseded by the unified preview project.

## Runtime Notes

FFmpeg playback and export runtimes are pinned. Runtime binaries are staged locally and are not committed unless a later release explicitly changes that policy.
