# Runtime Files

The FFmpeg runtime DLLs used by Frame Player are intentionally not stored in git.

For local development:

1. Run `.\scripts\Ensure-DevRuntime.ps1`
2. Or run `.\scripts\Build-FramePlayer.ps1`, which downloads the runtime first and then builds the app

The runtime is pinned through `Runtime\runtime-manifest.json` and is downloaded from the GitHub release assets for this project.
