# Historical Custom FFmpeg Engine Plan

This is a historical planning checkpoint kept for project history. It predates the active `v1.4.0` GPU release candidate. For current shipped behavior, see `docs\gpu-vulkan-phase1-release.md` and `docs\ffmpeg-8.1-build-notes.md`.

## What Changed

- Added an engine abstraction layer under `Core/` with a small `IVideoReviewEngine` contract and review-oriented models for media info, positions, decoded frames, and frame-step results.
- Implemented a real FFmpeg-backed review cursor in `Engines/FFmpeg/FfmpegReviewEngine.cs` using `FFmpeg.AutoGen`.
- Added focused FFmpeg helpers for error handling, timestamp conversion, BGRA conversion, and a small decoded-frame cache in:
  `Engines/FFmpeg/FfmpegNativeHelpers.cs`, `Engines/FFmpeg/FfmpegFrameConverter.cs`, and `Engines/FFmpeg/FfmpegDecodedFrameCache.cs`.
- Extended the shared models so decoded frames can carry truthful source timestamps, honest frame-index semantics, step-source metadata, and an engine-owned BGRA pixel buffer suitable for a future `WriteableBitmap` path.
- Added real backward single-frame review for the custom engine:
  cache-backed when the prior decoded frame is already retained, and reconstruction-backed when the engine must seek backward and decode forward to rebuild the immediately preceding display-order frame.
- Added real `SeekToFrameAsync` support for the custom engine:
  exact when the requested absolute frame is already in the decoded absolute-frame cache, otherwise exact via stream-start reconstruction and decode-forward to the requested display-order frame.
- Added lightweight custom-engine diagnostics in `Diagnostics/ReviewEngineComparisonRunner.cs`.
- Added a file-global frame index for the custom FFmpeg engine:
  the primary video stream is scanned in decoded display order on open, and the index now provides absolute frame identity, timestamp mapping, and a nearest-earlier seek anchor for materializing real frames.
- Added timed video playback to the custom FFmpeg engine:
  playback advances decoded display-order frames on a background loop using actual frame presentation timing when available, while preserving the existing review cursor/cache semantics.
- Added MVP audio playback and basic A/V sync:
  audio is decoded on a separate FFmpeg demux/decode path, resampled to stereo 16-bit PCM, submitted to Windows `waveOut`, and used as the playback master clock when audio output starts successfully.
- Made the custom FFmpeg comparison build display and accept zero-indexed frame numbers consistently:
  frame `0` is the first user-visible frame in the custom path, including the frame input, status text, and diagnostics export.
- Cut the active WPF app path over to the custom FFmpeg engine:
  `MainWindow` now constructs `FfmpegReviewEngine` directly, the visible app surface is the custom BGRA image surface, and app startup configures `FFmpeg.AutoGen` directly.
- Removed the legacy FFME adapter, FFME package reference, and FFME comparison script after the custom-only app path was validated.
- Removed comparison-era UI branding and restored the normal Frame Player presentation:
  the visible app now uses a single Play/Pause toggle and a neutral decoded-frame cache status indicator.

## Current Architecture

- The WPF window now owns the visible custom FFmpeg image surface and bundled FFmpeg runtime/bootstrap flow.
- `MainWindow` now talks to `IVideoReviewEngine` for transport and review actions.
- `MainWindow` directly constructs `FfmpegReviewEngine`; there is no backend switch in the normal app path.
- `FfmpegReviewEngine` now supports:
  open, first-frame decode, timed audio/video playback, pause, seek-to-time, seek-to-frame, forward single-frame stepping, backward single-frame stepping, BGRA conversion, state-change events, and frame-presented events.
- The main app routes `FramePresented` BGRA frames into a visible WPF image surface for manual visual testing and normal playback.
- Audio playback is intentionally MVP-scoped:
  when an audio stream is present and decodable, a separate audio session feeds Windows `waveOut`; when audio is absent or initialization fails, video playback continues using the existing video-timing fallback.

## What Metadata Is Now Real

- `VideoMediaInfo` is populated from FFmpeg stream/container data for the custom engine path:
  file path, width, height, codec name, duration when available, nominal frame rate when available, video stream index, stream time base, audio stream presence, audio codec, audio stream index, sample rate, and channel count when available.
- `FrameDescriptor` for decoded custom-engine frames reflects:
  decoded display-order frame index, whether that frame index is absolute from stream start, presentation time, presentation timestamp, decode timestamp, duration timestamp when available, keyframe flag, source pixel format, and converted output pixel format.
