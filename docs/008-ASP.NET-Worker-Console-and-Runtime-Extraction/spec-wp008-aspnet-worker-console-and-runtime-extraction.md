# WP008 Specification - ASP.NET, Worker, Console, and Runtime Extraction

## Document Control

| Field | Value |
| --- | --- |
| Work Package | WP008 - ASP.NET, Worker, Console, and Runtime Extraction |
| Output Path | `docs/008-ASP.NET-Worker-Console-and-Runtime-Extraction/spec-wp008-aspnet-worker-console-and-runtime-extraction.md` |
| Source Work Package | `docs/foundation/work-packages.md` WP008 |
| Source Brief | `docs/foundation/archon_full_concept_brief.md` |
| Specification Basis | `spec-template_v1.1.md` requested; template file was not present in the workspace at creation time, so this document follows the repository specification structure and documented work-package rules. |
| Status | Draft |
| Audience | Product owner, architect, implementation team, test engineer |

## 1. Overview

### 1.1 Purpose

This specification defines the requirements for WP008, the Archon work package that extracts runtime-facing application facts from analyzed .NET repositories. The package identifies ASP.NET Core applications, classic ASP.NET applications, console applications, worker services, hosted services, scheduled workloads, queue consumers, message handlers, Windows-service-style hosts, and runtime entry points.

WP008 builds on the prior project, package, semantic symbol, configuration, dependency-injection, snapshot orchestration, and Neo4j persistence foundations. It must contribute evidence-backed runtime nodes, relationships, metadata, confidence, warnings, and unknowns through the established extraction pipeline rather than introducing a separate persistence or query path.

### 1.2 Background

Archon provides deterministic, evidence-backed architecture intelligence for modern and legacy .NET estates. Runtime extraction is central to that mission because it reveals how applications start, what HTTP surfaces they expose, which background processes run, which queues or topics they handle, and where execution enters the system.

The controlling work-package sequence is API-first and MCP-first. WP008 therefore focuses on backend extraction and graph population only. Human-facing dashboards, explorer pages, graph views, endpoint viewers, worker viewers, and other Archon Discovery UI surfaces remain excluded.

### 1.3 High-Level Scope

WP008 covers these runtime extraction areas:

- ASP.NET Core host and endpoint extraction.
- ASP.NET Core controller, route, filter, middleware, authorization, MVC, and OpenAPI setup extraction.
- Classic ASP.NET, Web Forms, MVC 5, Web API 2, handler, module, `Global.asax`, and route-configuration extraction.
- Console entry-point and top-level statement extraction in analyzed target repositories.
- Worker service, hosted service, background service, scheduled job, queue consumer, and message handler extraction.
- Windows-service-style hosting, Topshelf-style hosting, and custom host-loop detection.
- Runtime node, relationship, metadata, evidence, confidence, warning, and unknown emission.
- Tests for all production behavior introduced by this work package.
- Documentation updates explaining supported runtime extraction behavior and validation.

WP008 excludes Archon Discovery UI, data-access extraction, broad external-integration extraction beyond runtime consumer facts required by this package, rule-engine evaluation, API query product surface expansion, MCP tools, markdown export, snapshot diff, and direct Neo4j writes from extractor projects.

## 2. System Context

### 2.1 Product Context

Archon accepts API-triggered extraction requests, loads submitted repositories and explicit solution paths, extracts deterministic architecture facts, persists them in Neo4j, and later exposes them through API and MCP surfaces. WP008 contributes the runtime-facing application slice of the architecture graph.

The package must use the single extraction orchestration path created earlier in the sequence. It must not scan arbitrary directories independently of the submitted extraction request, bypass the snapshot contract, execute analyzed repository code, or persist data directly outside the established graph persistence adapter.

### 2.2 Source References

WP008 must align with these source materials:

- `docs/foundation/work-packages.md` WP008 objective, required implementation, and completion criteria.
- `docs/foundation/archon_full_concept_brief.md` section 18 for ASP.NET Core and classic ASP.NET extraction.
- `docs/foundation/archon_full_concept_brief.md` section 20 for worker and console extraction.
- `docs/foundation/archon_full_concept_brief.md` section 24 for integrations that overlap with runtime consumers.
- `docs/foundation/archon_full_concept_brief.md` section 35 phase 4 for application and runtime discovery.
- `docs/foundation/archon_full_concept_brief.md` Appendix E sections E.6.3 and E.7.3 for runtime node, edge, metadata, evidence, and persistence support.
- `docs/foundation/work-packages.md` completion rules for evidence-backed facts, explicit unknowns, deterministic stable keys, Neo4j as the system of record, tests, documentation, and no Discovery UI.

### 2.3 Users and Stakeholders

| Stakeholder | Interest |
| --- | --- |
| Product owner | Confirms runtime extraction satisfies WP008 scope without UI delivery or deferred mandatory behavior. |
| Architect | Confirms runtime applications, endpoints, workers, queues, and startup patterns are represented consistently in the graph. |
| Developer | Uses extracted facts to understand application entry points, HTTP surfaces, hosted services, workers, and message consumers. |
| Test engineer | Verifies detection coverage, evidence quality, confidence, unknown handling, and extraction-pipeline integration. |
| Future API consumer | Depends on persisted runtime facts being complete enough for query APIs in later work packages. |
| Future MCP consumer | Depends on evidence-backed runtime facts for impact analysis and Copilot workflows in later work packages. |

## 3. Component Summary

### 3.1 ASP.NET Core Runtime Extractor

The ASP.NET Core runtime extractor detects modern application bootstrap patterns, minimal API mappings, endpoint route builder usage, controllers, route attributes, authorization attributes, filters, middleware registrations, MVC setup, and OpenAPI setup. It contributes endpoint, controller, route, middleware, authorization, and runtime metadata facts to the shared snapshot accumulator.

### 3.2 Classic ASP.NET Runtime Extractor

The classic ASP.NET runtime extractor detects `System.Web` usage, `Global.asax`, Web Forms pages, code-behind files, HTTP handlers, HTTP modules, `web.config`, MVC 5 controllers, Web API 2 controllers, and route configuration. It contributes classic runtime facts with evidence and confidence appropriate to source artifacts and symbol resolution.

