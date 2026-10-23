# Sandbox: support creating from a path (direct mode), not just clone

- Status: Approved
- Date: 2026-10-16
- Issue: https://github.com/Markeli/ai-agents-tooling/issues/10
- ADRs: none

## Problem and goal

`agent-sandbox` always runs `sbx create --clone ... "$repo"`, which requires `$repo` to resolve to a single git
repository (`git -C "$repo" rev-parse --show-toplevel`) and gives the bot a standalone, isolated clone. There is no
way to mount a path directly — sbx's other, non-`--clone` mode — so `agent-sandbox` cannot back a sandbox with
something that isn't (only) a single clonable repo, e.g. a parent directory holding several sibling repositories,
each with its own `CLAUDE.md`/`AGENTS.md`.

Goal: let `agent-sandbox` create a sandbox bind-mounted directly at a given path, as an alternative to clone mode,
without losing today's clone-mode behaviour (isolation, `--recreate`'s unpushed-commits safety net) for the common
single-repo case.

## Scope

In scope:

- An explicit way to request direct mode for any path.
- Automatically falling back to direct mode when the path isn't a single git repo, instead of crashing.
- A sandbox name fallback that doesn't depend on the path being a git repo.
- Making `--recreate`'s safety check work for (or correctly skip) direct-mode sandboxes.
- Detecting the mode and source of an *existing* sandbox from `sbx`, and refusing to attach to it when it doesn't
  match the current invocation.
- Documenting when to use clone vs. direct mode.

Out of scope (non-goals):

