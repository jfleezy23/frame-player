# Frame Player v2.1.0

Frame Player v2.1.0 is a security, runtime, and reliability update for the universal Avalonia application on Windows x64 and Apple Silicon macOS.

## What Changed

- Updated the bundled native FFmpeg runtime to 8.1.2 and aligned the managed FFmpeg bindings with the 8.1 API.
- Consolidated Windows and macOS packaging, validation, and documentation around the single supported `FramePlayer.Avalonia` application.
- Stabilized bundled FFmpeg discovery and startup so invalid or incomplete runtime paths fail cleanly without corrupting previously initialized state.
- Improved decoded-frame cache accounting and bounded native indexing, decode, conversion, and media-probe resource use.
- Hardened native media-buffer ownership, cleanup, cancellation, and platform interop.
- Hardened Windows audio-buffer teardown and retry behavior.
- Made the visible loop status an accessible toggle and kept native-backed frames visible when zoom changes.
- Corrected macOS About-version metadata so it matches the packaged release label.
- Enabled audio insertion from the primary pane during compare review and added MPEG-4 `.m4v` and HEVC MP4 input support.

## Security

- Fixed the invalid native converter-pointer path reported against the Rust FFmpeg wrapper.
- Removed user-selected filenames from FFmpeg filtergraph syntax; native exports now bind filenames through typed FFmpeg options.
- Added validation around native pointers, buffer layouts, allocation sizes, managed/native ABI compatibility, and partial-failure cleanup.
- Updated pinned GitHub security actions while retaining immutable full-commit pins.

## Planned Release Packages

- `FramePlayer-Windows-x64-2.1.0.zip`
- `FramePlayer-macOS-arm64-2.1.0.zip`
- A SHA256 file accompanies each archive.

The packages are self-contained and include the pinned playback and export runtimes. They do not require a separate FFmpeg installation and do not ship developer FFmpeg command-line tools.

Before publication, the macOS application must be Developer ID signed, notarized, stapled, and verified with Gatekeeper. The Windows application binaries must be Authenticode signed.

The macOS package supports Apple Silicon on macOS 13 or later. The Windows package supports Windows x64.

## Upgrade Notes

After publication, replace the previous application with the v2.1.0 package for your platform. Frame Player remains one cross-platform Avalonia product with the same review, compare, loop, export, audio-insertion, recent-file, and diagnostics workflows.
