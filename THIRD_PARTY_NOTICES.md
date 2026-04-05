# Third-Party Notices

Frame Player depends on third-party libraries and runtime components that are not covered by the top-level [LICENSE](LICENSE).

## Managed Dependencies

### FFME.Windows 7.0.361-beta.1

- Project: <https://github.com/unosquare/ffmediaelement>
- Package metadata source: `packages/ffme.windows/7.0.361-beta.1/ffme.windows.nuspec`
- Package license file source: `packages/ffme.windows/7.0.361-beta.1/LICENSE`
- License: Microsoft Public License (Ms-PL)

### FFmpeg.AutoGen 7.0.0

- Project: <https://github.com/Ruslan-B/FFmpeg.AutoGen>
- Package metadata source: `packages/ffmpeg.autogen/7.0.0/*.nuspec`
- Package license file source: `packages/ffmpeg.autogen/7.0.0/LICENSE.txt`
- License: GNU Lesser General Public License v3.0

## Native Runtime

### FFmpeg Runtime Binaries

- Project: <https://ffmpeg.org/>
- Legal guidance: <https://ffmpeg.org/legal.html>
- Runtime version currently used by the packaged release assets: `n7.0.2-6-g7e69129d2f-20240831`
- Runtime DLLs are downloaded locally through `scripts/Ensure-DevRuntime.ps1` and are distributed through this repository's GitHub release assets, not stored in git history

Important note:

- FFmpeg licensing depends on how the runtime binaries were built.
- FFmpeg's official legal page states that FFmpeg is generally available under the LGPL v2.1 or later, but GPL obligations apply if GPL-enabled parts were compiled in.
- If you replace the runtime bundle, you are responsible for verifying the exact redistribution obligations of the replacement build.

## Attribution

Frame Player itself is authored and released separately from the components above. Review the individual upstream projects and license texts before redistributing modified builds.
