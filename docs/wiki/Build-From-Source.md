# Build From Source

The unified cross-platform application builds from `src\FramePlayer.Avalonia`. It also requires Rust/Cargo for release packaging; the Rust toolchain is pinned by `rust-toolchain.toml`.

On Windows, restore the pinned playback and export runtimes before building or packaging:

```powershell
.\scripts\Ensure-DevRuntime.ps1
.\scripts\Ensure-DevExportRuntime.ps1 -Required
.\scripts\Build-RustFfmpegProbe.ps1
dotnet build .\src\FramePlayer.Avalonia\FramePlayer.Avalonia.csproj -c Release
dotnet test .\tests\FramePlayer.Core.Tests\FramePlayer.Core.Tests.csproj -c Release
dotnet test .\tests\FramePlayer.Avalonia.Tests\FramePlayer.Avalonia.Tests.csproj -c Release --filter "Category!=ReleaseCandidate"
.\scripts\Package-UnifiedWindows.ps1 -Version <release-version>
```

On macOS, stage the pinned macOS FFmpeg runtime under `Runtime/macos/<rid>/ffmpeg`, make sure `cargo` is available, then run the matching native commands. On an Intel Mac, use `osx-x64`:

```bash
scripts/Build-RustFfmpegProbe.sh osx-x64
dotnet build src/FramePlayer.Avalonia/FramePlayer.Avalonia.csproj -c Release -r osx-x64
dotnet test tests/FramePlayer.Core.Tests/FramePlayer.Core.Tests.csproj -c Release
dotnet test tests/FramePlayer.Avalonia.Tests/FramePlayer.Avalonia.Tests.csproj -c Release --filter "Category!=ReleaseCandidate"
MAC_RUNTIME_IDENTIFIER=osx-x64 script/validate_macos_release_candidate.sh --corpus "Video Test Files"
```

Package a local signed release build:

```bash
PACKAGE_VERSION="<release-version>" script/package_unified_macos_release.sh --sign
codesign --verify --deep --verbose=2 "dist/Frame Player.app"
```

Replace `<release-version>` with the exact candidate or final version being packaged. The package scripts build the first-party Rust FFmpeg native library and include it beside the Avalonia executable. Normal dev builds can run without the native library, but release packaging requires Rust/Cargo. The exact frame index builder, indexed decode-window helper, and BGRA frame converter can be forced with `FRAMEPLAYER_FFMPEG_INDEX_BUILDER=rust`, `FRAMEPLAYER_FFMPEG_DECODE_CORE=rust`, and `FRAMEPLAYER_FFMPEG_FRAME_CONVERTER=rust`; each can be bypassed with `managed` or left in fallback mode with `auto`.

Developer ID notarization is documented in [docs/macos-release.md](https://github.com/jfleezy23/frame-player/blob/main/docs/macos-release.md).

## Runtime Notes

FFmpeg playback and export runtimes are pinned. Runtime binaries are staged locally and are not committed unless a later release explicitly changes that policy.
