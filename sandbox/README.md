# Sandbox: Claude Code as `markeli-agent`

Runs Claude Code in a [Docker Sandbox](https://docs.docker.com/ai/sandboxes/) (`sbx`, microVM) under the bot account
`markeli-agent`. The bot commits, pushes, opens PRs/MRs and answers review threads; the owner is a co-author and the only
one who merges. Tokens and SSH keys never enter the VM: the `sbx` host proxy injects API tokens, and only the bot's SSH
agent is forwarded.

```text
sandbox/
├── image/                  template image (ghcr.io/markeli/claude-sandbox), built by CI
│   ├── Dockerfile          .NET 9/10, Node.js LTS, glab, Plannotator, skills, plugins, bot git identity
│   ├── CHANGELOG.md        image version and release notes (see Image versioning)
│   ├── inventory.sh        lists the toolchain with versions (/etc/claude-code/inventory.sh)
│   ├── managed-settings.json  plugins, commit/PR attribution, Remote Control, claude.ai connectors off
│   ├── git-hooks/          system core.hooksPath: co-author trailers, chains to repository hooks
│   ├── CLAUDE.md           bot rules entry point, loaded as managed instructions
│   ├── rules/              topic rules the entry point tells the bot to read on demand
│   └── templates/          spec and ADR templates (/etc/claude-code/templates)
├── kits/markeli-claude/    v2 kit: extends built-in claude; network rules, Plannotator port, gitlab credential
└── bin/agent-sandbox       create-once-and-attach launcher for one repository
```

## Toolchain in the image

- **.NET**: 9.0, 10.0 SDKs (no .NET 8: `net8.0` tests run in CI only), `csharp-ls` (global dotnet tool,
  pinned by `CSHARP_LS_VERSION`)
- **Node.js**: current LTS from NodeSource, with corepack enabled and `pnpm`/`yarn` pre-activated for the `agent`
  user (no download/prompt on first use) — builds Docusaurus and Astro sites with npm, pnpm or yarn;
  global `typescript-language-server` with TypeScript 6 (TypeScript 7 is the native compiler without `tsserver`)
- **Source control**: `gh`, `glab`
- **IaC / config**: `terraform`, `yq`, `ansible-core`, `ansible-lint` (the last two via `uv tool install`)
- **CI / linting**: `actionlint`, `hadolint`, `shellcheck`, `shfmt`
- **Plan review**: [Plannotator](https://github.com/backnotprop/plannotator) — the `plannotator` binary, the
  `plannotator@plannotator` plugin and its `/plannotator-*` skills, all pinned by `PLANNOTATOR_VERSION` (see
  [Plannotator](#plannotator))
- **Claude Code plugins** from `claude-plugins-official`, enabled in `managed-settings.json`:
  - `superpowers` — brainstorming → plan → TDD → debugging → verification workflow
  - `csharp-lsp`, `typescript-lsp` — wire `csharp-ls` and `typescript-language-server` into Claude's LSP tool
  - `microsoft-docs` — Microsoft Learn MCP server (`learn.microsoft.com`, allowed in the kit)
  - `security-guidance` — pattern warnings on edits plus an LLM review of the diff on every turn end and on
    `git commit`; its `claude-agent-sdk` venv is prebuilt under `SECURITY_WARNINGS_STATE_DIR`. The reviews spend
    tokens; turn them off with `ENABLE_STOP_REVIEW=0` / `ENABLE_COMMIT_REVIEW=0`
  - `pr-review-toolkit` — review agents for tests, error handling, type design and comments before opening a PR
- **Claude Code skills**: `grill-me` (with the `grilling` skill it delegates to) from
  [mattpocock/skills](https://github.com/mattpocock/skills), pinned by `MATTPOCOCK_SKILLS_COMMIT`. They live in the
  managed skills directory `/etc/claude-code/.claude/skills`, because sbx mounts its shared skills store over
  `~/.claude/skills` and would hide anything baked there.
- **General purpose**: `build-essential`, `tree`, `zip`, `fd` (Debian's `fd-find`, symlinked), `sqlite3`,
  `postgresql-client`

## Commit and PR attribution

Every bot commit carries both `Co-Authored-By: Claude <noreply@anthropic.com>` and
`Co-authored-by: Maxim Markelow <markelow.dev@gmail.com>`, and every PR/MR description the
"🤖 Generated with [Claude Code](https://claude.com/claude-code)" line. Two layers make that happen:

1. **`attribution` in `managed-settings.json`** (plus a rule in the managed `CLAUDE.md`): Claude Code's own commit and
   PR attribution, extended with the owner's trailer. It is an instruction to the model, so it covers only commits
   and PRs that Claude Code writes itself.
2. **`git-hooks/` as the system `core.hooksPath`** (`/etc/git-hooks`): a `prepare-commit-msg` hook adds both trailers
   with `git interpret-trailers --if-exists addIfDifferent`, so commits made any other way (a script's plain
   `git commit`, `--amend`, `--no-verify`, merges, cherry-picks and rebases) get them too, exactly once. It does so
   only when the commit being created is authored by the bot (`markelow.dev+agent@gmail.com`): amending,
   cherry-picking or rebasing someone else's commit keeps that commit's author and leaves its message alone. git
   exports the new commit's author to the hook, except for the picks and fixups that cherry-pick and rebase commit
   in-process; there the hook reads the author of `CHERRY_PICK_HEAD` (pick) or `HEAD` (fixup) instead. `rebase
   --apply` never runs the hook. A system
   `core.hooksPath` makes git ignore `.git/hooks`, so every other standard hook name links to `chain`, which runs
   the repository's own hook with the same arguments, stdin and exit code; `prepare-commit-msg` runs it before
   adding the trailers, and `push-to-checkout` falls back to git's built-in `updateInstead` behaviour. An empty
   message is left alone, so git still aborts the commit.

A repository that sets its own `core.hooksPath` (husky, lefthook) — or a global one in the `~/.gitconfig` copied
from the host — overrides the system value; there only the `attribution` layer applies. PR/MR descriptions are
covered only by the `attribution` layer.

## Bot instructions

`image/CLAUDE.md` is copied to `/etc/claude-code/CLAUDE.md` and loaded in every session, so it holds only what applies
to every message: role, workflow, hard git rules, what to do with knowledge it discovers and sandbox limits, plus a
table of topic files. The topic rules in
`image/rules/` (`dotnet.md`, `libraries.md`, `source-control.md`, `specs-and-adrs.md`) land in `/etc/claude-code/rules/`
and are read by the bot with the Read tool before the matching work. They are referenced by plain paths on purpose: an
`@path` import would be loaded at startup and save nothing. A repository's own `CLAUDE.md`/`AGENTS.md` and synced
convention files such as Curiosus' `.claude/curiosus.md` take precedence.

The bot's own memory lives in `~/.claude` inside the VM and disappears on every recreate, so the entry point tells it to
put non-obvious repository facts (build gotchas, environment constraints, undocumented conventions) into that
repository's `AGENTS.md` through its PRs/MRs, where the owner reviews them like code. Personal preferences stay in its
memory; decisions with alternatives go to ADRs.

## Specs and ADRs

The bot rules (`image/rules/specs-and-adrs.md`) make the bot write a spec for user-visible features and
multi-PR work, and an ADR for decisions that are expensive to reverse or depart from a convention. Both follow the
templates in `image/templates/`, are written in English unless the project says otherwise, and land in each
repository under `docs/specs/YYYY-MM-DD-<slug>.md` and `docs/adr/NNNN-<slug>.md` (plans stay in `docs/plans/`).

A spec goes in its own PR before any implementation code, and the owner's merge is the approval. ADRs ship in the PR
that needs the decision and are superseded, never rewritten.

## Plannotator

When Claude exits plan mode (or on `/plannotator-review`, `/plannotator-annotate`, `/plannotator-last`), Plannotator
serves its review UI from inside the sandbox. The image sets `PLANNOTATOR_REMOTE=1` and `PLANNOTATOR_PORT=19432`, so it
listens on `0.0.0.0:19432` instead of a random loopback port, and the kit publishes container port 19432 to the host.

The host port is ephemeral, so several sandboxes don't collide. Find it with:

```bash
sbx ports <name>          # e.g. 127.0.0.1:53817 -> 19432/tcp; open http://127.0.0.1:53817 in the host browser
```

The URL Plannotator prints (`http://localhost:19432`) is the in-sandbox address; use the host port from `sbx ports`.
To get a stable URL for a single sandbox, pin it with `sbx ports <name> --publish 19432:19432`. Ports are fixed at
`sbx create`, so a sandbox created before this kit change needs `--recreate` (or that `sbx ports --publish` command).
With one fixed port, a sandbox runs one Plannotator session at a time.

Network: no extra allow rules. `PLANNOTATOR_SHARE=disabled` turns off share links, which would send plan content to
`share.plannotator.ai` or its paste service. The release check runs in the browser, so it reaches `api.github.com`
from the host, not from the sandbox. URL annotation tries `r.jina.ai` first and fetches the page directly when that
is blocked; both go through the sandbox network policy.

## Status line

`image/statusline.sh` is copied to `/etc/claude-code/statusline.sh` and wired up via `statusLine` in
`managed-settings.json`, so it survives `sbx create` regenerating `~/.claude/settings.json`. It mirrors the owner's
host status line (`user / host  path  [branch]  time  | model | ctx: N% used`), except the identity segment reads
`markeli-agent @ <sandbox name>` — the sandbox name comes from `$SANDBOX_NAME`, falling back to the hostname — and
the path is shown relative to the repository root (`<repo>/sub/dir`) instead of relative to `$HOME`: the workspace is
mounted at its host path (e.g. `/Users/...`), while `$HOME` in the sandbox is `/home/agent`, so the host's `~`
substitution never fires there. The path is bright magenta instead of the host's blue, which is barely readable on a
dark terminal.

## Remote Control

`remoteControlAtStartup` in `managed-settings.json` connects every interactive session to
[Remote Control](https://code.claude.com/docs/en/remote-control), so a session started in the sandbox can be continued
from claude.ai or the Claude mobile app. It needs a claude.ai login (not an API key) on a plan where Remote Control is
enabled (on Team/Enterprise an Owner turns it on in the Claude Code admin settings); sessions appear under the account
Claude Code is logged in with. It stays off when `CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC`, `DISABLE_TELEMETRY`,
`DO_NOT_TRACK` or `DISABLE_GROWTHBOOK` is set or `ANTHROPIC_BASE_URL` points elsewhere. A repository can opt out with
`"remoteControlAtStartup": false` in its `.claude/settings.json`; `--rc` / `/remote-control` still work per session.

## One-time setup (host)

1. Bot accounts `markeli-agent` on GitHub and GitLab.com, invited to the repositories (GitHub: collaborator;
   GitLab: Developer, no merge rights on the default branch).
2. Bot SSH key `~/.ssh/markeli_agent` (ed25519, with passphrase) registered in both accounts. Load it once so the
   passphrase lands in the login keychain:

   ```bash
   ssh-agent -a ~/.ssh/markeli-agent.sock
   SSH_AUTH_SOCK=~/.ssh/markeli-agent.sock ssh-add --apple-use-keychain ~/.ssh/markeli_agent
   ```

3. Bot API tokens in the keychain (GitHub: classic PAT `repo`; GitLab: PAT `api`):

   ```bash
   security add-generic-password -U -s agent/personal-github -a markeli-agent -w
   security add-generic-password -U -s agent/personal-gitlab -a markeli-agent -w
   ```

4. `sbx`:

   ```bash
   brew trust docker/tap && brew install docker/tap/sbx
   sbx login
   sbx policy init balanced
   sbx policy allow network "**:443" --method GET --path '/**'
   sbx policy allow network "**:80"  --method GET --path '/**'
   sbx settings set ssh.agentSocketPath ~/.ssh/markeli-agent.sock && sbx daemon restart
   ```

   The two `http` rules let every sandbox read any website (docs, blogs) without an approval, while other methods
   to hosts outside the allow-list stay blocked. Keep the port in the resource: a bare `**` also matches
   `github.com:22`/`gitlab.com:22`, and since SSH can't be inspected at L7 the proxy then drops it
   (`sbx policy log`: `l7 enforcement unavailable`). The rules can't live in `kits/markeli-claude/spec.yaml`: kit
   network entries are host/port patterns only, without methods or paths. GET still lets data out through the URL,
   so this guards against writes, not exfiltration.

## Use

```bash
sandbox/bin/agent-sandbox ~/Development/Personal/<repo>      # create once, then attach
sandbox/bin/agent-sandbox . -- -c                            # pass extra Claude Code args
sandbox/bin/agent-sandbox --no-attach ~/Development/Personal/<repo>   # create/ensure, don't attach
sandbox/bin/agent-sandbox --recreate ~/Development/Personal/<repo>    # see "Update or recreate a sandbox"
```

The first run asks to approve the kit's credentials (`anthropic`, `github`, `gitlab`); approvals are stored in
`~/.config/sbx/credentials.yaml`. The sandbox is named after the repository and works on a private clone; the bot
pushes branches to `origin` over SSH. Remove it with `sbx rm <name>`.

`--no-attach` creates the sandbox and binds its secrets (or confirms both already exist) without opening a shell —
useful for pre-provisioning a sandbox or driving it from another script.

### Several sandboxes for one repository

Pass `--name NAME` to run more than one sandbox against the same repository in parallel — e.g. to work on two
branches of `markeli.github.io` at once:

```bash
sandbox/bin/agent-sandbox --name blog-1 ~/Development/Personal/markeli.github.io
sandbox/bin/agent-sandbox --name blog-2 ~/Development/Personal/markeli.github.io
```

`NAME` must be lowercase letters, digits and hyphens, starting with a letter or digit (so a slip like
`--name --recreate` is rejected instead of naming a sandbox `--recreate`, and an empty `--name ''` is an error
rather than a silent fallback — only omitting `--name` uses the default); it replaces the repo-derived default
everywhere a sandbox is keyed by name — sandbox-scoped secrets, `sbx create`, `--recreate`'s `sandbox-<name>`
remote, `--no-attach`, and attach. Each named sandbox is its own independent clone with its own `sandbox-<name>`
remote on the host (see "Update or recreate a sandbox"); use a separate branch per sandbox so they don't push over
each other.

## Image versioning

The image version is the first `## [x.y.z] - yyyy-mm-dd` section of
[`sandbox/image/CHANGELOG.md`](image/CHANGELOG.md); changes not released yet sit under `## [Unreleased]`. The workflow
is `.github/workflows/sandbox-image.yml`, its decisions are in `.github/scripts/sandbox-image-plan.sh`, and the design
is in [the spec](../docs/specs/2026-10-23-sandbox-image-versioning.md) and ADRs
[0001](../docs/adr/0001-version-the-sandbox-image-from-its-changelog.md) and
[0002](../docs/adr/0002-update-pinned-image-inputs-with-renovate.md).

| Tag | Meaning |
| --- | --- |
| `x.y.z` | A release; immutable, CI never writes an existing one |
| `x.y`, `x` | The highest release of that minor or major line |
| `latest` | The highest release from `main` (maintenance releases never move it); the kit's default |
| `tree-<hash>` | One build per git tree of `sandbox/image`, never rebuilt; internal, deleted after 30 days unless released |
| `sha-<short>` | Legacy: no longer published; existing tags stay |

### SemVer for the image

- **Major**: something a sandbox or a user relies on is removed or changed incompatibly: a dropped SDK or runtime
  (like .NET 8), a removed tool or plugin, changed paths of the managed config (`/etc/claude-code/...`), the user or
  home layout, or the kit contract (ports, environment, credentials the image expects). Add a `**Breaking:**` note
  with what to change.
- **Minor**: added tools, plugins, rules or templates, or a new major version of a bundled tool.
- **Patch**: minor or patch bumps of pinned inputs, a new base image digest, fixes, and changes to rules and
  templates that don't add any.

### Releasing

1. Every PR that changes `sandbox/image` adds its entry under `## [Unreleased]` (`### Added`, `### Changed`,
   `### Removed`, `### Fixed`, `### Security`). CI, tests and internal refactoring need none.
2. To release, open a PR that turns `## [Unreleased]` into `## [x.y.z] - yyyy-mm-dd`, with the bump chosen from the
   entries by the rules above, and a summary of the Renovate updates merged since the last release.
3. The merge publishes it: CI points `x.y.z`, `x.y`, `x` and `latest` at the `tree-<hash>` image the PR built, and
   creates the git tag `claude-sandbox.v<x.y.z>` and a GitHub release with that CHANGELOG section, the toolchain
   changes since the previous release and `inventory.tsv`. Re-running a failed publish completes it. Publishes run
   one at a time; GitHub cancels a waiting publish when a third one queues, so re-run a cancelled one. The publish
   checks the tags again when it runs, so a late re-run never moves a tag back or takes Latest from a newer release.

Pushes with only `Unreleased` changes publish nothing. Every PR gets a release-check comment: whether the merge
releases a version, whether the change is under `Unreleased` (or ⚠️ has no entry), and a table of the toolchain
changes against the release it builds on (for example, the Claude Code version a base image bump brings). It never
fails the build.

### Updates through Renovate

Every input of the image is pinned: the base image by digest, tools in `ARG`s by version, the
`claude-plugins-official` marketplace and mattpocock skills by commit, GitHub Actions by SHA. The Renovate GitHub
App (`renovate.json`) opens one grouped PR for all non-major updates on Monday mornings (Europe/Moscow) and a separate
PR per major. Nothing is automerged. A base digest update rebuilds every layer, which also brings in Ubuntu security
updates. Renovate PRs don't edit the CHANGELOG: the release check flags them, and the next release PR summarises them.
.NET SDK channels (`DOTNET_CHANNELS`) stay a manual change. Apt packages and .NET SDK patch versions are resolved when
a tree is built.

### Inside a sandbox

`/etc/claude-code/image-release` holds `VERSION` (`x.y.z`, or `<x.y.z>+unreleased.<tree>` for a build of unreleased
changes), `TREE` (the `sandbox/image` tree hash) and `REVISION` (the commit the tree was built from: the PR head for
images built by a PR, which a merge retags without rebuilding, so it can differ from the `main` commit; the git tag
`claude-sandbox.v<x.y.z>` points at that one). The OCI labels `org.opencontainers.image.version`, `revision` and
`created` carry the same. `/etc/claude-code/inventory.sh` lists the toolchain as built. Claude Code and its plugins
keep auto-updating at runtime, so a long-running sandbox can be ahead of its inventory. `agent-sandbox` prints the
image reference, digest and version of the sandbox it creates or attaches to.

### Pinning the kit and rolling back

The kit (`sandbox/kits/markeli-claude/spec.yaml`) uses `latest`, and **existing sandboxes are unaffected** by new
releases: the image is resolved once, at `sbx create`. To keep new sandboxes on a known release, or to roll back,
set `image: ghcr.io/markeli/claude-sandbox:<x.y.z>` (or `:<x>` to follow a major) in `spec.yaml`, run
`make validate`, and recreate the sandbox (below). The GitHub releases list what changed between versions.

### Maintenance of an older major

When a new major is out and the previous one needs a fix:

1. The owner creates `release/<major>.x` from the last tag of that major (`claude-sandbox.v1.4.0`). Only admins can
   create or push `release/*` (ruleset below), since a push there publishes.
2. Fix on `main` first, then cherry-pick into a branch from `release/<major>.x` with a new `## [1.4.1]` section, and
   open the PR to `release/<major>.x`. The merge publishes `1.4.1` and moves `1.4` and `1`, but not `latest`, and its
   GitHub release is not marked Latest. Renovate updates `main` only.

### One-time setup (owner)

- Install the [Renovate GitHub App](https://github.com/apps/renovate) on this repository and review its onboarding PR.
- Rulesets: `release/**` can be created and pushed only by admins (the bot has push access), and release tags can't
  be moved or deleted. The workflow's `GITHUB_TOKEN` still creates the tags.

  ```bash
  gh api --method POST repos/Markeli/ai-agents-tooling/rulesets --input - <<'EOF'
  {"name": "release-branches", "target": "branch", "enforcement": "active",
   "conditions": {"ref_name": {"include": ["refs/heads/release/**"], "exclude": []}},
   "rules": [{"type": "creation"}, {"type": "update"}, {"type": "deletion"}, {"type": "non_fast_forward"}],
   "bypass_actors": [{"actor_id": 5, "actor_type": "RepositoryRole", "bypass_mode": "always"}]}
  EOF
  gh api --method POST repos/Markeli/ai-agents-tooling/rulesets --input - <<'EOF'
  {"name": "release-tags", "target": "tag", "enforcement": "active",
   "conditions": {"ref_name": {"include": ["refs/tags/claude-sandbox.v*"], "exclude": []}},
   "rules": [{"type": "update"}, {"type": "deletion"}, {"type": "non_fast_forward"}],
   "bypass_actors": [{"actor_id": 5, "actor_type": "RepositoryRole", "bypass_mode": "always"}]}
  EOF
  ```

- Cleanup: `.github/workflows/sandbox-image-cleanup.yml` runs weekly (and manually, as a dry run by default) and
  deletes `tree-*` and untagged versions older than 30 days, keeping releases, `latest`, `sha-*` and the parts of the
  images they point at. It uses `GITHUB_TOKEN`, which can delete versions only while this repository has the
  **Admin** role on the package: on the package page, Package settings → Manage Actions access, set
  `ai-agents-tooling` to Admin. Without it the job fails with a 403 instead of skipping.

## Update or recreate a sandbox

A sandbox never picks up a new image build or a kit change (network allow list, credentials, the `image:` tag)
after it's created — both are fixed at `sbx create` time. To pick up either, remove and recreate it:

```bash
make load                                                   # optional: build+load the image locally first
sandbox/bin/agent-sandbox --recreate ~/Development/Personal/<repo>
# or, equivalently and by hand:
sbx rm <name>
sandbox/bin/agent-sandbox ~/Development/Personal/<repo>                            # reuses a locally loaded image
AGENT_SANDBOX_PULL=always sandbox/bin/agent-sandbox ~/Development/Personal/<repo>  # forces the current remote `latest`
```

`agent-sandbox` prints the version it runs (`agent-sandbox: <name> runs <image>@<digest>, version 1.3.0`); compare
it with the [releases](https://github.com/Markeli/ai-agents-tooling/releases) to see what a recreate brings. A
local `make load` build reports `0.0.0+local`. To recreate on a specific release instead of `latest`, pin the kit
first ([Pinning the kit and rolling back](#pinning-the-kit-and-rolling-back)).

`--recreate` refuses to remove a sandbox whose clone has commits not pushed to `origin`, checked via the
`sandbox-<name>` git remote that clone mode adds to the host repo (no access into the sandbox itself is needed).
That remote (and its git-daemon) only exists while the sandbox is running, so `--recreate` starts a stopped
sandbox itself before checking — no need to `sbx exec <name> true` it awake first. The check **cannot** detect
uncommitted or staged-but-not-committed changes in the sandbox's working tree — the git-daemon that serves
`sandbox-<name>` only exposes refs, not working-tree state — so push or otherwise save anything you care about
before recreating, even when `--recreate` doesn't object.

Add `--yes` to skip `sbx rm`'s own confirmation prompt (it passes `--force` through), once the unpushed-commits
check above has already passed — useful for running `--recreate` non-interactively.

Removing a sandbox (via `--recreate`, `sbx rm`, or just letting it expire) loses, inside that VM:

- Unpushed commits and branches in the sandbox's clone — it's a standalone clone, not synced to the host.
- Anything installed or cached beyond the image itself: apt/npm/NuGet/`uv` packages, build caches.
- Docker images/containers built or pulled inside the sandbox's own Docker daemon (e.g. a test
  `docker build -t claude-sandbox:test sandbox/image`).
- Claude Code session/conversation history — `~/.claude` is regenerated on every `sbx create` regardless, so this
  is lost on every recreate, not just on removal.

## Known limits

- amd64-only images (SQL Server) do not run in the arm64 VM — run those tests on the host or in CI.
- `ssh.agentSocketPath` is global: every sandbox gets the personal bot key. Other scopes need their own launcher.
- claude.ai connectors are disabled (`ENABLE_CLAUDEAI_MCP_SERVERS=false`); they would act as the owner. Add MCP servers
  through the sbx gateway (`sbx mcp add`, `--static-mcp`).
- sbx custom secrets, kits and shared skills are experimental and may change between releases.
