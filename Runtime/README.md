# Runtime Files

The FFmpeg runtime DLLs used by Frame Player are intentionally not stored in git.

For local development:

1. Run `.\scripts\ffmpeg\Build-FFmpeg-8.1.ps1`
2. Then run `.\scripts\Ensure-DevRuntime.ps1`
3. Or run `.\scripts\Build-FramePlayer.ps1`, which restores the runtime first and then builds the app

The runtime is pinned through `Runtime\runtime-manifest.json`.
The active runtime is the self-built FFmpeg 8.1 line staged locally by `scripts\ffmpeg\Build-FFmpeg-8.1.ps1`.
`scripts\Ensure-DevRuntime.ps1` restores `Runtime\ffmpeg\` from the local candidate folder or local runtime archive before attempting any download fallback.
Packaged builds must include `libwinpthread-1.dll` beside the FFmpeg DLLs and `FramePlayer.exe`.

Current pinned FFmpeg runtime version: `n8.1-frameplayer-source`.

Source provenance note: the pinned runtime was built from the official FFmpeg source tag `n8.1` at commit `9047fa1b084f76b1b4d065af2d743df1b40dfb56`.
The local source-build workflow and produced archive path are documented in `docs\ffmpeg-8.1-build-notes.md`.
This runtime also requires `libwinpthread-1.dll`, which is staged beside the FFmpeg DLLs and validated through the manifest.
