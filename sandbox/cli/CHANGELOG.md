# Changelog

All notable changes to the `agent-sandbox` launcher (the `AgentSandbox` .NET tool) and the kits embedded in it. The
format follows [Keep a Changelog](https://keepachangelog.com) and versions follow [SemVer](https://semver.org). The
first `## [x.y.z]` section must match `<Version>` in `src/AgentSandbox/AgentSandbox.csproj`: `dotnet cake` checks it.

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
