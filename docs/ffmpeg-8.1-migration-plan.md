# FFmpeg 8.1 Source-Build Migration Plan

This is a historical migration checkpoint. The FFmpeg 8.1 runtime is now the active runtime in the repo. Keep `docs\ffmpeg-8.1-build-notes.md` as the current runtime provenance note and `docs\release-v1.8.1-maintenance.md` as the current branch product/release note.

## Current Runtime Integration

- The app loads FFmpeg in-process through `FFmpeg.AutoGen`; `App.xaml.cs` sets `ffmpeg.RootPath` to the application base directory after runtime validation succeeds.
- `Runtime\runtime-manifest.json` is embedded in the app and is the runtime integrity source of truth. `RuntimeManifestService` validates every DLL listed under `files` by SHA256 before allowing the runtime path to be used.
- `scripts\Ensure-DevRuntime.ps1` downloads the manifest asset, verifies the archive hash, extracts an `ffmpeg` folder, copies `*.dll` into `Runtime\ffmpeg`, and verifies per-DLL hashes.
- `FramePlayer.csproj` copies `Runtime\ffmpeg\*.dll` to the build output, but its MSBuild conditions currently assume the old `avcodec-61.dll` name.
- Current tracked runtime metadata names the old 7-era DLL majors: `avcodec-61`, `avdevice-61`, `avfilter-10`, `avformat-61`, `avutil-59`, `swresample-5`, and `swscale-8`.
- `FFmpeg.AutoGen` is currently pinned to `7.1.1`; the next migration pass should test the available 8.x binding package before changing the runtime manifest.

## Target FFmpeg 8.1 Output

- Build from the official FFmpeg `n8.1` source tag. Do not use third-party prebuilt binaries as the shipped runtime.
- Expected FFmpeg 8.1 library majors from the `n8.1` source headers are `avcodec-62`, `avdevice-62`, `avfilter-11`, `avformat-62`, `avutil-60`, `swresample-6`, and `swscale-9`.
- Package only the runtime DLL set needed by the app into the existing `ffmpeg` archive layout so `Ensure-DevRuntime.ps1` and `RuntimeManifestService` can continue using the same model.
- A single x64 release runtime is sufficient for the app. Separate debug FFmpeg DLLs are not needed for normal Frame Player Debug/Release builds.

## Recommended Source-Build Strategy

- Use a Windows x64 MSYS2 MinGW-w64 environment, following FFmpeg's documented Windows shared-library build path.
- Start with a conservative shared-library build rather than a heavily minimized `--disable-everything` build. The app opens user-provided `.avi`, `.m4v`, `.mp4`, `.mkv`, and `.wmv` files, so over-pruning demuxers/decoders is a higher release risk than shipping a moderately broader native runtime.
- Recommended initial configure direction:
  - `--enable-shared`
  - `--disable-static`
  - `--disable-programs` for the shipped runtime, or build programs in a separate non-shipped validation output if useful
  - `--disable-doc`
  - no `--enable-nonfree`
  - avoid GPL/external codec libraries for the first pass unless corpus validation proves a required codec/container is missing
- Keep assembly optimizations enabled if the toolchain supports them, because frame review performance benefits from optimized decode/scale paths.
- After build, inspect DLL dependencies and either avoid or explicitly package any non-Windows runtime DLL dependencies required by the FFmpeg build.

## Minimum Component Set

The current app uses these FFmpeg libraries directly:

- `libavformat`: container open/probe, stream discovery, packet reading, seeks.
- `libavcodec`: video and audio decoder discovery, codec context setup, send/receive decode loop.
- `libavutil`: frame/packet allocation, timestamps, image/audio buffer helpers, channel layout helpers, memory management, error strings.
- `libswscale`: video pixel conversion to BGRA for the WPF surface.
- `libswresample`: audio resampling to the WinMM PCM output format.

The current manifest also ships `libavfilter` and `libavdevice`, but the app does not currently call filtergraph or device APIs directly. For the first FFmpeg 8.1 source-built runtime, keep those DLLs only if they are built by the chosen configure profile or required by dependency inspection. Do not add filtering/device features in the migration pass.

## Migration Risks

- ABI and DLL-name changes require updating manifest filenames/hashes and the `FramePlayer.csproj` `avcodec-61.dll` conditions.
- `FFmpeg.AutoGen 7.1.1` may not be the right managed binding for FFmpeg 8.1 DLLs; test `FFmpeg.AutoGen` 8.x in a narrow compatibility pass.
- Audio is a higher-risk area because it uses `AVChannelLayout` and `swr_alloc_set_opts2`; compile and runtime validation must cover audio decode/resample.
- Exact frame stepping, seek-to-frame, seek-to-time, slider click/drag seek, end-of-video slider agreement, background indexing, and decoded-frame cache behavior are release blockers if they regress.
- A too-minimal FFmpeg build can silently drop demuxer/decoder coverage for the local test corpus. Validate with the real `C:\Projects\Video Test Files` corpus before publishing.
- Licensing/provenance depends on the configure flags and linked external libraries. Record the source tag, configure command, toolchain version, archive SHA256, per-DLL SHA256 values, and license implications before release.

## Stepwise Execution Plan

1. Source-build preparation pass:
   - Install/verify MSYS2 MinGW-w64 x64 toolchain, GNU make, pkgconf, and NASM.
   - Clone or download official FFmpeg `n8.1` source into an ignored build workspace.
   - Decide the exact configure command and capture it in a build note or script.
2. Source-build pass:
   - Build FFmpeg 8.1 shared x64 DLLs from source.
   - Package the runtime into an `ffmpeg` folder layout matching the current app runtime archive contract.
   - Generate archive and per-DLL SHA256 hashes.
3. App integration pass:
   - Update `Runtime\runtime-manifest.json` to the self-built FFmpeg 8.1 archive and DLL filenames.
   - Update `FramePlayer.csproj` runtime-existence checks away from `avcodec-61.dll`.
   - Update `FFmpeg.AutoGen` only if required and fix narrow compile-time API compatibility issues.
   - Update `Runtime\README.md`, `README.md`, `TESTING_NOTES.md`, and `THIRD_PARTY_NOTICES.md` with the new source-built runtime provenance.
4. Regression validation pass:
   - Build the app and run open, first-frame, playback, audio, pause, seek-to-time, seek-to-frame, slider click/drag seek, backward/forward stepping, and end-of-video slider tests.
   - Validate the bundled sample plus representative files from `C:\Projects\Video Test Files`.
   - Treat any frame stepping or seek correctness regression as a release blocker.

## Source References

- FFmpeg source tag: `n8.1` from `https://git.ffmpeg.org/ffmpeg.git`.
- FFmpeg `n8.1` annotated tag object: `a65b3bfe9dacc3b20597ef199d0afdd8bc8128e2`; source commit: `9047fa1b084f76b1b4d065af2d743df1b40dfb56`.
- FFmpeg Windows build guidance: `https://ffmpeg.org/platform.html`.
- FFmpeg installation flow: `https://ffmpeg.org/doxygen/trunk/md_INSTALL.html`.
