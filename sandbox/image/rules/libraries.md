# Dependencies and libraries

## Curiosus packages first

- Before writing a helper or adding a third-party dependency, check whether a package from
  [curiosus-dev](https://github.com/curiosus-dev) (`Curiosus.*` on NuGet) already solves it: `Curiosus.Utils`
  (configuration, hosting, data access, email and SMS, notifications, request processing, caching, SFTP, file
  readers/writers — see its "Available packages" table), `Curiosus.Migrations` (database migrations),
  `Curiosus.TelegramBot` (Telegram bots), `dotnet-tools` (shared build, CI, release and editor setup).
- Read the package's README or source before using it; don't guess its API.
- If it almost fits, propose improving the package instead of working around it. A third-party package comes in only
  when no Curiosus package covers the need; record that choice in an ADR when it is significant
  (see `/etc/claude-code/rules/specs-and-adrs.md`).

## Dependency versions

- Don't bump dependencies of a library without a reason (a security fix or a needed feature): its dependency version is
  the minimum version for its consumers. Test-only dependencies can be updated freely.

## Authoring and releasing packages

Applies when the repository publishes NuGet packages. In Curiosus repositories `.claude/curiosus.md` is the full
reference (Cake targets, release workflow, maintenance branches); follow it over this summary.

- Semantic Versioning: major for incompatible changes (public API, behaviour consumers rely on, dropped target
  frameworks, stored identifiers such as message type names), minor for compatible features, patch for compatible
  fixes. Pre-releases are `x.y.z-alpha.N` / `-beta.N` / `-rc.N`.
- CHANGELOG in [Keep a Changelog](https://keepachangelog.com) format: every change a consumer can notice (API,
  behaviour, dependencies, target frameworks, fixes) gets an entry under `## [Unreleased]`, grouped as `### Added`,
  `### Changed`, `### Deprecated`, `### Removed`, `### Fixed`, `### Security`. Tests, CI and internal refactoring
  need none. Breaking changes get a **Breaking:** note with migration steps.
- Don't add a version section unless asked to release. To release, turn `## [Unreleased]` into
  `## [x.y.z] - yyyy-mm-dd`, picking the bump from its entries.
- Root README sections, in order: title and one-line description; badges; **Why use it**; **Features**;
  **Quick start** (`dotnet add package` and the smallest working example); library-specific details;
  **Available packages** table (multi-package repositories); License. A package README packed for nuget.org uses
  absolute URLs for links and images.
