# WP001 Specification - Solution Foundation, Onion Boundaries, and Host Bootstrap

## Document Control

| Field | Value |
| --- | --- |
| Work Package | WP001 - Solution Foundation, Onion Boundaries, and Host Bootstrap |
| Output Path | `docs/001-Solution-Foundation/spec-wp001-solution-foundation.md` |
| Source Work Package | `docs/foundation/work-packages.md` WP001 |
| Source Brief | `docs/foundation/archon_full_concept_brief.md` |
| Specification Basis | `spec-template_v1.1.md` requested; template file was not present in the workspace at creation time, so this document follows the repository specification structure and documented work-package rules. |
| Required Aspire SDK | `13.3.3` |
| Status | Draft |
| Audience | Product owner, architect, implementation team, test engineer |

## 1. Overview

### 1.1 Purpose

This specification defines the requirements for WP001, the foundational implementation work package for Archon. WP001 establishes the executable solution skeleton, project boundaries, host bootstrap behavior, Aspire orchestration, Neo4j composition, service defaults, and test scaffolding required for all later Archon capabilities.

The package creates the complete planned production and test project layout up front, while deliberately limiting runtime behavior to health, readiness, composition, and architecture-boundary verification. Extraction, graph query, MCP tool behavior, markdown export, and user-facing Discovery UI features are not implemented in this work package.

### 1.2 Background

Archon is a .NET-first architecture intelligence platform for large modern and legacy .NET estates. Its controlling product principle is that architectural knowledge must come from deterministic extraction, must be evidence-backed, and must be persisted in Neo4j as the system of record. WP001 does not implement those extraction capabilities, but it must establish the solution structure and host seams that make them possible in later work packages.

The work package is governed by `docs/foundation/work-packages.md`, which states that every work package must be completed in order and that no work may be deferred outside the sequence. WP001 is therefore responsible for creating the full target project skeleton, including projects whose implementation arrives in later work packages.

### 1.3 High-Level Scope

WP001 covers the foundation of the Archon system:

- Aspire AppHost composition.
- Shared service defaults.
- API host bootstrap.
- MCP host bootstrap.
- Neo4j orchestration through Aspire.
- Domain, application, API module, Roslyn, extractor, infrastructure, host, and corresponding test project skeletons.
- Onion Architecture dependency direction.
- Health and readiness endpoints.
- Bootstrap and architecture-boundary tests.
- Documentation needed to understand and manually verify the foundation.

WP001 excludes Archon Discovery UI implementation, extraction behavior, graph persistence behavior beyond composition seams, API query functionality, MCP tools/resources/prompts, markdown export, and production rule evaluation.

## 2. System Context

### 2.1 Product Context

Archon will eventually accept architecture extraction requests through an API, analyze .NET repositories through Roslyn and specialized extractors, persist deterministic architecture facts in Neo4j, and expose evidence-backed architecture knowledge through API and MCP surfaces.

WP001 establishes the first runnable system slice. It must make it possible for a developer to build the solution, run the Aspire AppHost manually, and observe Neo4j, the API host, and the MCP host composed without any Discovery UI.

### 2.2 Source References

WP001 must align with these source materials:

- `docs/foundation/work-packages.md` WP001 objective, required implementation, and completion criteria.
- `docs/foundation/archon_full_concept_brief.md` section 1 for Archon's deterministic architecture-intelligence purpose.
- `docs/foundation/archon_full_concept_brief.md` section 4 for Architecture Operating System responsibilities.
- `docs/foundation/archon_full_concept_brief.md` section 5 for deterministic facts, evidence, unknowns, legacy-first, and .NET-first principles.
- `docs/foundation/archon_full_concept_brief.md` section 8.1 for Archon API Host, API modules, Neo4j graph, and MCP Server responsibilities.
- `docs/foundation/archon_full_concept_brief.md` section 9 and 9.1 for recommended solution structure and project responsibilities.
- `docs/foundation/archon_full_concept_brief.md` section 10 for Aspire hosting model.
- `docs/foundation/archon_full_concept_brief.md` section 35 phase 1 and section 36 Core Platform epic, excluding UI shell delivery.
- `docs/foundation/archon_full_concept_brief.md` Appendix E sections E.1.4 and E.5.6 for API-triggered extraction direction and repository-root/solution-list constraints.
- Microsoft Learn guidance for Aspire service defaults, health checks, telemetry, service discovery, and resilience.

