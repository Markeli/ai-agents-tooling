# Changelog

All notable changes to the `ghcr.io/markeli/claude-sandbox` image. The format follows
[Keep a Changelog](https://keepachangelog.com), and versions follow the image SemVer rules in
[`sandbox/README.md`](../README.md#image-versioning): the first `## [x.y.z]` section is the image version.

## [Unreleased]

The baseline for 1.0.0: the image as it was published from `latest` before versioning, plus the pinning below.

### Added

- Toolchain on `docker/sandbox-templates:claude-code-docker` (Claude Code, `gh`, `uv`, Python): .NET 9.0 and 10.0
  SDKs and `csharp-ls`; Node.js 24 LTS from NodeSource with corepack, pnpm and yarn; TypeScript 6 and
  `typescript-language-server`; `glab`; `terraform`, `yq`, `ansible-core`, `ansible-lint`; `actionlint`,
  `hadolint`, `shellcheck`, `shfmt`; Plannotator; `build-essential`, `tree`, `zip`, `fd`, `sqlite3`,
  `postgresql-client`.
- Claude Code plugins from `claude-plugins-official`: `superpowers`, `csharp-lsp`, `typescript-lsp`,
  `microsoft-docs`, `security-guidance` (with a prebuilt `claude-agent-sdk` venv), `pr-review-toolkit`; and
  `plannotator@plannotator`.
- Skills in `/etc/claude-code/.claude/skills`: `grill-me` and `grilling` from mattpocock/skills, and the
  `/plannotator-*` skills.
- Managed config in `/etc/claude-code`: `managed-settings.json` (plugins, commit and PR attribution, status line,
  Remote Control at startup, claude.ai connectors off), the bot's `CLAUDE.md`, `rules/`, spec and ADR `templates/`,
  `statusline.sh`.
- The `markeli-agent` git identity, pushes over SSH, and system git hooks that add both co-author trailers to every
  commit.
- `/etc/claude-code/image-release` with the image `VERSION`, `TREE` and `REVISION`, and
  `/etc/claude-code/inventory.sh`, which lists the toolchain with versions.

### Changed

- Every input is pinned and updated through Renovate PRs instead of a weekly rebuild without cache: the base image by
  release tag and digest, the `claude-plugins-official` marketplace by commit, and pnpm, yarn, ansible-core,
  ansible-lint and claude-agent-sdk by version (previously `latest`/`stable`/unpinned).
