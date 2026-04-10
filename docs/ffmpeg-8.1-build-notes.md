# FFmpeg 8.1 Source Build Notes

This file records the isolated source-build workflow for the FFmpeg 8.1 runtime that is now active in the app. The build output is staged separately first, then restored into `Runtime\ffmpeg\` through `scripts\Ensure-DevRuntime.ps1`.

## Build Command

```powershell
.\scripts\ffmpeg\Build-FFmpeg-8.1.ps1 -Clean
```

The default source/build scratch path is `%TEMP%\frameplayer-ffmpeg-8.1-source-build` because FFmpeg rejects out-of-tree builds when the source path contains whitespace.

## Source

- Source repository: `https://git.ffmpeg.org/ffmpeg.git`
- Source tag: `n8.1`
- Expected annotated tag object: `a65b3bfe9dacc3b20597ef199d0afdd8bc8128e2`
- Expected source commit: `9047fa1b084f76b1b4d065af2d743df1b40dfb56`

## Toolchain

- Windows x64
- MSYS2 MinGW-w64 x64 environment
- Required packages: `git`, `make`, `pkgconf`, `diffutils`, `mingw-w64-x86_64-gcc`, `mingw-w64-x86_64-nasm`, `mingw-w64-x86_64-pkgconf`

## Configure Scope

The initial candidate build is intentionally app-focused:

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

## Candidate Output

The script stages output in:

```text
Runtime\ffmpeg-8.1-candidate\
```

The script also stages a local runtime archive in:

```text
artifacts\FramePlayer-ffmpeg-runtime-x64.zip
```

Both paths are intentionally ignored by git. `scripts\Ensure-DevRuntime.ps1` restores the active `Runtime\ffmpeg\` directory from this self-built candidate or archive instead of a third-party prebuilt bundle.

## Current Restore Model

- `Runtime\runtime-manifest.json` records the expected FFmpeg 8.1 DLL hashes, archive filename, archive SHA256, and source-build provenance.
- `scripts\Ensure-DevRuntime.ps1` restores `Runtime\ffmpeg\` from `Runtime\ffmpeg-8.1-candidate\` or `artifacts\FramePlayer-ffmpeg-runtime-x64.zip` before attempting any remote download path.
- If neither local source exists, the script now fails fast when the manifest does not provide a verified remote `tag` or `assetUrl` for the pinned FFmpeg 8.1 runtime.

## Windows CI

GitHub Actions Windows CI now runs compile validation only on clean runners:

```powershell
msbuild .\FramePlayer.csproj /t:Restore,Build /p:Configuration=Release /p:Platform=x64 /p:SkipRuntimeBootstrap=true
```

This is intentional. Clean GitHub runners do not have the staged local FFmpeg runtime artifacts, and the current manifest does not yet identify a verified published FFmpeg 8.1 restore source.

## Still Needed For Clean-Runner Restore

- Publish the exact FFmpeg 8.1 runtime archive that matches the current manifest hashes.
- Update `Runtime\runtime-manifest.json` with a verified release `tag` or explicit `assetUrl` for that archive.
- Keep `assetSha256` and the per-DLL SHA256 entries aligned with the published archive before re-enabling bootstrap on clean runners.
- Do not point the manifest at any published archive unless the archive SHA256 and extracted DLL hashes are proven to match the current FFmpeg 8.1 runtime.
