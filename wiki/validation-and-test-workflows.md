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

## WP004 API extraction start, status, history, and orchestration validation

The WP004 extraction slices are validated through application and API tests rather than a running Aspire AppHost. The application tests exercise request validation, path normalization, duplicate solution detection, outside-root rejection, no-run-on-validation-failure behavior, run creation, scheduling through the application seam, status lookup, recent-run ordering, progress update visibility, deterministic pipeline execution, placeholder stage boundaries, snapshot assembly, orchestration order, controlled failure handling, and persistence handoff through the application-layer writer port. The persistence-handoff coverage is intentionally explicit: tests prove the writer is invoked once for successful accepted runs, not invoked for validation or pipeline failures, and receives the complete generalized snapshot shape with repository, solution, snapshot header, nodes, edges, evidence, rules, findings, metrics, generated summaries, warnings, and errors sections. The API extraction tests exercise JSON route behavior for `POST /extractions`, `GET /extractions/{runId}`, and `GET /extractions` through an in-memory ASP.NET Core test server. They also verify the direct no-`/api` route contract, validation problem responses, metadata-value redaction, not-found behavior, terminal completed status with snapshot identity, status progress visibility, history summaries, and accepted-run failure redaction.

Use these focused commands from the repository root after building the changed projects:

```powershell
dotnet test .\test\Archon.Application.Tests\Archon.Application.Tests.csproj --no-build --filter FullyQualifiedName~StartExtractionApplicationServiceTests
dotnet test .\test\Archon.Application.Tests\Archon.Application.Tests.csproj --no-build --filter "FullyQualifiedName~ExtractionPipelineRunnerTests|FullyQualifiedName~ExtractionSnapshotAssemblerTests"
dotnet test .\test\Archon.Application.Tests\Archon.Application.Tests.csproj --no-build --filter FullyQualifiedName~ExtractionOrchestratorTests
dotnet test .\test\Archon.Application.Tests\Archon.Application.Tests.csproj --no-build --filter FullyQualifiedName~ArchitectureSnapshotAccumulatorTests
dotnet test .\test\Archon.Api.Extraction.Tests\Archon.Api.Extraction.Tests.csproj --no-build --filter FullyQualifiedName~ExtractionEndpointTests
dotnet test .\test\ArchonApi.Tests\ArchonApi.Tests.csproj --no-build --filter FullyQualifiedName~ArchonApiHealthEndpointTests
```

These commands do not start the Aspire AppHost, do not require Neo4j credentials, and do not execute full repository or Roslyn extraction. They prove that the API can accept valid start requests, reject invalid requests before creating run state, return status for accepted runs, list recent run summaries, expose progress updates through the application surface, run deterministic placeholder pipeline stages in application tests, assemble a generalized snapshot shape, hand the complete snapshot to persistence through a test double exactly once for successful accepted runs, record snapshot identity only after persistence success, convert pipeline/persistence/exception failures into controlled failed runs, sanitize unsafe diagnostic text at the HTTP boundary, and keep unrelated host endpoints absent until their own work packages implement them.

When a contributor needs to verify the API surface manually, use the examples on [API extraction workflow](api-extraction-workflow.md). Manual verification is an exploration activity, not an automated acceptance gate. It should use non-sensitive sample paths and metadata, should confirm the direct `/extractions` route family rather than `/api/extractions`, and should treat stack traces or secret-like values in responses as a bug. Automated validation for WP004 remains the focused build and test commands above; it must not start the Aspire AppHost.

## WP005 repository, solution, and project metadata extraction validation

The WP005 extraction slices replace placeholder pipeline behavior with real repository, submitted-solution, supported project, project-reference, analyzer-reference, FilePath, package-reference, and application type classification graph contributions. Their focused validation should still avoid the Aspire AppHost and should not require Neo4j credentials. The production project extractor tests create temporary repository roots, minimal Visual Studio solution files, supported C# or VB.NET project files, and project-adjacent package, analyzer, source, configuration, and build artifacts, then execute the `project-repository-solution` stage directly through the shared stage context. These tests prove repository node creation, solution node creation, multi-solution preservation, no unsubmitted solution scanning, solution-file evidence, project-declaration evidence, project-node creation, solution-to-project containment, project-file evidence, C# and VB.NET language metadata, SDK-style and old-style project metadata, target framework extraction, project-reference extraction, resolved `REFERENCES` edges, unresolved-reference warnings, duplicate-reference deduplication, repository-contained out-of-solution reference targets, analyzer metadata and evidence, missing repository-contained analyzer warnings, FilePath nodes for relevant artifacts, SDK-style package-reference extraction, legacy `packages.config` extraction, package nodes, `USES_PACKAGE` edges, local central package version resolution, unknown package version retention, imported repository-contained package declarations, unsafe import exclusion, duplicate package-reference deduplication, malformed legacy package warnings with evidence, application type classification for required categories, classification confidence bands, Unknown behavior for insufficient or contradictory evidence, deterministic classification metadata, XML snippet hashes and previews for supported evidence, unsupported project warnings, no-supported-project blocking behavior, and controlled malformed-solution errors.

