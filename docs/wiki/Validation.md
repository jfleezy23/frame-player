# Validation

Frame Player is validated as one Avalonia application on Windows and macOS.

## Required checks

- Build `src/FramePlayer.Avalonia/FramePlayer.Avalonia.csproj` in Release configuration.
- Run `tests/FramePlayer.Core.Tests` and `tests/FramePlayer.Avalonia.Tests`.
- Validate repository harness syntax and pinned GitHub Action revisions.
- Build and inspect the self-contained package for each target operating system.
- Run the maintained media corpus through playback, seeking, frame stepping, loop, compare, clip export, side-by-side export, and audio-insertion paths.
- Force the Rust index, decode, and conversion modes during native parity checks so managed fallback cannot hide a native failure.
- Review dependency, static-analysis, and security-diff results before merging release changes.

## Package invariants

- The executable is `FramePlayer.Avalonia.exe` on Windows and `FramePlayer.Avalonia` inside the macOS bundle.
- The first-party Rust FFmpeg probe library and pinned playback/export runtimes ship beside the application.
- Developer-only `ffmpeg.exe`, `ffprobe.exe`, and `ffmpeg-tools` content must not ship.
- Public artifacts must have verified checksums and platform signatures; macOS artifacts also require notarization and stapling.

## Published release evidence

Release-specific commit IDs, package hashes, signing evidence, and notarization submission IDs belong in the GitHub release record. Validation claims should always identify the exact source commit and artifact hash they cover.
