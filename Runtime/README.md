# Runtime Files

The FFmpeg runtime DLLs used by Frame Player are intentionally not stored in git.

For local development:

1. Run `.\scripts\ffmpeg\Build-FFmpeg-8.1.ps1`
2. Then run `.\scripts\Ensure-DevRuntime.ps1`
3. If you want the export host available locally, run `.\scripts\ffmpeg\Build-FFmpeg-Tools-8.1.ps1`
4. Then run `.\scripts\Ensure-DevExportTools.ps1`
5. Then run `.\scripts\Ensure-DevExportRuntime.ps1`
6. If you want to stage the lean DLL-only candidate locally, run `.\scripts\ffmpeg\Build-FFmpeg-ExportRuntime-8.1.ps1`
7. Or run `.\scripts\Build-FramePlayer.ps1`, which restores the playback runtime, restores local export tools when needed for dev/test flows, restores the export runtime, and then builds the app

The runtime is pinned through `Runtime\runtime-manifest.json`.
The Windows source-build entrypoints now target FFmpeg 8.1.2. The active Windows restore manifests still describe the prior FFmpeg 8.1 archives until replacement Windows artifacts and hashes are generated.
`scripts\Ensure-DevRuntime.ps1` restores `Runtime\ffmpeg\` from the local candidate folder or local runtime archive before attempting any download fallback.
Packaged builds must include `libwinpthread-1.dll` beside the FFmpeg DLLs and `FramePlayer.exe`.
The separate export runtime is pinned through `Runtime\export-runtime-manifest.json` and restored to `Runtime\ffmpeg-export\`.
The local/dev-only CLI tool bundle remains pinned through `Runtime\export-tools-manifest.json` and restored to `Runtime\ffmpeg-tools\`, but it is not expected in shipped app output.
The unified Avalonia preview also builds a first-party Rust FFmpeg native library into `Runtime\rust\<rid>\` before packaging. That generated native library is ignored by git and is not part of the FFmpeg restore manifests. It contains the runtime probe, exact decoded-frame global index builder, indexed decode-window helper, and BGRA frame converter. Use `FRAMEPLAYER_FFMPEG_INDEX_BUILDER=managed|rust|auto`, `FRAMEPLAYER_FFMPEG_DECODE_CORE=managed|rust|auto`, and `FRAMEPLAYER_FFMPEG_FRAME_CONVERTER=managed|rust|auto` to force or bypass the Rust paths during validation.

Current pinned Windows FFmpeg runtime version: `n8.1-frameplayer-source`.
Current pinned Windows FFmpeg export runtime version: `n8.1-frameplayer-export-runtime`.
Current pinned Windows FFmpeg export-tools version: `n8.1-frameplayer-export-tools`.
Current staged macOS FFmpeg runtime version: `n8.1.2-frameplayer-macos-source`.

The Windows 8.1.2 source-build target is the official `n8.1.2` tag at commit `38b88335f99e76ed89ff3c93f877fdefce736c13`. The macOS runtime tracked by `Runtime\macos\osx-arm64\ffmpeg\build-provenance.txt` is already built from that source.
The local source-build workflow and produced archive path are documented in `docs\ffmpeg-8.1-build-notes.md`.
This runtime also requires `libwinpthread-1.dll`, which is staged beside the FFmpeg DLLs and validated through the manifest.
The export runtime is staged separately so export/probe work can run in a secondary headless host without touching the primary playback DLL set.
The current restore path can seed `ffmpeg-export` from the local export-tools bundle for dev/test convenience, but shipped output is expected to carry only the DLL-based export runtime.
The dedicated `Build-FFmpeg-ExportRuntime-8.1.ps1` script stages a lean local candidate under `Runtime\ffmpeg-export-8.1.2-candidate\` when you want to validate the smaller no-program runtime directly.
Use `scripts\Build-RustFfmpegProbe.ps1` on Windows or `scripts/Build-RustFfmpegProbe.sh` on macOS to stage the Rust FFmpeg native library for unified-preview packaging.
