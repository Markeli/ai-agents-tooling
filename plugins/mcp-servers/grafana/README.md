# grafana-mcp

Gives agents access to a Grafana through [mcp-grafana](https://github.com/grafana/mcp-grafana), Grafana's own MCP
server: metrics, logs (Loki and, since mcp-grafana 2.4.0, VictoriaLogs), dashboards, folders, annotations, alerting
and local snapshots. mcp-grafana runs on the Mac; sandboxes reach it through the sbx MCP gateway, and the owner's
Claude Code and Claude Desktop start it themselves
([ADR 0005](../../../docs/adr/0005-connect-agents-to-grafana-through-the-sbx-mcp-gateway.md),
[spec](../../../docs/specs/2026-11-28-grafana-mcp.md)).

```text
grafana-mcp <grafana url> <keychain item>
```

The script sets `GRAFANA_URL`, puts `~/.local/bin`, `/opt/homebrew/bin` and `/usr/local/bin` first in `PATH` (a
process started by the sbx gateway may lack them) and runs mcp-grafana through
[`keychain-env`](../keychain-env), which reads the service account token from the Keychain item into
`GRAFANA_SERVICE_ACCOUNT_TOKEN`. The token is never in a config file or a process's arguments. The tool policy (below)
lives in the script, not in the registrations: `sbx mcp add --args` is a comma-separated list, and the tool lists
contain commas.

## Set up a Mac

```bash
brew install mcp-grafana
agent-sandbox mcp setup grafana --url https://grafana.example.com --owner-item owner/personal-grafana
```

`agent-sandbox mcp setup grafana --url <url> [--name personal-grafana] [--agent-item agent/personal-grafana]
[--owner-item <item>]` can be run again at any time (after a new `agent-sandbox` version it repoints the symlinks);
every step says what it did, found already done, or what is left to the owner, and the exit code is 1 while a required
step is missing. It:

1. writes `keychain-env` and `grafana-mcp` from the tool to `~/.local/share/agent-sandbox/mcp/<tool version>/` and
   points `~/.local/bin/keychain-env` and `~/.local/bin/grafana-mcp` at them (a file there that isn't such a symlink
   is left alone and reported);
2. checks that `mcp-grafana` is installed (`PATH`, `/opt/homebrew/bin`, `/usr/local/bin`);
3. checks that the Keychain items exist (never reading the tokens) and prints how to create a missing one: the bot's
   item always, the owner's only with `--owner-item`;
4. registers the server in sbx for the sandboxes, with the bot's item;
5. with `--owner-item` and `claude` in `PATH`, registers it in Claude Code (user scope) with the owner's item;
6. prints what it doesn't do: the Claude Desktop entry, the Grafana server settings and how to check the setup.

An existing registration with other arguments is replaced, one with the same arguments is left as it is.

### The same by hand

1. Tokens. In Grafana: Administration → Users and access → Service accounts → Add service account, one per client
   (the bot and the owner, so they can be revoked separately), role **Editor** → Add service account token (expiry:
   one year). Store each in the Keychain; `-w` without a value prompts for the token, so it stays out of the shell
   history:

   ```bash
   security add-generic-password -U -s agent/personal-grafana -a markeli-agent -w
   security add-generic-password -U -s owner/personal-grafana -a "$USER" -w
   ```

   On the first start macOS asks whether `security` may read the item: choose "Always Allow".

2. Scripts, from a checkout of this repository:

   ```bash
   mkdir -p ~/.local/bin
   install -m 0755 plugins/mcp-servers/keychain-env/keychain-env plugins/mcp-servers/grafana/grafana-mcp ~/.local/bin/
   ```

3. Registrations (full paths: the gateway and the clients don't expand `~`):

   ```bash
   sbx mcp add personal-grafana --command "$HOME/.local/bin/grafana-mcp" \
     --args https://grafana.example.com,agent/personal-grafana
   claude mcp add --scope user personal-grafana -- \
     "$HOME/.local/bin/grafana-mcp" https://grafana.example.com owner/personal-grafana
   ```

4. Claude Desktop: add the server to `mcpServers` in
   `~/Library/Application Support/Claude/claude_desktop_config.json` and restart the app:

   ```json
   "personal-grafana": {
     "command": "/Users/<you>/.local/bin/grafana-mcp",
     "args": ["https://grafana.example.com", "owner/personal-grafana"]
   }
   ```

5. Grafana server settings that keep data inside Grafana:

   ```ini
   GF_SNAPSHOTS_EXTERNAL_ENABLED=false
   GF_PUBLIC_DASHBOARDS_ENABLED=false
   ```

Sandboxes attach the server themselves: an agent finds it with `mcp-find` and attaches it with `mcp-add` (the
gateway's dynamic mode). To check: ask an agent to list the Grafana data sources, to show the error log lines of the
last hour, and to try an external snapshot, which Grafana must refuse with 403 "External dashboard creation is
disabled".

## Tools

mcp-grafana starts with `--disable-write`, only the categories below enabled, and only the listed write tools turned
back on (`--enable-write-tools`).

| Category                        | Read | Write tools on                                                                    |
|---------------------------------|------|-----------------------------------------------------------------------------------|
| `search`                        | yes  | –                                                                                 |
| `datasource`                    | yes  | – (data sources are never changed)                                                |
| `prometheus`, `loki`            | yes  | –                                                                                 |
| `dashboard`                     | yes  | `update_dashboard` (create or change; no deleting)                                |
| `folder`                        | yes  | `create_folder` (no deleting)                                                     |
| `navigation`                    | yes  | –                                                                                 |
| `alerting`                      | yes  | `alerting_rules_write`, `alerting_manage_routing`, `alerting_routing_write`, `alerting_silences_write` |
| `annotations`                   | yes  | `create_annotation`, `update_annotation`, `delete_annotation`                     |
| `snapshot`                      | yes  | `create_snapshot`, `delete_snapshot` (local only, see the server settings)        |
| every other category            | off  | off                                                                               |

Off on purpose: writes to data sources, deleting dashboards or folders, and the other categories of mcp-grafana
(users and teams, Grafana Cloud features and so on). The role is **Editor**: Viewer can't save dashboards,
annotations or alerting, while Admin could change data sources, users and settings, which nothing here needs. Editor
itself could still delete a dashboard through the API, so the tool list is the second limit.

After `brew upgrade mcp-grafana`, check that the server still starts (`claude mcp get personal-grafana`, or ask an
agent to list the data sources): a renamed category or tool makes mcp-grafana refuse to start or drops the tool.

## Changes made through the API

- **Dashboards.** A dashboard provisioned from files is overwritten from its file when Grafana restarts or the file
  changes, so an edit made through the API lasts only until then. Keep an edit by committing the dashboard JSON to
  the repository the dashboards are provisioned from.
- **Alerting.** Rules, contact points and notification policies created through the API live in Grafana's database.
  Once they settle, export them (Alerting → the rule or contact point → Export) into the provisioning files, so a
  rebuilt Grafana has them too.

## Revoke and rotate tokens

- Revoke: Administration → Users and access → Service accounts → the account → delete its token (or disable the
  account). The client using it gets 401 from then on; the other client keeps working.
- Rotate: add a new token to the same service account, store it with the same `security add-generic-password -U ...`
  command (`-U` updates the item), then delete the old token. Registrations don't change, since they name the item,
  not the token. A running server keeps the old token until it restarts (restart the client, or let the gateway
  start it again).

## Tests

`tests/run.sh` runs the script with keychain-env, its `security` stub and an `mcp-grafana` stub
(`tests/stubs/mcp-grafana`) that prints its arguments and environment, in a temporary `HOME`. CI:
`.github/workflows/mcp-servers.yml`.
