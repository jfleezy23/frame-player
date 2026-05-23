# Getting Started

## Install

macOS:

1. Download `FramePlayer-macOS-arm64-2.0.0.zip` and its SHA256 file from the [Frame Player v2.0.0 release page](https://github.com/jfleezy23/frame-player/releases/tag/v2.0.0).
2. Verify the ZIP hash.
3. Unzip the app and move `Frame Player.app` to `/Applications`.
4. Launch `Frame Player.app`.

The macOS release is built for Apple Silicon.

Windows:

1. Download `FramePlayer-Windows-x64-2.0.0.zip` and its SHA256 file from the [Frame Player v2.0.0 release page](https://github.com/jfleezy23/frame-player/releases/tag/v2.0.0).
2. Verify the ZIP hash.
3. Extract the ZIP to a local folder.
4. Run `FramePlayer.Avalonia.exe`.

## Open A Video

- Use `File > Open Video`.
- Use the `Open Video` button in the empty state.
- Drag a supported video file onto the review surface.
- Use `File > Open Recent` after you have opened files before.

Supported release formats include `.avi`, `.m4v`, `.mp4`, `.mkv`, and `.wmv`.

## Basic Review Flow

1. Open a video.
2. Use Space to play or pause.
3. Use Left and Right while paused to step one frame.
4. Enter a frame number in the `Frame` box and press Enter to jump.
5. Scrub the timeline to seek, then release to land paused at the selected position.

Frame Player uses 1-based frame numbers in the UI and treats decoded display-order frame identity as the source of truth for frame review.
