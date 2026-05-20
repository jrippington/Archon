# WP001 Implementation Notes

## Contributor Overview

WP001 establishes the Archon foundation. The goal is not to deliver extraction, query, persistence, MCP tool, markdown export, findings, or user-interface behavior. The goal is to create a complete, buildable, and verifiable foundation that later numbered work packages can extend without reshaping the solution. This document is therefore both an implementation record and a contributor guide: it explains what exists now, why it exists, how to validate it, and which capabilities are intentionally assigned to later work.

Archon is organized as a .NET 10 solution using Onion Architecture. **Onion Architecture** is a dependency model where stable core concepts sit at the center and replaceable delivery or infrastructure details sit at the outside. Dependencies should point inward. In WP001, `Archon.Domain` is the center, `Archon.Application` is the use-case and orchestration layer, API modules and extractors are future feature slices, infrastructure projects are outer adapters, and host projects compose or deliver runtime behavior.

The root solution file is `Archon.slnx`. Production projects live under `src`, test projects live under `test`, and each production project has a corresponding test project. Project identity is treated as a repository-root-relative `.csproj` path, such as `src/Archon.Domain/Archon.Domain.csproj`, rather than an absolute machine path. That normalized identity matters because Archon will later analyze architecture evidence across developer workstations and CI agents where clone locations differ.

## Foundation Structure and Project Families

The `src/Archon` project is the Aspire AppHost. An **AppHost** is an Aspire orchestration project that describes which local processes, containers, and resources run together. Archon's AppHost is also the **composition root**, which means it wires the runtime graph together but does not implement business behavior. It composes Neo4j, `ArchonApi`, and `ArchonMcp` for local development and deliberately excludes a Discovery UI resource.

`src/Archon.ServiceDefaults` contains shared host runtime configuration. **Service defaults** are common configuration applied to multiple hosts so each executable project does not duplicate health checks, telemetry, service discovery, and HTTP client resilience setup. `ArchonApi` and `ArchonMcp` both consume this project.

`src/ArchonApi` is the API delivery host. In WP001 it maps only operational probes. It does not expose extraction, query, management, Swagger, Scalar, or UI endpoints. `src/ArchonMcp` is the future MCP delivery host. In WP001 it also maps only operational probes and does not expose MCP tools, MCP resources, MCP prompts, or architecture-query behavior.

The core projects are `src/Archon.Domain` and `src/Archon.Application`. `Archon.Domain` is intentionally minimal and must not depend on outer layers. `Archon.Application` may depend on the domain layer and will later hold use-case contracts and orchestration seams. The API module projects (`Archon.Api.Extraction`, `Archon.Api.Query`, and `Archon.Api.Management`) exist so later API behavior has clear homes without placing feature logic directly in the API host.

The Roslyn projects (`Archon.Roslyn`, `Archon.Roslyn.CSharp`, `Archon.Roslyn.VisualBasic`, and `Archon.Roslyn.Legacy`) establish the future analysis abstraction and implementation slices. The extractor projects establish future source-evidence extraction areas, including project structure, ASP.NET, UI frameworks, dependency injection, configuration, data access, legacy web, and hotlist analysis. The infrastructure projects (`Archon.Infrastructure.Roslyn`, `Archon.Infrastructure.Neo4j`, and `Archon.Infrastructure.Markdown`) establish outer adapter locations for Roslyn workspace loading, graph persistence, and markdown export.

All skeleton projects are created in WP001 even though most behavior arrives later. This is intentional. The complete project map makes responsibility boundaries visible before feature code exists, lets architecture-boundary tests lock those boundaries early, and prevents later work packages from needing to move large amounts of code simply to create the right project homes.

`ArchonUi` and `ArchonUi.Tests` are absent by design. WP001 explicitly excludes Discovery UI implementation. No dashboard, explorer, graph view, evidence viewer, hotlist viewer, prompt panel, UI component, static asset, or UI test is part of this work package.

## Automated Build and Test Verification

Use PowerShell from the repository root (`D:\Dev\Archon` in the current workspace) unless otherwise noted. The foundation targets .NET 10, and the environment used for this implementation had .NET SDK `10.0.300` active. Restore should be run before build when package or project references change:

```powershell
dotnet restore .\Archon.slnx
```

A successful restore completes without NuGet errors. For WP001, restore also proves that the Aspire AppHost SDK `13.3.3`, service-default packages, and test packages can be resolved in the current environment.

