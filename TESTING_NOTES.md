# Frame Player Release Verification Notes

Release: `1.8.1`

## Current release focus

- The app is now custom FFmpeg only; FFME has been removed from the active path and older FFME-era releases are legacy/deprecated.
- Video playback, audio playback, basic A/V sync, seek-to-time, seek-to-frame, exact frame stepping, and opportunistic Vulkan decode with strict CPU fallback are implemented in the custom engine.
- The current WPF shell includes the combined Play/Pause control, cache-status visibility, immediate post-frame-entry arrow-key stepping, a visible GPU toggle, pane-aware decoded-frame budgeting, shared Vulkan warmup, and backend-aware compare behavior without changing the frames-first review contract.
- The current `v1.8.1` release keeps the `v1.8.0` feature set, including live timeline scrubbing, A/B loop playback on the main transport, pane-local compare loop boxes, pane-local compare navigation, Inspector V2 with pane context-menu access, pending frame-number honesty while background indexing is still resolving absolute frame identity, reviewed-loop MP4 clip export through a separate FFmpeg export host/runtime path, and side-by-side compare export with loop and whole-video modes.
- The current MVP-finish pass adds single-pane `Audio Insertion > Replace Audio Track...` for H.264 `.mp4` sources, pane-local paused zoom/pan in both review layouts, zoom-aware pixel readout, `Playback > Reset Zoom`, pane context-menu zoom reset, and crop-aware clip/compare export rendering.
- The shipped app output for this release line no longer includes `ffmpeg.exe`, `ffprobe.exe`, or an `ffmpeg-tools` directory; probe/export work runs through the DLL-only `ffmpeg-export` runtime in the headless export host.

## Manual test checklist

- Launch the app from `bin\TestDrop\FramePlayer.exe`.
- Open a normal video file and verify the first displayed frame is frame `1`.
- Watch the status bar after open: it should distinguish index building/ready from the decoded review-cache window.
- Open a representative HEVC file with `Playback > Use GPU Acceleration` enabled and confirm diagnostics/log output report `ffmpeg-vulkan` when the local machine supports the Vulkan path.
- Disable `Playback > Use GPU Acceleration`, reopen the same file, and confirm the app stays correct on the CPU path.
- Press Play, confirm visible playback advances, then press Pause.
- Set `[` and `]` in single-pane mode, enable `Playback > Loop Playback`, and confirm playback loops the boxed range instead of the full clip.
- Right-click the main timeline and confirm `Set Position A Here`, `Set Position B Here`, `Loop Playback`, and `Save Loop As Clip...` all work from the timeline menu.
- On the main timeline, confirm `Set Position B Here` is blocked when the clicked target lands before position A, and that the menu enables once the clicked target is at or after A.
- With a valid reviewed main-loop range, use `Playback > Save Loop As Clip...` and confirm an MP4 clip is written with duration close to the selected A/B window.
- In single-pane mode with a loaded H.264 `.mp4`, open `Audio Insertion > Replace Audio Track...`, choose a `.wav`, save to a new `.mp4`, and confirm the output keeps the source video duration while replacing the original audio.
- Repeat `Audio Insertion > Replace Audio Track...` with a `.mp3` replacement track and confirm the output stays an `.mp4`, the video stream is preserved, and the replacement audio is present.
- Switch to two-pane compare and confirm `Audio Insertion > Replace Audio Track...` stays visible but disabled with a tooltip explaining audio insertion is unavailable there.
- Load a non-`.mp4` source or a non-H.264 `.mp4` source and confirm `Audio Insertion > Replace Audio Track...` stays disabled with a reason that matches the source limitation.
- Set a loop marker before indexing is ready on a large file and confirm the loop status stays visibly pending instead of pretending the range is finalized.
- Seek by time and confirm playback/review state remains coherent.
- On a large HEVC file, click-seek before indexing finishes and confirm the time lands while the frame number stays visibly pending instead of claiming a fake absolute frame.
- Type a frame number, commit it, then press Left/Right immediately to verify frame stepping works without another play/pause cycle.
- Step backward and forward repeatedly and confirm the frame counter moves exactly one frame at a time.
- In single-pane mode while paused, use the mouse wheel to zoom, left-drag to pan, then play, pause, seek, and frame-step to confirm the zoomed viewport persists until `Playback > Reset Zoom` is used.
- While zoomed, confirm the status-bar pixel readout tracks the zoomed crop rather than the original full-frame fit.
- Open two panes and confirm both panes stay responsive while stepping and seeking together.
- In two-pane mode, confirm the main transport controls both panes together while the pane-local sliders and frame boxes still operate on their own panes.
- In two-pane mode, set different pane-local loop boxes on Primary and Compare and confirm each pane slider shows its own boxed range instead of sharing one loop box.
- In two-pane mode, confirm only the Primary and Compare pane timelines expose pane-local `Set Position A Here`, `Set Position B Here`, `Loop Playback`, and `Save Loop As Clip...` actions.
- In two-pane mode while paused, zoom and pan each pane independently, then confirm playback keeps each pane in its own zoomed state and `Reset Zoom` from the pane context menu only resets the targeted pane.
- In two-pane mode, right-click each pane and use `Save Loop As Clip...` to confirm Primary and Compare can each export their own pane-local loop as separate MP4 clips.
- In single-pane mode with a zoomed viewport, export a reviewed loop clip and confirm the rendered MP4 reflects the zoomed crop while keeping the existing loop timing and audio behavior.
- In two-pane mode, use `Playback > Export Side-by-Side Compare...`, choose `Loop`, select audio from both panes on separate runs, and confirm the merged MP4 keeps both panes at full reviewed raster size without downscaling.
- In two-pane mode, use `Playback > Export Side-by-Side Compare...`, choose `Whole Video`, and confirm the merged MP4 preserves the current compare alignment by adding black lead-in to the earlier pane instead of trimming it away.
- In two-pane mode with independent pane zoom/pan applied, run both `Loop` and `Whole Video` compare exports and confirm each pane's rendered side uses its own zoomed crop without changing loop timing, whole-video alignment, or audio-source selection rules.
- If the selected side-by-side export audio source has no audio stream, confirm the merged MP4 still exports successfully as silent video.
- Right-click the primary pane and the compare pane, open `Video Info...` from both, and confirm two inspector windows can stay open at once with the correct pane-specific FFmpeg metadata.
- While zoomed, confirm simple click still focuses the pane, double-click still toggles fullscreen, and `Playback > Reset Zoom` returns the focused pane to the full-frame view without disturbing playback state.
- Try at least one video with audio and confirm audio starts during playback.
- If possible, try one video-only clip and confirm playback still works without audio errors.

