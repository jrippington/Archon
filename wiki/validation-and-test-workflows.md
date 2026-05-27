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

Persistence-diagnostic hardening uses the same application and API validation path, with an additional Neo4j infrastructure focus when adapter behavior changes. Application orchestration tests should cover successful diagnostics, failed partial diagnostics, failed no-diagnostic compatibility, preservation of the top-level `Persistence` and `Total` timings, persistence progress remaining at the top-level lifecycle stage, and warning merge behavior when pipeline warnings and persistence diagnostic warnings are both present. Neo4j infrastructure tests should cover completed diagnostics, validation failures before write stages, initialization failures before write transactions, safe error translation, and absence of sensitive details such as Bolt endpoints, raw Cypher, driver type names, connection-string fragments, and stack traces. These tests intentionally inspect the application-owned result and status contracts rather than querying private collector state.

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

## WP009 data-access extraction validation

The current WP009 validation path covers LINQ to SQL DBML model extraction, generated designer extraction, source usage extraction, EF6 extraction, EF Core extraction, ADO.NET/raw SQL extraction, and typed DataSet extraction at both extractor level and API orchestration level. The data-access extractor tests create temporary repository roots with complete, partial, malformed, and secret-bearing `.dbml` files, typed DataSet `.xsd` files, generated designer source, generated typed DataSet source, LINQ to SQL usage source, typed DataSet TableAdapter usage source, EF6 source fixtures, EF Core source fixtures, and ADO.NET fixtures, then execute the production data-access extractor entry path directly. These tests validate `LinqToSqlDataContext`, `DbContext`, `Entity`, `DatabaseTable`, `DatabaseColumn`, `StoredProcedure`, `GeneratedArtifact`, `Method`, and raw SQL node emission; `MAPS_ENTITY`, `MAPS_TABLE`, `MAPS_COLUMN`, `USES_LINQ_TO_SQL_CONTEXT`, `USES_DB_CONTEXT`, `READS_TABLE`, `WRITES_TABLE`, `CALLS_STORED_PROCEDURE`, `REFERENCES`, and `EXECUTES_RAW_SQL` relationship emission; deterministic model-scoped and project-scoped stable keys; DBML/designer/source deduplication; typed DataSet XSD/generated-source/usage correlation; EF context/entity/table/column/migration/provider facts; ADO.NET command, provider, stored-procedure, read/write, dynamic SQL, SQL hash/preview, and affected-table facts; DBML and XSD evidence with repository-relative file paths, XML line metadata, snippet hashes, and snippet previews; designer-generated-code evidence; source-code evidence; confidence and explicit unknown state for partial model identity, partial typed DataSet table metadata, convention-only EF table identities, EF Core shadow properties, unresolved `GetTable<T>()` targets, computed SQL, and missing command text; malformed XML warnings; and redaction of secret-like connection-string or SQL literal content.

The API extraction tests run an in-memory ASP.NET Core test host, submit an accepted extraction run, replace only the snapshot writer seam, and verify that data-access facts reach the generalized snapshot alongside earlier extraction-stage output. The current API coverage also includes the final WP009 cross-slice correlation path: a fixture combines configuration files, dependency-injection `AddDbContext` registration, runtime source methods, EF context usage, and ADO.NET command usage so the test can assert `USES_CONFIG`, `USES_DB_CONTEXT`, and runtime-to-data-access `DEPENDS_ON` correlation relationships in the persisted snapshot. These tests do not start the Aspire AppHost, do not require Neo4j credentials, do not connect to target databases, do not execute generated LINQ to SQL or typed DataSet designer code, do not instantiate EF contexts, do not execute `OnModelCreating`, do not apply EF migrations, do not open ADO.NET connections, do not execute SQL command text, and do not scan unsubmitted solution files. Source usage recognition remains bounded by the submitted solution list; DBML and typed DataSet XSD discovery remain repository-root based because these XML model artifacts may not be compile items.

