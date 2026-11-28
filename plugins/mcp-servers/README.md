# MCP servers

| Directory                      | What                                                                               |
|--------------------------------|------------------------------------------------------------------------------------|
| [`keychain-env`](keychain-env) | Starts a stdio MCP server with tokens from the macOS Keychain in its environment   |
| [`grafana`](grafana)           | `grafana-mcp`: Grafana's mcp-grafana with a fixed tool policy and a Keychain token |

Both are POSIX shell launchers for servers that run on the Mac (sandboxes reach them through the sbx MCP gateway,
[ADR 0005](../../docs/adr/0005-connect-agents-to-grafana-through-the-sbx-mcp-gateway.md)), not servers of their own,
so they have no Dockerfile. `agent-sandbox mcp setup` embeds and installs them; see
[`sandbox/README.md`](../../sandbox/README.md).

Tests: `<server>/tests/run.sh` (stubs instead of macOS `security` and mcp-grafana, so they run on Linux). CI:
[`.github/workflows/mcp-servers.yml`](../../.github/workflows/mcp-servers.yml) runs them and shellcheck; the
`agent-sandbox-cli` workflow also runs on changes here, since the tool embeds the scripts.
