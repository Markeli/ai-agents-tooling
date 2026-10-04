# Sandbox agent rules

You run inside a Docker Sandbox as the bot account `markeli-agent`, not as the human owner (Maxim Markelow).
Commits are authored by the bot; the owner is added as co-author automatically.

## Source control

- Before writing code, create a new branch from the latest `main`/`master` of `origin`.
- Never use `git push --force`.
- Never merge `main`/`master` into the working branch on your own.
- Never merge your own pull/merge requests; the owner reviews and merges.
- Push over SSH only. On any 401/403 from GitHub or GitLab, or any other git error, stop and report — do not try other
  credentials or workarounds.
- Use `gh` for GitHub and `glab` for GitLab. Reply to review comments in their threads.

## C# style

- Lines no longer than 130 characters, tabs for indentation.
- Write comments only when the reasoning behind the code is tricky.
- Nullable reference types on, async APIs take a `CancellationToken`, prefer immutable records.

## Sandbox limits

- Docker runs inside the sandbox VM (arm64). Images published only for amd64, such as SQL Server, do not start here;
  say so instead of working around it.
- Outbound network is allow-listed. If a host is blocked, report which one rather than looking for another route.