Use these focused commands when changing WP009 LINQ to SQL, EF6, EF Core, ADO.NET, typed DataSet, raw SQL, or API orchestration integration:

```powershell
dotnet test .\test\Archon.Extractors.DataAccess.Tests\Archon.Extractors.DataAccess.Tests.csproj
dotnet test .\test\Archon.Extractors.DataAccess.Tests\Archon.Extractors.DataAccess.Tests.csproj --filter TypedDataSet
dotnet test .\test\Archon.Extractors.DataAccess.Tests\Archon.Extractors.DataAccess.Tests.csproj --filter AdoNet
dotnet test .\test\Archon.Extractors.DataAccess.Tests\Archon.Extractors.DataAccess.Tests.csproj --filter RawSql
dotnet test .\test\Archon.Api.Extraction.Tests\Archon.Api.Extraction.Tests.csproj --filter DataAccess
dotnet build .\Archon.slnx --no-restore
```

Contributor-facing details for DBML graph shape, generated designer graph shape, EF6 and EF Core graph shape, ADO.NET command graph shape, typed DataSet graph shape, generated typed DataSet source correlation, source usage relationships, raw SQL handling, stored procedure handling, affected-table hints, dynamic SQL unknowns, migrations, provider configuration, evidence, stable keys, redaction, confidence, unknown state, API stage boundaries, and current exclusions are described in [data access extraction](data-access-extraction.md).

## WP010 external integration extraction validation

The current WP010 validation path covers the integration extractor project marker, deterministic stable keys for known and unknown external services, queues, topics, and relationships, no-op foundation extraction, minimal observation-to-graph projection, explicit unknown handling, cancellation behavior, HTTP and REST source detection, WCF/SOAP/gRPC generated-client detection, messaging detection, storage detection, SMTP/email detection, payment-provider detection, internal service API correlation, redaction, deduplication, API stage registration, API stage no-op execution, snapshot accumulation, and provider warning/error propagation. HTTP and REST detector tests compile in-memory C# fixtures with local API stubs so Roslyn can bind `HttpClient`, `IHttpClientFactory`, `AddHttpClient`, RestSharp, and wrapper patterns without restoring target application packages or contacting external systems. RPC and generated-client tests combine in-memory semantic documents with temporary repository artifacts so the extractor can inspect WCF service references, ASMX generated proxy hints, endpoint configuration, gRPC generated source, `GrpcChannel` setup, and oversized generated-file safeguards without executing generated code. Messaging tests combine in-memory semantic documents with local configuration artifacts so the extractor can inspect Azure Service Bus senders, receivers, processors, topic subscriptions, NServiceBus endpoints, handlers, sagas, RabbitMQ queues, exchanges, routing keys, MSMQ queue paths, and queue abstractions without connecting to brokers. Storage, SMTP/email, and payment tests use temporary repository roots, local `appsettings*.json` artifacts, and in-memory source stubs so the extractor can inspect Azure Blob Storage, Azure File Storage, generic storage abstractions, `SmtpClient`, email sender abstractions, Stripe-style SDK usage, and payment gateway wrappers without opening storage accounts, sending mail, or calling payment providers. Internal service tests combine client-side in-memory C# source with prior endpoint facts so correlation can prove route ownership, reject false positives, record explicit unknowns, check redaction, check stable-key deduplication, honor cancellation, and document the current VB.NET parity limit without starting service hosts. These tests use in-memory observations, semantic documents, temporary local artifacts, endpoint facts, and stage contexts; they do not start the Aspire AppHost, do not call external services, do not connect to brokers or storage accounts, do not send email, do not call payment providers, and do not require credentials.

Use these focused commands when changing WP010 foundation contracts, HTTP/REST detectors, RPC/generated-client detectors, messaging detectors, storage/email/payment detectors, stable-key behavior, graph projection, provider seams, or API orchestration integration:

