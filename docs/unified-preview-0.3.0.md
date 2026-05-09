# Unified Preview 0.3.0

This note prepares the next synchronized Windows/macOS Avalonia preview. Unified Preview `0.3.0` is the first combined preview planned to ship the Rust FFmpeg native pipeline in both platform packages.

- Planned release: [Frame Player Unified Preview 0.3.0](https://github.com/jfleezy23/frame-player/releases/tag/unified-preview-0.3.0)
- Tag: `unified-preview-0.3.0`
- Rust pipeline merge target: `3902a121f634e80dd7058ca547f1eb36736e9950`
- Final release target: pending release-prep merge
- Windows artifact: `FramePlayer-Windows-x64-unified-preview-0.3.0.zip`
- Windows SHA256: pending final Windows package
- macOS artifact: `FramePlayer-macOS-arm64-unified-preview-0.3.0.zip`
- macOS SHA256: pending final signed/notarized macOS package
- Apple notarization submission: pending final signed package

## What Changed

- Added the first-party Rust FFmpeg native library to the unified Windows and macOS packaging paths.
- Added a Rust runtime probe that verifies the bundled FFmpeg libraries can be loaded from the packaged runtime.
- Ported the exact decoded-frame global index builder into Rust, preserving display-order frame identity for seek and frame-entry workflows.
- Added a Rust indexed decode-window helper for seek materialization, backward reconstruction, forward cache priming, and playback decode windows.
- Added a Rust BGRA frame converter that returns Rust-owned native BGRA buffers for the Avalonia presenter copy path.
- Fixed the stateful shared-transport bug where two-pane playback could fail after play plus seek/scrub/move interactions.
- Preserved decoder realignment after Rust indexed seek so playback can resume after materializing a visible frame.

## Rust Pipeline Posture

The packages ship:

- macOS: `libframeplayer_ffmpeg_probe.dylib`
- Windows: `frameplayer_ffmpeg_probe.dll`

The runtime modes are controlled by:

- `FRAMEPLAYER_FFMPEG_INDEX_BUILDER=managed|rust|auto`
- `FRAMEPLAYER_FFMPEG_DECODE_CORE=managed|rust|auto`
- `FRAMEPLAYER_FFMPEG_FRAME_CONVERTER=managed|rust|auto`

Release-candidate validation must force all three to `rust` so fallback cannot hide native-path failures. Normal package behavior can remain `auto` while the Rust path is hardened against the local corpus and user testing.

## Branch Discipline

- Rust implementation and transport fix PR: [#69](https://github.com/jfleezy23/frame-player/pull/69), merged into `main`.
- PR #69 received `@codex review`; no major issues were reported.
- PR #69 passed Windows CI, macOS Avalonia, SonarQube/SonarCloud, CodeQL, dependency review, and dependency submission.

## Local Validation

```bash
cargo fmt --check --manifest-path native/frameplayer_ffmpeg_probe/Cargo.toml
cargo check --manifest-path native/frameplayer_ffmpeg_probe/Cargo.toml
dotnet test tests/FramePlayer.Core.Tests/FramePlayer.Core.Tests.csproj -c Release
dotnet test tests/FramePlayer.Avalonia.Tests/FramePlayer.Avalonia.Tests.csproj -c Release
```

For forced Rust playback regression coverage on macOS after staging `Runtime/rust/osx-arm64/libframeplayer_ffmpeg_probe.dylib`:

```bash
DYLD_LIBRARY_PATH="$PWD/Runtime/rust/osx-arm64" \
FRAMEPLAYER_ENABLE_RUST_PLAYBACK_FLOW_TESTS=1 \
FRAMEPLAYER_FFMPEG_INDEX_BUILDER=rust \
FRAMEPLAYER_FFMPEG_DECODE_CORE=rust \
FRAMEPLAYER_FFMPEG_FRAME_CONVERTER=rust \
dotnet test tests/FramePlayer.Avalonia.Tests/FramePlayer.Avalonia.Tests.csproj -c Release --filter "FullyQualifiedName~ForcedRustPlayback_RemainsMonotonicAfterIndexedSeek|FullyQualifiedName~ForcedRustPlayback_RemainsResumableAfterCompareModeRoundTrip"
```

For macOS package smoke validation:

```bash
export PATH="$HOME/.cargo/bin:$HOME/.dotnet:$PATH"
PACKAGE_VERSION=unified-preview-0.3.0 script/package_unified_macos_release.sh --unsigned
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
- Two-pane playback starts both panes from the master transport after play plus seek/scrub interactions.

## Release Gate

- Final release-prep PR checks pass on the branch that carries the `0.3.0` release metadata.
- Windows ZIP is rebuilt from the final release target and validated by SHA256.
- macOS ZIP is rebuilt from the final release target, Developer ID signed, notarized, stapled, extracted, and accepted by Gatekeeper.
- SHA256 sidecars are generated for both artifacts.
- Release notes, README, wiki links, package scripts, and CI package smoke checks all use `0.3.0`.

## Superseded Previews

Unified Preview `0.2.0` remains the first synchronized Windows/macOS preview and is retained as historical validation evidence. The split macOS Preview `0.1.1` and Windows Avalonia Preview `0.1.0` releases remain superseded by the unified preview line.