### 2.3 Users and Stakeholders

| Stakeholder | Interest |
| --- | --- |
| Product owner | Confirms the foundation matches the Archon delivery sequence and excludes UI delivery. |
| Architect | Confirms project boundaries, dependency direction, and host responsibilities. |
| Developer | Uses the generated solution skeleton to implement later work packages. |
| Test engineer | Verifies build, bootstrap, health/readiness behavior, and architecture-boundary rules. |
| Future MCP consumer | Depends on a correctly separated MCP host that can later expose evidence-backed tools. |

## 3. Component Summary

### 3.1 Aspire AppHost

The Aspire AppHost is the orchestration root. It composes Neo4j, the API host, and the MCP host for local development. It must not contain domain logic, extraction logic, graph persistence implementation, API endpoint implementation, or MCP tool logic.

### 3.2 Service Defaults

The service defaults project provides shared host-level defaults for health checks, telemetry, resilience, service discovery, and common runtime configuration. API and MCP hosts use these defaults consistently.

### 3.3 API Host

The API host is a thin delivery host for HTTP surfaces. In WP001 it exposes only bootstrap health and readiness endpoints. It must be structured so later API extraction, query, and management modules can be composed without violating Onion Architecture.

### 3.4 MCP Host

The MCP host is the future AI-facing interface. In WP001 it exposes only health and readiness behavior and contains no MCP tools, resources, prompts, or architecture-query behavior.

### 3.5 Domain and Application Projects

The domain and application projects establish inward layers for later architecture model concepts, use cases, contracts, ports, and orchestration. In WP001 they may contain only minimal compile-safe skeletons needed to establish project identity and references.

### 3.6 API Module Projects

The API extraction, query, and management modules establish future application-facing API slices. WP001 creates them as compile-safe modules without implementing extraction, query, or management behavior beyond any minimal dependency-injection markers needed for host composition tests.

### 3.7 Roslyn and Extractor Projects

Roslyn abstraction projects and extractor slice projects are created during WP001 so later work packages add implementation without reshaping the solution. WP001 does not implement Roslyn loading, syntax analysis, semantic analysis, or extraction behavior.

### 3.8 Infrastructure Projects

Infrastructure projects establish outer adapters for Roslyn, Neo4j, and markdown. WP001 creates their project skeletons and references without implementing graph persistence, Roslyn workspace loading, or markdown export behavior.

### 3.9 Test Projects

Every planned production project has a corresponding test project under `./test`. Tests in WP001 verify the foundation: buildability, project presence, dependency direction, service defaults behavior, and host bootstrap behavior.

## 4. Functional Requirements

### 4.1 Solution and Project Structure

| ID | Requirement |
| --- | --- |
| FR-001 | The repository shall contain an `Archon.slnx` solution file used for WP001 implementation and later work packages. |
| FR-002 | All production projects shall be placed under `./src`. |
| FR-003 | All test projects shall be placed under `./test`. |
| FR-004 | Every planned production project required by the work-package sequence shall be created during WP001. |
| FR-005 | Every production project shall have a corresponding test project created during WP001. |
| FR-006 | Executable host projects shall use non-dotted names where specified by the source brief: `Archon`, `ArchonApi`, and `ArchonMcp`. |
| FR-007 | The Archon Discovery UI host shall not be implemented in WP001. |
| FR-008 | No UI pages, components, front-end assets, dashboard, explorer, graph view, evidence viewer, hotlist viewer, prompt panel, or other human-facing UI feature shall be created. |

### 4.2 Required Production Projects

WP001 shall create or align these production projects:

