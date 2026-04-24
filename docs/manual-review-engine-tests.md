# Manual Review Engine Tests

## Purpose

`Run-ReviewEngine-ManualTests.ps1` runs a deterministic custom FFmpeg review sequence across one or more clips.

The workflow is now custom-only. It is intended for local regression checks of the active engine, not backend comparison.

The PowerShell harness launches `FramePlayer.exe` headlessly through the app's
`--run-review-engine-manual-tests-request` entrypoint, so the sweep runs under
the app's own .NET runtime instead of attempting to load `FramePlayer.dll`
inside Windows PowerShell.

## How To Run

Single file:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Run-ReviewEngine-ManualTests.ps1 `
  -Path "C:\videos\sample.mp4" `
  -Output ".\artifacts\review-engine-tests" `
  -Configuration Debug
```

Multiple explicit files:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Run-ReviewEngine-ManualTests.ps1 `
  -Path "C:\videos\a.mp4","C:\videos\b.mkv" `
  -Output ".\artifacts\review-engine-tests" `
  -Configuration Debug
```

Folder input:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Run-ReviewEngine-ManualTests.ps1 `
  -Path "C:\video-library" `
  -Recurse `
  -Output ".\artifacts\review-engine-tests" `
  -Configuration Debug
```

Local supported-format corpus:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Run-ReviewEngine-ManualTests.ps1 `
  -Path "C:\Projects\Video Test Files" `
  -Recurse `
  -Output ".\artifacts\review-engine-tests\full-corpus" `
  -Configuration Release
```

## Supported Input Modes

- A single file path
- Multiple file paths passed to `-Path`
- A folder path
- A folder path plus `-Recurse` to include subdirectories

When a folder is supplied, the script collects these extensions by default:

- `.mp4`
- `.mkv`
- `.avi`
- `.wmv`
- `.m4v`

The extension filter can be adjusted with `-IncludeExtensions`.

## Standard Per-File Test Sequence

Each file uses one deterministic custom-engine sequence:

1. Open the file and capture initial metadata / first-frame state.
2. Start playback briefly, then pause.
3. Capture whether audio was present, whether audio playback initialized, whether audio bytes were submitted, and whether the audio clock was used.
4. Seek to 25% of duration when duration is known and long enough.
5. If duration is unavailable or the clip is too short, clamp the seek-to-time target to the start position.
6. Seek to a frame target from the custom engine's global-index midpoint when available.
7. If a reliable global index is not available during planning, fall back to a duration/fps-based midpoint estimate.
8. If neither index nor duration/fps is reliable, fall back to frame `0`.
9. Step backward once.
10. Step forward once.

This keeps the sequence simple, repeatable, and less likely to hit clip-end behavior unnecessarily.

## Output Files

The script writes three files into the `-Output` directory:

- `review-engine-manual-tests.json`
  Full structured report with per-file plan details, custom-engine outcomes, notes, warnings, and metrics.
- `review-engine-manual-tests.csv`
  Granular rows for each operation in the standard sequence plus separate advisory, warning, and failure rows that were not already represented by an operation result.
- `review-engine-manual-tests-summary.md`
  Human-readable summary with scenario totals, operation totals, a per-file operation matrix, and explicit warning/failure/advisory sections.

The script also prints a short console summary with result-row totals, operation-row totals, pass/warning/fail/advisory counts, and the output paths.

## Supplemental UI Regression Checks

The scripted manual runner is review-engine focused. The app-driven regression suite now covers audio insertion, crop-aware zoom/export, linked compare zoom, and selected compare-mode command states; keep these manual checks as visible UI confidence checks before release:

- Single-pane `Audio Insertion > Replace Audio Track...` with both `.wav` and `.mp3` replacement audio on an H.264 `.mp4`.
- Disabled audio insertion coverage for two-pane compare, non-`.mp4` sources, non-H.264 sources, and unknown-codec cases.
- Paused pane-local zoom and pan in single-pane review, including play/pause/seek/frame-step persistence plus `Playback > Reset Zoom`.
- Linked compare zoom defaults, independent paused zoom and pan on both panes after disabling `Link Zoom`, and pane-context `Reset Zoom`.
- Zoom-aware pointer readout validation in the status bar.
- Crop-aware loop clip export and crop-aware side-by-side compare export, ensuring zoom changes rendered pixels only and does not change loop timing, whole-video alignment, or audio-source rules.

## Result Classification

- `pass`
  All required operations completed, and any plan-time fallback stayed advisory rather than turning into a degraded runtime result.
- `warning`
  The sequence completed, but the runtime result or the exercised coverage was materially reduced.
- `fail`
  One or more required operations did not complete successfully.
- `advisory`
  Planning or preflight context that should stay visible in the report but should not be mistaken for a runtime failure.

Common warning cases:

- Preflight planning failed and the runner had to fall back to conservative defaults.
- Duration was unavailable, or the clip was short enough that seek-to-time had to clamp to the start position.
- Frame-target planning had to fall back all the way to frame `0`.
- Seek-to-frame did not retain absolute frame identity.
- Seek-to-time still lacked absolute frame identity in a case where the runner should have been able to recover it.
- A very short clip caused step operations to hit a boundary during the reduced test path.

Planning-only advisories remain visible in the JSON report and are also emitted as explicit `plan-advisory` rows in the CSV and markdown summary even when the backend scenario classification remains `pass`.

## Known Limitations

- The custom FFmpeg engine builds the global frame index in the background after first-frame open. Manual batch runs still pay that full-file scan cost per custom-engine open, but it is no longer intended to block first paint.
- The manual runner performs a custom-engine preflight open per file to derive a deterministic plan, then runs the planned custom-engine sequence. That means the custom path is exercised more than once per file during testing.
- This harness is for deterministic review diagnostics, not playback benchmarking.
- Audio playback is MVP-scoped: the engine decodes audio through FFmpeg, resamples it to stereo 16-bit PCM, and sends it to Windows `waveOut`. It does not yet provide audio device selection, volume controls, advanced drift correction, hardware acceleration, or audio/video benchmarking.
- Video-only files are expected to pass when the video review operations succeed; missing audio is not treated as a failure.

## Interpreting Warnings Vs Failures

- Treat `warning` as "sequence ran, but inspect the notes before trusting the result."
- Treat `fail` as "the custom engine could not complete the planned review sequence for this file."
