# Unified Preview 0.2.0

This note documents the first synchronized Windows/macOS Avalonia preview.

- Release: [Frame Player Unified Preview 0.2.0](https://github.com/jfleezy23/frame-player/releases/tag/unified-preview-0.2.0)
- Tag: `unified-preview-0.2.0`
- Release target: `585a277f4c6c939562d1fdd10de2c31370b4ebb6`
- Windows artifact: `FramePlayer-Windows-x64-unified-preview-0.2.0.zip`
- Windows SHA256: `f417f3535627da5ea857cc8e9aec23bcebb83ac56a40bc26edeec1fbc5fdce79`
- macOS artifact: `FramePlayer-macOS-arm64-unified-preview-0.2.0.zip`
- macOS SHA256: `9dd253ffd1e18bceb7432230100bfbc5d7cd7cb4975c5458f1357317d72afb87`
- Apple notarization submission: `cd7a2d35-df28-4170-bd31-3ca49e4acebd`

## Branch Discipline

- Implementation PR: #65, merged into `main`.
- macOS ZIP-signature packaging fix PR: #66, merged into `main`.
- PR #65 and PR #66 received `@codex review`; no major issues were reported after the final changes.

## Artifacts

- Windows: `FramePlayer-Windows-x64-unified-preview-0.2.0.zip`.
- macOS: `FramePlayer-macOS-arm64-unified-preview-0.2.0.zip`.
- Both ZIPs are attached to the same GitHub prerelease: `unified-preview-0.2.0`.

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

- GitHub checks on the release target passed: Windows CI, macOS Avalonia, SonarQube, CodeQL, and dependency submission.
- Windows validation evidence: `docs/unified-preview-0.2.0-windows-validation.md` (Windows packaged smoke is GO for the playback-after-seek blocker; rebuild final artifacts from the merged release head).
- Windows ZIP launches `FramePlayer.Avalonia.exe` and plays H.264/AAC with audible audio.
- Windows ZIP was rebuilt from the release target with pinned FFmpeg runtime archives restored from the checked-in manifests and validated by SHA256 before packaging.
- macOS ZIP is Developer ID signed, notarized, stapled, extracted, and accepted by Gatekeeper.
- SHA256 files are generated for both artifacts.
- Release notes and wiki links use `0.2.0` for both platforms.

## Superseded Previews

The split macOS Preview `0.1.1` and Windows Avalonia Preview `0.1.0` releases are superseded by Unified Avalonia Preview `0.2.0`. They remain available only as historical validation and provenance records.