| Area | Projects |
| --- | --- |
| Host and composition | `Archon`, `Archon.ServiceDefaults`, `ArchonApi`, `ArchonMcp` |
| Core | `Archon.Domain`, `Archon.Application` |
| API modules | `Archon.Api.Extraction`, `Archon.Api.Query`, `Archon.Api.Management` |
| Roslyn | `Archon.Roslyn`, `Archon.Roslyn.CSharp`, `Archon.Roslyn.VisualBasic`, `Archon.Roslyn.Legacy` |
| Extractors | `Archon.Extractors.Projects`, `Archon.Extractors.AspNet`, `Archon.Extractors.Ui`, `Archon.Extractors.Blazor`, `Archon.Extractors.Razor`, `Archon.Extractors.WinForms`, `Archon.Extractors.Wpf`, `Archon.Extractors.WinUI`, `Archon.Extractors.Maui`, `Archon.Extractors.Avalonia`, `Archon.Extractors.DependencyInjection`, `Archon.Extractors.Configuration`, `Archon.Extractors.DataAccess`, `Archon.Extractors.LinqToSql`, `Archon.Extractors.EntityFramework`, `Archon.Extractors.AdoNet`, `Archon.Extractors.LegacyWeb`, `Archon.Extractors.Hotlist` |
| Infrastructure | `Archon.Infrastructure.Roslyn`, `Archon.Infrastructure.Neo4j`, `Archon.Infrastructure.Markdown` |

### 4.3 Required Test Projects

WP001 shall create corresponding test projects for every production project listed in section 4.2. Test projects shall use the production project name plus `.Tests`, for example `Archon.Domain.Tests` for `Archon.Domain` and `ArchonMcp.Tests` for `ArchonMcp`.

No `ArchonUi.Tests` project shall be created unless a non-UI placeholder is required solely to preserve a documented target-state mapping. If such a placeholder is created, it must contain no UI implementation and must be clearly documented as inactive until a future UI delivery decision.

### 4.4 Aspire Composition

| ID | Requirement |
| --- | --- |
| FR-009 | The `Archon` AppHost shall compose Neo4j, `ArchonApi`, and `ArchonMcp`. |
| FR-010 | The AppHost shall not compose an Archon Discovery UI resource. |
| FR-011 | The AppHost shall pass required service configuration through Aspire-supported service discovery or configuration patterns. |
| FR-012 | The AppHost shall configure health checks for composed project resources where applicable. |
| FR-013 | Automated tests shall not start the AppHost as a blocking process. |
| FR-014 | Documentation shall include manual verification instructions for starting the AppHost and confirming Neo4j, API, and MCP resources. |

### 4.5 Service Defaults

| ID | Requirement |
| --- | --- |
| FR-015 | `Archon.ServiceDefaults` shall provide shared host configuration extensions. |
| FR-016 | Service defaults shall configure health check endpoints for liveness and readiness. |
| FR-017 | Service defaults shall configure OpenTelemetry-compatible telemetry defaults. |
| FR-018 | Service defaults shall configure service discovery defaults suitable for Aspire-hosted services. |
| FR-019 | Service defaults shall configure HTTP client resilience defaults where applicable. |
| FR-020 | Service defaults shall be consumed by both `ArchonApi` and `ArchonMcp`. |

### 4.6 API Host Bootstrap

| ID | Requirement |
| --- | --- |
| FR-021 | `ArchonApi` shall expose a health endpoint. |
| FR-022 | `ArchonApi` shall expose a readiness endpoint. |
| FR-023 | `ArchonApi` shall not expose extraction endpoints in WP001. |
| FR-024 | `ArchonApi` shall not expose query endpoints in WP001. |
| FR-025 | `ArchonApi` shall not expose management endpoints in WP001 beyond health/readiness. |
| FR-026 | `ArchonApi` shall remain a thin host over module services and shall not contain domain, extraction, or persistence logic. |

### 4.7 MCP Host Bootstrap

| ID | Requirement |
| --- | --- |
| FR-027 | `ArchonMcp` shall expose a health endpoint or equivalent health probe surface appropriate to its hosting model. |
| FR-028 | `ArchonMcp` shall expose a readiness endpoint or equivalent readiness probe surface appropriate to its hosting model. |
| FR-029 | `ArchonMcp` shall not expose MCP tools in WP001. |
| FR-030 | `ArchonMcp` shall not expose MCP resources in WP001. |
| FR-031 | `ArchonMcp` shall not expose MCP prompts in WP001. |
| FR-032 | `ArchonMcp` shall be structured so later MCP behavior can consume application-layer query contracts without depending on infrastructure or host internals. |

