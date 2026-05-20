# Copilot Instructions – Coding Standards (Microsoft Best Practices)

Purpose: Define coding standards aligned with current Microsoft guidance. Always verify latest recommendations via `microsoft.docs.mcp` (Microsoft Learn MCP docs).

General principles
- Prefer latest supported .NET (target `.NET 10` where feasible) and C# language features.
- Enable nullable reference types and treat warnings as build-time issues.
- Favor async/await for I/O and network-bound work; avoid blocking calls.
- Keep code small, cohesive, and testable; prefer composition over inheritance.
- Use DI for services (constructor injection); avoid service locators.
- Follow SOLID, clean architecture boundaries, and separation of concerns.

Project setup
- Use SDK-style projects with explicit `<TargetFramework>` and `<Nullable>enable</Nullable>`.
- Treat warnings as errors for new code when possible: `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.

Naming & structure
- PascalCase for `class`, `method`, `property`, and `namespace` names.
- camelCase for local variables and parameters; PascalCase for constants only when appropriate.
- One public type per file; keep files under clear feature-folders.
- Use meaningful names; avoid abbreviations except accepted domain terms.
- Private fields prefixed with `_` (underscore) and camelCase.
- Use a single file per class or enum, struct or record unless nested.

Braces & formatting
- Braces must always be on a new line (Allman style).
- Do not use single-line brace patterns (e.g. `if (x) { DoThing(); }`).
- Always use block-scoped namespaces (e.g., `namespace X.Y { ... }`) rather than file-scoped namespaces.

Code commenting
- In addition to XML documentation for public APIs, add clear and concise developer comments explaining algorithms or logical flow when it is not simple or obvious.
- Use `//` comments and place them on their own line (do not use end-of-line trailing comments).

API & libraries
- Prefer Microsoft.Extensions.* libraries for logging, configuration, hosting, and options.
- Use `IOptions<T>`, `IOptionsSnapshot<T>` appropriately in server apps.
- Prefer minimal APIs and endpoint routing for new HTTP endpoints.
- Validate inputs and model state; return consistent problem details for errors.

Blazor/UI
- Prefer component parameters over cascading parameters; keep components pure.
- Use `@code` blocks for logic; move complex logic to services.
- Use `Task` methods for event handlers; avoid `async void`.
- Validate and handle nulls defensively when binding data.

Logging & telemetry
- Use `ILogger<T>` with structured logs (`{Name}` placeholders); avoid string concatenation.
- Log at appropriate levels; avoid sensitive data.
- Correlate using `Activity`/`TraceId` when applicable.

Error handling
- Use specific exceptions; avoid catch-all unless logging/fallback is required.
- Provide clear messages; prefer `ProblemDetails` in APIs.
- Implement retries with `Polly` when recommended for transient faults.

Performance & reliability
- Avoid synchronous over async; use `ConfigureAwait(false)` only in libraries where appropriate.
- Prefer streaming over buffering for large payloads.
- Use cancellation tokens for long-running operations.

Security
- Never hardcode secrets; use managed identities or secure stores.
- Validate untrusted inputs; encode/escape user content.
- Keep packages updated; monitor vulnerabilities.

Testing
- Use xUnit for unit tests; bUnit for Blazor components when applicable.
- Follow TDD where practical; cover success, error, and edge cases.
- Mock external dependencies; avoid network calls in unit tests.

Documentation & reviews
- Document public APIs with XML comments where helpful.
- Keep README and `docs/specs` updated for behavior changes.
- Use feature branches and PR reviews; enforce lint/build/test gates.

References
- Microsoft Learn (latest): `microsoft.docs.mcp` and product-specific docs (Blazor, ASP.NET Core, .NET)
- .NET coding conventions: https://learn.microsoft.com/dotnet/standard/design-guidelines
- ASP.NET Core fundamentals: https://learn.microsoft.com/aspnet/core
- Blazor best practices: https://learn.microsoft.com/aspnet/core/blazor
