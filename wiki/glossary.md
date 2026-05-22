# Glossary

This glossary defines repository-specific terms used across the Archon wiki. Topic pages define terms in context when first introduced and link here when a contributor may need a central reference. Return to [home](home.md) for reader paths or [work-package documentation workflow](work-package-documentation-workflow.md) for maintenance rules.

## Accumulator

An accumulator is a stateful application-layer builder that accepts graph fact contributions and emits one assembled `ExtractedArchitectureSnapshot`. Archon's current accumulator is `ArchitectureSnapshotAccumulator`.

## AppHost

An AppHost is an Aspire project that describes which services, containers, and dependencies run together for local development. Archon's AppHost is `src/Archon`.

## ADO.NET

ADO.NET is the .NET data-access API family built around connections, commands, readers, adapters, and in-memory data containers. Archon extracts supported ADO.NET evidence statically from source code, including command text, execution methods, stored procedure command types, provider hints, and conservative table read/write hints. It does not open connections or execute command text.

## Architecture graph

The architecture graph is the durable representation of architecture facts, evidence, findings, metrics, and summaries. In the current persistence foundation, Neo4j stores this graph using stable labels, stable keys, fingerprints, and support relationships.

## Classic ASP.NET application

A classic ASP.NET application is a legacy `System.Web` web application whose runtime surface can be declared across project references, `Global.asax`, `web.config`, Web Forms markup, MVC 5 controllers, Web API 2 controllers, handlers, modules, and route configuration. Archon extracts these artifacts statically and does not run the target application.

## ASP.NET Core controller

An ASP.NET Core controller is a class that participates in MVC-style HTTP request handling. Archon recognizes supported controller classes from controller naming, controller base types, and marker attributes, then records `Controller` nodes and controller-to-action endpoint relationships when deterministic source evidence exists.

## ASP.NET Core middleware registration

An ASP.NET Core middleware registration is a startup pipeline call such as `UseRouting`, `UseAuthorization`, or `UseMiddleware<T>()`. Archon records supported calls as project-level runtime metadata in source order because the current graph contract does not yet define a dedicated middleware node kind.

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

Confidence is a deterministic value that describes how certain Archon is about a graph fact. The persisted domain model uses decimal confidence, while the current Roslyn intermediate model uses categories such as `CompilerResolved`, `MetadataOnly`, `Generated`, `PartiallyResolved`, `Inferred`, and `Unresolved` to preserve whether a fact came from compiler binding, external metadata, generated source, deterministic inference, or an explicit semantic gap.

## Configuration key

A configuration key is the logical path used by configuration systems to address a value, usually written with colon separators such as `Service:Endpoint`. Archon's modern configuration extractor normalizes JSON object nesting into this form, and the legacy configuration extractor prefixes XML concepts with categories such as `Legacy:AppSettings:`, `Legacy:ConnectionStrings:`, `Legacy:CustomSections:`, and `Legacy:BindingRedirects:`. Both forms are stored as `ConfigurationKey` nodes with centralized `config://` stable keys.

## ConfigurationManager

ConfigurationManager is the legacy `System.Configuration` API that .NET Framework applications commonly use to read `appSettings` and `connectionStrings` from XML `.config` files. Archon records compiler-bound `ConfigurationManager.AppSettings[...]` and `ConfigurationManager.ConnectionStrings[...]` source usage as `USES_CONFIG` graph facts.

## Global.asax

`Global.asax` is the classic ASP.NET application directive file that can point to an application class and lifecycle methods such as `Application_Start`. Archon records the directive and lifecycle source as classic runtime evidence.

## Constructor injection

Constructor injection is the dependency-injection pattern where a type declares required collaborators as constructor parameters. The current Roslyn C# relationship slice represents compiler-resolved constructor parameter types as `INJECTS` relationships because the constructor signature is deterministic source evidence for that collaboration boundary. The dependency-injection extractor also correlates registered implementation types with constructor parameters and emits deterministic `INJECTS` and `DEPENDS_ON` facts so registered service dependencies can be queried from the DI slice.

## Console entry point

A console entry point is the source method or top-level statement file where a command-line .NET application begins execution. Archon currently detects explicit C# `static Main` methods, explicit VB.NET `Sub Main` or `Function Main` methods, and C# top-level statements from submitted target repository projects, then represents them as `Method` facts with console runtime metadata.

