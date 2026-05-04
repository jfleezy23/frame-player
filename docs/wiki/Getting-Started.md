# Getting Started

## Install

Windows stable:

1. Download `FramePlayer-CustomFFmpeg-1.8.4.zip` from the [Frame Player v1.8.4 release page](https://github.com/jfleezy23/frame-player/releases/tag/v1.8.4).
2. Extract the ZIP to a local folder.
3. Run `FramePlayer.exe`.

macOS Preview:

1. Download `FramePlayer-macOS-arm64-macos-preview-0.1.0.zip` and its SHA256 file from the [Frame Player v1.8.4 release page](https://github.com/jfleezy23/frame-player/releases/tag/v1.8.4).
2. Verify the ZIP hash if you are validating the release candidate.
3. Unzip the app and move `Frame Player.app` to `/Applications`.
4. Launch `Frame Player.app`.

The macOS Preview is notarized for Apple Silicon. Intel and universal macOS builds are not part of `0.1.0`.

Windows Avalonia Preview:

1. Download `FramePlayer-Desktop-Windows-x64-avalonia-windows-preview-0.1.0.zip` and its SHA256 file from the [Frame Player v1.8.4 release page](https://github.com/jfleezy23/frame-player/releases/tag/v1.8.4).
2. Verify the ZIP hash if you are validating the preview candidate.
3. Extract the ZIP to a local folder.
4. Run `FramePlayer.Desktop.exe`.

The Windows Avalonia Preview is self-contained for Windows x64 testers. It is a separate preview path and does not replace the stable Windows WPF app.

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
