# Validation

## macOS Preview 0.1.0

The Apple Silicon macOS Preview was validated as a controlled preview release on 2026-05-02.

Recorded validation evidence:

- `dotnet build src/FramePlayer.Mac/FramePlayer.Mac.csproj -c Release`: passed.
- `dotnet test tests/FramePlayer.Mac.Tests/FramePlayer.Mac.Tests.csproj -c Release --filter "Category!=ReleaseCandidate"`: passed, 46 tests.
- `FRAMEPLAYER_MAC_CORPUS="Video Test Files" script/validate_macos_release_candidate.sh --corpus "Video Test Files"`: passed, 51 tests against 10 supported corpus media files.
- GitHub PR checks for the macOS preview passed, including Windows CI, macOS Avalonia CI, Dependency Review, CodeQL, and SonarCloud.
- The published ZIP was Developer ID signed, notarized, stapled, and accepted by Gatekeeper.

Published macOS Preview details:

- Artifact: `FramePlayer-macOS-arm64-macos-preview-0.1.0.zip`
- SHA256: `81d3cfc15030e1040f658b9fd0e85755c35b461f4f3515f28f5a130a0bf87728`
- Notarization submission: `9f727885-b388-4ac0-a988-e10378933a32`

## Windows Avalonia Preview 0.1.0

The Windows Avalonia Preview was validated as a controlled preview release on 2026-05-04.

Recorded validation evidence:

- `dotnet build src\FramePlayer.Desktop\FramePlayer.Desktop.csproj -c Release`: passed.
- `dotnet test tests\FramePlayer.Desktop.Tests\FramePlayer.Desktop.Tests.csproj -c Release`: passed, 30 tests.
- `dotnet test tests\FramePlayer.Core.Tests\FramePlayer.Core.Tests.csproj -c Release`: passed, 37 tests.
- `dotnet build FramePlayer.csproj -c Release -p:Platform=x64`: passed, guarding the stable WPF build.
- Self-contained Windows x64 publish of `src\FramePlayer.Desktop`: passed.
- Published ZIP contents were verified for `FramePlayer.Desktop.exe`, app icon, playback DLLs, export runtime DLLs, runtime manifests, license, and third-party notices.
- Packaged launch smoke from the published folder: passed.
- GitHub PR checks for PR #60 passed, including Windows CI, macOS Avalonia CI, Dependency Review, CodeQL, and SonarCloud.
- `@codex review`: no major issues on the final requested review pass.

Published Windows Avalonia Preview details:

- Artifact: `FramePlayer-Desktop-Windows-x64-avalonia-windows-preview-0.1.0.zip`
- SHA256: `7e3f19e2f16dd752e6424d6679405f63a490b78a822c729e3280e1f50c87469b`
- Known limitation: audible Windows audio output is not implemented in the shared `src/FramePlayer.Engine.FFmpeg` path yet, so Play/Play-Pause is gated for media with audio streams in this preview.

## Windows Stable

Windows stable remains `v1.8.4`. Its WPF source path, build path, tests, runtime bootstrap, and release process are intentionally separate from the macOS Preview and Windows Avalonia Preview.

## Screenshot Validation

The checked-in screenshots in `docs/assets/screenshots/` are real app screenshots. The macOS captures were refreshed from the installed app during this documentation pass. The Windows captures were refreshed from the built WPF `v1.8.4` app surface at 1280x820. No loaded-video Windows screenshots were available locally for this pass.