- `DecodedFrameBuffer` is now the authoritative decoded-frame payload, and the WPF shell creates `BitmapSource` instances only at presentation time.
- `ReviewPosition` reflects the current decoded frame's actual presentation metadata instead of placeholder timing guesses.
- `FrameStepResult` now records whether a frame step was satisfied from cache and whether reconstruction was required.

## Current Review Cursor Behavior

- Opening a file now prioritizes first-frame presentation:
  the primary stream is opened/probed, the first displayable frame is decoded, and the small decoded review cache is warmed before the full file-global frame index finishes.
- The file-global index now builds in the background after the first frame is available, so longer clips no longer block first paint on the full-file scan.
- The file-global index records, for each decoded displayable frame:
  absolute frame index, presentation/decode timestamps when available, presentation time, keyframe status, and a nearest-earlier decode anchor for later materialization.
- Seeking still uses real FFmpeg seek plus decode-forward mechanics to materialize the frame that will actually be presented.
- `SeekToTimeAsync` now asks the global index for the first indexed frame at or after the requested timestamp when possible, then seeks to that frame's stored anchor and decodes forward until the exact indexed target frame is reached.
- `SeekToFrameAsync` now uses the global index as its primary path:
  cache hit when the requested absolute frame is already decoded, otherwise decode-forward from the index-provided anchor instead of blindly restarting from stream start.
- Successful indexed `SeekToTimeAsync` and `SeekToFrameAsync` results now preserve absolute frame identity across the clip much more consistently than before.
- `SeekToFrameAsync` now targets an absolute decoded display-order frame index from stream start.
- If that absolute frame is still provably present in the current decoded absolute-frame cache, the engine moves directly to it without re-decoding.
- Otherwise `SeekToFrameAsync` uses the global index anchor for the requested frame, decodes forward until the exact requested absolute frame is reached, and then rebuilds the local cache window around that frame.
- Forward stepping advances exactly one decoded display-order frame at a time.
- Forward stepping prefers a cached decoded frame when one is already available and falls back to continued decode when the forward cache has been exhausted.
- Backward stepping also advances by true decoded display-order identity rather than timestamp subtraction.
- Backward stepping first checks the retained previous-frame cache window.
- On cache miss, backward stepping now prefers the global frame index when the current frame already has absolute identity:
  it targets the previous absolute frame, seeks to that frame's indexed anchor, and decodes forward until the exact previous frame is materialized.
- If indexed absolute identity is not available, backward stepping still falls back to the earlier timestamp-based reconstruction path.
- If the initial reconstruction seek lands too close to the current frame to prove whether a predecessor exists, the engine retries from stream start before reporting that it is already at the first displayable frame.
- Timed playback advances the same review cursor used by stepping and seeking.
- During playback, the engine first consumes decoded forward-cache frames when available and then continues decoding forward in display order.
- Playback timing prefers the delta between adjacent decoded frame presentation times, then frame duration metadata, then the media position step/nominal frame-rate fallback.
- When audio output is active, playback uses the submitted audio clock as the master clock and presents video frames only when the next decoded display-order frame is due.
- The playback loop does not drop, skip, or synthesize review frames to catch up; preserving the exact review cursor remains more important than smoothness under load.
- Pausing playback leaves the current frame identity intact so seek-to-frame, step-forward, and step-backward can continue from the paused review cursor.
- User-visible frame numbering is zero-indexed:
  frame `0` is the first decoded display-order frame, and the visible `/` value is the last zero-indexed frame when a global index is available.

## Cache Behavior

- The custom engine keeps a small sliding cache of decoded BGRA frames around the review cursor.
- The status bar now reports cache activity and the current cached window size, for example when indexing, seeking, warming, or rebuilding the local decoded-frame cache.
- The cache currently retains the current frame, up to eight prior frames, and up to three forward frames.
- Open and seek populate the current frame and warm the forward side of the cache.
- Cache-backed forward steps consume prefetched frames without re-decoding them.
- When the forward cache runs out, the next forward step decodes one more displayable frame, advances the cursor, and then repopulates the forward side of the cache.
- Cache-backed backward steps reuse retained previous decoded frames directly.
- Reconstruction-backed backward steps reload the cache with a coherent review window:
  up to two frames before the reconstructed target, the target frame as current, the original current frame as the first forward frame, and additional forward frames when available.
