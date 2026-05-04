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

The current cross-platform preview builds from `src\FramePlayer.Avalonia`. On Windows, restore the pinned playback and export runtimes before building or packaging:

```powershell
.\scripts\Ensure-DevRuntime.ps1
.\scripts\Ensure-DevExportRuntime.ps1 -Required
dotnet build .\src\FramePlayer.Avalonia\FramePlayer.Avalonia.csproj -c Release
dotnet test .\tests\FramePlayer.Avalonia.Tests\FramePlayer.Avalonia.Tests.csproj -c Release
.\scripts\Package-UnifiedWindowsPreview.ps1
```

On macOS, stage the pinned macOS FFmpeg runtime under `Runtime/macos/osx-arm64/ffmpeg`, then run:

```bash
dotnet build src/FramePlayer.Avalonia/FramePlayer.Avalonia.csproj -c Release
dotnet test tests/FramePlayer.Avalonia.Tests/FramePlayer.Avalonia.Tests.csproj -c Release
FRAMEPLAYER_MAC_CORPUS="Video Test Files" script/validate_macos_release_candidate.sh --corpus "Video Test Files"
```

Package a local signed release candidate:

```bash
PACKAGE_VERSION=unified-preview-0.2.0 script/package_unified_macos_release.sh --sign
codesign --verify --deep --verbose=2 "dist/Frame Player.app"
```

Developer ID notarization is documented in [docs/macos-preview-release.md](https://github.com/jfleezy23/frame-player/blob/main/docs/macos-preview-release.md).

The old `src\FramePlayer.Mac` and `src\FramePlayer.Desktop` split-preview projects are superseded by the unified preview project.

## Runtime Notes

FFmpeg playback and export runtimes are pinned. Runtime binaries are staged locally and are not committed unless a later release explicitly changes that policy.
