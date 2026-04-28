# AGENTS.md

## Sonar Cleanup Policy

- Do not start broad Sonar cleanup or refactoring by default.
- When making a real feature, bug fix, or release-prep change, check the files already being touched for nearby Sonar warnings.
- Only address no-risk or low-risk Sonar warnings when the fix is small, behavior-preserving, and keeps the original task focused.
- Do not reshape public APIs, split large methods, restructure transport models, or perform broad UI/native refactors just to satisfy Sonar.
- Do not edit `Engines/FFmpeg/*`, frame decode/index/cache/audio playback code, `Services/DecodedFrameBudgetCoordinator.cs`, or `Services/MediaProbeService.cs` for Sonar cleanup without explicit user approval.
