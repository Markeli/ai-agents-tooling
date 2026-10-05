# Sandbox: Claude Code as `markeli-agent`

Runs Claude Code in a [Docker Sandbox](https://docs.docker.com/ai/sandboxes/) (`sbx`, microVM) under the bot account
`markeli-agent`. The bot commits, pushes, opens PRs/MRs and answers review threads; the owner is a co-author and the only
one who merges. Tokens and SSH keys never enter the VM: the `sbx` host proxy injects API tokens, and only the bot's SSH
agent is forwarded.

```text
sandbox/
├── image/                  template image (ghcr.io/markeli/claude-sandbox), built by CI on main
│   ├── Dockerfile          .NET 8/9/10, Node.js LTS, glab, csharp-ls, plugins, bot git identity
│   ├── managed-settings.json  plugins, Co-authored-by, claude.ai connectors off
│   └── CLAUDE.md           bot rules, loaded as managed instructions
├── kits/markeli-claude/    v2 kit: extends built-in claude, adds network rules and the gitlab credential
└── bin/agent-sandbox       create-once-and-attach launcher for one repository
```

## Toolchain in the image

- **.NET**: 8.0, 9.0, 10.0 SDKs, `csharp-ls`
- **Node.js**: current LTS from NodeSource, with corepack enabled and `pnpm`/`yarn` pre-activated for the `agent`
  user (no download/prompt on first use) — builds Docusaurus and Astro sites with npm, pnpm or yarn
- **Source control**: `gh`, `glab`
- **IaC / config**: `terraform`, `yq`, `ansible-core`, `ansible-lint` (the last two via `uv tool install`)
- **CI / linting**: `actionlint`, `hadolint`, `shellcheck`, `shfmt`
- **General purpose**: `build-essential`, `tree`, `zip`, `fd` (Debian's `fd-find`, symlinked), `sqlite3`,
  `postgresql-client`

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
   sbx settings set ssh.agentSocketPath ~/.ssh/markeli-agent.sock && sbx daemon restart
   ```

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

`NAME` must be lowercase letters, digits and hyphens only; it replaces the repo-derived default everywhere a
sandbox is keyed by name — sandbox-scoped secrets, `sbx create`, `--recreate`'s `sandbox-<name>` remote,
`--no-attach`, and attach. Each named sandbox is its own independent clone with its own `sandbox-<name>` remote on
the host (see "Update or recreate a sandbox"); use a separate branch per sandbox so they don't push over each
other.

## Image versioning

CI publishes two tags on every push to `main` (see `.github/workflows/sandbox-image.yml`): a moving `latest` and an
immutable `sha-<short>` pinned to the commit that built it. The kit (`sandbox/kits/markeli-claude/spec.yaml`)
references `latest` — the simplest option, and the one `agent-sandbox` already assumes with its `--pull missing`
default (below).

Pinning `image:` to a `sha-<short>` tag instead makes the whole sandbox fleet reproducible and immune to upstream
drift, at the cost of a manual, two-step bump: merge a toolchain change to `main`, wait for CI to publish the new
`sha-<short>` tag (copy it from the workflow run's job summary or the GHCR package page), then commit the updated
pin in `spec.yaml` before recreating sandboxes. `latest` needs no such follow-up — new sandboxes just pick up
whatever CI published most recently, with the trade-offs described below.

Either way, **existing sandboxes are unaffected**: the image is resolved once, at `sbx create` time, and never
re-pulled or re-resolved afterwards. Run `make validate` after editing `spec.yaml` (bumping the pin or anything
else) to check the kit descriptor before recreating sandboxes against it.

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

`--recreate` refuses to remove a sandbox whose clone has commits not pushed to `origin`, checked via the
`sandbox-<name>` git remote that clone mode adds to the host repo (no access into the sandbox itself is needed).
It **cannot** detect uncommitted or staged-but-not-committed changes in the sandbox's working tree — the
git-daemon that serves `sandbox-<name>` only exposes refs, not working-tree state — so push or otherwise save
anything you care about before recreating, even when `--recreate` doesn't object.

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
