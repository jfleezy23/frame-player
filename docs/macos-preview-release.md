# macOS Preview Release Process

This process is for the macOS Avalonia preview only. It must not change the Windows WPF `v1.8.4` source path, build path, tests, packaging, or release artifacts.

## Local Build And Test

Stage the pinned macOS FFmpeg runtime locally under `Runtime/macos/osx-arm64/ffmpeg`, then run:

```bash
dotnet build src/FramePlayer.Mac/FramePlayer.Mac.csproj -c Release
dotnet test tests/FramePlayer.Mac.Tests/FramePlayer.Mac.Tests.csproj -c Release --filter "Category!=ReleaseCandidate"
FRAMEPLAYER_MAC_CORPUS="Video Test Files" script/validate_macos_release_candidate.sh --corpus "Video Test Files"
```

The validation script never downloads substitute videos. If the corpus is missing, it fails and asks for the real corpus path.

## Signed RC ZIP

For controlled testing before notarization:

```bash
script/package_macos_release.sh --sign
codesign --verify --deep --verbose=2 "dist/Frame Player.app"
codesign -dvvv --entitlements :- "dist/Frame Player.app"
```

`--sign` auto-selects a `Developer ID Application` identity first when present, then falls back to `Apple Development` for local testing. Apple Development signatures are not accepted by Gatekeeper for public distribution.

## Developer ID Notarization

Notarization starts only after PR review, CI, Sonar, and full corpus validation are green.

Prerequisites:

- install a `Developer ID Application` certificate in the local keychain or CI signing keychain
- store notary credentials with `xcrun notarytool store-credentials`, or configure CI secrets for notary submission
- sign with hardened runtime and the minimum required entitlement: `com.apple.security.cs.allow-jit`

Validation and submission:

```bash
script/package_macos_release.sh --sign "Developer ID Application: <Team Name> (<TEAMID>)"
codesign --verify --strict --deep --verbose=2 "dist/Frame Player.app"
codesign -dvvv --entitlements :- "dist/Frame Player.app"
spctl -a -vvv -t exec "dist/Frame Player.app"
ditto -c -k --keepParent "dist/Frame Player.app" "artifacts/macos-release-candidate/FramePlayer-macOS-notary-submit.zip"
xcrun notarytool submit "artifacts/macos-release-candidate/FramePlayer-macOS-notary-submit.zip" --wait --keychain-profile "<profile>"
xcrun stapler staple "dist/Frame Player.app"
spctl -a -vvv -t exec "dist/Frame Player.app"
```

Do not strip AppleDouble metadata or extended attributes from the notarization or release ZIP. Several .NET assemblies are signed as nested code with detached signatures; `ditto -c -k --keepParent` preserves those signatures for Apple notarization and Gatekeeper validation.

After stapling, re-zip the stapled app and publish only after Gatekeeper accepts it on a clean machine.

## Repo Hygiene

- Do not stage `Video Test Files/`, `dist/`, `artifacts/`, `bin/`, `obj/`, `.dylib`, `.pfx`, `.cer`, or local signing outputs.
- Do not change `FramePlayer.csproj`, `MainWindow.xaml`, root `Engines/FFmpeg/**`, root `Services/**`, `tests/FramePlayer.Core.Tests/**`, or Windows packaging/release scripts for this preview.
- Keep macOS CI and release scripts under Mac-owned workflow/script paths.
- Keep runtime provenance and third-party notices in the same PR as any runtime change.
