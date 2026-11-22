# Changelog

All notable changes to the `ghcr.io/markeli/claude-sandbox` image. The format follows
[Keep a Changelog](https://keepachangelog.com), and versions follow the image SemVer rules in
[`sandbox/README.md`](../README.md#image-versioning): the first `## [x.y.z]` section is the image version.

## [Unreleased]

### Added

- Playwright (`PLAYWRIGHT_VERSION`) with headless Chromium in `/opt/ms-playwright`: the bot checks web pages in a
  browser without downloading one per session; `NODE_PATH` lets a one-off script `require('playwright')`.
- `trust-proxy-ca.sh`, run by a SessionStart hook: adds the sbx proxy CA to the agent's NSS database, which is the
  only trust store Chromium reads on Linux, so pages open through the proxy without certificate errors.

### Changed

- Source control rules: stacked PRs/MRs without force-push (merge the lower branch, retarget the base, comment the
  merge order), and PRs/MRs open at the same time that touch shared files are built together; a flaky test from
  another PR is fixed in a separate `test:` PR; a handoff issue is kept up to date for work spanning sessions; a
  subagent reviews large diffs before the PR/MR, and subagent-made bulk changes are reviewed and disclosed; claims in
  descriptions and docs are verified first; release and deploy pipelines are followed to the end after a merge.
- Library rules: ask the owner before a breaking change in curiosus-dev repositories; release a package together with
  or after the dependencies whose new API it uses; before working around a bug or gap of a Curiosus package in an app,
  check its `main`, `[Unreleased]` and issues, don't copy an unreleased fix into the app, open or comment on a library
  issue instead and ask the owner whether to wait.
- .NET rules: established mechanisms (Polly on a named `HttpClient`, `IAsyncDisposable`) over hand-written retries,
  counters and blocking calls.
- Sandbox limits: `lscr.io` is blocked (use the Docker Hub copies), third-party API calls need approval.
- The bot rules entry point is `/etc/claude-code/AGENTS.md`; `/etc/claude-code/CLAUDE.md` only imports it
  (`@AGENTS.md`), so the rules no longer live in a Claude-specific file. Nothing changes for the bot.
- Hard git rules: a 5xx error from GitHub/GitLab may be retried once after checking that the action didn't take
  effect; auth errors still stop the bot.

## [1.1.0] - 2026-11-14

### Added

- The `watch-pr` skill (`/watch-pr [<url>|<number>]`) and the `watch-pr` command: after opening a PR/MR, the bot
  watches it until it is merged or closed and is woken, without spending tokens in between, by the allow-listed
  authors' comments, threads, replies and reviews and by failed CI (GitHub and GitLab.com).
- `/etc/claude-code/watch-pr/authors`: the allow-list, the owner and `markeli-agent` (for sessions in other roles)
  on GitHub and GitLab.
- Source control rules: watch every PR/MR right after opening it, and end every PR/MR comment with the session
  marker.

### Changed

- The image is also built from the repository's `skills/claude-code`, whose skills ship in
  `/etc/claude-code/.claude/skills/`; `TREE` in `image-release` is the tree of both directories (ADR 0004).

## [1.0.2] - 2026-11-13

### Changed

- Pinned tools updated: `glab` 1.130.0, `yq` 4.54.3, `terraform` 1.17.2, Plannotator 0.28.64 (binary, plugin and
  `/plannotator-*` skills), `typescript-language-server` 6.1.0, `pnpm` 12.18.0, `ansible-core` 2.22.0,
  `ansible-lint` 26.11.0, `claude-agent-sdk` 0.2.181, the mattpocock skills to commit `5863806` and the
  `claude-plugins-official` marketplace to commit `099387b`.

## [1.0.1] - 2026-11-01

### Changed

- Pinned tools updated: `glab` 1.124.0, `terraform` 1.17.0, Plannotator 0.28.29 (binary, plugin and
  `/plannotator-*` skills), `claude-agent-sdk` 0.2.174, and the mattpocock skills to commit `5beb35b`.
- Source control rules: the bot assigns its PRs/MRs to itself (`markeli-agent`) and requests review from the owner.

## [1.0.0] - 2026-10-24

The first versioned release: the image as it was published from `latest` before versioning, plus the pinning below.

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
