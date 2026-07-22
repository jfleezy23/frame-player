# Runtime Files

The FFmpeg runtime DLLs used by Frame Player are intentionally not stored in git.

For local development:

1. Run `.\scripts\ffmpeg\Build-FFmpeg-8.1.ps1` when producing a local playback-runtime candidate.
2. Run `.\scripts\Ensure-DevRuntime.ps1` to stage the pinned playback runtime.
3. Run `.\scripts\ffmpeg\Build-FFmpeg-Tools-8.1.ps1` when producing the local CLI-tool candidate used by development and test flows.
4. Run `.\scripts\Ensure-DevExportTools.ps1` and `.\scripts\Ensure-DevExportRuntime.ps1 -Required` to stage the pinned export dependencies.
5. Run `.\scripts\ffmpeg\Build-FFmpeg-ExportRuntime-8.1.ps1` when producing the lean DLL-only export-runtime candidate directly.
6. Run `.\scripts\Build-RustFfmpegProbe.ps1` and then build `src\FramePlayer.Avalonia\FramePlayer.Avalonia.csproj`.

Use `.\scripts\Package-UnifiedWindows.ps1` to publish the Windows target of the universal application after its runtime inputs are staged.

The runtime is pinned through `Runtime\runtime-manifest.json`.
The Windows source-build entrypoints and active restore manifests target FFmpeg 8.1.2.
`scripts\Ensure-DevRuntime.ps1` restores `Runtime\ffmpeg\` from the local candidate folder or local runtime archive before attempting any download fallback.
Packaged Windows builds must include `libwinpthread-1.dll` beside the FFmpeg DLLs and `FramePlayer.Avalonia.exe`.
The separate export runtime is pinned through `Runtime\export-runtime-manifest.json` and restored to `Runtime\ffmpeg-export\`.
The local/dev-only CLI tool bundle remains pinned through `Runtime\export-tools-manifest.json` and restored to `Runtime\ffmpeg-tools\`, but it is not expected in shipped app output.
The universal Avalonia application also builds a first-party Rust FFmpeg native library into `Runtime\rust\<rid>\` before packaging. That generated native library is ignored by git and is not part of the FFmpeg restore manifests. It contains the runtime probe, exact decoded-frame global index builder, indexed decode-window helper, and BGRA frame converter. Use `FRAMEPLAYER_FFMPEG_INDEX_BUILDER=managed|rust|auto`, `FRAMEPLAYER_FFMPEG_DECODE_CORE=managed|rust|auto`, and `FRAMEPLAYER_FFMPEG_FRAME_CONVERTER=managed|rust|auto` to force or bypass the Rust paths during validation.

Current pinned Windows FFmpeg runtime version: `n8.1.2-frameplayer-source`.
Current pinned Windows FFmpeg export runtime version: `n8.1.2-frameplayer-export-runtime`.
Current pinned Windows FFmpeg export-tools version: `n8.1.2-frameplayer-export-tools`.
Current staged macOS FFmpeg runtime version: `n8.1.2-frameplayer-macos-source`.

All pinned FFmpeg 8.1.2 native runtimes use the official `n8.1.2` tag at commit `38b88335f99e76ed89ff3c93f877fdefce736c13`.
The local source-build workflow and produced archive path are documented in `docs\ffmpeg-8.1-build-notes.md`.
This runtime also requires `libwinpthread-1.dll`, which is staged beside the FFmpeg DLLs and validated through the manifest.
The export runtime is staged separately so export/probe work can run in a secondary headless host without touching the primary playback DLL set.
The export runtime restores only from its own source-built candidate or pinned archive. The distinct export-tools bundle is never used as a runtime seed because its binaries have separate build provenance and hashes.
The dedicated `Build-FFmpeg-ExportRuntime-8.1.ps1` script stages a lean local candidate under `Runtime\ffmpeg-export-8.1.2-candidate\` when you want to validate the smaller no-program runtime directly.
Use `scripts\Build-RustFfmpegProbe.ps1` on Windows or `scripts/Build-RustFfmpegProbe.sh` on macOS to stage the Rust FFmpeg native library for packaging the corresponding universal-app target.