### 4.8 Onion Architecture Boundaries

| ID | Requirement |
| --- | --- |
| FR-033 | Domain projects shall not reference application, API module, infrastructure, Roslyn implementation, extractor, or host projects. |
| FR-034 | Application projects shall not reference infrastructure or host projects. |
| FR-035 | API module projects shall not reference host projects. |
| FR-036 | Infrastructure projects shall not reference host projects. |
| FR-037 | Host projects may reference application/module services and infrastructure composition as required for delivery wiring. |
| FR-038 | Dependency-boundary tests shall verify the intended direction of project references. |

### 4.9 Later-Capability Assignment

| ID | Requirement |
| --- | --- |
| FR-039 | WP001 documentation shall identify extraction, query, MCP tools, markdown export, findings, and graph persistence implementation as assigned to later numbered work packages, not as unspecified future work. |
| FR-040 | No extraction, query, MCP tool, or UI capability shall be marked as deferred or optional. |
| FR-041 | Placeholder projects shall make later capability locations explicit without implementing those capabilities in WP001. |

## 5. Non-Functional Requirements

### 5.1 Buildability

| ID | Requirement |
| --- | --- |
| NFR-001 | The full solution shall build successfully after WP001. |
| NFR-002 | Project skeletons shall compile without unused experimental code or broken placeholder references. |
| NFR-003 | Package references shall be kept minimal and necessary for project type, test framework, Aspire, and host bootstrap behavior. |

### 5.2 Maintainability

| ID | Requirement |
| --- | --- |
| NFR-004 | C# code shall use block-scoped namespaces. |
| NFR-005 | C# code shall use Allman braces. |
| NFR-006 | C# files shall contain one public type per file. |
| NFR-007 | Private fields shall use underscore-prefixed naming. |
| NFR-008 | Executable entry points shall avoid top-level statements and use explicit program or host bootstrap classes. |

### 5.3 Observability

| ID | Requirement |
| --- | --- |
| NFR-009 | Host bootstrap shall include health/readiness probes. |
| NFR-010 | Service defaults shall establish telemetry configuration appropriate for Aspire and OpenTelemetry-compatible local development. |
| NFR-011 | Host bootstrap shall use `ILogger` abstractions rather than custom logging callbacks. |

### 5.4 Testability

| ID | Requirement |
| --- | --- |
| NFR-012 | Bootstrap behavior shall be testable without launching the Aspire AppHost process. |
| NFR-013 | Service default behavior shall be testable through extension methods or host-builder seams. |
| NFR-014 | Dependency-boundary rules shall be testable through project metadata or compiled reference inspection. |

### 5.5 Developer Experience

| ID | Requirement |
| --- | --- |
| NFR-015 | A developer shall be able to restore and build the solution using standard .NET SDK tooling. |
| NFR-016 | A developer shall be able to manually run the Aspire AppHost from `./src/Archon/Archon.csproj`. |
| NFR-017 | Manual verification instructions shall explain how to confirm Neo4j, API host, and MCP host are running without a Discovery UI. |

## 6. Technical Requirements

### 6.1 Target Runtime and SDK

The implementation shall use the repository-approved .NET and Aspire versions. Repository guidance states that Archon stable project identity shall normalize project file paths relative to the repository root and that Aspire SDK 13.3.3 shall be used for WP001 unless later repository guidance supersedes it.

### 6.2 Project Identity

Project identity and dependency-boundary checks shall use normalized project paths relative to the repository root so identities are deterministic across developer machine locations.

### 6.3 Health Endpoint Naming

The API and MCP hosts shall expose consistent health and readiness endpoints. Recommended endpoint names are:

| Endpoint | Purpose |
| --- | --- |
| `/health` | Overall health/readiness where a single probe is sufficient. |
| `/alive` | Liveness when service defaults expose separate liveness behavior. |

