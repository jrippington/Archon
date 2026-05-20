# Archon Sequential Work Packages

## Source brief and controlling interpretation

This document uses [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md), canonical workspace path `D:\Dev\Archon\docs\foundation\archon_full_concept_brief.md`, as the mandatory source brief for Archon.

The sequence below is mandatory and complete for the API-first and MCP-first delivery of Archon. Every work package must be completed in order. No item in a work package may be deferred, postponed, treated as optional, or left for a later unspecified phase. A work package is complete only when its source-brief requirements, tests, documentation, and acceptance criteria are satisfied.

This document intentionally contains **no Archon Discovery UI work packages**. The source brief describes a human-facing Discovery UI in [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) sections 8.1, 28, 35 phases 1, 6, and 7, 36 UI epic, and 41, but the current delivery direction is to complete the full API and MCP product capability before any user-interface delivery is considered. Therefore:

- no `ArchonUi` host is implemented by this work-package sequence;
- no dashboard, explorer, graph view, evidence viewer, hotlist viewer, prompt panel, or other human-facing UI page is implemented by this work-package sequence;
- all query, traversal, evidence, hotlist, diff, markdown, and architecture-intelligence capability must instead be exposed through API and MCP surfaces;
- backend extraction of .NET UI technologies remains in scope because [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) treats Blazor, Razor, Windows Forms, WPF, WinUI, .NET MAUI, and Avalonia as architecture facts that must be available to API and MCP consumers, not as Archon's own product UI.

The final package in this sequence completes the full API and MCP product capability described by [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md). No API, MCP, extraction, graph, rule, evidence, metric, markdown, or operational capability described by [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) is left as future work.

## Source brief reference map

Each work package below references the relevant [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) sections directly. The main source-brief areas are:

