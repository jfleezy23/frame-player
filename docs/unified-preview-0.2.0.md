# Unified Preview 0.2.0

This branch prepares the first synchronized Windows/macOS Avalonia preview. The target prerelease tag is `unified-preview-0.2.0`.

## Branch Discipline

- Implementation branch: `codex/unified-avalonia-preview-0.2.0`.
- Do not release from `main` until the unified PR is reviewed and merged.
- Request CI, SonarCloud, dependency review, CodeQL if enabled for the repository, and `@codex review` before creating the prerelease.

## Artifacts

- Windows: `FramePlayer-Windows-x64-unified-preview-0.2.0.zip`.
- macOS: `FramePlayer-macOS-arm64-unified-preview-0.2.0.zip`.
- Both ZIPs must be attached to the same GitHub prerelease: `unified-preview-0.2.0`.

## Local Validation

```bash
dotnet build src/FramePlayer.Avalonia/FramePlayer.Avalonia.csproj -c Release
dotnet test tests/FramePlayer.Avalonia.Tests/FramePlayer.Avalonia.Tests.csproj -c Release
dotnet test tests/FramePlayer.Desktop.Tests/FramePlayer.Desktop.Tests.csproj -c Release
dotnet test tests/FramePlayer.Mac.Tests/FramePlayer.Mac.Tests.csproj -c Release --filter "Category!=ReleaseCandidate"
```

For macOS package smoke validation:

```bash
PACKAGE_VERSION=unified-preview-0.2.0 script/package_unified_macos_release.sh --unsigned
```

For Windows package validation, run on Windows after restoring the pinned runtimes:

```powershell
.\scripts\Ensure-DevRuntime.ps1
.\scripts\Ensure-DevExportRuntime.ps1 -Required
.\scripts\Package-UnifiedWindowsPreview.ps1
```

## Release Gate

- Windows ZIP launches `FramePlayer.Avalonia.exe` and plays H.264/AAC with audible audio.
- macOS ZIP is Developer ID signed, notarized, stapled, extracted, and accepted by Gatekeeper.
- SHA256 files are generated for both artifacts.
- Release notes and wiki links must use `0.2.0` for both platforms.
