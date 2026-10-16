# Commits, pull requests and CI

The hard git rules (branching, no force push, no self-merge, stop on errors, co-author trailers) are in
`/etc/claude-code/CLAUDE.md` and always apply.

## Tools

Repositories live on GitHub and GitLab.com. Use `gh` for GitHub and `glab` for GitLab (PRs/MRs, issues, pipelines,
labels); GitHub/GitLab MCP only when the CLI can't do the task.

## Commit messages

Follow the repository's existing convention (check `git log`). Without one, use
[Conventional Commits](https://www.conventionalcommits.org):

```text
<type>(<scope>): <summary in the imperative, lower case, no period>

<body: why, not what>

Refs #123
BREAKING CHANGE: <what breaks and how to migrate>
```

- Types: `feat`, `fix`, `docs`, `test`, `refactor`, `perf`, `build`, `ci`, `chore`, `revert`.
- Scope: the affected package or component (in Curiosus repositories the package without the `Curiosus.` prefix,
  e.g. `feat(email.smtp): ...`); omit it for repository-wide changes.
- Breaking changes: `!` after the type/scope plus a `BREAKING CHANGE:` footer.
- Reference the task in the footer: `Refs #123`, or `Closes #123` when the commit completes it; another repository's
  issue as `Refs owner/repo#123`.

## Pull and merge requests

- One PR/MR is one logical change. Its title uses the commit message format, because squash merges take it; in
  libraries the title's type matches the version bump it causes.
- The description says what changed, why, how it was verified (which commands, which target frameworks), and what
  was not verified.
- Reply to review comments in their threads; push fixes as new commits.

## CI workflows

- Pin GitHub Actions to a commit SHA, with the version in a trailing comment.
- Don't edit files synced from another repository (for Curiosus: `.editorconfig`, `build/`, synced workflows,
  `.claude/curiosus.md`, `website/`): change them at their source or say they need changing there.
