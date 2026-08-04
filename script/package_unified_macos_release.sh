#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ARTIFACT_VERSION="${PACKAGE_VERSION:-${VERSION:-2.1.0-rc.17}}"
SIGN_MODE="${SIGN_MODE:-auto}"
SIGNING_IDENTITY="${SIGNING_IDENTITY:-}"
DIST_DIR="$ROOT_DIR/dist"
APP_BUNDLE="$DIST_DIR/Frame Player.app"

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
  echo "Cross-packaging is not supported: requested $MAC_RUNTIME_IDENTIFIER on $HOST_RUNTIME_IDENTIFIER." >&2
  exit 2
fi

ARTIFACT_DIR="$ROOT_DIR/artifacts/$ARTIFACT_VERSION"
ZIP_ARCH="${MAC_RUNTIME_IDENTIFIER#osx-}"
ZIP_PATH="$ARTIFACT_DIR/FramePlayer-macOS-$ZIP_ARCH-$ARTIFACT_VERSION.zip"
RUNTIME_SOURCE_DIR="$ROOT_DIR/Runtime/macos/$MAC_RUNTIME_IDENTIFIER/ffmpeg"
RUNTIME_MANIFEST="$ROOT_DIR/Runtime/macos/$MAC_RUNTIME_IDENTIFIER/ffmpeg-runtime-manifest.json"