- Product principles: [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) sections 1 through 5, especially deterministic facts, evidence, unknowns, legacy support, and .NET-first scope.
- External references: [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 6 for Roslyn, Microsoft lifecycle and obsoletion guidance, and MCP.
- Architecture semantic graph: [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 7.
- High-level architecture and component responsibilities: [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 8, excluding Discovery UI delivery.
- Solution structure and project responsibilities: [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 9, excluding `ArchonUi` implementation work.
- Aspire hosting and Neo4j: [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) sections 10 and 11.
- Core data model, stable keys, and extraction pipeline: [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) sections 12 through 14.
- Extraction domains: [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) sections 15 through 24 and Appendix E section E.6.
- Hotlist, rules, findings, and rule catalog: [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) sections 25 through 27 and Appendices A and B.
- MCP and Copilot workflows: [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) sections 29 and 30 and Appendix C.
- Markdown export: [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 31.
- Quality, classification, metrics, and architecture rules: [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) sections 32 through 34.
- Existing phase and backlog guidance: [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) sections 35 through 37, adjusted only to remove Archon Discovery UI work packages.
- Risks and mitigations: [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 39.
- Full graph persistence and extraction specification: [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) Appendix E.

## Completion rules for every work package

Every work package must satisfy these rules:

1. Implement the complete capability described for the package; partial implementation is not acceptable.
2. Preserve the source-brief principles that Roslyn extracts deterministic facts, Neo4j stores the architecture graph, API and MCP expose evidence-backed knowledge, and AI does not invent facts.
3. Persist every architectural statement with evidence unless the statement is purely derived from persisted facts.
4. Represent unknowns explicitly with confidence and unknown-reason data instead of silently omitting them.
5. Use deterministic stable keys independent of database IDs.
6. Keep Neo4j as the system of record for extraction output.
7. Ensure API-triggered extraction accepts a repository root directory and explicit solution path list.
8. Provide tests for all implemented production behavior.
9. Update repository documentation as part of the work package.
10. Do not introduce Archon Discovery UI implementation.

---

# WP001 - Solution Foundation, Onion Boundaries, and Host Bootstrap

## Objective

Create the repository implementation foundation required for every later package: the complete solution structure, every planned production project under `./src`, every corresponding test project under `./test`, project boundaries, Aspire hosting, service defaults, API host, MCP host shell, core domain/application projects, infrastructure seams, and test-project skeletons. This package establishes the executable and architectural skeleton but does not implement extraction behavior beyond health and bootstrap verification.

## Mandatory source brief references

- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 1: Archon's purpose as a deterministic architecture intelligence platform.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 4: Architecture Operating System responsibilities.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 5: deterministic facts, evidence, unknowns, legacy-first, and .NET-first principles.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 8.1: Archon API Host, API modules, Neo4j graph, and MCP Server responsibilities.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 9 and 9.1: recommended solution structure and project responsibilities.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 10: Aspire hosting model.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 35 phase 1 and section 36 Core Platform epic, excluding UI shell.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) Appendix E sections E.1.4 and E.5.6: API-triggered extraction constraint and extraction contract direction.

## Required implementation

- Create or align all planned production projects under `./src` during this work package, not incrementally in later packages. This includes AppHost, service defaults, domain, application, API modules, Roslyn abstractions, extractor slices, infrastructure adapters, API host, MCP host, and every additional production project required by the complete work-package sequence.
- Create corresponding test projects under `./test` for every production project during this work package, including test projects for capabilities whose implementation arrives in later work packages.
- Configure the Aspire AppHost to compose Neo4j, the API host, and the MCP host.
- Configure service defaults for health checks, telemetry, resilience, and shared host configuration.
- Establish Onion Architecture references so hosts depend outward on application/module services and no inward layer depends on hosts or infrastructure.
- Create API host bootstrap endpoints for health/readiness only.
- Create MCP host bootstrap with health/readiness only and no tools yet.
- Exclude any `ArchonUi` implementation and any UI page, component, or front-end asset work.

## Completion criteria

- The solution builds with the complete planned production and test project skeleton present.
- Automated verification must not run the Aspire AppHost process because it blocks the executing agent. Instead, provide the user with explicit manual verification instructions to start Aspire and confirm Neo4j, API host, and MCP host run without Discovery UI.
- Project references enforce the architecture direction described by [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md).
- Tests verify host bootstrap, service defaults, and dependency boundaries.
- No extraction, query, MCP tool, or UI feature is marked as deferred; those capabilities are assigned to later packages in this document.

---

# WP002 - Architecture Graph Domain Model and Shared Contracts

## Objective

Implement the complete domain and application contract model for snapshots, repositories, solutions, nodes, edges, evidence, rules, findings, metrics, generated summaries, stable keys, fingerprints, classification, confidence, unknowns, and extraction accumulation.

## Mandatory source brief references

- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 7: Roslyn projection into the Architecture Semantic Graph.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 12: core persistence concepts, snapshot-centered graph, node model, edge model, evidence, rules/findings, metrics/summaries, link tables, classification, and unknowns.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 13: stable keys.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) sections 32 and 33: quality, confidence, classification, and metrics.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) Appendix E sections E.2 through E.5.5: target state, graph architecture, functional requirements, core graph elements, supporting relationships, stable keys, and metadata strategy.

## Required implementation

- Implement domain value objects, enums, and models for every required node kind, edge kind, evidence kind, rule category, finding severity, finding status, knowledge kind, confidence, metric scope, and summary kind.
- Implement snapshot-scoped models for repositories, solutions, architecture nodes, architecture edges, evidence, rules, findings, metrics, and generated summaries.
- Implement deterministic stable-key generation in one shared component with every prefix required by [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) Appendix E section E.5.4.
- Implement fingerprint generation for nodes, edges, evidence, findings, metrics, and generated summaries.
- Implement explicit unknown-state and confidence representation for nodes, edges, evidence, and findings.
- Implement extraction accumulation contracts that allow all extractor slices to contribute nodes, edges, evidence, findings, metrics, warnings, and errors into one snapshot.

## Completion criteria

- Every required node kind and edge kind from [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) sections 12.3, 12.4, E.4.2, and E.4.3 exists in code.
- Every required stable-key prefix from [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) Appendix E section E.5.4 is implemented and tested.
- Unknowns and confidence cannot be bypassed for persisted fact contracts.
- Tests cover stable-key determinism, fingerprint determinism, enum/string serialization, metadata handling, and extraction accumulation.

---

# WP003 - Neo4j Persistence Foundation

## Objective

Implement Neo4j as the complete system of record for Archon's architecture graph, including graph initialization, constraints, indexes, snapshot writing, evidence deduplication, rule catalog persistence, findings, metrics, generated summaries, and supporting relationship patterns.

## Mandatory source brief references

- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 8.1: Neo4j Architecture Graph responsibilities.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 11: Neo4j database choice and native graph reasoning.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 12: full core data model.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 13: stable keys must not depend on database IDs.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) Appendix E sections E.5.1 through E.5.3: persistence strategy, core graph elements, and supporting relationships.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) Appendix E section E.7.1: persistence foundation deliverables and acceptance criteria.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) Appendix E section E.8.3: query performance risk mitigations.

## Required implementation

- Implement Neo4j connection configuration, health checks, and lifecycle integration.
- Implement graph constraints and indexes for repositories, solutions, snapshots, architecture nodes, architecture relationships, evidence, rules, findings, metrics, and generated summaries.
- Implement snapshot persistence for all graph primitives.
- Implement evidence deduplication per snapshot.
- Implement relationships for snapshot-to-solution, node-to-evidence, edge-to-evidence, metric-to-evidence, finding-to-evidence, and finding-to-node.
- Implement graph recreation support with no migration requirement, as permitted by [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) Appendix E section E.1.4.
- Implement tests against Neo4j seams or test containers sufficient to prove graph creation and persistence behavior.

## Completion criteria

- A Neo4j graph can be created from scratch and used as the sole persistence model.
- One snapshot can persist mixed node and edge kinds, multiple evidence records, findings, metrics, and summaries.
- One evidence record can support multiple nodes, edges, findings, or metrics within a snapshot.
- Snapshot-scoped stable keys and fingerprints are queryable through indexes.
- Tests prove constraints, indexes, persistence, deduplication, and relationship creation.

---

# WP004 - API Extraction Contract and Snapshot Orchestration

## Objective

Implement the API-triggered extraction workflow and snapshot orchestration contract. This package wires request validation, repository and solution resolution, extraction pipeline sequencing, snapshot assembly, persistence handoff, run history, warnings, and errors, but it uses only placeholder extractor outputs until later extractor packages fill the pipeline.

## Mandatory source brief references

- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 8.1: API Host and API Extraction Module responsibilities.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 14.1: extraction pipeline stages.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) Appendix E sections E.5.6 and E.5.7: API extraction contract and extraction pipeline.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) Appendix E section E.9: acceptance criteria for POST-driven extraction and generalized snapshot contract.

