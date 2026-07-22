#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
FFMPEG_TAG="n8.1.2"
FFMPEG_COMMIT="38b88335f99e76ed89ff3c93f877fdefce736c13"
X264_COMMIT="0480cb05fa188d37ae87e8f4fd8f1aea3711f7ee"
MACOS_DEPLOYMENT_TARGET="13.0"
WORK_ROOT="${WORK_ROOT:-/tmp/frameplayer-ffmpeg-macos-8.1.2-source-build}"
RUNTIME_DIR="${RUNTIME_DIR:-$ROOT_DIR/Runtime/macos/osx-arm64/ffmpeg}"
JOBS="${JOBS:-$(sysctl -n hw.logicalcpu)}"

export MACOSX_DEPLOYMENT_TARGET="$MACOS_DEPLOYMENT_TARGET"

if [[ "$(uname -s)-$(uname -m)" != "Darwin-arm64" ]]; then
  echo "This build script currently supports only native Apple Silicon macOS hosts." >&2
  exit 2
fi

case "$WORK_ROOT" in
  ""|/|"$ROOT_DIR"|"$ROOT_DIR"/)
    echo "Refusing unsafe FFmpeg work root: '$WORK_ROOT'." >&2
    exit 2
    ;;
  *) ;;
esac

runtime_parent="$(cd "$(dirname "$RUNTIME_DIR")" && pwd)"
if [[ "$runtime_parent" != "$ROOT_DIR/Runtime/macos/osx-arm64" && "$runtime_parent" != "$WORK_ROOT"* ]]; then
  echo "Refusing to stage FFmpeg outside the repository macOS runtime or the task work root: '$RUNTIME_DIR'." >&2
  exit 2
fi

for tool in clang git install_name_tool make otool shasum; do
  if ! command -v "$tool" >/dev/null 2>&1; then
    echo "Missing required build tool: $tool" >&2
    exit 2
  fi
done

FFMPEG_SOURCE_DIR="$WORK_ROOT/ffmpeg-$FFMPEG_TAG"
FFMPEG_BUILD_DIR="$WORK_ROOT/build-osx-arm64-shared"
FFMPEG_INSTALL_DIR="$WORK_ROOT/install-osx-arm64-shared"
X264_SOURCE_DIR="$WORK_ROOT/x264-$X264_COMMIT"
X264_BUILD_DIR="$WORK_ROOT/build-x264-osx-arm64"
X264_INSTALL_DIR="$WORK_ROOT/install-x264-osx-arm64"
PKG_CONFIG_WRAPPER="$WORK_ROOT/pkg-config-x264"
STAGING_DIR="$WORK_ROOT/runtime-osx-arm64-$FFMPEG_TAG"

mkdir -p "$WORK_ROOT"

if [[ ! -d "$FFMPEG_SOURCE_DIR/.git" ]]; then
  git clone --branch "$FFMPEG_TAG" --depth 1 https://git.ffmpeg.org/ffmpeg.git "$FFMPEG_SOURCE_DIR"
else
  git -C "$FFMPEG_SOURCE_DIR" fetch --depth 1 origin "tag" "$FFMPEG_TAG"
  git -C "$FFMPEG_SOURCE_DIR" checkout --detach --force "$FFMPEG_TAG"
fi

actual_ffmpeg_commit="$(git -C "$FFMPEG_SOURCE_DIR" rev-parse HEAD)"
if [[ "$actual_ffmpeg_commit" != "$FFMPEG_COMMIT" ]]; then
  echo "Unexpected FFmpeg $FFMPEG_TAG commit: $actual_ffmpeg_commit" >&2
  exit 3
fi

if [[ ! -d "$X264_SOURCE_DIR/.git" ]]; then
  git clone https://code.videolan.org/videolan/x264.git "$X264_SOURCE_DIR"
fi
git -C "$X264_SOURCE_DIR" fetch --depth 1 origin "$X264_COMMIT"
git -C "$X264_SOURCE_DIR" checkout --detach --force "$X264_COMMIT"

rm -rf "$X264_BUILD_DIR" "$X264_INSTALL_DIR"
mkdir -p "$X264_BUILD_DIR" "$X264_INSTALL_DIR"
(
  cd "$X264_BUILD_DIR"
  "$X264_SOURCE_DIR/configure" \
    --prefix="$X264_INSTALL_DIR" \
    --host=aarch64-apple-darwin \
    --enable-static \
    --enable-pic \
    --disable-cli
  make -j"$JOBS"
  make install-lib-static
)

