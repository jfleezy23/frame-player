# Unified Preview 0.2.0 Windows Validation

This note records the Windows-side validation evidence for PR #65 so the macOS developer can review and accept the unified preview handoff.

## Scope

- PR: https://github.com/jfleezy23/frame-player/pull/65
- Branch: `codex/unified-avalonia-preview-0.2.0`
- Latest product head checked: `e3e36d73ced245aa3c6a8c03d04360f85181247b`
- Last fully passing Windows validation head: `41d60c703872737313ba949da81d56b8850f8e76`
- Earlier heads observed during validation: `3e9b45bd9b62ccc35e5e1ea570d15132fe2aa489`, `754dfa82df8fb1cbfcfc0581e5e4a7184a20daf0`
- Date: 2026-05-04
- Decision: NO-GO for the latest PR head until the playback-after-seek regression is addressed and revalidated. The Windows package built from `41d60c703872737313ba949da81d56b8850f8e76` had a passing smoke, but that result is superseded by the later audio-path commit `e3e36d73ced245aa3c6a8c03d04360f85181247b`.

No product code changes were made by this Windows validation pass.

## Environment

- Windows: `Microsoft Windows NT 10.0.26200.0`
- OS architecture: `X64`
- Runtime description: `Microsoft Windows 10.0.26200`
- .NET SDK: `10.0.203`
- RID: `win-x64`

## CI

GitHub PR checks were green for `41d60c703872737313ba949da81d56b8850f8e76`. After the branch advanced to `e3e36d73ced245aa3c6a8c03d04360f85181247b`, a recheck on 2026-05-04 showed `submit-nuget` still pending while the listed analysis/build checks had passed.

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
4229bdf6ea2df7506f13697e0031be18b24a0e7b69fe43254c8d914fbce8652f
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

Latest-head smoke result:

- Playback after seek: fail on `e3e36d73ced245aa3c6a8c03d04360f85181247b`. While playback was active, clicking the timeline slider around 35% moved the position to `00:00:06.986` / `Frame 168`, changed state to `Paused`, and remained paused for at least 3 seconds.

Previous fully passing smoke results from `41d60c703872737313ba949da81d56b8850f8e76`:

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
- Peak during playback: `0.4378`
- Peak after seek while playback continued: `0.4214`
- This confirms Windows produced a nonzero audible output signal through the default render endpoint during AAC playback and after seek on `41d60c703872737313ba949da81d56b8850f8e76`. No subjective human-ear listening note was captured separately.

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
- `FramePlayer.Avalonia.Tests`: passed, 26/26.
- `FramePlayer.Desktop.Tests`: passed, 32/32.
- `FramePlayer.Avalonia.Tests --filter WindowsAudioOutputContractTests`: passed, 3/3.
- `FramePlayer.Desktop.Tests --filter WindowsAudioOutputContractTests`: passed, 3/3.

## Notes for Acceptance

- Do not accept the current Windows package from `e3e36d73ced245aa3c6a8c03d04360f85181247b` until the playback-after-seek failure is fixed or explained and revalidated.
- The previously passing Windows artifact was built from `41d60c703872737313ba949da81d56b8850f8e76`; do not treat that as acceptance for the later audio-path commit.
- The PR branch advanced during validation, so release acceptance must use the head SHA and SHA256 from the final revalidation pass.
- The validation pass did not merge PR #65 and did not publish a release.
- If release acceptance requires subjective listening through speakers or headphones, have a human tester perform that final listening check. The automated Windows output-meter evidence confirms the app produced nonzero audio through the default render endpoint.