```powershell
dotnet test .\test\Archon.Extractors.Integrations.Tests\Archon.Extractors.Integrations.Tests.csproj
dotnet test .\test\Archon.Extractors.Integrations.Tests\Archon.Extractors.Integrations.Tests.csproj --filter RpcGeneratedClientIntegrationExtractorTests
dotnet test .\test\Archon.Extractors.Integrations.Tests\Archon.Extractors.Integrations.Tests.csproj --filter MessagingIntegrationExtractorTests
dotnet test .\test\Archon.Extractors.Integrations.Tests\Archon.Extractors.Integrations.Tests.csproj --filter ExternalServiceIntegrationExtractorTests
dotnet test .\test\Archon.Extractors.Integrations.Tests\Archon.Extractors.Integrations.Tests.csproj --filter InternalServiceIntegrationExtractorTests
dotnet test .\test\Archon.Api.Extraction.Tests\Archon.Api.Extraction.Tests.csproj --filter Wp010ExternalIntegrationExtractionStageTests
dotnet build .\Archon.slnx --no-restore
```

When package or project references have changed, run `dotnet restore .\Archon.slnx` before using `--no-restore` for the build gate. Future concrete detector slices should add focused tests in the integration extractor test project for supported evidence shapes and degraded unknown shapes. A detector fixture should assert graph node kind, relationship kind and direction, deterministic stable key, repository-relative evidence path, redacted snippet behavior when relevant, confidence or unknown state, warning text for unsupported dynamic shapes, and the absence of live external access. HTTP and REST fixtures should additionally assert operation metadata, relative path hints, named-client or typed-client metadata, `USES_CONFIG` relationships for configuration-backed endpoints, RestSharp method/resource recognition, wrapper-abstraction conservatism, and deduplication of repeated call-site facts. RPC and generated-client fixtures should additionally assert WCF endpoint configuration names, binding metadata, service contracts, SOAP or ASMX generated proxy evidence, gRPC channel and typed-client metadata, explicit unknowns for unresolved generated proxies or runtime-computed channels, warnings for oversized generated artifacts, and deduplication of repeated generated-client call sites. Messaging fixtures should additionally assert Azure Service Bus queue/topic/processor metadata, NServiceBus endpoint/handler/saga/routing/recoverability metadata, RabbitMQ exchange/routing-key metadata, MSMQ queue path metadata, generic abstraction metadata, `HANDLES` relationships for consumer roles, `USES_CONFIG` relationships for configuration-backed broker targets, explicit unknowns for dynamic target names, and redaction of broker connection strings. Storage, SMTP/email, and payment fixtures should additionally assert storage read/write/delete hints, Azure Blob container and Azure File share/path metadata, generic storage abstraction metadata, SMTP host and authentication-hint metadata, email sender abstraction metadata, payment provider and endpoint-key metadata, `USES_CONFIG` relationships for configuration-backed storage/email/payment targets, explicit unknowns for dynamic targets, deduplication of repeated call-site facts, and aggressive redaction of connection strings, SMTP credentials, payment API keys, tokens, card data, and customer payment identifiers. Internal service fixtures should additionally assert positive route-to-endpoint correlation, false-positive prevention when endpoint facts are missing or ambiguous, unknown ownership reasons, endpoint/controller/method/project stable-key metadata, internal/external classification metadata, duplicate call-site deduplication, C# client support, and feasible VB.NET parity limits. Contributor-facing details for the current integration graph model, HTTP/REST detector behavior, RPC/generated-client detector behavior, messaging detector behavior, storage/email/payment detector behavior, internal service correlation behavior, safety boundary, provider seam, stable keys, and fixture expectations are described in [external integration extraction](external-integration-extraction.md).

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

## WP012 rule catalog, evaluator, persistence, extraction integration, finding, and hotlist validation

