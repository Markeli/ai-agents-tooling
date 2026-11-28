# keychain-env

Starts a command with secrets from the macOS Keychain in its environment. Meant as the command of a local stdio MCP
server, so the server's token lives only in the Keychain: not in an MCP config file, not in a process's arguments and
not in any output.

```text
keychain-env VAR=item [VAR=item ...] -- command [args...]
```

For each pair it runs `security find-generic-password -s <item> -w`, exports the value as `VAR` and then `exec`s the
command, which keeps its own arguments unchanged.

| Case                                         | Result                                                         |
|----------------------------------------------|----------------------------------------------------------------|
| an item doesn't exist                        | `keychain-env: keychain item <item> not found`, exit 1         |
| an item can't be read (access denied)        | `keychain-env: cannot read keychain item <item> (...)`, exit 1 |
| no pair, no `--`, no command, bad `VAR` name | usage on stderr, exit 2                                        |

The command doesn't start in any of these cases. `VAR` must match `[A-Za-z_][A-Za-z0-9_]*`.

Create an item so that the token stays out of the shell history (`-w` without a value prompts for it):

```bash
security add-generic-password -U -s agent/example -a "$USER" -w
```

On the first read macOS asks whether `security` may access the item: choose "Always Allow", or a server started in
the background fails until someone answers the prompt.

[`../grafana`](../grafana) uses it; `agent-sandbox mcp setup` installs it as `~/.local/bin/keychain-env`.

## Tests

`tests/run.sh` runs the script with a stub `security` (`tests/stubs/security`) that answers from fixture files, so
the tests also run on Linux. CI: `.github/workflows/mcp-servers.yml`.
