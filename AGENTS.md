# ai-agents-tooling

Personal monorepo for AI agent tooling: skills, MCP servers, Claude plugins, system prompts, and Docker sandbox templates.

## Structure

- `skills/` — Coding agent skills (Claude Code, Cursor, shared); every skill in `skills/claude-code` ships in the
  sandbox image (ADR 0004), so a change there needs an image CHANGELOG entry
- `plugins/mcp-servers/` — MCP servers (prefer C# / .NET)
- `plugins/claude-plugins/` — Claude-specific plugins
- `prompts/system/` — System prompts for AI agents
- `prompts/templates/` — Reusable prompt templates
- `sandbox/` — Docker Sandbox (sbx) template image, v3 kit and launcher for the `markeli-agent` bot
- `docs/specs/` — Specs (`YYYY-MM-DD-<slug>.md`), merged before their implementation
- `docs/adr/` — Architecture decision records (`NNNN-<slug>.md`)
- `docs/plans/` — Implementation plans and design docs

## Conventions

- **MCP servers**: C# / .NET preferred; include a Dockerfile
- **Skills**: Markdown files following each agent's skill format
- **Prompts**: Plain Markdown, one file per prompt
- Each top-level component should have its own README
- Keep things minimal — avoid over-engineering scaffolding

## Checks and gotchas

- Sandbox image CI scripts (`.github/scripts/`): tests are `.github/scripts/tests/run.sh` (stubs for `docker`/`gh`,
  real throwaway git repos); the PR `lint` job of `sandbox-image.yml` also runs shellcheck, actionlint, hadolint and
  the Renovate validator.
- `agent-sandbox` launcher (`sandbox/cli`): run `dotnet tool restore && dotnet cake` there (Default: build and test;
  `--target=Format` checks formatting). `global.json` selects Microsoft.Testing.Platform (xunit.v3), so a plain
  `dotnet test` needs `--solution AgentSandbox.slnx`. System.CommandLine translates its help headings and parse errors
  to the system language: don't assert on them.
- sbx kits (`sandbox/kits/`): sbx reads a kit directory's descriptor only as `spec.yaml`, v3 descriptors included,
  not as `<name>.yaml` like the examples of the v3 spec. `sbx` isn't available in the agent sandbox: `make validate`
  and any sandbox creation run on the owner's host.
- `watch-pr` skill (`skills/claude-code/watch-pr`): tests are `tests/run.sh` there (stub `gh`/`glab` answering from
  numbered fixture files, `--once` runs; `WATCH_PR_POLLS`/`WATCH_PR_TRACE`/`WATCH_PR_SLEEP` are test hooks).
- In the sandbox, shellcheck dies with `commitBuffer: invalid argument` on files with emoji unless the locale is
  UTF-8: run `LC_ALL=C.UTF-8 shellcheck -x ...`.
- `renovate --platform=local --dry-run=extract` (via `npx --package renovate@<version> --`) only sees git-tracked
  files, `renovate.json` included: `git add -N` new files first, or it silently extracts with the default config.