The final implementation may use the default Aspire service defaults endpoint pattern if it is consistent across hosts and verified by tests.

### 6.4 Package and Project Reference Layout

`.csproj` files shall keep `PackageReference` entries in `ItemGroup` blocks that contain only package references. `ProjectReference` entries shall be kept in separate `ItemGroup` blocks.

### 6.5 Documentation Pass

WP001 shall include a documentation pass covering:

- How to build the solution.
- How to run the AppHost manually.
- How to confirm the absence of Discovery UI implementation.
- How the project structure maps to the work-package sequence.
- How architecture-boundary tests enforce Onion direction.
- Any notable implementation decisions or intentional placeholders.

Internal and non-public implementation types introduced for WP001 shall be treated as requiring the same developer-level documentation standard as public types when documentation is necessary to understand the architecture or behavior.

## 7. Exclusions

WP001 shall not implement:

- Archon Discovery UI host, pages, components, assets, or tests for UI behavior.
- Extraction submission API beyond any future-contract placeholder explicitly required for project structure.
- Roslyn solution loading.
- Project inventory extraction.
- Package reference extraction.
- Source-code semantic extraction.
- Neo4j graph schema creation, constraints, indexes, persistence, or Cypher query behavior.
- API query endpoints.
- API management endpoints beyond health/readiness.
- MCP tools, resources, prompts, architecture lookups, or Copilot workflows.
- Markdown export.
- Hotlist rule evaluation.
- Snapshot diff or architecture drift behavior.

## 8. Data and Integration Requirements

### 8.1 Neo4j

Neo4j shall be composed as an Aspire-managed runtime dependency in WP001. WP001 does not require graph schema creation or data persistence, but the system must establish the composition path that later work packages will use.

### 8.2 API and MCP Integration

The API and MCP hosts shall be separately runnable resources under the Aspire AppHost. They do not need to call each other in WP001. Their shared runtime configuration shall come through service defaults and Aspire composition.

### 8.3 Future Extraction Contract Awareness

Although extraction is not implemented in WP001, the project layout must preserve the later API-triggered extraction direction: callers will provide a repository root directory and explicit solution path list. No WP001 design decision may require extraction to be initiated only by scanning arbitrary directories or by manually running a local CLI.

## 9. Test Requirements

### 9.1 Required Test Coverage

| Test Area | Required Verification |
| --- | --- |
| Solution structure | Expected production and test project files exist under `./src` and `./test`. |
| Build | The solution builds. |
| Project references | Dependency direction follows Onion Architecture. |
| Service defaults | Health, telemetry, service discovery, and resilience defaults are registered through shared service configuration. |
| API host bootstrap | Health/readiness endpoints are mapped and reachable through an in-memory or test host approach. |
| MCP host bootstrap | Health/readiness behavior is mapped and reachable through an in-memory or test host approach where applicable. |
| Aspire composition | AppHost resource model includes Neo4j, API host, and MCP host, and excludes Discovery UI. |
| Documentation | Manual AppHost verification instructions exist and explicitly warn not to automate blocking AppHost execution. |

### 9.2 Test Constraints

Automated verification must not run the Aspire AppHost process as a blocking command. Tests may inspect AppHost composition through safe seams or project metadata, but they must not rely on long-running orchestration processes.

For this work package, the full test suite should not be run unless explicitly requested. Run targeted tests relevant to the foundation and a solution build as final validation.

## 10. Acceptance Criteria

WP001 is accepted when all of the following are true:

1. `Archon.slnx` exists and includes the complete planned production and test project skeleton.
2. All planned production projects under `./src` exist and build.
3. Corresponding test projects under `./test` exist and build.
4. The Aspire AppHost composes Neo4j, `ArchonApi`, and `ArchonMcp`.
5. The Aspire AppHost does not compose Archon Discovery UI.
6. Service defaults are shared by the API and MCP hosts.
7. API host bootstrap health/readiness behavior is implemented and tested.
8. MCP host bootstrap health/readiness behavior is implemented and tested.
9. Onion Architecture reference direction is enforced by tests.
10. No extraction, query, MCP tool, graph persistence, markdown export, or UI capability is implemented in WP001.
11. Later capabilities are explicitly assigned to later work packages rather than being treated as optional or deferred.
12. Manual Aspire verification instructions are documented.
13. The solution builds successfully.
14. Targeted tests for WP001 pass.