Build the full solution with:

```powershell
dotnet build .\Archon.slnx --no-restore
```

A successful build reports `Build succeeded` and compiles all production and test projects. Use `--no-restore` after a successful restore so build failures point to compile or project-graph issues rather than repeating package resolution.

Targeted tests are organized around the WP001 slices. The service-default tests verify shared runtime registrations and the `/health` and `/alive` endpoint mapping:

```powershell
dotnet test .\test\Archon.ServiceDefaults.Tests\Archon.ServiceDefaults.Tests.csproj --no-build
```

The API host tests verify that `ArchonApi` exposes only probe endpoints and keeps extraction, query, management, Swagger, Scalar, and UI paths absent:

```powershell
dotnet test .\test\ArchonApi.Tests\ArchonApi.Tests.csproj --no-build
```

The MCP host tests verify that `ArchonMcp` exposes only probe endpoints and keeps MCP tools, resources, prompts, and architecture-query paths absent:

```powershell
dotnet test .\test\ArchonMcp.Tests\ArchonMcp.Tests.csproj --no-build
```

The AppHost composition tests are safe metadata checks. They inspect project files and source text to verify Neo4j, `ArchonApi`, `ArchonMcp`, health checks, and UI absence without starting the AppHost or requiring Docker:

```powershell
dotnet test .\test\Archon.Tests\Archon.Tests.csproj --no-build --filter FullyQualifiedName~AppHostComposition
```

The boundary and identity tests verify repository-root-relative project identities and Onion Architecture dependency direction:

```powershell
dotnet test .\test\Archon.Tests\Archon.Tests.csproj --no-build --filter FullyQualifiedName~Boundary
dotnet test .\test\Archon.Tests\Archon.Tests.csproj --no-build --filter FullyQualifiedName~ProjectIdentity
```

Expected success signals are straightforward: each targeted test command should report zero failed tests, and the solution build should report `Build succeeded`. The tests intentionally avoid starting the Aspire AppHost as a blocking process. For this work package, do not replace these targeted checks with a full test-suite run unless a later instruction explicitly requires it.

The repository documentation-pass rules in `./.github/instructions/documentation-pass.instructions.md` are mandatory for source-code work performed in WP001. In practical terms, public and internal types, constructors, methods, and meaningful parameters introduced by this work package must carry local developer documentation. Test helpers and test methods are included in that requirement because they are hand-maintained code that future contributors will rely on when diagnosing boundary failures.

## Manual AppHost Verification Walkthrough

Manual AppHost verification is the only WP001 validation path that should start the Aspire distributed application. It is manual because the AppHost is a long-running orchestration process: it starts hosts, starts or connects to container resources, opens the Aspire dashboard, and waits until the developer stops it. Automated agents and build scripts must not run it as a blocking validation command.

Before running the AppHost, ensure a local OCI-compatible container runtime is available. Docker Desktop is the usual local choice. This prerequisite exists because WP001 composes Neo4j as a container resource named `neo4j`. Neo4j is the graph database runtime that later work packages will use as the architecture-fact store. WP001 does not create graph schema, persist facts, or query the graph; it only proves that the local runtime seam exists.

Start the AppHost manually from the repository root:

```powershell
dotnet run --project .\src\Archon\Archon.csproj
```

When the Aspire dashboard opens, confirm the resource list contains exactly the WP001 runtime resources expected for the foundation: `neo4j`, `ArchonApi`, and `ArchonMcp`. The dashboard should not contain `ArchonUi`, Discovery UI, or any other UI resource. The API and MCP resources should expose `/health` checks through the dashboard after the hosts are ready. A healthy API or MCP probe means the host process is running and the Work Item 2 service-default endpoints are mapped.

If you manually browse a host endpoint, use the local URL assigned by ASP.NET Core or shown in the Aspire dashboard and request `/health` or `/alive`. `/health` is the readiness probe: it answers whether the service is ready to accept work. `/alive` is the liveness probe: it answers whether the process is responsive. In WP001 both should return successful health-check responses for the API and MCP hosts.

Stop the AppHost manually after verification, usually with `Ctrl+C` in the terminal that started it or by stopping the debug session in Visual Studio. Do not leave the AppHost running after the check, because it may keep containers and development ports allocated.

## Later Capability Assignment

