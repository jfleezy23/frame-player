# Frame Player v1.5.0 Release Note

This note documents the current branch release target `v1.5.0`. It is the maintainer-facing summary of what is new on top of `v1.4.4`, what remains intentionally deferred, and what validation evidence currently backs the branch.

## What Is New In v1.5.0

- Exact A/B loop playback on the main transport:
  - `Playback > Loop Playback` still enables looping
  - `[` sets loop-in
  - `]` sets loop-out
  - `Playback > Clear Loop Points` clears the current loop context
- Frames-first loop honesty:
  - pending markers stay visibly pending
  - invalid `out < in` ranges stay visibly invalid
  - A/B repeat does not start until exact frame identity is proven
- Timeline loop visualization:
  - the shared main timeline now greys out content outside the boxed A/B range
  - visible `[` and `]` markers are rendered on the loop box
- Compare-mode pane-local loop support:
  - the Primary and Compare sliders can each carry independent pane-local loop boxes
  - pane-local loop status is shown directly in the pane footer
- Expanded automated coverage:
  - smoke checks now cover loop-marker honesty, shared A/B rendering, loop entry seek, bounded loop playback, and pane-local loop independence
  - the broader regression suite can now carry these loop checks without being a one-off harness

## What Stays Deferred

- No trimming or export feature ships in `v1.5.0`.
- The loop-range model is intentionally shaped to feed future trimming/export work, but there is no clip-save UI or ffmpeg-driven export path yet.
- No pane-local timed playback choreography is automated beyond pane-local loop-box rendering/independence checks in the hidden-window regression harness. That remains a future compare-focused proof area.

## Runtime And CI Truth

- Product version target on this branch: `v1.5.0`
- Current published clean-runner bootstrap asset: `v1.4.4`
- `Runtime\runtime-manifest.json` remains pinned to the verified `v1.4.4` runtime asset until the `v1.5.0` release assets are actually published and hash-verified.
- Do not retarget the manifest to `v1.5.0` before the runtime archive is live on the release.

## Validation Evidence

Last-known green validation runs for this branch:

- Loop smoke, default backend:
  - files tested: `2`
  - checks run: `83`
  - pass / warning / fail: `79 / 4 / 0`
- Loop smoke, forced CPU:
  - files tested: `2`
  - checks run: `83`
  - pass / warning / fail: `79 / 4 / 0`
- Full corpus regression, default backend:
  - files tested: `15`
  - checks run: `558`
  - pass / warning / fail: `523 / 35 / 0`
- Full corpus regression, forced CPU:
  - files tested: `15`
  - checks run: `558`
  - pass / warning / fail: `523 / 35 / 0`

Known non-blocking warnings remain the same honest-state warnings already accepted on the current line:

- pre-index seek can land on time before absolute frame identity is ready
- the UI withholds a numeric frame claim in that pending window
- hidden-window UI playback is still skipped for some audio-bearing corpus cases while engine-level playback/audio checks still run

## Exact Commands Used

```powershell
.\scripts\Run-RegressionSuite.ps1 -Path "C:\Projects\Video Test Files" -Recurse -Output ".\artifacts\regression-loop-phase2-full" -Configuration Release

$env:FRAMEPLAYER_GPU_BACKEND="disabled"
.\scripts\Run-RegressionSuite.ps1 -Path "C:\Projects\Video Test Files" -Recurse -Output ".\artifacts\regression-loop-phase2-full-cpu" -Configuration Release
Remove-Item Env:FRAMEPLAYER_GPU_BACKEND
```

Focused loop smoke:

```powershell
.\scripts\Run-RegressionSuite.ps1 -Path @(".\dist\Frame Player\sample-test.mp4", "C:\Projects\Video Test Files\Audio_Video_Sync_23,98_HEVC_1080p-by_PhotoJoseph.mov") -Output ".\artifacts\regression-loop-phase2-smoke" -Configuration Release

$env:FRAMEPLAYER_GPU_BACKEND="disabled"
.\scripts\Run-RegressionSuite.ps1 -Path @(".\dist\Frame Player\sample-test.mp4", "C:\Projects\Video Test Files\Audio_Video_Sync_23,98_HEVC_1080p-by_PhotoJoseph.mov") -Output ".\artifacts\regression-loop-phase2-smoke-cpu" -Configuration Release
Remove-Item Env:FRAMEPLAYER_GPU_BACKEND
```

## Release Guidance

- Treat `v1.5.0` as a minor feature release on top of `v1.4.4`.
- Keep `Properties\AssemblyInfo.cs` as the canonical version source.
- Keep the new loop smoke checks in the broader regression suite; they are stable enough to stay wired into normal validation.
- During actual release publication:
  - publish the new `FramePlayer-CustomFFmpeg-1.5.0.zip`
  - publish `FramePlayer-ffmpeg-runtime-x64.zip` on the `v1.5.0` release
  - only then retarget `Runtime\runtime-manifest.json` and CI/bootstrap references to the `v1.5.0` asset in the same change or release cut
