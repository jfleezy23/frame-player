# Release Checklist

This checklist is the maintainer-facing ship gate for the current release line.

Use it after feature work is done and before building or publishing release artifacts.

The Windows checklist below remains the ship gate for the WPF `v1.8.x` release line. The macOS preview checklist later in this file is additive and must not replace or modify the Windows release process.

## Pre-Ship Repo Hygiene

- Ensure the intended release commit is on `main` and pushed to `origin/main`.
- Confirm the working tree is clean before tagging or packaging.
- Confirm `Properties\AssemblyInfo.cs` and `src\FramePlayer.Controls\Properties\AssemblyInfo.cs` carry the intended product version.
- Confirm the current release note and README point at the same release document.
- Confirm `TESTING_NOTES.md` reflects the active release line and current validation expectations.
- If AI review is desired for the release, request Codex review manually on the release-prep PR and record the result before tagging.

## Validation Gate

- Run repository harness syntax validation:
  - `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-RepoHarnessScripts.ps1`
- Run the Release build path without publishing:
  - `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-FramePlayer.ps1 -Configuration Release`
- Run the review-engine sweep against the supported local corpus:
  - `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Run-ReviewEngine-ManualTests.ps1 -Path "C:\Projects\Video Test Files" -Recurse -Configuration Release -Output ".\artifacts\review-engine-manual-tests-full"`
- Run the packaged regression suite against the full supported local corpus:
  - `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Run-RegressionSuite.ps1 -CorpusPath "C:\Projects\Video Test Files" -Recurse -MaxCorpusFiles 0 -Configuration Release -Output ".\artifacts\regression-suite-full"`
- Review warnings before ship and confirm they are limited to known non-blocking frames-first pending-index or tiny-clip coverage cases.

## Distribution Gate

Do not run these until the repo state and validation evidence above are accepted.

- Build the release verification output:
  - `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-TestDrop.ps1 -Configuration Release -Platform x64 -RequireExportTools`
- Confirm the packaged output carries the `ffmpeg-export` DLL runtime and does not contain `ffmpeg.exe`, `ffprobe.exe`, or an `ffmpeg-tools` directory.
- Build the signed install artifact using the maintained signing flow and organization-approved signing inputs.
- Verify the produced artifact names match the intended product version.
- Prefer the signed install artifact for real deployment; treat loose-file test outputs as validation artifacts only when necessary.

## Publish Gate

- Create the release tag from the validated `main` commit.
- Publish the release with the current release note content or a summary derived from it.
- Attach the intended artifacts only after version names, signatures, and hashes are verified.
- If runtime/tooling manifests changed, confirm the recorded hashes and provenance notes were updated in the same release.
- Confirm any manually requested Codex review findings for the release-prep PR were accepted, resolved, or explicitly deferred.

## Post-Ship Smoke

- Confirm the published release points at the expected commit and tag.
- Confirm the release assets download and unpack/install cleanly.
- Confirm the shipped app starts and opens a representative clip on a clean-ish machine.
- Confirm the release note linked from `README.md` is the note you intended to ship.

## macOS Preview Checklist

Use this section only for the macOS Avalonia preview release line. Do not use it to publish or modify Windows WPF artifacts.

### Pre-Ship Repo Hygiene

- Confirm the release branch was created from the current `origin/main`.
- Confirm `git diff --name-status origin/main...HEAD` contains no changes to `FramePlayer.csproj`, `MainWindow.xaml`, root `Engines/FFmpeg/**`, root `Services/**`, `tests/FramePlayer.Core.Tests/**`, or Windows packaging/release scripts.
- Confirm `Video Test Files/`, `dist/`, `artifacts/`, `bin/`, `obj/`, and macOS `.dylib` files are not staged.
- Confirm any temporary duplicated Mac-owned engine/export code is documented as preview isolation rather than a Windows-path refactor.

### Validation Gate

- Build the macOS app:
  - `dotnet build src/FramePlayer.Mac/FramePlayer.Mac.csproj -c Release`
- Run ordinary Mac CI tests:
  - `dotnet test tests/FramePlayer.Mac.Tests/FramePlayer.Mac.Tests.csproj -c Release --filter "Category!=ReleaseCandidate"`
- Run full release-candidate corpus validation with the real local corpus:
  - `FRAMEPLAYER_MAC_CORPUS="Video Test Files" script/validate_macos_release_candidate.sh --corpus "Video Test Files"`
- Confirm the validation output lists the expected corpus file count and writes a passing TRX under `artifacts/macos-release-candidate/results/`.

### Review And CI Gate

- Confirm Windows CI remains green.
- Confirm Dependency Review is green.
- Confirm SonarQube is green, or explicitly skipped because `SONAR_TOKEN` is absent.
- Confirm macOS Avalonia CI is green.
- Address every review, CI, and Sonar comment before merge.

### Distribution Gate

- Build a signed RC ZIP for tester distribution:
  - `script/package_macos_release.sh --sign`
- Verify the app signature:
  - `codesign --verify --deep --verbose=2 "dist/Frame Player.app"`
- Verify entitlements:
  - `codesign -dvvv --entitlements :- "dist/Frame Player.app"`
- Treat Apple Development signatures as local/test signatures only. Developer ID Application signing and notarization are required before public distribution.

### Publish Gate

- Tag the validated merge commit with a macOS-only preview tag such as `macos-preview-0.1.0`.
- Create a GitHub prerelease titled `Frame Player macOS Preview 0.1.0`.
- Attach only the notarized/stapled macOS ZIP and its SHA256 file.
- Confirm the release ZIP was produced with `ditto -c -k --keepParent` so detached .NET assembly signatures are preserved.
- Extract the final ZIP and verify `xcrun stapler validate` and `spctl -a -vvv -t exec` both pass.
- Do not attach, rename, or modify Windows release artifacts.

