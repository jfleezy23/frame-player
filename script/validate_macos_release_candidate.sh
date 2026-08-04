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

case "$(uname -s)-$(uname -m)" in
  Darwin-arm64) HOST_RUNTIME_IDENTIFIER="osx-arm64" ;;
  Darwin-x86_64) HOST_RUNTIME_IDENTIFIER="osx-x64" ;;
  *)
    echo "Unsupported macOS host architecture: $(uname -s)-$(uname -m)" >&2
    exit 2
    ;;
esac

MAC_RUNTIME_IDENTIFIER="${MAC_RUNTIME_IDENTIFIER:-$HOST_RUNTIME_IDENTIFIER}"
case "$MAC_RUNTIME_IDENTIFIER" in
  osx-arm64|osx-x64) ;;
  *)
    echo "Unsupported macOS runtime identifier: $MAC_RUNTIME_IDENTIFIER" >&2
    exit 2
    ;;
esac

if [[ "$MAC_RUNTIME_IDENTIFIER" != "$HOST_RUNTIME_IDENTIFIER" ]]; then
  echo "Cross-validation is not supported: requested $MAC_RUNTIME_IDENTIFIER on $HOST_RUNTIME_IDENTIFIER." >&2
  exit 2
fi

usage() {
  cat >&2 <<USAGE
usage: $0 --corpus <folder-or-zip>

Builds the universal Avalonia application and validates its macOS package
against the maintained Frame Player test corpus. No substitute media is downloaded.
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

if [[ -z "$CORPUS_INPUT" && -d "$ROOT_DIR/Video Test Files" ]]; then
  CORPUS_INPUT="$ROOT_DIR/Video Test Files"
fi

if [[ -z "$CORPUS_INPUT" || ! -e "$CORPUS_INPUT" ]]; then
  echo "A real corpus path is required. Pass --corpus <folder-or-zip> or set FRAMEPLAYER_MAC_CORPUS." >&2
  exit 2
fi

command -v dotnet >/dev/null 2>&1 || {
  echo "dotnet SDK not found. Install the SDK pinned by global.json first." >&2
  exit 1
}

rm -rf "$RESULTS_DIR"
mkdir -p "$WORK_ROOT" "$RESULTS_DIR"

if [[ -d "$CORPUS_INPUT" ]]; then
  # Resolve a locally-linked corpus before find scans it; find does not descend a
  # symlink supplied as its starting path on macOS.
  CORPUS_DIR="$(cd -P "$CORPUS_INPUT" && pwd)"
else
  rm -rf "$CORPUS_DIR"
  mkdir -p "$CORPUS_DIR"
  case "$CORPUS_INPUT" in
    *.zip) /usr/bin/ditto -x -k "$CORPUS_INPUT" "$CORPUS_DIR" ;;
    *)
      echo "Unsupported corpus archive. Provide a folder or .zip file: $CORPUS_INPUT" >&2
      exit 2
      ;;
  esac
fi

find "$CORPUS_DIR" -type f \( \
  -iname '*.avi' -o -iname '*.m4v' -o -iname '*.mkv' -o -iname '*.mov' -o \
  -iname '*.mp4' -o -iname '*.wmv' \
\) | sort > "$RESULTS_DIR/corpus-files.txt"

if [[ ! -s "$RESULTS_DIR/corpus-files.txt" ]]; then
  echo "No supported media files found in corpus: $CORPUS_DIR" >&2
  exit 1
fi

PACKAGE_VERSION="${PACKAGE_VERSION:-2.1.0-rc.17}" \
MAC_RUNTIME_IDENTIFIER="$MAC_RUNTIME_IDENTIFIER" \
  "$ROOT_DIR/script/package_unified_macos_release.sh" --unsigned

[[ -s "$APP_BUNDLE/Contents/Resources/FramePlayer.icns" ]]
[[ -x "$APP_BUNDLE/Contents/MacOS/FramePlayer.Avalonia" ]]
[[ -f "$APP_BUNDLE/Contents/MacOS/libframeplayer_ffmpeg_probe.dylib" ]]

runtime_dir="$APP_BUNDLE/Contents/MacOS/Runtime/macos/$MAC_RUNTIME_IDENTIFIER/ffmpeg"
missing_dylibs=()
for dylib in "${REQUIRED_DYLIBS[@]}"; do
  if [[ ! -f "$runtime_dir/$dylib" ]]; then
    missing_dylibs+=("$dylib")
  fi
done

if [[ ${#missing_dylibs[@]} -gt 0 ]]; then
  {
    echo "Missing required macOS FFmpeg runtime libraries in $runtime_dir:"
    printf '  %s\n' "${missing_dylibs[@]}"
  } >&2
  exit 1
fi

FRAMEPLAYER_MAC_CORPUS="$CORPUS_DIR" \
FRAMEPLAYER_MAC_REQUIRE_CORPUS=1 \
FRAMEPLAYER_MAC_APP_BUNDLE="$APP_BUNDLE" \
FRAMEPLAYER_AVALONIA_RUNTIME_BASE="$APP_BUNDLE/Contents/MacOS" \
FRAMEPLAYER_AVALONIA_APP_BASE_DIRECTORY="$APP_BUNDLE/Contents/MacOS" \
FRAMEPLAYER_AVALONIA_EXPORT_HOST_EXECUTABLE="$APP_BUNDLE/Contents/MacOS/FramePlayer.Avalonia" \
FRAMEPLAYER_GPU_BACKEND=cpu \
dotnet test "$ROOT_DIR/tests/FramePlayer.Avalonia.Tests/FramePlayer.Avalonia.Tests.csproj" \
  -c Release \
  -r "$MAC_RUNTIME_IDENTIFIER" \
  --filter "Category=ReleaseCandidate" \
  --logger "trx;LogFileName=macos-release-candidate.trx" \
  --results-directory "$RESULTS_DIR"

echo "macOS release-candidate validation passed."
echo "Corpus files: $(wc -l < "$RESULTS_DIR/corpus-files.txt" | tr -d ' ')"
echo "Results: $RESULTS_DIR"
