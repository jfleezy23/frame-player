# Third-Party Notices

Frame Player depends on third-party libraries and runtime components that are not covered by the top-level [LICENSE](LICENSE).

## Managed Dependencies

### FFmpeg.AutoGen 8.0.0.1

- Project: <https://github.com/Ruslan-B/FFmpeg.AutoGen>
- Package metadata source: `packages/ffmpeg.autogen/8.0.0.1/*.nuspec`
- Package license file source: `packages/ffmpeg.autogen/8.0.0.1/LICENSE.txt`
- License: MIT

## Native Runtime

### FFmpeg Runtime Binaries

- Project: <https://ffmpeg.org/>
- Legal guidance: <https://ffmpeg.org/legal.html>
- Runtime version currently used by the app: `n8.1-frameplayer-source`
- Runtime provenance: built from the official FFmpeg source tag `n8.1` at commit `9047fa1b084f76b1b4d065af2d743df1b40dfb56`
- Runtime DLLs are restored locally through `scripts/ffmpeg/Build-FFmpeg-8.1.ps1` and `scripts/Ensure-DevRuntime.ps1`, not stored in git history

Important note:

- FFmpeg licensing depends on how the runtime binaries were built.
- FFmpeg's official legal page states that FFmpeg is generally available under the LGPL v2.1 or later, but GPL obligations apply if GPL-enabled parts were compiled in.
- If you replace the runtime bundle, you are responsible for verifying the exact redistribution obligations of the replacement build.

### FFmpeg Export Tools

- Project: <https://ffmpeg.org/>
- Tool bundle version currently used by the app: `n8.1-frameplayer-export-tools`
- Tool provenance: built from the official FFmpeg source tag `n8.1` for `ffmpeg.exe` / `ffprobe.exe`, restored locally through `scripts/ffmpeg/Build-FFmpeg-Tools-8.1.ps1` and `scripts/Ensure-DevExportTools.ps1`
- The export-tools bundle is intentionally separated from the in-process playback runtime and lives under `ffmpeg-tools`

Important note:

- The export-tools build enables `libx264`, which makes that tool bundle GPL-governed.
- The export-tools bundle also stages required MinGW-w64/MSYS2 dependency DLLs that the CLI resolves at runtime, including the x264 runtime and related support libraries.
- If you redistribute the export-tools bundle, review FFmpeg and x264 upstream licensing terms and make sure your redistribution flow satisfies the resulting GPL obligations.

### x264

- Project: <https://code.videolan.org/videolan/x264>
- Role in this repo: enabled only in the separate FFmpeg export-tools bundle to provide H.264 MP4 clip export
- License note: x264 is GPL-licensed; review the upstream project before redistributing builds that include it

## Attribution

Frame Player itself is authored and released separately from the components above. Review the individual upstream projects and license texts before redistributing modified builds.