### 3.3 Worker and Console Runtime Extractor

The worker and console runtime extractor detects console entry points, `static Main`, top-level statements, `IHostedService`, `BackgroundService`, worker-service projects, scheduled jobs, queue consumers, message handlers, Windows-service-style hosting, Topshelf-style services, and custom host loops. It contributes runtime entry-point, hosted-service, worker, queue/topic handler, scheduling, and consumer facts.

### 3.4 Runtime Evidence and Graph Integration

WP008 uses Roslyn semantic outputs, repository file artifacts, configuration facts, and dependency-injection facts from earlier work packages. It must emit facts through the established extraction snapshot contract. The Neo4j persistence adapter remains the only persistence path, and extractors must not invent facts that cannot be tied to evidence or represented as explicit unknowns.

## 4. Functional Requirements

### 4.1 Extraction Pipeline Participation

| ID | Requirement |
| --- | --- |
| FR-001 | WP008 shall register runtime extractors with the existing extraction orchestration path. |
| FR-002 | WP008 extractors shall run only as part of an API-triggered extraction using a repository root directory and explicit solution path list. |
| FR-003 | WP008 extractors shall consume repository, solution, project, semantic symbol, configuration, dependency-injection, and file artifact context produced by earlier extraction stages. |
| FR-004 | WP008 extractors shall contribute nodes, relationships, evidence, metadata, warnings, and errors to the shared snapshot accumulator. |
| FR-005 | WP008 extractors shall not persist directly to Neo4j, write sidecar extraction files, or introduce an alternate storage model. |
| FR-006 | WP008 output shall be snapshot-scoped and compatible with deterministic stable keys and fingerprints established by prior work packages. |

### 4.2 ASP.NET Core Host Detection

| ID | Requirement |
| --- | --- |
| FR-007 | The extractor shall detect ASP.NET Core applications through project metadata, package references, SDK indicators, host setup code, and source artifacts where evidence exists. |
| FR-008 | The extractor shall detect `Program.cs` startup files. |
| FR-009 | The extractor shall detect `Startup.cs` classes and startup methods. |
| FR-010 | The extractor shall detect `WebApplication.CreateBuilder` usage. |
| FR-011 | The extractor shall detect `Host.CreateDefaultBuilder` and generic-host patterns used for web applications. |
| FR-012 | The extractor shall detect `ConfigureWebHostDefaults` and related web host builder patterns. |
| FR-013 | The extractor shall detect ASP.NET Core application startup even when startup logic is split across extension methods, where source is available. |
| FR-014 | The extractor shall classify partial or ambiguous ASP.NET Core startup evidence with explicit confidence and unknown reason metadata. |

### 4.3 ASP.NET Core Minimal API and Endpoint Mapping

| ID | Requirement |
| --- | --- |
| FR-015 | The extractor shall detect minimal API mappings created with `MapGet`. |
| FR-016 | The extractor shall detect minimal API mappings created with `MapPost`. |
| FR-017 | The extractor shall detect minimal API mappings created with `MapPut`. |
| FR-018 | The extractor shall detect minimal API mappings created with `MapDelete`. |
| FR-019 | The extractor shall detect other common endpoint mapping methods such as `MapPatch`, `MapMethods`, `MapGroup`, and `MapFallback` where symbol or syntax evidence supports detection. |
| FR-020 | The extractor shall capture route template metadata for mapped endpoints where the route can be determined. |
| FR-021 | The extractor shall capture HTTP method metadata for mapped endpoints where the method can be determined. |
| FR-022 | The extractor shall link endpoint facts to handler methods, lambdas, local functions, or containing source locations where available. |
| FR-023 | The extractor shall represent dynamic or computed route templates as explicit unknowns with available string-part evidence. |
| FR-024 | The extractor shall preserve endpoint group metadata where `MapGroup` or equivalent grouping patterns are detected. |

### 4.4 ASP.NET Core Controllers, Routing, and MVC

| ID | Requirement |
| --- | --- |
| FR-025 | The extractor shall detect ASP.NET Core controllers. |
| FR-026 | The extractor shall detect controller action methods. |
| FR-027 | The extractor shall detect route attributes on controllers and actions. |
| FR-028 | The extractor shall detect HTTP verb attributes on action methods. |
| FR-029 | The extractor shall combine controller-level and action-level route metadata where deterministic combination is possible. |
| FR-030 | The extractor shall detect `IEndpointRouteBuilder` usage. |
| FR-031 | The extractor shall detect MVC setup calls such as `AddControllers`, `AddControllersWithViews`, `AddMvc`, and `MapControllers`. |
| FR-032 | The extractor shall detect conventional route mapping where deterministic route patterns are available. |
| FR-033 | The extractor shall preserve controller, action, route, HTTP method, project, and evidence metadata. |

### 4.5 ASP.NET Core Authorization, Filters, Middleware, and OpenAPI

| ID | Requirement |
| --- | --- |
| FR-034 | The extractor shall detect authorization attributes on controllers, action methods, minimal API handlers, or endpoint groups. |
| FR-035 | The extractor shall detect anonymous access attributes where present. |
| FR-036 | The extractor shall detect filter attributes and filter registration patterns where supported by symbol analysis. |
| FR-037 | The extractor shall detect middleware registrations including `UseMiddleware<T>`, common `Use*` middleware calls, and custom middleware types where source evidence exists. |
| FR-038 | The extractor shall detect endpoint pipeline calls that influence routing, authentication, authorization, CORS, static files, exception handling, and health checks where evidence exists. |
| FR-039 | The extractor shall detect OpenAPI or Swagger setup patterns, including service registration and middleware usage where evidence exists. |
| FR-040 | The extractor shall capture middleware ordering metadata where it can be derived from source order. |
| FR-041 | The extractor shall represent middleware or filter targets that cannot be resolved as explicit unknowns rather than inventing target names. |

### 4.6 Classic ASP.NET Application Detection