Use these focused commands from the repository root after building changed projects:

```powershell
dotnet build .\src\Archon.Extractors.Projects\Archon.Extractors.Projects.csproj
dotnet build .\src\Archon.Api.Extraction\Archon.Api.Extraction.csproj
dotnet build .\test\Archon.Extractors.Projects.Tests\Archon.Extractors.Projects.Tests.csproj
dotnet build .\test\Archon.Application.Tests\Archon.Application.Tests.csproj
dotnet build .\test\Archon.Api.Extraction.Tests\Archon.Api.Extraction.Tests.csproj
dotnet test .\test\Archon.Extractors.Projects.Tests\Archon.Extractors.Projects.Tests.csproj --no-build --filter FullyQualifiedName~ProjectMetadataExtractionStageTests
dotnet test .\test\Archon.Extractors.Projects.Tests\Archon.Extractors.Projects.Tests.csproj --no-build
dotnet test .\test\Archon.Extractors.Projects.Tests\Archon.Extractors.Projects.Tests.csproj --no-build --filter FullyQualifiedName~RepositorySolutionExtractionStageTests
dotnet test .\test\Archon.Application.Tests\Archon.Application.Tests.csproj --no-build --filter FullyQualifiedName~ExtractionOrchestratorTests
dotnet test .\test\Archon.Api.Extraction.Tests\Archon.Api.Extraction.Tests.csproj --no-build --filter "FullyQualifiedName~ExtractionEndpointTests|FullyQualifiedName~AddArchonExtractionApi"
```

The solution fixtures used for this validation must contain a recognizable Visual Studio solution header. Empty `.sln` files still pass the earlier existence and extension validation boundary, but they are not valid evidence for the WP005 stage and should produce a controlled pipeline error during extraction. Supported C# and VB.NET declarations must point to real project XML files because the current stage reads those files for deterministic metadata. That distinction is intentional: request validation proves the path is allowed to be analyzed, while the project extraction stage proves the submitted file is useful solution evidence and that supported project declarations can be explained by project-file evidence.

## WP006 Roslyn semantic extraction validation

The current WP006 slice validates compiler-backed C# and VB.NET declaration and relationship extraction without starting the Aspire AppHost, Neo4j, API endpoints, MCP tools, repository scanning, or Visual Studio automation. The shared Roslyn tests cover repository-relative path normalization, semantic stable-key determinism, symbol-reference key determinism, relationship-source key disambiguation, snippet preview limits, and snippet hash determinism. The C# and VB.NET Roslyn tests create in-memory syntax trees and compilations, obtain real semantic models, and assert that namespace, type, constructor, method, property, field, evidence, `CONTAINS`, `CALLS`, `IMPLEMENTS`, `INHERITS`, `INJECTS`, and `DEPENDS_ON` relationship facts are emitted deterministically. The VB.NET tests also cover modules, structures, delegates, events, constants, default properties, shared members, extension methods, generic constraints, and root namespace effects.

Use these focused commands from the repository root after package restore when Roslyn semantic extraction changes:

```powershell
dotnet test .\test\Archon.Roslyn.Tests\Archon.Roslyn.Tests.csproj --no-restore
dotnet test .\test\Archon.Roslyn.CSharp.Tests\Archon.Roslyn.CSharp.Tests.csproj --no-restore
dotnet test .\test\Archon.Roslyn.VisualBasic.Tests\Archon.Roslyn.VisualBasic.Tests.csproj --no-restore
dotnet build .\Archon.slnx --no-restore
```

These commands are intentionally narrower than a full test-suite run. They validate the shared semantic helper layer, the C# declaration and relationship extractor, the VB.NET declaration and relationship extractor, and integrated solution compilation. When package references have changed or a clean environment is being used, run `dotnet restore .\Archon.slnx` first and then repeat the commands with `--no-restore` so failures are attributable to compile or test behavior rather than package acquisition.

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
