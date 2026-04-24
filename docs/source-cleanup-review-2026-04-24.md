# Source Cleanup and Documentation Review

Date: 2026-04-24

Branch: `codex/codebase-cleanup-docs`

Scope:
- WPF desktop application source
- custom FFmpeg review engine and frame-first contract
- export and export-side probe host boundary
- runtime/bootstrap network posture
- security-facing and maintainer-facing documentation

Guardrail:
- Frame-first correctness remains the primary design rule. This pass avoids changing decode,
  seek, frame-step, cache, playback, export, or synchronization behavior.

## Executive Summary

- No runtime telemetry, analytics SDK, auto-update path, HTTP client, socket listener, or
  background network-service code was found in the desktop app source.
- Expected outbound network activity is limited to build/developer tooling: NuGet restore,
  pinned FFmpeg artifact restore, optional official FFmpeg source clones for local source builds,
  and optional package-signing timestamp requests.
- The main cleanup gap was documentation clarity: security reviewers need an explicit distinction
  between shipped-app runtime behavior and repository build/bootstrap behavior.
- This branch adds low-risk source and markdown documentation around the frame-first contract,
  local export-host boundary, runtime trust boundaries, and network/telemetry posture.

## Changes Made In This Branch

1. Added explicit network and telemetry posture documentation to `README.md` and `SECURITY.md`.
2. Added source-adjacent documentation for the frame-first review engine contract in
   `Core/Abstractions/IVideoReviewEngine.cs` and `Core/Models/FrameDescriptor.cs`.
3. Documented that the export host is a hidden local child process using temporary JSON files,
   not a daemon, socket listener, or telemetry channel.
4. Documented the separate trust boundaries for playback runtime validation, export runtime
   validation, and developer/test export-tool validation.
5. Added a build-project comment explaining that runtime restoration is build-time bootstrap
   behavior and can be disabled for offline builds with `SkipRuntimeBootstrap=true`.

## Alignment Follow-Up

The second pass tightened wording in user-facing and source-adjacent documentation:

- Standardized current docs and comments on "frame-first" for the active design language.
- Changed broad "probe work" wording to "export-side probe work" where the text is describing
  the hidden export host, because normal media inspection can also happen in-process through
  `MediaProbeService`.
- Added NuGet restore to the expected build/developer network paths, since `NuGet.config` points
  package restore at `https://api.nuget.org/v3/index.json`.
- Clarified the Help window text so it says frame counts are shown when exact frame identity is
  available, matching the UI's pending-frame behavior during background indexing.

## Network And Telemetry Review

Search terms included:
- `http`, `https`, `telemetry`, `analytics`, `socket`, `websocket`, `webclient`, `httpclient`
- `dns`, `tcpclient`, `udpclient`, `upload`, `download`, `Invoke-WebRequest`, `curl`, `wget`
- process-launch terms for local export and export-side probe helpers

Findings:
- No executable runtime app code uses `HttpClient`, `WebClient`, socket APIs, DNS APIs,
  analytics SDKs, telemetry APIs, upload code, or automatic update code.
- XAML `http://schemas.microsoft.com/...` namespace declarations are markup identifiers, not
  runtime network calls.
- `NuGet.config` points package restore at `https://api.nuget.org/v3/index.json`.
- Runtime manifests and documentation contain HTTPS source/provenance URLs.
- `scripts\Ensure-DevRuntime.ps1`, `scripts\Ensure-DevExportRuntime.ps1`, and
  `scripts\Ensure-DevExportTools.ps1` can use `Invoke-WebRequest` to download pinned artifacts
  when local runtime artifacts are absent.
- `scripts\ffmpeg\Build-FFmpeg-8.1.ps1`,
  `scripts\ffmpeg\Build-FFmpeg-ExportRuntime-8.1.ps1`, and
  `scripts\ffmpeg\Build-FFmpeg-Tools-8.1.ps1` can clone official FFmpeg source for local
  source-build workflows.
- `Packaging\MSIX\build-msix.ps1` accepts an HTTPS timestamp URL for package signing.

Conclusion:
- The shipped app should be presented to IT security as a local desktop tool with no runtime
  network telemetry.
- Build/bootstrap network activity should be reviewed as supply-chain behavior and can be
  avoided in network-restricted environments by restoring NuGet packages from an approved local
  cache/feed and pre-staging runtime folders before building with `-p:SkipRuntimeBootstrap=true`.

## Documentation And Consistency Findings