## Background service

A background service is a hosted service that derives from `Microsoft.Extensions.Hosting.BackgroundService`. Archon marks registered implementations as background services when Roslyn proves that inheritance relationship during dependency-injection extraction, and the WP008 worker runtime slice emits source-proven background services as `HostedService` nodes with `runtimeKind` metadata of `BackgroundService`.

## Dependency injection

Dependency injection is the application-composition pattern where services receive collaborators from a container or composition root rather than constructing every dependency directly. Archon's current WP007 slice models Microsoft.Extensions.DependencyInjection registrations as graph facts when Roslyn can prove the registration method and participating types.

## Data access extraction

Data access extraction is Archon's static-analysis process for identifying repository evidence about database-facing technologies and mapping that evidence into the architecture graph. The current WP009 slice supports LINQ to SQL DBML model files, generated LINQ to SQL designer source, LINQ to SQL source usage, EF6 source artifacts, EF Core source artifacts, ADO.NET/raw SQL source usage, and typed DataSet XSD/generated-source/usage evidence. It does not connect to target databases, apply migrations, open ADO.NET connections, execute SQL command text, execute TableAdapter methods, or execute target application code.

## Data adapter

A data adapter is an ADO.NET component, such as `SqlDataAdapter` or `OleDbDataAdapter`, that fills `DataSet` or `DataTable` objects from a command. Archon treats supported adapter `Fill` calls as source evidence for raw SQL execution and conservative table reads when static command text is available.

## DBML

DBML is the XML model format used by LINQ to SQL designers to describe a database model, generated DataContext, mapped entity types, tables, columns, associations, and stored-procedure wrapper methods. Archon parses `.dbml` files as static XML artifacts and records DBML evidence with redacted snippets.

## DataContext

A DataContext is the LINQ to SQL unit-of-work type generated from a DBML model. In the current data-access graph, a DBML `Database` element with a class name becomes a `LinqToSqlDataContext` node.

## DataSet

A DataSet is an ADO.NET in-memory container for tabular data. Archon recognizes ordinary DataSet usage as part of ADO.NET extraction context, especially when a data adapter fills it from static SQL evidence. It also recognizes typed DataSet model roots from `.xsd` artifacts when the XML schema is marked as a generated DataSet model.

## DataTable

A DataTable is an ADO.NET in-memory representation of a table-like result set. In the current WP009 slice, ordinary DataTable variables are context evidence for ADO.NET usage, while typed DataSet XSD table declarations and generated DataTable classes become `Entity` nodes mapped to `DatabaseTable` and `DatabaseColumn` facts when the model metadata is deterministic.

## TableAdapter

A TableAdapter is a Visual Studio generated component in a typed DataSet model that wraps one or more database commands for a typed DataTable. Archon extracts TableAdapter definitions from typed DataSet `.xsd` files, represents generated TableAdapter artifacts as `GeneratedArtifact` nodes, records text queries as raw SQL facts, records stored-procedure commands as `StoredProcedure` facts, and links source methods to tables or procedures when generated TableAdapter methods are called.

## Typed DataSet

A typed DataSet is a legacy ADO.NET model generated from an `.xsd` schema. It provides strongly typed DataSet, DataTable, row, and TableAdapter classes for code that predates or avoids ORM patterns such as Entity Framework. Archon extracts typed DataSet facts statically from XSD artifacts and generated/consumer source; it does not run designer code, instantiate TableAdapters, or connect to the database described by the model.

## XSD model artifact

An XSD model artifact is an XML schema file that can describe a typed DataSet model. In WP009, Archon recognizes XSD files marked with typed DataSet metadata, reads DataSet names, DataTable declarations, column declarations, TableAdapters, query command text, stored procedure references, and connection-string property names, then emits graph facts with redacted XML evidence.

## DbCommand

DbCommand is the abstract ADO.NET command base type used by provider-independent code. Archon records executions through `DbCommand` parameters or variables even when the concrete provider is unknown, and it uses explicit unknown state when command text cannot be resolved statically.

## Database table node

A database table node is an architecture node representing a table identity extracted from data-access evidence. In the current DBML slice, a deterministic `Table Name="schema.Table"` value becomes a `DatabaseTable` node scoped by the DBML model path.

## Database column node

