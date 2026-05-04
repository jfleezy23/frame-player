# Frame Player macOS Preview 0.1.1 Release Note

> Superseded: use [Frame Player Unified Preview 0.2.0](https://github.com/jfleezy23/frame-player/releases/tag/unified-preview-0.2.0) for current Windows/macOS Avalonia preview testing. This split macOS preview remains available only as historical validation evidence.

This note documents the controlled Apple Silicon macOS Preview refresh. Windows stable remains the WPF `v1.8.4` release line and is not changed by this preview.

- Release: [Frame Player macOS Preview 0.1.1](https://github.com/jfleezy23/frame-player/releases/tag/macos-preview-0.1.1)
- Artifact: `FramePlayer-macOS-arm64-macos-preview-0.1.1.zip`
- SHA256: `7c9ee402c7a7b2375a00567cd0bff1bf7fc8629deceb5d6837cc82d0b6ac0f62`
- Apple notarization submission: `4e9a64b3-4004-4d38-aaaa-73a0ce69c5eb`

## What Changed

- Aligned the macOS Avalonia preview shell with the Windows Avalonia preview layout while keeping native macOS window and menu chrome.
- Updated the macOS review body with the flat video viewport, centered empty state, fixed frame-entry rails, stable status strip, compare toolbar parity, and disabled command states before media load.
- Fixed macOS export-host runtime resolution so release-candidate export flows use the bundled macOS runtime base instead of falling back toward Windows export runtime manifest entries.
- Expanded macOS UI/runtime contract coverage, including status-strip overflow protection, focused-pane command state refresh, and release-candidate environment restoration.

## Release Track

- Windows stable remains `v1.8.4`.
- Windows Avalonia Preview remains `avalonia-windows-preview-0.1.0`.
- macOS preview tag: `macos-preview-0.1.1`.
- Implementation PR: #62, merged as `500198c0320bfdd6dfa12108aa136f977514232e`.
- This is not a declaration that Avalonia replaces the Windows WPF app.

## Validation Evidence

Validated locally and in GitHub on 2026-05-04:

- Build:
  - command: `dotnet build src/FramePlayer.Mac/FramePlayer.Mac.csproj -c Release`
  - result: passed, 0 warnings, 0 errors
- Ordinary Mac tests:
  - command: `dotnet test tests/FramePlayer.Mac.Tests/FramePlayer.Mac.Tests.csproj -c Release --filter "Category!=ReleaseCandidate"`
  - result: passed, 50 tests
- Release-candidate corpus validation:
  - command: `FRAMEPLAYER_MAC_CORPUS="/Users/jflow231/Developer/frame-player/Video Test Files" script/validate_macos_release_candidate.sh --corpus "/Users/jflow231/Developer/frame-player/Video Test Files"`
  - result: passed, 55 tests
  - corpus files enumerated: 10 supported media files
  - results: `artifacts/macos-release-candidate/results`
- GitHub checks on PR #62:
  - Windows CI: passed
  - macOS Avalonia CI: passed
  - Dependency Review: passed
  - CodeQL: passed
  - SonarCloud: passed, 0 new issues and 0 security hotspots
  - `@codex review`: requested; no findings were posted before merge. Local Codex review findings were fixed before PR creation.
- Notarized release package:
  - signing identity: `Developer ID Application: JONATHAN MARQUETTE FLOYD (37H6P7CPFP)`
  - packaging path: sign app contents, sign the `.app` bundle with hardened runtime, preserve detached .NET signatures with `/usr/bin/ditto -c -k --keepParent`, submit with `xcrun notarytool`, staple, re-zip, then verify the extracted final ZIP with Gatekeeper
  - result: passed
  - artifact: `artifacts/macos-release-candidate/FramePlayer-macOS-arm64-macos-preview-0.1.1.zip`
  - SHA256: `7c9ee402c7a7b2375a00567cd0bff1bf7fc8629deceb5d6837cc82d0b6ac0f62`
- Apple notarization and Gatekeeper:
  - notarization submission: `4e9a64b3-4004-4d38-aaaa-73a0ce69c5eb`
  - `xcrun stapler validate "dist/Frame Player.app"`: passed
  - `spctl -a -vvv -t exec "dist/Frame Player.app"`: accepted, `Notarized Developer ID`
  - extracted ZIP validation: `xcrun stapler validate` passed and `spctl -a -vvv -t exec` accepted the app extracted from the final ZIP

## Current Limitations

- The published preview artifact is `osx-arm64` for Apple Silicon Macs only.
- No pinned `osx-x64` macOS runtime is included, so Intel and universal macOS artifacts are out of scope for `0.1.1`.
- macOS FFmpeg provenance shows `--enable-gpl --enable-libx264`; redistribution of this preview must account for GPL/x264 obligations.

## Release Guidance

- Announce this as a controlled macOS Preview refresh for Apple Silicon.
- Keep the GitHub release marked as a prerelease.
- Attach only the notarized/stapled `osx-arm64` macOS ZIP and SHA256 file.
- Do not attach or alter Windows stable or Windows Avalonia preview artifacts for this release.
