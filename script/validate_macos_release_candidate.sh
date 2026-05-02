#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
APP_BUNDLE="$ROOT_DIR/dist/Frame Player.app"
CORPUS_INPUT="${FRAMEPLAYER_MAC_CORPUS:-}"
WORK_ROOT="$ROOT_DIR/artifacts/macos-release-candidate"
CORPUS_DIR="$WORK_ROOT/corpus"
RESULTS_DIR="$WORK_ROOT/results"
REQUIRED_DYLIBS=(
  "libavutil.60.dylib"
  "libswresample.6.dylib"
  "libswscale.9.dylib"
  "libavfilter.11.dylib"
  "libavcodec.62.dylib"
  "libavformat.62.dylib"
)

usage() {
  cat >&2 <<USAGE
usage: $0 --corpus <folder-or-zip>

Validates the macOS release candidate against the real Frame Player test corpus.
This script intentionally does not download substitute sample videos.
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --corpus)
      CORPUS_INPUT="${2:-}"
      shift 2
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      echo "unknown argument: $1" >&2
      usage
      exit 2
      ;;
  esac
done

resolve_dotnet() {
  if [[ -n "${DOTNET_ROOT:-}" && -x "$DOTNET_ROOT/dotnet" ]]; then
    printf '%s\n' "$DOTNET_ROOT/dotnet"
    return 0
  fi

  if command -v dotnet >/dev/null 2>&1; then
    command -v dotnet
    return 0
  fi

  if [[ -x "$HOME/.dotnet/dotnet" ]]; then
    printf '%s\n' "$HOME/.dotnet/dotnet"
    return 0
  fi

  return 1
}

if [[ -z "$CORPUS_INPUT" && -d "$ROOT_DIR/Video Test Files" ]]; then
  CORPUS_INPUT="$ROOT_DIR/Video Test Files"
fi

if [[ -z "$CORPUS_INPUT" ]]; then
  echo "A real corpus path is required. Pass --corpus <folder-or-zip> or set FRAMEPLAYER_MAC_CORPUS." >&2
  exit 2
fi

if [[ ! -e "$CORPUS_INPUT" ]]; then
  echo "Corpus path does not exist: $CORPUS_INPUT" >&2
  exit 2
fi

DOTNET_BIN="$(resolve_dotnet)" || {
  echo "dotnet SDK not found. Install the SDK pinned by global.json first." >&2
  exit 1
}

DOTNET_BIN_DIR="$(cd "$(dirname "$DOTNET_BIN")" && pwd)"
if [[ -n "${DOTNET_ROOT:-}" ]]; then
  export DOTNET_ROOT
  export PATH="$DOTNET_ROOT:$PATH"
else
  export PATH="$DOTNET_BIN_DIR:$PATH"
fi

rm -rf "$RESULTS_DIR"
mkdir -p "$WORK_ROOT" "$RESULTS_DIR"

if [[ -d "$CORPUS_INPUT" ]]; then
  CORPUS_DIR="$(cd "$CORPUS_INPUT" && pwd)"
else
  rm -rf "$CORPUS_DIR"
  mkdir -p "$CORPUS_DIR"
  case "$CORPUS_INPUT" in
    *.zip)
      /usr/bin/ditto -x -k "$CORPUS_INPUT" "$CORPUS_DIR"
      ;;
    *)
      echo "Unsupported corpus archive. Provide a folder or .zip file: $CORPUS_INPUT" >&2
      exit 2
      ;;
  esac
fi

find "$CORPUS_DIR" -type f \( \
  -iname '*.avi' -o \
  -iname '*.m4v' -o \
  -iname '*.mkv' -o \
  -iname '*.mov' -o \
  -iname '*.mp4' -o \
  -iname '*.ts' -o \
  -iname '*.wmv' \
\) | sort > "$RESULTS_DIR/corpus-files.txt"

if [[ ! -s "$RESULTS_DIR/corpus-files.txt" ]]; then
  echo "No supported media files found in corpus: $CORPUS_DIR" >&2
  exit 1
fi

CONFIGURATION=Release "$ROOT_DIR/script/build_and_run.sh" --build-only

/usr/libexec/PlistBuddy -c "Print :CFBundleIconFile" "$APP_BUNDLE/Contents/Info.plist" > "$RESULTS_DIR/bundle-icon.txt"
[[ -s "$APP_BUNDLE/Contents/Resources/FramePlayer.icns" ]]
[[ -x "$APP_BUNDLE/Contents/MacOS/FramePlayer.Mac" ]]

runtime_dir="$APP_BUNDLE/Contents/MacOS/Runtime/macos/osx-arm64/ffmpeg"
if [[ ! -d "$runtime_dir" ]]; then
  runtime_dir="$APP_BUNDLE/Contents/MacOS/Runtime/macos/osx-arm64"
fi
if [[ ! -d "$runtime_dir" ]]; then
  runtime_dir="$APP_BUNDLE/Contents/MacOS"
fi

missing_dylibs=()
for dylib in "${REQUIRED_DYLIBS[@]}"; do
  if [[ ! -f "$runtime_dir/$dylib" ]]; then
    missing_dylibs+=("$dylib")
  fi
done

if [[ ${#missing_dylibs[@]} -gt 0 ]]; then
  {
    echo "Missing required macOS FFmpeg runtime dylibs in $runtime_dir:"
    printf '  %s\n' "${missing_dylibs[@]}"
    echo
    echo "Stage the pinned macOS runtime under Runtime/macos/osx-arm64/ffmpeg before running corpus validation."
  } >&2
  exit 1
fi

FRAMEPLAYER_MAC_CORPUS="$CORPUS_DIR" \
FRAMEPLAYER_MAC_REQUIRE_CORPUS=1 \
FRAMEPLAYER_MAC_APP_BUNDLE="$APP_BUNDLE" \
FRAMEPLAYER_GPU_BACKEND=cpu \
"$DOTNET_BIN" test "$ROOT_DIR/tests/FramePlayer.Mac.Tests/FramePlayer.Mac.Tests.csproj" \
  -c Release \
  --logger "trx;LogFileName=macos-release-candidate.trx" \
  --results-directory "$RESULTS_DIR"

echo "macOS release-candidate validation passed."
echo "Corpus files: $(wc -l < "$RESULTS_DIR/corpus-files.txt" | tr -d ' ')"
echo "Results: $RESULTS_DIR"