| ID | Requirement |
| --- | --- |
| FR-042 | The extractor shall detect classic ASP.NET applications through `System.Web` references, project metadata, package references, configuration artifacts, and source artifacts where evidence exists. |
| FR-043 | The extractor shall detect `Global.asax` files. |
| FR-044 | The extractor shall detect `Global.asax` code-behind files and lifecycle methods where source is available. |
| FR-045 | The extractor shall detect `web.config` as a classic ASP.NET runtime artifact. |
| FR-046 | The extractor shall detect ASP.NET application, session, request, error, and route registration lifecycle hooks where evidence exists. |
| FR-047 | The extractor shall represent partial classic ASP.NET evidence with confidence and unknown reason metadata. |

### 4.7 Web Forms, HTTP Handlers, and HTTP Modules

| ID | Requirement |
| --- | --- |
| FR-048 | The extractor shall detect Web Forms `.aspx` pages. |
| FR-049 | The extractor shall detect Web Forms code-behind files and page classes where evidence exists. |
| FR-050 | The extractor shall detect `.ascx` user controls where they participate in runtime-facing Web Forms behavior. |
| FR-051 | The extractor shall detect HTTP handlers from source code, configuration, or file artifacts where evidence exists. |
| FR-052 | The extractor shall detect HTTP modules from source code or configuration where evidence exists. |
| FR-053 | The extractor shall capture route, virtual path, handler type, module type, and configuration metadata where available. |
| FR-054 | The extractor shall preserve evidence from markup, code-behind, configuration, and project files. |

### 4.8 MVC 5 and Web API 2 Detection

| ID | Requirement |
| --- | --- |
| FR-055 | The extractor shall detect MVC 5 controllers. |
| FR-056 | The extractor shall detect Web API 2 controllers. |
| FR-057 | The extractor shall detect controller action methods and HTTP verb attributes in classic ASP.NET MVC/Web API projects. |
| FR-058 | The extractor shall detect route configuration patterns such as route tables, attribute routing setup, and Web API route registration where evidence exists. |
| FR-059 | The extractor shall capture route templates, controller names, action names, HTTP methods, and project metadata where deterministic extraction is possible. |
| FR-060 | The extractor shall use explicit unknowns for convention-based or dynamic routes that cannot be resolved deterministically. |

### 4.9 Console Entry Point Detection

| ID | Requirement |
| --- | --- |
| FR-061 | The extractor shall detect console application projects from output type, SDK metadata, source structure, and entry-point symbols where evidence exists. |
| FR-062 | The extractor shall detect `static Main` entry points in C# and VB.NET where Roslyn supports symbol resolution. |
| FR-063 | The extractor shall detect top-level statements in analyzed target repositories. |
| FR-064 | The extractor shall distinguish target-repository top-level statements from Archon host code and test harness code by using submitted extraction context. |
| FR-065 | The extractor shall capture entry-point method, containing type where applicable, source file, project, target framework, and evidence metadata. |
| FR-066 | The extractor shall represent ambiguous or generated entry points with confidence and unknown reason metadata. |

### 4.10 Worker Service and Hosted Service Detection

| ID | Requirement |
| --- | --- |
| FR-067 | The extractor shall detect worker service projects. |
| FR-068 | The extractor shall detect implementations of `IHostedService` where symbol resolution is available. |
| FR-069 | The extractor shall detect classes derived from `BackgroundService` where symbol resolution is available. |
| FR-070 | The extractor shall correlate hosted-service runtime facts with `AddHostedService<T>()` and related registration facts emitted by WP007 where available. |
| FR-071 | The extractor shall detect host builder setup for worker services. |
| FR-072 | The extractor shall capture hosted-service type, implementation type, registration source, execution method, project, and evidence metadata. |
| FR-073 | The extractor shall emit hosted-service facts even when registration is not found, when source evidence proves the runtime role. |

### 4.11 Scheduled Job Detection

| ID | Requirement |
| --- | --- |
| FR-074 | The extractor shall detect scheduled job patterns where evidence exists in source, configuration, package references, or known scheduler APIs. |
| FR-075 | The extractor shall detect recurring job registration patterns for common scheduler libraries where source evidence exists. |
| FR-076 | The extractor shall capture schedule expression, job type, job method, scheduler technology, project, and evidence where deterministic extraction is possible. |
| FR-077 | The extractor shall redact or avoid persisting sensitive configuration values used by scheduler setup. |
| FR-078 | The extractor shall represent computed schedules or unresolved job targets as explicit unknowns with available evidence. |

### 4.12 Queue Consumer and Message Handler Detection

| ID | Requirement |
| --- | --- |
| FR-079 | The extractor shall detect queue consumers where source, dependency-injection, configuration, or known messaging API evidence exists. |
| FR-080 | The extractor shall detect topic or subscription consumers where evidence exists. |
| FR-081 | The extractor shall detect message handler classes and handler methods where symbol or naming evidence exists. |
| FR-082 | The extractor shall capture queue name, topic name, subscription name, handler type, handler method, transport hint, configuration key, project, and evidence where available. |
| FR-083 | The extractor shall reuse or emit `Queue` and `Topic` nodes as required by the graph model. |
| FR-084 | The extractor shall emit `HANDLES` relationships from handler/runtime nodes to queue or topic nodes where evidence supports the relationship. |
| FR-085 | The extractor shall represent unknown queue or topic names explicitly when names are computed, configuration-driven, or otherwise unresolved. |

### 4.13 Windows-Service-Style and Custom Host Loop Detection

| ID | Requirement |
| --- | --- |
| FR-086 | The extractor shall detect Windows-service-style hosting where source, project metadata, package references, or service setup calls provide evidence. |
| FR-087 | The extractor shall detect Topshelf-style services if present. |
| FR-088 | The extractor shall detect custom host loops such as long-running loops, polling loops, and manual service runners where evidence supports runtime classification. |
| FR-089 | The extractor shall capture host technology, service type, run method, lifecycle method, project, and evidence metadata where available. |
| FR-090 | The extractor shall apply lower confidence to heuristic custom host-loop detection and preserve confidence reason metadata. |

### 4.14 Graph Nodes and Relationships

