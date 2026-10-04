# Sandbox: Claude Code as `markeli-agent`

Runs Claude Code in a [Docker Sandbox](https://docs.docker.com/ai/sandboxes/) (`sbx`, microVM) under the bot account
`markeli-agent`. The bot commits, pushes, opens PRs/MRs and answers review threads; the owner is a co-author and the only
one who merges. Tokens and SSH keys never enter the VM: the `sbx` host proxy injects API tokens, and only the bot's SSH
agent is forwarded.

```text
sandbox/
├── image/                  template image (ghcr.io/markeli/claude-sandbox), built by CI on main
│   ├── Dockerfile          .NET 8/9/10, glab, csharp-ls, plugins, bot git identity
│   ├── managed-settings.json  plugins, Co-authored-by, claude.ai connectors off
│   └── CLAUDE.md           bot rules, loaded as managed instructions
├── kits/markeli-claude/    v2 kit: extends built-in claude, adds network rules and the gitlab credential
└── bin/agent-sandbox       create-once-and-attach launcher for one repository
```

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
```

The first run asks to approve the kit's credentials (`anthropic`, `github`, `gitlab`); approvals are stored in
`~/.config/sbx/credentials.yaml`. The sandbox is named after the repository and works on a private clone; the bot
pushes branches to `origin` over SSH. Remove it with `sbx rm <name>`.

## Change the image

```bash
make load        # build ghcr.io/markeli/claude-sandbox:latest locally and load it into sbx
make validate    # check the kit descriptor
```

`agent-sandbox` creates sandboxes with `--pull missing`, so a locally loaded image wins until you remove it;
set `AGENT_SANDBOX_PULL=always` to take the CI build. Pushing to `main` rebuilds and publishes the image
(`linux/arm64`).

## Known limits

- amd64-only images (SQL Server) do not run in the arm64 VM — run those tests on the host or in CI.
- `ssh.agentSocketPath` is global: every sandbox gets the personal bot key. Other scopes need their own launcher.
- claude.ai connectors are disabled (`ENABLE_CLAUDEAI_MCP_SERVERS=false`); they would act as the owner. Add MCP servers
  through the sbx gateway (`sbx mcp add`, `--static-mcp`).
- sbx custom secrets, kits and shared skills are experimental and may change between releases.
