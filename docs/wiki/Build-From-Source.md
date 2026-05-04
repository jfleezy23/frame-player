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

## macOS Preview

Stage the pinned macOS FFmpeg runtime under `Runtime/macos/osx-arm64/ffmpeg`, then run:

```bash
dotnet build src/FramePlayer.Mac/FramePlayer.Mac.csproj -c Release
dotnet test tests/FramePlayer.Mac.Tests/FramePlayer.Mac.Tests.csproj -c Release --filter "Category!=ReleaseCandidate"
FRAMEPLAYER_MAC_CORPUS="Video Test Files" script/validate_macos_release_candidate.sh --corpus "Video Test Files"
```

Package a local signed release candidate:

```bash
script/package_macos_release.sh --sign
codesign --verify --deep --verbose=2 "dist/Frame Player.app"
```

Developer ID notarization is documented in [docs/macos-preview-release.md](https://github.com/jfleezy23/frame-player/blob/main/docs/macos-preview-release.md).

## Windows Avalonia Preview

The Windows Avalonia Preview builds from `src\FramePlayer.Desktop` and uses separate preview tests.

```powershell
dotnet restore src\FramePlayer.Desktop\FramePlayer.Desktop.csproj
dotnet build src\FramePlayer.Desktop\FramePlayer.Desktop.csproj -c Release
dotnet test tests\FramePlayer.Desktop.Tests\FramePlayer.Desktop.Tests.csproj -c Release
```

For a local tester ZIP, publish the Desktop preview and compress the published folder:

```powershell
dotnet publish src\FramePlayer.Desktop\FramePlayer.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o artifacts\avalonia-windows-preview\publish
Compress-Archive -Path artifacts\avalonia-windows-preview\publish\* -DestinationPath artifacts\avalonia-windows-preview\FramePlayer-Desktop-Windows-x64-local.zip -Force
```

## Runtime Notes

FFmpeg playback and export runtimes are pinned. Runtime binaries are staged locally and are not committed unless a later release explicitly changes that policy.