| ID | Requirement |
| --- | --- |
| FR-091 | The extractor shall emit `Endpoint` nodes through the snapshot contract. |
| FR-092 | The extractor shall emit `Controller` nodes through the snapshot contract. |
| FR-093 | The extractor shall emit `HostedService` nodes through the snapshot contract. |
| FR-094 | The extractor shall emit `Queue` and `Topic` nodes where runtime consumer facts require them. |
| FR-095 | The extractor shall reuse existing `Project`, `Type`, `Method`, `ConfigurationKey`, `FilePath`, and related nodes rather than creating duplicate conceptual nodes. |
| FR-096 | The extractor shall emit `DECLARES_ENDPOINT` relationships for project, application, controller, or route declaration facts according to the established graph model. |
| FR-097 | The extractor shall emit `EXPOSES` relationships for runtime-facing surfaces exposed by projects or application nodes according to the established graph model. |
| FR-098 | The extractor shall emit `HANDLES` relationships for queue, topic, message, scheduled job, request, or runtime handler facts where evidence exists. |
| FR-099 | The extractor shall emit `DEPENDS_ON` relationships for runtime dependencies where supported by semantic, configuration, or DI facts. |
| FR-100 | The extractor shall emit `USES_CONFIG` relationships for runtime facts that depend on configuration keys. |
| FR-101 | The extractor shall attach evidence to every non-derived runtime fact. |
| FR-102 | The extractor shall store route template, HTTP method, authorization metadata, transport, scheduler metadata, middleware metadata, handler metadata, and runtime classification metadata in metadata fields where available. |

### 4.15 Confidence, Unknowns, Warnings, and Errors

| ID | Requirement |
| --- | --- |
| FR-103 | The extractor shall assign high confidence to symbol-resolved runtime facts and exact source-artifact matches. |
| FR-104 | The extractor shall assign medium confidence to strongly supported syntax or file-pattern detections that are not fully symbol-resolved. |
| FR-105 | The extractor shall assign low confidence to heuristic detections such as custom host loops, naming-based message handlers, or computed route patterns. |
| FR-106 | The extractor shall represent unresolved route templates, unresolved controller actions, unresolved middleware targets, unresolved queue names, unresolved schedule expressions, and unresolved handler targets as explicit unknowns with unknown reason. |
| FR-107 | The extractor shall produce warnings for unreadable runtime artifacts, malformed configuration artifacts, unsupported runtime frameworks, unresolvable wrapper methods, and partial compilation failures that affect runtime extraction. |
| FR-108 | The extractor shall produce extraction errors only for failures that prevent the runtime slice from completing for a project or solution. |
| FR-109 | The extractor shall not silently omit partially detectable runtime facts when explicit unknown representation is possible. |

## 5. Non-Functional Requirements

### 5.1 Determinism and Evidence

| ID | Requirement |
| --- | --- |
| NFR-001 | Given the same repository content, solution paths, extraction settings, and dependency versions, WP008 shall produce deterministic runtime facts. |
| NFR-002 | Stable keys and fingerprints for WP008 facts shall not depend on database IDs, absolute developer machine paths, or enumeration order. |
| NFR-003 | Every persisted runtime architectural statement shall have evidence unless it is purely derived from persisted facts. |
| NFR-004 | Evidence shall preserve enough context for later API and MCP consumers to explain the fact without re-reading source files. |

### 5.2 Security and Safe Analysis

| ID | Requirement |
| --- | --- |
| NFR-005 | The extractor shall not execute analyzed repository code, run startup methods, instantiate target hosts, invoke middleware pipelines, connect to queues, connect to databases, or call external services. |
| NFR-006 | Secret-like configuration values referenced by runtime setup shall not be stored in metadata, evidence snippets, warnings, errors, logs, API-ready responses, or generated outputs. |
| NFR-007 | The extractor shall preserve key names and source locations while redacting values that look like passwords, connection-string secrets, tokens, API keys, certificates, private keys, or credentials. |
| NFR-008 | Runtime extraction shall be static and deterministic, based on source files, project artifacts, configuration artifacts, and Roslyn semantic information. |

### 5.3 Performance and Scalability

| ID | Requirement |
| --- | --- |
| NFR-009 | The extractor shall avoid repeated semantic analysis of the same syntax tree or symbol where prior Roslyn context is available. |
| NFR-010 | The extractor shall use cancellation tokens from the extraction orchestration path. |
| NFR-011 | The extractor shall avoid unbounded recursion when following startup extension methods, route registration methods, middleware wrappers, or handler-registration wrappers. |
| NFR-012 | The extractor shall define and test safeguards for large route tables, large Web Forms projects, large configuration files, and deeply nested endpoint group structures. |
| NFR-013 | The extractor shall avoid holding full secret-bearing configuration documents in long-lived memory beyond the extraction scope. |

### 5.4 Maintainability

| ID | Requirement |
| --- | --- |
| NFR-014 | C# code shall use block-scoped namespaces. |
| NFR-015 | C# code shall use Allman braces. |
| NFR-016 | C# files shall contain one public type per file. |
| NFR-017 | Private fields shall use underscore-prefixed naming. |
| NFR-018 | Executable entry points shall avoid top-level statements. |
| NFR-019 | `.csproj` files shall keep `PackageReference` entries in `ItemGroup` blocks that contain only package references. |
| NFR-020 | Internal and non-public types introduced for WP008 shall be treated as requiring the same developer-level documentation standard as public types when documentation is necessary to understand architecture or behavior. |

### 5.5 Testability

| ID | Requirement |
| --- | --- |
| NFR-021 | Runtime extraction logic shall be testable without starting the Aspire AppHost. |
| NFR-022 | Runtime extraction logic shall be testable using in-memory or fixture-based source repositories. |
| NFR-023 | Runtime classification, confidence assignment, stable-key behavior, evidence generation, redaction, and unknown handling shall be directly testable. |
| NFR-024 | Tests shall not require external service credentials, running web servers, queue brokers, scheduler services, Windows services, or database servers. |

## 6. Technical Requirements

### 6.1 Target Runtime and Project Placement

WP008 implementation shall use the repository-approved .NET target and the project layout created by WP001. The expected primary production projects are:

