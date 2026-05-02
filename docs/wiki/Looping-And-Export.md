# Looping And Export

## Loop Review

Frame Player supports whole-media loop playback and exact A/B loop playback.

- Press `[` to set loop in.
- Press `]` to set loop out.
- Press `L` to toggle loop playback.
- Loop status appears near the relevant transport surface.

In compare mode, loop context follows the active shared or pane-local review surface.

## Clip Export

Reviewed loop ranges can be exported as MP4 clips through the bundled FFmpeg export runtime. Export work runs through a hidden export host and does not require a system FFmpeg install.

## Compare Export

Two-pane compare sessions can export side-by-side MP4 output. The export can use loop range or whole-video mode, with audio selected from the appropriate pane when available.

## Audio Insertion

Audio insertion is available from the `Audio Insertion` menu. Use it when a review workflow needs to combine a video stream with an external or alternate audio stream.
