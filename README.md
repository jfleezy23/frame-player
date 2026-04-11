# Frame Player

Frame Player is a frames-first WPF review tool built on a custom FFmpeg engine with a bundled in-process FFmpeg runtime. It treats decoded display-order frame identity as the source of truth for exact frame stepping, frame seeks, and review state, prioritizing deterministic review behavior over generic consumer media-player parity.

## What It Does

- Opens `.avi`, `.mov`, `.m4v`, `.mp4`, `.mkv`, and `.wmv`
- Uses a standard desktop UI with `Open Video`, `Open Recent`, and `Close Video`
- Supports drag and drop for supported files
- Supports play, pause, rewind 5 seconds, fast forward 5 seconds, previous frame, and next frame
- Plays decoded audio when a supported audio stream is present, with video-only fallback for silent clips or unsupported audio
- Lets you jump directly to a frame number
- Uses 1-based frame numbers in the UI and shows current / total in the status bar
- Supports full screen playback controls
- Keeps Left and Right for single-frame stepping while paused, and supports hold-to-repeat stepping
- Shows playback state, FPS, frame-step size, duration, frame number, and a frame-derived timecode readout
- Shows decoded-frame cache status so it is clear when seek/step operations are warming or rebuilding the local cache
- Auto-enables Vulkan-backed FFmpeg decode when the current runtime, driver, codec, and device path are verified, with CPU fallback everywhere else
- Persists a visible `Playback > Use GPU Acceleration` preference for newly opened media
- Can export a diagnostic session log for external testers
- Includes `Help` and `About` dialogs in the menu bar

## Review Architecture

- The custom FFmpeg engine decodes video frames directly through `FFmpeg.AutoGen`, treats neutral BGRA frame buffers as the engine-to-shell contract, and only creates WPF `BitmapSource` objects at presentation time.
- Decoded display-order frame identity is the source of truth; frame stepping is not derived from slider position, timestamp math, nominal FPS, or wall-clock playback time.
- A file-global frame index maps zero-based absolute frame indices to stream timestamps and decode anchors so seeks can materialize the real target frame through FFmpeg seek/decode-forward work.
- A decoded review cache keeps a hardware-aware local window around the cursor for responsive frame review; GPU sessions avoid a forward review cache and spend budget on exact/backward resilience instead.
- Playback uses the audio clock when audio output is active, otherwise video presentation timing, while pause/seek/step operations continue to preserve exact frame identity.

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

- Latest published release: `v1.3.0`
- Pinned FFmpeg runtime version: `n8.1-frameplayer-source`
- Runtime provenance: built from the official FFmpeg source tag `n8.1` at commit `9047fa1b084f76b1b4d065af2d743df1b40dfb56`
- Runtime hashes and source-build metadata are recorded in `Runtime\\runtime-manifest.json` and `docs\\ffmpeg-8.1-build-notes.md`
- The current source-build path enables FFmpeg's Vulkan hardware-device support, but actual GPU acceleration still depends on a system Vulkan loader/driver at runtime
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
- The FFmpeg development runtime is not stored in git; local restore comes from the self-built candidate folder or local runtime archive staged by `scripts\ffmpeg\Build-FFmpeg-8.1.ps1`
- The runtime manifest records the expected archive filename, archive SHA256, human-readable FFmpeg version, DLL hashes, and source-build metadata
- The current manifest does not yet declare a verified published FFmpeg 8.1 restore `tag` or `assetUrl` for clean-runner bootstrap

## Quick Start

If you just want a working local build without thinking about dependencies:

```powershell
.\scripts\Build-FramePlayer.ps1
```

That script:

1. Restores the pinned FFmpeg runtime from the local source-built candidate or local runtime archive when available
2. Verifies the runtime archive SHA256 and the extracted DLL hashes
3. Restores NuGet packages
4. Builds the app in `Release|x64`

If the self-built runtime has not been staged yet, run `.\scripts\ffmpeg\Build-FFmpeg-8.1.ps1` first. Regular Visual Studio and MSBuild builds still bootstrap `Runtime\ffmpeg` automatically when it is missing, but today that flow assumes the FFmpeg 8.1 runtime has already been staged locally.

For phase-1 GPU validation, keep the default `Playback > Use GPU Acceleration` setting enabled and test on a machine with a working Vulkan loader/driver. Unsupported systems and unsupported codec/device combinations stay on CPU decode automatically.

## Requirements

- Windows 10 or later
- Visual Studio 2022 Build Tools or Visual Studio 2022 with `.NET desktop development`
- `.NET Framework 4.8 Developer Pack`
- PowerShell 5.1 or later

For Vulkan-backed GPU decode testing, the local machine also needs a working Vulkan loader/driver. CPU decode remains the default fallback when Vulkan is unavailable or unsupported.

## Build From Source

For most machines, use the helper script:

```powershell
.\scripts\Build-FramePlayer.ps1
```

If the active runtime directory is missing, run `.\scripts\ffmpeg\Build-FFmpeg-8.1.ps1` once, then `.\scripts\Ensure-DevRuntime.ps1`, and rebuild.

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

## Windows CI

GitHub Actions Windows CI is compile validation on a clean runner. The workflow builds with `/p:SkipRuntimeBootstrap=true`, so it intentionally skips runtime bootstrap in CI while local/dev builds continue to use the default bootstrap path. This stays in place until the manifest has a verified published FFmpeg 8.1 restore source for clean-runner acquisition.

## Notes

- Frame stepping uses decoded display-order frame identity instead of timestamp math
- Timecode is frame-derived and uses nominal whole-frame buckets for fractional frame rates like `23.976` -> `24`
- The standard build output is in `bin\Release`
- The packaged test-drop output used for release verification is `bin\TestDrop`
- The portable release archive is written to `artifacts\FramePlayer-CustomFFmpeg-1.3.0.zip`
- The runtime bootstrap is pinned through `Runtime\runtime-manifest.json`
- The active runtime is the self-built FFmpeg 8.1 line staged by `scripts\ffmpeg\Build-FFmpeg-8.1.ps1`

## License

- Project source: [MIT](LICENSE)
- Third-party components and runtime notes: [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)

## Deployment Notes

- The portable ZIP is convenient for testing, but production deployments should prefer a signed package and a protected install location.
- The included MSIX script now requires either `-UseDevCertificate` for local testing or `-SigningPfxPath` for real signing.
- For production or government deployments, use an organization's trusted code-signing certificate, timestamp the signature, and distribute the app through a protected install location.
- The About dialog intentionally stays short; full license and third-party notice text ships as `LICENSE` and `THIRD_PARTY_NOTICES.md` instead of being embedded in the dialog.
