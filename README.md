# Frame Player

Compact WPF desktop video player built on FFME with a bundled in-process FFmpeg runtime.

Frame Player is a compact Windows video player focused on exact frame stepping, quick playback controls, and easy local packaging.

## What It Does

- Opens `.avi`, `.mov`, `.m4v`, and `.mp4`
- Uses a standard desktop UI with `Open Video`, `Open Recent`, and `Close Video`
- Supports drag and drop for supported files
- Supports play, pause, rewind 5 seconds, fast forward 5 seconds, previous frame, and next frame
- Lets you jump directly to a frame number
- Shows the frame jump field as `current / total`
- Supports full screen playback controls
- Keeps Left and Right for single-frame stepping while paused, and supports hold-to-repeat stepping
- Shows playback state, FPS, frame-step size, duration, frame number, and a frame-derived timecode readout
- Can export a diagnostic session log for external testers
- Includes `Help` and `About` dialogs in the menu bar

## Shortcuts

- `Ctrl+O` opens a video
- `Ctrl+W` closes the current video
- `Ctrl+Shift+E` exports diagnostics
- `F1` opens help
- `Space` plays or pauses
- `Left` / `Right` step one frame while paused
- hold `Left` / `Right` to continue stepping frame by frame
- `J` / `L` seek backward or forward 5 seconds
- `F11` or `Alt+Enter` toggles full screen

## Packaging Model

The shipped app is packaged with the FFmpeg runtime DLLs next to `FramePlayer.exe`.

- There is no FFmpeg folder picker in the UI
- The app does not call `ffmpeg.exe` or `ffprobe.exe`
- Playback and frame stepping run through FFME and the bundled FFmpeg DLLs loaded in-process
- Session diagnostics are written to `%LocalAppData%\\FramePlayer\\Logs\\latest-session.log`
- `File > Export Diagnostics...` saves a shareable text report with runtime and playback state
- A signed local MSIX can be built with `Packaging\\MSIX\\build-msix.ps1`
- The generated MSIX artifacts are written to `dist\\MSIX`
- The FFmpeg development runtime is downloaded on demand instead of being stored in git

## Quick Start

If you just want a working local build without thinking about dependencies:

```powershell
.\scripts\Build-FramePlayer.ps1
```

That script:

1. Downloads the pinned FFmpeg runtime from this repository's GitHub release assets
2. Restores NuGet packages
3. Builds the app in `Release|x64`

## Requirements

- Windows 10 or later
- Visual Studio 2022 Build Tools or Visual Studio 2022 with `.NET desktop development`
- `.NET Framework 4.8 Developer Pack`
- PowerShell 5.1 or later

## Build From Source

From a Visual Studio Developer PowerShell or with MSBuild installed:

```powershell
.\scripts\Ensure-DevRuntime.ps1
& "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\amd64\MSBuild.exe" .\Rpcs3VideoPlayer.csproj /t:Restore,Build /p:Configuration=Release /p:Platform=x64
```

To build the local MSIX package:

```powershell
powershell -ExecutionPolicy Bypass -File .\Packaging\MSIX\build-msix.ps1 -CertificatePassword "YourLocalPassword"
```

## Notes

- Frame stepping uses FFME's native step APIs instead of timestamp math
- Timecode is frame-derived and uses nominal whole-frame buckets for fractional frame rates like `23.976` -> `24`
- The build output is in `bin\Release`
- The packaged app folder used for testing is `dist\Frame Player`
- The runtime bootstrap is pinned through `Runtime\runtime-manifest.json`

## License

- Project source: [MIT](LICENSE)
- Third-party components and runtime notes: [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)
