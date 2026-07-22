# AGENTS.md

## Product Scope

- `src/FramePlayer.Avalonia` is the only supported application product.
- Windows and macOS are platform targets of the same universal Avalonia application, not separate product implementations.
- Do not create or maintain separate platform product implementations, tests, packaging paths, release tracks, or documentation.
- Shared libraries under `src/FramePlayer.Core` and `src/FramePlayer.Engine.FFmpeg` are part of the universal Avalonia product and should remain platform-neutral except where a native platform adapter is required.

## Sonar Cleanup Policy

- Do not start broad Sonar cleanup or refactoring by default.
- When making a real feature, bug fix, or release-prep change, check the files already being touched for nearby Sonar warnings.
- Only address no-risk or low-risk Sonar warnings when the fix is small, behavior-preserving, and keeps the original task focused.
- Do not reshape public APIs, split large methods, restructure transport models, or perform broad UI/native refactors just to satisfy Sonar.
- Do not edit `src/FramePlayer.Engine.FFmpeg/Engines/FFmpeg/*`, frame decode/index/cache/audio playback code, `src/FramePlayer.Engine.FFmpeg/Services/DecodedFrameBudgetCoordinator.cs`, or `src/FramePlayer.Avalonia/Services/MediaProbeService.cs` for Sonar cleanup without explicit user approval.