The current WP012 foundation validates the copied-output JSON rule catalog, persists validated catalog entries as versioned Neo4j rule records, integrates rule loading and evaluation into the extraction pipeline, evaluates enabled rules against application-layer graph facts, constructs deterministic finding records from matched rule context, persists finding history and suppression seams, and exposes controlled rule catalog, hotlist, finding detail, finding history, and suppression API behavior without starting the Aspire AppHost. The targeted tests exercise the application-layer loader, evaluator, extraction integration service, finding construction service, in-memory finding store, query service, query API endpoints, Neo4j rule catalog store, Neo4j finding store Cypher, Neo4j hotlist query Cypher, and API stage composition because the current slices are about authored rule content, runtime folder resolution, deterministic diagnostics, catalog availability, boolean DSL semantics, condition/operator behavior, code/version upsert identity, non-destructive historical catalog behavior, data-only predicate execution, finding stable keys, finding history keys, confidence derivation, evidence/node links, suppression overlays, controlled filters, pagination, deterministic ordering, and redaction safeguards.

Run the focused catalog tests after changing rule files, rule catalog contracts, validation rules, or copied-output project configuration:

```powershell
dotnet test .\test\Archon.Application.Tests\Archon.Application.Tests.csproj --filter FullyQualifiedName~RuleCatalogLoaderTests
```

Run the focused evaluator tests after changing boolean group evaluation, condition-kind mapping, operator behavior, unknown handling, warning behavior, evidence references, or evaluator result ordering:

```powershell
dotnet test .\test\Archon.Application.Tests\Archon.Application.Tests.csproj --filter FullyQualifiedName~RuleEvaluatorTests
```

Run the focused built-in catalog tests after changing `rules/**/*.json`, first-cut rule-family coverage metadata, representative fixture facts, or security-sensitive redaction expectations:

```powershell
dotnet test .\test\Archon.Application.Tests\Archon.Application.Tests.csproj --filter FullyQualifiedName~BuiltInRuleCatalogTests
```

Run the extraction integration tests after changing copied-output rule initialization, accumulated snapshot projection, catalog upsert sequencing, enabled-rule selection during extraction, evaluation diagnostics, or cancellation flow:

```powershell
dotnet test .\test\Archon.Application.Tests\Archon.Application.Tests.csproj --filter FullyQualifiedName~RuleExtractionIntegrationServiceTests
dotnet test .\test\Archon.Api.Extraction.Tests\Archon.Api.Extraction.Tests.csproj --filter "FullyQualifiedName~AddArchonExtractionApi|FullyQualifiedName~RuleEvaluation"
```

Run the integrated end-to-end WP012 application path when changes span rule loading, catalog persistence, evaluator output, finding construction, finding persistence, hotlist queries, finding detail/history, suppression, unknown-state handling, or redaction. The focused command can be run by itself while iterating on the integrated path:

```powershell
dotnet test .\test\Archon.Application.Tests\Archon.Application.Tests.csproj --filter FullyQualifiedName~WP012EndToEndPath_WhenRepresentativeFactsExist_ShouldPersistQueryAndSuppressFindingsSafely
```

That test deliberately composes application-layer seams and does not start the Aspire AppHost. It validates the same sequence a production WP012 flow depends on: load copied-output rule JSON, upsert rule catalog entries, evaluate enabled rules over established graph facts, create deterministic findings, persist findings, query hotlist output, read finding detail and history, apply suppression, and verify secret-like values remain redacted from public DTOs and extraction diagnostics.

Run the Neo4j rule catalog tests after changing rule catalog persistence, mapper fields, Cypher, schema assumptions, idempotency behavior, disabled-rule persistence, version coexistence, or removed-on-disk non-deletion behavior:

```powershell
dotnet test .\test\Archon.Infrastructure.Neo4j.Tests\Archon.Infrastructure.Neo4j.Tests.csproj --filter RuleCatalog
```

Run the finding construction and persistence tests after changing finding stable keys, fingerprints, history, confidence, unknown preservation, affected-node links, evidence links, suppression validation, suppression carry-forward, Neo4j finding Cypher, or finding-store dependency injection:

