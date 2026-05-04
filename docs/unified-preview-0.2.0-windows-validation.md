# Unified Preview 0.2.0 Windows Validation

This note records the Windows-side validation evidence for PR #65 so the macOS developer can review and accept the unified preview handoff.

## Scope

- PR: https://github.com/jfleezy23/frame-player/pull/65
- Branch: `codex/unified-avalonia-preview-0.2.0`
- Latest Windows packaged smoke head: `7e1128bb3d95fb24d3fc759d48c913db4119f96a`
- Playback fix head: `16e9f6e3ed09fe93b2ca8268717fc9744b787ae3`
- Earlier failing head: `8d99a0b55f86c2717542813cc5dcaf7b56283fc2`
- Date: 2026-05-04
- Decision: GO for the Windows packaged smoke. The playback-after-seek blocker reproduced on `8d99a0b55f86c2717542813cc5dcaf7b56283fc2` and was revalidated as fixed in the package built from `7e1128bb3d95fb24d3fc759d48c913db4119f96a`. Final release artifacts should still be rebuilt from the merged release head.

No product code changes were made by this Windows validation pass.

## Environment

- Windows: `Microsoft Windows NT 10.0.26200.0`
- OS architecture: `X64`
- Runtime description: `Microsoft Windows 10.0.26200`
- .NET SDK: `10.0.203`
- RID: `win-x64`

## CI

GitHub PR checks were green for `7e1128bb3d95fb24d3fc759d48c913db4119f96a` after the Windows package smoke completed. Release acceptance still requires the final PR head to pass CI, SonarCloud, dependency review, CodeQL, and Codex review before merging and rebuilding final artifacts.

- Windows CI: pass
- macOS Avalonia: pass
- Dependency Review: pass
- CodeQL Analyze actions/csharp/python: pass
- SonarQube analyze: pass
- SonarCloud Code Analysis: pass
- Automatic Dependency Submission: pass
- Standalone CodeQL check: neutral/skipped

## Package

Built with the checked-in script:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Package-UnifiedWindowsPreview.ps1
```

Artifact:

```text
C:\Projects\RPCS3\Video Player\artifacts\unified-preview-0.2.0\FramePlayer-Windows-x64-unified-preview-0.2.0.zip
```

SHA256 sidecar:

```text
C:\Projects\RPCS3\Video Player\artifacts\unified-preview-0.2.0\FramePlayer-Windows-x64-unified-preview-0.2.0.zip.sha256
```

SHA256:

```text
15a76d42933b03a374fbbc388026968964f529cc0c1b7df1b105b2ebd187a9f3
```

The ZIP was extracted to:

```text
C:\Projects\RPCS3\Video Player\artifacts\unified-preview-0.2.0\extracted-clean
```

Package layout checks passed:

- `FramePlayer.Avalonia.exe` is present.
- Playback FFmpeg DLLs are beside the executable: `avcodec-62.dll`, `avformat-62.dll`, `avutil-60.dll`, `swresample-6.dll`, `swscale-9.dll`.
- Export runtime DLLs are under `ffmpeg-export`, including `avfilter-11.dll`.
- `Runtime\runtime-manifest.json` and `Runtime\export-runtime-manifest.json` are present.
- No macOS dylibs, `.app` bundle contents, `__MACOSX`, `.DS_Store`, or other macOS artifacts were found.
- The shipped output remains DLL-only for export runtime; no `ffmpeg.exe`, `ffprobe.exe`, or `ffmpeg-tools` directory was present.

## Smoke Validation

The extracted package was launched directly from `extracted-clean\FramePlayer.Avalonia.exe`.

Primary audio/video test media:

```text
C:\Projects\RPCS3\Video Player\artifacts\manual-validation\h264_aac_audio_smoke_looped.mp4
```

This file was made by stream-looping the known-good local H.264/AAC MP4 without re-encoding. Probe result:

- Video: H.264, 1920x1080, 24000/1001 fps, 20.250833 seconds
- Audio: AAC, 48000 Hz, 2 channels, 20.251000 seconds

Latest Windows packaged smoke result:

- Launch/open/play from clean extraction: pass on `7e1128bb3d95fb24d3fc759d48c913db4119f96a`.
- Audio before seek: pass; Windows default render endpoint peak reached `0.416`.
- Playback after seek: pass; the timeline seek landed at `00:00:16.538`, stayed `Playing` after 300 ms and 1300 ms, and post-seek audio peak reached `0.4222`.
- Current-head rebuilt package also launched, opened media, and played from clean extraction with nonzero post-click audio peak `0.4375`.

Earlier fully passing smoke results from `41d60c703872737313ba949da81d56b8850f8e76`:

- Launch: pass.
- Open media through the Windows file picker: pass.
- Recent/open behavior: pass; media was closed and reopened from the first recent item.
- Transport controls: pass; play/pause worked and playback position advanced.
- Timeline seek: pass; seek worked while playing.
- Playback after seek: pass; playback remained active after seek.
- Frame step: pass; stable paused check moved `Frame 1 -> Frame 2 -> Frame 1`.
- Compare mode: pass; compare pane opened and sync controls enabled after both panes loaded.
- Context menus: pass; pane context menu exposed `Video Info...` and `Reset Zoom`; timeline context menu exposed A/B/loop items with `Save Loop As Clip...` disabled without loop markers.
- Export command state: pass; before media, export commands disabled; single-pane media kept loop and compare export disabled without required state; compare mode enabled side-by-side export after both panes loaded.
- Audio insertion command state: pass; enabled for single-pane H.264 MP4 and disabled in compare mode.
- Repeated open/close stability: pass; no hangs or crashes observed during open, close, recent reopen, compare open, seek, playback, and final shutdown.
- Windows Avalonia audio gate: pass; playback was not blocked by a Windows audio-unavailable gate.

Audio evidence:

- Windows default render endpoint baseline peak: `0`
- Peak during playback: `0.416`
- Peak after seek while playback continued: `0.4222`
- Current-head rebuilt package post-click peak: `0.4375`
- This confirms Windows produced a nonzero audible output signal through the default render endpoint during AAC playback and after seek on the accepted Windows package smoke. No subjective human-ear listening note was captured separately.

## Local Commands

PR and CI:

```powershell
git fetch origin codex/unified-avalonia-preview-0.2.0
git switch codex/unified-avalonia-preview-0.2.0
git pull --ff-only
gh pr checks 65 --repo jfleezy23/frame-player
gh pr view 65 --repo jfleezy23/frame-player --json headRefOid,statusCheckRollup
```

Packaging and ZIP validation:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Package-UnifiedWindowsPreview.ps1
Expand-Archive -LiteralPath .\artifacts\unified-preview-0.2.0\FramePlayer-Windows-x64-unified-preview-0.2.0.zip -DestinationPath .\artifacts\unified-preview-0.2.0\extracted-clean -Force
Get-FileHash -Algorithm SHA256 .\artifacts\unified-preview-0.2.0\FramePlayer-Windows-x64-unified-preview-0.2.0.zip
```

