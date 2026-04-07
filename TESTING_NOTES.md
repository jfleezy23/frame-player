# Frame Player Custom FFmpeg Test Drop

Checkpoint: `1.1.0-test1`

## What changed recently

- The app is now custom FFmpeg only; FFME has been removed from the active path and older FFME-era releases are legacy/deprecated.
- Video playback, audio playback, basic A/V sync, seek-to-time, seek-to-frame, and exact zero-indexed frame stepping are implemented in the custom engine.
- The latest UI pass combined Play/Pause into one toggle, restored the normal visual tone, removed temporary custom-build banners, added a cache status indicator, and fixed arrow-key stepping immediately after frame entry.
- The latest performance pass moves the full file-global frame index scan behind first-frame open and increases the decoded backward review cache to eight frames.

## Manual test checklist

- Launch the app from `bin\TestDrop\FramePlayer.exe`.
- Open a normal video file and verify the first displayed frame is frame `0`.
- Watch the status bar after open: it should distinguish index building/ready from the decoded review-cache window.
- Press Play, confirm visible playback advances, then press Pause.
- Seek by time and confirm playback/review state remains coherent.
- Type a frame number, commit it, then press Left/Right immediately to verify frame stepping works without another play/pause cycle.
- Step backward and forward repeatedly and confirm the frame counter moves exactly one frame at a time.
- Try at least one video with audio and confirm audio starts during playback.
- If possible, try one video-only clip and confirm playback still works without audio errors.

## Known limitations

- Playback is an MVP path: no audio device selection, volume controls, hardware acceleration, advanced drift correction, or frame dropping/catch-up behavior yet.
- Cache/index status is intentionally coarse (`building` / `ready`) and may change quickly on short clips.
- Large files still require a full-file frame index scan, but that work now happens in the background after the first frame is visible.

## Build and shortcut

- Test drop executable: `bin\TestDrop\FramePlayer.exe`
- Test drop archive: `artifacts\FramePlayer-CustomFFmpeg-1.1.0-test1.zip`
- Desktop shortcut name: `Frame Player - Custom FFmpeg`
- Shortcut refresh script: `scripts\Create-Comparison-Shortcuts.ps1`

To refresh the shortcut after rebuilding:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\scripts\Create-Comparison-Shortcuts.ps1"
```
