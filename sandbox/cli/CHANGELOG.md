# Changelog

All notable changes to the `agent-sandbox` launcher (the `AgentSandbox` .NET tool) and the kits embedded in it. The
format follows [Keep a Changelog](https://keepachangelog.com) and versions follow [SemVer](https://semver.org). The
first `## [x.y.z]` section must match `<Version>` in `src/AgentSandbox/AgentSandbox.csproj`: `dotnet cake` checks it.

## [0.2.0] - 2026-11-28

### Added

- `agent-sandbox mcp setup grafana --url <url> [--name personal-grafana] [--agent-item agent/personal-grafana]
  [--owner-item <item>]` sets up Grafana's mcp-grafana on the Mac for the sandboxes (through the sbx MCP gateway) and
  the owner's Claude Code (docs/specs/2026-11-28-grafana-mcp.md, ADR 0005). It writes the embedded `keychain-env` and
  `grafana-mcp` (`plugins/mcp-servers/`) to `~/.local/share/agent-sandbox/mcp/<tool version>/` and links them from
  `~/.local/bin`, checks that `mcp-grafana` and the Keychain items exist (never reading a token), registers the server
  in sbx with the bot's item and, with `--owner-item`, in Claude Code with the owner's, and prints what is left by
  hand: creating the tokens, the Claude Desktop entry, the Grafana server settings. Running it again changes nothing;
  a registration with other arguments is replaced. Exit 1 while a required step is missing.

### Changed

- `mcp` is a subcommand like `help`: write a repository directory named `mcp` as `./mcp`.

## [0.1.2] - 2026-11-14

### Fixed

- `sbx`, `git`, `ssh-agent` and `ssh-add` are looked up only in `PATH`, like a shell does. .NET looked in the current
  directory and the tool's own first, so a file named `sbx` in `~` broke the launcher, and an executable one in a
  repository would have run instead of the real program.
- A program that exists but can't run reports why (exit 126, e.g. `Permission denied`) instead of `command not found`.

## [0.1.1] - 2026-11-14

### Fixed

- A failing `sbx settings get ssh.agentSocketPath` (e.g. the sbx daemon isn't running) stops the launcher with sbx's
  own error and exit code instead of `sbx forwards ''` and advice to set the socket again.

## [0.1.0] - 2026-11-13

The first version of the launcher as a .NET global tool (ADR 0003), replacing the `agent-sandbox` shell script.

### Added

- Creates a clone-mode sandbox for a repository once, then attaches to Claude Code in it: `--name`, `--no-attach`,
  `--recreate` (refuses while `sandbox-<name>` has commits origin doesn't), `--yes`, `--force-pull`, and Claude Code
  arguments after `--`.
- Loads the bot's key into its own ssh-agent and checks that sbx forwards that agent's socket.
- Binds the `github` and `gitlab` secrets to the sandbox from the macOS keychain.
- Embeds every kit under `sandbox/kits/` and writes it to `~/.local/share/agent-sandbox/kits/<tool version>/`;
  `AGENT_SANDBOX_KIT` uses a kit directory instead.
- Prints the image version and revision the sandbox runs; `AGENT_SANDBOX_IMAGE` replaces the image.
- Needs sbx 0.51 or newer: the sandbox is sbx's `claude` agent with the image as `--template` and the
  `markeli-claude` v3 mixin kit (network rules, Plannotator port, gitlab credential, the bot's ssh-agent).

### Removed

- `AGENT_SANDBOX_PULL`: `--force-pull` replaces it.
