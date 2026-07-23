# Release Checklist

This is the ship gate for the single Avalonia application. Run platform packaging from the same validated commit.

## Repository gate

- Confirm the intended commit is on `main`, pushed, and the working tree is clean.
- Confirm version labels, release notes, runtime manifests, and third-party notices agree.
- Confirm no generated runtimes, signing material, local corpus files, `bin/`, `obj/`, `dist/`, or `artifacts/` outputs are staged.
- Review the exact diff and resolve security, dependency, CI, and static-analysis findings.

## Build and test gate

```powershell
dotnet build .\src\FramePlayer.Avalonia\FramePlayer.Avalonia.csproj -c Release
dotnet test .\tests\FramePlayer.Core.Tests\FramePlayer.Core.Tests.csproj -c Release
dotnet test .\tests\FramePlayer.Avalonia.Tests\FramePlayer.Avalonia.Tests.csproj -c Release --filter "Category!=ReleaseCandidate"
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-RepoHarnessScripts.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-WorkflowActionPinning.ps1
```

- Run the applicable release-candidate corpus suite on each target platform.
- Perform the transport, loop, compare, export, audio, zoom, and runtime checks in `TESTING_NOTES.md`.
- Treat missing required corpus/runtime inputs as a failed release gate, not a skipped success.

## Windows package gate

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Package-UnifiedWindows.ps1 -Version 2.1.0
```

- Verify the ZIP and SHA256 output.
- Verify `FramePlayer.Avalonia.exe`, the Rust probe library, the pinned playback runtime, and `ffmpeg-export` runtime are present.
- Verify `ffmpeg.exe`, `ffprobe.exe`, and `ffmpeg-tools` are absent.
- Sign all distributable executables and libraries with the approved signing identity, then verify every signature before recreating the ZIP and checksum.
- If producing MSIX, use `Packaging/MSIX/build-msix.ps1` only with approved package identity and signing inputs.

## macOS package gate

```bash
PACKAGE_VERSION=2.1.0 script/package_unified_macos_release.sh --sign "Developer ID Application: <Team Name> (<TEAMID>)"
codesign --verify --strict --deep --verbose=2 "dist/Frame Player.app"
codesign -dvvv --entitlements :- "dist/Frame Player.app"
```

- Confirm the bundle executable is `Contents/MacOS/FramePlayer.Avalonia`, the identifier is `com.frameplayer`, and the required FFmpeg/Rust libraries are present.
- Submit a `ditto -c -k --keepParent` archive to Apple notarization, staple the accepted ticket, and verify both `xcrun stapler validate` and `spctl -a -vvv -t exec`.
- Recreate the public ZIP from the stapled bundle and verify its checksum on a clean machine.

## Publish gate

- Tag the exact validated merge commit.
- Attach only signed, verified artifacts and their checksums.
- Confirm both target-platform packages identify the same product version and source commit.
- Download the published artifacts, verify hashes/signatures, launch them, and open a representative clip.
