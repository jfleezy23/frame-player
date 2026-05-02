# Frame Player macOS Preview 0.1.0 Release Note

This note documents the macOS-only Avalonia preview release. Windows remains the WPF `v1.8.4` release line and is not changed by this preview.

## What Changed

- Added `src/FramePlayer.Mac` as a macOS Avalonia app that mirrors the Windows review UI, transport layer, timeline behavior, loop controls, compare workflow, menu commands, shortcuts, dialogs, and status surfaces while using native macOS window/menu chrome.
- Added Mac-owned shared source projects under `src/FramePlayer.Core` and `src/FramePlayer.Engine.FFmpeg` so the preview can compile without changing the Windows WPF path.
- Added Mac release-candidate tests for corpus open/seek/step coverage, audio playback submission, loop playback containment, focused right-pane Open Recent behavior, clip export, side-by-side compare export, diagnostics export, and audio insertion.
- Added Mac packaging scripts for building the local `.app`, validating against the real corpus, signing a preview RC ZIP, and preparing the later Developer ID notarization flow.

## Release Track

- Windows stable remains `v1.8.4`.
- macOS preview tag target: `macos-preview-0.1.0`.
- This is not a declaration that Avalonia replaces the Windows WPF app.
- The preview intentionally keeps temporary Mac-owned duplication where needed to avoid changing Windows source/build/release behavior.

## Validation Evidence

Validated locally on 2026-05-01:

- Build:
  - command: `dotnet build src/FramePlayer.Mac/FramePlayer.Mac.csproj -c Release`
  - result: passed, 0 warnings, 0 errors
- Ordinary Mac tests:
  - command: `dotnet test tests/FramePlayer.Mac.Tests/FramePlayer.Mac.Tests.csproj -c Release --filter "Category!=ReleaseCandidate"`
  - result: passed, 33 tests
- Release-candidate corpus validation:
  - command: `FRAMEPLAYER_MAC_CORPUS="/Users/jflow231/Developer/frame-player/Video Test Files" script/validate_macos_release_candidate.sh --corpus "/Users/jflow231/Developer/frame-player/Video Test Files"`
  - result: passed, 38 tests
  - corpus files enumerated: 10 supported media files
  - results: `artifacts/macos-release-candidate/results`
- Signed local RC package:
  - command: `PACKAGE_VERSION=macos-preview-0.1.0-rc.local script/package_macos_release.sh --sign`
  - result: passed
  - artifact: `artifacts/macos-release-candidate/FramePlayer-macOS-arm64-macos-preview-0.1.0-rc.local.zip`
  - SHA256: `675da73491f0d5db2549ec2ca9ca2a52803142906b4fa4ea198a320a2789e1ab`
- Signing validation:
  - command: `codesign --verify --strict --deep --verbose=2 "dist/Frame Player.app"`
  - result: passed
  - identity: `Apple Development: JONATHAN MARQUETTE FLOYD (35J2UBS6TF)`
  - `spctl` result: rejected, expected for Apple Development signing before Developer ID notarization
- Local install smoke:
  - path: `/Applications/Frame Player.app`
  - result: installed, code signature verified, launch process observed

## Current Limitations

- The current staged local runtime is `osx-arm64`; no pinned `osx-x64` macOS runtime was found in the repo workspace.
- The signed RC ZIP is for controlled testing. Public distribution still requires Developer ID Application signing, notarization, stapling, and Gatekeeper validation.
- macOS FFmpeg provenance currently shows `--enable-gpl --enable-libx264`; redistribution must account for GPL/x264 obligations before public release.

## Release Guidance

- Merge through PR only after Windows CI, dependency review, SonarQube, macOS CI, code review, and local full-corpus RC validation are green.
- Publish as a GitHub prerelease titled `Frame Player macOS Preview 0.1.0`.
- Attach only the signed macOS ZIP and SHA256 file.
- Do not attach or alter Windows artifacts for this preview.
