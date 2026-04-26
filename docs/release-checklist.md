# Release Checklist

This checklist is the maintainer-facing ship gate for the current release line.

Use it after feature work is done and before building or publishing release artifacts.

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
