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
5. `--recreate` must detect, per sandbox `name`, whether that *existing* sandbox is clone-mode or direct-mode, and
   only run the unpushed-commits safety check (fetch `sandbox-<name>`, compare against `origin`) for clone-mode
   sandboxes. For a direct-mode sandbox there is nothing to check (see Edge cases) and `--recreate` must proceed
   straight to removal.
6. `--yes` continues to skip only `sbx rm`'s own confirmation prompt, unchanged.

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
  Given a sandbox "name" exists and was created in clone mode (a "sandbox-name" remote exists on the host repo)
  When I run `agent-sandbox --recreate <path>`
  Then agent-sandbox runs the existing unpushed-commits safety check before removing it

Scenario: Recreating a direct-mode sandbox
  Given a sandbox "name" exists and was created in direct mode (no "sandbox-name" remote exists)
  When I run `agent-sandbox --recreate --direct <path>`
  Then agent-sandbox skips the unpushed-commits check and removes the sandbox directly
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
- `--recreate` mode detection for an *existing* sandbox: probe for the `sandbox-<name>` remote on the host repo
  (`git -C "$repo" remote get-url "sandbox-$name"`, tolerating `$repo` not being a git repo at all). sbx itself
  creates that remote only for `--clone` sandboxes, so its presence reliably distinguishes the two without
  `agent-sandbox` persisting any extra state. Present → run the existing fetch-and-compare safety check. Absent →
  skip straight to `sbx rm`.

## Edge cases and failure modes

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
`--direct`) plus one `--recreate` run against an existing clone-mode sandbox and one against an existing
direct-mode sandbox. `shellcheck`/`shfmt` (already in the toolchain) continue to lint the script.

## Rollout

No backward-incompatible change: existing invocations without `--direct` behave exactly as before for any path that
is a single git repo. The only behaviour change for existing users is that a non-repo path, which previously crashed
with a raw `git rev-parse` error, now succeeds in direct mode (with a notice). No migration, flag, or deployment
ordering needed.

## Open questions

None.
