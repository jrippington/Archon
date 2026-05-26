# WP014 Specification - Query API Product Surface

## Document Control

| Field | Value |
| --- | --- |
| Work Package | WP014 - Query API Product Surface |
| Output Path | `docs/014-Query-API-Product-Surface/spec-wp014-query-api-product-surface.md` |
| Source Work Package | `docs/foundation/work-packages.md` WP014 |
| Source Brief | `docs/foundation/archon_full_concept_brief.md` |
| Specification Basis | `spec-template_v1.1.md` requested; template file was not present in the workspace at creation time, so this document follows the repository specification structure and documented work-package rules. |
| Status | Draft |
| Audience | Product owner, architect, implementation team, test engineer, API consumer, MCP implementer |

## 1. Overview

### 1.1 Purpose

This specification defines the requirements for WP014, the Archon work package that delivers the complete API query and controlled management product surface for non-UI consumers.

WP014 turns the persisted architecture graph, extraction snapshots, findings, rules, metrics, hotspots, and snapshot diffs from earlier work packages into stable HTTP-accessible product capabilities. It is the API-first surface that later MCP tools, resources, prompts, markdown export access, automation workflows, and external consumers rely on.

### 1.2 Background

Archon is an API-first and MCP-first deterministic architecture intelligence platform for modern and legacy .NET estates. Earlier work packages establish the solution foundation, graph domain model, Neo4j persistence, extraction orchestration, repository and project extraction, Roslyn semantic extraction, configuration and dependency-injection extraction, runtime extraction, data-access extraction, external integration extraction, .NET client and UI-technology extraction, rule catalog and findings, metrics, hotspots, architecture rules, and snapshot diff.

WP014 builds on those completed capabilities. It does not introduce Archon Discovery UI pages, client-side visualization, MCP tool hosting, or markdown export generation. Instead, it exposes evidence-backed query and management operations over the persisted graph through stable API contracts suitable for direct API use and later MCP consumption.

### 1.3 High-Level Scope

WP014 covers these capability areas:

- Dashboard-summary query data for repository, solution, snapshot, count, hotspot, and latest-change summaries.
- Project catalogue and project detail query surfaces.
- Dependency, dependent, transitive traversal, graph-neighbourhood, and dependency-path query surfaces.
- Symbol lookup and symbol usage query surfaces.
- Endpoint, controller, route, request/response DTO, authorization, service usage, and runtime entry-point query surfaces.
- Worker, hosted-service, background-service, queue/topic consumer, scheduled-job, and worker-runtime query surfaces.
- Data-access, configuration usage, external integration, and .NET UI-technology fact query surfaces.
- Evidence drill-down for every exposed claim that can be traced to source evidence.
- Hotlist, finding, rule catalog, metric, hotspot, architecture-rule result, and snapshot diff query surfaces.
- Repository registration, solution registration, metadata management, snapshot lifecycle, retention, rule visibility, rule enablement, extraction run history, health, readiness, and controlled maintenance operations.
- Response-size controls, pagination, filtering, sorting, stable DTO contracts, error contracts, and MCP-suitable response shapes.
- Tests and documentation for all production behavior introduced or formalized by this work package.

WP014 excludes Archon Discovery UI implementation, UI graph rendering, MCP server tools/resources/prompts, markdown export generation, arbitrary graph query execution by callers, arbitrary SQL execution, arbitrary filesystem mutation, code modification, automatic remediation, and any product behavior that bypasses the application/query layer to expose Neo4j directly.

## 2. System Context

### 2.1 Product Context

Archon accepts API-triggered extraction requests, extracts deterministic architecture facts into snapshots, persists those facts in Neo4j, and exposes architecture knowledge through API and MCP surfaces. WP014 is the primary HTTP query and management layer over that persisted model.

The package must use existing application services, query services, graph repository abstractions, stable-key strategy, fingerprint strategy, snapshot model, evidence model, rule/finding model, metric model, hotspot output, and diff semantics. It must not replace earlier persistence or extraction paths, create a parallel read model that becomes a competing source of truth, or expose raw database implementation details as public API contracts.

### 2.2 Source References

WP014 must align with these source materials:

- `docs/foundation/work-packages.md` WP014 objective, required implementation, and completion criteria.
- `docs/foundation/archon_full_concept_brief.md` section 8.1 for API host, query module, management module, authentication, authorization, validation, telemetry, and host composition responsibilities.
- `docs/foundation/archon_full_concept_brief.md` sections 28.1 through 28.11 for non-visual information needs that must be satisfied through API responses in the API-first sequence.
- `docs/foundation/archon_full_concept_brief.md` section 29.2 for MCP tool dependencies on query capability.
- `docs/foundation/archon_full_concept_brief.md` section 31 for markdown export access expectations.
- `docs/foundation/archon_full_concept_brief.md` Appendix E section E.5.8 for minimum query model requirements.
- `docs/foundation/archon_full_concept_brief.md` Appendix E section E.5.9 for stable-key and fingerprint based diff semantics.
- `docs/foundation/archon_full_concept_brief.md` Appendix E section E.9 for acceptance criteria relevant to graph support, evidence, classification, unknowns, diff, and rule support.
- `docs/foundation/work-packages.md` completion rules for evidence-backed statements, explicit unknowns, deterministic stable keys, Neo4j as the system of record, tests, documentation, and no Discovery UI.
- `.github/instructions/documentation-pass.instructions.md` for mandatory source documentation expectations during implementation planning and execution, including internal and non-public types.

### 2.3 Users and Stakeholders

| Stakeholder | Interest |
| --- | --- |
| Product owner | Confirms WP014 exposes the mandatory API-first product capabilities without UI delivery or deferred query behavior. |
| Architect | Confirms API contracts preserve deterministic evidence-backed architecture intelligence and avoid exposing raw persistence details. |
| Developer | Uses API queries to inspect projects, dependencies, symbols, endpoints, workers, data access, configuration, integrations, findings, metrics, diffs, and evidence. |
| MCP implementer | Depends on stable, complete, response-size-limited query APIs rather than direct Neo4j access. |
| Test engineer | Verifies every query family, management operation, filtering, paging, sorting, error response, and evidence linkage. |
| Operations owner | Uses health, readiness, extraction run history, retention controls, maintenance operations, and audit-ready metadata. |
| Security reviewer | Confirms management operations are controlled and query APIs do not expose secrets, raw arbitrary query execution, or mutation beyond intended management commands. |

## 3. Component Summary

### 3.1 API Query Module

The API Query Module exposes read-oriented HTTP endpoints over the architecture-wide persisted model. It coordinates query request validation, snapshot selection, pagination, filtering, sorting, response-size controls, DTO mapping, evidence inclusion, unknown reporting, and error response shaping.

### 3.2 API Management Module