usage() {
  cat >&2 <<USAGE
usage: $0 [--unsigned|--sign [identity]]

Environment:
  PACKAGE_VERSION=<label>      Artifact version label. Default: 2.1.0-rc.17
  APP_VERSION=<version>        Bundle short version. Default: numeric segment from PACKAGE_VERSION
  MAC_RUNTIME_IDENTIFIER=<rid> Native macOS runtime. Default: current host (osx-arm64 or osx-x64)
  SIGNING_IDENTITY=<identity>  codesign identity name/hash

Signing notes:
  --sign without an identity auto-picks Developer ID Application first, then Apple Development.
  Apple Development is useful for local testing; Developer ID Application is the public release identity.
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --unsigned)
      SIGN_MODE="none"
      shift
      ;;
    --sign)
      SIGN_MODE="sign"
      if [[ $# -gt 1 && "${2:0:1}" != "-" ]]; then
        SIGNING_IDENTITY="$2"
        shift 2
      else
        shift
      fi
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

bundle_short_version_from_artifact() {
  local label="$1"
  if [[ "$label" =~ ([0-9]+)(\.([0-9]+))?(\.([0-9]+))? ]]; then
    local major="${BASH_REMATCH[1]}"
    local minor="${BASH_REMATCH[3]:-0}"
    local patch="${BASH_REMATCH[5]:-0}"
    printf '%s.%s.%s\n' "$major" "$minor" "$patch"
    return 0
  fi

  echo "Artifact version '$label' must contain a numeric version segment for the bundle short version." >&2
  return 1
}

BUNDLE_SHORT_VERSION="${APP_VERSION:-$(bundle_short_version_from_artifact "$ARTIFACT_VERSION")}"

mkdir -p "$ARTIFACT_DIR"

[[ -f "$RUNTIME_SOURCE_DIR/SHA256SUMS.txt" ]] || {
  echo "Missing pinned macOS FFmpeg checksum file: $RUNTIME_SOURCE_DIR/SHA256SUMS.txt" >&2
  exit 1
}
[[ -f "$RUNTIME_SOURCE_DIR/build-provenance.txt" ]] || {
  echo "Missing macOS FFmpeg build provenance: $RUNTIME_SOURCE_DIR/build-provenance.txt" >&2
  exit 1
}

if [[ "$SIGN_MODE" != "none" && ! -f "$RUNTIME_MANIFEST" ]]; then
  echo "Signed/public $MAC_RUNTIME_IDENTIFIER packaging requires a pinned runtime manifest: $RUNTIME_MANIFEST" >&2
  exit 1
fi

resolve_signing_identity() {
  if [[ -n "$SIGNING_IDENTITY" ]]; then
    echo "$SIGNING_IDENTITY"
    return 0
  fi

  local developer_id
  developer_id="$(security find-identity -v -p codesigning 2>/dev/null | awk '/Developer ID Application/ { print $2; exit }')"
  if [[ -n "$developer_id" ]]; then
    echo "$developer_id"
    return 0
  fi

  local development_id
  development_id="$(security find-identity -v -p codesigning 2>/dev/null | awk '/Apple Development/ { print $2; exit }')"
  if [[ -n "$development_id" ]]; then
    echo "$development_id"
    return 0
  fi

  return 1
}

sign_app_bundle() {
  local identity="$1"
  local entitlements="$ROOT_DIR/src/FramePlayer.Avalonia/FramePlayer.Avalonia.entitlements"
  local main_executable="$APP_BUNDLE/Contents/MacOS/FramePlayer.Avalonia"
  local timestamp_args=(--timestamp=none)

  if [[ "$identity" == Developer\ ID\ Application:* ]]; then
    timestamp_args=(--timestamp)
  fi

  while IFS= read -r item; do
    if [[ "$item" == "$main_executable" ]]; then
      continue
    fi

    codesign --force "${timestamp_args[@]}" --options runtime --sign "$identity" "$item"
  done < <(find "$APP_BUNDLE/Contents/MacOS" -type f | sort -r)

  refresh_runtime_checksums "$APP_BUNDLE/Contents/MacOS/Runtime/macos/$MAC_RUNTIME_IDENTIFIER/ffmpeg" "$identity" "${timestamp_args[@]}"
  codesign --force "${timestamp_args[@]}" --options runtime --entitlements "$entitlements" --sign "$identity" "$APP_BUNDLE"
  codesign --verify --deep --verbose=2 "$APP_BUNDLE"
}

refresh_runtime_checksums() {
  local runtime_dir="$1"
  local identity="$2"
  shift 2
  local timestamp_args=("$@")
  local runtime_checksums="$runtime_dir/SHA256SUMS.txt"
  local signed_checksums=""

  [[ -f "$runtime_checksums" ]] || {
    echo "Missing macOS runtime checksum file: $runtime_checksums" >&2
    return 1
  }

  while read -r expected_hash file_name || [[ -n "${expected_hash:-}" || -n "${file_name:-}" ]]; do
    expected_hash="${expected_hash%$'\r'}"
    if [[ -z "${expected_hash:-}" || "$expected_hash" == \#* ]]; then
      continue
    fi

    file_name="${file_name%$'\r'}"
    if [[ -z "${file_name:-}" || "$file_name" == */* || "$file_name" == "." || "$file_name" == ".." ]]; then
      echo "Invalid macOS runtime checksum entry: ${file_name:-<missing>}" >&2
      return 1
    fi

    local runtime_file="$runtime_dir/$file_name"
    [[ -f "$runtime_file" ]] || {
      echo "Missing signed macOS runtime file: $file_name" >&2
      return 1
    }

    local signed_hash
    signed_hash="$(shasum -a 256 "$runtime_file")"
    signed_checksums+="${signed_hash%% *}  $file_name"$'\n'
  done < "$runtime_checksums"

  [[ -n "$signed_checksums" ]] || {
    echo "The macOS runtime checksum file did not contain any file entries." >&2
    return 1
  }
  printf '%s' "$signed_checksums" > "$runtime_checksums"
  chmod 0644 "$runtime_checksums"
  codesign --force "${timestamp_args[@]}" --options runtime --sign "$identity" "$runtime_checksums"
}

validate_macos_runtime_checksums() {
  local runtime_dir="$APP_BUNDLE/Contents/MacOS/Runtime/macos/$MAC_RUNTIME_IDENTIFIER/ffmpeg"
  (
    cd "$runtime_dir"
    shasum -a 256 -c SHA256SUMS.txt
  )
}

rm -f "$ZIP_PATH" "$ZIP_PATH.sha256"

env -u VERSION \
  "$ROOT_DIR/scripts/Build-RustFfmpegProbe.sh" "$MAC_RUNTIME_IDENTIFIER"

env -u VERSION \
  CONFIGURATION=Release \
  APP_NAME=FramePlayer.Avalonia \
  APP_VERSION="$BUNDLE_SHORT_VERSION" \
  APP_INFORMATIONAL_VERSION="$ARTIFACT_VERSION" \
  BUNDLE_ID=com.frameplayer \
  APP_RUNTIME_IDENTIFIER="$MAC_RUNTIME_IDENTIFIER" \
  PROJECT="$ROOT_DIR/src/FramePlayer.Avalonia/FramePlayer.Avalonia.csproj" \
  APP_ICON_SOURCE="$ROOT_DIR/src/FramePlayer.Avalonia/Assets/FramePlayer.icns" \
  "$ROOT_DIR/script/build_and_run.sh" --build-only

[[ -x "$APP_BUNDLE/Contents/MacOS/FramePlayer.Avalonia" ]]
[[ -s "$APP_BUNDLE/Contents/Resources/FramePlayer.icns" ]]
/usr/libexec/PlistBuddy -c "Print :CFBundleShortVersionString" "$APP_BUNDLE/Contents/Info.plist" >/dev/null
/usr/libexec/PlistBuddy -c "Print :CFBundleVersion" "$APP_BUNDLE/Contents/Info.plist" >/dev/null
[[ -f "$APP_BUNDLE/Contents/MacOS/Runtime/macos/$MAC_RUNTIME_IDENTIFIER/ffmpeg/libavformat.62.dylib" ]]
[[ -f "$APP_BUNDLE/Contents/MacOS/Runtime/macos/$MAC_RUNTIME_IDENTIFIER/ffmpeg/libavfilter.11.dylib" ]]
[[ -f "$APP_BUNDLE/Contents/MacOS/Runtime/macos/$MAC_RUNTIME_IDENTIFIER/ffmpeg/SHA256SUMS.txt" ]]
[[ -f "$APP_BUNDLE/Contents/MacOS/Runtime/macos/$MAC_RUNTIME_IDENTIFIER/ffmpeg/build-provenance.txt" ]]
if [[ -f "$RUNTIME_MANIFEST" ]]; then
  [[ -f "$APP_BUNDLE/Contents/MacOS/Runtime/macos/$MAC_RUNTIME_IDENTIFIER/ffmpeg-runtime-manifest.json" ]]
fi
[[ -f "$APP_BUNDLE/Contents/MacOS/libframeplayer_ffmpeg_probe.dylib" ]]
if [[ "$SIGN_MODE" != "none" ]]; then
  resolved_identity="$(resolve_signing_identity)" || {
    echo "No usable codesigning identity found. Re-run with --unsigned or install a signing certificate." >&2
    exit 1
  }
  echo "Signing app bundle with: $resolved_identity"
  sign_app_bundle "$resolved_identity"
fi
validate_macos_runtime_checksums

(
  cd "$DIST_DIR"
  /usr/bin/ditto -c -k --keepParent "Frame Player.app" "$ZIP_PATH"
)

shasum -a 256 "$ZIP_PATH" > "$ZIP_PATH.sha256"

echo "Packaged unified macOS release candidate:"
echo "$ZIP_PATH"
cat "$ZIP_PATH.sha256"