## Required implementation

- Implement `StartExtractionRequest` with repository root directory, solution paths, optional branch name, optional commit SHA, optional requested-by, and metadata.
- Implement request validation requiring one repository root and at least one solution path before Roslyn loading.
- Implement extraction run lifecycle state with started, completed, failed, warnings, and errors.
- Implement a single extraction orchestration path that all later extractors must use.
- Implement `ExtractedArchitectureSnapshot` with repositories, solutions, snapshot header, nodes, edges, evidence, findings, metrics, generated summaries, warnings, and errors.
- Persist snapshot output through the Neo4j persistence adapter.
- Expose API endpoints to start extraction, inspect extraction run status, and retrieve extraction run history.

## Completion criteria

- API extraction cannot start with invalid paths or an empty solution list.
- API extraction produces a generalized snapshot contract, not a project-only aggregate.
- Snapshot persistence receives the complete generalized contract.
- Tests cover validation, orchestration order, error handling, warnings, persistence handoff, and run-history behavior.

---

# WP005 - Repository, Solution, Project, and Package Extraction

## Objective

Implement repository, solution, project, package, project-reference, package-reference, analyzer-reference, target-framework, project-format, and application-type extraction for C# and VB.NET projects.

## Mandatory source brief references

- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 14.2: extraction scope for `.sln`, `.csproj`, `.vbproj`, `.props`, `.targets`, `packages.config`, and related artifacts.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 15: C# and VB.NET Roslyn support.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 16: project and package extraction fields and application type indicators.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 17.1: project-level dependencies.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 35 phase 1 and section 36 Project Extraction epic.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) Appendix E sections E.6.1 and E.7.2: repository/solution modeling and project/code slice implementation.

## Required implementation