A database column node is an architecture node representing a column identity under a known database table. In the current DBML slice, deterministic `Column` metadata under a known table becomes a `DatabaseColumn` node.

## DbContext

A DbContext is the primary Entity Framework unit-of-work and mapping type. EF6 and EF Core context classes expose `DbSet<TEntity>` properties, provider configuration, model-building methods, and save operations. Archon represents EF6 and EF Core context types as `DbContext` graph nodes and records source-backed relationships from methods that use those contexts.

## DbSet

A DbSet is an Entity Framework property or object that represents a queryable and mutable collection of a specific entity type. Archon uses `DbSet<TEntity>` declarations to identify mapped entity types and conservative table dependencies, then links method usage to `DatabaseTable` nodes when the mapping can be determined.

## Dynamic SQL

Dynamic SQL is SQL text constructed from runtime values, concatenation, interpolation, formatting, or other computed expressions. Archon does not evaluate dynamic SQL. It records partial source evidence, a raw SQL fact, lower confidence, and an explicit unknown reason so consumers can see that a database command exists without trusting a guessed table or procedure target.

## Entity Framework 6

Entity Framework 6, often abbreviated EF6, is the legacy full-framework-era Entity Framework object-relational mapper that commonly uses `System.Data.Entity.DbContext`, `ObjectContext`, `DbSet<TEntity>`, Code First migrations, provider configuration, and raw SQL APIs such as `SqlQuery` or `ExecuteSqlCommand`. Archon extracts EF6 evidence statically from source and does not instantiate contexts or query databases.

## Entity Framework Core

Entity Framework Core, often abbreviated EF Core, is the modern cross-platform Entity Framework object-relational mapper that commonly uses `Microsoft.EntityFrameworkCore.DbContext`, `DbSet<TEntity>`, Fluent API model building, migrations, provider setup calls such as `UseSqlServer`, and raw SQL APIs such as `FromSqlRaw` or `ExecuteSqlRaw`. Archon extracts EF Core evidence statically from source and treats convention-only mappings and shadow properties as explicit uncertainty when exact database identity cannot be proven.

## EF migration

An EF migration is a source artifact that describes database schema changes for Entity Framework. Archon records migration classes and supported operations as generated artifact facts with source evidence. It never applies migrations during extraction.

## Fluent API mapping

Fluent API mapping is Entity Framework model configuration written through chained method calls, usually inside `OnModelCreating`. Examples include `.ToTable(...)`, `.Property(...).HasColumnName(...)`, and relationship configuration calls. Archon extracts deterministic mapping details from supported chains and records unknowns for source shapes that cannot be proven statically.

## ObjectContext

ObjectContext is a legacy Entity Framework context type from `System.Data.Entity.Core.Objects`. Archon represents ObjectContext-derived application types as `DbContext` graph nodes with metadata that identifies the legacy context kind.

## Shadow property

A shadow property is an EF Core model property that exists in the EF model but has no CLR property on the entity class. Archon records supported shadow-property column mappings with explicit unknown state because the database column can be observed from source mapping but there is no normal CLR property declaration to navigate to.

## Legacy container

A legacy container is a dependency-injection container other than Microsoft.Extensions.DependencyInjection, commonly found in older .NET systems. Archon's current dependency-injection extractor recognizes supported compiler-bound registration shapes for Unity, Autofac, Castle Windsor, StructureMap, Ninject, and SimpleInjector, and records unsupported scanning forms as explicit unknown registration facts instead of guessed service mappings.

## Manual factory

A manual factory is project code that creates an implementation behind an abstraction without a container registration call. Archon records narrow deterministic manual-factory patterns as medium-confidence heuristic composition facts when a source method returns an interface and directly constructs a concrete implementation of that interface.

## Custom host loop

A custom host loop is a source method that appears to run continuously or repeatedly, often through a loop combined with delay or cancellation checks. Archon records these as medium-confidence method-level runtime facts because the pattern suggests a service runner or polling loop but does not prove deployment behavior.

## Secret redaction

Secret redaction is the process of replacing sensitive configuration values with a placeholder before they can appear in evidence previews, metadata, diagnostics, logs, or test output. The configuration extractor applies redaction to appsettings values, legacy XML connection-string values, custom section payloads, source snippets, and diagnostics before graph records are created.

## Raw SQL

