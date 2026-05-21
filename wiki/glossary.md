# Glossary

This glossary defines repository-specific terms used across the Archon wiki. Topic pages define terms in context when first introduced and link here when a contributor may need a central reference. Return to [home](home.md) for reader paths or [work-package documentation workflow](work-package-documentation-workflow.md) for maintenance rules.

## Accumulator

An accumulator is a stateful application-layer builder that accepts graph fact contributions and emits one assembled `ExtractedArchitectureSnapshot`. Archon's current accumulator is `ArchitectureSnapshotAccumulator`.

## AppHost

An AppHost is an Aspire project that describes which services, containers, and dependencies run together for local development. Archon's AppHost is `src/Archon`.

## Architecture graph

The architecture graph is the durable representation of architecture facts, evidence, findings, metrics, and summaries. In the current persistence foundation, Neo4j stores this graph using stable labels, stable keys, fingerprints, and support relationships.

## Application type classification

Application type classification is the WP005 project metadata value that places a supported project into a broad category such as ASP.NET Core Web App, ASP.NET Core Web API, Classic ASP.NET Web App, Web Forms App, MVC App, Web API 2 App, Console App, Worker Service, Class Library, Test Project, Tooling Project, or Unknown. Archon records confidence, evidence, and unknown reasons with the classification so consumers can distinguish direct project-file evidence from weaker artifact or naming indicators.

## Architecture relationship

An architecture relationship is the domain fact that one architecture node relates to another. Examples include a project referencing a package, a service calling an endpoint, or a component depending on configuration.

## Asynchronous extraction

Asynchronous extraction means the API start request validates and accepts work, records a run, queues the work through a scheduler seam, and returns before later extraction, snapshot assembly, or persistence finishes.

## Bolt-compatible URI

A Bolt-compatible URI is the address used by the Neo4j driver protocol, such as `bolt://localhost:7687`. It is separate from Neo4j's HTTP browser endpoint.

## Composition root

A composition root wires runtime resources and services together. Archon's Aspire AppHost is a composition root: it declares Neo4j, the API host, and the MCP host, but it does not implement domain rules or feature behavior.

## Confidence

Confidence is a deterministic value that describes how certain Archon is about a graph fact. The persisted domain model uses decimal confidence, while the current Roslyn intermediate model uses categories such as `CompilerResolved` to preserve whether a relationship came from compiler binding or from a weaker future inference path.

## Constructor injection

Constructor injection is the dependency-injection pattern where a type declares required collaborators as constructor parameters. The current Roslyn C# relationship slice represents compiler-resolved constructor parameter types as `INJECTS` relationships because the constructor signature is deterministic source evidence for that collaboration boundary.

## Constraint

A constraint is a Neo4j-enforced rule, such as uniqueness of repository stable keys or snapshot-scoped architecture node identities.

## Controlled value

A controlled value is a domain-owned string identity that behaves like a smart enum. It avoids numeric enum drift by using stable external strings instead of serialized numeric ordinals.

## Evidence-first

Evidence-first means graph facts are designed to carry or link to the explanation that caused Archon to believe them. Evidence can be a project file, source symbol, configuration artifact, compiler diagnostic, inference, or manual annotation.

## Evidence span

An evidence span is the source line range associated with an evidence record. When XML parsers expose line information, Archon records the exact line for project references, analyzer references, package references, and legacy package entries; otherwise it falls back to file-level evidence.

## Roslyn semantic model

A Roslyn semantic model is the compiler object that answers symbol and type questions for a specific syntax tree in a compilation. Archon uses semantic models so source declarations can be extracted from compiler-resolved symbols rather than from text matching alone.

## Semantic declaration fact

A semantic declaration fact is the Roslyn layer's graph-ready intermediate representation of a source declaration such as a namespace, type, constructor, method, property, or field. VB.NET modules, structures, interfaces, enums, delegates, default properties, events, and constants are projected into this shared declaration vocabulary. A semantic declaration fact carries source language, declaration kind, symbol identity, stable key, project context, parent declaration identity, and semantic evidence.

