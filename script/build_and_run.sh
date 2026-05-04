#!/usr/bin/env bash
set -euo pipefail

MODE="${1:-run}"
CONFIGURATION="${CONFIGURATION:-Debug}"
APP_NAME="${APP_NAME:-FramePlayer.Mac}"
BUNDLE_NAME="${BUNDLE_NAME:-Frame Player}"
BUNDLE_ID="${BUNDLE_ID:-com.frameplayer.app}"
MIN_SYSTEM_VERSION="${MIN_SYSTEM_VERSION:-13.0}"
APP_VERSION="${APP_VERSION:-0.1.0}"
APP_BUILD="${APP_BUILD:-}"

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="${PROJECT:-$ROOT_DIR/src/FramePlayer.Mac/FramePlayer.Mac.csproj}"
PUBLISH_ROOT="$ROOT_DIR/dist/publish"
DIST_DIR="$ROOT_DIR/dist"
APP_BUNDLE="$DIST_DIR/$BUNDLE_NAME.app"
APP_CONTENTS="$APP_BUNDLE/Contents"
APP_MACOS="$APP_CONTENTS/MacOS"
APP_RESOURCES="$APP_CONTENTS/Resources"
INFO_PLIST="$APP_CONTENTS/Info.plist"
APP_ICON_SOURCE="${APP_ICON_SOURCE:-$ROOT_DIR/src/FramePlayer.Mac/Assets/FramePlayer.icns}"
APP_ICON_NAME="${APP_ICON_NAME:-FramePlayer}"
MAC_RUNTIME_SOURCE="${MAC_RUNTIME_SOURCE:-$ROOT_DIR/Runtime/macos}"

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

DOTNET_BIN="$(resolve_dotnet)" || {
  echo "dotnet SDK not found. Install the SDK pinned by global.json first." >&2
  exit 1
}

if [[ -z "$APP_BUILD" ]]; then
  APP_BUILD="$(git -C "$ROOT_DIR" rev-list --count HEAD 2>/dev/null || printf '1')"
fi

DOTNET_BIN_DIR="$(cd "$(dirname "$DOTNET_BIN")" && pwd)"
if [[ -n "${DOTNET_ROOT:-}" ]]; then
  export DOTNET_ROOT
  export PATH="$DOTNET_ROOT:$PATH"
else
  export PATH="$DOTNET_BIN_DIR:$PATH"
fi

pkill -x "$APP_NAME" >/dev/null 2>&1 || true

build_app() {
  "$DOTNET_BIN" publish "$PROJECT" -c "$CONFIGURATION" -r osx-arm64 --self-contained true -p:PublishSingleFile=false -o "$PUBLISH_ROOT/osx-arm64"
  rm -rf "$APP_BUNDLE"
  mkdir -p "$APP_MACOS" "$APP_RESOURCES"
  cp -R "$PUBLISH_ROOT/osx-arm64/." "$APP_MACOS/"
  cp "$APP_ICON_SOURCE" "$APP_RESOURCES/$APP_ICON_NAME.icns"
  if [[ -d "$MAC_RUNTIME_SOURCE" ]]; then
    mkdir -p "$APP_MACOS/Runtime"
    cp -R "$MAC_RUNTIME_SOURCE" "$APP_MACOS/Runtime/"
  fi

  cat >"$INFO_PLIST" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleExecutable</key>
  <string>$APP_NAME</string>
  <key>CFBundleIdentifier</key>
  <string>$BUNDLE_ID</string>
  <key>CFBundleName</key>
  <string>$BUNDLE_NAME</string>
  <key>CFBundleDisplayName</key>
  <string>$BUNDLE_NAME</string>
  <key>CFBundleShortVersionString</key>
  <string>$APP_VERSION</string>
  <key>CFBundleVersion</key>
  <string>$APP_BUILD</string>
  <key>CFBundleIconFile</key>
  <string>$APP_ICON_NAME</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>LSMinimumSystemVersion</key>
  <string>$MIN_SYSTEM_VERSION</string>
  <key>NSHighResolutionCapable</key>
  <true/>
  <key>NSPrincipalClass</key>
  <string>NSApplication</string>
</dict>
</plist>
PLIST
}

open_app() {
  /usr/bin/open -n "$APP_BUNDLE"
}

build_app

case "$MODE" in
  run)
    open_app
    ;;
  --debug|debug)
    lldb -- "$APP_MACOS/$APP_NAME"
    ;;
  --logs|logs)
    open_app
    /usr/bin/log stream --info --style compact --predicate "process == \"$APP_NAME\""
    ;;
  --telemetry|telemetry)
    open_app
    /usr/bin/log stream --info --style compact --predicate "subsystem == \"$BUNDLE_ID\""
    ;;
  --verify|verify)
    open_app
    sleep 2
    pgrep -x "$APP_NAME" >/dev/null
    ;;
  --build-only|build-only)
    ;;
  *)
    echo "usage: $0 [run|--debug|--logs|--telemetry|--verify|--build-only]" >&2
    exit 2
    ;;
esac