- Load submitted solutions through the shared extraction path.
- Extract repository and solution nodes.
- Extract project nodes for C# and VB.NET projects.
- Extract target frameworks, output type, assembly name, root namespace, SDK-style status, old-style project status, nullable setting, implicit usings, analyzer references, project references, package references, and `packages.config` references.
- Classify application type using the full set listed in [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 16.
- Persist `Project`, `Package`, `Repository`, `Solution`, and relevant `FilePath` nodes with `CONTAINS`, `REFERENCES`, and `USES_PACKAGE` relationships.
- Capture evidence from project files, solution files, package references, and configuration artifacts.

## Completion criteria

- C# and VB.NET projects are extracted from submitted solutions.
- SDK-style and old-style project formats are both represented.
- `PackageReference` and `packages.config` dependencies are represented.
- Application type classification covers every value required by [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 16.
- Tests cover multi-solution repositories, mixed C#/VB.NET solutions, project references, package references, old-style projects, and evidence spans.

---

# WP006 - Roslyn Semantic Extraction for C# and VB.NET

## Objective

Implement compiler-grade semantic extraction for C# and VB.NET declarations and relationships, including namespaces, types, methods, properties, fields, inheritance, interface implementation, constructor dependencies, method calls, attribute analysis, compiler diagnostics, and evidence spans.

## Mandatory source brief references

- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 6.1: Roslyn references.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 7: Roslyn syntax trees, semantic models, symbols, compilations, and projection into the graph.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 15: C# and VB.NET requirements.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) sections 17.2 through 17.4: type-level and method-level dependencies and confidence.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 35 phase 2 and section 36 Roslyn Symbols epic.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) Appendix E sections E.4.1, E.7.2, and E.9: semantic extraction coverage and acceptance.

## Required implementation

- Implement language-agnostic Roslyn abstractions and shared helpers.
- Implement C# syntax and semantic extraction.
- Implement VB.NET syntax and semantic extraction with parity where Roslyn supports it.
- Extract namespaces, types, methods, properties, fields, attributes, inheritance, interface implementation, constructor injection, method calls, property access, object creation, parameters, return types, and diagnostics.
- Persist `Namespace`, `Type`, `Method`, `Property`, and `Field` nodes.
- Persist `CONTAINS`, `CALLS`, `IMPLEMENTS`, `INHERITS`, `INJECTS`, `DEPENDS_ON`, and related relationships with confidence and evidence.
- Capture compiler-symbol and source-code evidence with file path, line span, symbol name, containing symbol, snippet hash, and snippet preview.

## Completion criteria

- Mixed C# and VB.NET solutions produce symbol-level architecture facts.
- Method calls, constructor dependencies, inheritance, and interface implementations are represented with evidence.
- Compiler diagnostics can be represented as evidence.
- Tests cover C# extraction, VB.NET extraction, cross-project symbols, unresolved symbols, generated-code handling, confidence classification, and explicit unknowns.

---

# WP007 - Configuration and Dependency Injection Extraction

## Objective

Implement extraction of modern and legacy configuration usage and dependency-injection registrations, including extension-method registration wrappers, legacy containers, service locators, configuration files, options binding, connection strings, and configuration-key usage.

## Mandatory source brief references

- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 21: dependency injection extraction.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 22: modern and legacy configuration extraction.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 17.3: method-level dependencies on configuration and services.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) Appendix E sections E.6.2, E.6.5, and E.7.3: configuration, DI, runtime slice enablement, evidence, and metadata.

## Required implementation

- Detect `IServiceCollection` registrations, hosted-service registrations, `HttpClient` registrations, and wrapper extension methods.
- Detect legacy containers and service-locator patterns listed in [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 21.
- Extract service-to-implementation mappings, lifetimes, constructor dependencies, and registration sources.
- Detect `appsettings.json`, environment-specific appsettings files, `IConfiguration`, options binding, `GetSection`, indexer access, `Bind`, and `Configure<TOptions>`.
- Detect `app.config`, `web.config`, `ConfigurationManager.AppSettings`, `ConfigurationManager.ConnectionStrings`, custom XML sections, binding redirects, and machine-level assumptions.
- Persist `ConfigurationKey` nodes, `USES_CONFIG`, `REGISTERED_AS_SERVICE`, `INJECTS`, and `DEPENDS_ON` relationships.
- Store registration lifetime and configuration-provider details in metadata.

## Completion criteria

- Modern and legacy configuration usage is extractable through API output.
- DI registrations and service dependencies are represented with evidence.
- Legacy container usage is identified with confidence and unknown handling.
- Tests cover options binding, connection strings, wrapper registrations, hosted services, legacy containers, and configuration evidence.

---

# WP008 - ASP.NET, Worker, Console, and Runtime Extraction

## Objective

Implement runtime-facing extraction for ASP.NET Core, classic ASP.NET, Web Forms, MVC/Web API, console applications, worker services, hosted services, scheduled jobs, queue consumers, message handlers, Windows-service-style hosts, and runtime entry points.

## Mandatory source brief references

- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 18: ASP.NET Core and classic ASP.NET extraction.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 20: worker and console extraction.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 24: integrations that overlap with runtime consumers.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 35 phase 4: application and runtime discovery.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) Appendix E sections E.6.3 and E.7.3: runtime extraction support.

## Required implementation

- Detect ASP.NET Core `Program.cs`, `Startup.cs`, `WebApplication.CreateBuilder`, minimal API mappings, controllers, route attributes, authorization attributes, filters, middleware registrations, `IEndpointRouteBuilder`, MVC setup, and OpenAPI setup.
- Detect classic ASP.NET `System.Web`, `Global.asax`, Web Forms pages, code-behind files, handlers, modules, `web.config`, MVC 5 controllers, Web API 2 controllers, and route configuration.
- Detect console entry points, `static Main`, top-level statements in analyzed target repositories, hosted services, background services, scheduled jobs, queue consumers, message handlers, Windows-service-style hosting, Topshelf, and custom host loops.
- Persist `Endpoint`, `Controller`, `HostedService`, `Queue`, `Topic`, and relevant `Method` or `Type` nodes.
- Persist `DECLARES_ENDPOINT`, `EXPOSES`, `HANDLES`, `DEPENDS_ON`, `USES_CONFIG`, and related relationships.

## Completion criteria

- API consumers can query runtime-facing endpoints, controllers, workers, hosted services, entry points, and queue/topic consumers.
- Classic and modern ASP.NET facts are represented with evidence and confidence.
- Worker and console facts are represented with evidence and unknown handling.
- Tests cover minimal APIs, controllers, classic ASP.NET artifacts, Web Forms markers, hosted services, background services, console entry points, and queue handlers.

---

# WP009 - Data Access Extraction

## Objective

Implement full data-access extraction for LINQ to SQL, DBML, generated designer files, Entity Framework Classic / EF6, Entity Framework Core, ADO.NET, raw SQL, stored procedures, and typed DataSets.

## Mandatory source brief references

- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 23: data access extraction and all subsections.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 26.3: legacy data-access hotlist inputs.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 35 phase 3 and section 36 LINQ to SQL epic.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) Appendix E sections E.6.6 and E.7.4: data-access extraction support.

## Required implementation

