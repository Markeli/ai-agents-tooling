# Sandbox image: semantic versioning, Renovate instead of the weekly rebuild, reproducible builds

- Status: Implemented
- Date: 2026-10-23
- Issue: https://github.com/Markeli/ai-agents-tooling/issues/11
- ADRs: [0001](../adr/0001-version-the-sandbox-image-from-its-changelog.md) (version source and release flow),
  [0002](../adr/0002-update-pinned-image-inputs-with-renovate.md) (Renovate as the update mechanism)

## Problem and goal

`.github/workflows/sandbox-image.yml` and `.github/scripts/sandbox-image-plan.sh` publish
`ghcr.io/markeli/claude-sandbox` as `tree-<hash>` (git tree of `sandbox/image`), `sha-<short>` (the `main` commit) and
`latest`. A weekly scheduled run, and a manual `force` run, rebuild without cache and move `latest` and `tree-<hash>`.
As a result, no tag says what changed or whether it breaks anything. After a weekly rebuild, the new `latest` has no
immutable tag, and `tree-<hash>` points at different images over time. Most inputs are unpinned (base image,
marketplace plugins, pnpm, yarn, claude-agent-sdk), and the pinned ones are never updated. A running sandbox can't tell
which image it runs, there is no changelog, and `tree-*` tags pile up in GHCR.

Goal: every published image has an immutable SemVer version taken from `sandbox/image/CHANGELOG.md`. Every input is
pinned, and updates arrive as Renovate PRs instead of a silent rebuild. A given `sandbox/image` tree is built once and
its image never changes. The version can be read inside the sandbox and from the launcher. Each release lists its
toolchain and diffs it against the previous release.

## Scope

In scope:

- `sandbox/image/CHANGELOG.md` as the version source; the release flow, release check, maintenance branches, rulesets.
- Pinning every image and CI input, and `renovate.json` to update them; removing the weekly and forced rebuilds.
- `/etc/claude-code/image-release`, OCI labels, the toolchain inventory, version output in `sandbox/bin/agent-sandbox`.
- A scheduled cleanup of stale GHCR versions.
- Tests for the scripts, `sandbox/README.md`, and a line in the bot's entry-point `sandbox/image/CLAUDE.md`.

Out of scope (non-goals):

