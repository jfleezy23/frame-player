# Unified Preview 0.3.2

This note records the synchronized Windows/macOS Avalonia preview patch that resolves sync-over-async blocking, improves diagnostic log thread-safety, and unifies runtime manifest validation logic across the suite.

- Published release: [Frame Player Unified Preview 0.3.2](https://github.com/jfleezy23/frame-player/releases/tag/unified-preview-0.3.2)
- Tag: `unified-preview-0.3.2`
- Final release target: `66811c0249d1d4ea86df357a311808dce9ede290`
- macOS artifact: `FramePlayer-macOS-arm64-unified-preview-0.3.2.zip`
- macOS SHA256: `837622cc50f3ad9ebd054698fc2c9913193848e4768da5690879c52f8fa55b31`
- Apple notarization submission: `8cae3d39-4b18-49c6-b89c-5e831fc7f405`
- Windows artifact: (Pending Windows run)
- Windows SHA256: (Pending Windows run)

## What Changed

- **Eliminated Sync-over-Async in FfmpegCliTooling**: Switched from `Task.WaitAll` to a dedicated background reader thread for draining `stderr`, preventing pipe deadlocks while avoiding dispatcher/threadpool blocking.
- **Diagnostic Logging Thread-Safety**: Added robust monitor locking around log entries and DPAPI serialization in `DiagnosticLogService` to protect against concurrent writes during aggressive parallel probing.
- **Unified Manifest Validation**: Centralized duplicate SHA256 hashing, safe leaf filename checks, and JSON data contract serialization from `RuntimeManifestService`, `ExportRuntimeManifestService`, and `ExportToolsManifestService` into a single testable `ManifestValidationHelper`.
- **Test Hardening**: Added 16 new robust unit tests in `RuntimeAndToolingHardeningTests` reaching 100% path coverage for the new validation helper.

## Branch Discipline

- Core async, logging, and manifest stability PR: [#77](https://github.com/jfleezy23/frame-player/pull/77), merged into `main` before `0.3.2`.
- PR #77 passed Windows CI, macOS Avalonia, SonarQube/SonarCloud, CodeQL, dependency review, and dependency submission.

## Local Validation

```bash
git diff --check
dotnet build src/FramePlayer.Avalonia/FramePlayer.Avalonia.csproj -c Release
dotnet test tests/FramePlayer.Avalonia.Tests/FramePlayer.Avalonia.Tests.csproj -c Release
dotnet build src/FramePlayer.Mac/FramePlayer.Mac.csproj -c Release
dotnet test tests/FramePlayer.Mac.Tests/FramePlayer.Mac.Tests.csproj -c Release --filter "Category!=ReleaseCandidate"
FRAMEPLAYER_MAC_CORPUS="Video Test Files" script/validate_macos_release_candidate.sh --corpus "Video Test Files"
```

For macOS package smoke validation:

```bash
export PATH="$HOME/.cargo/bin:$HOME/.dotnet:$PATH"
PACKAGE_VERSION=unified-preview-0.3.2 script/package_unified_macos_release.sh --unsigned
```

For Windows package validation, run on Windows after restoring the pinned runtimes:

```powershell
.\scripts\Ensure-DevRuntime.ps1
.\scripts\Ensure-DevExportRuntime.ps1 -Required
.\scripts\Package-UnifiedWindowsPreview.ps1
```

## Release Gate

- GitHub checks on the merged fix target passed before this release-prep pass.
- Local macOS review after PR #77 passed the unified build, full unified test suite, forced-Rust playback tests, split Mac build/tests, and the broader Mac corpus release-candidate validator.
- macOS Notarization and Gatekeeper validation succeeded.
- Final release publication completed against target `66811c0249d1d4ea86df357a311808dce9ede290`.

## Superseded Previews

Unified Preview `0.3.1` is superseded by this `0.3.2` release containing the async blocking and concurrency fixes.