The absence of feature behavior in WP001 is not a deferral in the sense of optional or unspecified work. It is a deliberate sequencing decision. WP001 creates the foundation so later numbered work packages have stable project homes, runtime seams, and boundary tests before feature implementation begins.

Extraction behavior belongs to later extraction work packages. That includes repository-root and solution-list intake, Roslyn workspace loading, project inventory extraction, package reference extraction, semantic source analysis, ASP.NET and UI extraction, dependency-injection extraction, configuration extraction, data-access extraction, legacy web extraction, and hotlist rule evaluation. The extractor projects already exist so those capabilities can be implemented in the correct slices.

Query behavior belongs to later API and application work packages. `Archon.Api.Query` exists as the future API module location, but `ArchonApi` does not expose query endpoints in WP001. Management behavior is similarly assigned to later work in `Archon.Api.Management`, and extraction submission behavior is assigned to later work in `Archon.Api.Extraction`.

Graph persistence belongs to later infrastructure work. `Archon.Infrastructure.Neo4j` exists, and the AppHost composes Neo4j, but WP001 does not create constraints, indexes, schema, Cypher queries, persistence services, migrations, or graph data. Markdown export belongs to later work in `Archon.Infrastructure.Markdown` and is not implemented in WP001.

MCP behavior belongs to later MCP-focused work packages. `ArchonMcp` is runnable and health-probed, but it does not expose tools, resources, prompts, architecture lookups, Copilot workflows, or evidence-backed responses in WP001.

Discovery UI capability belongs to later UI work after the API-first and MCP-first foundation is in place. No `ArchonUi` project, UI page, component, asset, dashboard, explorer, graph view, evidence viewer, hotlist viewer, or prompt panel is created in WP001. This is an explicit boundary, not an omission.

## Work Item 1 - Foundation Solution Skeleton

Work Item 1 created the initial buildable Archon solution skeleton. The repository now contains `Archon.slnx` at the root, all WP001 production projects under `src`, and all corresponding test projects under `test`. The solution intentionally does not include `ArchonUi` or `ArchonUi.Tests` because the WP001 specification excludes Discovery UI implementation.

The installed SDK baseline was inspected before project creation. The environment has .NET SDK `8.0.100` and `.NET SDK 10.0.300`, and the active `dotnet --version` is `10.0.300`. The root solution file already existed as a folder-only `.slnx`, so Work Item 1 aligned that file instead of replacing it with a legacy `.sln` file. The `Archon` AppHost project uses `Aspire.AppHost.Sdk/13.3.3`, matching the WP001 requirement.

All production projects target `net10.0`, enable nullable reference types, enable implicit usings, and treat warnings as errors. Executable skeleton projects (`Archon`, `ArchonApi`, and `ArchonMcp`) use explicit `Program` classes with `Main` methods and do not use top-level statements. Library projects contain documented marker types only; those markers exist to give tests and future composition code a stable assembly identity without implementing behavior assigned to later work packages.

Every test project targets `net10.0`, uses xUnit, references its corresponding production project, and includes a documented smoke test that verifies the referenced production assembly can be loaded. Package references and project references are kept in separate `ItemGroup` blocks.

## Validation

Validation performed for Work Item 1:

1. `dotnet restore .\Archon.slnx` succeeded.
2. `dotnet build .\Archon.slnx --no-restore` succeeded.
3. `dotnet test .\Archon.slnx --no-build --filter FullyQualifiedName~ProjectReferenceTests` succeeded with 34 tests passed, 0 failed, and 0 skipped.
4. A project-presence check confirmed all 34 expected production projects and all 34 expected test projects exist.
5. A UI-exclusion check confirmed neither `src/ArchonUi` nor `test/ArchonUi.Tests` exists.

The Visual Studio Test Explorer runner was also attempted for a small project subset. It reported zero tests and aborted because of stale build-failure state even though the command-line solution build and targeted command-line test run succeeded. The CLI validation above is the authoritative validation result for this work item.

## Wiki Review Result

The mandatory wiki review was performed according to `./.github/instructions/wiki.instructions.md`. The repository currently has no `wiki` directory, so there were no existing wiki pages to update, split, rename, or retire. Because Work Item 1 creates foundational developer-facing structure, this result is recorded here explicitly: no wiki page update could be made in-place because the wiki content is not present in the workspace. The current-state developer guidance for this work item is captured in this implementation-notes document and in the updated WP001 plan record.

## Manual Verification Notes

