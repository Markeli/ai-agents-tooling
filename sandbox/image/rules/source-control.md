# Commits, pull requests and CI

The hard git rules (branching, no force push, no self-merge, stop on errors, co-author trailers) are in
`/etc/claude-code/AGENTS.md` and always apply.

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
  was not verified. Claims in the description and in docs (an API format, an environment variable, a tool's
  behaviour) are checked by running them or against the primary source first; what couldn't be checked is named.
- Before opening a large PR/MR (a new API, a rewrite, many files), have a subagent review the diff for correctness
  bugs and fix what it confirms; say so in the description. Mechanical bulk changes (hundreds of analyzer or rename
  sites) may also be made by a subagent: review its whole diff yourself and say which part it made.
- Assign the PR/MR to yourself (`markeli-agent`) and request review from the owner (GitHub `Markeli`, GitLab
  `markeli`): `gh pr create --assignee markeli-agent --reviewer Markeli ...`,
  `glab mr create --assignee markeli-agent --reviewer markeli ...`. If either can't be set, say so instead of
  dropping it silently.
- Reply to review comments in their threads; push fixes as new commits.
- After a merge that releases or deploys, follow the resulting pipelines to the end and report the outcome.
- Right after opening a PR/MR, watch it with the `/watch-pr` skill until it is merged or closed: it wakes you on the
  owner's comments, replies, reviews and failed CI.
- End every comment, reply and review body you post on a PR/MR with the line
  `<!-- watch-pr session=<$CLAUDE_CODE_SESSION_ID> -->` (the skill explains why).

### Stacked PRs/MRs

When several PRs/MRs change the same files (a CHANGELOG, a docs page) or one builds on another, stack them instead
of letting each conflict with `main`:

- Merge the lower PR's branch into the upper one with a normal merge commit and resolve the conflicts there, keeping
  the lower PR's entries first; never rebase or force-push a pushed branch.
- Set the upper PR's base to the lower PR's branch (`gh pr edit <n> --base <branch>`), so its diff shows only its own
  change, and comment the merge order (bottom-up) on each upper PR.
- After the lower PR is merged, merge the default branch into the next one if its base doesn't switch on its own.
- PRs/MRs open at the same time that touch dependencies, build files or shared code but aren't stacked: build and test
  them merged together before saying they can be merged in any order; each can be green alone and break `main`
  together.

### Failures not caused by the PR/MR

- A test that fails on CI but not because of this PR/MR (a flaky test merged earlier): fix it in a separate `test:` PR,
  comment on the affected PRs that it is unrelated and fixed there, and merge the default branch into them once the fix
  is merged, so CI reruns.

### Handoff issue

- When the work spans sessions (several PRs, an agreed plan), keep one handoff issue in the repository up to date after
  each merged PR and each decision of the owner: done PRs, the agreed next steps and order, decisions not to re-open,
  repository gotchas. A new session starts from it.

## CI workflows

- Pin GitHub Actions to a commit SHA, with the version in a trailing comment.
- Don't edit files synced from another repository (for Curiosus: `.editorconfig`, `build/`, synced workflows,
  `.claude/curiosus.md`, `website/`): change them at their source or say they need changing there.
