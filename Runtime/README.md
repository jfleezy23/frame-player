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
The active runtime is the self-built FFmpeg 8.1 line staged locally by `scripts\ffmpeg\Build-FFmpeg-8.1.ps1`.
`scripts\Ensure-DevRuntime.ps1` restores `Runtime\ffmpeg\` from the local candidate folder or local runtime archive before attempting any download fallback.
Packaged builds must include `libwinpthread-1.dll` beside the FFmpeg DLLs and `FramePlayer.exe`.
The separate export runtime is pinned through `Runtime\export-runtime-manifest.json` and restored to `Runtime\ffmpeg-export\`.
The local/dev-only CLI tool bundle remains pinned through `Runtime\export-tools-manifest.json` and restored to `Runtime\ffmpeg-tools\`.

Current pinned FFmpeg runtime version: `n8.1-frameplayer-source`.
Current pinned FFmpeg export runtime version: `n8.1-frameplayer-export-tools`.
Current pinned FFmpeg export-tools version: `n8.1-frameplayer-export-tools`.

Source provenance note: the pinned runtime was built from the official FFmpeg source tag `n8.1` at commit `9047fa1b084f76b1b4d065af2d743df1b40dfb56`.
The local source-build workflow and produced archive path are documented in `docs\ffmpeg-8.1-build-notes.md`.
This runtime also requires `libwinpthread-1.dll`, which is staged beside the FFmpeg DLLs and validated through the manifest.
The export runtime is staged separately so export/probe work can run in a secondary headless host without touching the primary playback DLL set.
The current restore path can seed `ffmpeg-export` from the local export-tools bundle for dev/test convenience, but shipped output is expected to carry only the DLL-based export runtime.
The dedicated `Build-FFmpeg-ExportRuntime-8.1.ps1` script stages a lean local candidate under `Runtime\ffmpeg-export-8.1-candidate\` when you want to validate the smaller no-program runtime directly.
