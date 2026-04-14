# Frame Player Release Candidate Test Drop

Release: `1.6.0`

## What changed recently

- The app is now custom FFmpeg only; FFME has been removed from the active path and older FFME-era releases are legacy/deprecated.
- Video playback, audio playback, basic A/V sync, seek-to-time, seek-to-frame, exact frame stepping, and opportunistic Vulkan decode with strict CPU fallback are implemented in the custom engine.
- The latest UI pass combined Play/Pause into one toggle, restored the normal visual tone, removed temporary custom-build banners, added a cache status indicator, and fixed arrow-key stepping immediately after frame entry.
- The latest GPU/cache pass adds a visible GPU toggle, pane-aware decoded-frame budgeting, shared Vulkan warmup, and backend-aware compare behavior without changing the frames-first review contract.
- The latest release polish pass adds live timeline scrubbing, A/B loop playback on the main transport, pane-local compare loop boxes, pane-local compare navigation, Inspector V2 with pane context-menu access, pending frame-number honesty while background indexing is still resolving absolute frame identity, follow-up fixes for fullscreen status-bar chrome plus pane-local frame clamping, and reviewed-loop MP4 clip export through a separate FFmpeg CLI bundle.

## Manual test checklist

- Launch the app from `bin\TestDrop\FramePlayer.exe` or the packaged `FramePlayer-CustomFFmpeg-<product-version>.zip` drop.
- Open a normal video file and verify the first displayed frame is frame `1`.
- Watch the status bar after open: it should distinguish index building/ready from the decoded review-cache window.
- Open a representative HEVC file with `Playback > Use GPU Acceleration` enabled and confirm diagnostics/log output report `ffmpeg-vulkan` when the local machine supports the Vulkan path.
- Disable `Playback > Use GPU Acceleration`, reopen the same file, and confirm the app stays correct on the CPU path.
- Press Play, confirm visible playback advances, then press Pause.
- Set `[` and `]` in single-pane mode, enable `Playback > Loop Playback`, and confirm playback loops the boxed range instead of the full clip.
- With a valid reviewed main-loop range, use `Playback > Save Loop As Clip...` and confirm an MP4 clip is written with duration close to the selected A/B window.
- Set a loop marker before indexing is ready on a large file and confirm the loop status stays visibly pending instead of pretending the range is finalized.
- Seek by time and confirm playback/review state remains coherent.
- On a large HEVC file, click-seek before indexing finishes and confirm the time lands while the frame number stays visibly pending instead of claiming a fake absolute frame.
- Type a frame number, commit it, then press Left/Right immediately to verify frame stepping works without another play/pause cycle.
- Step backward and forward repeatedly and confirm the frame counter moves exactly one frame at a time.
- Open two panes and confirm both panes stay responsive while stepping and seeking together.
- In two-pane mode, confirm the main transport controls both panes together while the pane-local sliders and frame boxes still operate on their own panes.
- In two-pane mode, set different pane-local loop boxes on Primary and Compare and confirm each pane slider shows its own boxed range instead of sharing one loop box.
- In two-pane mode, right-click each pane and use `Save Loop As Clip...` to confirm Primary and Compare can each export their own pane-local loop as separate MP4 clips.
- Right-click the primary pane and the compare pane, open `Video Info...` from both, and confirm two inspector windows can stay open at once with the correct pane-specific FFmpeg metadata.
- Try at least one video with audio and confirm audio starts during playback.
- If possible, try one video-only clip and confirm playback still works without audio errors.

## Known limitations

- GPU decode is opportunistic, not guaranteed: unsupported runtime, codec, driver, or device combinations must stay on CPU decode.
- The current GPU path still performs hardware decode plus CPU readback and BGRA conversion for the existing WPF presentation path.
- Playback is still an MVP path: no audio device selection, volume controls, advanced drift correction, or frame dropping/catch-up behavior.
- Cache/index status is intentionally coarse (`building` / `ready`) and may change quickly on short clips.
- Large files still require a full-file frame index scan, but that work now happens in the background after the first frame is visible.
- The pinned FFmpeg runtime is `n8.1-frameplayer-source`, recorded in `Runtime\runtime-manifest.json`.
- The runtime was built from the official FFmpeg source tag `n8.1` and is restored locally from the self-built candidate/archive produced by `scripts\ffmpeg\Build-FFmpeg-8.1.ps1`.
- The packaged runtime also requires `libwinpthread-1.dll`; it must ship beside `FramePlayer.exe` with the FFmpeg DLL set.
- Clip export uses a separate `ffmpeg-tools` folder beside the app output and depends on the hashes recorded in `Runtime\export-tools-manifest.json`.
- Clean-runner bootstrap restores from the verified `v1.5.0` runtime release asset recorded in `Runtime\runtime-manifest.json`.

## Build and shortcut

- Test drop executable: `bin\TestDrop\FramePlayer.exe`
- Release archive: `artifacts\FramePlayer-CustomFFmpeg-<product-version>.zip`
- Test-drop build script: `scripts\Build-TestDrop.ps1`
- Desktop shortcut name: `Frame Player`
- Shortcut refresh script: `scripts\Create-Comparison-Shortcuts.ps1`

To refresh the shortcut after rebuilding:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\scripts\Create-Comparison-Shortcuts.ps1"
```