cat > "$PKG_CONFIG_WRAPPER" <<EOF
#!/usr/bin/env bash
set -euo pipefail
case " \$* " in
  *" --version "*) echo "0.29" ;;
  *" --exists "*) exit 0 ;;
  *" --cflags-only-I "*|*" --cflags "*) echo "-I$X264_INSTALL_DIR/include" ;;
  *" --libs "*) echo "-L$X264_INSTALL_DIR/lib -lx264 -lpthread -lm" ;;
  *" --variable=includedir "*) echo "$X264_INSTALL_DIR/include" ;;
  *" --modversion "*) echo "0.165" ;;
  *) echo "Unsupported pkg-config invocation: \$*" >&2; exit 2 ;;
esac
EOF
chmod +x "$PKG_CONFIG_WRAPPER"

rm -rf "$FFMPEG_BUILD_DIR" "$FFMPEG_INSTALL_DIR" "$STAGING_DIR"
mkdir -p "$FFMPEG_BUILD_DIR" "$FFMPEG_INSTALL_DIR" "$STAGING_DIR"

configure_flags=(
  --prefix="$FFMPEG_INSTALL_DIR"
  --cc=clang
  --pkg-config="$PKG_CONFIG_WRAPPER"
  --arch=arm64
  --target-os=darwin
  --install-name-dir=@loader_path
  --enable-shared
  --disable-static
  --enable-gpl
  --enable-libx264
  --disable-programs
  --disable-doc
  --disable-debug
  --disable-avdevice
  --disable-network
  --disable-autodetect
  --disable-videotoolbox
  --disable-audiotoolbox
  --extra-cflags=-mmacosx-version-min=$MACOS_DEPLOYMENT_TARGET
  --extra-ldflags=-mmacosx-version-min=$MACOS_DEPLOYMENT_TARGET
  --extra-version=frameplayer-macos-source
)

(
  cd "$FFMPEG_BUILD_DIR"
  "$FFMPEG_SOURCE_DIR/configure" "${configure_flags[@]}"
  grep -q '^#define CONFIG_LIBX264_ENCODER 1$' config_components.h
  grep -q '^#define CONFIG_AVFILTER 1$' config.h
  make -j"$JOBS"
  make install
)

for installed_dylib in "$FFMPEG_INSTALL_DIR"/lib/*.dylib; do
  dylib_name="$(basename "$installed_dylib")"
  cp -L "$installed_dylib" "$STAGING_DIR/$dylib_name"
  install_name_tool -id "@loader_path/$dylib_name" "$STAGING_DIR/$dylib_name"
done

required_dylibs=(
  libavutil.60.dylib
  libswresample.6.dylib
  libswscale.9.dylib
  libavfilter.11.dylib
  libavcodec.62.dylib
  libavformat.62.dylib
)
for required_dylib in "${required_dylibs[@]}"; do
  if [[ ! -f "$STAGING_DIR/$required_dylib" ]]; then
    echo "Built runtime is missing required library: $required_dylib" >&2
    exit 4
  fi
done

for staged_dylib in "$STAGING_DIR"/*.dylib; do
  dylib_minimum="$(otool -l "$staged_dylib" | awk '$1 == "minos" { print $2; exit }')"
  if [[ "$dylib_minimum" != "$MACOS_DEPLOYMENT_TARGET" ]]; then
    echo "Unexpected macOS deployment target for $(basename "$staged_dylib"): '$dylib_minimum' (expected '$MACOS_DEPLOYMENT_TARGET')." >&2
    exit 4
  fi
done

clang_version="$(clang --version | head -1)"
make_version="$(make --version | head -1)"
{
  echo "FFmpeg tag: $FFMPEG_TAG"
  echo "FFmpeg commit: $actual_ffmpeg_commit"
  echo "x264 commit: $X264_COMMIT"
  echo "Toolchain: macOS clang arm64"
  echo "Deployment target: $MACOS_DEPLOYMENT_TARGET"
  echo "Clang: $clang_version"
  echo "Make: $make_version"
  echo "Configure flags:"
  printf '  %q' "$FFMPEG_SOURCE_DIR/configure" "${configure_flags[@]}"
  printf '\n'
} > "$STAGING_DIR/build-provenance.txt"

(
  cd "$STAGING_DIR"
  shasum -a 256 -- *.dylib | LC_ALL=C sort -k 2 > SHA256SUMS.txt
)

mkdir -p "$RUNTIME_DIR"
find "$RUNTIME_DIR" -maxdepth 1 -type f -name '*.dylib' -delete
cp "$STAGING_DIR"/*.dylib "$RUNTIME_DIR/"
cp "$STAGING_DIR/SHA256SUMS.txt" "$STAGING_DIR/build-provenance.txt" "$RUNTIME_DIR/"

echo "Staged FFmpeg $FFMPEG_TAG macOS runtime at: $RUNTIME_DIR"