- Parse `.dbml` files and extract DataContext names, database names, connection information, tables, columns, associations, functions, stored procedures, and entity names.
- Extract generated designer DataContext classes, entity classes, `Table<T>` properties, table mappings, column mappings, associations, and stored procedure methods.
- Detect LINQ to SQL usage including DataContext construction, table queries, `SubmitChanges`, `ExecuteQuery`, `ExecuteCommand`, and stored procedure wrapper calls.
- Detect EF Classic / EF6 and EF Core contexts, entities, mappings, migrations, relationships, provider configuration, `SaveChanges`, raw SQL APIs, and usage sites.
- Detect ADO.NET connections, commands, readers, adapters, datasets, SQL command text, stored procedure calls, read/write hints, dynamic SQL indicators, and affected tables where detectable.
- Detect typed DataSets, `.xsd` files, table adapters, tables, queries, stored procedures, and usage sites.
- Persist `DbContext`, `LinqToSqlDataContext`, `Entity`, `DatabaseTable`, `DatabaseColumn`, and `StoredProcedure` nodes.
- Persist `USES_DB_CONTEXT`, `USES_LINQ_TO_SQL_CONTEXT`, `MAPS_ENTITY`, `MAPS_TABLE`, `MAPS_COLUMN`, `READS_TABLE`, `WRITES_TABLE`, `CALLS_STORED_PROCEDURE`, and `EXECUTES_RAW_SQL` relationships.

## Completion criteria

- API and MCP consumers can identify projects, methods, entities, tables, and stored procedures involved in data access.
- LINQ to SQL is first-class and fully covered.
- EF6, EF Core, ADO.NET, raw SQL, and typed DataSets are represented with evidence and confidence.
- Tests cover DBML parsing, designer extraction, LINQ to SQL usage, EF usage, ADO.NET command analysis, dynamic SQL unknowns, read/write hints, and stored procedure mapping.

---

# WP010 - External Integration Extraction

## Objective

Implement extraction of external service and integration usage, including HTTP clients, RestSharp, WCF, SOAP, gRPC, queues, Azure Service Bus, RabbitMQ, MSMQ, storage clients, SMTP/email, payment providers, and internal service APIs.

## Mandatory source brief references

- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 24: external integration extraction.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 17.3: method-level external-service dependencies.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) sections 20 and 21: workers, queues, topics, and `HttpClient` registrations.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) Appendix E sections E.6.7 and E.7.4: external integration nodes, edges, and metadata.

## Required implementation

- Detect `HttpClient`, `IHttpClientFactory`, typed clients, named clients, RestSharp, WCF clients, SOAP clients, gRPC clients, message queues, Azure Service Bus, RabbitMQ, MSMQ, storage clients, blob/file storage, SMTP/email, payment providers, and internal API clients.
- Extract integration name, owning project, client type, base URL configuration key, authentication hints where detectable, usage sites, and evidence.
- Persist `ExternalService`, `Queue`, and `Topic` nodes.
- Persist `CALLS_EXTERNAL_SERVICE`, `HANDLES`, `USES_CONFIG`, and `DEPENDS_ON` relationships.
- Represent uncertain external targets explicitly as unknowns rather than inventing service names.

## Completion criteria

- External integrations can be queried by project, service, queue/topic, method, and configuration key.
- Authentication hints and base URL configuration keys are captured where detectable.
- Tests cover HTTP clients, named/typed clients, WCF/SOAP/gRPC clients, queues, storage clients, SMTP, unknown service targets, and evidence handling.

---

# WP011 - .NET Client and UI-Technology Extraction for API/MCP Facts

## Objective

Implement extraction of .NET UI technologies as architecture facts for API and MCP consumers only. This package does **not** implement Archon's own Discovery UI. It extracts user-facing technologies in analyzed repositories because [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) requires them to participate in impact analysis, graph traversal, evidence lookup, and MCP workflows.

## Mandatory source brief references

- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 19: .NET UI extraction across Blazor, Razor Pages/views, Windows Forms, WPF, WinUI, .NET MAUI, and Avalonia.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 12.3 and 12.4: UI-related node and edge kinds.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) Appendix E sections E.4.1 through E.4.3 and E.6.4: UI extraction model support.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 35 phase 5 and section 36 .NET UI Extraction epic, interpreted strictly as backend extraction rather than Archon product UI delivery.

## Required implementation

- Detect and extract Blazor `.razor` components, routes, layouts, injected services, parameters, cascading parameters, event callbacks, render modes, authorization metadata, component usage, API/client usage, and configuration usage.
- Detect and extract Razor Pages and MVC Razor views, `_ViewImports`, `_ViewStart`, layouts, partials, view components, tag helpers, page models, handler methods, form posts, anchor tag helpers, route conventions, and controller/action linkage where detectable.
- Detect and extract Windows Forms applications, forms, user controls, designer files, resources, controls, `InitializeComponent`, event handlers, data bindings, startup forms, code-behind dependencies, service usage, and data-access usage.
- Detect and extract WPF windows, pages, user controls, resource dictionaries, styles, templates, bindings, commands, routed events, navigation, view-model relationships, service usage, and data-access usage.
- Detect and extract WinUI windows, pages, user controls, resources, styles, bindings, commands, navigation, app startup, packaging metadata, service usage, and data-access usage.
- Detect and extract .NET MAUI pages, views, Shell routes, handlers, resources, styles, bindings, commands, view-model relationships, platform heads, navigation targets, service usage, and data-access usage.
- Detect and extract Avalonia AXAML applications, windows, user controls, resources, styles, bindings, commands, view locators, view-model relationships, navigation targets, service usage, and data-access usage.
- Persist UI-related architecture facts using the required node and edge kinds from [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md), exposed through API and MCP only.

