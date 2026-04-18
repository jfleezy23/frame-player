# Deep Review and Hardening Report

Date: 2026-04-18

Scope:
- full repository review across app code, FFmpeg/runtime trust boundaries, export tooling, packaging, workflows, and maintainer documentation

Guardrail:
- frame-first correctness and playback responsiveness were treated as non-negotiable; no decode, seek, step, cache, or presentation-path behavior was intentionally changed in this pass

## Summary
- No confirmed blocking security findings remain after this pass.
- This pass only landed zero-runtime-cost and cold-path hardening:
  - pinned floating GitHub Action references to immutable SHAs and disabled persisted checkout credentials
  - added manifest filename validation to runtime/export-tool integrity checks
  - enforced HTTPS-only download/timestamp URLs in bootstrap and packaging scripts
  - hardened FFmpeg CLI export/probe process handling to avoid redirected-output stalls on failure paths
  - moved the manual review-engine PowerShell harness onto a headless app CLI path so it now runs under Frame Player's own runtime
  - added cold-path unit coverage and CI checks that keep the existing PowerShell harness carried in-repo
- The validation baseline remained clean:
  - Release `x64` build passed
  - `dotnet list package --vulnerable --include-transitive` reported no vulnerable packages
  - runtime/export-tool bootstrap scripts passed
  - packaged test drop build passed
  - MSIX packaging passed with `-UseDevCertificate`
  - full-corpus regression and manual review-engine sweeps completed without failures

## Blocking security findings
- None confirmed after this pass.

Resolved in this pass:

1. Workflow supply-chain drift from floating action tags
   Impact:
   Workflow executions depended on movable major tags in `Windows CI`, `Dependency Review`, and `Desktop Packaging Helper`, which weakens provenance and makes rebuilds less deterministic.
   Fix:
   Pinned action references to immutable SHAs and disabled persisted checkout credentials in `.github/workflows/windows-ci.yml:19`, `.github/workflows/windows-ci.yml:21`, `.github/workflows/windows-ci.yml:24`, `.github/workflows/dependency-review.yml:15`, `.github/workflows/dependency-review.yml:17`, `.github/workflows/dependency-review.yml:20`, `.github/workflows/dotnet-desktop.yml:24`, `.github/workflows/dotnet-desktop.yml:26`, `.github/workflows/dotnet-desktop.yml:29`, `.github/workflows/dotnet-desktop.yml:43`, `.github/workflows/dotnet-desktop.yml:50`, `.github/workflows/dotnet-desktop.yml:56`, and `.github/workflows/sonarqube.yml:26`.

2. Bootstrap/package scripts trusted manifest-driven path material more broadly than needed
   Impact:
   Runtime/export-tool bootstrap relied on manifest entries for archive names and file names. The manifests are maintainer-controlled, but explicitly constraining them to leaf names reduces accidental path traversal and keeps trust boundaries crisp.
   Fix:
   Added leaf-filename validation and HTTPS URL validation in `scripts/Ensure-DevRuntime.ps1:51`, `scripts/Ensure-DevRuntime.ps1:67`, `scripts/Ensure-DevRuntime.ps1:128`, `scripts/Ensure-DevRuntime.ps1:133`, `scripts/Ensure-DevRuntime.ps1:136`, `scripts/Ensure-DevExportTools.ps1:35`, `scripts/Ensure-DevExportTools.ps1:51`, `scripts/Ensure-DevExportTools.ps1:144`, `scripts/Ensure-DevExportTools.ps1:149`, and `scripts/Ensure-DevExportTools.ps1:153`. Added HTTPS-only timestamp validation and literal-path PFX checks in `Packaging/MSIX/build-msix.ps1:42`, `Packaging/MSIX/build-msix.ps1:148`, and `Packaging/MSIX/build-msix.ps1:238`.

3. FFmpeg CLI export/probe helper could stall on redirected output buffers
   Impact:
   `RunProcess` previously consumed stdout and stderr serially. FFmpeg failure paths can emit enough redirected output to block if one buffer fills before the other is drained, turning a cold-path export/probe failure into a hang.
   Fix:
   Added executable/working-directory validation and concurrent redirected stream draining in `Services/FfmpegCliTooling.cs:42` and `Services/FfmpegCliTooling.cs:83`.

## Cold-path hardening opportunities
- Runtime and export-tool manifest validation now rejects non-leaf file entries before hashing begins, in `Services/RuntimeManifestService.cs:34`, `Services/RuntimeManifestService.cs:36`, `Services/RuntimeManifestService.cs:95`, `Services/ExportToolsManifestService.cs:34`, `Services/ExportToolsManifestService.cs:36`, and `Services/ExportToolsManifestService.cs:95`.
- The repository-carried PowerShell harness remains the product-level validation source of truth, and CI now has a dedicated syntax/presence check to keep those scripts from silently drifting or disappearing.
- `scripts/Run-ReviewEngine-ManualTests.ps1` no longer depends on loading app assemblies inside Windows PowerShell; it now hands execution off to a headless app entrypoint that uses the same runtime/configuration path as the desktop app.
- A remaining low-risk follow-up is to add more focused unit coverage around export-plan construction and other service-level cold paths that do not require live playback.

## Critical-path risks or rejected hardening ideas
- Rejected:
  Revalidate FFmpeg runtime hashes on every media open, seek, or step.
  Reason:
  That would add repeated filesystem I/O to frame-review flows for little practical gain because the runtime is already validated at startup and before packaging/bootstrap restore paths.
- Rejected:
  Add extra hot-path logging around decode, presentation, cache refill, or audio timing.
  Reason:
  It would risk UI jitter and timing noise in the exact places Frame Player is supposed to stay lean.