- After stepping backward, the cache may temporarily hold more than three forward frames relative to the new cursor because the already-decoded window is preserved for cursor coherence rather than discarded immediately.
- Indexed seek materialization normalizes the cached window back onto the global absolute frame sequence whenever the indexed anchor can identify the target path reliably.
- Cache-backed frame seeks are now limited mainly by whether the requested absolute frame is already present in the decoded cache, not by whether the engine had to reconstruct absolute identity from scratch.

## Global Index

- The custom engine now starts building a file-global frame index for the selected primary video stream after the first decoded frame is available.
- The current implementation still performs a separate full-file FFmpeg decode scan for indexing, but that work runs on a cancellable background task instead of blocking first-frame presentation.
- This is still correctness-first:
  longer clips continue to pay a full-file indexing cost, but the user can see the first frame sooner and exact review operations continue to materialize frames through real decode paths.
- The index stores enough information to support:
  absolute frame-index lookup, timestamp-to-frame lookup, unique timestamp-based identity resolution where possible, and nearest-earlier anchor selection for real frame materialization.
- The index does not replace decoding:
  the engine still seeks and decodes the actual target frame for presentation rather than synthesizing frame delivery from timestamps alone.
- Until the background index is ready, operations that cannot prove global identity use the existing exact cache/reconstruction paths or stay honest about segment-local identity rather than faking an indexed result.

## Manual Diagnostics

- `Diagnostics/ReviewEngineComparisonRunner.cs` can run the same open, playback, seek-to-time, seek-to-frame, step-backward, and step-forward sequence against the active custom FFmpeg engine.
- `scripts/Run-ReviewEngine-ManualTests.ps1` adds a batch-oriented manual workflow for single files, explicit file lists, or folders of videos and writes JSON, CSV, and markdown summaries for repeatable review.
- The report keeps backend labels explicit and includes high-signal notes about:
  global index availability, indexed frame count, anchor strategy, whether the resulting custom-engine position retained absolute frame identity, audio stream availability, audio playback initialization, submitted audio bytes, and whether playback used the audio clock.

## FFME Removal Status

- The normal app no longer uses FFME.
- `MainWindow.xaml` no longer contains the legacy media element.
- `App.xaml.cs` configures `FFmpeg.AutoGen` runtime loading directly.
- The FFME adapter file, FFME package reference, and standalone backend comparison script have been removed.
- At the time of this checkpoint the active release line was still the custom-FFmpeg-only branch, and older FFME-era releases were already legacy/deprecated.
- The current test-drop output is expected under `bin\TestDrop\`.

## What Still Remains Stubbed

- The custom engine now implements MVP audio playback and basic audio-master A/V sync, but it does not implement hardware acceleration, advanced drift correction, audio device selection, volume controls, or full consumer-player parity.
- The current file-global index is still a full-file scan, which is correct but can be expensive on longer clips; the scan now happens in the background after first-frame open.
- Absolute identity still depends on the index's ability to match decoded frames back to indexed entries when timestamps are available or when materialization starts from a known indexed anchor.
- The backward reconstruction path still has a non-indexed fallback for uncertain cases where absolute identity cannot be proven immediately.
- The index currently stores a simple nearest-earlier anchor and does not yet maintain a richer persistent keyframe map, packet-position map, or incremental background indexing strategy.
- Playback currently avoids frame dropping/catch-up behavior under decode pressure because review accuracy and deterministic stepping are higher priority than smooth catch-up.
- If audio initialization fails for a supported video, playback falls back to video-only and exposes the audio error through diagnostics.

## Recommended Next Pass

1. Continue GUI usability polish:
   refine seek/open progress feedback, improve end-of-stream messaging, and make audio fallback information easier to discover without cluttering the main window.
2. Add playback robustness controls:
   investigate end-of-stream messaging, audio fallback visibility, and more visible error reporting.
3. Optimize index construction and reuse:
   preserve correctness-first behavior, but investigate cached index reuse or staged/background indexing so `OpenAsync` does not always require a full-file scan.
4. Strengthen indexed materialization metadata:
   expose explicit operation outcomes for cache hit versus indexed-anchor decode versus timestamp-only fallback in a more structured way.
5. Improve backward-step efficiency with richer anchors:
   extend the global index with better keyframe/decode-start hints so reconstruction misses require less decode work on longer GOPs.