The API Management Module exposes controlled operations for repository registration, solution registration, metadata management, snapshot lifecycle, retention controls, rule catalog visibility, rule enablement controls, extraction run history, health, readiness, and safe maintenance. It must distinguish controlled product operations from arbitrary database, shell, filesystem, or code mutation capabilities.

### 3.3 Dashboard Summary Query Component

The dashboard summary query component returns non-visual summary data for repositories, solutions, snapshots, project counts, language counts, application counts, endpoint counts, data-access counts, hotlist counts, top coupling hotspots, and latest architecture changes.

### 3.4 Project Catalogue and Detail Query Component

The project query component supports searchable and pageable project catalogue results and detailed project records. Project detail includes summary, responsibilities where available, evidence, entry points, project references, referencing projects, packages, application type, endpoints, workers, data access, configuration keys, external integrations, hotlist findings, scoped graph data, and unknowns.

### 3.5 Dependency and Graph Traversal Query Component

The dependency query component supports direct dependencies, direct dependents, transitive dependencies, transitive dependents, dependency paths between two nodes, graph neighbourhoods, edge-kind filters, direction filters, depth filters, and large-result controls.

### 3.6 Symbol Query Component

The symbol query component supports symbol lookup, symbol detail, and symbol usage discovery over persisted Roslyn semantic facts. It must return stable symbol identities, containing project and source context, relationships, evidence, confidence, and unknowns where resolution was partial.

### 3.7 Runtime Query Component

The runtime query component supports endpoint, controller, handler, worker, hosted-service, background-service, queue/topic consumer, scheduled-job, and runtime entry-point queries. It provides evidence-backed runtime facts extracted in earlier work packages.

### 3.8 Data, Configuration, Integration, and UI-Technology Fact Query Component

This component exposes data-access, configuration usage, external integration, and .NET UI-technology facts. It includes LINQ to SQL, Entity Framework Classic/EF6, Entity Framework Core, ADO.NET, typed DataSets, raw SQL, stored procedures, configuration keys, integration endpoints, clients, protocols, and backend facts about .NET UI technologies extracted for API and MCP consumers.

### 3.9 Evidence Query Component

The evidence query component provides drill-down from API claims to file path, line range, symbol, snippet preview, finding or rule context, snapshot, confidence, classification, and unknown-reason data. It must be available for all exposed claims that have supporting evidence.

### 3.10 Rule, Finding, Metric, Hotspot, and Diff Query Component

This component exposes rule catalog records, rule status, findings, hotlist reports, metrics, hotspots, architecture-rule results, cycles, and snapshot diff records across nodes, edges, findings, and metrics.

### 3.11 Contract, Pagination, and Error Handling Component

The contract component defines stable DTOs, response envelopes, pagination cursors or page metadata, filtering and sorting conventions, response-size truncation metadata, validation errors, not-found errors, conflict errors, authorization errors, and partial-result warnings.

## 4. Functional Requirements

### 4.1 API Surface Principles

| ID | Requirement |
| --- | --- |
| FR-001 | WP014 shall expose query and management capabilities through the Archon API host. |
| FR-002 | Query APIs shall read from the persisted architecture graph through application/query abstractions rather than by exposing Neo4j directly to callers. |
| FR-003 | Query APIs shall return deterministic, evidence-backed responses based on persisted facts, persisted findings, persisted metrics, persisted rules, and deterministic diff logic. |
| FR-004 | Query APIs shall not invent facts, ownership, intent, risk, or remediation advice that is not supported by persisted facts, rules, findings, metrics, evidence, or explicit unknowns. |
| FR-005 | Query APIs shall include unknowns and confidence where persisted source data indicates incomplete or uncertain extraction. |
| FR-006 | Query APIs shall use stable keys in public contracts instead of Neo4j internal IDs. |
| FR-007 | Query APIs shall support explicit snapshot selection where the result depends on snapshot state. |
| FR-008 | Query APIs shall support a latest/current snapshot selector for convenience where unambiguous. |
| FR-009 | Query APIs shall make snapshot identity visible in response metadata. |
| FR-010 | Query APIs shall expose DTO contracts suitable for direct MCP consumption without requiring MCP to query Neo4j directly. |
| FR-011 | Query APIs shall not implement human-facing Archon Discovery UI pages, components, or front-end assets. |
| FR-012 | Query APIs shall not expose arbitrary Cypher, arbitrary SQL, shell execution, filesystem mutation, database mutation outside controlled management operations, or code modification capabilities. |

### 4.2 Common Request and Response Behavior

| ID | Requirement |
| --- | --- |
| FR-013 | List endpoints shall support pagination. |
| FR-014 | List endpoints shall provide deterministic ordering when callers do not specify a sort. |
| FR-015 | List endpoints shall support filtering by snapshot where applicable. |
| FR-016 | List endpoints shall support filtering by repository and solution where applicable. |
| FR-017 | List endpoints shall support filtering by stable key where applicable. |
| FR-018 | List endpoints shall support filtering by project where applicable. |
| FR-019 | List endpoints shall support text search where useful for catalogue, symbol, endpoint, finding, rule, integration, and data-access queries. |
| FR-020 | List endpoints shall support sorting by stable deterministic fields where sorting is meaningful. |
| FR-021 | Responses shall include pagination metadata for paged results. |
| FR-022 | Responses shall include truncation metadata when response-size limits are applied. |
| FR-023 | Responses shall include applied filter metadata where that improves explainability. |
| FR-024 | Responses shall include evidence references when individual records are evidence-backed. |
| FR-025 | Responses shall include warnings when results are partial due to extraction gaps, limits, or unavailable optional data. |
| FR-026 | Responses shall use consistent error shapes for validation, not found, conflict, unauthorized, forbidden, and server errors. |
| FR-027 | Error responses shall not expose secrets, connection strings, local environment secrets, raw stack traces, or sensitive internal implementation details. |

### 4.3 Dashboard Summary Queries

| ID | Requirement |
| --- | --- |
| FR-028 | WP014 shall expose a dashboard-summary query for a repository, solution, or snapshot scope. |
| FR-029 | Dashboard summary shall return repository identity where available. |
| FR-030 | Dashboard summary shall return solution identity where available. |
| FR-031 | Dashboard summary shall return latest or selected snapshot identity. |
| FR-032 | Dashboard summary shall return commit SHA when persisted. |
| FR-033 | Dashboard summary shall return analysis date/time. |
| FR-034 | Dashboard summary shall return project count. |
| FR-035 | Dashboard summary shall return C# project count. |
| FR-036 | Dashboard summary shall return VB.NET project count. |
| FR-037 | Dashboard summary shall return API count. |
| FR-038 | Dashboard summary shall return worker count. |
| FR-039 | Dashboard summary shall return endpoint count. |
| FR-040 | Dashboard summary shall return data-context count. |
| FR-041 | Dashboard summary shall return hotlist finding count. |
| FR-042 | Dashboard summary shall return top coupling hotspots where metrics are available. |
| FR-043 | Dashboard summary shall return latest architecture changes where snapshot diff data is available. |
| FR-044 | Dashboard summary shall represent missing optional summary inputs as unknowns or warnings rather than silently omitting required summary categories. |

