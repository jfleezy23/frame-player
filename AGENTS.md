# AGENTS.md

## Sonar Cleanup Policy

- Do not start broad Sonar cleanup or refactoring by default.
- When making a real feature, bug fix, or release-prep change, check the files already being touched for nearby Sonar warnings.
- Only address no-risk or low-risk Sonar warnings when the fix is small, behavior-preserving, and keeps the original task focused.
- Do not reshape public APIs, split large methods, restructure transport models, or perform broad UI/native refactors just to satisfy Sonar.
- Do not edit `Engines/FFmpeg/*`, frame decode/index/cache/audio playback code, `Services/DecodedFrameBudgetCoordinator.cs`, or `Services/MediaProbeService.cs` for Sonar cleanup without explicit user approval.

## Release & Packaging Policy

- **Windows Build Artifacts:** Never package the Avalonia build as an MSIX unless explicitly requested. Always use the `scripts\Package-UnifiedWindowsPreview.ps1` script to create the expected self-contained `.zip` portable application.
- **Windows Artifact Signing:** Always use `dotnet sign code artifact-signing` with the Azure Trusted Signing identity `frameplayersigningjflow` against the individual payload binaries (`.exe` and `.dll`s) in the publish directory *before* compressing them into the final `.zip`. Do not sign `.zip` containers directly.
- **GitHub Branch Protection & Documentation:** The `main` branch has strict branch protection. If a PR only contains documentation (`docs/*.md`), the `build` CI check will not run and remain pending indefinitely. Use `gh pr merge <PR_NUMBER> --admin --merge` to bypass the blocked status check and force the merge.
- **SmartScreen Warnings:** Understand that Microsoft Trusted Signing does not immediately bypass SmartScreen warnings for new publishers; do not assume the signature is broken just because Windows Defender throws a warning. Just follow the documentation.