## Completion criteria

- API and MCP consumers can query .NET UI technology facts from analyzed repositories without any Archon UI implementation.
- All required UI-related node and edge kinds are populated where evidence exists.
- Tests cover every .NET UI technology listed by [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 19.
- No dashboard, explorer, graph page, prompt panel, or Discovery UI surface is created.

---

# WP012 - Rule Catalog, Rule Engine, Hotlist, and Findings

## Objective

Implement the full JSON rule catalog, disk-backed rule loading, schema validation, boolean detection DSL, first-cut built-in rules, rule persistence, rule evaluation, suppressible findings, hotlist output, and finding history across snapshots.

## Mandatory source brief references

- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) sections 25 through 27: modernization hotlist, starter hotlist, and rule engine requirements.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 34: architecture rules and layering.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 35 phase 6: hotlist and findings, excluding Hotlist UI.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) Appendices A and B: rule catalog format and example rule pack.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) Appendix E sections E.4.5, E.5.2.7, E.5.2.8, E.5.7, E.6.8, and E.7.5: rule/finding model, disk-backed rules, loading, and acceptance criteria.

## Required implementation

- Create repository-root `./rules` with first-cut JSON rule files for every currently identified legacy detection scenario required by [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md).
- Implement JSON schema validation for rule files.
- Implement the rule detection DSL with `match: all`, `match: any`, `match: none`, `conditions`, nested `groups`, `nodeKinds`, explicit condition kinds, and explicit operators.
- Support required condition kinds: target-framework-membership, namespace, symbol, package, file-pattern, method-call, attribute, and metric-threshold.
- Support required operators: Equal, NotEqual, GreaterThan, GreaterThanOrEqual, LessThan, LessThanOrEqual, In, NotIn, Contains, StartsWith, EndsWith, and MatchesPattern.
- Implement disk-based rule loading from copied output content for runtime projects.
- Upsert loaded rules into Neo4j using rule code and version.
- Evaluate rules after extraction and persist findings with rule code, rule version, severity, status, confidence, first/latest seen data, evidence links, node links, suppression fields, and metadata.
- Expose hotlist and rule catalog query APIs.

## Completion criteria

- All first-cut legacy, lifecycle, obsolete API, security-sensitive, data-access, configuration, dependency-risk, and architecture-smell rules required by [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) exist under `./rules`.
- Rule files are validated before loading.
- Nested boolean groups work exactly as specified by [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 27.5.
- Findings link to rules, nodes, evidence, snapshots, confidence, and suggested investigation context.
- Tests cover rule parsing, schema validation, invalid rules, DSL evaluation, nested groups, metric thresholds, built-in rules, disk loading, Neo4j upsert, finding persistence, and suppression fields.

---

# WP013 - Metrics, Hotspots, Architecture Rules, and Snapshot Diff

## Objective

Implement persisted architecture metrics, coupling/hotspot calculations, modernization metrics, architecture rule checks that depend on metrics or graph structure, and snapshot diff across nodes, edges, findings, and metrics.

## Mandatory source brief references

- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 12.7: metrics and generated summaries model.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 25 through 27: hotlist and findings inputs.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) sections 32 through 34: classification, metrics, and architecture rules.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 35 phase 7: snapshot diff and architecture drift, excluding graph UI.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) Appendix E sections E.4.6, E.4.8, E.5.2.9, E.5.9, E.6.9, and E.7.5: metrics and diff strategy.

## Required implementation

- Calculate project metrics: incoming project references, outgoing project references, package count, public type count, endpoint count, data-access count, hotlist finding count, and target-framework age/risk.
- Calculate graph metrics: fan-in, fan-out, centrality, dependency depth, transitive dependency count, cycle detection, and neighbourhood size.
- Calculate modernization metrics: legacy technology count, security-sensitive finding count, out-of-support target count, framework-only dependency count, data-access spread, and shared table usage count.
- Persist metrics as first-class snapshot outputs with stable keys and fingerprints.
- Implement architecture-rule checks for layering and dependency patterns described by [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 34 without hard-coding organization-specific rules beyond the configured rule catalog.
- Implement snapshot diff using stable keys and fingerprints for nodes, edges, findings, and metrics.
- Expose API endpoints for metrics, hotspots, architecture-rule results, and snapshot diff.

## Completion criteria

- Metrics are persisted during extraction and not recomputed as transient-only query values.
- Diff reports added, removed, changed, and unchanged records across nodes, edges, findings, and metrics.
- API consumers can retrieve project metrics, graph metrics, modernization metrics, hotspots, cycles, and snapshot diffs.
- Tests cover metric calculation, persistence, fingerprint comparison, added/removed/changed/unchanged diff cases, cycle detection, hotspot detection, and architecture-rule integration.

---

# WP014 - Query API Product Surface

## Objective

Implement the complete API query and management product surface required for non-UI consumers: project catalogue, project details, dependency traversal, dependents, dependency paths, symbol lookup, evidence drill-down, endpoint lookup, worker lookup, UI-technology fact lookup, data-access lookup, configuration usage, integration lookup, hotlist reports, metrics, snapshot diff, markdown export access, repository/solution management, rule catalog visibility, retention, health, readiness, and controlled maintenance.

## Mandatory source brief references

- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 8.1: API Query Module and API Management Module responsibilities.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) sections 28.1 through 28.11: informational needs originally described for UI must be satisfied through API responses in this API-first sequence, without UI pages.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 29.2: MCP tools depend on query capability.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 31: markdown export access.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) Appendix E sections E.5.8 and E.9: query model and acceptance criteria.