| Project | Responsibility |
| --- | --- |
| `Archon.Extractors.AspNet` | ASP.NET Core host, minimal API, controller, route, middleware, filter, authorization, MVC, and OpenAPI extraction. |
| `Archon.Extractors.LegacyWeb` | Classic ASP.NET, Web Forms, HTTP handler, HTTP module, MVC 5, Web API 2, `Global.asax`, and route-configuration extraction. |
| `Archon.Extractors.Projects` | Project metadata and application-type context consumed by runtime extractors. |
| `Archon.Extractors.DependencyInjection` | Hosted-service registration and runtime service registration context consumed by WP008. |
| `Archon.Extractors.Configuration` | Configuration key and provider context consumed by runtime extractors. |
| `Archon.Roslyn` and language-specific Roslyn projects | Shared semantic context, symbol resolution, invocation analysis, attribute analysis, and evidence projection support. |
| `Archon.Application` | Shared extraction contracts, snapshot accumulation contracts, and orchestration interfaces. |
| `Archon.Api.Extraction` | Coordination of extractor execution through the established API-triggered extraction path. |

Expected corresponding test projects are:

| Test Project | Responsibility |
| --- | --- |
| `Archon.Extractors.AspNet.Tests` | ASP.NET Core host, endpoint, controller, route, middleware, filter, authorization, MVC, and OpenAPI extraction behavior. |
| `Archon.Extractors.LegacyWeb.Tests` | Classic ASP.NET, Web Forms, handler, module, MVC 5, Web API 2, `Global.asax`, and route-configuration extraction behavior. |
| `Archon.Extractors.Projects.Tests` | Any application-type or project metadata behavior introduced specifically to support WP008. |
| `Archon.Extractors.DependencyInjection.Tests` | Hosted-service registration correlation and DI/runtime integration behavior introduced or adjusted for WP008. |
| `Archon.Extractors.Configuration.Tests` | Runtime configuration-key correlation behavior introduced or adjusted for WP008. |
| `Archon.Api.Extraction.Tests` | Pipeline participation, orchestration integration, warning/error propagation, and snapshot accumulation behavior. |
| `Archon.Roslyn.Tests`, `Archon.Roslyn.CSharp.Tests`, `Archon.Roslyn.VisualBasic.Tests` | Any shared semantic helper behavior introduced specifically to support WP008. |

### 6.2 Dependency Direction

WP008 must preserve Onion Architecture dependency direction:

- Domain must not depend on application, extractors, infrastructure, or hosts.
- Application may define contracts and ports but must not depend on infrastructure or hosts.
- Extractors may depend on application and Roslyn abstractions according to existing solution direction.
- API extraction coordination may depend on extractor contracts but must not absorb extractor implementation details that belong in extractor projects.
- Infrastructure and hosts must not become a dumping ground for runtime extraction logic.

### 6.3 Runtime Artifact Analysis

The implementation shall analyze runtime artifacts as source data. It shall not execute startup code, build target application hosts, invoke framework runtime pipelines, or require target applications to be runnable.

Runtime artifact analysis shall preserve:

- Repository-relative source file path.
- Runtime artifact kind.
- Route, endpoint, handler, service, or host-loop identifier where available.
- Project and target framework context.
- Source line span where available.
- Confidence and detection mode.
- Unknown reason where a runtime fact is partial.

### 6.4 Route and Endpoint Analysis

Route and endpoint analysis shall use Roslyn semantic information where available. It shall recognize endpoint and route calls by symbol identity when possible and by syntax fallback only where symbol identity is not available. Fallback detections must carry lower confidence and explicit metadata identifying the detection mode.

Route and endpoint analysis shall preserve:

- Route template.
- HTTP method.
- Controller and action metadata where applicable.
- Minimal API handler metadata where applicable.
- Endpoint group metadata where applicable.
- Authorization metadata.
- Filter metadata.
- Middleware or pipeline metadata where relevant.
- Dynamic-route unknowns where exact route values cannot be determined.

### 6.5 Worker and Message Consumer Analysis

Worker and message consumer analysis shall use Roslyn semantic information, dependency-injection registration facts, configuration facts, package references, and source artifacts where available. The implementation shall not connect to queue brokers, start background services, or run scheduler frameworks.

Worker and message consumer analysis shall preserve:

- Worker or hosted service type.
- Entry or execution method.
- Queue, topic, subscription, or schedule metadata where available.
- Transport or scheduler technology hint.
- Configuration key references.
- Handler source location.
- Confidence and unknown reason metadata.

### 6.6 Documentation Pass

WP008 shall include a documentation pass covering:

- Supported ASP.NET Core extraction patterns.
- Supported classic ASP.NET extraction patterns.
- Supported Web Forms, MVC 5, and Web API 2 extraction patterns.
- Supported console and worker extraction patterns.
- Supported hosted-service, scheduled-job, queue-consumer, and message-handler extraction patterns.
- Confidence and unknown-state behavior.
- Secret redaction behavior for runtime configuration evidence.
- Testing and fixture guidance for runtime extraction.
- Limitations and known unsupported patterns, expressed as current implementation constraints rather than deferred mandatory requirements.

Internal and non-public implementation types introduced for WP008 shall be treated as requiring the same developer-level documentation standard as public types when documentation is necessary to understand the architecture or behavior.

## 7. Exclusions

WP008 shall not implement:

- Archon Discovery UI host, pages, components, assets, endpoint explorer, worker explorer, graph view, or tests for UI behavior.
- API query endpoints for browsing runtime facts; those belong to the query API work package.
- MCP tools, MCP resources, MCP prompts, or Copilot workflows.
- Rule catalog evaluation, hotlist generation, finding suppression, or rule management.
- Data-access extraction beyond runtime dependency hints already represented by prior or later slices.
- Full external integration extraction beyond queue, topic, message-consumer, and runtime consumer facts explicitly required by WP008.
- .NET UI technology extraction for Blazor, Razor Pages/views, Windows Forms, WPF, WinUI, .NET MAUI, or Avalonia; those belong to WP011 unless a runtime artifact is already covered by ASP.NET or classic web scope.
- Markdown export.
- Snapshot diff.
- Direct Neo4j writes from extractor projects.
- Execution of analyzed repository startup code, application hosts, middleware pipelines, background services, scheduler jobs, or message handlers.

## 8. Data and Integration Requirements

### 8.1 Required Graph Facts

WP008 shall contribute graph facts that fit the existing Archon graph model:

