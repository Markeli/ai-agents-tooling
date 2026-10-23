# 0002. Pin every sandbox image input and update the pins with Renovate

- Status: Accepted
- Date: 2026-10-23
- Related: [spec](../specs/2026-10-23-sandbox-image-versioning.md), https://github.com/Markeli/ai-agents-tooling/issues/11

## Context

A weekly scheduled CI run rebuilds the image without cache, so it picks up a new base image
(`docker/sandbox-templates:claude-code-docker`, and with it Claude Code), Ubuntu security updates, and whatever the
unpinned inputs resolve to that day: marketplace plugins, `pnpm@latest`, `yarn@stable`, `claude-agent-sdk`,
ansible-core, ansible-lint. The rebuild changes `latest` with no review and no record of what changed. Meanwhile the
pinned inputs (`ARG <TOOL>_VERSION`, the mattpocock skills commit, GitHub Actions on major tags) are never updated.

With every input pinned, rebuilding an unchanged tree has nothing to pick up. Updates have to come from somewhere else,
as reviewable changes.

Inputs to update:

- the base image digest in `FROM`;
- versions in Dockerfile `ARG`s from GitHub and GitLab releases, npm, NuGet, PyPI, and Node.js releases;
- git commits in `ARG`s (`MATTPOCOCK_SKILLS_COMMIT`, the `claude-plugins-official` marketplace commit);
- GitHub Actions pinned to SHAs, as `/etc/claude-code/rules/source-control.md` requires.

## Decision drivers

- Covers all four kinds of inputs, `ARG` values and git refs included.
- Updates arrive as PRs the owner reviews and merges; no automerge.
- Weekly cadence like today, but explicit; routine updates grouped so they don't flood the owner.
- No infrastructure to run or maintain.
- Curiosus uses Dependabot; departing from that needs a reason.

## Considered options

### Option 1: Dependabot

- Good: built into GitHub; the Curiosus convention; handles the `FROM` digest (docker ecosystem) and GitHub Actions.
- Bad: can't update versions held in Dockerfile `ARG`s or arbitrary git commits, which is most of the inputs here.
  They would stay frozen or need a second mechanism.

### Option 2: Renovate (Mend-hosted GitHub App)

- Good: covers `FROM` digests, GitHub Actions SHA pinning with version comments, and, through regex custom managers
  with `# renovate:` comments, `ARG` versions from any datasource and git commits (`git-refs`). Grouping, schedules,
  `allowedVersions` (typescript `<7`) and a dependency dashboard are configuration. Hosted, so nothing to run.
- Bad: a third-party app with write access to PR branches; a new config file and a regex the owner has to read.
  The hosted app allows only an undocumented list of `postUpgradeTasks` commands, so Renovate can't reliably write a
  CHANGELOG entry.

### Option 3: Keep the weekly rebuild, pin nothing

- Good: no new tooling; security updates arrive automatically.
- Bad: what this issue exists to remove: unreviewed changes to `latest`, no immutable tag for them, no changelog,
  `tree-<hash>` no longer content-addressed.

### Option 4: A custom scheduled workflow that bumps pins and opens a PR

- Good: full control, no third-party app.
- Bad: reimplements Renovate's datasources, versioning, grouping and rebasing in shell; more code to maintain and test
  than everything else in this repository's CI.

## Decision

Chosen option: **Option 2, Renovate**, because it is the only option that updates every kind of pinned input,
`ARG` versions and git refs included, without running infrastructure. Dependabot would cover only the base image and
the Actions.

Configuration: one grouped PR for all non-major updates and separate PRs for majors, on a Monday-morning schedule in
Europe/Moscow, with no automerge. .NET SDK channels stay a manual change.

Renovate PRs don't add CHANGELOG entries. Besides the `postUpgradeTasks` limit, a workflow pushing an entry to
Renovate's branch would make Renovate treat the PR as edited by someone else and stop rebasing it. Grouped PRs would
also conflict on the same CHANGELOG lines. Instead, the release check flags Renovate PRs, and the release PR
summarises the merged updates.

## Consequences

- Positive: every input is pinned, so a tree is reproducible up to apt and .NET SDK patch versions. Updates are
  visible, grouped and reviewable. The base digest bump rebuilds all layers, which brings in Ubuntu security updates
  the weekly rebuild used to provide. Actions are SHA-pinned, as the rules require.
- Negative: the owner must install and trust the Mend app. Security updates now wait for the owner to merge the weekly
  PR and then a release PR. Pinning adds `# renovate:` comments and a few new `ARG`s to the Dockerfile.
- Follow-ups: the owner installs the app and reviews the onboarding PR. If the hosted app ever allows a command that can
  write CHANGELOG entries, revisit requirement 19 of the spec.
