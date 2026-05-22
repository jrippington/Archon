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

The current WP006 slice validates compiler-backed C# and VB.NET declaration, relationship, degraded diagnostic, unknown extraction, API orchestration, and snapshot projection without starting the Aspire AppHost, MCP tools, repository-wide scanning, Visual Studio automation, or a live Neo4j database for mapper-only tests. The shared Roslyn tests cover repository-relative path normalization, semantic stable-key determinism, symbol-reference key determinism, diagnostic and unknown stable keys, relationship-source key disambiguation, snippet preview limits, snippet hash determinism, and degraded extraction result contracts. The C# and VB.NET Roslyn tests create in-memory syntax trees and compilations, obtain real semantic models, and assert that namespace, type, constructor, method, property, field, evidence, `CONTAINS`, `CALLS`, `IMPLEMENTS`, `INHERITS`, `INJECTS`, and `DEPENDS_ON` relationship facts are emitted deterministically. They also validate degraded compilations with missing references, explicit unknowns for unresolved symbols, ambiguous or unsupported calls, C# dynamic dispatch, Visual Basic late-bound calls, reflection targets, generated-code metadata, partial declaration evidence contributions, metadata-only dependencies, and confidence values. The VB.NET tests also cover modules, structures, delegates, events, constants, default properties, shared members, extension methods, generic constraints, and root namespace effects. Infrastructure and API tests now verify that semantic extraction runs through the shared API extraction path, that semantic facts reach the application-layer snapshot writer seam, that deterministic graph keys survive repeated extraction, and that generic Neo4j mapping preserves semantic node, relationship, evidence, confidence, unknown-state, and metadata fields.

Use these focused commands from the repository root after package restore when Roslyn semantic extraction changes:

```powershell
dotnet test .\test\Archon.Roslyn.Tests\Archon.Roslyn.Tests.csproj --no-restore
dotnet test .\test\Archon.Roslyn.CSharp.Tests\Archon.Roslyn.CSharp.Tests.csproj --no-restore
dotnet test .\test\Archon.Roslyn.VisualBasic.Tests\Archon.Roslyn.VisualBasic.Tests.csproj --no-restore
dotnet test .\test\Archon.Roslyn.Legacy.Tests\Archon.Roslyn.Legacy.Tests.csproj --no-restore
dotnet test .\test\Archon.Infrastructure.Roslyn.Tests\Archon.Infrastructure.Roslyn.Tests.csproj --no-restore
dotnet test .\test\Archon.Api.Extraction.Tests\Archon.Api.Extraction.Tests.csproj --no-restore
dotnet test .\test\Archon.Infrastructure.Neo4j.Tests\Archon.Infrastructure.Neo4j.Tests.csproj --no-restore --filter FullyQualifiedName~Neo4jSnapshotPersistenceMapperTests
dotnet build .\Archon.slnx --no-restore
```

These commands are intentionally narrower than a full test-suite run. They validate the shared semantic helper layer, the C# declaration and relationship extractor, the VB.NET declaration and relationship extractor, legacy generated-code classification, infrastructure solution/project/source loading, API orchestration and persistence-seam handoff, generic Neo4j mapper behavior, and integrated solution compilation. When package references have changed or a clean environment is being used, run `dotnet restore .\Archon.slnx` first and then repeat the commands with `--no-restore` so failures are attributable to compile or test behavior rather than package acquisition.

## WP007 configuration and dependency-injection extraction validation

The current WP007 implementation covers the Microsoft dependency-injection registration slice, hosted-service recognition, HttpClientFactory registration recognition, registration-wrapper traversal, constructor dependency correlation for registered implementation types, legacy container recognition, CommonServiceLocator and manual-factory heuristics, modern appsettings/options configuration extraction, legacy `.config`/`ConfigurationManager` extraction, the top-level `ConfigurationExtractor` that composes modern and legacy configuration slices into one merged snapshot, and API extraction pipeline composition through the `wp007-configuration-dependency-injection` stage. The DI tests compile in-memory C# fixtures, bind direct `AddSingleton<TService, TImplementation>()`, `AddScoped<TService, TImplementation>()`, and `AddTransient<TService, TImplementation>()` calls through Roslyn, and also validate service-only overloads, `typeof(...)` overloads, factory registrations with explicit unknown implementation state, descriptor-based `TryAdd`, `TryAddEnumerable`, and `Replace`, `AddHostedService<T>()`, BackgroundService assignability, default, named, typed, and typed-implementation `AddHttpClient` calls, wrapper methods that accept `IServiceCollection`, wrapper cycles, recursion-depth safeguards, unavailable wrapper source, dynamic invocations, constructor-driven `INJECTS` and `DEPENDS_ON` relationships, Unity, Autofac, Castle Windsor, StructureMap, Ninject, SimpleInjector, CommonServiceLocator, manual factory facts, unsupported legacy scanning unknowns, legacy container warnings, confidence bands, and evidence quality. The configuration tests create temporary appsettings and `.config` files, compile in-memory C# fixtures, bind modern and legacy configuration usage through Roslyn, and validate normalized `ConfigurationKey` nodes, centralized `config://` stable keys, `USES_CONFIG` relationships, options binding and options injection metadata, `ConfigurationManager.AppSettings` and `ConfigurationManager.ConnectionStrings` metadata, dynamic-key unknowns, unknown-source-provider facts, deterministic secret redaction, malformed XML warnings, evidence quality, modern-only and legacy-only responsibility boundaries, composed extractor output, and no target application configuration-code execution. The API extraction tests run an in-memory HTTP host, submit accepted extraction runs, replace only the snapshot writer seam, and verify that WP007 dependency-injection and configuration facts are accumulated with repository, project, and semantic facts in the persisted snapshot. These tests do not start the Aspire AppHost, do not require Neo4j credentials, do not invoke MCP tools, and do not scan unrelated repositories.