Raw SQL is command text written directly in source or model artifacts instead of expressed through a higher-level mapping abstraction. Archon stores only redacted previews and stable hashes for static raw SQL text, classifies simple statements with read/write hints, and records unknowns for dynamic or missing command text.

## Read/write hint

A read/write hint is metadata that classifies a data-access relationship as a likely `Read`, `Write`, `ReadWrite`, or `Unknown` operation. Archon derives hints conservatively from API shape and leading SQL verbs such as `SELECT`, `INSERT`, `UPDATE`, `DELETE`, `MERGE`, or `CREATE`; it does not claim full database impact analysis.

## Stored procedure command

A stored procedure command is an ADO.NET command whose `CommandType` is set to `StoredProcedure` and whose command text names a procedure. Archon represents the procedure as a `StoredProcedure` node and links the source method to it with `CALLS_STORED_PROCEDURE` when the name is static.

## Factory registration

A factory registration is a dependency-injection registration where a delegate creates or retrieves the implementation at runtime. Archon records the service type and lifetime for supported Microsoft DI factory overloads but uses explicit unknown state when the concrete implementation cannot be proven from the registration itself.

## Registration wrapper

A registration wrapper is a method that accepts `IServiceCollection` and delegates one or more Microsoft dependency-injection registrations to another method body. Archon's DI extractor follows compiler-bound wrappers when source bodies are available, records invocation-chain metadata, and emits warnings rather than guessed registrations when wrapper source, dynamic dispatch, recursion depth, or cycles prevent deterministic traversal.

## Constraint

A constraint is a Neo4j-enforced rule, such as uniqueness of repository stable keys or snapshot-scoped architecture node identities.

## Controlled value

A controlled value is a domain-owned string identity that behaves like a smart enum. It avoids numeric enum drift by using stable external strings instead of serialized numeric ordinals.

## Evidence-first

Evidence-first means graph facts are designed to carry or link to the explanation that caused Archon to believe them. Evidence can be a project file, source symbol, configuration artifact, compiler diagnostic, inference, or manual annotation.

## Dynamic dispatch

Dynamic dispatch is a C# runtime-binding pattern where a `dynamic` receiver chooses the member target at runtime rather than through compile-time symbol binding. Archon records dynamic dispatch as a semantic unknown instead of inventing a resolved `CALLS` relationship.

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

## Worker service

A worker service is a .NET application that uses the generic host to run background work, commonly through `IHostedService` or `BackgroundService`, rather than exposing HTTP endpoints as its primary runtime surface. Archon's WP008 worker slice represents source-proven worker components as hosted-service runtime facts and project-level generic-host setup metadata.

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

## Generated source

Generated source is code produced by tools, designers, source generators, or build steps rather than directly maintained by contributors. Archon detects deterministic generated-code signals such as `.g.cs`, `.g.vb`, `.Designer.cs`, `.Designer.vb`, generated folders, `obj` paths, and auto-generated headers, then marks semantic facts from that source instead of discarding them.

## Generic host

The generic host is the Microsoft.Extensions.Hosting runtime model that starts an application, manages dependency injection, configuration, logging, lifetime events, and hosted services. Archon's worker runtime slice records deterministic generic-host setup calls as project metadata and does not start or evaluate the target host.

## Handler identity

Handler identity is the deterministic source or symbol identity Archon uses to distinguish runtime handlers such as minimal API delegates, controller action methods, queue message handlers, scheduled-job methods, and entry points. It is used in stable keys and metadata so two runtime facts with the same route or target name can still be distinguished by the code that handles them.

## Legacy configuration artifact

A legacy configuration artifact is a repository-contained XML `.config` file such as `app.config` or `web.config`. Archon parses supported XML concepts from these files as static evidence and does not execute configuration section handlers, transforms, or machine-level configuration during extraction.

## Unknown-source provider

An unknown-source provider is an explicit unknown state used when source code references a configuration key but no matching provider definition was discovered in repository configuration artifacts. The fact preserves the source dependency without claiming that the repository contains the matching setting.

## Graph fact

A graph fact is a domain object that states something Archon knows about an architecture snapshot, such as a repository, solution, node, edge, evidence record, finding, metric, or generated summary.

## Graph recreation

Graph recreation is a deliberately destructive local/test workflow that deletes Archon-owned graph records and then recreates constraints and indexes. It is not a migration, repair mechanism, production endpoint, or startup hook.

