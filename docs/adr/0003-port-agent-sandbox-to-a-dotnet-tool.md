# 0003. Port the agent-sandbox launcher from bash to a .NET tool

- Status: Accepted
- Date: 2026-11-01
- Related: [direct-mode spec](../specs/2026-10-16-sandbox-direct-mode.md), issues #10, #9, #13 and #28
  (https://github.com/Markeli/ai-agents-tooling/issues)

## Context

`sandbox/bin/agent-sandbox` is a 145-line bash script that runs on the owner's Mac. It loads the bot's SSH key, binds
the bot's API tokens from the macOS Keychain as sandbox secrets, runs `sbx create --clone`, guards `--recreate`
against commits that were not pushed, prints the image version, and attaches. It has no tests.

The next three issues add most of its logic:

- #10 (spec approved): direct mode, detecting the mode and source of an existing sandbox from `sbx inspect --json`,
  and a `--recreate` guard for each mode;
- #9: a repeatable `--mount PATH[:ro|:rw]`, keeping mounts across `--recreate` by reading them back from
  `sbx inspect`, and strict argument parsing (today a second positional argument silently replaces the repository);
- #13: `--persist-memory`, with state directories on the host and a symlink created through `sbx exec`.

Each of them requires tests with a stubbed `sbx` and `git`. #28 adds Windows hosts. There, `sbx` is a native Windows
program, the secret store is Credential Manager, and bash is not available without WSL.

Facts that constrain the choice:

- `sbx` supports macOS on Apple silicon, Windows 11 on x86_64 and Ubuntu 24.04+ on x86_64 and arm64.
- The owner works in C#. `AGENTS.md` prefers C# / .NET for tooling, and the owner's Mac already has the .NET SDK.
- `Curiosus.Hosting` has a console tool bootstrapper, `CuriosusToolAppBootstrapper`. It is built around a YAML config
  directory, NLog and DI for long-running tools. The launcher has no config file and no logging. It needs a flag
  parser that passes everything after `--` through to Claude Code.

## Decision drivers

- The logic from #10, #9 and #13 stays testable and reviewable as it grows: argument parsing, JSON from
  `sbx inspect`, decisions on create and recreate.
- A path to Windows (#28) without a second implementation.
- Same behaviour and command line for the owner: flags, environment overrides, output.
- Little to install and maintain on the host, and no new infrastructure.

## Considered options

### Option 1: Keep bash, add a bash test harness

- Good: no rewrite; no runtime to install; already where `sbx` and `git` live.
- Bad: JSON parsing depends on `jq` and on guessing field names; the parsing bugs in #9 are the kind bash invites.
  Stubs on `PATH` test whole runs, not decisions. Windows needs WSL or a second launcher in PowerShell.

### Option 2: A .NET global tool (`dotnet tool install -g`)

- Good: C#, so the owner's stack and the repository's convention. Unit tests on decisions, with a fake process runner.
  Typed `sbx inspect --json` with System.Text.Json. Runs natively on macOS, Windows and Linux. Each OS-specific part
  is one interface implementation per OS. Versioned like any NuGet package.
- Bad: a rewrite. The .NET SDK (or runtime 10) is needed on the host. The tool has to be packed and installed, and
  updates take a reinstall.

### Option 3: A .NET 10 file-based app (`#!/usr/bin/env dotnet` + `agent-sandbox.cs`)

- Good: C# with no project and no packing; runs from the checkout like the script does today.
- Bad: one file. A test project can't reference it, so the logic would need a separate library anyway, which takes
  away the point of a single file. The first run after a change compiles it, which takes seconds.

### Option 4: Go (or another single-binary language)

- Good: a static binary per OS, no runtime on the host.
- Bad: a language the owner and this repository don't use, plus cross-compilation and distribution in CI.

## Decision

Chosen option: **Option 2, a .NET global tool**, because it is the only option that keeps the growing logic testable
in the owner's stack and runs on Windows without a second implementation. A file-based app loses testability, and
bash or Go each lose one of the two.

Details:

- **Layout.** `sandbox/cli/` holds `AgentSandbox.slnx`, `src/AgentSandbox/` (command `agent-sandbox`, `net10.0`,
  `RollForward=Major`) and `tests/AgentSandbox.Tests/` (xUnit). It also gets `Directory.Build.props` (nullable,
  warnings as errors), `Directory.Packages.props` (central versions) and `.editorconfig`. Renovate's built-in `nuget`
  manager already picks up the central package versions.
- **Dependencies.** System.CommandLine 2.0 handles parsing. It is stable and owned by Microsoft, and none of the
  Curiosus packages fits, as noted above. Processes run through a small `IProcessRunner` over
  `System.Diagnostics.Process` rather than CliWrap, because the launcher needs only run, capture and exec. Tests use
  a fake runner that returns scripted results and records the calls.
- **OS-specific parts behind interfaces.** `ISecretStore` holds the keychain lookup and the command that
  `sbx secret set --command` runs. `IBotSshAgent` loads the key into the bot's agent socket. The port implements macOS
  only. On other OSes the tool stops with a message that links to #28.
- **The kit ships inside the tool.** An installed tool can't find the kit relative to its own path, as the script does
  with `$script_dir/../kits/markeli-claude`. So `sandbox/kits/markeli-claude/` is embedded in the package and
  extracted per tool version under the user's local application data (`~/.local/share/agent-sandbox/kits/<version>/`
  on macOS and Linux). The kit is versioned together with the code that relies on it. `AGENT_SANDBOX_KIT` still
  overrides it, for example to try a kit from a working tree.
- **Build with Cake.** `sandbox/cli/build.cake` is the one build script, for local runs and for CI. Its task names
  follow the Curiosus `curiosus.cake`: `Clean`, `Build`, `Test`, `Default` (build and test) and `Pack`. It adds
  `Format`, which runs `dotnet format --verify-no-changes`, and `Install`. `Cake.Tool` is pinned in
  `sandbox/cli/.config/dotnet-tools.json`, and Renovate's `nuget` manager updates it. Run it from `sandbox/cli`:
  `dotnet tool restore && dotnet cake --target=<Task>`. The repository doesn't take the synced `curiosus.cake`:
  that script is built for Curiosus libraries, with nuget.org push, coverage badges and the Curiosus release flow,
  and syncing it would also bring the root `.editorconfig`, workflows and `.claude/curiosus.md` into this monorepo.
- **Install from the checkout, no feed.** `dotnet cake --target=Install` packs the tool into `sandbox/cli/artifacts/`
  and runs `dotnet tool update -g AgentSandbox --add-source artifacts`. That command installs or upgrades. There is
  no NuGet.org or GitHub Packages publishing. The tool hard-codes one owner's bot, key and keychain names, so it is not
  meant for other users, and GitHub Packages needs a token even to read. Revisit this when a second machine needs
  the tool (#28).
- **Behaviour parity first.** The port PR keeps the flags (`--no-attach`, `--recreate`, `--yes`, `--name`, `-- ARGS`),
  the environment overrides (`AGENT_SANDBOX_KIT`, `AGENT_SSH_SOCK`, `AGENT_SSH_KEY`), the messages and the exit
  codes; the review of the port turned `AGENT_SANDBOX_PULL` into the `--force-pull` flag. It replaces `sandbox/bin/agent-sandbox` and updates `sandbox/README.md`.
  #10, #9 and #13 follow in C#. The #10 implementation PR updates the bash-specific details in its spec, such as line
  references and `jq`.
- **CI.** A workflow runs on changes under `sandbox/cli/**` (and on its own changes). It runs the Cake `Default`
  and `Format` targets on `ubuntu-latest`. The tests don't touch the OS because processes are faked. The real run
  against `sbx` stays with the owner, as it does today.

Out of scope: the bash scripts in `.github/scripts/`. They run in Linux CI and are covered by their own tests.

## Consequences

- Positive:
  - The decisions for create, recreate, mounts and memory get unit tests, and `sbx inspect` is parsed with types.
  - Strict argument parsing comes from System.CommandLine.
  - Windows and Linux become implementations of two interfaces rather than a new launcher.
  - The kit and the launcher are released together.
- Negative:
  - The tool must be reinstalled (`dotnet cake --target=Install`) after changes. A `git pull` alone no longer
    updates it, and an old install keeps an old kit.
  - The .NET SDK is a host prerequisite.
  - The port PR changes how the owner starts the launcher: `agent-sandbox ...` from `PATH` instead of
    `sandbox/bin/agent-sandbox ...`.
  - The rewrite costs about a day before #10 can start.
- Follow-ups:
  - the port PR, with tests, `build.cake`, the CI workflow and the README;
  - then #10, #9 and #13 on top of it;
  - #28 adds the Windows implementations and the amd64 image.