## Windows Avalonia Preview Checklist

Use this section only for the Windows Avalonia preview release line. Do not use it to publish or modify Windows WPF stable artifacts or macOS preview artifacts.

### Pre-Ship Repo Hygiene

- Confirm the preview branch was created from the current `origin/main`.
- Confirm `git diff --name-status origin/main...HEAD` contains no changes to root `FramePlayer.csproj`, root `MainWindow.xaml`, root `MainWindow.xaml.cs`, root `Engines/FFmpeg/**`, root `Services/**`, `src/FramePlayer.Mac/**`, `tests/FramePlayer.Mac.Tests/**`, Windows packaging scripts, macOS packaging scripts, or `Runtime/macos/**` unless explicitly authorized.
- Confirm `Video Test Files/`, `dist/`, `artifacts/`, `bin/`, `obj/`, and local runtime binaries are not staged.

### Validation Gate

- Build the Desktop preview:
  - `dotnet build src\FramePlayer.Desktop\FramePlayer.Desktop.csproj -c Release`
- Run Desktop preview tests:
  - `dotnet test tests\FramePlayer.Desktop.Tests\FramePlayer.Desktop.Tests.csproj -c Release`
- Run Core tests and the stable WPF build as guardrails:
  - `dotnet test tests\FramePlayer.Core.Tests\FramePlayer.Core.Tests.csproj -c Release`
  - `dotnet build FramePlayer.csproj -c Release -p:Platform=x64`
- Launch the Desktop preview and perform manual tester checks for open/recent files, compare review, loop/export paths, disabled states, layout stability, and diagnostics.

### Publish Gate

- Tag the validated preview commit with a Windows Avalonia-only preview tag such as `avalonia-windows-preview-0.1.0`.
- Create a GitHub prerelease titled `Frame Player Avalonia Windows Preview 0.1.0`.
- Attach the Windows x64 preview ZIP and SHA256 file to the preview release and the unified current-download release page.
- Confirm the stable `v*` release remains the latest release.
- Do not rename, remove, or replace the stable Windows WPF artifact.
### Unified Windows v2.0 Release Checklist

Use this section to package, sign, and release the modern unified Avalonia application for Windows using a self-contained ZIP and Microsoft Trusted Signing.

### Pre-Ship Repo Hygiene

- Confirm the release branch was created from the current origin/main (which now represents the unified cross-platform path).
- Ensure Azure CLI is authenticated (`az login`) with the identity that has access to the Microsoft Trusted Signing account (`frameplayersigningjflow`).
- Ensure the `dotnet sign` global tool is installed: `dotnet tool install --global sign --prerelease`.

### Validation Gate

- Build the Windows Avalonia project and run tests:
  - `dotnet build src\FramePlayer.Avalonia\FramePlayer.Avalonia.csproj -c Release`
  - `dotnet test tests\FramePlayer.Avalonia.Tests\FramePlayer.Avalonia.Tests.csproj -c Release`

### Distribution & Signing Gate

- Build the self-contained Windows portable package (do not use MSIX):
  - `pwsh -ExecutionPolicy Bypass -File scripts\Package-UnifiedWindowsPreview.ps1 -Version "2.0.0.0"`
- Sign all payload binaries (`.exe` and `.dll`) inside the publish directory using the `dotnet sign code artifact-signing` provider before distributing the ZIP. Move into the publish directory to avoid pathing errors:
  - `cd "artifacts\2.0.0\windows-publish"`
  - `sign code artifact-signing "FramePlayer.Avalonia.exe" "*.dll" "ffmpeg-export\*.dll" --artifact-signing-endpoint "https://wus2.codesigning.azure.net/" --artifact-signing-account "frameplayersigningjflow" --artifact-signing-certificate-profile "frameplayerpublic"`
- Verify the final production signature on the main executable:
  - `Get-AuthenticodeSignature "FramePlayer.Avalonia.exe" | Format-List`
  - Ensure the Status is Valid and the Issuer reflects the Microsoft Trusted Signing CA (e.g., Microsoft ID Verified CS AOC CA).
- Re-zip the fully signed binaries and generate the SHA256 checksum:
  - `cd ..\..\..`
  - `Remove-Item -Force "artifacts\2.0.0\FramePlayer-Windows-x64-2.0.0.zip", "artifacts\2.0.0\FramePlayer-Windows-x64-2.0.0.zip.sha256" -ErrorAction SilentlyContinue`
  - `Compress-Archive -Path "artifacts\2.0.0\windows-publish\*" -DestinationPath "artifacts\2.0.0\FramePlayer-Windows-x64-2.0.0.zip" -Force`
  - `$hash = Get-FileHash -Algorithm SHA256 "artifacts\2.0.0\FramePlayer-Windows-x64-2.0.0.zip"`
  - `"$($hash.Hash.ToLowerInvariant())  FramePlayer-Windows-x64-2.0.0.zip" | Set-Content -NoNewline -Encoding ascii "artifacts\2.0.0\FramePlayer-Windows-x64-2.0.0.zip.sha256"`

### Publish Gate

- Colocate the signed Windows `.zip` artifact and its `.sha256` with the macOS release artifacts on the existing unified GitHub release.
  - `gh release upload v2.0.0 "artifacts\2.0.0\FramePlayer-Windows-x64-2.0.0.zip" "artifacts\2.0.0\FramePlayer-Windows-x64-2.0.0.zip.sha256"`
