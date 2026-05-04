# Avalonia Windows Preview Handoff

This branch adds a separate Avalonia desktop preview surface for Windows validation.

Protected reference builds:

- Windows WPF `v1.8.4` remains the stable Windows release path.
- macOS Preview `0.1.0` remains the sealed macOS preview path.

Do not edit either release surface while validating this branch. The new work lives under:

- `src/FramePlayer.Desktop`
- `tests/FramePlayer.Desktop.Tests`

## Mac-Side Validation

Run these before handing the branch to Windows:

```bash
~/.dotnet/dotnet build src/FramePlayer.Mac/FramePlayer.Mac.csproj -c Release
~/.dotnet/dotnet test tests/FramePlayer.Mac.Tests/FramePlayer.Mac.Tests.csproj -c Release --filter "Category!=ReleaseCandidate"
~/.dotnet/dotnet build src/FramePlayer.Desktop/FramePlayer.Desktop.csproj -c Release
~/.dotnet/dotnet test tests/FramePlayer.Desktop.Tests/FramePlayer.Desktop.Tests.csproj -c Release
```

## Windows Handoff

```powershell
git fetch origin
git switch codex/avalonia-windows-preview
```

Required Windows validation:

- Existing WPF build and tests still pass.
- `src\FramePlayer.Desktop\FramePlayer.Desktop.csproj` builds and launches on Windows.
- Windows FFmpeg playback DLLs are staged beside `FramePlayer.Desktop.exe`; export DLLs are staged under `ffmpeg-export`.
- Open/recent files, two-pane compare, loop/export, diagnostics, and audio insertion work.
- Screenshots confirm layout stability, disabled states, text fit, and no transport/control movement.

Do not merge this branch until the Windows validation evidence is recorded in the PR.

## Windows Validation Notes

Validated on Windows on 2026-05-02:

- `dotnet --info`
- `dotnet restore src/FramePlayer.Desktop/FramePlayer.Desktop.csproj`
- `dotnet build FramePlayer.csproj -c Release -p:Platform=x64`
- `dotnet test tests/FramePlayer.Core.Tests/FramePlayer.Core.Tests.csproj -c Release`
- `dotnet build src/FramePlayer.Desktop/FramePlayer.Desktop.csproj -c Release`
- `dotnet test tests/FramePlayer.Desktop.Tests/FramePlayer.Desktop.Tests.csproj -c Release`
- `dotnet run --project src/FramePlayer.Desktop/FramePlayer.Desktop.csproj -c Release`

Observed:

- The Desktop shell launched and loaded a temp H.264/AAC MP4 through the Windows open dialog path.
- Desktop recent files were written under `%LOCALAPPDATA%\FramePlayer.DesktopPreview`, separate from the WPF `%LOCALAPPDATA%\FramePlayer` area.
- Playback advanced frames and UI state to the end of the temp clip.
- The hidden export-host probe wrote a valid response for the temp H.264/AAC MP4 after waiting for the WinExe child process.

Remaining risk:

- Audible Windows playback is not implemented in the `src/FramePlayer.Engine.FFmpeg` path. Non-macOS playback currently uses `ManagedAudioClockOutput`, which advances audio-clock timing from submitted PCM byte counts without writing PCM to a Windows audio device. The legacy WPF root engine has a separate `WinMmAudioOutput`, but that delivered WPF path is intentionally untouched by this preview branch.
- Smallest follow-up is a separate shared-engine pass that adds a Windows `IAudioOutput` implementation under `src/FramePlayer.Engine.FFmpeg`, adapted from the existing WPF `WinMmAudioOutput`, and switches `AudioOutputFactory` to it only on Windows.