Work Item 1 does not start the Aspire AppHost and does not implement AppHost composition behavior. Later WP001 work items will add shared service defaults, host health/readiness behavior, and manual AppHost verification instructions. Automated validation must continue to avoid running the AppHost as a blocking process.

## Work Item 2 - Shared Service Defaults and Minimal Host Health Slice

Work Item 2 replaced the host skeleton behavior with the first independently runnable runtime slice. `Archon.ServiceDefaults` now follows the Aspire service-defaults pattern: it registers health checks, OpenTelemetry-compatible logging, metrics, and tracing, service discovery, and resilient `HttpClient` defaults. The OTLP exporter is only enabled when `OTEL_EXPORTER_OTLP_ENDPOINT` is configured, which keeps local tests and standalone host runs independent from an external collector.

The term **service defaults** refers to the shared host-level configuration that every runtime process receives. In Archon, this keeps cross-cutting runtime behavior out of individual feature hosts so the API and MCP processes do not drift apart as later work packages add endpoints and MCP capabilities. The term **readiness** means a process is prepared to receive work; the term **liveness** means the process itself is responsive. Work Item 2 maps readiness to `/health` and liveness to `/alive` for both `ArchonApi` and `ArchonMcp`.

`ArchonApi` and `ArchonMcp` now expose explicit documented `Program` bootstraps with `BuildApplication` seams so tests can create in-memory hosts without launching the Aspire AppHost or binding real ports. Both hosts consume `Archon.ServiceDefaults`, log startup through `ILogger`, and map only `/health` and `/alive`. `ArchonApi` intentionally does not expose extraction, query, management, Swagger, Scalar, or UI endpoints. `ArchonMcp` intentionally does not expose MCP tools, resources, prompts, or architecture-query endpoints.

Validation performed for Work Item 2:

1. `dotnet restore .\Archon.slnx` succeeded.
2. `dotnet build .\Archon.slnx --no-restore` succeeded.
3. `dotnet test .\test\Archon.ServiceDefaults.Tests\Archon.ServiceDefaults.Tests.csproj --no-build` succeeded.
4. `dotnet test .\test\ArchonApi.Tests\ArchonApi.Tests.csproj --no-build` succeeded.
5. `dotnet test .\test\ArchonMcp.Tests\ArchonMcp.Tests.csproj --no-build` succeeded.

The Work Item 2 tests validate service-default registrations, in-memory `/health` and `/alive` responses, and representative excluded paths for API and MCP capabilities. No automated test starts the Aspire AppHost as a blocking process.

Wiki review result for Work Item 2: updated `wiki/home.md` because this work item changes developer-facing runtime behavior, health endpoint terminology, and setup guidance. The page now explains service defaults, readiness, liveness, the current host-only runtime slice, manual host commands, and the intentionally absent API/MCP feature endpoints using current-state narrative guidance.

Manual host verification for Work Item 2 can be performed with these commands:

```powershell
dotnet run --project .\src\ArchonApi\ArchonApi.csproj
dotnet run --project .\src\ArchonMcp\ArchonMcp.csproj
```

After a host starts, open the assigned local ASP.NET Core URL and request `/health` and `/alive`. Both endpoints should return successful health-check responses. Stop manually run host processes after verification.

## Work Item 3 - Aspire Composition Slice

Work Item 3 implemented the first distributed application composition path. The `Archon` project remains the Aspire AppHost and uses `Aspire.AppHost.Sdk/13.3.3`. In Aspire terminology, an **AppHost** is the local-development orchestration project that declares which processes, containers, and other resources should run together. Archon's AppHost is also the **composition root**, meaning it wires external resources and host processes together but does not implement domain rules, extraction logic, graph persistence behavior, API endpoints, or MCP tools.

The AppHost now composes three resources for local development: a Neo4j container named `neo4j`, an `ArchonApi` project resource, and an `ArchonMcp` project resource. Neo4j is the graph database runtime that later work packages will use as the architecture-fact store. No dedicated `Aspire.Hosting.Neo4j` package was available from NuGet during implementation, so WP001 uses Aspire's generic container resource support with the official `neo4j:latest` image. Neo4j starts in secured mode by reading the `neo4j-username` and `neo4j-password` Aspire parameters from `src/Archon/appsettings.json` and using them to set `NEO4J_AUTH`; the password parameter is marked secret in the AppHost resource graph.

