# Validation

## Unified Avalonia Preview 0.3.1

Unified Avalonia Preview `0.3.1` is the current combined Windows/macOS preview release.
It is a patch release for the Rust-enabled unified preview line after the forced-Rust Windows corpus stability pass.

Recorded validation evidence:

- Windows forced-Rust corpus stability PR: [#74](https://github.com/jfleezy23/frame-player/pull/74), merged as `05550fe3817a8186b45aac3ceb82bcf94110e1f0`.
- GitHub checks on PR #74 and the merged `main` commit passed: Windows CI, macOS Avalonia, SonarQube/SonarCloud, CodeQL, dependency review, and dependency submission.
- Local macOS regression review after PR #74 passed the unified Avalonia build and tests, forced-Rust playback tests, the split Mac build/tests, package smoke validation, and the Mac corpus release-candidate validator.
- Release target: `5e96c8cc1ae58b5f8edf28b30e4776ea99b6fa05`.
- Windows ZIP: `FramePlayer-Windows-x64-unified-preview-0.3.1.zip`, including `frameplayer_ffmpeg_probe.dll` beside `FramePlayer.Avalonia.exe`.
- Windows SHA256: `439b6ac5f90d5c5e84ce4a4c4374b670e06e9447643e292551bba319e5fb652f`.
- macOS ZIP: `FramePlayer-macOS-arm64-unified-preview-0.3.1.zip`, including `libframeplayer_ffmpeg_probe.dylib` beside the app executable.
- macOS SHA256: `e3776ea9a7f2643d7c77e18b3d7207d0cd721f6af439a2998ecf9d382fda1f29`.
- Release: [Frame Player Unified Preview 0.3.1](https://github.com/jfleezy23/frame-player/releases/tag/unified-preview-0.3.1)
- Apple notarization submission: `cdf36d19-1c75-4d34-8144-b6522cef0d87`.

Additional Rust-path validation should force all three modes with `FRAMEPLAYER_FFMPEG_INDEX_BUILDER=rust`, `FRAMEPLAYER_FFMPEG_DECODE_CORE=rust`, and `FRAMEPLAYER_FFMPEG_FRAME_CONVERTER=rust` so fallback cannot hide native-path failures.

## Unified Avalonia Preview 0.3.0

Unified Avalonia Preview `0.3.0` was published on 2026-05-09 as the first Rust-enabled combined Windows/macOS preview release.
It is the first combined preview to ship the Rust FFmpeg native pipeline in both platform packages.

Recorded validation evidence:

- Rust pipeline merge PR: [#69](https://github.com/jfleezy23/frame-player/pull/69), merged as `3902a121f634e80dd7058ca547f1eb36736e9950`.
- `@codex review`: completed with no major issues reported.
- GitHub checks on PR #69 passed: Windows CI, macOS Avalonia, SonarQube/SonarCloud, CodeQL, dependency review, and dependency submission.
- Release-prep and package-cleanup PR checks passed before publication.
- Release target: `bdd9829e08fad1deed94b687405494a14e84e90c`.
- Windows ZIP was rebuilt with the Rust DLL and pinned FFmpeg runtime files, inventoried for required package contents, and validated by SHA256.
- macOS ZIP was Developer ID signed, notarized, stapled, extracted, and accepted by Gatekeeper.
- Uploaded GitHub release assets were downloaded and revalidated by SHA256; the downloaded macOS ZIP was extracted and accepted by Gatekeeper.

Published Unified Preview details:

- Windows ZIP: `FramePlayer-Windows-x64-unified-preview-0.3.0.zip`, including `frameplayer_ffmpeg_probe.dll` beside `FramePlayer.Avalonia.exe`.
- Windows SHA256: `9df3e95afee515832902c5f1e26ba752879080a42ad2d38c0d579b3e16c39b2b`
- macOS ZIP: `FramePlayer-macOS-arm64-unified-preview-0.3.0.zip`, including `libframeplayer_ffmpeg_probe.dylib` beside the app executable.
- Release: [Frame Player Unified Preview 0.3.0](https://github.com/jfleezy23/frame-player/releases/tag/unified-preview-0.3.0)
- macOS SHA256: `29ed24b269889987a9641952d0a5e0b8746a38b157db7b82c43d0042b319b97e`
- Apple notarization submission: `421c32c3-f0da-4436-9ef1-7ac2b4e22237`

Additional Rust-path validation should force all three modes with `FRAMEPLAYER_FFMPEG_INDEX_BUILDER=rust`, `FRAMEPLAYER_FFMPEG_DECODE_CORE=rust`, and `FRAMEPLAYER_FFMPEG_FRAME_CONVERTER=rust` so fallback cannot hide native-path failures.

## Unified Avalonia Preview 0.2.0

Unified Avalonia Preview `0.2.0` was validated as the first synchronized Windows/macOS preview on 2026-05-04.
The Rust FFmpeg integration was added after the original published artifact validation; rebuilt unified-preview artifacts must also include `frameplayer_ffmpeg_probe.dll` on Windows or `libframeplayer_ffmpeg_probe.dylib` on macOS.
The native library contains the runtime probe, exact decoded-frame global index builder, indexed decode-window helper, and BGRA frame converter. Use `FRAMEPLAYER_FFMPEG_INDEX_BUILDER=managed|rust|auto`, `FRAMEPLAYER_FFMPEG_DECODE_CORE=managed|rust|auto`, and `FRAMEPLAYER_FFMPEG_FRAME_CONVERTER=managed|rust|auto` to force parity checks; `auto` uses Rust when available and falls back to the managed path.

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

The split macOS Preview `0.1.1` and Windows Avalonia Preview `0.1.0` releases are superseded by Unified Avalonia Preview `0.3.1`. They remain available only as historical validation and provenance records.

## Windows Stable

Windows stable remains `v1.8.4`. Its WPF source path, build path, tests, runtime bootstrap, and release process are intentionally separate from Unified Avalonia Preview.

## Screenshot Validation

The checked-in screenshots in `docs/assets/screenshots/` are real app screenshots. The macOS captures were refreshed from the installed app during this documentation pass. The Windows captures were refreshed from the built WPF `v1.8.4` app surface at 1280x820. No loaded-video Windows screenshots were available locally for this pass.
