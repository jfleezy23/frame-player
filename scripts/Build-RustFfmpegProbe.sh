#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
RUNTIME_IDENTIFIER="${1:-}"
HOST_RUNTIME_IDENTIFIER=""
case "$(uname -s)-$(uname -m)" in
  Darwin-arm64) HOST_RUNTIME_IDENTIFIER="osx-arm64" ;;
  Darwin-x86_64) HOST_RUNTIME_IDENTIFIER="osx-x64" ;;
esac

if [[ -z "$RUNTIME_IDENTIFIER" ]]; then
  case "$HOST_RUNTIME_IDENTIFIER" in
    osx-arm64|osx-x64) RUNTIME_IDENTIFIER="$HOST_RUNTIME_IDENTIFIER" ;;
    *) echo "Could not infer a supported Rust FFmpeg probe runtime identifier." >&2; exit 2 ;;
  esac
fi

if ! command -v cargo >/dev/null 2>&1; then
  echo "cargo is required to build the Rust FFmpeg probe. Install Rust with rustup and retry." >&2
  exit 1
fi

CRATE_DIR="$ROOT_DIR/native/frameplayer_ffmpeg_probe"
DEST_DIR="$ROOT_DIR/Runtime/rust/$RUNTIME_IDENTIFIER"

case "$RUNTIME_IDENTIFIER" in
  osx-arm64|osx-x64)
    if [[ "$RUNTIME_IDENTIFIER" != "$HOST_RUNTIME_IDENTIFIER" ]]; then
      echo "Cross-compiling the Rust FFmpeg probe is not supported by this script: requested $RUNTIME_IDENTIFIER on ${HOST_RUNTIME_IDENTIFIER:-unsupported host}." >&2
      exit 2
    fi
    cargo build --manifest-path "$CRATE_DIR/Cargo.toml" --release
    SOURCE_LIB="$CRATE_DIR/target/release/libframeplayer_ffmpeg_probe.dylib"
    DEST_LIB="$DEST_DIR/libframeplayer_ffmpeg_probe.dylib"
    ;;
  *)
    echo "Unsupported Rust FFmpeg probe runtime identifier for this script: $RUNTIME_IDENTIFIER" >&2
    exit 2
    ;;
esac

mkdir -p "$DEST_DIR"
cp "$SOURCE_LIB" "$DEST_LIB"
if [[ "$RUNTIME_IDENTIFIER" == osx-* ]] && command -v install_name_tool >/dev/null 2>&1; then
  install_name_tool -id "@rpath/$(basename "$DEST_LIB")" "$DEST_LIB"
fi
echo "Built Rust FFmpeg probe: $DEST_LIB"