## Required implementation

- Implement project catalogue and project detail queries.
- Implement dependency, dependent, transitive dependency, transitive dependent, and dependency path queries with depth and edge-kind filters.
- Implement symbol lookup and symbol usage queries.
- Implement endpoint, controller, worker, hosted-service, queue/topic, configuration, integration, data-access, and .NET UI-technology fact queries.
- Implement evidence drill-down with file path, line range, symbol, snippet preview, confidence, and snapshot context.
- Implement hotlist, rule, finding, metric, hotspot, and snapshot diff queries.
- Implement repository registration, solution registration, metadata management, snapshot lifecycle, retention controls, rule catalog visibility, rule enablement controls, extraction run history, health, readiness, and controlled maintenance operations.
- Implement response-size limits, pagination, filtering, and stable DTO contracts suitable for MCP consumption.

## Completion criteria

- Every information need listed in [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 28 is available through API responses where it is not inherently visual.
- MCP can consume all required query capabilities without directly querying Neo4j.
- Management operations are available through controlled APIs.
- Tests cover every query family, filtering, pagination, authorization seams if present, management operations, retention behavior, and error responses.

---

# WP015 - MCP Server, Tools, Resources, Prompts, and Security

## Objective

Implement the complete read-only MCP server product capability for Copilot and other AI assistants, backed by the API/query/application layer and never by arbitrary shell, SQL, filesystem mutation, database mutation, or code modification capabilities.

## Mandatory source brief references

- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 6.3: MCP reference points.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 8.1: Archon MCP Server responsibilities.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 29: MCP design principle, tools, resources, prompts, and security requirements.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 30: Copilot workflows.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) Appendix C: example MCP tool response shape.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 35 phase 8 and section 36 MCP epic.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) Appendix E sections E.5.7 and E.5.8: MCP resource refresh and query model implications.

## Required implementation

- Implement read-only MCP server hosting.
- Implement all required MCP tools: `archon.search`, `archon.describe_project`, `archon.get_dependencies`, `archon.get_dependents`, `archon.find_dependency_paths`, `archon.describe_symbol`, `archon.find_symbol_usages`, `archon.get_data_access_usage`, `archon.assess_change_impact`, `archon.get_architecture_rules`, `archon.get_hotlist_findings`, and `archon.get_snapshot_diff`.
- Implement MCP resources: `archon://snapshot/current`, `archon://project/{projectKey}`, `archon://symbol/{symbolKey}`, `archon://rules/current`, `archon://hotlist/current`, `archon://hotspots/current`, and `archon://snapshot/{snapshotId}/diff/{previousSnapshotId}`.
- Implement MCP prompts: `impact-analysis`, `modernization-brief`, `refactoring-preflight`, `new-feature-placement`, `legacy-data-access-review`, `hotlist-summary`, and `architecture-rule-check`.
- Enforce read-only behavior, authentication, authorization, audit logging, tool allow-listing, environment isolation, no secrets exposure, response-size limits, and prompt-injection-aware output handling.
- Return evidence-backed responses with summary, confidence, facts, evidence, findings, unknowns, and suggested follow-ups.

## Completion criteria

- Every MCP tool, resource, and prompt listed by [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) is implemented and tested.
- MCP cannot execute shell commands, arbitrary SQL, arbitrary graph queries, filesystem mutation, database mutation, or code modification.
- MCP responses include evidence, confidence, unknowns, and response-size controls.
- Tests cover each tool, each resource, each prompt, read-only constraints, authentication/authorization seams, audit logging, prompt-injection handling, and large-response truncation.

---

# WP016 - Markdown Export and Generated Architecture Knowledge Base

## Objective

Implement generated markdown export as an output of the deterministic architecture model, with persisted generated summaries and API/MCP access to exported architecture knowledge.

## Mandatory source brief references

- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 31: markdown export structure and source-of-truth rule.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 8.1: markdown export access through the query module.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 12.7: generated summaries model.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 35 phase 9: markdown export and architecture knowledge base.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) Appendix E sections E.5.2.10, E.5.7, and E.6.10: generated summaries and markdown generation stage.

## Required implementation