| Fact Type | Required Treatment |
| --- | --- |
| Endpoint | Represent as `Endpoint` nodes with route, HTTP method, project, handler, metadata, evidence, confidence, and unknowns where applicable. |
| Controller | Represent as `Controller` nodes with project, type, action, route, authorization, metadata, evidence, confidence, and unknowns where applicable. |
| Hosted service | Represent as `HostedService` nodes with implementation type, registration source, execution method, project, metadata, evidence, confidence, and unknowns where applicable. |
| Queue or topic | Represent as `Queue` and `Topic` nodes where runtime consumers handle messages or subscriptions. |
| Entry point | Represent through the established node and relationship model for methods, types, projects, and runtime metadata. |
| Runtime relationships | Represent endpoint declarations, exposed surfaces, handlers, dependencies, and configuration usage through `DECLARES_ENDPOINT`, `EXPOSES`, `HANDLES`, `DEPENDS_ON`, and `USES_CONFIG` relationships where applicable. |
| Evidence | Link file, symbol, call site, markup, configuration, line span, snippet hash, snippet preview, confidence, and redaction metadata. |
| Unknown | Represent unresolved routes, dynamic endpoint definitions, unresolved handlers, computed queue names, computed schedules, and unsupported runtime patterns with explicit unknown reason. |

### 8.2 Metadata Requirements

WP008 metadata shall support later API and MCP consumption. Metadata shall include, where available:

- Runtime application kind.
- Runtime framework or technology.
- Project key.
- Target framework.
- Startup file.
- Startup type or method.
- Entry-point method.
- Route template.
- HTTP method.
- Controller name.
- Action name.
- Minimal API handler identity.
- Endpoint group.
- Authorization policy or role metadata.
- Anonymous access indicator.
- Filter metadata.
- Middleware type or call metadata.
- Middleware order where deterministically available.
- OpenAPI or Swagger setup indicator.
- Web Forms virtual path.
- Handler type.
- Module type.
- Hosted service type.
- Background service indicator.
- Scheduler technology.
- Schedule expression or unknown reason.
- Queue name.
- Topic name.
- Subscription name.
- Message handler type and method.
- Transport hint.
- Configuration key reference.
- Detection mode.
- Confidence reason.
- Unknown reason.

### 8.3 Evidence Requirements

Evidence shall include enough information for later API and MCP consumers to show why the fact exists:

- Repository-relative file path.
- Line and column span where available.
- Symbol name where available.
- Containing symbol where available.
- Runtime artifact type.
- Route template or handler identifier where relevant.
- Configuration key path where relevant.
- Markup or configuration element path where relevant.
- Snippet hash.
- Snippet preview with secrets redacted.
- Detection mode.
- Confidence.

### 8.4 Integration with Earlier Work Packages

WP008 shall integrate with earlier outputs as follows:

- Use project and application-type facts from WP005 to identify runtime candidate projects.
- Use semantic symbol facts from WP006 to identify types, methods, attributes, invocations, inheritance, and interface implementations.
- Use configuration facts from WP007 to resolve route, queue, topic, scheduler, and runtime setup configuration keys where available.
- Use dependency-injection facts from WP007 to correlate hosted-service registrations, middleware registrations, service dependencies, and runtime composition.
- Reuse existing nodes and relationships when earlier work packages already emitted equivalent facts.

### 8.5 Integration with Later Work Packages

WP008 output shall be shaped so later work packages can:

- Query endpoints by project, controller, route, HTTP method, authorization metadata, and snapshot.
- Query worker and hosted-service facts by project, type, service registration, queue/topic, schedule, and snapshot.
- Explain change impact from runtime endpoints, hosted services, queues, topics, configuration keys, and service dependencies.
- Feed rule evaluation and hotlist findings for legacy web technologies, risky runtime patterns, out-of-support framework usage, and architecture smells.
- Expose evidence-backed runtime facts through MCP tools and resources.
- Include runtime maps in generated markdown.

## 9. Test Requirements

### 9.1 Required Test Coverage

| Test Area | Required Verification |
| --- | --- |
| Pipeline integration | Runtime extractors run through the existing extraction orchestration path and emit snapshot facts. |
| ASP.NET Core host detection | `Program.cs`, `Startup.cs`, `WebApplication.CreateBuilder`, generic host, and web host builder patterns are detected. |
| Minimal APIs | `MapGet`, `MapPost`, `MapPut`, `MapDelete`, endpoint groups, route templates, handlers, dynamic routes, and HTTP methods are detected or represented as unknowns. |
| Controllers and MVC | Controllers, action methods, route attributes, HTTP verb attributes, MVC setup, and conventional routing are detected. |
| Authorization, filters, middleware, OpenAPI | Authorization attributes, anonymous access, filters, middleware registrations, middleware order, and OpenAPI setup are detected. |
| Classic ASP.NET | `System.Web`, `Global.asax`, lifecycle methods, `web.config`, and classic runtime indicators are detected. |
| Web Forms | `.aspx` pages, code-behind files, user controls, virtual paths, and evidence are detected. |
| HTTP handlers and modules | Handler and module declarations from source or configuration are detected. |
| MVC 5 and Web API 2 | Controllers, actions, HTTP verb attributes, route configuration, and unresolved routes are handled. |
| Console entry points | `static Main`, VB.NET entry points, top-level statements, and ambiguous entry points are detected or represented as unknowns. |
| Worker services | Worker projects, `IHostedService`, `BackgroundService`, host builders, registration correlation, and execution methods are detected. |
| Scheduled jobs | Scheduler patterns, job types, schedule expressions, computed schedules, and unknown schedule cases are handled. |
| Queue consumers and message handlers | Queue/topic consumers, message handlers, transport hints, configuration keys, and unknown queue/topic names are handled. |
| Windows-service-style hosting | Windows-service-style hosts, Topshelf patterns, custom loops, lifecycle methods, and confidence levels are handled. |
| Graph facts | `Endpoint`, `Controller`, `HostedService`, `Queue`, `Topic`, `DECLARES_ENDPOINT`, `EXPOSES`, `HANDLES`, `DEPENDS_ON`, and `USES_CONFIG` facts are emitted as applicable. |
| Evidence | Every non-derived fact has source evidence with file path, line span where available, snippet hash, and redacted preview. |
| Confidence | High, medium, and low confidence cases are assigned consistently. |
| Unknowns | Dynamic routes, unresolved handlers, unresolved middleware, computed queue names, computed schedules, and partial runtime patterns produce explicit unknowns. |
| Deduplication | Duplicate facts from multiple detection paths do not create duplicate graph facts. |
| C# support | C# runtime extraction patterns are covered. |
| VB.NET support | VB.NET runtime extraction patterns are covered where Roslyn supports semantic detection. |

