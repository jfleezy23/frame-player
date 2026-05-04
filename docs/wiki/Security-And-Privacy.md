# Security And Privacy

## Local-Only Runtime Behavior

Frame Player does not include telemetry, analytics, auto-update, HTTP client, socket, or background network-service behavior in normal playback, review, export, recent-file, or diagnostic flows.

Expected network activity is limited to developer/build tooling such as NuGet restore, pinned runtime artifact restore, optional FFmpeg source retrieval, and optional timestamping during signing.

## Local Data

- Playback, frame stepping, metadata inspection, clip export, compare export, audio insertion, recent-file storage, and diagnostics operate on local files.
- Recent files are protected at rest through platform storage.
- Diagnostics are exported only when the user asks for them.

## Signing And Notarization

Windows stable, macOS Preview, and Windows Avalonia Preview have separate release paths. The macOS Preview `0.1.0` artifact is Developer ID signed, notarized, stapled, and verified with Gatekeeper. The Windows Avalonia Preview `0.1.0` artifact is a self-contained Windows x64 ZIP and is separate from the stable Windows WPF ZIP/release path.

For a notarized macOS release:

1. Sign with a Developer ID Application certificate.
2. Use hardened runtime and only required entitlements.
3. Verify with `codesign`.
4. Submit with `xcrun notarytool`.
5. Staple with `xcrun stapler`.
6. Recheck Gatekeeper with `spctl`.

## FFmpeg And Third-Party Runtime Posture

The app bundles pinned FFmpeg runtimes. Current macOS FFmpeg provenance includes GPL/x264 implications, so redistribution must follow the notices in [THIRD_PARTY_NOTICES.md](https://github.com/jfleezy23/frame-player/blob/main/THIRD_PARTY_NOTICES.md).
