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

## Attribution

Frame Player itself is authored and released separately from the components above. Review the individual upstream projects and license texts before redistributing modified builds.