```powershell
dotnet test .\test\Archon.Application.Tests\Archon.Application.Tests.csproj --filter FullyQualifiedName~FindingConstructionServiceTests
dotnet test .\test\Archon.Infrastructure.Neo4j.Tests\Archon.Infrastructure.Neo4j.Tests.csproj --filter "FullyQualifiedName~Neo4jRuleCatalogStoreTests|FullyQualifiedName~Neo4jServiceCollectionExtensionsTests"
```

Run the query API tests after changing rule catalog query DTOs, hotlist filters, WP013 metric/cycle/hotspot/architecture-rule/snapshot-diff filters, paging, deterministic ordering, finding detail, finding history, route/query-parameter stable-key behavior, suppression endpoint validation, response-size limits, metadata redaction, logging diagnostics, validation-problem shaping, or endpoint metadata:

```powershell
dotnet test .\test\Archon.Api.Query.Tests\Archon.Api.Query.Tests.csproj --filter "FullyQualifiedName~QueryEndpointTests|FullyQualifiedName~ArchonApiQueryProjectReferenceTests"
dotnet test .\test\Archon.Infrastructure.Neo4j.Tests\Archon.Infrastructure.Neo4j.Tests.csproj --filter "FullyQualifiedName~Neo4jRuleCatalogStoreTests|FullyQualifiedName~Neo4jServiceCollectionExtensionsTests"
```

For WP013 query hardening changes and final WP013 readiness checks, also run the focused application and infrastructure projects that own metric, cycle, hotspot, architecture-rule, diff, metadata-safety, and persistence-adapter behavior:

```powershell
dotnet test .\test\Archon.Api.Query.Tests\Archon.Api.Query.Tests.csproj
dotnet test .\test\Archon.Application.Tests\Archon.Application.Tests.csproj
dotnet test .\test\Archon.Infrastructure.Neo4j.Tests\Archon.Infrastructure.Neo4j.Tests.csproj
```

These targeted projects validate that snapshot metric, graph metric, modernization metric, dependency cycle, hotspot, architecture-rule result, and snapshot diff endpoints keep consistent stable identities, confidence fields, unknown-state fields, evidence references, sanitized metadata, deterministic ordering, bounded paging, validation-problem responses, and no-arbitrary-query boundaries. They also cover the final WP013 readiness path because the application tests own metric calculation, cycle detection, hotspot detection, architecture-rule evaluation, and diff comparison; the API query tests own HTTP contract consistency and validation shaping; and the Neo4j infrastructure tests own snapshot-owned metric persistence and adapter composition. They do not start the Aspire AppHost and they do not create an Archon Discovery UI, dashboard, graph explorer, prompt panel, or any other human-facing product surface.

Then run the solution build gate:

```powershell
dotnet build .\Archon.slnx --no-restore
```

