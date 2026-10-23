# ai-agents-tooling

Personal monorepo for AI agent tooling: skills, MCP servers, Claude plugins, system prompts, and Docker sandbox templates.

## Structure

- `skills/` — Coding agent skills (Claude Code, Cursor, shared)
- `plugins/mcp-servers/` — MCP servers (prefer C# / .NET)
- `plugins/claude-plugins/` — Claude-specific plugins
- `prompts/system/` — System prompts for AI agents
- `prompts/templates/` — Reusable prompt templates
- `sandbox/` — Docker Sandbox (sbx) template image, v2 kit and launcher for the `markeli-agent` bot
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
- In the sandbox, shellcheck dies with `commitBuffer: invalid argument` on files with emoji unless the locale is
  UTF-8: run `LC_ALL=C.UTF-8 shellcheck -x ...`.
- `renovate --platform=local --dry-run=extract` (via `npx --package renovate@<version> --`) only sees git-tracked
  files, `renovate.json` included: `git add -N` new files first, or it silently extracts with the default config.