## Visual Basic default property

A Visual Basic default property is a property that can be accessed through an indexed object expression without writing the property name. Archon projects default properties as `Property` declaration facts and records property-access dependencies when Roslyn resolves the property symbol.

## Visual Basic module

A Visual Basic module is a compiler-backed type whose members are shared by default. Archon projects modules as `Type` declaration facts so module members participate in the same graph vocabulary as C# static classes and other source-declared types.

## Visual Basic root namespace

A Visual Basic root namespace is project-level namespace text that the Visual Basic compiler composes with namespaces declared in source files. When Roslyn exposes the composed namespace in semantic symbols, Archon uses that fully qualified namespace in declaration and relationship facts.

## Visual Basic shared member

A Visual Basic shared member is a member accessed on a type rather than on a particular instance. Archon records calls and dependencies for shared members through the same `CALLS` and `DEPENDS_ON` relationship vocabulary used for C# static members.

## Semantic evidence

Semantic evidence is source evidence captured for compiler-backed declarations and relationships. It includes repository-relative file path, one-based line and column span, symbol name, containing symbol, snippet preview, and snippet hash when source text is available.

## Semantic relationship fact

A semantic relationship fact is the Roslyn layer's graph-ready intermediate representation of a compiler-backed relationship such as `CONTAINS`, `CALLS`, `IMPLEMENTS`, `INHERITS`, `INJECTS`, or `DEPENDS_ON`. It carries deterministic endpoint keys, source and target symbol identity where available, confidence, evidence, relationship metadata, and optional unknown-reason data.

## Semantic stable key

A semantic stable key is the deterministic logical identity used by Roslyn extraction before domain persistence projection. Current C# declaration keys are scoped by source language, project context, fully qualified symbol name, and compiler-facing metadata name or signature; relationship keys are derived from relationship kind, endpoint keys, and a relationship-source qualifier; symbol-reference keys identify relationship endpoints that do not have a source declaration fact.

## Extraction pipeline

The extraction pipeline is the application-layer sequence of deterministic stages that contribute facts, warnings, or errors to the shared accumulator for one accepted run.

## Extraction stage

An extraction stage is one named unit of pipeline work with a stable stage identifier. A stage receives validated input, accepted run context, and the accumulator, then reports whether the pipeline can continue.

## Evidence deduplication

Evidence deduplication means equivalent evidence payloads submitted more than once in the same snapshot collapse to one canonical `ArchonEvidence` node. Equivalent evidence in different snapshots remains separate.

## Fingerprint

A fingerprint is a deterministic hash of diff-relevant graph content. A stable key asks whether two records represent the same logical fact; a fingerprint asks whether the content of that fact changed.

## Generated summary

A generated summary is durable narrative or report-ready content associated with a snapshot or graph target. Summary generation behavior remains later work, but persistence can store supplied generated summaries.

## Graph fact

A graph fact is a domain object that states something Archon knows about an architecture snapshot, such as a repository, solution, node, edge, evidence record, finding, metric, or generated summary.

## Graph recreation

Graph recreation is a deliberately destructive local/test workflow that deletes Archon-owned graph records and then recreates constraints and indexes. It is not a migration, repair mechanism, production endpoint, or startup hook.

## Graph schema

A graph schema is the set of Neo4j constraints and indexes that make graph writes safe and queryable.

## Idempotent

An operation is idempotent when it can run repeatedly without changing the result after the first successful run. Schema initialization is idempotent because it uses `CREATE ... IF NOT EXISTS` statements.

## Index

An index is a Neo4j lookup structure that helps queries find records by stable key, snapshot scope, kind, status, confidence, knowledge classification, fingerprint, or other indexed properties.

## Knowledge kind

Knowledge kind describes whether a fact is direct, inferred, explicitly unknown, or human-confirmed.

## Liveness

Liveness answers whether a process is responsive. Archon exposes liveness through `/alive`.

## Metadata