`ArchonApi` and `ArchonMcp` are composed as project resources and advertise their `/health` endpoints to Aspire using HTTP health checks. The API resource waits for Neo4j to be available. The MCP resource waits for both Neo4j and the API resource. The AppHost intentionally does not compose `ArchonUi` or any Discovery UI resource because WP001 excludes UI delivery.

Validation performed for Work Item 3:

1. `dotnet restore .\Archon.slnx` succeeded.
2. `dotnet build .\Archon.slnx --no-restore` succeeded after replacing unsupported container connection-string references with dependency-only `WaitFor` ordering.
3. `dotnet test .\test\Archon.Tests\Archon.Tests.csproj --no-build` succeeded with 4 tests passed, 0 failed, and 0 skipped.

The Work Item 3 tests are safe metadata checks. They inspect the AppHost project and source code to confirm the Aspire SDK version, API/MCP project references, Neo4j container declaration, API/MCP project resource declarations, `/health` checks, and absence of Discovery UI. They do not run `dotnet run --project .\src\Archon\Archon.csproj`, do not start Neo4j, and do not require a container runtime.

Wiki review result for Work Item 3: updated `wiki/home.md` because this work item changes developer-facing runtime composition, setup, Neo4j terminology, and manual verification guidance. The page now explains AppHost, composition root, Neo4j container composition, expected Aspire dashboard resources, and the requirement not to automate AppHost execution as a blocking validation step.

Manual AppHost verification for Work Item 3 is intentionally separate from automated validation:

```powershell
dotnet run --project .\src\Archon\Archon.csproj
```

Before running the command, ensure a local OCI-compatible container runtime such as Docker Desktop is available because Neo4j is composed as a container. When the Aspire dashboard opens, confirm that `neo4j`, `ArchonApi`, and `ArchonMcp` appear as resources. Confirm that no `ArchonUi` or Discovery UI resource appears. The API and MCP resources should report their `/health` probes through the dashboard once the hosts are ready. Stop the AppHost manually after verification.

## Work Item 4 - Onion Boundary and Project Identity Verification Slice

Work Item 4 converted the WP001 architecture rules into executable tests. The tests live in `test/Archon.Tests` because they reason over the whole solution rather than a single production project. They do not start hosts, containers, or the Aspire AppHost. Instead, they inspect project files and normalize the project graph from the repository root.

Project identity is normalized as a repository-root-relative path such as `src/Archon.Domain/Archon.Domain.csproj`. This avoids absolute machine-specific paths like `D:\Dev\Archon\...` and keeps identity stable across developer workstations and CI agents. The test catalog discovers projects under `src` and `test`, parses each `.csproj` file, classifies the project into a WP001 layer, and reads normalized `ProjectReference` targets.

The Onion Architecture rule enforced here is that dependencies point inward. `Archon.Domain` is the center and has no outward project references. `Archon.Application` may reference domain but not infrastructure or hosts. API module and infrastructure projects must not reference hosts. Host projects remain delivery and composition endpoints rather than inward dependencies. The `Archon` AppHost is intentionally allowed to reference `ArchonApi` and `ArchonMcp` because Aspire uses those references to compose project resources; that is a host composition reference, not a domain or application dependency.

Validation performed for Work Item 4:

1. `dotnet test .\test\Archon.Tests\Archon.Tests.csproj --filter FullyQualifiedName~Boundary` succeeded with 7 tests passed, 0 failed, and 0 skipped.
2. `dotnet test .\test\Archon.Tests\Archon.Tests.csproj --filter FullyQualifiedName~ProjectIdentity` succeeded with 3 tests passed, 0 failed, and 0 skipped.
3. `dotnet build .\Archon.slnx --no-restore` succeeded.

Wiki review result for Work Item 4: updated `wiki/home.md` because this work item adds contributor-facing architecture boundary and project identity guidance. The page now explains Onion Architecture, inward dependency direction, normalized project identity, and the intentionally allowed AppHost-to-host composition references.

## Work Item 5 - Foundation Documentation and Manual Verification Slice

Work Item 5 expanded this document from an implementation log into the contributor-facing guide for the WP001 foundation. It now explains the purpose of each project family, why the complete skeleton exists before most feature behavior, how project identity is normalized, how the API and MCP hosts expose only operational probes, and why `ArchonUi` is intentionally absent.

