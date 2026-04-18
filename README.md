# Frame Player

Frame Player is a frames-first WPF review tool built on a custom FFmpeg engine with a bundled in-process FFmpeg runtime. It treats decoded display-order frame identity as the source of truth for exact frame stepping, frame seeks, and review state, prioritizing deterministic review behavior over generic consumer media-player parity.

## What It Does

- Opens `.avi`, `.mov`, `.m4v`, `.mp4`, `.mkv`, and `.wmv`
- Uses a standard desktop UI with `Open Video`, `Open Recent`, and `Close Video`
- Supports drag and drop for supported files
- Supports play, pause, rewind 5 seconds, fast forward 5 seconds, previous frame, and next frame
- Plays decoded audio when a supported audio stream is present, with video-only fallback for silent clips or unsupported audio
- Supports two-pane compare review on the current WPF host
- Uses shared main transport plus pane-local transport and navigation in two-pane compare mode
- Lets you jump directly to a frame number
- Shows frame-number pending state instead of a fake numeric claim while background indexing is still resolving absolute frame identity
- Uses 1-based frame numbers in the UI and shows current / total in the status bar
- Supports full screen playback controls
- Keeps Left and Right for single-frame stepping while paused, and supports hold-to-repeat stepping
- Supports `Ctrl+Left` / `Ctrl+Right` for 10-frame moves and `Shift+Left` / `Shift+Right` for 100-frame moves while paused
- Supports live timeline scrubbing that lands paused on release
- Supports whole-media loop playback and exact A/B loop playback on the main transport using `[` and `]`
- In compare mode, the pane-local sliders can carry independent pane-local loop boxes for focused review
- Saves reviewed shared-loop and pane-local loop ranges as MP4 clip exports through a bundled FFmpeg CLI toolset
- Exports two-pane compare sessions as full-resolution side-by-side MP4 output in loop or whole-video mode, with audio selectable from either pane
- Shows a labeled pixel coordinate readout for the hovered pane
- Includes a structured `Video Info` inspector for FFmpeg-reported pane media metadata, including right-click access on video panes and compare-friendly modeless windows
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
- Compare mode now uses a pane-aware decoded-frame budget so mixed CPU/GPU sessions split one session budget while still favoring reverse-history stability over speculative forward caching.
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
- `[` sets loop-in on the current loop context
- `]` sets loop-out on the current loop context
- `,` / `.` seek backward or forward 5 seconds
- `L` toggles loop playback
- `F11` or `Alt+Enter` toggles full screen

## Packaging Model

The shipped app is packaged with the FFmpeg runtime DLLs next to `FramePlayer.exe`.

- Current release: `v1.7.1`
- Pinned FFmpeg runtime version: `n8.1-frameplayer-source`
- Runtime provenance: built from the official FFmpeg source tag `n8.1` at commit `9047fa1b084f76b1b4d065af2d743df1b40dfb56`
- Runtime hashes and source-build metadata are recorded in `Runtime\\runtime-manifest.json` and `docs\\ffmpeg-8.1-build-notes.md`
- Export-tool hashes and source-build metadata are recorded in `Runtime\\export-tools-manifest.json`
- The current source-build path enables FFmpeg's Vulkan hardware-device support, but actual GPU acceleration still depends on a system Vulkan loader/driver at runtime
- There is no FFmpeg folder picker in the UI
- Playback and frame stepping do not call `ffmpeg.exe` or `ffprobe.exe`; reviewed clip export uses a separate bundled `ffmpeg-tools\\ffmpeg.exe` / `ffmpeg-tools\\ffprobe.exe` toolset
- Playback and frame stepping run through the custom FFmpeg engine and the bundled FFmpeg DLLs loaded in-process
- Clip export runs through a separate pinned FFmpeg CLI bundle stored under `ffmpeg-tools` beside the app output
- Playback uses a simple audio-master clock when audio output is active, while exact frame stepping remains decode/index based
- Session diagnostics are mirrored to `%LocalAppData%\\FramePlayer\\Logs\\latest-session.log` and protected at rest with Windows DPAPI
- `File > Export Diagnostics...` saves a shareable text report with runtime and playback state
- Recent-file history is protected at rest with Windows DPAPI for the current user profile
- Diagnostics and UI error messages redact absolute file paths where practical
- A signed local MSIX can be built with `Packaging\\MSIX\\build-msix.ps1`
- The generated MSIX artifacts are written to `dist\\MSIX`
- The FFmpeg development runtime is not stored in git; local restore comes from the self-built candidate folder or local runtime archive staged by `scripts\ffmpeg\Build-FFmpeg-8.1.ps1`
- The runtime manifest records the expected archive filename, archive SHA256, human-readable FFmpeg version, DLL hashes, and source-build metadata
- The current manifest declares the verified `v1.5.0` GitHub release asset used for clean-runner bootstrap

