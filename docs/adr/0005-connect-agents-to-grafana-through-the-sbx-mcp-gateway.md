# 0005. Connect agents to Grafana through mcp-grafana on the host and the sbx MCP gateway

- Status: Accepted
- Date: 2026-11-28
- Related: [spec](../specs/2026-11-28-grafana-mcp.md)

## Context

The bot (`markeli-agent`, in an sbx sandbox) and the owner's Claude Code / Claude Desktop on the Mac need the owner's
Grafana: metrics (a Prometheus-compatible data source), logs (VictoriaLogs), dashboards and alerting.
[mcp-grafana](https://github.com/grafana/mcp-grafana), Grafana's own MCP server, covers all of them; since v2.4.0 its
Loki tools also query VictoriaLogs (LogsQL). It runs locally over stdio or as an HTTP server.

sbx gives agents in a sandbox one MCP endpoint, a gateway on the host: servers are registered on the host
(`sbx mcp add`), local stdio servers run on the host, and their secrets stay there. The sbx docs say nothing about MCP
servers inside the VM and ask to treat local servers as trusted host integrations; `mcp:*` secrets are never injected
into sandboxes. The gateway has a dynamic mode (the default: the agent finds and attaches any server registered on the
host with `mcp-find`/`mcp-add`) and a static one (`--static-mcp`: the sandbox gets only the listed servers).

Any Grafana token reads every metric and log line, and the logs contain text from the internet.

## Decision drivers

- No token in the sandbox VM; on the Mac only in the Keychain.
- No new public services.
- One setup for the sandbox and for the owner's Claude Code / Desktop.
- As little own code and as few image releases as possible; setting up a Mac is one command.
- Revoke the bot's and the owner's access separately.

## Considered options

### Option 1: mcp-grafana on the Mac, reaching sandboxes through the sbx gateway

- Good: the sbx way; tokens stay on the host; no change to the image or the kit; one `brew upgrade` updates every
  client; two service accounts, revoked separately. Two small scripts plus an `agent-sandbox mcp setup grafana`
  subcommand that installs them (embedded like the kits) and registers the server make the host setup one command.
- Bad: mcp-grafana runs on the host, outside the sandbox isolation: a bug in it triggered by an agent's input would
  hit the Mac, not the VM (it only talks to the Grafana API, so the risk is small). Tokens are still issued in Grafana
  and put into the Keychain by hand.

### Option 2: mcp-grafana in the sandbox image

A binary in the image, a Claude Code plugin with `.mcp.json`, the token injected by a kit credential.

- Good: the server is isolated in the VM.
- Bad: bypasses the gateway (its secrets never reach the VM, so a kit credential is needed); an image release for
  every update; the Mac needs its own install anyway.

### Option 3: a shared mcp-grafana server next to Grafana (HTTP behind a reverse proxy)

- Good: one place to configure.
- Bad: a new public service inside the monitoring network, where the metrics and logs databases answer without
  authentication; one shared token; the most work.

### Option 4: no MCP, curl against the Grafana API or the `gcx` CLI

- Bad: `gcx` has no VictoriaLogs support; raw `/api/ds/query` requests are error-prone for an agent.

### Gateway mode: dynamic or static

Static mode keeps a sandbox away from host servers it was not given, but servers then have to be attached from the
host (an `agent-sandbox` option, `sbx mcp load`). With a single server registered on the host there is no difference.

## Decision

Chosen option: **option 1, with the gateway in dynamic mode**, because it is the supported sbx path, keeps tokens on
the host, serves the sandbox and the owner's clients with one install, and needs only two scripts and a setup
subcommand. Static mode waits
until other MCP servers (e.g. work ones) are registered on the host.

## Consequences

- Positive: the bot and the owner use Grafana without changes to the image or to sandbox creation; after a sandbox is
  recreated the agent attaches the server by itself; `agent-sandbox mcp setup` can take other servers later.
- Negative: any agent in any sandbox on the host can attach any registered server; mcp-grafana runs outside the
  isolation; write access is limited by the Grafana role and the tool list, not by the sandbox.
- Follow-ups: when other MCP servers are registered on the host, static mode and an `--mcp` option in `agent-sandbox`
  (a new ADR).