## Graph schema

A graph schema is the set of Neo4j constraints and indexes that make graph writes safe and queryable.

## Hosted service

A hosted service is a service that participates in the .NET generic host lifecycle through `Microsoft.Extensions.Hosting.IHostedService`. Archon's DI extractor records `AddHostedService<T>()` and hosted-service-assignable registrations as `REGISTERED_AS_SERVICE` graph facts with hosted-service metadata. The WP008 worker runtime slice can also emit source-proven hosted services as `HostedService` nodes and correlate them with those prior registration facts.

## HttpClientFactory

HttpClientFactory is the Microsoft DI pattern for creating configured `HttpClient` instances through `AddHttpClient` registrations. Archon's current extractor records default, named, typed, and typed-implementation HttpClient registrations and marks unresolved external targets as explicit unknown data.

## HTTP handler

An HTTP handler is a classic ASP.NET type that implements `IHttpHandler` and can directly process requests, often through a `web.config` mapping. Archon represents handler types as `Type` nodes and configured handler paths as `Endpoint` nodes connected by `HANDLES` relationships.

## HTTP module

An HTTP module is a classic ASP.NET type that implements `IHttpModule` and participates in the request pipeline without declaring a single endpoint. Archon represents modules as `Type` nodes with runtime metadata.

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

## Metadata-only symbol

A metadata-only symbol is a Roslyn symbol resolved from a referenced assembly or package without a source declaration inside the analyzed repository. Archon represents dependencies on metadata-only symbols with deterministic symbol-reference stable keys and `MetadataOnly` confidence rather than inventing repository source nodes.

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

## Partial declaration

A partial declaration is one source declaration that contributes to a compiler symbol assembled from multiple source spans, such as a C# or VB.NET partial class. Archon merges partial declarations by Roslyn symbol identity and records additional evidence contributions so the graph does not double-count the type while still preserving each contributing source span.

## REFERENCES edge

A `REFERENCES` edge is an architecture relationship showing that one graph node directly references another. In the current WP005 project extraction stage, a project-to-project `REFERENCES` edge means a source project file declared a `ProjectReference` to the target project file.

## REGISTERED_AS_SERVICE edge

A `REGISTERED_AS_SERVICE` edge is an architecture relationship showing that an implementation type has been registered to satisfy a service abstraction in a dependency-injection container. In the current WP007 Microsoft DI slice, the edge points from the implementation type node to the service abstraction type node and carries lifetime metadata such as `Singleton`, `Scoped`, or `Transient`.

## Service registration

A service registration is a source statement that tells a dependency-injection container which implementation should satisfy a service request and what lifetime should govern the produced instance. Examples in the current WP007 slice include Microsoft DI calls such as `AddSingleton<TService, TImplementation>()`, service-only overloads, `typeof(...)` overloads, factory overloads, `TryAdd`, `TryAddEnumerable`, `Replace`, `AddHostedService<T>()`, and `AddHttpClient`, plus supported legacy container calls such as Unity `RegisterType`, Autofac `RegisterType().As`, Castle Windsor `Register(Component.For)`, StructureMap `For().Use`, Ninject `Bind().To`, and SimpleInjector `Register`.

## Service locator

A service locator is a global or ambient resolver that application code asks for services directly. Archon detects CommonServiceLocator `GetInstance<TService>()` as a medium-confidence heuristic fact because the requested service type is compiler-bound but the concrete implementation is not deterministically visible at the call site.

## Typed HttpClient

A typed HttpClient is a class or abstraction registered through `AddHttpClient<TClient>()` or `AddHttpClient<TClient, TImplementation>()` so callers consume a domain-specific client rather than requesting `HttpClient` or `IHttpClientFactory` directly.

## USES_PACKAGE edge

A `USES_PACKAGE` edge is an architecture relationship showing that a project directly uses a NuGet package. In the current WP005 project extraction stage, this edge can come from an SDK-style `PackageReference` declaration or a legacy `packages.config` entry and carries source-type, version-source, target framework, and asset metadata when those values are available.

## SDK-style project

An SDK-style project is an MSBuild project whose root `<Project>` element declares an `Sdk` attribute such as `Microsoft.NET.Sdk`. Archon reads that XML as metadata and does not execute build targets to identify the project style.

## Old-style project

