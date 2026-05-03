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
- Windows FFmpeg playback and export runtimes are staged and discovered.
- Audio playback, open/recent files, two-pane compare, loop/export, diagnostics, and audio insertion work.
- Screenshots confirm layout stability, disabled states, text fit, and no transport/control movement.

Do not merge this branch until the Windows validation evidence is recorded in the PR.
