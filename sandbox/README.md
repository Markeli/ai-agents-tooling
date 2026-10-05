# Sandbox: Claude Code as `markeli-agent`

Runs Claude Code in a [Docker Sandbox](https://docs.docker.com/ai/sandboxes/) (`sbx`, microVM) under the bot account
`markeli-agent`. The bot commits, pushes, opens PRs/MRs and answers review threads; the owner is a co-author and the only
one who merges. Tokens and SSH keys never enter the VM: the `sbx` host proxy injects API tokens, and only the bot's SSH
agent is forwarded.

```text
sandbox/
├── image/                  template image (ghcr.io/markeli/claude-sandbox), built by CI on main
│   ├── Dockerfile          .NET 8/9/10, Node.js LTS, glab, plugins, bot git identity
│   ├── managed-settings.json  plugins, commit/PR attribution, claude.ai connectors off
│   ├── git-hooks/          system core.hooksPath: co-author trailers, chains to repository hooks
│   └── CLAUDE.md           bot rules, loaded as managed instructions
├── kits/markeli-claude/    v2 kit: extends built-in claude, adds network rules and the gitlab credential
└── bin/agent-sandbox       create-once-and-attach launcher for one repository
```

## Toolchain in the image

- **.NET**: 8.0, 9.0, 10.0 SDKs
- **Node.js**: current LTS from NodeSource, with corepack enabled and `pnpm`/`yarn` pre-activated for the `agent`
  user (no download/prompt on first use) — builds Docusaurus and Astro sites with npm, pnpm or yarn
- **Source control**: `gh`, `glab`
- **IaC / config**: `terraform`, `yq`, `ansible-core`, `ansible-lint` (the last two via `uv tool install`)
- **CI / linting**: `actionlint`, `hadolint`, `shellcheck`, `shfmt`
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
   `git commit`, `--amend`, `--no-verify`, merges, cherry-picks and rebases) get them too, exactly once. A system
   `core.hooksPath` makes git ignore `.git/hooks`, so every other standard hook name links to `chain`, which runs
   the repository's own hook with the same arguments, stdin and exit code; `prepare-commit-msg` runs it before
   adding the trailers, and `push-to-checkout` falls back to git's built-in `updateInstead` behaviour. An empty
   message is left alone, so git still aborts the commit.

A repository that sets its own `core.hooksPath` (husky, lefthook) — or a global one in the `~/.gitconfig` copied
from the host — overrides the system value; there only the `attribution` layer applies. PR/MR descriptions are
covered only by the `attribution` layer.

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