Windows CI-equivalent local checks:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-WorkflowActionPinning.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-RepoHarnessScripts.ps1
dotnet build .\FramePlayer.csproj -c Release -p:Platform=x64
dotnet build .\src\FramePlayer.Avalonia\FramePlayer.Avalonia.csproj -c Release
```

Tests:

```powershell
dotnet test .\tests\FramePlayer.Core.Tests\FramePlayer.Core.Tests.csproj -c Release
dotnet test .\tests\FramePlayer.Avalonia.Tests\FramePlayer.Avalonia.Tests.csproj -c Release
dotnet test .\tests\FramePlayer.Desktop.Tests\FramePlayer.Desktop.Tests.csproj -c Release
dotnet test .\tests\FramePlayer.Avalonia.Tests\FramePlayer.Avalonia.Tests.csproj -c Release --filter WindowsAudioOutputContractTests
dotnet test .\tests\FramePlayer.Desktop.Tests\FramePlayer.Desktop.Tests.csproj -c Release --filter WindowsAudioOutputContractTests
```

Test results:

- `FramePlayer.Core.Tests`: passed, 37/37.
- `FramePlayer.Avalonia.Tests`: passed, 27/27 on the full local matrix head; Windows audio contract quick rerun passed, 4/4, after the later UI contract addition.
- `FramePlayer.Desktop.Tests`: passed, 32/32.
- `FramePlayer.Avalonia.Tests --filter WindowsAudioOutputContractTests`: passed, 4/4.
- `FramePlayer.Desktop.Tests --filter WindowsAudioOutputContractTests`: passed, 3/3.

## Notes for Acceptance

- The Windows playback-after-seek blocker is fixed and revalidated in the package built from `7e1128bb3d95fb24d3fc759d48c913db4119f96a`.
- Later commits after that package smoke did not change the Windows playback path, but the final public ZIP must still be rebuilt from the merged release head.
- The PR branch advanced during validation, so release acceptance must use the head SHA and SHA256 from the final revalidation pass.
- The validation pass did not merge PR #65 and did not publish a release.
- If release acceptance requires subjective listening through speakers or headphones, have a human tester perform that final listening check. The automated Windows output-meter evidence confirms the app produced nonzero audio through the default render endpoint.