The documentation also records the validation commands a contributor should use for the current foundation: solution restore, solution build, service-default tests, API host tests, MCP host tests, AppHost metadata checks, Onion boundary checks, and project identity checks. The AppHost walkthrough is deliberately manual. It tells contributors to run `dotnet run --project .\src\Archon\Archon.csproj` only when they are ready to operate a long-running Aspire orchestration process, verify the expected `neo4j`, `ArchonApi`, and `ArchonMcp` resources in the dashboard, confirm no `ArchonUi` resource exists, and then stop the process manually.

Validation performed for Work Item 5:

1. `dotnet restore .\Archon.slnx` succeeded.
2. `dotnet build .\Archon.slnx --no-restore` succeeded.
3. `dotnet test .\test\Archon.ServiceDefaults.Tests\Archon.ServiceDefaults.Tests.csproj --no-build` succeeded.
4. `dotnet test .\test\ArchonApi.Tests\ArchonApi.Tests.csproj --no-build` succeeded.
5. `dotnet test .\test\ArchonMcp.Tests\ArchonMcp.Tests.csproj --no-build` succeeded.
6. `dotnet test .\test\Archon.Tests\Archon.Tests.csproj --no-build --filter FullyQualifiedName~AppHostComposition` succeeded.
7. `dotnet test .\test\Archon.Tests\Archon.Tests.csproj --no-build --filter FullyQualifiedName~Boundary` succeeded.
8. `dotnet test .\test\Archon.Tests\Archon.Tests.csproj --no-build --filter FullyQualifiedName~ProjectIdentity` succeeded.

Wiki review result for Work Item 5: updated `wiki/home.md` because the documentation pass clarified contributor-facing setup, validation commands, manual AppHost verification, non-blocking automation guidance, and later-capability assignment. No wiki pages were retired or left stale.

## Work Item 6 - Targeted Validation and Work-Package Completion Record

Work Item 6 performed the final automated validation pass for WP001 and recorded the completion status for the foundation. The validation stayed within the documented automated path and did not start the Aspire AppHost as a blocking process. That distinction remains important because the AppHost is a long-running local orchestration process that may start containers, bind ports, and wait for manual shutdown.

Validation performed for Work Item 6:

1. `dotnet restore D:\Dev\Archon\Archon.slnx` succeeded.
2. `dotnet build D:\Dev\Archon\Archon.slnx` succeeded with `Build succeeded`.
3. `dotnet test D:\Dev\Archon\test\Archon.ServiceDefaults.Tests\Archon.ServiceDefaults.Tests.csproj` succeeded.
4. `dotnet test D:\Dev\Archon\test\ArchonApi.Tests\ArchonApi.Tests.csproj` succeeded.
5. `dotnet test D:\Dev\Archon\test\ArchonMcp.Tests\ArchonMcp.Tests.csproj` succeeded.
6. `dotnet test D:\Dev\Archon\test\Archon.Tests\Archon.Tests.csproj` succeeded; the final visible summary reported 14 tests passed, 0 failed, and 0 skipped.

The documentation completion check confirmed that this implementation record and `wiki/home.md` contain manual Aspire verification instructions, explicitly warn against automated blocking AppHost execution, document the `ArchonUi` exclusion, and assign extraction, query, graph persistence, markdown export, MCP behavior, findings, hotlist behavior, and Discovery UI work to later numbered work packages rather than treating them as optional future work.

Manual Aspire AppHost verification was not performed by the executor during Work Item 6 because the plan requires automated validation to avoid running the AppHost as a blocking process. A contributor can perform that manual check by ensuring a local OCI-compatible container runtime is available, running `dotnet run --project .\src\Archon\Archon.csproj`, confirming `neo4j`, `ArchonApi`, and `ArchonMcp` appear in the Aspire dashboard, confirming no `ArchonUi` or Discovery UI resource appears, checking `/health` or `/alive` for the API and MCP hosts if desired, and stopping the AppHost manually.

Wiki review result for Work Item 6: reviewed `wiki/home.md` and this implementation record against the final validation outcome. No additional wiki page update was required because Work Item 5 had already updated the current-state wiki guidance for restore/build/test commands, manual AppHost verification, non-blocking AppHost automation, `ArchonUi` exclusion, Onion Architecture, project identity, and later-capability assignment; Work Item 6 confirmed those statements still match the validated foundation.

## Work Item 7 - Mandatory Wiki Review and Update Gate

