#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ARTIFACT_VERSION="${PACKAGE_VERSION:-${VERSION:-unified-preview-0.2.0}}"
SIGN_MODE="${SIGN_MODE:-auto}"
SIGNING_IDENTITY="${SIGNING_IDENTITY:-}"
DIST_DIR="$ROOT_DIR/dist"
APP_BUNDLE="$DIST_DIR/Frame Player.app"
ARTIFACT_DIR="$ROOT_DIR/artifacts/$ARTIFACT_VERSION"
ZIP_PATH="$ARTIFACT_DIR/FramePlayer-macOS-arm64-$ARTIFACT_VERSION.zip"

mkdir -p "$ARTIFACT_DIR"

usage() {
  cat >&2 <<USAGE
usage: $0 [--unsigned|--sign [identity]]

Environment:
  PACKAGE_VERSION=<label>      Artifact version label. Default: unified-preview-0.2.0
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

resolve_signing_identity() {
  if [[ -n "$SIGNING_IDENTITY" ]]; then
    echo "$SIGNING_IDENTITY"
    return 0
  fi

  local developer_id
  developer_id="$(security find-identity -v -p codesigning 2>/dev/null | awk -F'\"' '/Developer ID Application/ { print $2; exit }')"
  if [[ -n "$developer_id" ]]; then
    echo "$developer_id"
    return 0
  fi

  local development_id
  development_id="$(security find-identity -v -p codesigning 2>/dev/null | awk -F'\"' '/Apple Development/ { print $2; exit }')"
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

  codesign --force "${timestamp_args[@]}" --options runtime --entitlements "$entitlements" --sign "$identity" "$APP_BUNDLE"
  codesign --verify --deep --verbose=2 "$APP_BUNDLE"
}

env -u VERSION \
  CONFIGURATION=Release \
  APP_NAME=FramePlayer.Avalonia \
  APP_VERSION=0.2.0 \
  BUNDLE_ID=com.frameplayer.unified-preview \
  PROJECT="$ROOT_DIR/src/FramePlayer.Avalonia/FramePlayer.Avalonia.csproj" \
  APP_ICON_SOURCE="$ROOT_DIR/src/FramePlayer.Avalonia/Assets/FramePlayer.icns" \
  "$ROOT_DIR/script/build_and_run.sh" --build-only

[[ -x "$APP_BUNDLE/Contents/MacOS/FramePlayer.Avalonia" ]]
[[ -s "$APP_BUNDLE/Contents/Resources/FramePlayer.icns" ]]
/usr/libexec/PlistBuddy -c "Print :CFBundleShortVersionString" "$APP_BUNDLE/Contents/Info.plist" >/dev/null
/usr/libexec/PlistBuddy -c "Print :CFBundleVersion" "$APP_BUNDLE/Contents/Info.plist" >/dev/null
[[ -f "$APP_BUNDLE/Contents/MacOS/Runtime/macos/osx-arm64/ffmpeg/libavformat.62.dylib" ]]
[[ -f "$APP_BUNDLE/Contents/MacOS/Runtime/macos/osx-arm64/ffmpeg/libavfilter.11.dylib" ]]

if [[ "$SIGN_MODE" != "none" ]]; then
  resolved_identity="$(resolve_signing_identity)" || {
    echo "No usable codesigning identity found. Re-run with --unsigned or install a signing certificate." >&2
    exit 1
  }
  echo "Signing app bundle with: $resolved_identity"
  sign_app_bundle "$resolved_identity"
fi

rm -f "$ZIP_PATH"
(
  cd "$DIST_DIR"
  COPYFILE_DISABLE=1 /usr/bin/ditto -c -k --norsrc --keepParent "Frame Player.app" "$ZIP_PATH"
)

shasum -a 256 "$ZIP_PATH" > "$ZIP_PATH.sha256"

echo "Packaged unified macOS release candidate:"
echo "$ZIP_PATH"
cat "$ZIP_PATH.sha256"
