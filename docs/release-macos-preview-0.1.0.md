# Frame Player macOS Preview 0.1.0 Release Note

This note documents the controlled Apple Silicon macOS Preview release. Windows remains the WPF `v1.8.4` release line and is not changed by this preview.

- Release: [Frame Player macOS Preview 0.1.0](https://github.com/jfleezy23/frame-player/releases/tag/macos-preview-0.1.0)
- Artifact: `FramePlayer-macOS-arm64-macos-preview-0.1.0.zip`
- SHA256: `81d3cfc15030e1040f658b9fd0e85755c35b461f4f3515f28f5a130a0bf87728`
- Apple notarization submission: `9f727885-b388-4ac0-a988-e10378933a32`

## What Changed

- Added `src/FramePlayer.Mac` as a macOS Avalonia app that mirrors the Windows review UI, transport layer, timeline behavior, loop controls, compare workflow, menu commands, shortcuts, dialogs, and status surfaces while using native macOS window/menu chrome.
- Added Mac-owned shared source projects under `src/FramePlayer.Core` and `src/FramePlayer.Engine.FFmpeg` so the preview can compile without changing the Windows WPF path.
- Added Mac release-candidate tests for corpus open/seek/step coverage, audio playback submission, loop playback containment, focused right-pane Open Recent behavior, clip export, side-by-side compare export, diagnostics export, and audio insertion.
- Added Mac packaging scripts for building the local `.app`, validating against the real corpus, signing a preview ZIP, and preserving the detached .NET signatures required for Apple notarization and Gatekeeper validation.

## Release Track

- Windows stable remains `v1.8.4`.
- macOS preview tag: `macos-preview-0.1.0`, targeting commit `40274fe0da8a36275dd90307c89d90d8d17078a8`.
- Post-release packaging documentation fix: PR #55, merged as `f3f5d671e75d61daa92bb63c84ee892f4cfb2667`.
- This is not a declaration that Avalonia replaces the Windows WPF app.
- The preview intentionally keeps temporary Mac-owned duplication where needed to avoid changing Windows source/build/release behavior.

## Validation Evidence

Validated locally and in GitHub on 2026-05-02:

- Build:
  - command: `dotnet build src/FramePlayer.Mac/FramePlayer.Mac.csproj -c Release`
  - result: passed, 0 warnings, 0 errors
- Ordinary Mac tests:
  - command: `dotnet test tests/FramePlayer.Mac.Tests/FramePlayer.Mac.Tests.csproj -c Release --filter "Category!=ReleaseCandidate"`
  - result: passed, 46 tests
- Release-candidate corpus validation:
  - command: `FRAMEPLAYER_MAC_CORPUS="/Users/jflow231/Developer/frame-player/Video Test Files" script/validate_macos_release_candidate.sh --corpus "/Users/jflow231/Developer/frame-player/Video Test Files"`
  - result: passed, 51 tests
  - corpus files enumerated: 10 supported media files
  - results: `artifacts/macos-release-candidate/results`
- GitHub checks on PR #54:
  - Windows CI: passed
  - macOS Avalonia CI: passed
  - Dependency Review: passed
  - CodeQL: passed
  - SonarCloud: passed, 0 new issues and 0 security hotspots
  - `@codex review`: no major issues on the final requested review pass
- GitHub checks on PR #55:
  - build, analyze, CodeQL, Dependency Review, and SonarCloud: passed
- Notarized release package:
  - signing identity: `Developer ID Application: JONATHAN MARQUETTE FLOYD (37H6P7CPFP)`
  - packaging path: sign app contents, sign the `.app` bundle with hardened runtime, preserve detached .NET signatures with `/usr/bin/ditto -c -k --keepParent`, submit with `xcrun notarytool`, staple, re-zip, then verify the extracted final ZIP with Gatekeeper
  - result: passed
  - artifact: `artifacts/macos-release-candidate/FramePlayer-macOS-arm64-macos-preview-0.1.0.zip`
  - SHA256: `81d3cfc15030e1040f658b9fd0e85755c35b461f4f3515f28f5a130a0bf87728`
  - script reproducibility check: `PACKAGE_VERSION=macos-preview-0.1.0-script-check script/package_macos_release.sh --sign "Developer ID Application: JONATHAN MARQUETTE FLOYD (37H6P7CPFP)"` passed after the packaging fix
- Signing validation:
  - command: `codesign --verify --deep --verbose=2 "/Applications/Frame Player.app"`
  - result: passed
  - identity: `Developer ID Application: JONATHAN MARQUETTE FLOYD (37H6P7CPFP)`
- Apple notarization and Gatekeeper:
  - notarization submission: `9f727885-b388-4ac0-a988-e10378933a32`
  - `xcrun stapler validate "/Applications/Frame Player.app"`: passed
  - `spctl -a -vvv -t exec "/Applications/Frame Player.app"`: accepted, `Notarized Developer ID`
- Manual install smoke:
  - path: `/Applications/Frame Player.app`
  - result: installed from the published preview, launched, and confirmed by the maintainer

## Current Limitations

- The published preview artifact is `osx-arm64` for Apple Silicon Macs only.
- No pinned `osx-x64` macOS runtime was found in the repo workspace, so Intel and universal macOS artifacts are out of scope for `0.1.0`.
- macOS FFmpeg provenance shows `--enable-gpl --enable-libx264`; redistribution of this preview must account for GPL/x264 obligations.

## Release Guidance

- Announce this as a controlled macOS Preview for Apple Silicon.
- Keep the GitHub release marked as a prerelease.
- Attach only the notarized/stapled `osx-arm64` macOS ZIP and SHA256 file.
- Do not attach or alter Windows artifacts for this preview.
- Treat the next release step as tester feedback and Apple Silicon preview hardening unless a separate Intel/universal runtime effort is approved.