### 9.2 Test Fixtures

Tests shall include fixture repositories or in-memory source sets for:

- Minimal ASP.NET Core application with direct endpoint mappings.
- ASP.NET Core application with controllers and attribute routing.
- ASP.NET Core application with `Startup.cs`.
- ASP.NET Core application with route groups, filters, authorization, middleware, and OpenAPI setup.
- Classic ASP.NET application with `Global.asax` and `web.config`.
- Web Forms application with `.aspx`, `.ascx`, and code-behind files.
- MVC 5 and Web API 2 applications with route configuration.
- Console applications with `static Main`.
- C# top-level statement application.
- VB.NET console or web runtime example where feasible.
- Worker service with `BackgroundService` and `AddHostedService<T>()` registration.
- Hosted service without detected registration but with source evidence.
- Scheduled job example.
- Queue consumer and message handler examples.
- Windows-service-style host and Topshelf-style host examples.
- Dynamic route, queue, topic, and schedule examples.
- Secret-like runtime configuration examples for redaction verification.

### 9.3 Test Constraints

Automated verification must not start the Aspire AppHost as a blocking process. Tests should use extractor-level fixtures, application-layer orchestration seams, and targeted integration tests. For this work package, the full test suite should not be run unless explicitly requested; run targeted WP008 tests and a solution build as final validation.

## 10. Acceptance Criteria

WP008 is accepted when all of the following are true:

1. Runtime extractors are wired into the existing extraction orchestration path.
2. ASP.NET Core `Program.cs`, `Startup.cs`, `WebApplication.CreateBuilder`, minimal API mappings, controllers, route attributes, authorization attributes, filters, middleware registrations, `IEndpointRouteBuilder`, MVC setup, and OpenAPI setup are detected.
3. Classic ASP.NET `System.Web`, `Global.asax`, Web Forms pages, code-behind files, handlers, modules, `web.config`, MVC 5 controllers, Web API 2 controllers, and route configuration are detected.
4. Console entry points, `static Main`, and top-level statements in analyzed target repositories are detected.
5. Hosted services, background services, worker services, scheduled jobs, queue consumers, message handlers, Windows-service-style hosting, Topshelf-style services, and custom host loops are detected or represented as explicit unknowns where partially understood.
6. `Endpoint`, `Controller`, `HostedService`, `Queue`, `Topic`, and relevant `Method` or `Type` nodes are emitted through the snapshot contract.
7. `DECLARES_ENDPOINT`, `EXPOSES`, `HANDLES`, `DEPENDS_ON`, `USES_CONFIG`, and related relationships are emitted through the snapshot contract where applicable.
8. Route templates, HTTP methods, authorization metadata, transport metadata, scheduling metadata, handler metadata, and runtime classification metadata are stored where available.
9. Runtime facts are evidence-backed, deterministic, snapshot-scoped, and compatible with stable keys and fingerprints.
10. Unknowns and confidence are explicit for unresolved or inferred runtime facts.
11. Secret-like values from runtime configuration evidence are redacted before being stored or exposed in evidence, metadata, warnings, errors, or logs.
12. Tests cover minimal APIs, controllers, classic ASP.NET artifacts, Web Forms markers, hosted services, background services, console entry points, scheduled jobs, queue handlers, evidence, confidence, and unknown handling.
13. Documentation is updated for supported runtime extraction behavior and validation.
14. No Archon Discovery UI implementation is introduced.
15. The solution builds successfully.
16. Targeted WP008 tests pass.

## 11. Risks and Decisions

### 11.1 Risks

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Minimal API routes may be built dynamically. | Extracted endpoint routes may be incomplete or misleading. | Use exact route evidence when available and explicit unknowns with lower confidence for dynamic routes. |
| ASP.NET Core startup logic may be split across wrapper extension methods. | Runtime setup may be missed. | Analyze available source wrapper methods with recursion safeguards and emit warnings for unresolvable wrappers. |
| Classic ASP.NET routing and Web Forms behavior may be convention-heavy. | Route and handler facts may be partial. | Preserve artifact-level facts, confidence, evidence, and unknowns instead of inventing route details. |
| Queue and scheduled job technologies vary widely. | Runtime consumers may be under-detected or overgeneralized. | Use known API patterns, configuration clues, package references, and conservative confidence assignment. |
| Custom host loops are heuristic. | False positives may reduce trust. | Require source evidence, use low confidence, and preserve confidence reason metadata. |
| Runtime configuration can contain secrets. | Persisted evidence could expose credentials. | Reuse or enforce redaction before evidence, metadata, warning, error, or log emission. |
| Multiple extraction slices may emit overlapping runtime dependency facts. | Duplicate graph relationships could reduce query quality. | Deduplicate by stable key and fingerprint through the snapshot accumulator. |
| VB.NET support may differ from C# for modern runtime idioms. | Mixed-language estates may have uneven coverage. | Use Roslyn Visual Basic semantic support where available and document/test supported parity. |

### 11.2 Decisions

