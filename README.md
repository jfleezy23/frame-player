# Frame Player

Compact WPF desktop video player built on a custom FFmpeg engine with a bundled in-process FFmpeg runtime.

Frame Player is a compact Windows video player focused on exact frame stepping, quick playback controls, and easy local packaging.

## What It Does

- Opens `.avi`, `.mov`, `.m4v`, `.mp4`, `.mkv`, `.wmv`, and `.ts`
- Uses a standard desktop UI with `Open Video`, `Open Recent`, and `Close Video`
- Supports drag and drop for supported files
- Supports play, pause, rewind 5 seconds, fast forward 5 seconds, previous frame, and next frame
- Plays decoded audio when a supported audio stream is present, with video-only fallback for silent clips or unsupported audio
- Lets you jump directly to a frame number
- Shows the frame jump field as `current / total`
- Supports full screen playback controls
- Keeps Left and Right for single-frame stepping while paused, and supports hold-to-repeat stepping
- Shows playback state, FPS, frame-step size, duration, frame number, and a frame-derived timecode readout
- Shows decoded-frame cache status so it is clear when seek/step operations are warming or rebuilding the local cache
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
- after entering a frame number, `Enter` commits the jump and returns focus to video controls for immediate arrow-key stepping
- `J` / `L` seek backward or forward 5 seconds
- `F11` or `Alt+Enter` toggles full screen

## Packaging Model

The shipped app is packaged with the FFmpeg runtime DLLs next to `FramePlayer.exe`.

- There is no FFmpeg folder picker in the UI
- The app does not call `ffmpeg.exe` or `ffprobe.exe`
- Playback and frame stepping run through the custom FFmpeg engine and the bundled FFmpeg DLLs loaded in-process
- Playback uses a simple audio-master clock when audio output is active, while exact frame stepping remains decode/index based
- Session diagnostics are mirrored to `%LocalAppData%\\FramePlayer\\Logs\\latest-session.log` and protected at rest with Windows DPAPI
- `File > Export Diagnostics...` saves a shareable text report with runtime and playback state
- Recent-file history is protected at rest with Windows DPAPI for the current user profile
- Diagnostics and UI error messages redact absolute file paths where practical
- A signed local MSIX can be built with `Packaging\\MSIX\\build-msix.ps1`
- The generated MSIX artifacts are written to `dist\\MSIX`
- The FFmpeg development runtime is downloaded on demand instead of being stored in git
- The pinned runtime archive and DLL hashes are recorded in `Runtime\\runtime-manifest.json`

## Quick Start

If you just want a working local build without thinking about dependencies:

```powershell
.\scripts\Build-FramePlayer.ps1
```

That script:

1. Downloads the pinned FFmpeg runtime from this repository's GitHub release assets
2. Verifies the runtime archive SHA256 and the extracted DLL hashes
3. Restores NuGet packages
4. Builds the app in `Release|x64`

Regular Visual Studio and MSBuild builds now try to bootstrap the pinned runtime automatically if `Runtime\ffmpeg` is missing.

## Requirements

- Windows 10 or later
- Visual Studio 2022 Build Tools or Visual Studio 2022 with `.NET desktop development`
- `.NET Framework 4.8 Developer Pack`
- PowerShell 5.1 or later

## Build From Source

For most machines, use the helper script:

```powershell
.\scripts\Build-FramePlayer.ps1
```

If the build machine blocks the automatic runtime download, run `.\scripts\Ensure-DevRuntime.ps1` once and rebuild.

If you need to call MSBuild directly, use a Visual Studio Developer PowerShell or a machine with Visual Studio Build Tools installed:

```powershell
.\scripts\Ensure-DevRuntime.ps1
& "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\amd64\MSBuild.exe" .\FramePlayer.csproj /t:Restore,Build /p:Configuration=Release /p:Platform=x64
```

To build the local MSIX package:

```powershell
powershell -ExecutionPolicy Bypass -File .\Packaging\MSIX\build-msix.ps1 -UseDevCertificate -CertificatePassword "YourLocalPassword"
```

To build the MSIX package with an organization's trusted signing certificate:

```powershell
powershell -ExecutionPolicy Bypass -File .\Packaging\MSIX\build-msix.ps1 -SigningPfxPath "C:\path\to\signing-cert.pfx" -SigningPfxPassword "YourPfxPassword" -TimestampUrl "https://your-approved-timestamp-service"
```

## Notes

- Frame stepping uses decoded display-order frame identity instead of timestamp math
- Timecode is frame-derived and uses nominal whole-frame buckets for fractional frame rates like `23.976` -> `24`
- The build output is in `bin\Release`
- The packaged app folder used for testing is `dist\Frame Player`
- The runtime bootstrap is pinned through `Runtime\runtime-manifest.json`

## License

- Project source: [MIT](LICENSE)
- Third-party components and runtime notes: [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)

## Deployment Notes

- The portable ZIP is convenient for testing, but production deployments should prefer a signed package and a protected install location.
- The included MSIX script now requires either `-UseDevCertificate` for local testing or `-SigningPfxPath` for real signing.
- For production or government deployments, use an organization's trusted code-signing certificate, timestamp the signature, and distribute the app through a protected install location.
- The About dialog intentionally stays short; full license and third-party notice text ships as `LICENSE` and `THIRD_PARTY_NOTICES.md` instead of being embedded in the dialog.
