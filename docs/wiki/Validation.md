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

## Windows Stable

Windows stable remains `v1.8.4`. Its WPF source path, build path, tests, runtime bootstrap, and release process are intentionally separate from the macOS Preview.

## Screenshot Validation

The checked-in screenshots in `docs/assets/screenshots/` are real app screenshots. The macOS captures were refreshed from the installed app during this documentation pass. The Windows captures were taken from clean existing Windows app screenshots in the developer folder. No loaded-video Windows screenshots were available locally for this pass.
