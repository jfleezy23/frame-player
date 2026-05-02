#!/usr/bin/env bash
set -euo pipefail

MODE="${1:-run}"
CONFIGURATION="${CONFIGURATION:-Debug}"
APP_NAME="FramePlayer.Mac"
BUNDLE_NAME="Frame Player"
BUNDLE_ID="com.frameplayer.app"
MIN_SYSTEM_VERSION="13.0"

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
DOTNET_BIN="$DOTNET_ROOT/dotnet"
PROJECT="$ROOT_DIR/src/FramePlayer.Mac/FramePlayer.Mac.csproj"
PUBLISH_ROOT="$ROOT_DIR/dist/publish"
DIST_DIR="$ROOT_DIR/dist"
APP_BUNDLE="$DIST_DIR/$BUNDLE_NAME.app"
APP_CONTENTS="$APP_BUNDLE/Contents"
APP_MACOS="$APP_CONTENTS/MacOS"
APP_RESOURCES="$APP_CONTENTS/Resources"
INFO_PLIST="$APP_CONTENTS/Info.plist"
APP_ICON_SOURCE="$ROOT_DIR/src/FramePlayer.Mac/Assets/FramePlayer.icns"
APP_ICON_NAME="FramePlayer"
MAC_RUNTIME_SOURCE="$ROOT_DIR/Runtime/macos"

if [[ ! -x "$DOTNET_BIN" ]]; then
  echo "dotnet SDK not found at $DOTNET_BIN. Install the SDK pinned by global.json first." >&2
  exit 1
fi

export DOTNET_ROOT
export PATH="$DOTNET_ROOT:$PATH"

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
