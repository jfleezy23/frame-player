# Frame Player Validation Notes

Frame Player is one Avalonia application in `src/FramePlayer.Avalonia`. The same application sources are built and packaged for every supported operating system.

## Automated validation

Run the supported build and test surface from the repository root:

```powershell
dotnet build .\src\FramePlayer.Avalonia\FramePlayer.Avalonia.csproj -c Release
dotnet test .\tests\FramePlayer.Core.Tests\FramePlayer.Core.Tests.csproj -c Release
dotnet test .\tests\FramePlayer.Avalonia.Tests\FramePlayer.Avalonia.Tests.csproj -c Release --filter "Category!=ReleaseCandidate"
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-RepoHarnessScripts.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-WorkflowActionPinning.ps1
```

On Windows, build the self-contained package with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Package-UnifiedWindows.ps1 -Version 2.0.0-rc.1
```

On macOS, build the self-contained application bundle and archive with:

```bash
PACKAGE_VERSION=2.0.0-rc.1 script/package_unified_macos_release.sh --unsigned
```

Release-candidate corpus tests are tagged `Category=ReleaseCandidate` and require the platform-specific corpus environment documented in `docs/macos-release.md` or the Windows Rust corpus harness.

## Manual product checks

- Open a representative clip and confirm the first frame is displayed.
- Exercise play, pause, time seek, exact frame seek, and single-frame stepping.
- Set A/B markers and verify loop playback and loop clip export.
- Verify a supported H.264 MP4 can replace its audio with WAV and MP3 inputs.
- Open compare mode; verify synchronized and pane-local transport, independent loop ranges, linked and unlinked zoom, and side-by-side export.
- Verify pixel inspection, zoom, pan, and reset behavior while paused and across transport operations.
- Confirm a file with audio plays audio and a video-only file remains usable.
- Exercise both the normal CPU path and opportunistic Vulkan acceleration where the local runtime, codec, device, and driver support it.
- Confirm diagnostics identify the selected decode path and report actionable runtime failures.

## Runtime and export checks

- The application consumes the pinned FFmpeg DLL/dylib runtime described by `Runtime/runtime-manifest.json`.
- Export work runs through the same application executable in headless host mode and the separately staged DLL/dylib runtime described by `Runtime/export-runtime-manifest.json`.
- A shipped package must not contain `ffmpeg.exe`, `ffprobe.exe`, or an `ffmpeg-tools` directory.
- Developer-only CLI tools may be restored locally by `scripts/Ensure-DevExportTools.ps1`; they are not distribution inputs.
- Windows packages must contain `libwinpthread-1.dll` beside the FFmpeg libraries and `FramePlayer.Avalonia.exe`.

## Known limitations

- Hardware decode is opportunistic and must fall back cleanly to CPU decode.
- Hardware frames are currently read back and converted to BGRA before presentation.
- Playback has no audio-device selection, volume controls, advanced drift correction, or frame-dropping catch-up policy.
- Large files require a background full-file frame-index scan after the first frame becomes visible.
- The checked-in macOS runtime is Apple Silicon focused. An Intel or universal2 artifact requires pinned and validated `osx-x64` FFmpeg and Rust libraries.