| Decision | Rationale |
| --- | --- |
| Use a single WP008 specification document. | User requested a single markdown document spec for WP008. |
| Create the documentation under `docs/008-ASP.NET-Worker-Console-and-Runtime-Extraction/`. | This is the next incremental documentation work-package folder after WP007. |
| Do not create separate overview and component spec documents. | The user explicitly requested a single markdown document, overriding the multi-document collaboration pattern for this output. |
| Treat runtime extraction as extraction slices, not persistence services. | The work-package sequence requires extractors to contribute through the snapshot contract and keep Neo4j as the system of record. |
| Keep broad external integration extraction out of WP008 except runtime consumer facts. | Full external integration extraction belongs to WP010, while WP008 only needs runtime queue/topic/handler and related consumer facts. |
| Preserve explicit unknowns rather than suppressing partial runtime facts. | The source brief requires unknowns to be represented instead of omitted or invented. |
| Use deterministic runtime stable-key inputs. | WP008 stable keys shall be deterministic and shall not depend on database IDs, absolute developer machine paths, or enumeration order. Endpoint keys shall use project key, route template, HTTP method, and handler identity. Controller keys shall use project key and fully qualified controller type name. Hosted service keys shall use project key and fully qualified implementation type name. Queue and topic consumer keys shall use project key, transport kind, queue or topic name or configuration key, and handler identity. Entry point keys shall use project key plus fully qualified method identity or normalized top-level-statement file path. |
| Use ownership and dependency direction for runtime relationships. | `Project` or runtime application nodes shall `DECLARES_ENDPOINT` to `Endpoint`. `Project` or runtime application nodes shall `EXPOSES` to `Endpoint` or `Controller`. `Controller` shall `DECLARES_ENDPOINT` to `Endpoint`. Handler `Method` or `Type` nodes shall `HANDLES` to `Queue`, `Topic`, or message targets. Runtime nodes shall `USES_CONFIG` to `ConfigurationKey`. Runtime nodes shall `DEPENDS_ON` dependency nodes. Hosted service registration direction shall match the established WP007 direction for `REGISTERED_AS_SERVICE`. |
| Use lower camel case runtime metadata field names. | Runtime metadata fields shall use stable API-friendly lower camel case names, including `runtimeKind`, `framework`, `targetFramework`, `routeTemplate`, `httpMethod`, `controllerName`, `actionName`, `handlerSymbol`, `authorizationPolicy`, `allowsAnonymous`, `middlewareType`, `middlewareOrder`, `openApiEnabled`, `queueName`, `topicName`, `subscriptionName`, `transportKind`, `scheduleExpression`, `configurationKey`, `detectionMode`, `confidenceReason`, and `unknownReason`. |
| Represent runtime subtypes as metadata, not new graph node kinds. | WP008 shall keep the core graph node kinds aligned with WP002 and Appendix E: `Endpoint`, `Controller`, `HostedService`, `Queue`, `Topic`, plus existing `Project`, `Type`, and `Method`. Finer runtime distinctions shall be represented as metadata, such as `Endpoint.runtimeKind` values `AspNetCoreMinimalApi`, `AspNetCoreControllerAction`, `Mvc5Action`, `WebApi2Action`, `WebFormsPage`, and `HttpHandler`; `HostedService.runtimeKind` values `IHostedService`, `BackgroundService`, `WorkerService`, `WindowsService`, `TopshelfService`, and `CustomHostLoop`; `Queue.transportKind` values `AzureServiceBus`, `RabbitMq`, `Msmq`, and `Unknown`; and `Controller.framework` values `AspNetCore`, `Mvc5`, and `WebApi2`. |

## 12. Manual Verification Requirements

The implementation documentation for WP008 shall instruct a developer to verify the work package by:

1. Restoring and building the solution.
2. Running targeted tests for ASP.NET Core runtime extraction.
3. Running targeted tests for classic ASP.NET runtime extraction.
4. Running targeted tests for worker, console, hosted-service, scheduled-job, queue-consumer, and message-handler extraction.
5. Running targeted extraction integration tests through the API extraction module seam without launching the blocking Aspire AppHost process.
6. Inspecting representative snapshot output to confirm `Endpoint`, `Controller`, `HostedService`, `Queue`, `Topic`, `DECLARES_ENDPOINT`, `EXPOSES`, `HANDLES`, `DEPENDS_ON`, and `USES_CONFIG` facts are emitted where applicable.
7. Confirming evidence includes redacted snippets and source locations.
8. Confirming secret-like runtime configuration values are not present in test output, logs, warnings, errors, metadata, or evidence previews.
9. Confirming no Archon Discovery UI resource, page, component, or front-end asset was created.

Automated validation instructions shall explicitly state not to run the AppHost as a blocking process during agent-driven verification.

## 13. Traceability Matrix

| Source Requirement | Specification Coverage |
| --- | --- |
| Detect ASP.NET Core `Program.cs`, `Startup.cs`, `WebApplication.CreateBuilder`, minimal API mappings, controllers, route attributes, authorization attributes, filters, middleware registrations, `IEndpointRouteBuilder`, MVC setup, and OpenAPI setup | Sections 4.2 through 4.5, 9, 10 |
| Detect classic ASP.NET `System.Web`, `Global.asax`, Web Forms pages, code-behind files, handlers, modules, `web.config`, MVC 5 controllers, Web API 2 controllers, and route configuration | Sections 4.6 through 4.8, 9, 10 |
| Detect console entry points, `static Main`, top-level statements, hosted services, background services, scheduled jobs, queue consumers, message handlers, Windows-service-style hosting, Topshelf, and custom host loops | Sections 4.9 through 4.13, 9, 10 |
| Persist `Endpoint`, `Controller`, `HostedService`, `Queue`, `Topic`, and relevant `Method` or `Type` nodes | Sections 4.14, 8.1, 10 |
| Persist `DECLARES_ENDPOINT`, `EXPOSES`, `HANDLES`, `DEPENDS_ON`, `USES_CONFIG`, and related relationships | Sections 4.14, 8.1, 10 |
| Metadata for route templates, authorization, transport, and scheduling details | Sections 4.14, 8.2, 10 |
| Evidence-backed facts | Sections 4.15, 5.1, 8.3, 9, 10 |
| Confidence and unknown handling | Sections 4.15, 8.1, 9, 10 |
| Tests cover minimal APIs, controllers, classic ASP.NET artifacts, Web Forms markers, hosted services, background services, console entry points, and queue handlers | Sections 9, 10 |
| Repository documentation updated | Sections 6.6, 12, 10 |
| No Discovery UI implementation | Sections 1.3, 7, 10, 12 |

## 14. Open Questions

No open questions remain for WP008. Stable-key inputs, graph relationship direction, metadata field names, and runtime subtype representation are recorded as definitive decisions in section 11.2.

## 15. Change Log

| Date | Change |
| --- | --- |
| 2026-05-22 | Created initial single-document WP008 specification from `docs/foundation/work-packages.md` and the Archon source brief. |
| 2026-05-22 | Recorded definitive answers for stable-key inputs, relationship direction, metadata field names, and runtime subtype representation. |