### 4.4 Project Catalogue Queries

| ID | Requirement |
| --- | --- |
| FR-045 | WP014 shall expose a project catalogue query. |
| FR-046 | Project catalogue records shall include project name. |
| FR-047 | Project catalogue records shall include project path. |
| FR-048 | Project catalogue records shall include language. |
| FR-049 | Project catalogue records shall include project type. |
| FR-050 | Project catalogue records shall include target framework. |
| FR-051 | Project catalogue records shall indicate SDK-style or old-style project format where available. |
| FR-052 | Project catalogue records shall include incoming reference count. |
| FR-053 | Project catalogue records shall include outgoing reference count. |
| FR-054 | Project catalogue records shall include package count. |
| FR-055 | Project catalogue records shall include endpoint count. |
| FR-056 | Project catalogue records shall include data-access indicators. |
| FR-057 | Project catalogue records shall include hotlist count. |
| FR-058 | Project catalogue records shall include risk indicators derived from persisted findings, metrics, or classification facts. |
| FR-059 | Project catalogue query shall support search by project name and path. |
| FR-060 | Project catalogue query shall support filters for language, project type, target framework, SDK-style status, risk indicator, and hotlist severity where data exists. |
| FR-061 | Project catalogue query shall support pagination and deterministic sorting. |

### 4.5 Project Detail Queries

| ID | Requirement |
| --- | --- |
| FR-062 | WP014 shall expose a project detail query by project stable key or unambiguous project name. |
| FR-063 | Project detail shall include project summary data. |
| FR-064 | Project detail shall include responsibilities where persisted or deterministically derived. |
| FR-065 | Project detail shall include evidence references. |
| FR-066 | Project detail shall include entry points. |
| FR-067 | Project detail shall include project references. |
| FR-068 | Project detail shall include referencing projects. |
| FR-069 | Project detail shall include package references. |
| FR-070 | Project detail shall include application type. |
| FR-071 | Project detail shall include endpoints. |
| FR-072 | Project detail shall include workers and hosted services. |
| FR-073 | Project detail shall include data-access facts. |
| FR-074 | Project detail shall include configuration keys. |
| FR-075 | Project detail shall include external integrations. |
| FR-076 | Project detail shall include hotlist findings. |
| FR-077 | Project detail shall include scoped graph summary data. |
| FR-078 | Project detail shall include unknowns and confidence where extraction was incomplete. |
| FR-079 | Ambiguous project-name lookup shall return a conflict or disambiguation response rather than choosing an arbitrary project. |

### 4.6 Dependency and Graph Traversal Queries

| ID | Requirement |
| --- | --- |
| FR-080 | WP014 shall expose direct dependency queries for a node or project. |
| FR-081 | WP014 shall expose direct dependent queries for a node or project. |
| FR-082 | WP014 shall expose transitive dependency queries for a node or project. |
| FR-083 | WP014 shall expose transitive dependent queries for a node or project. |
| FR-084 | WP014 shall expose dependency path queries between two nodes. |
| FR-085 | WP014 shall expose graph-neighbourhood queries for scoped graph exploration. |
| FR-086 | Dependency and graph queries shall support direction filters. |
| FR-087 | Dependency and graph queries shall support depth filters. |
| FR-088 | Dependency and graph queries shall support edge-kind filters. |
| FR-089 | Graph-neighbourhood queries shall default to depth 1 unless explicitly overridden. |
| FR-090 | Graph-neighbourhood queries shall enforce maximum depth and result-size limits. |
| FR-091 | Graph-neighbourhood queries shall return a useful truncation message or metadata when too many nodes would be returned. |
| FR-092 | Dependency path queries shall return stable node keys and stable edge keys in path order. |
| FR-093 | Dependency path queries shall support no-path responses that distinguish no relationship from unavailable data. |
| FR-094 | Dependency and graph query results shall include evidence links where the underlying relationship has evidence. |

### 4.7 Symbol Queries

| ID | Requirement |
| --- | --- |
| FR-095 | WP014 shall expose symbol lookup by stable key. |
| FR-096 | WP014 shall expose symbol lookup by search text. |
| FR-097 | Symbol lookup shall support filtering by project, kind, namespace, containing type, and language where available. |
| FR-098 | Symbol detail shall include stable symbol key. |
| FR-099 | Symbol detail shall include symbol name and fully qualified name where available. |
| FR-100 | Symbol detail shall include symbol kind. |
| FR-101 | Symbol detail shall include containing project and source context. |
| FR-102 | Symbol detail shall include evidence references. |
| FR-103 | Symbol detail shall include related architecture relationships where available. |
| FR-104 | WP014 shall expose symbol usage queries. |
| FR-105 | Symbol usage queries shall identify calling or referencing nodes where persisted. |
| FR-106 | Symbol usage queries shall include file path, line range, snippet preview, confidence, and snapshot context where evidence exists. |
| FR-107 | Symbol queries shall preserve unresolved-symbol unknowns rather than implying complete resolution. |

### 4.8 Endpoint and Runtime Queries

| ID | Requirement |
| --- | --- |
| FR-108 | WP014 shall expose endpoint lookup queries. |
| FR-109 | Endpoint lookup shall support filters for HTTP method, route, project, controller or handler, authorization attribute, and snapshot where available. |
| FR-110 | Endpoint records shall include HTTP method. |
| FR-111 | Endpoint records shall include route. |
| FR-112 | Endpoint records shall include project. |
| FR-113 | Endpoint records shall include controller, handler, action, or method where available. |
| FR-114 | Endpoint records shall include request DTO where available. |
| FR-115 | Endpoint records shall include response DTO where available. |
| FR-116 | Endpoint records shall include authorization attributes where available. |
| FR-117 | Endpoint records shall include services used where available. |
| FR-118 | Endpoint records shall include DbContext or DataContext usage where available. |
| FR-119 | Endpoint records shall include configuration keys where available. |
| FR-120 | Endpoint records shall include evidence references. |
| FR-121 | WP014 shall expose controller and handler lookup where controller or handler facts are persisted separately from endpoint facts. |
| FR-122 | WP014 shall expose runtime entry-point lookup for API, worker, console, and service-host style entry points where extracted. |

### 4.9 Worker Queries

| ID | Requirement |
| --- | --- |
| FR-123 | WP014 shall expose worker lookup queries. |
| FR-124 | Worker records shall include worker project. |
| FR-125 | Worker records shall include entry point where available. |
| FR-126 | Worker records shall include hosted services. |
| FR-127 | Worker records shall include background services. |
| FR-128 | Worker records shall include queue or topic consumers where available. |
| FR-129 | Worker records shall include scheduled jobs where available. |
| FR-130 | Worker records shall include data-access facts where available. |
| FR-131 | Worker records shall include external integrations where available. |
| FR-132 | Worker records shall include configuration keys where available. |
| FR-133 | Worker records shall include evidence references. |
| FR-134 | Worker lookup shall support filters for project, hosted-service type, queue/topic identity, scheduled-job indicator, data-access indicator, integration indicator, and snapshot where available. |

