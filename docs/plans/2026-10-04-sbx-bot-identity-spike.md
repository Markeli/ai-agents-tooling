# Spike: coding agent as a bot inside Docker Sandboxes

Date: 2026-10-04 · sbx v0.46.0 · Claude Code 2.1.280 · test repo `curiosus-dev/Curiosus.Migrations` (PR #59, closed)

## Goal

The agent works under its own identity (`markeli-agent`): authors commits, pushes, opens PRs/MRs, answers review
threads. The owner stays co-author and the only one who merges. The agent must not be able to act as the owner.

## Decision

Custom template image on top of `docker/sandbox-templates:claude-code-docker`, run through a v2 kit that
`extends: claude`. A v3 workload is not needed: managed Claude Code config under `/etc/claude-code/` covers plugins and
global instructions, and the built-in kit keeps OAuth login, the MCP gateway and token injection.

## Results

| Check | Result |
|---|---|
| Claude subscription login inside the sandbox | ✅ stored on the host as a global `anthropic` OAuth secret |
| `gh` as the bot, token never in the VM | ✅ `sbx secret set github --command 'security find-generic-password …'` |
| `glab` as the bot | ✅ proxy substitution verified with `curl` for gitlab.com and self-hosted GitLab |
| Push over SSH with the bot key only | ✅ `ssh.agentSocketPath` → bot-only agent |
| Author `markeli-agent`, owner as co-author | ✅ GitHub links the `Co-authored-by` trailer to the owner's account |
| Plugins via `/etc/claude-code/managed-settings.json` | ✅ scope `managed`, skills visible |
| Global rules via `/etc/claude-code/CLAUDE.md` | ✅ |
| PR → owner comment → bot reply (issue comment and inline thread) | ✅ |
| .NET build, unit tests, Testcontainers PostgreSQL | ✅ |
| Testcontainers SQL Server | ❌ amd64-only image, arm64 VM has no emulation |

## Gotchas

1. sbx copies the host user's git identity into the sandbox `~/.gitconfig`; there is no setting to turn it off. The bot
   identity is pinned with `GIT_AUTHOR_*`/`GIT_COMMITTER_*` env vars in the image.
2. `url.<ssh>.insteadOf` rewrites anonymous HTTPS clones too (plugin marketplace install fails). Use `pushInsteadOf`.
3. The `balanced` policy allows `nuget.org` but not `api.nuget.org`; SSH (:22) is closed for every host.
4. claude.ai connectors (Gmail, Drive, corporate MCPs) arrive with the subscription login and bypass host network
   policy through `mcp-proxy.anthropic.com`. Disabled with `ENABLE_CLAUDEAI_MCP_SERVERS=false`.
5. sbx forwards the SSH agent of whichever client creates/starts/joins the sandbox unless `ssh.agentSocketPath` is set.
6. Custom secrets (`set-custom`) replace the placeholder only in headers of the bound host, so git over HTTPS with Basic
   auth cannot use them. The kit declares a `gitlab` service with a `PRIVATE-TOKEN` header instead.
7. Third-party kits, including a kit that extends a built-in agent, need an approved credential binding
   (`~/.config/sbx/credentials.yaml`); without it the credential is silently withheld.
8. Clone mode adds a `sandbox-<name>` remote to the host repository; `sbx rm` removes it.
9. Host `~/.claude` is not imported; `~/.claude/settings.json` and `~/.claude.json` in the sandbox are regenerated on
   every create.

## Open

- Verify `glab` end to end on a GitLab.com repository through the kit credential.
- Per-scope SSH agents (work vs personal) — `ssh.agentSocketPath` is global.
- MCP servers through the sbx gateway for real tasks.