An old-style project is a non-SDK-style MSBuild project, often using the legacy MSBuild XML namespace and properties such as `TargetFrameworkVersion`. Archon records it as old-style when no root `Sdk` attribute is present.

## Run lifecycle

A run lifecycle is the operational status model for an accepted extraction request. It records states such as queued, running, completed, failed, or cancelled together with progress, warnings, errors, timestamps, and snapshot identity when available.

## Runtime extraction slice

A runtime extraction slice is a static-analysis contribution that identifies runtime-facing application behavior, such as an HTTP endpoint, console entry point, hosted service, scheduled job, or message consumer, without executing the analyzed application. The current WP008 slices recognize direct ASP.NET Core minimal API mappings, endpoint groups, attributed controllers and actions, MVC setup calls, middleware registration order, OpenAPI setup in C# source, console entry points in C# and VB.NET source, worker hosted-service source, scheduled-job source, queue/topic consumer source, service-host setup source, custom host-loop source, and extractor-level classic ASP.NET artifacts such as `Global.asax`, Web Forms, handlers, modules, MVC 5, Web API 2, and route configuration.

## Scheduled job

A scheduled job is runtime work registered with a scheduler so it can run at a time, interval, or recurrence expression. Archon represents supported scheduled jobs as method-level runtime facts and records a schedule expression only when source evidence provides a compile-time literal.

## Topic consumer

A topic consumer is a message consumer that reads from a publish/subscribe target, often using both a topic name and a subscription name. Archon represents supported topic consumers as `Topic` nodes with transport, topic, and subscription metadata when those values are deterministic.

## Web Forms page

A Web Forms page is a classic ASP.NET `.aspx` markup artifact that is addressable by a virtual path. Archon represents supported pages as `Endpoint` nodes with Web Forms runtime metadata and markup evidence.

## Web Forms user control

A Web Forms user control is a classic ASP.NET `.ascx` markup artifact that can participate in page rendering but is not directly addressable as an endpoint by itself. Archon represents supported user controls as `FilePath` nodes with user-control runtime metadata.

## Endpoint group

An endpoint group is an ASP.NET Core minimal API route prefix created with `MapGroup`. Archon combines a literal group prefix with literal endpoint route templates and records explicit unknown state when the group prefix is computed.

## OpenAPI setup

OpenAPI setup is source-level configuration that enables OpenAPI or Swagger descriptions for an ASP.NET Core application, such as `AddSwaggerGen`, `AddOpenApi`, `UseSwagger`, `UseSwaggerUI`, or `MapOpenApi`. Archon records supported setup calls as project-level runtime metadata rather than invoking the application to generate a document.

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

## Stored procedure node

A stored procedure node is an architecture node representing a database stored procedure or DBML function wrapper target that static evidence can identify. In the current DBML slice, deterministic `Function Name="schema.Procedure"` metadata becomes a `StoredProcedure` node connected to the DataContext by `CALLS_STORED_PROCEDURE`.

## Testcontainers

Testcontainers is a test library that starts short-lived Docker containers under test control and removes them after tests. Archon uses it for real Neo4j integration tests without starting the Aspire AppHost.

## Target framework

A target framework is the .NET platform moniker or legacy framework version a project builds for, such as `net10.0`, `net8.0`, or `v4.7.2`. Archon records single-target, multi-target, and legacy target framework values from project-file metadata when available.

## Target repository context

Target repository context is the accepted repository root, submitted solution path list, and repository-relative project/source paths that scope extraction to the application being analyzed. Runtime extractors use this context to avoid confusing target application entry points with Archon host code, test harness code, package-cache files, or arbitrary files outside the submitted extraction input.

## Top-level statements

Top-level statements are C# statements written directly in a source file, usually `Program.cs`, instead of inside an explicit `Main` method. The C# compiler turns them into an implicit entry point; Archon represents that implicit entry point with method metadata keyed by the project and normalized repository-relative file path.

## Unknown state

Unknown state records whether a fact contains unknown data and, when it does, the reason the data is unknown. Facts that use unknown knowledge or declare unknown data must carry a non-empty reason.

## Work-package implementation record

The work-package implementation record is the concise historical status retained in a plan after work completes: what changed, what validation ran, and what wiki review outcome was recorded. It must not become a parallel source of contributor-facing guidance; current-state guidance belongs in the wiki.
