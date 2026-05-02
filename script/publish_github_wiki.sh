#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SOURCE_DIR="$REPO_ROOT/docs/wiki"
WIKI_REMOTE="${FRAMEPLAYER_WIKI_REMOTE:-https://github.com/jfleezy23/frame-player.wiki.git}"
WIKI_BRANCH="${FRAMEPLAYER_WIKI_BRANCH:-master}"
ASSET_BRANCH="${FRAMEPLAYER_WIKI_ASSET_BRANCH:-main}"
RAW_BASE="${FRAMEPLAYER_WIKI_RAW_BASE:-https://raw.githubusercontent.com/jfleezy23/frame-player/${ASSET_BRANCH}}"

if [[ ! -d "$SOURCE_DIR" ]]; then
  echo "Wiki source directory not found: $SOURCE_DIR" >&2
  exit 1
fi

if ! compgen -G "$SOURCE_DIR/*.md" > /dev/null; then
  echo "No Wiki Markdown files found in $SOURCE_DIR" >&2
  exit 1
fi

TMP_DIR="$(mktemp -d)"
cleanup() {
  rm -rf "$TMP_DIR"
}
trap cleanup EXIT

WIKI_DIR="$TMP_DIR/frame-player.wiki"
if ! git clone "$WIKI_REMOTE" "$WIKI_DIR"; then
  echo "Wiki remote is not initialized yet; preparing first Wiki push."
  mkdir -p "$WIKI_DIR"
  git -C "$WIKI_DIR" init
  git -C "$WIKI_DIR" branch -M "$WIKI_BRANCH"
  git -C "$WIKI_DIR" remote add origin "$WIKI_REMOTE"
elif ! git -C "$WIKI_DIR" rev-parse --verify HEAD >/dev/null 2>&1; then
  git -C "$WIKI_DIR" checkout -B "$WIKI_BRANCH"
fi

find "$WIKI_DIR" -maxdepth 1 -type f -name '*.md' -delete
cp "$SOURCE_DIR"/*.md "$WIKI_DIR"/

for file in "$WIKI_DIR"/*.md; do
  perl -0pi -e "s#\\]\\(\\.\\./assets/screenshots/#](${RAW_BASE}/docs/assets/screenshots/#g" "$file"
  perl -0pi -e "s#\\]\\(docs/assets/screenshots/#](${RAW_BASE}/docs/assets/screenshots/#g" "$file"
  perl -0pi -e "s#\\]\\(/docs/assets/screenshots/#](${RAW_BASE}/docs/assets/screenshots/#g" "$file"
done

cd "$WIKI_DIR"

if [[ -z "$(git status --porcelain -- .)" ]]; then
  echo "Wiki already matches $SOURCE_DIR"
  exit 0
fi

git add -- *.md
git commit -m "docs: publish Frame Player wiki"
git push origin HEAD

echo "Published Wiki from $SOURCE_DIR"