Use this focused command when changing the dependency-injection extractor:

```powershell
dotnet test .\test\Archon.Extractors.DependencyInjection.Tests\Archon.Extractors.DependencyInjection.Tests.csproj
```

Use this focused command when changing the configuration extractor:

```powershell
dotnet test .\test\Archon.Extractors.Configuration.Tests\Archon.Extractors.Configuration.Tests.csproj
```

Use this focused command when changing the API pipeline stage composition or the API-triggered WP007 snapshot handoff:

```powershell
dotnet test .\test\Archon.Api.Extraction.Tests\Archon.Api.Extraction.Tests.csproj --no-restore
```

Use the integrated build gate after the focused tests pass:

```powershell
dotnet build .\Archon.slnx --no-restore
```

Run restore first when dependency-injection or configuration extractor package or project references have changed, then use `--no-restore` for the build gate so later failures point to compile or validation behavior. Contributor-facing details for the DI graph shape, configuration key graph shape, evidence, stable keys, redaction, and currently supported registration and configuration forms are described in [configuration and dependency-injection extraction](configuration-and-dependency-injection-extraction.md).

## WP008 runtime extraction validation

The current WP008 validation path covers runtime extraction both at extractor level and at the API-triggered orchestration seam. The extractor tests validate focused static-analysis behavior for ASP.NET Core minimal API endpoints, endpoint groups, controllers and actions, pipeline metadata, C# and VB.NET console entry points, ambiguous console entry points, worker hosted services, generic-host setup, hosted-service registration correlation, scheduled jobs with literal schedules, scheduled jobs with computed schedules, queue and topic consumers, computed queue names, message handler subscriptions, service-style host setup, and conservative custom host loops. The classic ASP.NET extractor tests validate `System.Web` application artifacts, `Global.asax`, `web.config`, Web Forms pages and user controls, HTTP handlers, HTTP modules, MVC 5 controllers, Web API 2 controllers, and conventional-route unknowns. The API extraction tests run an in-memory ASP.NET Core test host, submit explicit repository root and solution path lists, replace only the snapshot writer seam, and verify that runtime facts from modern web, console, worker, queue, and scheduled-job source reach the generalized snapshot alongside earlier project, semantic, configuration, and dependency-injection facts.

Use these focused commands when changing WP008 runtime extraction or its API orchestration integration:

```powershell
dotnet test .\test\Archon.Api.Extraction.Tests\Archon.Api.Extraction.Tests.csproj --no-restore
dotnet test .\test\Archon.Extractors.AspNet.Tests\Archon.Extractors.AspNet.Tests.csproj --no-restore
dotnet test .\test\Archon.Extractors.LegacyWeb.Tests\Archon.Extractors.LegacyWeb.Tests.csproj --no-restore
dotnet build .\Archon.slnx --no-restore
```

These commands do not start the Aspire AppHost, do not require Neo4j credentials, do not run the target applications being analyzed, and do not connect to external brokers, schedulers, Windows service managers, or MCP tools. A successful API orchestration test proves that WP008 uses the accepted repository root and explicit solution path list, receives accumulated context from earlier stages where available, preserves runtime warnings and unknowns in the same snapshot contract as other stages, and hands graph-ready facts to the application-layer `IArchitectureSnapshotWriter` instead of letting extractor projects write directly to persistence. If a runtime fixture has partial or degraded source context, the expected behavior is a controlled warning or explicit unknown state; unrelated graph facts should still be present and deterministic.

When adding or changing WP008 fixtures, name the test after the runtime evidence shape rather than after an implementation helper. Good names describe the supported or unsupported scenario: computed route, literal endpoint group, ambiguous entry point, uncorrelated hosted service, literal scheduled job, computed schedule, literal queue, computed queue, topic subscription, Windows-service setup, Topshelf setup, custom host loop, Web Forms page, HTTP handler, or conventional classic route. Assertions should cover the graph node kind, relationship direction, stable lower-camel-case metadata, confidence or unknown state when relevant, repository-relative evidence path, snippet hash or preview when available, and absence of unexpected errors. A fixture that proves unsupported or partial behavior should assert the warning or unknown reason explicitly instead of only checking that extraction does not throw.

The build gate remains part of WP008 validation even when a change appears documentation-heavy. Source comments and wiki edits can still expose stale names, broken XML documentation, or invalid generated samples through tests and build. If package references have changed, restore first; otherwise use the `--no-restore` commands above after a successful restore so failures point to compile, extraction, or documentation consistency issues rather than package acquisition.

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