- Runtime auto-updates inside a sandbox. The base image leaves Claude Code's auto-updater on, and the official
  marketplace auto-updates plugins at startup. Both stay as they are (owner's decision). `image-release` and the
  inventory describe the image as built, not what a long-running sandbox has updated itself to.
- Pinning apt packages (Ubuntu, NodeSource) and .NET SDK patch versions. They float within the pinned base digest,
  `NODE_MAJOR` and `DOTNET_CHANNELS`, are resolved once per tree build, and are refreshed when the base digest changes.
  .NET SDK channels stay a manual change.
- Pre-release versions (`x.y.z-rc.N`), an `edge` channel, and automerge.
- amd64 images: the build stays arm64-only on `ubuntu-24.04-arm`.
- Changing the kit's default `image:` from `latest`: pinning to `x.y.z` is documented, not imposed.

## Requirements

### Version source and SemVer

1. `sandbox/image/CHANGELOG.md` follows [Keep a Changelog](https://keepachangelog.com). The first
   `## [x.y.z] - yyyy-mm-dd` heading is the **release version**. `## [Unreleased]`, if present, sits above it and
   collects unreleased changes, grouped as `### Added`, `### Changed`, `### Deprecated`, `### Removed`, `### Fixed`,
   `### Security`. An `Unreleased` section with no non-blank lines counts as absent.
2. A version heading that is not `x.y.z` with non-negative integers and an ISO date fails the plan with an error that
   names the heading. With no version heading at all, the release version is `0.0.0` and nothing is published.
3. SemVer for the image:
   - **major**: something a sandbox or a user relies on is removed or changed incompatibly. Examples: a dropped
     SDK or runtime (like .NET 8), a removed tool or plugin, changed paths of the managed config
     (`/etc/claude-code/...`), the user or home layout, or the kit contract (ports, environment, credentials the
     image expects);
   - **minor**: added tools, plugins, rules or templates, or a new major version of a bundled tool;
   - **patch**: minor or patch bumps of pinned inputs, a new base image digest, fixes, and changes to rules and
     templates that don't add any.

   Breaking entries get a `**Breaking:**` note with what to change. The same rules go into `sandbox/README.md`.
4. Bootstrap: the implementation PR adds the CHANGELOG with a `## [1.0.0] - <date>` section that describes the current
   image (toolchain, plugins, skills, managed config) plus the pinning changes, so its merge publishes 1.0.0 through
   the new flow (owner's decision in the implementation PR, instead of a separate release PR).

### Release flow

5. A release is a PR that turns `## [Unreleased]` into `## [x.y.z] - yyyy-mm-dd`, with the bump chosen from the
   entries by the rules above. There is no release command and no manually created tag.
6. Every push to `main` or `release/*` that touches the workflow's paths runs an idempotent publish of the release
   version. A tag `T` is **published** when `docker buildx imagetools inspect <image>:T` succeeds. "Not found" means
   absent; any other error fails the run, as `exists()` does today.
   - `x.y.z` absent: make sure `tree-<hash>` exists (build and push it if not), then point `x.y.z` at it with
     `docker buildx imagetools create`. Then move the moving tags (requirement 8), create the git tag
     `claude-sandbox.v<x.y.z>` at the pushed commit, and create the GitHub release (requirement 9).
   - `x.y.z` present with the digest of `tree-<hash>` (a re-run of the same publish): create whichever of the moving
     tags, git tag and GitHub release is missing; touch nothing else.
   - `x.y.z` present with a different digest: publish nothing, create no tag or release, and write to the job summary
     that `x.y.z` is already published and the tree's changes are unreleased (a warning annotation if `Unreleased` is
     empty). The run succeeds.
   - `x.y.z` absent but `Unreleased` not empty: publish nothing and say so in the job summary. A newer push queued
     behind the release push can supersede it, and publishing now would ship the `Unreleased` changes as `x.y.z`.
     Re-running the workflow of the release commit publishes it.
7. `x.y.z` is immutable. CI never writes a tag `x.y.z` that already exists, and checks again for it right before the
   `imagetools create`. Publishes run in one concurrency group shared by `main` and `release/*`, never cancelled.
8. Moving tags `x.y`, `x` and `latest`: each points at the image with the highest version in its range. Tag `M` is
   moved to `x.y.z` only if `M` is absent or the `org.opencontainers.image.version` label of its image is a lower
   version. If `M` exists without a parseable version label (images from before 1.0.0), it is moved. `latest` is
   moved only from `main`, never from `release/*`.
9. The GitHub release `claude-sandbox.v<x.y.z>` is titled `claude-sandbox v<x.y.z>`. Its notes are the CHANGELOG
   section of that version, followed by the toolchain diff (requirement 23). It carries the inventory as an asset. From
   `release/*` it is created with `--latest=false`, and from `main` it is marked Latest.
10. `release/<major>.x` branches maintain an older major: the release version's major must equal `<major>`, or the
    plan fails. The process mirrors the Curiosus one: create the branch from the last tag of that major, fix on `main`
    first, cherry-pick into a branch with a new `## [x.y.z]` section, and open the PR to `release/<major>.x`.
    Renovate updates `main` only. The README documents this.
11. Rulesets, created by the owner with the `gh api` commands from the README (no rulesets-as-code here):
    - `release-branches`: `refs/heads/release/**`, rules `creation`, `update`, `deletion`, `non_fast_forward`; bypass
      only the repository admin role. A push to `release/*` publishes, and `markeli-agent` has push access, so the bot
      must not be able to create or push such a branch. PRs into it are merged by the owner.
    - `release-tags`: `refs/tags/claude-sandbox.v*`, rules `update`, `deletion`, `non_fast_forward`; bypass the
      repository admin role. Creation stays open, because the workflow's `GITHUB_TOKEN` creates these tags.
      Requirement 8 doesn't read git tags, so a stray tag can't move an image tag.
12. `sha-<short>` is no longer published, since `x.y.z` is the immutable, meaningful tag. Existing `sha-*` tags stay
    (kits may pin them) and are kept by the cleanup.
13. Changes under `## [Unreleased]` publish nothing on `main`. A PR from this repository pushes only `tree-<hash>`,
    as today, and a fork PR builds without pushing.

### Release check

14. Every PR that triggers the workflow runs a release check that never fails the build. It writes a sticky comment
    that starts with `<!-- release-check -->`: an existing comment from `github-actions[bot]` is updated in place, and
    a new one is created only when the state is not "no image changes". For fork PRs, the check writes the run summary
    only. The comment ends with the PR head SHA it was computed for. States, in priority order:
    - 🚀 the merge releases `x.y.z` (the release version is not published);
    - ⚠️ the CHANGELOG changed but nothing will be released: `x.y.z` is already published and `Unreleased` is empty
      (for example, two PRs claim the same version);
    - 📝 the image changed and the change is under `Unreleased`, released later;
    - ⚠️ `sandbox/image/**` changed without a CHANGELOG entry, plus a warning annotation on the first changed file.
      CI, tests and internal refactoring need no entry;
    - no image changes (the PR touched only the workflow or scripts).

    "Changed" compares the PR merge commit with its first parent (`HEAD^1`), like Curiosus `build.yml`.
15. Below the state, the same comment shows the **toolchain changes** of the PR: the inventory (requirement 23) of the
    PR's tree image, diffed against the `inventory.tsv` of the release the merge would build on. That is the release
    `latest` points at for PRs into `main`, and the highest release of that major for PRs into `release/<major>.x`.
    The diff is a table `Item | <x.y.z> | This PR` with added, removed and changed rows only, or "no toolchain changes"
    when there are none. A reviewer of a Renovate PR or a Dockerfile change sees, for example, the Claude Code or
    plugin versions it brings without reading digests. The inventory runs in the PR's image after the build: a
    same-repo PR uses the pushed `tree-<hash>`, and a fork PR loads its build into the runner's Docker (`load: true`).
    If the tree image was already built, it is pulled, not rebuilt. If there is no release yet, or it has no
    `inventory.tsv` (before 1.0.0), the section lists the full inventory and says there is nothing to diff against.
    The section is omitted in the "no image changes" state. A failure to produce the diff is reported in the section
    and, like the rest of the check, never fails the build.

### Pinned inputs and Renovate

16. Remove the `schedule` trigger, the `force` input, and the `no_cache`/forced logic in the plan script. An existing
    `tree-<hash>` is never rebuilt or overwritten. The build keeps `cache-from`/`cache-to: type=gha`.
17. Pin every input, each with a `# renovate:` comment that a regex custom manager reads:
    - `FROM docker/sandbox-templates:claude-code-docker-<x.y.z>@sha256:<index digest>`: the template's release tag
      for a meaningful bump, the digest because Docker Hub tags can be re-pushed. Renovate updates both (a `regex:`
      versioning for the prefixed tag). Claude Code's version comes with it;
    - `ARG <TOOL>_VERSION` from GitHub releases (glab from GitLab releases): glab, yq, actionlint, hadolint, terraform,
      plannotator;
    - csharp-ls (NuGet); typescript-language-server and typescript (npm, typescript `<7` because 7 ships no `tsserver`);
      `NODE_MAJOR` (node-version, LTS majors only);
    - new ARGs for inputs that are unpinned today: `PNPM_VERSION` (npm `pnpm`), `YARN_VERSION` (npm
      `@yarnpkg/cli-dist`), `CLAUDE_AGENT_SDK_VERSION`, `ANSIBLE_CORE_VERSION`, `ANSIBLE_LINT_VERSION` (PyPI), used as
      `corepack prepare pnpm@${PNPM_VERSION}`, `uv tool install ansible-core==${ANSIBLE_CORE_VERSION}` and so on;
    - git refs with the `git-refs` datasource, tracking `main` by digest: `MATTPOCOCK_SKILLS_COMMIT`, and a new
      `CLAUDE_PLUGINS_OFFICIAL_COMMIT` for the `claude-plugins-official` marketplace (requirement 18);
    - GitHub Actions in all workflows (`sandbox-image.yml`, `codeql.yml`, new ones) pinned to commit SHAs with a
      trailing version comment.
18. `claude plugin marketplace add` passes a `#<ref>` to `git clone --branch`, so it takes branches and tags but not
    commit SHAs. `claude-plugins-official` has no tags, and Claude Code reserves that marketplace name for GitHub
    sources from `anthropics`, so a local checkout can't be added under it. The Dockerfile therefore adds the
    marketplace as today, then fetches and checks out the pinned commit in its clone, then installs the plugins. All
    three facts were checked with Claude Code 2.1.309 in this sandbox: `install` used the checked-out commit's version.
    At runtime the auto-update (non-goal) replaces the clone with a snapshot of the current marketplace, marked by a
    `.gcs-sha` file instead of `.git`; the inventory reads whichever is there. Plugins with an external `url` source
    (superpowers, microsoft-docs) carry a `sha` in that marketplace's `marketplace.json`, so the marketplace commit
    pins them too. Plannotator stays pinned by its `#v<version>` tag.
19. `renovate.json` at the repository root, for the Mend Renovate GitHub App:
    - extends `config:recommended`, `docker:pinDigests` and `helpers:pinGitHubActionDigests`;
    - `timezone: Europe/Moscow`, `schedule: ["before 9am on monday"]`;
    - one group for all non-major updates (`minor`, `patch`, `digest`, `pin`, `pinDigest`), named for the sandbox image
      and CI; majors as separate PRs on the same schedule; no automerge;
    - two regex custom managers on `sandbox/image/Dockerfile`: one for versions
      (`# renovate: datasource=… depName=… [packageName=…] [versioning=…]` above an `ARG X=<version>`), one for
      `git-refs` digests (`# renovate: datasource=git-refs depName=<url> branch=<branch>` above an `ARG X=<sha>`);
    - `packageRules` for `typescript` (`allowedVersions: "<7"`) and Node.js (even majors only);
    - no manager for .NET channels.
20. Renovate PRs don't edit the CHANGELOG. The Mend-hosted app allows only an undocumented list of `postUpgradeTasks`
    commands, and a workflow committing to Renovate's branch would stop Renovate from rebasing it (ADR 0002). The
    release check flags these PRs with ⚠️ and shows the toolchain changes they bring (requirement 15), and the
    release PR summarises the merged Renovate updates under
    `### Changed` (or `### Security`).

### Version inside the image

21. The plan computes the **baked version** of the tree being built: `x.y.z` if `Unreleased` is empty and `x.y.z` is
    not yet published, otherwise `<x.y.z>+unreleased.<first 12 characters of the tree hash>` (`0.0.0+…` before the
    first release). CI passes it, the tree hash and the revision as build args `IMAGE_VERSION`, `IMAGE_TREE` and
    `IMAGE_REVISION`. These args are declared after the last `COPY` and used only by the final `RUN`, which writes
    `/etc/claude-code/image-release` (mode 0644, readable by `agent`), so a changed version rebuilds only that layer.
    Because the version comes from the CHANGELOG inside the tree, an image built by the release PR already carries
    `x.y.z`, and a retag on `main` keeps the right version.
22. OCI labels set by CI: `org.opencontainers.image.version` (the baked version), `org.opencontainers.image.revision`
    (`IMAGE_REVISION`), `org.opencontainers.image.created` (build time). `revision` is the commit the tree was built
    from: the PR head SHA for PR builds (the merge preview commit is not kept), the pushed commit for builds on `main`
    or `release/*`. After a retag, `revision` is still the PR head, which may differ from the `main` commit. The `tree`
    hash identifies the content, and the git tag `claude-sandbox.v<x.y.z>` gives the `main` commit. The README states
    this. `sandbox/image/CLAUDE.md` gets one line telling the bot that `/etc/claude-code/image-release` holds its image
    version.

### Visibility

23. `sandbox/image/inventory.sh`, copied to `/etc/claude-code/inventory.sh`, prints one `name<TAB>version` line per
    item, sorted: Claude Code, each .NET SDK, Node.js, npm, pnpm, yarn, typescript, typescript-language-server,
    csharp-ls, gh, glab, yq, actionlint, hadolint, shellcheck, shfmt, terraform, plannotator, uv, Python,
    ansible-core, ansible-lint, claude-agent-sdk, each installed plugin (version and git commit from
    `installed_plugins.json`), the `claude-plugins-official` and mattpocock skills commits. CI adds the base image
    digest. Every PR build (requirement 15) and every publish runs it in the image (the runner is arm64, so it runs
    natively) and writes a Markdown table to the job summary. A release attaches `inventory.tsv` and appends to its
    notes the same diff table as the PR comment, against the `inventory.tsv` of the previous release (the highest
    `claude-sandbox.v*` release with a lower version, from the same major on `release/*`). If there is no previous
    release, or it has no inventory, the notes say so.
24. `sandbox/bin/agent-sandbox` prints, after creating a sandbox or before attaching to one (also with `--no-attach`):
    `agent-sandbox: <name> runs <image ref>@<digest>, version <VERSION>`. The image ref and digest come from
    `sbx inspect "$name" --json` (field names are confirmed during implementation). `VERSION` comes from
    `sbx exec "$name" cat /etc/claude-code/image-release`. Failing to read either prints `unknown` (or
    `unversioned` when the file is missing, as in images from before 1.0.0) and never fails the launcher.

### Cleanup

25. `.github/workflows/sandbox-image-cleanup.yml` runs weekly and on `workflow_dispatch`, with a `dry-run` input. It
    deletes package versions of `claude-sandbox` whose `updated_at` is more than 30 days old and that are either
    untagged or tagged only with `tree-*`. It keeps every version tagged with a semver, `latest` or `sha-*`, and every
    manifest referenced by a kept image index, so that the platform and attestation manifests of a kept image are
    never deleted. It lists every deleted and kept version in the job summary.
26. Token: the workflow uses `GITHUB_TOKEN` with `packages: write`. GitHub allows this when the repository has the
    `admin` role on the package. A package first published by a workflow of this repository gets that role; otherwise
    the owner grants it under the package's "Manage Actions access". A 403 fails the job with a message pointing to
    that README section. It never skips silently. A PAT is not needed.

### Non-functional

27. `shellcheck`, `actionlint`, `hadolint` and `npx --yes --package renovate -- renovate-config-validator` (pinned
    Renovate version) pass. A `lint` job in `sandbox-image.yml` runs them and the script tests on PRs, and the
    workflow's paths gain `renovate.json` and `.github/scripts/**`.
28. Third-party content is still fetched only from pinned versions or commits, with checksums where the source
    publishes them, as today. Shell scripts quote all external values: CHANGELOG text reaches `gh` only through
    `--notes-file`, never interpolated into a command.

## Acceptance scenarios

```gherkin
Scenario: PR changes the image with an Unreleased entry
  Given sandbox/image/Dockerfile and an Unreleased entry change in a same-repo PR, and 1.2.0 is published
  When the workflow runs
  Then tree-<hash> is built and pushed with version "1.2.0+unreleased.<tree12>"
  And the release-check comment says the change is under Unreleased

Scenario: Renovate PR shows its toolchain changes
  Given latest is 1.2.0 with an inventory.tsv listing Claude Code 2.1.309
  And a same-repo Renovate PR bumps the base image digest, which brings Claude Code 2.1.320
  When the workflow runs
  Then the release-check comment has a row "Claude Code | 2.1.309 | 2.1.320"
  And rows for unchanged items are not shown

Scenario: PR changes the image without a CHANGELOG entry
  Given a same-repo PR changes sandbox/image/Dockerfile only
  When the workflow runs
  Then the release-check comment shows the ⚠️ no-entry state and the build passes

Scenario: Main push with only Unreleased changes
  Given main's CHANGELOG has a non-empty Unreleased section above the published 1.2.0
  When a push to main runs the workflow
  Then no semver, latest, git tag or GitHub release is created or moved

Scenario: Release merge
  Given main's CHANGELOG top section is "## [1.3.0] - 2026-11-02" and 1.3.0 is not in GHCR
  When a push to main runs the workflow
  Then 1.3.0, 1.3, 1 and latest point at tree-<hash>
  And git tag claude-sandbox.v1.3.0 and a Latest GitHub release with that section and inventory.tsv exist

Scenario: Already published version
  Given 1.3.0 exists with the digest of tree-<hash>, its git tag and its release
  When the workflow runs again on main
  Then nothing is written

Scenario: Published version missing its tag or release
  Given 1.3.0 exists with the digest of tree-<hash> but claude-sandbox.v1.3.0 has no git tag or release
  When the workflow re-runs on that commit
  Then the missing tag and release are created and no image tag changes

Scenario: Maintenance release
  Given release/1.x has "## [1.4.1]", latest is 2.0.0 and 1 points at 1.4.0
  When a push to release/1.x runs the workflow
  Then 1.4.1, 1.4 and 1 point at the new image, latest still points at 2.0.0
  And the GitHub release is not marked Latest

Scenario: Fork PR
  Given a PR from a fork changes the image
  When the workflow runs
  Then the image is built without pushing and the release check is in the run summary only
```

## Contracts

- **Image tags**: `x.y.z` (immutable), `x.y`, `x`, `latest` (moving, requirement 8), `tree-<40-hex>` (one per
  tree, built once, internal, cleaned after 30 days unless released), `sha-<7-hex>` (legacy, no longer written).
- **Git tag / release**: `claude-sandbox.v<x.y.z>`; asset `inventory.tsv`.
- **`/etc/claude-code/image-release`** (os-release style, `KEY=value`, no quotes):

  ```text
  VERSION=1.3.0
  TREE=<40-hex git tree of sandbox/image>
  REVISION=<40-hex commit>
  ```

- **Plan script** `.github/scripts/sandbox-image-plan.sh`. Inputs (environment): `IMAGE`, `EVENT`, `REF`,
  `BASE_REF` (PR base branch), `DEFAULT_BRANCH`, `SHA`, `HEAD_SHA` (PR head), `SAME_REPO`, `RELEASE_CHECK` (path of
  the release-check Markdown). Outputs (`GITHUB_OUTPUT`, or stdout when unset):
  `build` (tree image missing), `push`, `tree`, `tree_tag`, `version` (baked), `revision`, `publish` (true on
  `main`/`release/*` when the release version must be published or completed), `release` (`x.y.z` or empty),
  `publish_tags` (newline-separated tags to point at the tree image), `latest` (whether the GitHub release is marked
  Latest), `release_check` (path of the generated Markdown), `changed`, `notes` (why nothing is published).
- **Publish script** `.github/scripts/sandbox-image-publish.sh`: `IMAGE`, `TREE_TAG`, `RELEASE`, `PUBLISH_TAGS`,
  `LATEST`, `SHA`, `INVENTORY`; points the tags at the tree image and creates the release if it is missing.
- **Inventory**: `/etc/claude-code/inventory.sh`, stdout `name<TAB>version`, sorted by name, exit 0.
- **Inventory diff**: `.github/scripts/sandbox-image-inventory-diff.sh <old.tsv|-> <new.tsv> <old label>
  <new label>`, stdout the Markdown table of requirement 15 (`-` for no previous inventory). Used by both the release
  check and the release notes.
- **Cleanup**: `.github/scripts/sandbox-image-cleanup.sh`. Inputs `OWNER`, `PACKAGE`, `MAX_AGE_DAYS` (default 30),
  `DRY_RUN`; uses `gh api` and `docker buildx imagetools inspect --raw`.

## Edge cases and failure modes

- Registry error other than "not found" (auth, network, rate limit) → the run fails; it never counts as absent.
- Two release PRs claim the same version → the second one's merge publishes nothing and says so in the summary; its
  release check showed ⚠️ after the first one merged.
- Merge after `tree-<hash>` was cleaned (PR open more than 30 days) → `main` builds the tree, then publishes.
- Version heading malformed, or `release/<major>.x` with a CHANGELOG of another major → the plan fails with a message.
- `imagetools create` succeeded but tagging or the release failed → a re-run completes them (requirement 6).
- A waiting publish cancelled by GitHub (one running and one pending job per concurrency group) → re-run it. The
  publish script checks `x.y.z` and the moving tags again under the group, and marks the release Latest only while
  `latest` points at it, so the order of re-runs doesn't matter.
- A lookup in the registry fails after the tag was found → the run fails; an empty label or digest never counts as
  "unversioned" or "same image".
- Cleanup: a stale `tree-*` version gets released between the listing and its deletion → its tags are read again
  right before the DELETE, and it is kept.
- Moving tag points at a higher version (a maintenance release after a newer minor) → not moved.
- Previous release without `inventory.tsv` (none before 1.0.0) → the notes say there is nothing to diff against.
- Cleanup token without admin on the package → the job fails with a pointer to the README.
- `sbx inspect` or `sbx exec` fails in the launcher → it prints `unknown` and carries on.
- A Renovate digest update of the base image → all layers rebuild (each layer is keyed on its parent), so apt layers
  pick up Ubuntu security updates; that is the replacement for the weekly rebuild.

## Test plan

- `.github/scripts/tests/run.sh`: plain bash, no extra dependencies. It runs every case in a throwaway git
  repository (a base commit and the change under test, so `HEAD^1` is the base as in a PR merge commit) and puts stubs
  for `docker` and `gh` on `PATH`, with a fixture per case (registry tags with digests and version labels,
  releases). It asserts on the plan outputs and the release-check Markdown. Cases:
  - same-repo PR with and without an `Unreleased` entry, a release PR, a PR touching only the workflow or scripts;
  - fork PR (no push, summary only);
  - `main` push with only `Unreleased` changes (no publish);
  - release merge (publish tags incl. `latest`, release marked Latest);
  - already published with the same digest (no-op), and published with a missing git tag or release (completes them);
  - published with a different digest (no publish, summary note);
  - `release/1.x` push (moves `1.4`/`1`, not `latest`; release not Latest), and a major mismatch (fails);
  - malformed version heading (fails), no version heading (`0.0.0+unreleased`, no publish);
  - registry error other than "not found" (fails);
  - inventory diff: changed, added and removed rows; identical inventories ("no toolchain changes"); no previous
    inventory (full list); the diff target for a PR into `main` (`latest`) and into `release/1.x` (highest 1.x);
  - cleanup: tree-only and untagged versions older than 30 days deleted, newer ones, semver, `latest`, `sha-*` and
    children of kept indexes kept, a 403 fails.
- The `lint` job runs these tests, `shellcheck` on all scripts, `actionlint`, `hadolint` and the Renovate validator.
- Not testable inside the sandbox (no GHCR write access, no `sbx`): the real publish, the cleanup against GHCR and the
  launcher output. They are verified by the implementation PR's own run (PR build), by the release PR merge, and by the
  owner on the host.

## Rollout

1. This spec PR. Then an implementation PR from the updated `main`: pinning, `renovate.json`, workflow and scripts,
   tests, inventory, `image-release`, launcher, cleanup, README and `CLAUDE.md`, and a CHANGELOG with `## [1.0.0]`.
   Its merge publishes 1.0.0, 1.0, 1 and `latest`, the git tag `claude-sandbox.v1.0.0` and the GitHub release.
2. The owner installs the Mend Renovate GitHub App on the repository, creates the two rulesets from the README, and
   checks that the package grants the repository the admin role.
3. The owner checks the Renovate onboarding PR and the first grouped update PR.

Existing sandboxes are unaffected: the image is resolved once at `sbx create`. Existing `sha-*` and `tree-*` tags stay,
and the cleanup removes only `tree-*` and untagged versions.

## Open questions

None.
