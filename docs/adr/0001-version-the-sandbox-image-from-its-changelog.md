# 0001. Version the sandbox image from its CHANGELOG and release by merging a version section

- Status: Accepted
- Date: 2026-10-23
- Related: [spec](../specs/2026-10-23-sandbox-image-versioning.md), https://github.com/Markeli/ai-agents-tooling/issues/11

## Context

`ghcr.io/markeli/claude-sandbox` is published as `tree-<hash>`, `sha-<short>` and `latest`, plus a weekly rebuild
without cache. None of these tags says what changed or whether a change is breaking. After a weekly rebuild, `latest`
has no immutable tag, so rolling back means finding a digest. Sandboxes and kits need a meaningful, immutable version
they can pin and roll back to, and a changelog for it.

The owner's Curiosus libraries (`curiosus-dev/dotnet-tools`) already release this way. The CHANGELOG is the only
version source. A release is a PR that adds the version section. Every push to `main` or `release/*` idempotently
publishes versions that don't exist yet, creates a tag and a GitHub release, and a non-blocking release-check comment
runs on every PR. The bot already follows that convention there.

## Decision drivers

- One place for the version, readable in review, with release notes written next to the change.
- Every published version immutable; a failed publish can be re-run safely.
- No manual tagging or extra release command; the owner's merge is the release decision.
- The same workflow as the Curiosus repositories, so the bot and the owner don't switch conventions.
- Maintenance of an older major without moving `latest`.

## Considered options

### Option 1: CHANGELOG section as the version, publish on push (Curiosus model)

- Good: the version, the bump and the notes are reviewed in one PR; publishing is idempotent and re-runnable; matches
  the existing Curiosus convention, including `release/<major>.x` and the release check.
- Bad: releasing takes an extra PR; changes under `Unreleased` (Renovate PRs included) wait until someone writes a
  release PR; the CHANGELOG is in the image tree, so a release PR rebuilds the last layers.

### Option 2: git tag as the version (`git tag v1.2.0 && git push`)

- Good: common for container images; no CHANGELOG parsing.
- Bad: the release decision happens outside review; the bot can push tags, so it needs a tag ruleset anyway; notes
  must be written separately; diverges from the Curiosus convention.

### Option 3: Automatic versioning from Conventional Commits (release-please, semantic-release)

- Good: no hand-written bump; a release PR is generated.
- Bad: commit types describe repository changes, not image compatibility. A Renovate `chore(deps)` can be a major
  image change (a dropped runtime), and a `feat` in docs is no image change at all. It also adds a tool and its
  configuration, and diverges from Curiosus.

### Option 4: Keep `sha-<short>` and fix the rebuild tags only

- Good: least work.
- Bad: still no meaning, no changelog and no compatibility signal; solves only problem 2 of the issue.

## Decision

Chosen option: **Option 1**, because it is the only one that keeps the version, the SemVer decision and the release
notes in one reviewed PR, publishes idempotently, and matches the convention the owner and the bot already use.

Specifics for an image, which differs from a NuGet package:

- Tags `x.y.z` (immutable), `x.y`, `x` and `latest` move only forward, judged by the `org.opencontainers.image.version`
  label of the image they point at. `latest` moves only from `main`.
- Git tag `claude-sandbox.v<x.y.z>` (the Curiosus multi-package form, since the repository holds more than the image).
- `x.y.z` is published by retagging the tree image built in the PR, so the merged image is the one that was reviewed
  and tested. The baked version comes from the CHANGELOG inside the tree, so the retag doesn't make it misleading.
- `sha-<short>` is no longer published; existing tags stay.
- `release/**` branches and `claude-sandbox.v*` tags are protected by rulesets, because the bot has push access and a
  push to `release/*` publishes.

## Consequences

- Positive: meaningful, immutable, rollback-friendly versions; release notes and a toolchain diff per release; re-runs
  are safe; same mental model as Curiosus.
- Negative: updates reach `latest` only after a release PR, so security updates need one too. The plan script grows
  (CHANGELOG parsing, version comparison), which is why it gets stubbed tests. The two rulesets are configured by hand.
- Follow-ups: the owner writes release PRs (the bot can draft them on request); the first release is 1.0.0 after the
  implementation PR.