- Rejected:
  Wrap FFmpeg interop behind new security abstraction layers without a concrete flaw.
  Reason:
  That would add structural complexity to critical-path code without proving a correctness or security payoff.
- Deferred:
  Project-wide nullable enablement and unsafe-scope narrowing.
  Reason:
  They are worthwhile long-term hygiene goals, but changing them blindly across performance-sensitive code would create a large regression surface before targeted tests and hot-path measurements are in place.

## Code cleanliness and documentation gaps
1. `MainWindow.xaml.cs` remains a very large controller/code-behind file at roughly 7,347 lines.
   Why it matters:
   Reviewability, fault isolation, and documentation density are all low when UI coordination, command handling, diagnostics export, and workflow control live in one file.

2. `Diagnostics/RegressionSuiteRunner.cs` remains a very large orchestration file at roughly 5,131 lines.
   Why it matters:
   This concentrates packaging checks, runtime checks, and review-engine exercise logic into one place that is hard to validate incrementally.

3. `Engines/FFmpeg/FfmpegReviewEngine.cs` remains a large performance-sensitive file at roughly 2,570 lines.
   Why it matters:
   The file is too central to refactor casually, but it is large enough that future correctness fixes will be expensive without narrower internal seams and better local documentation.

4. Project-wide nullability is still disabled in `FramePlayer.csproj:12` and `src/FramePlayer.Controls/FramePlayer.Controls.csproj:9`.
   Why it matters:
   This keeps null-safety issues as reviewer discipline rather than compiler-enforced invariants.

5. Unsafe code is still enabled at the main-project level in `FramePlayer.csproj:10`.
   Why it matters:
   FFmpeg interop genuinely needs unsafe code, but the project-wide switch keeps the trust boundary wider than ideal.

6. The repo now has a buildable cold-path test project in `tests/FramePlayer.Core.Tests`, but product behavior still depends mainly on app-driven PowerShell harness coverage rather than broad unit-test coverage.
   Why it matters:
   This is an improvement, but deeper cleanup work still needs more subsystem-level tests before nullability and structural refactors become low-risk.

7. Source-level documentation remains sparse in non-generated code.
   Why it matters:
   Most of the project knowledge lives in markdown docs and maintainer memory rather than beside the critical code paths that future fixes will touch.

## Remediation roadmap
### Batch 1: zero-runtime-cost hardening
- Keep workflow action references SHA-pinned and enforce that policy in CI so future PRs cannot silently reintroduce floating tags.
- Continue using `persist-credentials: false` for checkout steps unless a workflow truly needs write-back behavior.
- Keep packaging/bootstrap URLs HTTPS-only and keep PFX checks on literal paths.
- Keep the repository-carried PowerShell harness syntax-checked in CI so those product-validation entrypoints remain present and parseable.

### Batch 2: cold-path-only safety improvements
- Expand the new buildable test project for services/scripts-adjacent logic, starting from the manifest validation and FFmpeg CLI failure-path tests landed in this pass.
- If future export work revisits the public plan models, consider moving from raw FFmpeg command-line strings to structured argv construction internally. Do that only if interface churn is acceptable, because the current public plan models expose command strings.
- Keep any future integrity or path validation at startup/package/export boundaries only; do not move it into playback loops.

### Batch 3: documentation and maintainability cleanup with no behavior change
- Split `MainWindow.xaml.cs` by responsibility using partial classes or narrow UI services, starting with export commands, diagnostics export, and compare/export workflow handling.
- Split `Diagnostics/RegressionSuiteRunner.cs` into packaging checks, runtime checks, and engine/UI exercise slices so future audits can target them separately.
- Add concise source-adjacent documentation for:
  - runtime/bootstrap trust boundaries
  - FFmpeg CLI export/probe behavior
  - review-engine performance guardrails

### Batch 4: critical-path changes only with explicit justification
- Enable nullable annotations one subsystem at a time once test coverage exists for that subsystem.
- Narrow unsafe-code exposure only if the change can be isolated and benchmarked without disturbing frame stepping, seek determinism, or playback timing.
- Refactor `FfmpegReviewEngine` only behind a preserved validation baseline that checks open, pre-index seek, indexed seek, frame step, playback, and cache behavior.

## Validation performed
- `dotnet build .\FramePlayer.csproj -c Release -p:Platform=x64`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-WorkflowActionPinning.ps1`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-RepoHarnessScripts.ps1`
- `dotnet test .\tests\FramePlayer.Core.Tests\FramePlayer.Core.Tests.csproj -c Release`
- `dotnet list .\FramePlayer.csproj package --vulnerable --include-transitive`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Ensure-DevRuntime.ps1`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Ensure-DevExportTools.ps1 -Required`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-TestDrop.ps1 -RequireExportTools`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Run-RegressionSuite.ps1 -Path 'C:\Projects\Video Test Files' -Recurse -Output '.\artifacts\regression-suite\codex-full-corpus' -Configuration Release`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Run-ReviewEngine-ManualTests.ps1 -Path '.\dist\Frame Player\sample-test.mp4' -Output '.\artifacts\review-engine-manual-tests\codex-smoke' -Configuration Release`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Run-ReviewEngine-ManualTests.ps1 -Path 'C:\Projects\Video Test Files' -Recurse -Output '.\artifacts\review-engine-manual-tests\codex-full-corpus' -Configuration Release`
- `powershell -NoProfile -ExecutionPolicy Bypass -File .\Packaging\MSIX\build-msix.ps1 -UseDevCertificate`
