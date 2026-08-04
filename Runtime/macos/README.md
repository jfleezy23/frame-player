# macOS Runtime Staging

The macOS target of the universal Avalonia application uses pinned FFmpeg runtime files staged locally under one of:

- `Runtime/macos/osx-arm64/ffmpeg`
- `Runtime/macos/osx-x64/ffmpeg`

Tracked files in this area are limited to the immutable archive manifest, provenance, and per-library hash records. The actual `.dylib` files are intentionally ignored by git and restored from the pinned archive before hosted packaging or release-candidate validation.
Packaging also stages the first-party Rust FFmpeg native library under the matching `Runtime/rust/<rid>/` folder; that generated `.dylib` is ignored and copied beside `FramePlayer.Avalonia` in the application bundle. It contains the runtime probe, exact decoded-frame global index builder, indexed decode-window helper, and BGRA frame converter.

Build and stage the pinned Apple Silicon runtime with:

```bash
scripts/ffmpeg/Build-FFmpeg-macOS-8.1.sh
```

Current local provenance shows a GPL/x264-enabled FFmpeg 8.1.2 build from the official `n8.1.2` tag at commit `38b88335f99e76ed89ff3c93f877fdefce736c13`. Do not publish a macOS artifact from a different runtime until its hashes, provenance, and third-party notice implications are updated.

The package and corpus validator select the native host runtime by default, or accept `MAC_RUNTIME_IDENTIFIER=osx-arm64` / `osx-x64`. An Intel candidate requires `SHA256SUMS.txt`, `build-provenance.txt`, its Rust probe, and successful corpus validation. A signed or public Intel artifact additionally requires a pinned `Runtime/macos/osx-x64/ffmpeg-runtime-manifest.json`; until that published-runtime manifest exists, `osx-x64` is local-validation-only.