### 1. Runtime network posture was not explicit enough

Status: addressed in this branch.

Why it matters:
- The repository legitimately contains HTTPS URLs and build scripts with download behavior.
  Without a clear explanation, reviewers could confuse build-time dependency restoration with
  shipped-app telemetry.

Smallest fix:
- Added dedicated network/telemetry sections to `README.md` and `SECURITY.md`.

### 2. Frame-first source contracts were under-documented

Status: addressed in this branch for the stable core contract.

Why it matters:
- The frame-first architecture depends on knowing when frame identity is absolute versus
  provisional. That rule should be visible in the source contract, not only in maintainer docs.

Smallest fix:
- Added XML documentation to `IVideoReviewEngine` and `FrameDescriptor`.

### 3. The export host name can sound network-like without source context

Status: addressed in this branch.

Why it matters:
- "Host" can read like a service boundary to security reviewers. In this app it is a hidden local
  child process used to isolate heavy FFmpeg export and export-side probe work.

Smallest fix:
- Documented the local temporary-file request/response model in `ExportHostClient`,
  `ExportHostRequest`, and `ExportHostResponse`.

### 4. Large source files remain the biggest human-review cost

Status: open follow-up.

Observed hotspots:
- `MainWindow.xaml.cs`: large WPF shell/controller file
- `Diagnostics\RegressionSuiteRunner.cs`: large product-regression harness
- `Engines\FFmpeg\FfmpegReviewEngine.cs`: large frame-critical FFmpeg implementation
- `Core\Coordination\ReviewWorkspaceCoordinator.cs`: central multi-pane coordination logic

Why it matters:
- Large files slow human review, make ownership boundaries harder to see, and make mechanical
  cleanup risky.

Recommended next step:
- Split only cold or shell-level responsibilities first. Good candidates are diagnostics export,
  export command orchestration, compare-export UI flow, and regression harness report formatting.
  Avoid frame-critical decode/cache/playback refactors until coverage and manual validation are
  staged for that exact behavior.

### 5. Manifest validation code is intentionally repetitive

Status: documented follow-up, not changed.

Why it matters:
- `RuntimeManifestService`, `ExportRuntimeManifestService`, and `ExportToolsManifestService`
  share similar validation logic. A shared helper could reduce repetition, but these files also
  represent separate trust boundaries: playback DLLs, export DLLs, and dev/test CLI tools.

Recommended next step:
- Consider a small internal helper only if future changes touch all three validators together.
  Keep user-facing error messages specific to each boundary.

### 6. Nullable and unsafe-code posture remains a deliberate long-term item

Status: unchanged.

Why it matters:
- Project-wide nullability is disabled and unsafe code is enabled for FFmpeg interop. Those are
  maintainability and review concerns, but broad changes would touch performance-sensitive paths.

Recommended next step:
- Enable nullable annotations one subsystem at a time after adding targeted tests.
- Narrow unsafe exposure only behind concrete interop defects or a carefully validated extraction.

## Suggested Review Narrative

For IT security:
- Frame Player is a local WPF desktop application.
- Runtime media review does not send telemetry or open network listeners.
- Local user data is limited to recent-file state, diagnostics, runtime preferences, and optional
  user-exported reports.
- Build-time FFmpeg artifact restoration is pinned, HTTPS-only, and hash-validated, and can be
  skipped when runtime artifacts are staged locally. NuGet restore should use an approved
  feed/cache in network-restricted environments.

For human developers:
- The design is intentionally frame-first.
- UI state should not invent frame identity while indexing is still pending.
- Export and export-side probe work are isolated in a child process to keep the interactive
  review process stable.
- Refactors should start at shell/cold-path boundaries before moving into FFmpeg decode logic.

## Validation Performed

Completed on this branch:
- `git diff --check`
- `dotnet build .\FramePlayer.csproj -c Release -p:Platform=x64`
- `dotnet test .\tests\FramePlayer.Core.Tests\FramePlayer.Core.Tests.csproj -c Release`
- post-change source network/telemetry scan for `HttpClient`, `WebClient`, socket APIs, DNS APIs,
  telemetry/analytics terms, upload terms, `Invoke-WebRequest`, FFmpeg source clones, package
  restore configuration, and signing timestamp configuration

Result:
- Release build passed with `0` warnings and `0` errors.
- Focused test project passed with `27/27` tests.
- The post-change executable C# network/telemetry scan found only the documentation comment that
  explicitly states the export host is not a socket listener or telemetry channel.
