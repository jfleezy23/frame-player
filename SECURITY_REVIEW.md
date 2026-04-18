# Frame Player Security Review

Latest follow-up:
- See [docs/deep-review-hardening-report.md](docs/deep-review-hardening-report.md) for the 2026-04-18 combined security, hygiene, and maintainability review with frame-first guardrails.

Date: 2026-04-05

Reviewer: CODEX

Scope:
- WPF desktop application source
- Bundled FFmpeg runtime loading path
- Diagnostics and recent-file persistence
- Runtime bootstrap and deployment-signing scripts
- Repository hygiene and obvious secret exposure

Verdict:
- No critical or high-severity code findings remain after the latest hardening pass.
- Production deployment should not use loose-file distribution as the primary distribution channel.
- Production deployment should still prefer signed install deployment over loose-file distribution.

## Findings

### P2 Open: Loose-file distribution remains less tamper-resistant than signed install deployment

Locations:
- [App.xaml.cs](App.xaml.cs)
- [FramePlayer.csproj](FramePlayer.csproj)
- [README.md](README.md)

Why it matters:
- The app validates required FFmpeg DLL hashes at startup, which is good defense-in-depth.
- Even so, loose-file deployment places native DLLs beside the executable and therefore depends on install-directory ACLs for tamper resistance.
- This is acceptable for local testing, but it is weaker than an enterprise-signed and managed install deployment.

Recommendation:
- Treat loose-file deployment as a test artifact only.
- For production deployment, distribute only an organization-signed and timestamped install artifact through a protected install path with locked-down write permissions.

Status:
- Open operational risk

### P3 Advisory: Exported diagnostics intentionally contain filenames and playback metadata

Location:
- [MainWindow.xaml.cs](MainWindow.xaml.cs)

Why it matters:
- The export flow is intentionally readable and includes filenames, playback state, timecode, frame counts, and recent runtime status.
- Absolute paths are redacted where practical, but exported reports are still sensitive support artifacts.

Recommendation:
- Treat exported diagnostics as controlled data.
- Avoid posting them to public issue trackers without review.

Status:
- Advisory

## Resolved

### Runtime integrity validation

Implemented:
- Embedded manifest-backed DLL hash validation before FFmpeg is enabled

Locations:
- [Services/RuntimeManifestService.cs](Services/RuntimeManifestService.cs)
- [App.xaml.cs](App.xaml.cs)
- [scripts/Ensure-DevRuntime.ps1](scripts/Ensure-DevRuntime.ps1)

### Sensitive local data now protected at rest

Implemented:
- Recent-file history moved from plaintext to DPAPI-protected storage
- On-disk session log moved from plaintext to DPAPI-protected storage

Locations:
- [Services/RecentFilesService.cs](Services/RecentFilesService.cs)
- [Services/DiagnosticLogService.cs](Services/DiagnosticLogService.cs)

### Diagnostics and error reporting now redact paths more consistently

Implemented:
- Filename-only display for exported diagnostics headers
- Best-effort path redaction in exception/log text

Location:
- [MainWindow.xaml.cs](MainWindow.xaml.cs)

### Install-signing workflow now distinguishes test signing from production signing

Implemented:
- Local dev signing requires explicit `-UseDevCertificate`
- Production signing accepts a real PFX and optional timestamp URL
- Install helper no longer assumes enterprise certs should be imported into `TrustedPeople`

Location:
- deployment signing script

### Prerelease FFME dependency removed

Implemented:
- The app no longer references the prerelease FFME package; playback and frame review now run through the custom FFmpeg engine.

Locations:
- [FramePlayer.csproj](FramePlayer.csproj)
- [Engines/FFmpeg/FfmpegReviewEngine.cs](Engines/FFmpeg/FfmpegReviewEngine.cs)

## Repository Hygiene Checks

Performed:
- Regex-based secret scan across tracked source, excluding build output and package caches
- Manual review of packaging scripts for embedded credentials
- Verification that the runtime bootstrap asset hash matches the manifest

Result:
- No obvious hardcoded API keys, tokens, private keys, or certificates were found in tracked source.
- Secret-scan matches were limited to password parameter names in the deployment signing script, which are expected.

## Build And Verification

Validated:
- `scripts\\Ensure-DevRuntime.ps1` passed with the pinned runtime present
- Release build succeeded with `0 warnings, 0 errors`
- the maintained signing flow produced a signed local install artifact successfully
- The rebuilt portable EXE launched successfully

## Deployment Recommendation

Recommended baseline:
- Use only the signed install path for deployment
- Sign with an organization-issued certificate
- Timestamp the package signature
- Install into a protected location with controlled write access
- Treat diagnostics exports as sensitive

Not covered by this review:
- Formal internal deployment approval requirements
- OS hardening baselines or STIG alignment
- Endpoint management policy, EDR policy, or enterprise certificate distribution