## Quick Start

If you just want a working local build without thinking about dependencies:

```powershell
.\scripts\Build-FramePlayer.ps1
```

That script:

1. Restores the pinned FFmpeg runtime from the local source-built candidate or local runtime archive when available
2. Attempts to restore the pinned FFmpeg export tools when a populated export-tools manifest is available
3. Verifies the runtime archive SHA256 and the extracted DLL hashes
4. Restores NuGet packages
5. Builds the app in `Release|x64`

If the self-built runtime has not been staged yet, run `.\scripts\ffmpeg\Build-FFmpeg-8.1.ps1` first if you want a local candidate/runtime archive. If you also want clip export available in the local build, run `.\scripts\ffmpeg\Build-FFmpeg-Tools-8.1.ps1` and then `.\scripts\Ensure-DevExportTools.ps1`. Regular Visual Studio and `dotnet` CLI builds still bootstrap `Runtime\ffmpeg` automatically when it is missing, and clean bootstrap environments fall back to the verified `v1.5.0` release asset.

For phase-1 GPU validation, keep the default `Playback > Use GPU Acceleration` setting enabled and test on a machine with a working Vulkan loader/driver. Unsupported systems and unsupported codec/device combinations stay on CPU decode automatically.

## Requirements

- Windows 10 or later
- .NET 10 SDK (`10.0.2xx`)
- Visual Studio 2026 Build Tools or Visual Studio 2026 with `.NET desktop development` for fully supported `net10.0-windows` targeting
- PowerShell 5.1 or later

For Vulkan-backed GPU decode testing, the local machine also needs a working Vulkan loader/driver. CPU decode remains the default fallback when Vulkan is unavailable or unsupported.

## Build From Source

For most machines, use the helper script:

```powershell
.\scripts\Build-FramePlayer.ps1
```

If the active runtime directory is missing, run `.\scripts\ffmpeg\Build-FFmpeg-8.1.ps1` once, then `.\scripts\Ensure-DevRuntime.ps1`, and rebuild. If the export-tools manifest has been pinned locally and you want clip export enabled, run `.\scripts\ffmpeg\Build-FFmpeg-Tools-8.1.ps1` and `.\scripts\Ensure-DevExportTools.ps1` too.

If you need to build directly instead of using the helper script, use a machine with the .NET 10 SDK installed:

```powershell
.\scripts\Ensure-DevRuntime.ps1
.\scripts\Ensure-DevExportTools.ps1
dotnet build .\FramePlayer.csproj -c Release -p:Platform=x64
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

GitHub Actions Windows CI is compile validation on a clean runner. The workflow restores the pinned FFmpeg runtime through `scripts\Ensure-DevRuntime.ps1` before building, using the currently verified `v1.5.0` runtime archive published on GitHub Releases. Local/dev builds continue to use the same bootstrap path, with local candidate/runtime archives still preferred when they are available.

## Notes

- Frame stepping uses decoded display-order frame identity instead of timestamp math
- Timecode is frame-derived and uses nominal whole-frame buckets for fractional frame rates like `23.976` -> `24`
- A/B loop ranges can be saved as exact MP4 clip exports through the bundled `ffmpeg-tools` path
- The standard build output is in `bin\Release`
- The packaged test-drop output used for release verification is `bin\TestDrop`
- The portable release archive is written to `artifacts\FramePlayer-CustomFFmpeg-<product-version>.zip`
- The runtime bootstrap is pinned through `Runtime\runtime-manifest.json`
- The active runtime is the self-built FFmpeg 8.1 line staged by `scripts\ffmpeg\Build-FFmpeg-8.1.ps1`
- The current release note is `docs\release-v1.7.1-keyboard-controls.md`

## License

- Project source: [MIT](LICENSE)
- Third-party components and runtime notes: [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)

## Deployment Notes

- The portable ZIP is convenient for testing, but production deployments should prefer a signed package and a protected install location.
- The included MSIX script now requires either `-UseDevCertificate` for local testing or `-SigningPfxPath` for real signing.
- For production or government deployments, use an organization's trusted code-signing certificate, timestamp the signature, and distribute the app through a protected install location.
- The About dialog intentionally stays short; full license and third-party notice text ships as `LICENSE` and `THIRD_PARTY_NOTICES.md` instead of being embedded in the dialog.