### 4.10 Data-Access Queries

| ID | Requirement |
| --- | --- |
| FR-135 | WP014 shall expose data-access lookup queries. |
| FR-136 | Data-access queries shall support LINQ to SQL facts. |
| FR-137 | Data-access queries shall support Entity Framework Classic and EF6 facts. |
| FR-138 | Data-access queries shall support Entity Framework Core facts. |
| FR-139 | Data-access queries shall support ADO.NET facts. |
| FR-140 | Data-access queries shall support typed DataSet facts. |
| FR-141 | Data-access queries shall support raw SQL facts. |
| FR-142 | Data-access queries shall support stored procedure facts. |
| FR-143 | LINQ to SQL query results shall expose DataContexts. |
| FR-144 | LINQ to SQL query results shall expose entities. |
| FR-145 | LINQ to SQL query results shall expose tables. |
| FR-146 | LINQ to SQL query results shall expose stored procedures. |
| FR-147 | LINQ to SQL query results shall expose usage sites. |
| FR-148 | LINQ to SQL query results shall expose SubmitChanges call sites. |
| FR-149 | LINQ to SQL query results shall expose ExecuteQuery and ExecuteCommand call sites. |
| FR-150 | Data-access queries shall answer which projects use a DataContext where data exists. |
| FR-151 | Data-access queries shall answer which methods call SubmitChanges where data exists. |
| FR-152 | Data-access queries shall answer which tables are touched by a project where data exists. |
| FR-153 | Data-access queries shall answer where raw SQL is used where data exists. |
| FR-154 | Data-access queries shall answer which entities are shared across many projects where data exists. |
| FR-155 | Data-access results shall include evidence references, confidence, and unknowns. |

### 4.11 Configuration Usage Queries

| ID | Requirement |
| --- | --- |
| FR-156 | WP014 shall expose configuration usage lookup queries. |
| FR-157 | Configuration usage queries shall support filtering by configuration key, project, consumer node, provider, environment, source file, and snapshot where available. |
| FR-158 | Configuration usage results shall include key identity, usage site, consuming project, binding source, inferred endpoint classification where available, confidence, unknowns, and evidence references. |
| FR-159 | Configuration usage results shall avoid exposing secret values. |
| FR-160 | Configuration usage results shall expose key names or safe metadata only when doing so does not reveal sensitive values beyond what is already present in source evidence. |

### 4.12 External Integration Queries

| ID | Requirement |
| --- | --- |
| FR-161 | WP014 shall expose external integration lookup queries. |
| FR-162 | Integration queries shall support filtering by project, integration kind, endpoint host or service name where safely available, protocol, client type, configuration key, and snapshot where available. |
| FR-163 | Integration results shall include consuming project, integration target metadata, client or call-site identity, configuration references, confidence, unknowns, and evidence references. |
| FR-164 | Integration results shall avoid exposing secrets, credentials, tokens, and connection strings. |

### 4.13 .NET UI-Technology Fact Queries

| ID | Requirement |
| --- | --- |
| FR-165 | WP014 shall expose backend query access to .NET UI-technology facts extracted for API and MCP consumers. |
| FR-166 | UI-technology fact queries shall include UI application, component, view, route, binding, command, and navigation lookup where persisted. |
| FR-167 | UI-technology fact queries shall support Blazor, Razor, Windows Forms, WPF, WinUI, .NET MAUI, and Avalonia facts where extracted. |
| FR-168 | UI-technology fact queries shall not create Archon Discovery UI pages or front-end assets. |
| FR-169 | UI-technology fact results shall include project context, technology classification, fact kind, evidence references, confidence, and unknowns. |

### 4.14 Evidence Drill-Down Queries

| ID | Requirement |
| --- | --- |
| FR-170 | WP014 shall expose evidence lookup by evidence stable key. |
| FR-171 | WP014 shall expose evidence lookup by related node, edge, finding, metric, or rule result where relationships exist. |
| FR-172 | Evidence records shall include file path. |
| FR-173 | Evidence records shall include line range where available. |
| FR-174 | Evidence records shall include symbol where available. |
| FR-175 | Evidence records shall include snippet preview where available and safe. |
| FR-176 | Evidence records shall include finding or rule context where applicable. |
| FR-177 | Evidence records shall include snapshot identity. |
| FR-178 | Evidence records shall include confidence. |
| FR-179 | Evidence records shall include classification where available. |
| FR-180 | Evidence records shall include unknown-reason data where the evidence supports an unknown or partial fact. |
| FR-181 | Evidence snippets shall be limited to safe size bounds. |
| FR-182 | Evidence snippets shall avoid expanding beyond persisted evidence context unless explicitly supported by the evidence model. |

### 4.15 Rule, Finding, and Hotlist Queries

| ID | Requirement |
| --- | --- |
| FR-183 | WP014 shall expose rule catalog query APIs. |
| FR-184 | Rule catalog queries shall support filtering by rule code, version, category, severity, enabled status, and snapshot or current catalog context where applicable. |
| FR-185 | Rule catalog records shall include rule code, rule version, display name, category, severity, description, enabled state, and metadata where available. |
| FR-186 | WP014 shall expose hotlist finding query APIs. |
| FR-187 | Finding queries shall support filters for critical only, legacy data access, out of support, security-sensitive, framework-only, project, technology, severity, status, rule code, affected node, and snapshot. |
| FR-188 | Finding records shall include finding identity, severity, category, status, affected project, affected node, evidence references, confidence, first seen snapshot, latest seen snapshot, suppression fields where available, and metadata. |
| FR-189 | Finding query APIs shall support stable ordering, pagination, and text search where useful. |
| FR-190 | Finding query APIs shall distinguish active, suppressed, resolved, and historical findings where such statuses are persisted. |

### 4.16 Metric, Hotspot, Architecture-Rule, and Cycle Queries

