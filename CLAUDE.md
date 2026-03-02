# ai-agents-tooling

Personal monorepo for AI agent tooling: skills, MCP servers, Claude plugins, system prompts, and Docker sandbox templates.

## Structure

- `skills/` — Coding agent skills (Claude Code, Cursor, shared)
- `plugins/mcp-servers/` — MCP servers (prefer C# / .NET)
- `plugins/claude-plugins/` — Claude-specific plugins
- `prompts/system/` — System prompts for AI agents
- `prompts/templates/` — Reusable prompt templates
- `sandbox/` — Docker sandbox templates
- `docs/plans/` — Implementation plans and design docs

## Conventions

- **MCP servers**: C# / .NET preferred; include a Dockerfile
- **Skills**: Markdown files following each agent's skill format
- **Prompts**: Plain Markdown, one file per prompt
- Each top-level component should have its own README
- Keep things minimal — avoid over-engineering scaffolding