## 11. Risks and Decisions

### 11.1 Risks

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Creating every planned project up front may produce a large skeleton. | The initial solution may look broad despite limited behavior. | Keep project contents minimal and compile-safe; document that behavior arrives in later work packages. |
| Aspire AppHost automation can block the executing agent. | Automated validation may hang. | Do not run AppHost in automated tests; provide manual verification instructions. |
| Excluding UI while source brief includes UI target state may cause ambiguity. | Developers may accidentally create `ArchonUi`. | WP001 and work-package sequence explicitly exclude Discovery UI implementation. |
| Placeholder projects may drift from architecture rules. | Later implementation may add invalid references. | Add dependency-boundary tests in WP001. |
| Neo4j runtime composition may be environment-sensitive. | Manual verification may require container runtime availability. | Document prerequisites and expected manual verification steps. |

### 11.2 Decisions

| Decision | Rationale |
| --- | --- |
| Use a single WP001 specification document. | User requested one markdown document spec for WP001. |
| Create the documentation under `docs/001-Solution-Foundation/`. | This is the next incremental work-package documentation folder; only `docs/foundation/` existed before creation. |
| Do not create separate component specs for WP001. | User requested a single markdown document for WP001, overriding the multi-document collaboration pattern for this output. |
| Exclude Discovery UI implementation from WP001. | The work-package sequence explicitly removes UI delivery until full API and MCP capability is complete. |
| Require dependency-boundary tests in the foundation. | Boundary enforcement is most effective when established before implementation projects accumulate behavior. |

## 12. Manual Verification Requirements

The implementation documentation for WP001 shall instruct a developer to verify Aspire manually by:

1. Restoring and building the solution.
2. Running the Aspire AppHost project from `./src/Archon/Archon.csproj`.
3. Confirming the Aspire dashboard shows Neo4j, `ArchonApi`, and `ArchonMcp` resources.
4. Confirming no Archon Discovery UI resource is present.
5. Opening the API health/readiness endpoint and confirming a healthy response.
6. Opening the MCP host health/readiness endpoint or equivalent probe and confirming a healthy response.
7. Stopping the AppHost manually after verification.

Automated validation instructions shall explicitly state not to run the AppHost as a blocking process during agent-driven verification.

## 13. Traceability Matrix

| Source Requirement | Specification Coverage |
| --- | --- |
| Complete planned production projects under `./src` | Sections 4.1, 4.2, 10 |
| Corresponding test projects under `./test` | Sections 4.3, 9, 10 |
| Aspire AppHost composes Neo4j, API, MCP | Sections 4.4, 8.1, 10, 12 |
| Service defaults for health, telemetry, resilience, shared configuration | Sections 4.5, 5.3, 9 |
| Onion Architecture references | Sections 4.8, 9, 10 |
| API health/readiness only | Sections 4.6, 7, 10 |
| MCP health/readiness only, no tools | Sections 4.7, 7, 10 |
| Exclude Discovery UI | Sections 4.1, 4.4, 7, 10, 11 |
| Do not automate blocking AppHost run | Sections 4.4, 9.2, 12 |
| Tests verify bootstrap, service defaults, dependency boundaries | Sections 9, 10 |
| Repository documentation updated | Sections 6.5, 12 |

## 14. Open Questions

No blocking open questions are known for producing the WP001 foundation specification. Aspire SDK `13.3.3` is confirmed as required for WP001. Implementation may still need to confirm that the developer environment has a compatible .NET SDK and can restore the required Aspire SDK version before code work begins.

## 15. Change Log

| Date | Change |
| --- | --- |
| 2026-05-20 | Created initial single-document WP001 specification from `docs/foundation/work-packages.md` and the Archon source brief. |
| 2026-05-20 | Recorded Aspire SDK `13.3.3` as a confirmed WP001 requirement. |
