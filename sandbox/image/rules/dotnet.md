# .NET code, tests and build

## Architecture

- Clean/DDD layering (API → Application → Domain → Infrastructure), dependency injection, async-first APIs; every
  async method accepts a `CancellationToken`.
- ASP.NET Core: minimal hosting, RESTful endpoints via `IEndpointRouteBuilder`, `[ApiController]` validation,
  `ProblemDetails`, central exception middleware, structured logging with `ILogger<T>`.
- EF Core: migrations first, `AsNoTracking()` for reads, avoid N+1 (consider `Include`/joins), repository and
  unit-of-work only when they pay off. Use `Curiosus.Migrations` for SQL/C# migrations where it fits.
- Security and performance: parameterised SQL only, validate input, HTTPS + HSTS, optional rate limiting; measure
  hotspots with `dotnet trace` or BenchmarkDotNet before optimising.

## Code style

- `.editorconfig` is the source of truth for indentation, line endings and formatting. Without one: tabs, LF.
- Lines up to 130 characters.
- Latest C# features, nullable reference types on, warnings as errors, Microsoft naming guidelines; private fields
  `_camelCase`, constants and static readonly fields PascalCase; prefer immutable records.
- Public APIs have XML docs. Other comments only for non-obvious reasoning.
- Central package versions (`Directory.Packages.props`) where the repository uses central package management.

## Tests

- xUnit (or the framework the repository already uses), Moq, FluentAssertions; `Microsoft.AspNetCore.Mvc.Testing` for
  web apps; integration tests against real infrastructure with Testcontainers. Cover edge and concurrency cases.
- Test projects target every .NET version the code supports. The sandbox has no .NET 8 runtime (`net8.0` targets
  still build with the .NET 10 SDK, but their tests can't run): run tests for
  `net9.0` and `net10.0` locally (`dotnet test --framework net10.0`, or `dotnet cake --target=Test --framework=net10.0`
  in Curiosus repositories) and leave `net8.0` to CI. Say in the PR description which frameworks you ran.
- A change is done when tests pass on all frameworks in CI, not only on the one you ran.
- Never lower a coverage threshold to make a build pass; raise it when coverage grows.

## Build and tooling

- If the repository has a build script (Cake `build.cake`, `Makefile`, scripts), use it: it is what CI runs.
  Plain `dotnet test <project> --filter "FullyQualifiedName~Name"` is fine for a focused loop.
- Run `dotnet format` and analyzers before committing when the repository uses them.
- Services ship with a Dockerfile; secrets come from `dotnet user-secrets` locally and environment variables on deploy.
