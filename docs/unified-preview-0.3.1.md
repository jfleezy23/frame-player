# Unified Preview 0.3.1

This note records the synchronized Windows/macOS Avalonia preview patch after the forced-Rust Windows corpus stability pass. Unified Preview `0.3.1` keeps the Rust FFmpeg native pipeline from `0.3.0` and adds the frame-presentation and test-harness fixes validated in PR #74.

- Published release: [Frame Player Unified Preview 0.3.1](https://github.com/jfleezy23/frame-player/releases/tag/unified-preview-0.3.1)
- Tag: `unified-preview-0.3.1`
- Windows stability merge target: `05550fe3817a8186b45aac3ceb82bcf94110e1f0`
- Final release target: `5e96c8cc1ae58b5f8edf28b30e4776ea99b6fa05`
- Windows artifact: `FramePlayer-Windows-x64-unified-preview-0.3.1.zip`
- Windows SHA256: `439b6ac5f90d5c5e84ce4a4c4374b670e06e9447643e292551bba319e5fb652f`
- macOS artifact: `FramePlayer-macOS-arm64-unified-preview-0.3.1.zip`
- macOS SHA256: `e3776ea9a7f2643d7c77e18b3d7207d0cd721f6af439a2998ecf9d382fda1f29`
- Apple notarization submission: `cdf36d19-1c75-4d34-8144-b6522cef0d87`

## What Changed

- Coalesced pending Avalonia frame presentation per pane so fast Rust decode/playback paths display the newest retained frame without backlogging stale UI dispatcher work.
- Preserved existing frame-presentation error reporting while disposing superseded pending frame buffers promptly.
- Added native pixel-buffer lifetime parity to the legacy root `Core/Models/DecodedFrameBuffer` used by the Windows root test path.
- Added a forced-Rust Windows corpus harness for probe, index, decode, and playback-flow validation across local media.
- Hardened headless Avalonia tests with named dispatch timeouts and direct cleanup of forced-Rust compare-mode playback paths.
- Verified the merged Windows-side changes did not regress the current unified macOS package path.

## Rust Pipeline Posture

The packages ship:

- macOS: `libframeplayer_ffmpeg_probe.dylib`
- Windows: `frameplayer_ffmpeg_probe.dll`

The runtime modes are controlled by:

- `FRAMEPLAYER_FFMPEG_INDEX_BUILDER=managed|rust|auto`
- `FRAMEPLAYER_FFMPEG_DECODE_CORE=managed|rust|auto`
- `FRAMEPLAYER_FFMPEG_FRAME_CONVERTER=managed|rust|auto`

Release-candidate validation should force all three to `rust` when testing native-path playback so fallback cannot hide a Rust decode or presentation failure. Normal package behavior can remain `auto`.

## Branch Discipline

- Rust pipeline release PR: [#69](https://github.com/jfleezy23/frame-player/pull/69), merged into `main` before `0.3.0`.
- Windows forced-Rust corpus stability PR: [#74](https://github.com/jfleezy23/frame-player/pull/74), merged into `main` as `05550fe3817a8186b45aac3ceb82bcf94110e1f0`.
- PR #74 passed Windows CI, macOS Avalonia, SonarQube/SonarCloud, CodeQL, dependency review, and dependency submission.

## Local Validation

```bash
git diff --check
dotnet build src/FramePlayer.Avalonia/FramePlayer.Avalonia.csproj -c Release
dotnet test tests/FramePlayer.Avalonia.Tests/FramePlayer.Avalonia.Tests.csproj -c Release
dotnet build src/FramePlayer.Mac/FramePlayer.Mac.csproj -c Release
dotnet test tests/FramePlayer.Mac.Tests/FramePlayer.Mac.Tests.csproj -c Release --filter "Category!=ReleaseCandidate"
FRAMEPLAYER_MAC_CORPUS="Video Test Files" script/validate_macos_release_candidate.sh --corpus "Video Test Files"
```

For forced Rust playback regression coverage on macOS after staging `Runtime/rust/osx-arm64/libframeplayer_ffmpeg_probe.dylib`:

```bash
DYLD_LIBRARY_PATH="$PWD/Runtime/rust/osx-arm64:$PWD/Runtime/macos/osx-arm64/ffmpeg" \
FRAMEPLAYER_ENABLE_RUST_PLAYBACK_FLOW_TESTS=1 \
dotnet test tests/FramePlayer.Avalonia.Tests/FramePlayer.Avalonia.Tests.csproj -c Release --filter "FullyQualifiedName~ForcedRustPlayback_RemainsMonotonicAfterIndexedSeek|FullyQualifiedName~ForcedRustPlayback_RemainsResumableAfterCompareModeRoundTrip"
```

For macOS package smoke validation:

```bash
export PATH="$HOME/.cargo/bin:$HOME/.dotnet:$PATH"
PACKAGE_VERSION=unified-preview-0.3.1 script/package_unified_macos_release.sh --unsigned
```

For Windows package validation, run on Windows after restoring the pinned runtimes:

```powershell
.\scripts\Ensure-DevRuntime.ps1
.\scripts\Ensure-DevExportRuntime.ps1 -Required
.\scripts\Package-UnifiedWindowsPreview.ps1
```

Windows package smoke must confirm:

- `FramePlayer.Avalonia.exe` launches from a clean extraction.
- `frameplayer_ffmpeg_probe.dll` is beside the executable.
- H.264/AAC playback starts, remains resumable after seek, and produces nonzero audio output.
- Two-pane playback remains responsive under forced Rust playback and compare-mode round trips.

## Release Gate

- GitHub checks on the merged Windows fix target passed before this release-prep pass.
- Local macOS review after PR #74 passed the unified build, full unified test suite, forced-Rust playback tests, split Mac build/tests, and the broader Mac corpus release-candidate validator.
- Final release publication completed against target `5e96c8cc1ae58b5f8edf28b30e4776ea99b6fa05`.

## Superseded Previews

Unified Preview `0.3.0` remains the first Rust-enabled synchronized Windows/macOS preview and is retained as historical validation evidence. Unified Preview `0.2.0` remains the first synchronized preview. The split macOS Preview `0.1.1` and Windows Avalonia Preview `0.1.0` releases remain superseded by the unified preview line.
