# FFmpeg 8.1.2 Source Build Notes

This file records the isolated source-build workflow for FFmpeg 8.1.2. Build output is staged separately and promoted only after its archive and per-file hashes are recorded in the runtime manifests.

## Build Command

```powershell
.\scripts\ffmpeg\Build-FFmpeg-8.1.ps1 -Clean
```

The default source/build scratch path is `%TEMP%\frameplayer-ffmpeg-8.1.2-source-build` because FFmpeg rejects out-of-tree builds when the source path contains whitespace.

## Source

- Source repository: `https://git.ffmpeg.org/ffmpeg.git`
- Source tag: `n8.1.2`
- Expected annotated tag object: `1c2c67c0b9f7f66ab32c19dcf7f227bcd290aa4c`
- Expected source commit: `38b88335f99e76ed89ff3c93f877fdefce736c13`

## Toolchain

- Windows x64
- MSYS2 MinGW-w64 x64 environment
- Required packages: `git`, `make`, `pkgconf`, `diffutils`, `mingw-w64-x86_64-gcc`, `mingw-w64-x86_64-nasm`, `mingw-w64-x86_64-pkgconf`
- For the FFmpeg Vulkan decode path, the MinGW-w64 environment also needs Vulkan headers available to `configure`, which in practice means installing `mingw-w64-x86_64-vulkan-headers`. Installing `mingw-w64-x86_64-vulkan-loader` alongside it is also recommended so the MinGW toolchain has the matching loader import library and `vulkan.pc` metadata.

## Configure Scope

The initial candidate build is intentionally scoped to the universal Avalonia application:

- `--enable-shared`
- `--disable-static`
- `--disable-programs`
- `--disable-doc`
- `--disable-debug`
- `--disable-avdevice`
- `--disable-avfilter`
- `--disable-network`
- `--disable-autodetect`
- `--disable-encoders`
- `--disable-muxers`

This should produce the libraries Frame Player directly uses today: `avcodec`, `avformat`, `avutil`, `swresample`, and `swscale`, with broad native demuxer/decoder coverage but without FFmpeg CLI programs, muxers, encoders, network protocols, filters, or devices. If corpus validation later shows demuxer/decoder coverage gaps, widen the source build configuration in a separate pass.

The MinGW-w64 build can also require `libwinpthread-1.dll` at runtime. The build script stages that toolchain runtime DLL if the built FFmpeg DLLs require it. It is not an FFmpeg prebuilt binary.

## Vulkan Decode Enablement

- The source-build script now configures FFmpeg with `--enable-vulkan` so the runtime can expose FFmpeg's Vulkan hardware-device path to the app.
- The source-build script verifies that FFmpeg `configure` actually enabled `CONFIG_VULKAN`, `CONFIG_H264_VULKAN_HWACCEL`, `CONFIG_HEVC_VULKAN_HWACCEL`, and `CONFIG_AV1_VULKAN_HWACCEL` before compiling the runtime.
- Frame Player uses GPU decode plus CPU readback into its Avalonia rendering path. The runtime archive does not bundle a verified Vulkan loader.
- Actual GPU acceleration therefore depends on a working system Vulkan loader/driver on the target machine. Unsupported hardware, unsupported codecs, or failed Vulkan device creation must fall back to CPU decode.

## Candidate Output

The script stages output in:

```text
Runtime\ffmpeg-8.1.2-candidate\
```

The script also stages a local runtime bundle in:

```text
artifacts\FramePlayer-ffmpeg-runtime-x64.zip
```

Both paths are intentionally ignored by git. `scripts\Ensure-DevRuntime.ps1` restores the active `Runtime\ffmpeg\` directory from this self-built candidate or staged bundle instead of a third-party prebuilt bundle.

## Current Restore Model

- `Runtime\runtime-manifest.json` records the expected FFmpeg 8.1.2 DLL hashes, archive filename, archive SHA256, and source-build provenance.
- `scripts\Ensure-DevRuntime.ps1` restores `Runtime\ffmpeg\` from `Runtime\ffmpeg-8.1.2-candidate\` or the staged local runtime archive before attempting any remote download path.
- If neither local source exists, the script downloads the immutable 8.1.2 archive declared in the manifest and validates both the archive SHA256 and extracted DLL hashes.
- `Runtime\runtime-manifest.json` now also records that the current source-built runtime targets FFmpeg Vulkan hardware-device support while still requiring a system Vulkan loader at runtime.

## Windows-Target CI

GitHub Actions stages the pinned playback and export runtimes, builds and tests the universal Avalonia project, and packages its Windows target on clean runners. The relevant build command is:

```powershell
.\scripts\Ensure-DevRuntime.ps1
.\scripts\Ensure-DevExportTools.ps1
.\scripts\Ensure-DevExportRuntime.ps1 -Required
dotnet build .\src\FramePlayer.Avalonia\FramePlayer.Avalonia.csproj -c Release
.\scripts\Package-UnifiedWindows.ps1
```

Clean GitHub runners restore the verified 8.1.2 playback, export-runtime, and developer-tool archives recorded by the manifests. The same application project is built and packaged for every supported platform target.

## Still Needed Beyond Clean-Runner Restore

- Keep the published runtime archive, `assetSha256`, and per-DLL SHA256 entries aligned whenever the pinned runtime changes.
- Rebuild and revalidate all affected archives before changing a source tag or build configuration.
- Do not retarget the manifest to any future published archive unless the archive SHA256 and extracted DLL hashes are proven to match the pinned runtime.
- If the pinned runtime ever changes again, update `tag`/`assetUrl`, archive SHA256, and CI expectations together after the new asset is live and hash-verified.