Metadata is deterministic extension data for extractor-specific details that do not belong in normalized graph properties. Metadata must not hide fields that the platform expects to query, compare, or validate directly.

## Metric

A metric is a durable computed value for a graph, snapshot, project, architecture node, architecture relationship, or modernization scope.

## Neo4j

Neo4j is the graph database used by Archon as the system of record for deterministic architecture facts.

## Normalized property

A normalized property is a graph field stored directly because later code needs to query, compare, or validate it. Examples include stable keys, graph kinds, evidence kinds, confidence, unknown-state values, and fingerprints.

## Onion Architecture

Onion Architecture is a dependency model where stable core concepts sit at the center and replaceable delivery or infrastructure details sit at the outside. Dependencies should point inward.

## Orchestrator

An orchestrator is the application-layer component that coordinates one accepted asynchronous extraction run through pipeline execution, snapshot assembly, persistence handoff, and run lifecycle updates.

## Persistence handoff

Persistence handoff is the application-layer boundary where an assembled `ExtractedArchitectureSnapshot` is given to an `IArchitectureSnapshotWriter` implementation and the returned result controls completion or failure status.

## Readiness

Readiness answers whether a process is ready to accept work. Archon exposes readiness through `/health`.

## Scalar API reference

The Scalar API reference is the development-time browser UI for Archon's generated OpenAPI document. `ArchonApi` exposes it at `/scalar/v1` in the Development environment, backed by the OpenAPI document at `/openapi/v1.json`.

## Recent run history

Recent run history is the operational list of accepted extraction runs returned by `GET /extractions`. It is ordered deterministically newest first and summarizes run state without becoming the durable architecture graph.

## Placeholder stage

The placeholder stage was the early extraction stage that proved the pipeline boundary by contributing a warning without inventing real repository, Roslyn, runtime, UI, data-access, markdown, MCP, rule, or architecture facts. The current extraction composition has moved beyond that placeholder for repository and submitted-solution facts.

## Project extraction stage

The project extraction stage is the WP005 pipeline stage family that reads submitted solution and project artifacts and contributes graph facts to the shared extraction accumulator. The current `project-repository-solution` stage reads explicitly submitted solution files, extracts supported C# and VB.NET project files declared by those solutions, follows repository-contained project-reference targets, reads deterministic local package, analyzer, and build-artifact declarations, classifies application types, and contributes repository nodes, solution nodes, project nodes, package nodes, FilePath nodes, `CONTAINS`, `REFERENCES`, and `USES_PACKAGE` relationships, evidence records, controlled warnings, and unsupported-project diagnostics.

## Project node

A project node is an architecture node representing a supported C# or VB.NET project file. Its stable key is based on the repository-relative project path so the same project declared by multiple submitted solutions remains one graph identity. Current project node metadata includes application type classification when WP005 can determine or explicitly preserve that category as Unknown.

## Package node

A package node is an architecture node representing a NuGet package dependency extracted from a supported project file or safe imported build file. Its stable key uses the normalized package ID and known version or explicit version-source state.

## FilePath node

A FilePath node is an architecture node representing a repository-contained source artifact path. In WP005, FilePath nodes are used for submitted solutions, supported project files, package manifests, local central package files, repository-contained imported build files, and repository-contained analyzer assemblies that support extracted facts.

## Analyzer reference

An analyzer reference is an MSBuild `Analyzer` item that points at a compiler analyzer assembly. Archon reads analyzer declarations from project XML as metadata and evidence without loading or running the analyzer.

## Imported build artifact

An imported build artifact is a repository-contained `.props` or `.targets` file that a project imports and that can be inspected safely as static XML. Archon excludes imports that require property expansion, wildcard traversal, missing files, or outside-repository paths because those cases would require build evaluation or external state.

## PackageReference

A `PackageReference` is an MSBuild item that declares a NuGet package dependency for an SDK-style project. Archon reads direct package IDs, versions, asset metadata, aliases, and safe repository-contained imported declarations without running restore or contacting package feeds.