Work Item 7 completed the final wiki-maintenance gate for WP001 according to `./.github/instructions/wiki.instructions.md`. The review scope included the final WP001 implementation record, the authoritative WP001 plan, the wiki-maintenance instruction file, and every wiki page present in the workspace. The only wiki page currently present is `wiki/home.md`.

The review checked the developer-facing concepts changed or clarified by WP001: solution structure, service defaults, health and liveness probes, Aspire AppHost composition, Neo4j as the local graph runtime seam, manual AppHost verification, non-blocking automated validation, Onion Architecture boundaries, repository-root-relative project identity, explicit `ArchonUi` exclusion, and later numbered work-package assignment for capabilities outside WP001.

No additional wiki page update was required during Work Item 7. `wiki/home.md` already describes the current WP001 foundation in present-tense narrative form, defines the specialized terms a contributor needs to understand, includes the relevant restore/build/test and manual AppHost command sequences, explains why automated validation must not run the AppHost as a blocking process, and states that missing extraction, query, graph persistence, MCP, findings, hotlist, markdown export, and Discovery UI behavior is intentional for WP001. No wiki pages were created, split, renamed, retired, or intentionally left stale.

Validation performed for Work Item 7:

1. Reviewed `wiki/home.md` as the only wiki page present under `./wiki`.
2. Reviewed `docs/001-Solution-Foundation/implementation-notes-wp001.md` for an explicit final wiki-review record.
3. Reviewed `docs/001-Solution-Foundation/plan-wp001-solution-foundation.md` for Work Item 7 completion criteria.
4. Ran `dotnet build D:\Dev\Archon\Archon.slnx` successfully after the documentation-only updates.

Wiki review result for Work Item 7: no wiki page update was necessary. Reviewed `wiki/home.md`; existing guidance remained sufficient because it already reflects the final validated WP001 foundation, contributor commands, runtime composition, architecture boundaries, manual verification workflow, and current capability exclusions.

## Neo4j Parameter Security Update

This update secured the WP001 Neo4j container startup path. The AppHost now reads the `neo4j-username` and `neo4j-password` values through Aspire parameters and assembles the Neo4j `NEO4J_AUTH` environment value from those parameters. This replaces the previous local-development `NEO4J_AUTH=none` behavior so the Neo4j container starts with authentication enabled.

The username and password parameter values currently live in `src/Archon/appsettings.json` under the `Parameters` section. The password is declared as a secret parameter in `src/Archon/Program.cs`, which preserves the usual Aspire parameter flow and avoids spreading credential values through code. If a contributor opens the Neo4j browser during manual AppHost verification, they should sign in with the configured `neo4j-username` and `neo4j-password` values.

Validation performed for this update:

1. `dotnet test D:\Dev\Archon\test\Archon.Tests\Archon.Tests.csproj --filter FullyQualifiedName~AppHostCompositionMetadataTests` succeeded.
2. `dotnet build D:\Dev\Archon\Archon.slnx` succeeded.

Wiki review result for the Neo4j parameter security update: updated `wiki/home.md` because the change materially affects developer-facing runtime composition and manual verification. The wiki now explains that Neo4j starts secured through Aspire parameters and that manual Neo4j browser access uses the configured parameter values.

## Neo4j Browser Connectivity Update

This update corrected the local Neo4j Browser connection path. The Neo4j HTTP browser could open, but Browser's Bolt client reported routing discovery failures because the local container did not advertise a host-reachable Bolt address. A routing-style `neo4j://` URI can also trigger discovery behavior that is unnecessary for this single local container.

The AppHost now binds Neo4j's HTTP browser endpoint to `localhost:7474`, binds the Bolt endpoint to `localhost:7687`, and sets Neo4j advertised addresses for both HTTP and Bolt. During manual AppHost verification, contributors should open the Browser resource and connect to `bolt://localhost:7687` with the configured `neo4j-username` and `neo4j-password` parameter values from `src/Archon/appsettings.json`.

Validation performed for this update:

1. `dotnet test D:\Dev\Archon\test\Archon.Tests\Archon.Tests.csproj --filter FullyQualifiedName~AppHostCompositionMetadataTests` succeeded.
2. `dotnet build D:\Dev\Archon\Archon.slnx` succeeded.

Wiki review result for the Neo4j Browser connectivity update: updated `wiki/home.md` because the change affects developer-facing manual verification and troubleshooting. The wiki now documents the stable local Neo4j ports and instructs contributors to use the direct `bolt://localhost:7687` Browser connection URL.
