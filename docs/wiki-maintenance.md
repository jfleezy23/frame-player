# Wiki Maintenance

The GitHub Wiki is published from reviewed source files in this repository.

## Source Of Truth

- Wiki source: `docs/wiki/*.md`
- Publish script: `script/publish_github_wiki.sh`

Do not edit the GitHub Wiki directly unless you are making an emergency correction. Normal changes should go through a PR against `docs/wiki/`.

## README Policy

The README is intentionally short. It should answer:

- what Frame Player is
- where to download the current Windows and macOS builds
- what the main features are
- where the Wiki, releases, security notes, and third-party notices live

Detailed usage, troubleshooting, build, validation, and runtime notes belong in the Wiki or existing docs files.

## Publishing The Wiki

After the docs PR merges to `main`, run:

```bash
script/publish_github_wiki.sh
```

The script clones `https://github.com/jfleezy23/frame-player.wiki.git`, copies `docs/wiki/*.md`, commits only the Wiki repository, and pushes it.

Verify the result at:

- [Frame Player Wiki](https://github.com/jfleezy23/frame-player/wiki)
- [Frame Player repository home](https://github.com/jfleezy23/frame-player)
