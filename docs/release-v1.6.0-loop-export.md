# Frame Player v1.6.0 Release Note

This note documents the current `v1.6.0` release. It is the maintainer-facing summary of what is new on top of `v1.5.0`, what remains intentionally deferred, and what validation evidence backs this release.

## What Is New In v1.6.0

- Reviewed-loop MP4 clip export now ships in the product:
  - `Playback > Save Loop As Clip...` exports the reviewed main A/B range in single-pane mode
  - Primary and Compare pane context menus each expose `Save Loop As Clip...` for pane-local compare exports
  - export runs through a separate pinned `ffmpeg-tools` bundle instead of the in-process playback runtime
- Frames-first export gating stays intact:
  - loop markers still begin life as pending until exact frame identity is proven
  - export and loop-status rendering now promote pending markers through indexed frame identity instead of time-only guesses
  - right-click export remains available as soon as the index proves the selected frames, even for corpus `.mov` review files
- Automated coverage expanded again:
  - audio-bearing main-window loop export is now covered in the hidden-window regression harness
  - existing app-driven loop/export regressions remain green on the shipped path

## What Stays Deferred

- Export is still one clip at a time with a fixed MP4 output contract.
- No batch export, preset chooser, alternate container/codec chooser, or side-by-side comparison render ships in `v1.6.0`.
- The pinned FFmpeg runtime bootstrap asset in `Runtime\runtime-manifest.json` still points at the verified `v1.5.0` runtime release because the runtime DLL set itself did not change for this product cut.

## Runtime And CI Truth

- Product version: `v1.6.0`
- Current published clean-runner bootstrap asset: `v1.5.0`
- `Runtime\runtime-manifest.json` remains pinned to the verified `v1.5.0` runtime asset and should stay aligned with the published archive SHA256 and DLL hashes until the runtime bundle itself changes.

## Validation Evidence

Green validation captured for this release:

- PR `#17` merged with green GitHub checks:
  - Windows CI
  - SonarQube / SonarCloud Code Analysis
  - CodeQL
  - dependency review
- Audio-bearing export regression:
  - file tested: `Audio_Video_Sync_23,98_HEVC_1080p-by_PhotoJoseph.mov`
  - checks run: `47`
  - pass / warning / fail: `43 / 4 / 0`
- Video-only export smoke:
  - file tested: `sample-test.mp4`
  - checks run: `53`
  - pass / warning / fail: `52 / 1 / 0`
- Real UI verification:
  - right-click `Save Loop As Clip...` on the corpus `.mov` opened the native save dialog
  - the exported file landed as `Audio_Video_Sync_23,98_HEVC_1080p-by_PhotoJoseph-000005005-000010010.mp4`
  - `ffprobe` reported a duration of `5.047` seconds for that saved clip

Known non-blocking warnings remain honest-state warnings rather than silent failures:

- pre-index seek can still land on time before absolute frame identity is ready
- the UI still withholds a numeric frame claim in that pending window

Follow-up regression hardening after the `v1.6.0` cut also removed the old hidden-window audio-bearing playback exemption:

- supported audio-bearing corpus files now run the same hidden-window timed playback and loop playback checks as video-only files
- full-corpus clip export remains covered on the supported container set `.avi`, `.mov`, `.m4v`, `.mp4`, `.mkv`, and `.wmv`
- unsupported `.ts` corpus entries remain intentionally excluded from the active supported surface

## Release Guidance

- Treat `v1.6.0` as the feature release that turns the reviewed A/B loop surface into a real export workflow.
- Keep `Properties\AssemblyInfo.cs` as the canonical product-version source.
- Keep the app-driven regression suite as the source of truth for loop/export validation; Sonar coverage exclusions intentionally reflect that desktop-path testing model.
- Release assets for this cut are expected to include:
  - `FramePlayer-CustomFFmpeg-1.6.0.zip`
  - the usual test-drop folder build from `bin\TestDrop`