These commands prove that `rules/**/*.json` content is copied into runtime output, that the default loader reads `AppContext.BaseDirectory/rules`, and that invalid catalog input produces deterministic diagnostics for missing folders, invalid JSON, missing fields, invalid category/severity/status values, invalid versions, empty detection groups, unsupported condition kinds, unsupported operators, incompatible operators, duplicate rule identities, and disabled-rule availability. They also prove that invalid required built-in content can fail visibly through the fail-fast validation helper. The evaluator tests prove that only enabled rules are selected, candidate nodes are restricted by `nodeKinds`, `all`, `any`, and `none` compose direct conditions and nested groups recursively, all required condition kinds map to graph facts, all required operators use deterministic comparison behavior, partial evaluation warnings and unknown context are preserved, and executable-looking rule values are treated as literal data. The built-in catalog tests validate every copied-output rule file, assert executable traceability for the first-cut lifecycle, legacy technology, data-access, obsolete API, security-sensitive, configuration, dependency-risk, modernization-blocker, and architecture-smell families, prove representative fixture matches, and verify that security-sensitive rules expose location evidence without storing secret values. The extraction and persistence tests prove that the WP012 stage runs after prior extraction stages, loads copied-output rules, upserts catalog records before evaluation, uses rule code plus version as the Neo4j identity, preserves new versions beside old versions, keeps disabled rules as catalog history, does not delete rules omitted from later disk loads, and surfaces rule diagnostics without putting evaluator logic in host composition. The end-to-end application test proves those pieces can be composed through the current application seams into persisted findings and controlled hotlist/detail/history/suppression output while preserving deterministic ordering, unknown-state context, and redaction. The finding tests prove that satisfied matches become deterministic findings, that equivalent findings can be tracked across snapshots, that suppression does not delete findings, and that Neo4j finding writes use stable logical identities rather than database-local IDs. The query tests prove that catalog list/detail, hotlist filters, paging, deterministic ordering, finding detail, history, suppression validation, route-safe stable-key lookup, and metadata redaction work through an in-memory ASP.NET Core test host and static Neo4j Cypher assertions. Contributor-facing behavior and authoring concepts are described in [rule catalog and rule engine](rule-catalog-and-rule-engine.md), product query behavior is described in [hotlist and findings](hotlist-and-findings.md), and persisted rule/finding identity is described in [Neo4j persistence foundation](neo4j-persistence-foundation.md).

## WP011 .NET UI and client extraction validation

The current WP011 validation path covers the shared UI helper layer, Blazor `.razor` extraction, Razor Pages and MVC Razor `.cshtml` extraction, Windows Forms designer/source extraction, WPF XAML extraction, WinUI XAML and package-manifest extraction, .NET MAUI XAML/Shell/platform-head extraction, Avalonia AXAML/view-locator/ReactiveUI extraction, and the unified API-triggered `wp011-ui-client` stage that runs all framework adapters through one orchestration path. The framework extractor tests create temporary repository roots with minimal project files and source-controlled UI artifacts, then execute the corresponding extractor project directly. The unified API tests create a mixed UI fixture so one run can assert cross-framework snapshot output, stable-key deduplication, redaction, warnings, unknowns, and the absence of product UI artifacts. Those tests validate supported static patterns and degraded dynamic patterns without compiling the target UI project, loading designers, loading XAML or AXAML, starting platform runtimes, launching browsers, opening database connections, or contacting live APIs.

The WP011 tests assert `UiApplication`, `UiComponent`, `UiPage`, `UiView`, `UiRoute`, `UiLayout`, `UiControl`, `UiResource`, `ViewModel`, `Command`, `Binding`, `Method`, `Controller`, `Type`, `Project`, `ExternalService`, and `ConfigurationKey` facts; `DECLARES_COMPONENT`, `DECLARES_UI_ROUTE`, `USES_LAYOUT`, `USES_COMPONENT`, `USES_CONTROL`, `USES_UI_RESOURCE`, `USES_VIEW_MODEL`, `NAVIGATES_TO`, `HANDLES_UI_EVENT`, `USES_COMMAND`, `BINDS_TO`, `CALLS_API`, `USES_CONFIG`, and `DEPENDS_ON` relationships; deterministic repository-relative evidence paths; line spans; snippet hashes; redacted previews; metadata values; confidence; warnings; deduplication; and explicit unknown state.

Use these focused commands when changing WP011 shared UI helpers, framework-specific UI extraction, or API orchestration integration:

```powershell
dotnet test .\test\Archon.Extractors.Ui.Tests\Archon.Extractors.Ui.Tests.csproj
dotnet test .\test\Archon.Extractors.Blazor.Tests\Archon.Extractors.Blazor.Tests.csproj --filter Blazor
dotnet test .\test\Archon.Extractors.Razor.Tests\Archon.Extractors.Razor.Tests.csproj
dotnet test .\test\Archon.Extractors.WinForms.Tests\Archon.Extractors.WinForms.Tests.csproj
dotnet test .\test\Archon.Extractors.Wpf.Tests\Archon.Extractors.Wpf.Tests.csproj --filter Wpf
dotnet test .\test\Archon.Extractors.WinUI.Tests\Archon.Extractors.WinUI.Tests.csproj --filter WinUI
dotnet test .\test\Archon.Extractors.Maui.Tests\Archon.Extractors.Maui.Tests.csproj --filter Maui
dotnet test .\test\Archon.Extractors.Avalonia.Tests\Archon.Extractors.Avalonia.Tests.csproj --filter Avalonia
dotnet test .\test\Archon.Api.Extraction.Tests\Archon.Api.Extraction.Tests.csproj --filter Ui
dotnet test .\test\Archon.Api.Extraction.Tests\Archon.Api.Extraction.Tests.csproj --filter Wp011
dotnet build .\Archon.slnx --no-restore
```

These commands do not start the Aspire AppHost, do not render Razor components or views, do not launch Playwright or a browser, do not call live APIs, do not instantiate target Blazor, ASP.NET Core, Windows Forms, WPF, WinUI, MAUI, or Avalonia applications, do not execute Razor Page handlers or MVC actions, do not evaluate tag helpers, do not load Windows Forms designers, do not load XAML or AXAML, do not run MAUI platform heads, do not start Avalonia desktop lifetimes, do not instantiate controls, do not open database connections, do not require Neo4j credentials, and do not invoke MCP tools. When package or project references have changed, run `dotnet restore .\Archon.slnx` first; otherwise use the focused test commands and the no-restore build gate so failures point to compile, extraction, or graph-contract behavior rather than package acquisition. Contributor-facing details for supported Blazor, Razor Pages, MVC Razor, Windows Forms, WPF, WinUI, MAUI, and Avalonia facts, stable keys, evidence, confidence, unknown state, redaction, current exclusions, and extension guidance live in [.NET UI and client extraction](dotnet-ui-client-extraction.md).

## WP015 MCP server validation

The current WP015 validation path covers the read-only MCP host product surface: runtime catalog readiness, common response envelopes, security and audit seams, bounded limits, tools, resources, prompts, forbidden-capability rejection, prompt-injection handling, redaction, cancellation, and representative host-level verification endpoints. MCP validation is intentionally test-led. It does not require a running Aspire AppHost, does not require a live MCP client, does not open Neo4j directly from MCP handlers, does not execute shell commands, does not execute SQL or Cypher, does not mutate files, and does not modify source repositories.

Use this focused command when changing MCP tools, resources, prompts, security behavior, response envelopes, catalog registration, verification endpoints, or MCP documentation examples that should be checked against current behavior:

```powershell
dotnet test .\test\ArchonMcp.Tests\ArchonMcp.Tests.csproj
```

Use the MCP host build gate after the focused tests pass:

```powershell
dotnet build .\src\ArchonMcp\ArchonMcp.csproj
```

The MCP test project validates the host without treating manual startup as an automated gate. Runtime baseline tests cover mandatory catalog registration, conservative limit defaults, forbidden names, readiness, and probe behavior. Search, project, dependency, path, symbol, data-access, impact, rule, hotlist, snapshot-diff, resource, parameterized-resource, and prompt tests verify request validation, authorization ordering, query abstraction delegation, stable-key output, evidence references, unknowns, warnings, truncation, safe follow-ups, and structured error categories. Security and integration tests verify that unsupported command, SQL, Cypher, graph-query, file/source mutation, rule mutation, finding mutation, and snapshot mutation paths fail closed; that prompt-injection content remains untrusted evidence; that representative secrets are redacted; that cancellation reaches query-backed handlers; and that host-level verification calls preserve common envelope shape. Contributor-facing setup, security, and troubleshooting guidance lives in [runtime foundation](runtime-foundation.md), while exact MCP tool, resource, and prompt contracts live in [MCP tool reference](mcp-tool-reference.md).

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
