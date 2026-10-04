# ai-agents-tooling

A personal monorepo for AI agent tooling: skills for coding agents, MCP servers, Claude plugins, system prompts, and Docker sandbox templates.

## Directory Structure

```
skills/           AI coding agent skills
  claude-code/    Skills for Claude Code
  cursor/         Skills for Cursor
  shared/         Cross-agent shared skills and utilities
plugins/          AI platform plugins
  mcp-servers/    Model Context Protocol servers
  claude-plugins/ Claude-specific plugins
prompts/          Prompt libraries
  system/         System prompts for AI agents
  templates/      Reusable prompt templates
sandbox/          Docker Sandbox (sbx) image, kit and launcher for the markeli-agent bot
docs/             Documentation
  plans/          Implementation plans and design docs
```

## Quick Start

1. Clone the repository
2. Browse the relevant directory for the tooling you need
3. Each skill, plugin, or server has its own README with usage instructions

## CI/CD

GitHub Actions builds `sandbox/image` and publishes `ghcr.io/markeli/claude-sandbox` on every change to `main`.
See [sandbox/README.md](sandbox/README.md).
