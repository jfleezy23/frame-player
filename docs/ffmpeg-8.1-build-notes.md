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
