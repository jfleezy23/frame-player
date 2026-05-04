# Validation

## Unified Avalonia Preview 0.2.0

Unified Avalonia Preview `0.2.0` was validated as the first synchronized Windows/macOS preview on 2026-05-04.

Recorded validation evidence:

- Release target: `585a277f4c6c939562d1fdd10de2c31370b4ebb6`.
- GitHub checks on the release target passed: Windows CI, macOS Avalonia, SonarQube, CodeQL, and dependency submission.
- PR #65 and PR #66 received `@codex review`; no major issues were reported after the final changes.
- Windows packaged smoke was GO, including H.264/AAC playback, seek-after-playback, and nonzero endpoint audio evidence.
- Windows ZIP was rebuilt from the release target with pinned FFmpeg runtime archives restored from the checked-in manifests and validated by SHA256 before packaging.
- macOS ZIP was Developer ID signed, notarized, stapled, extracted, and accepted by Gatekeeper.

Published Unified Preview details:

- Release: [Frame Player Unified Preview 0.2.0](https://github.com/jfleezy23/frame-player/releases/tag/unified-preview-0.2.0)
- Windows artifact: `FramePlayer-Windows-x64-unified-preview-0.2.0.zip`
- Windows SHA256: `f417f3535627da5ea857cc8e9aec23bcebb83ac56a40bc26edeec1fbc5fdce79`
- macOS artifact: `FramePlayer-macOS-arm64-unified-preview-0.2.0.zip`
- macOS SHA256: `9dd253ffd1e18bceb7432230100bfbc5d7cd7cb4975c5458f1357317d72afb87`
- Apple notarization submission: `cd7a2d35-df28-4170-bd31-3ca49e4acebd`

## Superseded Split Previews

The split macOS Preview `0.1.1` and Windows Avalonia Preview `0.1.0` releases are superseded by Unified Avalonia Preview `0.2.0`. They remain available only as historical validation and provenance records.

## Windows Stable

Windows stable remains `v1.8.4`. Its WPF source path, build path, tests, runtime bootstrap, and release process are intentionally separate from Unified Avalonia Preview.

## Screenshot Validation

The checked-in screenshots in `docs/assets/screenshots/` are real app screenshots. The macOS captures were refreshed from the installed app during this documentation pass. The Windows captures were refreshed from the built WPF `v1.8.4` app surface at 1280x820. No loaded-video Windows screenshots were available locally for this pass.