| ID | Requirement |
| --- | --- |
| FR-191 | WP014 shall expose metric query APIs. |
| FR-192 | Metric queries shall support project metrics, graph metrics, modernization metrics, and snapshot-level metrics. |
| FR-193 | Metric queries shall support filtering by metric kind, scope kind, target stable key, project, solution, repository, and snapshot. |
| FR-194 | Metric records shall include stable key, fingerprint, metric kind, scope, target, value, unit, metadata, snapshot identity, confidence, unknowns, and evidence references where available. |
| FR-195 | WP014 shall expose hotspot query APIs. |
| FR-196 | Hotspot queries shall support filtering by hotspot category, target type, project, severity or score band where available, and snapshot. |
| FR-197 | Hotspot records shall include target stable key, category, score or rank where available, contributing metrics, relevant findings, evidence references, confidence, and unknowns. |
| FR-198 | WP014 shall expose architecture-rule result query APIs. |
| FR-199 | Architecture-rule result queries shall expose layering and dependency-pattern results from configured architecture rules and graph checks. |
| FR-200 | Architecture-rule result records shall include rule identity, target node or edge, status, severity, evidence, confidence, and metadata. |
| FR-201 | WP014 shall expose cycle query APIs. |
| FR-202 | Cycle query records shall include stable node keys, stable edge keys, cycle path order, related metrics, evidence references where available, and truncation metadata where limits apply. |

### 4.17 Snapshot Diff Queries

| ID | Requirement |
| --- | --- |
| FR-203 | WP014 shall expose snapshot diff query APIs. |
| FR-204 | Snapshot diff queries shall compare two explicit snapshots. |
| FR-205 | Snapshot diff queries shall support default comparison of the latest snapshot to a previous snapshot where unambiguous. |
| FR-206 | Snapshot diff queries shall operate over nodes, edges, findings, and metrics. |
| FR-207 | Snapshot diff results shall classify records as added, removed, changed, or unchanged. |
| FR-208 | Snapshot diff changed classification shall use the same stable key with a different normalized fingerprint. |
| FR-209 | Snapshot diff results shall include new projects. |
| FR-210 | Snapshot diff results shall include removed projects. |
| FR-211 | Snapshot diff results shall include target framework changes. |
| FR-212 | Snapshot diff results shall include new project references. |
| FR-213 | Snapshot diff results shall include removed project references. |
| FR-214 | Snapshot diff results shall include new package references. |
| FR-215 | Snapshot diff results shall include removed package references. |
| FR-216 | Snapshot diff results shall include new endpoints. |
| FR-217 | Snapshot diff results shall include removed endpoints. |
| FR-218 | Snapshot diff results shall include changed routes. |
| FR-219 | Snapshot diff results shall include new data contexts. |
| FR-220 | Snapshot diff results shall include changed data access. |
| FR-221 | Snapshot diff results shall include new hotlist findings. |
| FR-222 | Snapshot diff results shall include resolved hotlist findings. |
| FR-223 | Snapshot diff results shall include coupling metric changes. |
| FR-224 | Snapshot diff APIs shall support filtering by record kind, change kind, project, target node, severity where applicable, and response-size controls. |

### 4.18 Search and AI/MCP-Oriented Query Support

| ID | Requirement |
| --- | --- |
| FR-225 | WP014 shall expose search-oriented query capability across supported architecture facts. |
| FR-226 | Search results shall identify result kind, stable key, display text, summary, snapshot, confidence, and evidence references where available. |
| FR-227 | Search shall support enough result kinds to support later `archon.search` MCP capability. |
| FR-228 | WP014 shall expose project description data sufficient for later `archon.describe_project` MCP capability. |
| FR-229 | WP014 shall expose dependency and dependent data sufficient for later `archon.get_dependencies` and `archon.get_dependents` MCP capabilities. |
| FR-230 | WP014 shall expose dependency path data sufficient for later `archon.find_dependency_paths` MCP capability. |
| FR-231 | WP014 shall expose symbol detail and usage data sufficient for later `archon.describe_symbol` and `archon.find_symbol_usages` MCP capabilities. |
| FR-232 | WP014 shall expose data-access usage data sufficient for later `archon.get_data_access_usage` MCP capability. |
| FR-233 | WP014 shall expose impact-oriented traversal and summary data sufficient for later `archon.assess_change_impact` MCP capability. |
| FR-234 | WP014 shall expose architecture rule, hotlist finding, and snapshot diff data sufficient for later MCP rule, hotlist, and diff tools. |
| FR-235 | AI-oriented query responses shall include confidence, evidence, related nodes, unknowns, and suggested follow-up query affordances when supported by deterministic data. |

### 4.19 Repository, Solution, and Metadata Management

| ID | Requirement |
| --- | --- |
| FR-236 | WP014 shall expose controlled repository registration APIs. |
| FR-237 | Repository registration shall accept required repository identity and root metadata without running extraction by itself unless explicitly routed through the extraction contract. |
| FR-238 | WP014 shall expose controlled solution registration APIs. |
| FR-239 | Solution registration shall associate solution paths with registered repository context. |
| FR-240 | Repository and solution management APIs shall validate path shape and policy constraints consistently with extraction-path validation rules where applicable. |
| FR-241 | WP014 shall expose metadata management APIs for supported repository, solution, snapshot, rule, or operational metadata. |
| FR-242 | Metadata management shall be limited to approved metadata fields and shall not permit arbitrary graph mutation. |
| FR-243 | Management operations shall produce auditable result metadata where audit infrastructure exists. |

### 4.20 Snapshot Lifecycle, Retention, Extraction Run History, and Maintenance

| ID | Requirement |
| --- | --- |
| FR-244 | WP014 shall expose snapshot lifecycle query APIs. |
| FR-245 | Snapshot lifecycle queries shall list snapshots by repository, solution, status, date range, and optional commit metadata where available. |
| FR-246 | Snapshot records shall include stable identity, repository, solution, analysis date/time, extraction status, warnings or errors summary, and metadata where available. |
| FR-247 | WP014 shall expose controlled retention APIs. |
| FR-248 | Retention APIs shall support configured retention behavior without deleting data outside the intended snapshot lifecycle scope. |
| FR-249 | Retention APIs shall validate requests before performing maintenance actions. |
| FR-250 | WP014 shall expose extraction run history APIs. |
| FR-251 | Extraction run history shall include request metadata, status, start/end timestamps, summary counts, warnings, errors, and produced snapshot identity where available. |
| FR-252 | WP014 shall expose controlled maintenance operations required by the work-package scope. |
| FR-253 | Maintenance operations shall not expose arbitrary database mutation. |
| FR-254 | Maintenance operations shall return explicit outcomes, warnings, errors, and audit metadata where available. |

### 4.21 Rule Enablement and Catalog Management

| ID | Requirement |
| --- | --- |
| FR-255 | WP014 shall expose rule catalog visibility through controlled APIs. |
| FR-256 | WP014 shall expose rule enablement controls where enablement state is supported by the rule model. |
| FR-257 | Rule enablement controls shall validate rule code and version. |
| FR-258 | Rule enablement controls shall not edit rule definitions stored on disk. |
| FR-259 | Rule enablement controls shall preserve audit-ready metadata about who or what changed enablement state where identity information is available. |
| FR-260 | Rule enablement state changes shall be visible in later rule and finding query responses where applicable. |

### 4.22 Health and Readiness

