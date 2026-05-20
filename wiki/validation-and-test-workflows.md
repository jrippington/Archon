# Validation and Test Workflows

Archon's validation model is test-led. Automated validation should build the solution and run targeted test projects. Manual runtime checks are useful for local exploration, but they must not replace automated build and test validation and must not start the Aspire AppHost as a blocking automation step.

Use this page with the [runtime foundation](runtime-foundation.md), [solution architecture](solution-architecture.md), and [Neo4j persistence foundation](neo4j-persistence-foundation.md) pages. Terms such as AppHost, Testcontainers, readiness, liveness, and relationship-node pattern are defined in the [glossary](glossary.md).

Reader path: [Home](home.md) -> Validation and test workflows -> [Work-package documentation workflow](work-package-documentation-workflow.md).

## Baseline restore and build

Start from the repository root:

```powershell
dotnet restore .\Archon.slnx
dotnet build .\Archon.slnx --no-restore
```

Run restore before build when package or project references change. Use `--no-restore` after a successful restore so build failures point to compile or project-graph issues rather than repeating package resolution.

## WP001 runtime foundation tests

The WP001 foundation uses targeted tests instead of a blocking AppHost run. The service-default, API, and MCP test projects validate shared runtime defaults and probe-only host behavior. The `Archon.Tests` project validates AppHost composition metadata, project identity, and Onion Architecture boundaries:

```powershell
dotnet test .\test\Archon.ServiceDefaults.Tests\Archon.ServiceDefaults.Tests.csproj --no-build
dotnet test .\test\ArchonApi.Tests\ArchonApi.Tests.csproj --no-build
dotnet test .\test\ArchonMcp.Tests\ArchonMcp.Tests.csproj --no-build
dotnet test .\test\Archon.Tests\Archon.Tests.csproj --no-build --filter FullyQualifiedName~AppHostComposition
dotnet test .\test\Archon.Tests\Archon.Tests.csproj --no-build --filter FullyQualifiedName~Boundary
dotnet test .\test\Archon.Tests\Archon.Tests.csproj --no-build --filter FullyQualifiedName~ProjectIdentity
```

These tests do not start the Aspire AppHost, do not start Neo4j through Aspire, and do not require a long-running dashboard process.

## Manual AppHost verification

Manual AppHost verification is described in detail in [runtime foundation](runtime-foundation.md). It is intentionally separate from automated validation. Use it when you want to inspect the local distributed application, not when you need a build/test gate.

```powershell
dotnet run --project .\src\Archon\Archon.csproj
```

Before running this command, make sure Docker Desktop or another OCI-compatible runtime is available. In the Aspire dashboard, confirm that `neo4j`, `ArchonApi`, and `ArchonMcp` appear and that no `ArchonUi` or Discovery UI resource appears. Stop the AppHost manually after verification.

## WP002 graph domain validation

The WP002 graph domain model is pure domain and application behavior. It should be validated through targeted domain and application tests:

```powershell
dotnet test .\test\Archon.Domain.Tests\Archon.Domain.Tests.csproj --filter "FullyQualifiedName~ControlledValue|FullyQualifiedName~StableKey|FullyQualifiedName~Metadata|FullyQualifiedName~Fingerprint|FullyQualifiedName~GraphFact|FullyQualifiedName~Unknown|FullyQualifiedName~Evidence"
dotnet test .\test\Archon.Application.Tests\Archon.Application.Tests.csproj --filter FullyQualifiedName~Accumulation
```

These commands validate controlled values, stable keys, metadata canonicalization, fingerprints, graph fact models, unknown-state rules, evidence records, and application-layer snapshot accumulation. They do not run Roslyn extraction, write Neo4j data, expose APIs, invoke MCP behavior, render markdown, or start UI behavior.

## WP003 Neo4j validation and Testcontainers

Neo4j integration tests use Testcontainers instead of the Aspire AppHost. **Testcontainers** starts short-lived Docker containers under test control and removes them after the test run. Docker Desktop or another OCI-compatible runtime must be running for these tests.

The first Neo4j slice validates options, driver lifecycle, and the readiness health check:

```powershell
dotnet test .\test\Archon.Infrastructure.Neo4j.Tests\Archon.Infrastructure.Neo4j.Tests.csproj --filter "FullyQualifiedName~Neo4jOptions|FullyQualifiedName~Neo4jHealth"
```

Schema initialization validation runs initialization twice and inspects Neo4j metadata:

```powershell
dotnet test .\test\Archon.Infrastructure.Neo4j.Tests\Archon.Infrastructure.Neo4j.Tests.csproj --filter "FullyQualifiedName~GraphSchema|FullyQualifiedName~GraphInitialization"
```

Guarded graph recreation validation seeds representative records, proves unauthorized requests leave data intact, proves authorized recreation clears Archon-owned records, and verifies schema remains initialized:

```powershell
dotnet test .\test\Archon.Infrastructure.Neo4j.Tests\Archon.Infrastructure.Neo4j.Tests.csproj --filter FullyQualifiedName~GraphRecreation
```

Minimal snapshot persistence validation writes representative minimal snapshots and verifies stable-key lookup, fingerprint lookup, evidence deduplication, and missing reference errors:

```powershell
dotnet test .\test\Archon.Infrastructure.Neo4j.Tests\Archon.Infrastructure.Neo4j.Tests.csproj --filter "FullyQualifiedName~MinimalSnapshot|FullyQualifiedName~EvidenceDeduplication"
```

Architecture relationship persistence validation verifies relationship-node counts, source and target endpoint links, edge-to-evidence links, traversal queryability, and missing reference failures:

```powershell
dotnet test .\test\Archon.Infrastructure.Neo4j.Tests\Archon.Infrastructure.Neo4j.Tests.csproj --filter "FullyQualifiedName~ArchitectureRelationship|FullyQualifiedName~EdgeEvidence"
```

Rule catalog and finding persistence validation verifies rule upsert behavior by rule code plus version, finding properties, support links, and missing reference failures:

```powershell
dotnet test .\test\Archon.Infrastructure.Neo4j.Tests\Archon.Infrastructure.Neo4j.Tests.csproj --filter "FullyQualifiedName~RuleCatalog|FullyQualifiedName~FindingPersistence"
```

Metric and generated-summary persistence validation verifies metric properties, metric support links, generated-summary properties, generated-summary target links, mixed metric/summary counts, and missing target behavior:

```powershell
dotnet test .\test\Archon.Infrastructure.Neo4j.Tests\Archon.Infrastructure.Neo4j.Tests.csproj --filter "FullyQualifiedName~MetricPersistence|FullyQualifiedName~GeneratedSummary"
```

Full mixed snapshot validation proves the Neo4j persistence features compose as one foundation. It writes a representative `ExtractedArchitectureSnapshot` containing every WP003 graph section and verifies every required support path:

```powershell
dotnet test .\test\Archon.Infrastructure.Neo4j.Tests\Archon.Infrastructure.Neo4j.Tests.csproj --filter "FullyQualifiedName~FullMixedSnapshot|FullyQualifiedName~SupportingRelationship|FullyQualifiedName~Neo4jInfrastructureComposition"
```

## Final WP003 closure validation

The complete WP003 verification path is:

```powershell
dotnet test .\test\Archon.Infrastructure.Neo4j.Tests\Archon.Infrastructure.Neo4j.Tests.csproj
dotnet test .\test\Archon.Tests\Archon.Tests.csproj --filter "FullyQualifiedName~Boundary|FullyQualifiedName~Neo4jDriver"
dotnet build .\Archon.slnx
```

The first command runs the Neo4j infrastructure test project, including real-container health, schema, recreation, persistence, and full mixed snapshot coverage. The second command verifies Onion Architecture and package-boundary rules that keep `Neo4j.Driver` out of `Archon.Domain` and `Archon.Application`. The final build confirms the repository still compiles as an integrated solution.

None of these commands starts the Aspire AppHost. Manual AppHost startup remains useful for local runtime exploration, but automated validation uses tests and build commands so it can finish without waiting on a long-running dashboard process.
