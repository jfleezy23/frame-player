# macOS Release Process

The macOS package is built from `src/FramePlayer.Avalonia`, the same application project used on every supported operating system. The checked-in runtime currently produces an Apple Silicon (`osx-arm64`) package.

## Build and test

Stage the pinned FFmpeg runtime under `Runtime/macos/osx-arm64/ffmpeg`, then run:

```bash
dotnet build src/FramePlayer.Avalonia/FramePlayer.Avalonia.csproj -c Release
dotnet test tests/FramePlayer.Core.Tests/FramePlayer.Core.Tests.csproj -c Release
dotnet test tests/FramePlayer.Avalonia.Tests/FramePlayer.Avalonia.Tests.csproj -c Release --filter "Category!=ReleaseCandidate"
```

For release-candidate corpus validation, run:

```bash
script/validate_macos_release_candidate.sh --corpus "Video Test Files"
```

The validator builds the Avalonia bundle, requires the maintained corpus, verifies native runtime files, and runs the `Category=ReleaseCandidate` tests through the packaged application/export host.

The validation never substitutes downloaded sample media for the maintained release corpus.

## Build the package

For an unsigned local package:

```bash
PACKAGE_VERSION=2.0.0-rc.1 script/package_unified_macos_release.sh --unsigned
```

For a signed release candidate:

```bash
PACKAGE_VERSION=2.0.0-rc.1 script/package_unified_macos_release.sh --sign
codesign --verify --deep --verbose=2 "dist/Frame Player.app"
codesign -dvvv --entitlements :- "dist/Frame Player.app"
```

Automatic identity selection prefers `Developer ID Application` and then `Apple Development`. The latter is suitable only for local testing.

## Notarization

Public distribution requires a Developer ID Application identity, hardened-runtime signing, the maintained Avalonia entitlements, notarization, and stapling:

```bash
script/package_unified_macos_release.sh --sign "Developer ID Application: <Team Name> (<TEAMID>)"
codesign --verify --strict --deep --verbose=2 "dist/Frame Player.app"
spctl -a -vvv -t exec "dist/Frame Player.app"
ditto -c -k --keepParent "dist/Frame Player.app" "artifacts/FramePlayer-macOS-notary-submit.zip"
xcrun notarytool submit "artifacts/FramePlayer-macOS-notary-submit.zip" --wait --keychain-profile "<profile>"
xcrun stapler staple "dist/Frame Player.app"
xcrun stapler validate "dist/Frame Player.app"
spctl -a -vvv -t exec "dist/Frame Player.app"
```

Preserve extended attributes and detached signatures by using `ditto -c -k --keepParent` for notarization and release archives.

## Repository hygiene

- Do not stage the media corpus, generated bundles, build output, generated native libraries, certificates, or signing keys.
- Keep runtime provenance, hashes, licensing notices, and release scripts in the same change as any runtime update.
- An Intel or universal2 package requires separately pinned and validated `osx-x64` FFmpeg and Rust artifacts before it can be shipped.
