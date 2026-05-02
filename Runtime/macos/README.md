# macOS Runtime Staging

The macOS preview uses pinned FFmpeg runtime files staged locally under:

- `Runtime/macos/osx-arm64/ffmpeg`

Tracked files in this area are limited to provenance and hash records. The actual `.dylib` files are intentionally ignored by git and must be staged locally before packaging or release-candidate validation.

Current local provenance shows a GPL/x264-enabled FFmpeg build. Do not publish a macOS artifact from a different runtime until its hashes, provenance, and third-party notice implications are updated.

The published `macos-preview-0.1.0` package is Apple Silicon focused because only an `osx-arm64` FFmpeg runtime has been staged. A universal or Intel-compatible release requires a pinned `osx-x64` macOS runtime with matching provenance and validation.
