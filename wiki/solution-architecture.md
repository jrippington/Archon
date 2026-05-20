# Solution Architecture

Archon uses **Onion Architecture**, which means dependencies point inward toward stable business concepts and away from delivery or infrastructure details. The domain model sits at the center, application contracts coordinate use cases around that model, infrastructure projects adapt external systems, and host projects compose runtime behavior at the outer edge. This separation matters because Archon is intended to reason about architecture facts over time; if the core model depended on a web host, database driver, or UI framework, later extraction, query, and reporting work would inherit avoidable coupling.

For term definitions used on this page, see the [glossary](glossary.md). For runtime composition details, continue to the [runtime foundation](runtime-foundation.md). For commands that validate these boundaries, use [validation and test workflows](validation-and-test-workflows.md).

Reader path: [Home](home.md) -> Solution architecture -> [Runtime foundation](runtime-foundation.md) -> [Graph domain model](graph-domain-model.md).

## Project families

Production projects live under `src`, and test projects live under `test`. Every production project has a corresponding test project. This structure was established as part of the WP001 foundation so later work packages can add behavior in the correct architectural slice instead of reshaping the repository each time a new capability appears.

The main project families are:

- Host and composition projects, including the Aspire AppHost in `src/Archon`, `src/ArchonApi`, and `src/ArchonMcp`.
- Core projects, including `src/Archon.Domain` and `src/Archon.Application`.
- API module projects, which provide future homes for extraction, query, and management endpoints without putting feature logic directly into the API host.
- Roslyn and extractor projects, which provide future analysis and source-evidence extraction areas.
- Infrastructure projects, including Roslyn workspace loading, Neo4j persistence, and markdown export adapters.

The Discovery UI is intentionally absent in the current foundation. No `ArchonUi` or `ArchonUi.Tests` project exists, and no AppHost resource composes a UI. Missing UI behavior is a correctness condition for the current foundation, not an accidental omission.

## Dependency direction

The `Archon.Domain` project is the center. It must not reference application, infrastructure, API module, extractor, Roslyn implementation, or host projects. `Archon.Application` may depend on the domain layer, but it must not depend on infrastructure or hosts. API modules and extractors can depend on application-facing contracts. Infrastructure adapts external systems such as Neo4j, and hosts wire the outer runtime together.

Host projects are delivery and composition endpoints. They may compose infrastructure and project resources, but they must not become a home for domain rules, extraction logic, graph persistence behavior, API feature logic, or MCP tool implementation. The Aspire AppHost is the clearest example: it may declare that Neo4j, `ArchonApi`, and `ArchonMcp` run together, but it must not implement the business behavior those services eventually expose.

## AppHost composition exception

One important project-reference exception is intentional. The `Archon` AppHost references `ArchonApi` and `ArchonMcp` so Aspire can compose them as project resources. That edge is an outer-boundary composition reference. It is not an inward dependency from domain or application code and does not weaken the Onion Architecture rule.

Boundary tests in `test/Archon.Tests` inspect project files, classify projects into layers, and assert these dependency rules. They also verify that production projects do not reference test projects and that the UI projects excluded from WP001 remain absent.

## Project identity

Archon normalizes project identity as a repository-root-relative `.csproj` path written with forward slashes, such as `src/Archon.Domain/Archon.Domain.csproj`. This is deliberately different from an absolute machine path such as `D:\Dev\Archon\src\Archon.Domain\Archon.Domain.csproj`.

Relative normalized identities remain stable across Windows workstations, Linux build agents, and CI environments. That stability matters because Archon later compares architecture evidence collected from different machines and different repository clones. A path that depends on a developer's local clone root would produce false differences and unstable graph identities.

## Package boundary for Neo4j

The official `Neo4j.Driver` package belongs only in `Archon.Infrastructure.Neo4j`. It must not appear in `Archon.Domain` or `Archon.Application`. Application ports such as `IArchitectureSnapshotWriter` describe persistence behavior in Archon terms, not database-driver terms. Keeping the Neo4j driver in the outer adapter preserves testability and leaves room for future persistence or query strategies without changing the core contracts.

The boundary validation command for the architecture and Neo4j package rules is:

```powershell
dotnet test .\test\Archon.Tests\Archon.Tests.csproj --filter "FullyQualifiedName~Boundary|FullyQualifiedName~Neo4jDriver"
```

This command validates project-reference Onion rules and Neo4j driver package boundaries without starting any host or container.