- Generate markdown under the structure described by [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 31, including index, system overview, solution inventory, project catalogue, dependency map, runtime map, data access, integration map, modernization hotlist, coupling hotspots, and snapshot diff.
- Ensure markdown is generated from persisted Neo4j facts, not from AI-invented content or unpersisted transient state.
- Persist generated summaries as snapshot-owned generated summary nodes.
- Expose markdown export access through API endpoints.
- Expose relevant generated summary access through MCP resources or tools where appropriate.
- Include evidence links, confidence, unknowns, snapshot identity, and generation metadata in exported content.

## Completion criteria

- Markdown export can be regenerated deterministically for a snapshot.
- Exported content traces back to persisted graph facts and evidence.
- Generated summaries are persisted with stable keys and fingerprints.
- Tests cover each markdown section, deterministic output, evidence links, unknowns, API access, MCP access, and generated summary persistence.

---

# WP017 - End-to-End Product Hardening, Performance, Security, and Operational Readiness

## Objective

Harden the full API and MCP product capability for realistic repository use: performance, concurrency, cancellation, large solutions, legacy failure modes, security controls, observability, retention, health, readiness, operational maintenance, and complete end-to-end validation.

## Mandatory source brief references

- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) sections 1 through 5: platform principles and operational capability.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 8.1: host, API, management, graph, and MCP responsibilities.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 29.8: MCP security requirements.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 39: risks and mitigations for scope, Roslyn performance, legacy resolution, AI trust, and MCP security.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) Appendix E sections E.8 and E.9: risks, mitigations, and full acceptance criteria.

## Required implementation

- Implement cancellation and timeout handling for extraction and query operations.
- Implement safe parallelization and compilation caching where appropriate for Roslyn extraction.
- Implement large-response paging, filtering, truncation, and continuation behavior across API and MCP.
- Implement robust error and warning reporting for unresolved symbols, unsupported project formats, dynamic SQL, reflection, missing files, and partial workspace load failures.
- Implement retention behavior for snapshots and extraction run history.
- Implement health and readiness checks for API, MCP, Neo4j, rule loading, extraction dependencies, and query services.
- Implement audit logging for extraction requests, management operations, and MCP tool calls.
- Verify no secrets are exposed through evidence, metadata, API responses, MCP responses, or markdown exports.
- Validate the full system against representative modern, legacy, mixed-language, data-heavy, runtime-heavy, and UI-technology-containing .NET repositories.

## Completion criteria

- Full end-to-end extraction, persistence, query, MCP, diff, hotlist, metrics, and markdown export succeeds against representative repositories.
- Failures are captured as warnings, errors, unknowns, or failed run states without corrupting persisted graph state.
- API and MCP satisfy security, audit, and response-size requirements.
- Performance tests demonstrate acceptable behavior for large solutions and document limits.
- Operational documentation explains startup, extraction, rule loading, Neo4j initialization, retention, troubleshooting, and MCP use.

---

# WP018 - Final Completeness Verification Against the Source Brief

## Objective

Perform a strict final verification that the API-first and MCP-first Archon implementation satisfies every non-Discovery-UI requirement in [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) and that no extraction, graph, evidence, API, MCP, rule, finding, metric, diff, markdown, operational, or testing capability is left incomplete.

## Mandatory source brief references

- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) entire document, with explicit attention to sections 1 through 27, 29 through 34, 39 through 41, and Appendices A through E.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 28 and UI-related backlog entries only as excluded human-facing Discovery UI requirements, not as implemented product UI.
- [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) section 41 final summary, interpreted for this sequence as API and MCP exposing the complete architecture memory before Discovery UI work is considered.

## Required implementation

- Create a traceability matrix mapping every non-Discovery-UI requirement from [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) to implemented code, tests, API endpoint, MCP tool/resource/prompt, markdown export, or operational documentation.
- Confirm that every required node kind, edge kind, evidence kind, rule field, finding field, metric, generated summary field, stable-key prefix, extraction domain, query family, MCP tool, MCP resource, MCP prompt, and security requirement is implemented.
- Confirm that no work item is labeled as future, later, optional, stretch, deferred, or pending within the API/MCP scope.
- Confirm that Discovery UI work is absent by instruction and that no accidental UI host/page/component implementation was introduced.
- Run full build and relevant full test validation for the completed API/MCP product capability.
- Resolve every failing test or build issue unless it is conclusively proven unrelated to this work and documented.

## Completion criteria

- The traceability matrix proves complete implementation of the full API and MCP product capability described by [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md).
- The final validation report contains no deferred API, MCP, extraction, graph, evidence, rule, finding, metric, diff, markdown, or operational capability.
- The repository contains no Archon Discovery UI implementation from this sequence.
- Build and test validation succeed, or any unrelated environment failure is documented with evidence and no product completeness gap remains.

## Final strict completion statement

When WP018 is complete, the Archon API and MCP product capability described by [`docs/foundation/archon_full_concept_brief.md`](./archon_full_concept_brief.md) is fully implemented. There must be no remaining API, MCP, extraction, persistence, evidence, query, rule, finding, metric, diff, markdown, security, operational, or documentation capability left for later. Human-facing Archon Discovery UI delivery is not represented in this document because it is explicitly excluded until after the full API and MCP product capability is available.
