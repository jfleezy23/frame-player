# macOS Runtime Staging

The macOS preview uses pinned FFmpeg runtime files staged locally under:

- `Runtime/macos/osx-arm64/ffmpeg`

Tracked files in this area are limited to provenance and hash records. The actual `.dylib` files are intentionally ignored by git and must be staged locally before packaging or release-candidate validation.
The unified Avalonia preview also stages its first-party Rust FFmpeg native library under `Runtime/rust/osx-arm64/` during packaging; that generated `.dylib` is ignored and copied beside the app executable. It contains the runtime probe, exact decoded-frame global index builder, indexed decode-window helper, and BGRA frame converter.

Build and stage the pinned Apple Silicon runtime with:

```bash
scripts/ffmpeg/Build-FFmpeg-macOS-8.1.sh
```

Current local provenance shows a GPL/x264-enabled FFmpeg 8.1.2 build from the official `n8.1.2` tag at commit `38b88335f99e76ed89ff3c93f877fdefce736c13`. Do not publish a macOS artifact from a different runtime until its hashes, provenance, and third-party notice implications are updated.

The published `macos-preview-0.1.0` package is Apple Silicon focused because only an `osx-arm64` FFmpeg runtime has been staged. A universal or Intel-compatible release requires a pinned `osx-x64` macOS runtime with matching provenance and validation.