## packages.config

A `packages.config` file is the legacy NuGet package manifest commonly found beside old-style .NET Framework project files. Archon reads valid package entries from a sibling repository-contained file as static XML, preserves package ID, version, target framework, and evidence line information, and represents those dependencies through the same package node and `USES_PACKAGE` relationship model used by SDK-style package references.

## Central Package Management

Central Package Management is the NuGet/MSBuild pattern where project files omit package versions and a `Directory.Packages.props` file supplies `PackageVersion` declarations. Archon resolves local deterministic central versions from repository-contained files only.

## ProjectReference

A `ProjectReference` is an MSBuild item in a C# or VB.NET project file that declares a direct dependency on another project file. Archon records the raw include path as evidence, resolves repository-contained targets to project nodes when possible, and represents resolved dependencies as `REFERENCES` edges.

## REFERENCES edge

A `REFERENCES` edge is an architecture relationship showing that one graph node directly references another. In the current WP005 project extraction stage, a project-to-project `REFERENCES` edge means a source project file declared a `ProjectReference` to the target project file.

## USES_PACKAGE edge

A `USES_PACKAGE` edge is an architecture relationship showing that a project directly uses a NuGet package. In the current WP005 project extraction stage, this edge can come from an SDK-style `PackageReference` declaration or a legacy `packages.config` entry and carries source-type, version-source, target framework, and asset metadata when those values are available.

## SDK-style project

An SDK-style project is an MSBuild project whose root `<Project>` element declares an `Sdk` attribute such as `Microsoft.NET.Sdk`. Archon reads that XML as metadata and does not execute build targets to identify the project style.

## Old-style project

An old-style project is a non-SDK-style MSBuild project, often using the legacy MSBuild XML namespace and properties such as `TargetFrameworkVersion`. Archon records it as old-style when no root `Sdk` attribute is present.

## Run lifecycle

A run lifecycle is the operational status model for an accepted extraction request. It records states such as queued, running, completed, failed, or cancelled together with progress, warnings, errors, timestamps, and snapshot identity when available.

## Relationship-node pattern

The relationship-node pattern stores an architecture edge as an `ArchonRelationship` node instead of only as a native Neo4j relationship. This lets the relationship fact carry its own stable key, fingerprint, metadata, confidence, unknown state, and evidence links.

## Repository-relative path

A repository-relative path is written from the repository root rather than from a developer's machine root. Repository-relative paths keep stable keys deterministic across workstations and CI agents.

## Service defaults

Service defaults are shared host configuration used by runtime processes. In Archon, `src/Archon.ServiceDefaults` configures health checks, OpenTelemetry-compatible telemetry, service discovery, and HTTP client resilience.

## Snapshot assembly

Snapshot assembly is the process of gathering many graph fact contributions into one in-memory `ExtractedArchitectureSnapshot` before persistence or presentation.

## Snapshot persistence

Snapshot persistence writes an assembled `ExtractedArchitectureSnapshot` into Neo4j using stable logical identities and support relationships.

## Stable key

A stable key is the durable logical identity for an architecture fact. It is not a database ID, process-local object reference, or machine-specific path.

## Testcontainers

Testcontainers is a test library that starts short-lived Docker containers under test control and removes them after tests. Archon uses it for real Neo4j integration tests without starting the Aspire AppHost.

## Target framework

A target framework is the .NET platform moniker or legacy framework version a project builds for, such as `net10.0`, `net8.0`, or `v4.7.2`. Archon records single-target, multi-target, and legacy target framework values from project-file metadata when available.

## Unknown state

Unknown state records whether a fact contains unknown data and, when it does, the reason the data is unknown. Facts that use unknown knowledge or declare unknown data must carry a non-empty reason.

## Work-package implementation record

The work-package implementation record is the concise historical status retained in a plan after work completes: what changed, what validation ran, and what wiki review outcome was recorded. It must not become a parallel source of contributor-facing guidance; current-state guidance belongs in the wiki.
