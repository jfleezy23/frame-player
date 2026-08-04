# macOS Release Process

The macOS package is built from `src/FramePlayer.Avalonia`, the same application project used on every supported operating system. The package scripts select the native host runtime (`osx-arm64` or `osx-x64`) by default.

## Build and test

Hosted CI currently restores the Apple Silicon runtime from `Runtime/macos/osx-arm64/ffmpeg-runtime-manifest.json` and validates the archive, every dylib, and the provenance record before building. Intel packaging is local-validation-only until `Runtime/macos/osx-x64/ffmpeg-runtime-manifest.json` pins a separately published Intel runtime archive.

For local validation, stage the pinned FFmpeg runtime under `Runtime/macos/<rid>/ffmpeg`, build the matching Rust probe, then run:

```bash
scripts/Build-RustFfmpegProbe.sh osx-x64 # use osx-arm64 on Apple Silicon
APP_RUNTIME_IDENTIFIER=osx-x64 dotnet build src/FramePlayer.Avalonia/FramePlayer.Avalonia.csproj -c Release -r osx-x64
dotnet test tests/FramePlayer.Core.Tests/FramePlayer.Core.Tests.csproj -c Release
dotnet test tests/FramePlayer.Avalonia.Tests/FramePlayer.Avalonia.Tests.csproj -c Release --filter "Category!=ReleaseCandidate"
```

For release-candidate corpus validation, run:

```bash
PACKAGE_VERSION="<release-version>" MAC_RUNTIME_IDENTIFIER=osx-x64 script/validate_macos_release_candidate.sh --corpus "Video Test Files"
```

The validator builds the Avalonia bundle, requires the maintained corpus, verifies native runtime files, and runs the `Category=ReleaseCandidate` tests through the packaged application/export host.

The validation never substitutes downloaded sample media for the maintained release corpus.

## Build the package

For an unsigned local package:

```bash
PACKAGE_VERSION="<release-version>" MAC_RUNTIME_IDENTIFIER=osx-x64 script/package_unified_macos_release.sh --unsigned
```

For a signed release candidate:

```bash
PACKAGE_VERSION="<release-version>" script/package_unified_macos_release.sh --sign
codesign --verify --deep --verbose=2 "dist/Frame Player.app"
codesign -dvvv --entitlements :- "dist/Frame Player.app"
```

Automatic identity selection prefers `Developer ID Application` and then `Apple Development`. The latter is suitable only for local testing.

Replace `<release-version>` with the exact candidate or final version being packaged. The published v2.1.0 archive is release evidence, not the version input for a later candidate.

## Notarization

Public distribution requires a Developer ID Application identity, hardened-runtime signing, the maintained Avalonia entitlements, notarization, and stapling:

```bash
PACKAGE_VERSION="<release-version>" script/package_unified_macos_release.sh --sign "Developer ID Application: <Team Name> (<TEAMID>)"
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
- An Intel or universal2 package requires separately pinned and validated `osx-x64` FFmpeg and Rust artifacts before it can be shipped. The x64 signed/public gate additionally requires a checked-in, published-runtime manifest; unsigned local corpus validation does not waive that gate.