- Mounting more than one path per sandbox (sbx's `PATH...` accepts several, e.g. `claude . /path/to/docs:ro`); not
  needed to satisfy the issue and adds surface area this spec doesn't resolve.
- Changing the sandbox name validation regex in `agent-sandbox` (stricter than sbx's own `--name` validation); out
  of scope for this issue.
- Any change to the clone-mode path when a single repo is used without the new flag and auto-detection doesn't
  apply — it keeps behaving exactly as today.

## Requirements

Functional:

1. `agent-sandbox --direct <path>` must create (or attach to) a sandbox bind-mounted directly at `<path>`
   (`sbx create` without `--clone`), regardless of whether `<path>` is a single git repository.
2. Without `--direct`, `agent-sandbox <path>` must keep using clone mode when `<path>` resolves to a single git
   repository (unchanged behaviour).
3. Without `--direct`, when `<path>` does **not** resolve to a single git repository, `agent-sandbox` must
   auto-detect this and fall back to direct mode instead of failing, and must print a one-line notice to stderr
   that it did so (since this silently changes isolation/host-write semantics from what clone mode would have
   given).
4. Sandbox naming must not require `<path>` to be a git repository: when direct mode applies (explicitly or via
   auto-detection), the name is derived from the resolved absolute path's basename, the same way it already is from
   `git rev-parse --show-toplevel`'s output in clone mode.
5. The mode and source of an *existing* sandbox are read from `sbx inspect <name>` (see Contracts), never inferred
   from the current invocation's `PATH` or flags. If they can't be determined (unrecognised output, `sbx inspect`
   fails), `agent-sandbox` must stop with an error and must not attach, remove or recreate anything.
6. When a sandbox with the resolved `name` already exists and `--recreate` is not given, `agent-sandbox` must compare
   the requested mode and path with the existing sandbox's, and attach only if both match. On a mismatch it must
   stop with an error naming the existing sandbox's mode and path and suggesting `--recreate` (to replace it) or
   `--name` (to keep both).
7. `--recreate` on an existing clone-mode sandbox must run the unpushed-commits safety check (start the sandbox if
   stopped, fetch `sandbox-<name>`, compare against `origin`) in the repository the sandbox was cloned from (its
   `Clone source`), not in the current `PATH`. If that repository is missing or the fetch fails, it must stop without
   removing the sandbox, as today.
8. `--recreate` on an existing direct-mode sandbox skips the safety check and proceeds straight to removal: there is
   nothing to check (see Edge cases). The new sandbox is then created according to the current invocation, so
   `--recreate` is also the way to switch a sandbox between modes or paths.
9. `--yes` continues to skip only `sbx rm`'s own confirmation prompt, unchanged.

Non-functional:

1. No new flag changes the meaning of any existing flag or the default behaviour for today's single-repo,
   no-`--direct` invocation.

## Acceptance scenarios

```gherkin
Scenario: Single repo, no flag — unchanged clone mode
  Given "<path>" resolves to a single git repository
  When I run `agent-sandbox <path>`
  Then the sandbox is created with `sbx create --clone ...`
  And the sandbox name is derived from `git -C <path> rev-parse --show-toplevel`

Scenario: Single repo, explicit direct mode
  Given "<path>" resolves to a single git repository
  When I run `agent-sandbox --direct <path>`
  Then the sandbox is created with `sbx create ...` (no `--clone`)
  And the host working tree is bind-mounted, not cloned

Scenario: Non-repo path, no flag — auto-detected direct mode
  Given "<path>" is a directory that is not itself a single git repository (e.g. a parent of several repos)
  When I run `agent-sandbox <path>`
  Then agent-sandbox prints a one-line notice to stderr that it is falling back to direct mode
  And the sandbox is created with `sbx create ...` (no `--clone`)
  And the sandbox name is derived from the basename of the resolved absolute path

Scenario: Non-repo path, explicit direct mode
  Given "<path>" is a directory that is not a single git repository
  When I run `agent-sandbox --direct <path>`
  Then the sandbox is created in direct mode without any auto-detection notice

Scenario: Recreating a clone-mode sandbox
  Given a sandbox "name" exists and `sbx inspect name` shows a "Clone source"
  When I run `agent-sandbox --recreate <path>`
  Then agent-sandbox runs the unpushed-commits safety check in the "Clone source" repository before removing it

Scenario: Recreating a stopped clone-mode sandbox
  Given a clone-mode sandbox "name" exists and is stopped
  When I run `agent-sandbox --recreate <path>`
  Then agent-sandbox starts it, runs the unpushed-commits safety check, and only then removes it

Scenario: Recreating a clone with the same name but a different path
  Given a clone-mode sandbox "work" was created from "/repos/app" and has commits not pushed to origin
  When I run `agent-sandbox --recreate --direct --name work /repos`
  Then agent-sandbox runs the safety check in "/repos/app", not "/repos"
  And it stops with an error about the unpushed commits without removing the sandbox

Scenario: Recreating a direct-mode sandbox
  Given a sandbox "name" exists and `sbx inspect name` shows no "Clone source"
  When I run `agent-sandbox --recreate --direct <path>`
  Then agent-sandbox skips the unpushed-commits check and removes the sandbox directly

Scenario: Direct mode requested for an existing clone
  Given a clone-mode sandbox "app" was created by `agent-sandbox /repos/app`
  When I run `agent-sandbox --direct /repos/app`
  Then agent-sandbox stops with an error that "app" is a clone of "/repos/app" and suggests `--recreate` or `--name`
  And it does not attach to the sandbox

Scenario: Clone mode requested for an existing direct-mode sandbox
  Given a direct-mode sandbox "app" was created by `agent-sandbox --direct /repos/app`
  When I run `agent-sandbox /repos/app`
  Then agent-sandbox stops with an error that "app" mounts "/repos/app" directly and suggests `--recreate` or `--name`
  And it does not attach to the sandbox

Scenario: Existing sandbox for another path
  Given a sandbox "work" exists for "/repos/app"
  When I run `agent-sandbox --name work /repos/other`
  Then agent-sandbox stops with an error naming "/repos/app" and does not attach

Scenario: Mode of an existing sandbox can't be determined
  Given a sandbox "name" exists and `sbx inspect name` fails or its output has no "Workspace" line
  When I run `agent-sandbox <path>` with or without `--recreate`
  Then agent-sandbox stops with an error and neither attaches to nor removes the sandbox
```

## Contracts

`agent-sandbox` CLI:

- New flag: `--direct` (boolean, no argument). Forces direct (bind-mount) mode for the given path, i.e. omits
  `--clone` from the underlying `sbx create` call. Usable together with any existing flag (`--name`, `--recreate`,
  `--yes`, `--no-attach`).
- Usage line changes from:
  `agent-sandbox [--no-attach] [--recreate] [--yes] [--name NAME] [REPO_PATH] [-- CLAUDE_ARGS...]`
  to:
  `agent-sandbox [--no-attach] [--recreate] [--yes] [--direct] [--name NAME] [PATH] [-- CLAUDE_ARGS...]`
  (`REPO_PATH` renamed to `PATH` in help text/docs, since it no longer has to be a repo).
- Path resolution:
  - Clone mode (default, single-repo path): `repo=$(git -C "$path" rev-parse --show-toplevel)`, unchanged.
  - Direct mode (`--direct`, or auto-detected because the path isn't a single repo): `repo=$(cd "$path" && pwd)`
    — the resolved absolute path, with no git involved.
  - Sandbox name derivation (`basename "$repo" | tr '[:upper:]' '[:lower:]' | tr '._' '--'`, then the existing
    validation regex) is unchanged and applied identically in both modes, since both now produce `repo` as an
    absolute path first.
- `sbx create` invocation: clone mode keeps `sbx create --clone --pull "$pull" --name "$name" "$kit" "$repo"`;
  direct mode drops `--clone`: `sbx create --pull "$pull" --name "$name" "$kit" "$repo"`.
- Existing sandbox detection: `sbx inspect "$name"`, which works without `agent-sandbox` persisting any state of its
  own and is tied to the sandbox, not to the invocation. Observed output (sbx v0.49.1) for a clone-mode sandbox:

  ```text
  Workspace:       /Users/markelow/Development/Personal/ai-agents-tooling
  Clone source:    /Users/markelow/Development/Personal/ai-agents-tooling
  ```

  - `Workspace:` missing or empty → mode unknown → error, no further action.
  - `Clone source: <dir>` present → clone mode; `<dir>` is the repository for the safety check and the path compared
    against the current invocation.
  - `Workspace:` present, `Clone source:` absent → direct mode; `Workspace` is the mounted path.
  - Paths are compared after the same resolution the current invocation uses (`git rev-parse --show-toplevel` in
    clone mode, `cd && pwd` in direct mode).
- `--recreate` safety check: `recreate_sandbox()` keeps its current steps (`sbx exec "$name" true` to start a stopped
  sandbox, `git fetch "sandbox-$name"`, `git rev-list --remotes="sandbox-$name" --not --remotes=origin`), but runs
  git in the `Clone source` directory instead of `$repo`.

## Edge cases and failure modes

- `sbx inspect` output is human-readable, not a stable format. A future sbx that renames or reformats the
  `Workspace`/`Clone source` lines makes the mode unknown, which `agent-sandbox` treats as an error rather than as
  direct mode, so a format change can block a run but can't delete a clone with unpushed commits.
- Sandboxes created before this change are all clone mode with a `Clone source` equal to their repository, so a
  plain `agent-sandbox <repo>` keeps attaching to them as today.
- The `Clone source` repository was moved or deleted on the host → the fetch fails and `--recreate` stops, as it
  does today when the fetch fails.

- `--direct` combined with a path that doesn't exist or isn't a directory → `sbx create` reports the error;
  `agent-sandbox` doesn't duplicate that validation.
- Direct mode, `--recreate`: nothing is lost by removing the sandbox, because the mounted path *is* the host's own
  files — there is no separate clone whose unpushed commits could disappear. Removal only loses what the "Known
  limits" / "Update or recreate a sandbox" sections of the README already describe as lost on every recreate
  (installed packages/caches, the sandbox's own Docker state, Claude session history), none of which direct mode
  changes.
- Path that is inside a git repo but not at its root, under `--direct`: resolved via `cd "$path" && pwd`, so the
  bind mount is exactly the given subdirectory, not the repo root — different from clone mode, which always
  resolves to the repo's top level. This is intentional: direct mode mounts what you point it at.
- A path that auto-detects into direct mode today and gains a `.git` later (e.g. someone runs `git init` in that
  parent folder): has no effect on an already-created sandbox; only `--recreate` would pick up the new resolution,
  and only if `--direct` isn't also passed.

## Test plan

No automated test suite exists for `sandbox/bin/agent-sandbox` today (it drives `sbx`/`git`/`ssh-agent`/keychain,
which aren't mockable cheaply in CI). Verification is manual, against a real `sbx` install, covering the four
`agent-sandbox`-facing scenarios above (single repo with/without `--direct`, non-repo parent path with/without
`--direct`), the mode/path mismatch scenarios, and `--recreate` against a running and a stopped clone-mode sandbox,
against a clone under the same `--name` with a different path, and against a direct-mode sandbox.
`shellcheck`/`shfmt` (already in the toolchain) continue to lint the script.

## Rollout

No backward-incompatible change: existing invocations without `--direct` behave exactly as before for any path that
is a single git repo. The behaviour changes for existing users are that a non-repo path, which previously crashed
with a raw `git rev-parse` error, now succeeds in direct mode (with a notice), and that `--name` pointing at an
existing sandbox for a different path now stops with an error instead of silently attaching to it. No migration,
flag, or deployment ordering needed.

## Open questions

To verify against a real sbx before this spec is merged (they decide whether the parsing above is right, not the
design):

1. Does `sbx inspect` print `Workspace:` and `Clone source:` for a **stopped** sandbox too?
2. What does `sbx inspect` print for a **direct-mode** sandbox: no `Clone source:` line, or the line with an empty
   value or a placeholder? Does `sbx inspect` have a machine-readable output (`--json` or similar)? If it does, the
   contract switches to that.