| ID | Requirement |
| --- | --- |
| FR-261 | WP014 shall expose health endpoints for the API host. |
| FR-262 | WP014 shall expose readiness endpoints for query and management dependencies. |
| FR-263 | Readiness shall include Neo4j query dependency status where the dependency is required for query operation. |
| FR-264 | Readiness shall include rule-loading or rule-catalog status where rule queries or finding behavior depend on it. |
| FR-265 | Health and readiness responses shall avoid exposing secrets or sensitive infrastructure details. |
| FR-266 | Health and readiness behavior shall remain suitable for automated monitoring. |

## 5. Non-Functional Requirements

### 5.1 Determinism and Evidence

| ID | Requirement |
| --- | --- |
| NFR-001 | API responses shall be deterministic for the same persisted snapshot, request parameters, and permissions. |
| NFR-002 | Public API contracts shall use stable keys and normalized snapshot identities rather than database IDs. |
| NFR-003 | Evidence-backed claims shall include direct or indirect evidence references where available. |
| NFR-004 | Derived claims shall identify enough source context to explain the derivation. |
| NFR-005 | Unknowns shall be represented explicitly rather than hidden or converted to false negatives. |

### 5.2 Security and Safety

| ID | Requirement |
| --- | --- |
| NFR-006 | Query APIs shall not expose secrets, credentials, tokens, connection string values, or sensitive local environment details. |
| NFR-007 | Management APIs shall be controlled and shall not provide arbitrary graph, database, filesystem, shell, or code mutation. |
| NFR-008 | APIs shall include authentication and authorization seams where repository host standards require them, even if local development defaults are permissive. |
| NFR-009 | Authorization-sensitive decisions shall be centralized rather than duplicated inconsistently across endpoints. |
| NFR-010 | Validation failures shall return safe client-facing messages. |
| NFR-011 | API responses intended for later AI/MCP consumption shall avoid prompt-injection amplification by treating evidence snippets and source text as untrusted content. |

### 5.3 Performance and Scalability

| ID | Requirement |
| --- | --- |
| NFR-012 | List and graph traversal APIs shall enforce pagination, maximum page size, maximum traversal depth, and response-size limits. |
| NFR-013 | Query implementations shall avoid loading entire repositories or full graph estates into memory for ordinary requests. |
| NFR-014 | Expensive traversal and diff queries shall use bounded execution strategies and return truncation metadata where limits are reached. |
| NFR-015 | Query APIs shall be cancellable through standard request cancellation behavior. |
| NFR-016 | Query APIs shall be designed for realistic large .NET solution estates. |

### 5.4 Compatibility and Contract Stability

| ID | Requirement |
| --- | --- |
| NFR-017 | API DTOs shall be stable enough for MCP tools and automation clients to depend on. |
| NFR-018 | API DTOs shall not leak Neo4j labels, relationship names, or query implementation details unless those names are intentionally part of the Archon domain contract. |
| NFR-019 | API versioning or compatibility strategy shall be documented where repository standards require it. |
| NFR-020 | Contract changes introduced during implementation shall be reflected in the WP014 specification, implementation plan, and repository documentation. |

### 5.5 Observability and Operations

| ID | Requirement |
| --- | --- |
| NFR-021 | Query and management operations shall emit structured logs through repository-standard logging abstractions. |
| NFR-022 | Management operations shall produce audit-ready metadata where supported by the platform. |
| NFR-023 | Error handling shall preserve enough diagnostic context for operators while avoiding sensitive details in client responses. |
| NFR-024 | Health and readiness endpoints shall be suitable for local development and automated monitoring. |

### 5.6 Documentation Quality

| ID | Requirement |
| --- | --- |
| NFR-025 | Implementation work for WP014 shall follow `.github/instructions/documentation-pass.instructions.md` as mandatory guidance. |
| NFR-026 | Public API surface introduced or modified by WP014 shall receive explicit local XML documentation where C# supports it. |
| NFR-027 | Internal and other non-public types introduced or modified by WP014 shall receive the same developer-level documentation standard as public types. |
| NFR-028 | Constructors and methods on internal or non-public types introduced or modified by WP014 shall receive explicit local documentation treatment and explanatory comments consistent with the documentation-pass instruction file. |
| NFR-029 | Repository wiki or contributor documentation shall be updated when WP014 implementation creates contributor-facing guidance about query APIs, management APIs, validation workflows, or operational behavior. |

## 6. API Contract Expectations

### 6.1 Contract Families

WP014 implementation shall define stable API contracts for these endpoint families:

| Family | Expected Capability |
| --- | --- |
| Dashboard summary | Repository, solution, snapshot, counts, top hotspots, latest changes. |
| Projects | Catalogue, detail, references, dependents, packages, responsibilities, evidence, unknowns. |
| Dependencies and graph | Direct traversal, transitive traversal, dependency paths, neighbourhoods, scoped graph result sets. |
| Symbols | Lookup, detail, usage, unresolved or partial symbol information. |
| Runtime | Endpoints, controllers, handlers, hosted services, background services, workers, queues/topics, scheduled jobs. |
| Data access | LINQ to SQL, EF Classic/EF6, EF Core, ADO.NET, typed DataSets, raw SQL, stored procedures, entities, tables, usage sites. |
| Configuration | Configuration keys, usage sites, provider metadata, binding context, safe metadata. |
| Integrations | External dependencies, client usage, protocols, target metadata, configuration references. |
| UI-technology facts | .NET UI application, component, view, route, binding, command, and navigation facts for API/MCP consumers. |
| Evidence | Evidence lookup by evidence stable key or related graph record. |
| Rules and findings | Rule catalog, rule enablement, findings, hotlist reports, suppression metadata where available. |
| Metrics and hotspots | Project metrics, graph metrics, modernization metrics, hotspots, cycles, architecture-rule results. |
| Snapshot diff | Node, edge, finding, and metric diff between snapshots. |
| Search | Cross-domain architecture fact search suitable for MCP. |
| Management | Repository registration, solution registration, metadata management, snapshot lifecycle, retention, extraction run history, controlled maintenance. |
| Operations | Health and readiness. |

### 6.2 Response Envelope

Responses should use a consistent envelope where practical. The envelope should support:

- result data;
- snapshot identity;
- repository and solution scope when applicable;
- pagination metadata for list responses;
- applied filters and sort metadata where useful;
- response-size limit and truncation metadata;
- confidence and unknown summaries where applicable;
- warnings for partial or degraded results;
- correlation or request metadata where repository standards support it.

### 6.3 Error Contract

The API error contract shall support:

- validation errors for invalid shape, invalid stable keys, unsupported filters, excessive depth, excessive page size, and invalid snapshot combinations;
- not-found errors for missing repositories, solutions, snapshots, nodes, edges, symbols, evidence, rules, findings, metrics, or generated outputs;
- conflict errors for ambiguous name lookup or unsafe management operation state;
- unauthorized and forbidden errors where authentication and authorization are active;
- safe server errors for unexpected failures.