## Automated regression coverage

- The repository-carried PowerShell harness is still the primary product-level validation surface:
  - `scripts\Run-RegressionSuite.ps1` for app-driven regression coverage
  - `scripts\Run-ReviewEngine-ManualTests.ps1` for deterministic manual review-engine sweeps, launched headlessly through the app runtime
  - `scripts\Build-TestDrop.ps1` for release-style runtime/test-drop validation
- Small unit tests can complement these harnesses for cold-path service logic, but they do not replace the app-driven harness for frame-review behavior.
- The supported full-corpus regression path now runs hidden-window timed playback, loop playback, clip export, and side-by-side compare export coverage for both audio-bearing and video-only files.
- The packaged regression suite also includes the repo-carried `dist\Frame Player\sample-test.mp4` when present, so the final file count can be one higher than the external corpus count alone.
- Audio insertion and crop-aware zoom/export remain manual-regression coverage for this MVP slice; there is still no committed automated UI test source under `tests\`.
- Full-corpus trim/export coverage is expected on the active supported container set: `.avi`, `.mov`, `.m4v`, `.mp4`, `.mkv`, and `.wmv`.
- `.ts` remains intentionally outside the active supported surface and is skipped by the full-corpus regression suite instead of counted as a product failure.

## Known limitations

- GPU decode is opportunistic, not guaranteed: unsupported runtime, codec, driver, or device combinations must stay on CPU decode.
- The current GPU path still performs hardware decode plus CPU readback and BGRA conversion for the existing WPF presentation path.
- Playback is still an MVP path: no audio device selection, volume controls, advanced drift correction, or frame dropping/catch-up behavior.
- Cache/index status is intentionally coarse (`building` / `ready`) and may change quickly on short clips.
- Large files still require a full-file frame index scan, but that work now happens in the background after the first frame is visible.
- The pinned FFmpeg runtime is `n8.1-frameplayer-source`, recorded in `Runtime\runtime-manifest.json`.
- The runtime was built from the official FFmpeg source tag `n8.1` and is restored locally from the self-built candidate/archive produced by `scripts\ffmpeg\Build-FFmpeg-8.1.ps1`.
- The bundled runtime also requires `libwinpthread-1.dll`; it must ship beside `FramePlayer.exe` with the FFmpeg DLL set.
- Export work uses a separate `ffmpeg-export` folder beside the app output and depends on the hashes recorded in `Runtime\export-runtime-manifest.json`.
- The `ffmpeg-tools` bundle remains a local/dev harness asset only and is not expected in the shipped app output.
- Clean-runner bootstrap restores from the verified runtime-only `v1.5.0` release asset recorded in `Runtime\runtime-manifest.json`.

## Build and shortcut

- Test drop executable: `bin\TestDrop\FramePlayer.exe`
- Startup-open helper for visible UI automation: `bin\TestDrop\FramePlayer.exe --open-file <absolute-media-path>`
- Test-drop build script: `scripts\Build-TestDrop.ps1`
- Live visible loop verifier: `scripts\ui_loop_visible_test.py`
- Desktop shortcut name: `Frame Player`
- Shortcut refresh script: `scripts\Create-Comparison-Shortcuts.ps1`

To refresh the shortcut after rebuilding:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\scripts\Create-Comparison-Shortcuts.ps1"
```
