# 0004. Build the sandbox image from `skills/claude-code` too, addressed by a tree of both directories

- Status: Accepted
- Date: 2026-11-14
- Related: [spec](../specs/2026-11-14-watch-pr.md), [ADR 0001](0001-version-the-sandbox-image-from-its-changelog.md),
  [image versioning spec](../specs/2026-10-23-sandbox-image-versioning.md)

## Context

The `watch-pr` skill has to ship in the sandbox image, and the owner wants its source in the repository's skills
directory, `skills/claude-code/watch-pr/`, next to other Claude Code skills, not hidden inside `sandbox/image`.

The image is built with `sandbox/image` as its only Docker build context. The image versioning spec and ADR 0001 rely
on that directory being the whole input. `.github/scripts/sandbox-image-plan.sh` names the image after
`git rev-parse HEAD:sandbox/image` (`tree-<hash>`), builds a tree once and never again, retags that tree image on
release, and decides whether a PR "changes the image" (and needs a CHANGELOG entry) from `git diff -- sandbox/image`.
The tree is also baked into the image as `TREE` in `/etc/claude-code/image-release`. Copying files from outside
`sandbox/image` without changing that address would publish a changed image under an old `tree-<hash>`, which is
never rebuilt.

## Decision drivers

- Skills live in `skills/`, as the repository's structure says.
- A given input is built once, and a different input never reuses an existing image (ADR 0001).
- A change to a shipped skill is an image change: it needs a CHANGELOG entry and a release, like any other.
- Keep the plan script, its tests, and the tag format as they are wherever possible.

## Considered options

### Option 1: Keep image skills inside `sandbox/image` (`sandbox/image/skills/`)

- Good: nothing in CI or ADR 0001 changes.
- Bad: the skill isn't where the owner wants skills, and `skills/claude-code` and the image's skills drift apart.

### Option 2: A second build context, and an address that covers both directories

- Good: the skill lives in `skills/claude-code`, every skill there ships in the image, and every change to it changes
  the image's address and is caught by the release check.
- Bad: the plan script, the workflow and the README change; the image's `TREE` no longer equals the tree of
  `sandbox/image`.

### Option 3: Copy `skills/claude-code` into `sandbox/image` in CI before the build

- Good: the Dockerfile keeps a single context.
- Bad: the git tree doesn't describe the build input, so the address is wrong exactly as described in Context, unless
  the address changes as in Option 2 anyway; local builds need the same copy step.

## Decision

Chosen option: **Option 2**, because it is the only one that keeps skills in `skills/` while keeping each image built
once from an input its address describes.

- The image's address is the hash of a git tree object made with `git mktree` from two entries:
  `image` → `HEAD:sandbox/image` and `skills` → `HEAD:skills/claude-code`. It stays a git tree hash, so the
  `tree-<hash>` tag format, `TREE` and the "built once" rule are unchanged; only what the hash covers changes.
- The Dockerfile copies the skills from a named build context (`skills`, given to `docker/build-push-action` as
  `build-contexts: skills=skills/claude-code`) into `/etc/claude-code/.claude/skills/`. Every skill in
  `skills/claude-code` ships in the image.
- A PR changes the image when it changes `sandbox/image` or `skills/claude-code`; the CHANGELOG stays in
  `sandbox/image`. The workflow's path filters include `skills/claude-code/**`.

## Consequences

- Positive: shipped skills live with the other skills and go through the same release flow as the image.
- Negative: the first build after this change has a new address even though `sandbox/image` didn't change, so it is
  built again; `tree-<hash>` can no longer be checked with `git rev-parse HEAD:sandbox/image` by hand. The README
  says how to compute it.
- Follow-ups: the `watch-pr` implementation PR changes the plan script, its tests, the workflow and the README.