### 6.4 MCP Suitability

DTOs and response metadata shall be shaped so MCP implementation in WP015 can consume the API without custom database access. This includes preserving evidence, confidence, facts, findings, unknowns, stable keys, pagination, truncation, and suggested follow-up affordances in a structured form.

## 7. Data and Persistence Requirements

### 7.1 Source of Truth

| ID | Requirement |
| --- | --- |
| DR-001 | Neo4j shall remain the system of record for extraction output. |
| DR-002 | Query APIs shall use persisted graph facts, findings, metrics, rules, and generated summary metadata where available. |
| DR-003 | Query APIs shall not create a second authoritative store for architecture facts. |
| DR-004 | Any cache introduced for performance shall be invalidated or scoped so it cannot return data for the wrong snapshot. |
| DR-005 | Query APIs shall use stable keys and fingerprints produced by earlier work packages. |

### 7.2 Snapshot Selection

| ID | Requirement |
| --- | --- |
| DR-006 | Snapshot-scoped queries shall require either an explicit snapshot identity or a well-defined latest/current snapshot selector. |
| DR-007 | Latest/current snapshot resolution shall be deterministic within repository and solution scope. |
| DR-008 | Snapshot diff queries shall validate that both snapshots exist and are comparable. |
| DR-009 | Responses shall disclose the snapshot or snapshots used. |

### 7.3 Evidence and Snippets

| ID | Requirement |
| --- | --- |
| DR-010 | Evidence lookup shall return persisted evidence metadata. |
| DR-011 | Snippet preview shall be limited to persisted or safely retrievable evidence context. |
| DR-012 | Snippet preview shall apply response-size bounds. |
| DR-013 | Evidence responses shall not expose source content outside the intended evidence range unless explicitly designed and tested. |

## 8. Validation and Testing Requirements

### 8.1 Test Coverage

| ID | Requirement |
| --- | --- |
| TR-001 | Tests shall cover dashboard summary queries. |
| TR-002 | Tests shall cover project catalogue queries. |
| TR-003 | Tests shall cover project detail queries. |
| TR-004 | Tests shall cover dependency and dependent queries. |
| TR-005 | Tests shall cover transitive traversal queries. |
| TR-006 | Tests shall cover dependency path queries. |
| TR-007 | Tests shall cover graph-neighbourhood depth, edge-kind, direction, and limit behavior. |
| TR-008 | Tests shall cover symbol lookup and symbol usage queries. |
| TR-009 | Tests shall cover endpoint and controller or handler queries. |
| TR-010 | Tests shall cover worker, hosted-service, background-service, queue/topic, and scheduled-job queries. |
| TR-011 | Tests shall cover data-access queries for each persisted data-access family available from earlier work packages. |
| TR-012 | Tests shall cover configuration usage queries and secret-safety behavior. |
| TR-013 | Tests shall cover external integration queries and secret-safety behavior. |
| TR-014 | Tests shall cover .NET UI-technology fact queries without introducing Archon Discovery UI. |
| TR-015 | Tests shall cover evidence lookup and evidence relationship traversal. |
| TR-016 | Tests shall cover rule catalog and rule enablement APIs. |
| TR-017 | Tests shall cover finding and hotlist query APIs. |
| TR-018 | Tests shall cover metric, hotspot, architecture-rule result, and cycle query APIs. |
| TR-019 | Tests shall cover snapshot diff APIs across nodes, edges, findings, and metrics. |
| TR-020 | Tests shall cover search and MCP-oriented query suitability. |
| TR-021 | Tests shall cover repository and solution registration APIs. |
| TR-022 | Tests shall cover metadata management APIs. |
| TR-023 | Tests shall cover snapshot lifecycle and retention APIs. |
| TR-024 | Tests shall cover extraction run history APIs. |
| TR-025 | Tests shall cover controlled maintenance APIs. |
| TR-026 | Tests shall cover health and readiness endpoints. |
| TR-027 | Tests shall cover pagination, sorting, filtering, and response-size truncation. |
| TR-028 | Tests shall cover validation, not-found, conflict, authorization seam, and safe server-error responses. |
| TR-029 | Tests shall cover unknown, confidence, and partial-result warning behavior. |
| TR-030 | Tests shall verify MCP does not need direct Neo4j access for WP015 query dependencies. |

### 8.2 Test Style Expectations

Testing shall use the existing repository test structure and conventions. Test projects created or used for WP014 shall preserve Onion Architecture boundaries. Where API behavior is tested, tests should prefer stable contract-level assertions over implementation-detail assertions. Where persistence is involved, tests shall use existing graph repository test patterns and shall not depend on Neo4j internal IDs in public assertions.

### 8.3 Validation Commands

The WP014 implementation plan shall define final validation commands appropriate for the changed projects. Because this specification is documentation-only, no build or test execution is required at specification creation time.

## 9. Documentation Requirements

### 9.1 Specification and Plan

| ID | Requirement |
| --- | --- |
| DOC-001 | The WP014 specification shall live under `docs/014-Query-API-Product-Surface/`. |
| DOC-002 | WP014 shall use a single markdown specification document for the work-package requirements. |
| DOC-003 | Any WP014 implementation plan shall live under the same work-package folder. |
| DOC-004 | The implementation plan shall reference this specification and the source work-package entry. |
| DOC-005 | The implementation plan shall explicitly require `.github/instructions/documentation-pass.instructions.md`. |

### 9.2 Contributor Documentation

| ID | Requirement |
| --- | --- |
| DOC-006 | Contributor-facing documentation shall be updated to describe query API contract families when implementation creates or changes them. |
| DOC-007 | Contributor-facing documentation shall be updated to describe management API safety boundaries. |
| DOC-008 | Contributor-facing documentation shall be updated to describe health, readiness, retention, extraction run history, and maintenance workflows where implemented. |
| DOC-009 | Documentation shall avoid duplicating the specification as long-form implementation notes. Current-state contributor guidance belongs in the repository wiki according to repository instructions. |
| DOC-010 | Documentation links to repository wiki pages shall use proper markdown links. |

### 9.3 API Documentation

| ID | Requirement |
| --- | --- |
| DOC-011 | API documentation shall describe endpoint families, request parameters, response envelopes, pagination, filtering, sorting, error contracts, and security expectations. |
| DOC-012 | API documentation shall not use Swagger UI if repository guidance continues to require Scalar instead. |
| DOC-013 | API documentation shall include examples only when they can be kept synchronized with implementation. |

## 10. Assumptions, Constraints, and Decisions

### 10.1 Assumptions

