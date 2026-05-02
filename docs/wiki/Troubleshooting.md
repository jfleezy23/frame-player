# Troubleshooting

## No Audio

- Confirm the file has a supported audio stream.
- Try another known-good file from the same source.
- Silent clips and unsupported audio streams fall back to video-only review.
- Export diagnostics if playback state does not match the file contents.

## Unsupported Or Problem Files

- Confirm the file extension is one of the supported release formats: `.avi`, `.m4v`, `.mp4`, `.mkv`, or `.wmv`.
- Try opening the file from a local folder rather than a network or cloud-sync path.
- Use `Video Info` to inspect stream metadata when a file opens but behaves unexpectedly.

## Recent Files

Recent files are stored locally and protected at rest by platform storage:

- Windows: user-profile DPAPI-backed storage.
- macOS Preview: macOS application support storage.

If recent files do not appear, open a file successfully, close the app, relaunch, and check `File > Open Recent`.

## Diagnostics

Use `File > Export Diagnostics` when reporting a playback, seek, export, or runtime issue. Diagnostics are local files and do not transmit data automatically.

## macOS Gatekeeper

The published Apple Silicon preview is Developer ID signed, notarized, and stapled. If Gatekeeper blocks a locally rebuilt app, confirm whether it was signed with Developer ID or only Apple Development signing.