| ID | Assumption |
| --- | --- |
| AS-001 | WP001 through WP013 capabilities exist or are completed before WP014 implementation begins, because WP014 exposes their persisted outputs. |
| AS-002 | Neo4j remains the authoritative persistence mechanism for extraction output. |
| AS-003 | Stable keys and fingerprints are already defined by earlier work packages and will be reused rather than redesigned. |
| AS-004 | API authentication and authorization seams exist or are introduced consistently with repository host standards. |
| AS-005 | Markdown export generation belongs to WP016, but WP014 may expose access surfaces or placeholders only where backed by generated export artifacts available at runtime. |
| AS-006 | MCP implementation belongs to WP015, but WP014 must provide complete query capability needed by MCP. |

### 10.2 Constraints

| ID | Constraint |
| --- | --- |
| CON-001 | No Archon Discovery UI implementation shall be introduced by WP014. |
| CON-002 | Query APIs shall not expose raw Neo4j query execution to callers. |
| CON-003 | Management APIs shall not provide arbitrary filesystem, database, shell, or code mutation. |
| CON-004 | API responses shall not expose secrets or unsafe evidence expansion. |
| CON-005 | Query behavior shall preserve evidence, confidence, and explicit unknowns. |
| CON-006 | Implementation shall preserve Onion Architecture dependency direction. |
| CON-007 | Production projects shall remain under `./src` and test projects under `./test`. |
| CON-008 | C# code introduced during implementation shall use block-scoped namespaces, Allman braces, one public type per file, and underscore-prefixed private fields. |

### 10.3 Decisions

| ID | Decision |
| --- | --- |
| DEC-001 | Section 28 UI information needs from the source brief are implemented as API response capabilities, not as Archon Discovery UI screens. |
| DEC-002 | WP014 exposes query and controlled management capabilities; MCP tools, resources, prompts, and MCP-specific security are implemented in WP015. |
| DEC-003 | Snapshot diff API behavior uses stable keys and normalized fingerprints consistently with WP013 and Appendix E section E.5.9. |
| DEC-004 | Query contracts are optimized for deterministic API and MCP consumption rather than visual graph rendering. |

## 11. Risks and Mitigations

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Query surface becomes too broad for a coherent API contract. | Inconsistent endpoints and hard-to-test behavior. | Group endpoints by contract family and enforce common envelope, pagination, filtering, and error patterns. |
| API leaks persistence details. | Clients become coupled to Neo4j implementation. | Map graph results to domain DTOs using stable keys and intentional domain names only. |
| Large graph queries return unusable responses. | Poor performance and MCP response-size failures. | Enforce default depth 1, maximum depth, pagination, truncation metadata, and scoped filters. |
| Evidence snippets expose sensitive information. | Security and privacy issues. | Return bounded snippets, avoid secret values, and treat source evidence as untrusted content. |
| Management APIs become arbitrary mutation surfaces. | Data corruption or security exposure. | Restrict management commands to explicit product operations with validation and audit metadata. |
| MCP implementation discovers missing query capabilities in WP015. | Rework and delayed MCP delivery. | Validate WP014 against every MCP tool data need from source brief section 29.2. |
| UI-source requirements are misread as UI implementation. | Violates API-first sequence. | Treat section 28 as non-visual information requirements only and explicitly test absence of Discovery UI additions where relevant. |

## 12. Acceptance Criteria

WP014 is complete when all of the following are true:

1. Every non-visual information need listed in `docs/foundation/archon_full_concept_brief.md` sections 28.1 through 28.11 is available through API responses where it is not inherently visual.
2. Project catalogue, project detail, dependency traversal, dependents, dependency paths, graph neighbourhoods, symbol lookup, symbol usage, endpoint lookup, worker lookup, UI-technology fact lookup, data-access lookup, configuration usage, integration lookup, evidence drill-down, hotlist reports, rules, findings, metrics, hotspots, architecture-rule results, and snapshot diff APIs are implemented and tested.
3. Repository registration, solution registration, metadata management, snapshot lifecycle, retention controls, rule catalog visibility, rule enablement controls, extraction run history, health, readiness, and controlled maintenance operations are implemented and tested.
4. MCP can consume all required query capabilities for WP015 without directly querying Neo4j.
5. API contracts use stable keys and snapshot identities rather than Neo4j internal IDs.
6. API responses include evidence, confidence, unknowns, warnings, pagination, filtering, and truncation metadata where applicable.
7. Management operations are controlled product operations and do not expose arbitrary graph, database, filesystem, shell, or code mutation.
8. Tests cover every query family, filtering, pagination, sorting, response-size limits, authorization seams if present, management operations, retention behavior, health/readiness, and error responses.
9. Documentation is updated according to repository documentation rules, including documentation-pass expectations for public, internal, and non-public implementation types.
10. No Archon Discovery UI implementation, UI page, UI component, or front-end asset work is introduced by WP014.

## 13. Traceability Matrix

| Source Requirement | WP014 Coverage |
| --- | --- |
| `work-packages.md` WP014 project catalogue and project detail queries | FR-045 through FR-079, TR-002, TR-003 |
| `work-packages.md` WP014 dependency, dependent, transitive, and path queries | FR-080 through FR-094, TR-004 through TR-007 |
| `work-packages.md` WP014 symbol lookup and usage queries | FR-095 through FR-107, TR-008 |
| `work-packages.md` WP014 endpoint, controller, worker, hosted-service, queue/topic, configuration, integration, data-access, and UI-technology queries | FR-108 through FR-169, TR-009 through TR-014 |
| `work-packages.md` WP014 evidence drill-down | FR-170 through FR-182, TR-015 |
| `work-packages.md` WP014 hotlist, rule, finding, metric, hotspot, and snapshot diff queries | FR-183 through FR-224, TR-016 through TR-019 |
| `work-packages.md` WP014 management operations | FR-236 through FR-260, TR-021 through TR-025 |
| `work-packages.md` WP014 health, readiness, and controlled maintenance | FR-244 through FR-266, TR-023 through TR-026 |
| `work-packages.md` WP014 response-size limits, pagination, filtering, stable DTO contracts | FR-013 through FR-027, NFR-012 through NFR-020, TR-027 |
| Source brief section 8.1 API host, query module, and management module | FR-001 through FR-012, FR-236 through FR-266 |
| Source brief sections 28.1 through 28.11 non-visual information needs | FR-028 through FR-235 |
| Source brief section 29.2 MCP tools depend on query capability | FR-225 through FR-235, TR-030 |
| Source brief section 31 markdown export access | AS-005 and API contract planning for generated outputs where available at runtime |
| Source brief Appendix E section E.5.8 query model | FR-045 through FR-224 |
| Source brief Appendix E section E.5.9 diff strategy | FR-203 through FR-224 |
| Source brief Appendix E section E.9 acceptance criteria | DR-001 through DR-013, acceptance criteria 1 through 10 |
| `.github/instructions/documentation-pass.instructions.md` internal and public documentation standards | NFR-025 through NFR-029, DOC-005 |

## 14. Change Log

| Date | Change |
| --- | --- |
| 2026-05-24 | Initial WP014 single-document specification created from `docs/foundation/work-packages.md` WP014 and relevant source brief sections. |
