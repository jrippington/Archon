# WP009 Specification - Data Access Extraction

## Document Control

| Field | Value |
| --- | --- |
| Work Package | WP009 - Data Access Extraction |
| Output Path | `docs/009-Data-Access-Extraction/spec-wp009-data-access-extraction.md` |
| Source Work Package | `docs/foundation/work-packages.md` WP009 |
| Source Brief | `docs/foundation/archon_full_concept_brief.md` |
| Specification Basis | `spec-template_v1.1.md` requested; template file was not present in the workspace at creation time, so this document follows the repository specification structure and documented work-package rules. |
| Status | Draft |
| Audience | Product owner, architect, implementation team, test engineer |

## 1. Overview

### 1.1 Purpose

This specification defines the requirements for WP009, the Archon work package that extracts data-access architecture facts from analyzed .NET repositories. The package identifies LINQ to SQL, `.dbml` models, generated designer files, Entity Framework Classic / EF6, Entity Framework Core, ADO.NET, raw SQL, stored procedures, and typed DataSets.

WP009 builds on the prior repository, project, package, semantic symbol, configuration, dependency-injection, runtime, snapshot orchestration, and Neo4j persistence foundations. It must contribute evidence-backed data-access nodes, relationships, metadata, confidence, warnings, and unknowns through the established extraction pipeline rather than introducing a separate persistence or query path.

### 1.2 Background

Archon provides deterministic, evidence-backed architecture intelligence for modern and legacy .NET estates. Data access is central to that mission because databases, tables, stored procedures, raw SQL, and legacy data-access frameworks often represent the strongest coupling and modernization risk in long-lived enterprise systems.

The controlling work-package sequence is API-first and MCP-first. WP009 therefore focuses on backend extraction and graph population only. Human-facing database explorers, data-access dashboards, schema viewers, stored-procedure viewers, and other Archon Discovery UI surfaces remain excluded.

### 1.3 High-Level Scope

WP009 covers these data-access extraction areas:

- LINQ to SQL `.dbml` model parsing.
- LINQ to SQL generated designer file extraction.
- LINQ to SQL usage extraction from source code.
- Entity Framework Classic / EF6 context, entity, mapping, migration, provider, raw SQL, and usage extraction.
- Entity Framework Core context, entity, mapping, migration, provider, raw SQL, and usage extraction.
- ADO.NET connection, command, reader, adapter, dataset, raw SQL, stored procedure, dynamic SQL, and read/write-hint extraction.
- Typed DataSet and `.xsd` model extraction.
- Data-access node, relationship, metadata, evidence, confidence, warning, and unknown emission.
- Tests for all production behavior introduced by this work package.
- Documentation updates explaining supported data-access extraction behavior and validation.

WP009 excludes Archon Discovery UI, broad external-integration extraction, API query product surface expansion, MCP tools, rule-engine evaluation, hotlist generation, markdown export, snapshot diff, database connectivity, database schema introspection from live databases, and direct Neo4j writes from extractor projects.

## 2. System Context

### 2.1 Product Context

Archon accepts API-triggered extraction requests, loads submitted repositories and explicit solution paths, extracts deterministic architecture facts, persists them in Neo4j, and later exposes them through API and MCP surfaces. WP009 contributes the data-access slice of the architecture graph.

The package must use the single extraction orchestration path created earlier in the sequence. It must not scan arbitrary directories independently of the submitted extraction request, bypass the snapshot contract, connect to target databases, execute analyzed repository code, or persist data directly outside the established graph persistence adapter.

### 2.2 Source References

WP009 must align with these source materials:

- `docs/foundation/work-packages.md` WP009 objective, required implementation, and completion criteria.
- `docs/foundation/archon_full_concept_brief.md` section 23 for data access extraction and all subsections.
- `docs/foundation/archon_full_concept_brief.md` section 26.3 for legacy data-access hotlist inputs.
- `docs/foundation/archon_full_concept_brief.md` section 35 phase 3 and section 36 LINQ to SQL epic.
- `docs/foundation/archon_full_concept_brief.md` Appendix E sections E.6.6 and E.7.4 for data-access extraction support.
- `docs/foundation/work-packages.md` completion rules for evidence-backed facts, explicit unknowns, deterministic stable keys, Neo4j as the system of record, tests, documentation, and no Discovery UI.

### 2.3 Users and Stakeholders

| Stakeholder | Interest |
| --- | --- |
| Product owner | Confirms data-access extraction satisfies WP009 scope without UI delivery or deferred mandatory behavior. |
| Architect | Confirms database contexts, entities, tables, columns, stored procedures, SQL usage, and read/write dependencies are represented consistently in the graph. |
| Developer | Uses extracted facts to understand data-access technologies, database coupling, table usage, stored-procedure usage, and modernization risks. |
| Test engineer | Verifies detection coverage, evidence quality, confidence, unknown handling, and extraction-pipeline integration. |
| Future API consumer | Depends on persisted data-access facts being complete enough for query APIs in later work packages. |
| Future MCP consumer | Depends on evidence-backed data-access facts for impact analysis and Copilot workflows in later work packages. |

## 3. Component Summary

### 3.1 LINQ to SQL Model Extractor

The LINQ to SQL model extractor parses `.dbml` files and extracts DataContext names, database names, connection metadata, tables, columns, associations, functions, stored procedures, and entity names. It contributes `LinqToSqlDataContext`, `Entity`, `DatabaseTable`, `DatabaseColumn`, and `StoredProcedure` facts with evidence from XML model artifacts.

### 3.2 LINQ to SQL Designer and Usage Extractor

The LINQ to SQL designer and usage extractor analyzes generated designer files and source-code usage. It detects `System.Data.Linq.DataContext`, generated DataContext classes, entity classes, `Table<T>` properties, `GetTable<T>()`, mapping attributes, association attributes, stored procedure methods, `SubmitChanges`, `InsertOnSubmit`, `DeleteOnSubmit`, `Attach`, `ExecuteQuery<T>`, `ExecuteCommand`, and stored procedure wrapper calls.

### 3.3 Entity Framework Extractor

The Entity Framework extractor detects Entity Framework Classic / EF6 and Entity Framework Core contexts, object contexts, DbSet properties, entities, mappings, relationships, migrations, provider configuration, `SaveChanges`, `SaveChangesAsync`, raw SQL APIs, and usage sites. It contributes data-access facts for both legacy and modern EF technologies without treating EF as Archon's persistence implementation.

### 3.4 ADO.NET and Raw SQL Extractor

The ADO.NET and raw SQL extractor detects `SqlConnection`, `SqlCommand`, readers, adapters, datasets, generic provider abstractions, OleDb, Odbc, SQL command text, stored procedure calls, dynamic SQL indicators, read/write hints, and affected tables where deterministically detectable. It uses conservative confidence and explicit unknowns for computed SQL.

### 3.5 Typed DataSet Extractor

The typed DataSet extractor detects `.xsd` files, typed DataSet classes, DataTables, TableAdapters, queries, stored procedures, generated code, and usage sites. It contributes table, query, stored-procedure, and usage facts while preserving model-artifact and source-code evidence.

### 3.6 Data Access Evidence and Graph Integration

WP009 uses Roslyn semantic outputs, repository file artifacts, project/package facts, configuration facts, dependency-injection facts, and runtime facts from earlier work packages. It must emit facts through the established extraction snapshot contract. The Neo4j persistence adapter remains the only persistence path, and extractors must not invent facts that cannot be tied to evidence or represented as explicit unknowns.

## 4. Functional Requirements

### 4.1 Extraction Pipeline Participation

| ID | Requirement |
| --- | --- |
| FR-001 | WP009 shall register data-access extractors with the existing extraction orchestration path. |
| FR-002 | WP009 extractors shall run only as part of an API-triggered extraction using a repository root directory and explicit solution path list. |
| FR-003 | WP009 extractors shall consume repository, solution, project, package, semantic symbol, configuration, dependency-injection, runtime, and file artifact context produced by earlier extraction stages. |
| FR-004 | WP009 extractors shall contribute nodes, relationships, evidence, metadata, warnings, and errors to the shared snapshot accumulator. |
| FR-005 | WP009 extractors shall not persist directly to Neo4j, write sidecar extraction files, introduce an alternate storage model, or connect to live databases. |
| FR-006 | WP009 output shall be snapshot-scoped and compatible with deterministic stable keys and fingerprints established by prior work packages. |

### 4.2 LINQ to SQL DBML Model Parsing

| ID | Requirement |
| --- | --- |
| FR-007 | The extractor shall detect `.dbml` files in analyzed target repositories. |
| FR-008 | The extractor shall parse `.dbml` files without executing generated code or connecting to referenced databases. |
| FR-009 | The extractor shall extract LINQ to SQL DataContext names from `.dbml` files. |
| FR-010 | The extractor shall extract database names where they are present in `.dbml` metadata. |
| FR-011 | The extractor shall extract connection information identifiers and configuration-key references where present, without persisting secret-like connection-string values. |
| FR-012 | The extractor shall extract table names from `.dbml` files. |
| FR-013 | The extractor shall extract column names and column metadata from `.dbml` files where available. |
| FR-014 | The extractor shall extract entity names and map them to database tables where deterministic model metadata exists. |
| FR-015 | The extractor shall extract associations and relationship metadata between LINQ to SQL entities where present. |
| FR-016 | The extractor shall extract database functions and stored procedures declared in `.dbml` files. |
| FR-017 | The extractor shall represent malformed, partial, or unsupported `.dbml` content with warnings and explicit unknowns where facts are partially available. |

### 4.3 LINQ to SQL Designer Extraction

| ID | Requirement |
| --- | --- |
| FR-018 | The extractor shall detect generated LINQ to SQL designer files, including `*.designer.cs` and equivalent generated code artifacts where present. |
| FR-019 | The extractor shall extract generated DataContext classes that derive from or behave as `System.Data.Linq.DataContext`. |
| FR-020 | The extractor shall extract generated entity classes. |
| FR-021 | The extractor shall extract `Table<T>` properties. |
| FR-022 | The extractor shall extract table mappings from `[Table]` attributes or generated metadata. |
| FR-023 | The extractor shall extract column mappings from `[Column]` attributes or generated metadata. |
| FR-024 | The extractor shall extract associations from `[Association]` attributes or generated metadata. |
| FR-025 | The extractor shall extract stored procedure methods from `[Function]` attributes and related generated method patterns. |
| FR-026 | The extractor shall extract stored procedure parameters from `[Parameter]` attributes and generated method signatures where available. |
| FR-027 | The extractor shall correlate designer facts with `.dbml` facts when deterministic identifiers or file relationships support correlation. |
| FR-028 | The extractor shall deduplicate model and designer facts that describe the same DataContext, entity, table, column, association, function, or stored procedure. |

### 4.4 LINQ to SQL Usage Extraction

| ID | Requirement |
| --- | --- |
| FR-029 | The extractor shall detect direct use of `System.Data.Linq.DataContext`. |
| FR-030 | The extractor shall detect construction of generated DataContext types. |
| FR-031 | The extractor shall detect methods that use LINQ to SQL DataContext instances. |
| FR-032 | The extractor shall detect `Table<T>` queries. |
| FR-033 | The extractor shall detect `GetTable<T>()` usage. |
| FR-034 | The extractor shall detect `SubmitChanges()` calls and classify them as write hints. |
| FR-035 | The extractor shall detect `InsertOnSubmit()` calls and classify them as write hints for the affected entity or table where deterministically known. |
| FR-036 | The extractor shall detect `DeleteOnSubmit()` calls and classify them as write hints for the affected entity or table where deterministically known. |
| FR-037 | The extractor shall detect `Attach()` calls and preserve their usage context. |
| FR-038 | The extractor shall detect `ExecuteQuery<T>()` calls and classify them as raw SQL read hints unless evidence indicates otherwise. |
| FR-039 | The extractor shall detect `ExecuteCommand()` calls and classify them as raw SQL execution with write or unknown read/write impact depending on SQL evidence. |
| FR-040 | The extractor shall detect generated stored procedure wrapper calls. |
| FR-041 | The extractor shall link LINQ to SQL usage sites to projects, methods, DataContexts, entities, tables, stored procedures, and raw SQL facts where evidence supports the link. |
| FR-042 | The extractor shall represent unresolved DataContext targets, computed SQL, or unresolved table targets as explicit unknowns rather than inventing names. |

### 4.5 Entity Framework Classic / EF6 Extraction

| ID | Requirement |
| --- | --- |
| FR-043 | The extractor shall detect Entity Framework Classic / EF6 usage from package references, namespaces, base types, source artifacts, and configuration where evidence exists. |
| FR-044 | The extractor shall detect EF6 `DbContext` types. |
| FR-045 | The extractor shall detect EF `ObjectContext` types. |
| FR-046 | The extractor shall detect `DbSet<T>` properties and their entity types. |
| FR-047 | The extractor shall detect entity classes used by EF6 contexts. |
| FR-048 | The extractor shall detect mapping attributes where present. |
| FR-049 | The extractor shall detect `OnModelCreating` mappings where deterministic extraction is possible. |
| FR-050 | The extractor shall detect `EntityTypeConfiguration` classes and relationships to entity types where evidence exists. |
| FR-051 | The extractor shall detect EF6 migrations and migration operations where source artifacts are present. |
| FR-052 | The extractor shall detect provider configuration and connection configuration keys where available. |
| FR-053 | The extractor shall detect `SaveChanges()` and `SaveChangesAsync()` usage and classify them as write hints. |
| FR-054 | The extractor shall detect EF6 raw SQL APIs and usage sites where present. |
| FR-055 | The extractor shall capture projects and methods using EF6 contexts. |
| FR-056 | The extractor shall represent unresolved table names, conventions, dynamic model configuration, and provider details as explicit unknowns where deterministic extraction is not possible. |

### 4.6 Entity Framework Core Extraction

| ID | Requirement |
| --- | --- |
| FR-057 | The extractor shall detect Entity Framework Core usage from package references, namespaces, base types, source artifacts, and configuration where evidence exists. |
| FR-058 | The extractor shall detect EF Core `DbContext` types. |
| FR-059 | The extractor shall detect `DbSet<T>` properties and their entity types. |
| FR-060 | The extractor shall detect EF Core entity classes. |
| FR-061 | The extractor shall detect mapping attributes where present. |
| FR-062 | The extractor shall detect Fluent API mapping in `OnModelCreating` where deterministic extraction is possible. |
| FR-063 | The extractor shall detect relationships between EF Core entities where source evidence supports them. |
| FR-064 | The extractor shall detect EF Core migrations and migration operations where source artifacts are present. |
| FR-065 | The extractor shall detect provider configuration calls including `UseSqlServer`, `UseSqlite`, `UseNpgsql`, and equivalent provider setup calls where symbol or syntax evidence supports detection. |
| FR-066 | The extractor shall detect provider configuration keys and connection-string key references where available. |
| FR-067 | The extractor shall detect `SaveChanges()` and `SaveChangesAsync()` usage and classify them as write hints. |
| FR-068 | The extractor shall detect raw SQL APIs including `FromSql`, `FromSqlRaw`, `FromSqlInterpolated`, `ExecuteSql`, and common EF Core SQL execution variants where present. |
| FR-069 | The extractor shall capture projects and methods using EF Core contexts. |
| FR-070 | The extractor shall represent unresolved table names, conventions, shadow properties, dynamic model configuration, and provider details as explicit unknowns where deterministic extraction is not possible. |

### 4.7 ADO.NET Connection and Command Extraction

| ID | Requirement |
| --- | --- |
| FR-071 | The extractor shall detect `SqlConnection` usage. |
| FR-072 | The extractor shall detect `SqlCommand` usage. |
| FR-073 | The extractor shall detect `SqlDataReader` usage. |
| FR-074 | The extractor shall detect `SqlDataAdapter` usage. |
| FR-075 | The extractor shall detect `DataSet` usage. |
| FR-076 | The extractor shall detect `DataTable` usage. |
| FR-077 | The extractor shall detect `DbConnection` and `DbCommand` usage. |
| FR-078 | The extractor shall detect `OleDbConnection` and related OleDb command usage. |
| FR-079 | The extractor shall detect `OdbcConnection` and related Odbc command usage. |
| FR-080 | The extractor shall detect `ExecuteReader()` and classify it as a read hint. |
| FR-081 | The extractor shall detect `ExecuteNonQuery()` and classify it as a write or unknown read/write hint depending on SQL evidence. |
| FR-082 | The extractor shall detect `ExecuteScalar()` and classify it as a read or unknown read/write hint depending on SQL evidence. |
| FR-083 | The extractor shall capture connection string key references and provider hints where available without persisting secret-like values. |

### 4.8 Raw SQL and Stored Procedure Extraction

| ID | Requirement |
| --- | --- |
| FR-084 | The extractor shall capture SQL command text when it is statically available and safe to persist after redaction. |
| FR-085 | The extractor shall identify stored procedure command types where `CommandType.StoredProcedure` or equivalent evidence exists. |
| FR-086 | The extractor shall extract stored procedure names where they are statically available. |
| FR-087 | The extractor shall identify raw SQL execution where command text or API evidence indicates direct SQL execution. |
| FR-088 | The extractor shall derive read hints from SQL statements such as `SELECT` where deterministic text evidence exists. |
| FR-089 | The extractor shall derive write hints from SQL statements such as `INSERT`, `UPDATE`, `DELETE`, `MERGE`, DDL, or executable stored-procedure patterns where deterministic text evidence exists. |
| FR-090 | The extractor shall identify affected tables where statically available SQL text permits conservative detection. |
| FR-091 | The extractor shall identify dynamic SQL indicators including concatenated SQL, interpolated SQL, formatted SQL, computed command text, and SQL built across multiple statements. |
| FR-092 | The extractor shall preserve partial SQL evidence and unknown reasons for dynamic SQL rather than attempting unsafe or speculative parsing. |
| FR-093 | The extractor shall redact secret-like values from SQL command evidence, connection-string fragments, warnings, errors, metadata, and logs. |

### 4.9 Typed DataSet Extraction

| ID | Requirement |
| --- | --- |
| FR-094 | The extractor shall detect `.xsd` files that define typed DataSets. |
| FR-095 | The extractor shall extract typed DataSet names from `.xsd` files where available. |
| FR-096 | The extractor shall extract DataTable definitions from typed DataSet artifacts. |
| FR-097 | The extractor shall extract TableAdapter definitions from typed DataSet artifacts. |
| FR-098 | The extractor shall extract query definitions from typed DataSet artifacts where available. |
| FR-099 | The extractor shall extract stored procedure references from typed DataSet artifacts where available. |
| FR-100 | The extractor shall detect generated typed DataSet classes where source artifacts are present. |
| FR-101 | The extractor shall detect usage sites for typed DataSets, DataTables, TableAdapters, queries, and stored procedure wrappers. |
| FR-102 | The extractor shall correlate `.xsd`, generated source, and usage facts where deterministic identifiers or file relationships support correlation. |

### 4.10 Graph Nodes and Relationships

| ID | Requirement |
| --- | --- |
| FR-103 | The extractor shall emit `DbContext` nodes through the snapshot contract for EF6 and EF Core context facts. |
| FR-104 | The extractor shall emit `LinqToSqlDataContext` nodes through the snapshot contract. |
| FR-105 | The extractor shall emit `Entity` nodes through the snapshot contract. |
| FR-106 | The extractor shall emit `DatabaseTable` nodes through the snapshot contract. |
| FR-107 | The extractor shall emit `DatabaseColumn` nodes through the snapshot contract. |
| FR-108 | The extractor shall emit `StoredProcedure` nodes through the snapshot contract. |
| FR-109 | The extractor shall reuse existing `Project`, `Type`, `Method`, `ConfigurationKey`, `FilePath`, and related nodes rather than creating duplicate conceptual nodes. |
| FR-110 | The extractor shall emit `USES_DB_CONTEXT` relationships for EF context usage where evidence exists. |
| FR-111 | The extractor shall emit `USES_LINQ_TO_SQL_CONTEXT` relationships for LINQ to SQL context usage where evidence exists. |
| FR-112 | The extractor shall emit `MAPS_ENTITY` relationships for context-to-entity and model-to-entity facts where evidence exists. |
| FR-113 | The extractor shall emit `MAPS_TABLE` relationships for entity-to-table and model-to-table facts where evidence exists. |
| FR-114 | The extractor shall emit `MAPS_COLUMN` relationships for entity-property-to-column and table-to-column facts where evidence exists. |
| FR-115 | The extractor shall emit `READS_TABLE` relationships for methods, contexts, queries, or commands that read tables where evidence exists. |
| FR-116 | The extractor shall emit `WRITES_TABLE` relationships for methods, contexts, commands, or unit-of-work calls that write tables where evidence exists. |
| FR-117 | The extractor shall emit `CALLS_STORED_PROCEDURE` relationships for methods, contexts, commands, generated wrappers, typed DataSets, or table adapters where evidence exists. |
| FR-118 | The extractor shall emit `EXECUTES_RAW_SQL` relationships for methods, contexts, or commands that execute raw SQL where evidence exists. |
| FR-119 | The extractor shall attach evidence to every non-derived data-access fact. |
| FR-120 | The extractor shall store data-access technology, provider, command, mapping, read/write, dynamic SQL, and unknown metadata in metadata fields where available. |

### 4.11 Confidence, Unknowns, Warnings, and Errors

| ID | Requirement |
| --- | --- |
| FR-121 | The extractor shall assign high confidence to symbol-resolved data-access facts and exact source-artifact matches. |
| FR-122 | The extractor shall assign high confidence to well-formed `.dbml` and typed DataSet `.xsd` facts that are parsed from explicit model metadata. |
| FR-123 | The extractor shall assign medium confidence to strongly supported syntax or file-pattern detections that are not fully symbol-resolved. |
| FR-124 | The extractor shall assign low confidence to heuristic raw SQL table detection, convention-based EF mapping, naming-based stored procedure detection, and dynamic SQL classification. |
| FR-125 | The extractor shall represent unresolved context targets, unresolved entities, unresolved table names, unresolved column names, unresolved stored procedure names, dynamic SQL, convention-only EF mappings, and unsupported provider details as explicit unknowns with unknown reason. |
| FR-126 | The extractor shall produce warnings for unreadable model artifacts, malformed `.dbml` files, malformed `.xsd` files, unsupported data-access frameworks, unresolvable generated-code relationships, unsupported SQL parsing cases, and partial compilation failures that affect data-access extraction. |
| FR-127 | The extractor shall produce extraction errors only for failures that prevent the data-access slice from completing for a project or solution. |
| FR-128 | The extractor shall not silently omit partially detectable data-access facts when explicit unknown representation is possible. |

## 5. Non-Functional Requirements

### 5.1 Determinism and Evidence

| ID | Requirement |
| --- | --- |
| NFR-001 | Given the same repository content, solution paths, extraction settings, and dependency versions, WP009 shall produce deterministic data-access facts. |
| NFR-002 | Stable keys and fingerprints for WP009 facts shall not depend on database IDs, absolute developer machine paths, enumeration order, generated temporary paths, or live database metadata. |
| NFR-003 | Every persisted data-access architectural statement shall have evidence unless it is purely derived from persisted facts. |
| NFR-004 | Evidence shall preserve enough context for later API and MCP consumers to explain the fact without re-reading source files. |

### 5.2 Security and Safe Analysis

| ID | Requirement |
| --- | --- |
| NFR-005 | The extractor shall not execute analyzed repository code, instantiate target DbContexts, run migrations, open database connections, execute SQL, inspect live database schemas, call stored procedures, or call external services. |
| NFR-006 | Secret-like data-access values shall not be stored in metadata, evidence snippets, warnings, errors, logs, API-ready responses, or generated outputs. |
| NFR-007 | The extractor shall preserve configuration key names and source locations while redacting values that look like passwords, connection-string secrets, tokens, API keys, certificates, private keys, or credentials. |
| NFR-008 | Data-access extraction shall be static and deterministic, based on source files, project artifacts, configuration artifacts, model artifacts, generated code, and Roslyn semantic information. |
| NFR-009 | SQL evidence shall be treated as source text and shall not be normalized in a way that changes user-authored identifiers or obscures evidence provenance. |

### 5.3 Performance and Scalability

| ID | Requirement |
| --- | --- |
| NFR-010 | The extractor shall avoid repeated semantic analysis of the same syntax tree or symbol where prior Roslyn context is available. |
| NFR-011 | The extractor shall use cancellation tokens from the extraction orchestration path. |
| NFR-012 | The extractor shall avoid unbounded recursion when following DataContext wrappers, repository methods, EF model builder extension methods, SQL builder helper methods, or generated-code relationships. |
| NFR-013 | The extractor shall define and test safeguards for large `.dbml` files, large typed DataSet `.xsd` files, large generated designer files, large migration histories, and source files with many SQL commands. |
| NFR-014 | The extractor shall avoid holding full secret-bearing configuration documents or large SQL texts in long-lived memory beyond the extraction scope. |

### 5.4 Maintainability

| ID | Requirement |
| --- | --- |
| NFR-015 | C# code shall use block-scoped namespaces. |
| NFR-016 | C# code shall use Allman braces. |
| NFR-017 | C# files shall contain one public type per file. |
| NFR-018 | Private fields shall use underscore-prefixed naming. |
| NFR-019 | Executable entry points shall avoid top-level statements. |
| NFR-020 | `.csproj` files shall keep `PackageReference` entries in `ItemGroup` blocks that contain only package references. |
| NFR-021 | Internal and non-public types introduced for WP009 shall be treated as requiring the same developer-level documentation standard as public types when documentation is necessary to understand architecture or behavior. |

### 5.5 Testability

| ID | Requirement |
| --- | --- |
| NFR-022 | Data-access extraction logic shall be testable without starting the Aspire AppHost. |
| NFR-023 | Data-access extraction logic shall be testable using in-memory or fixture-based source repositories. |
| NFR-024 | Data-access classification, confidence assignment, stable-key behavior, evidence generation, redaction, deduplication, and unknown handling shall be directly testable. |
| NFR-025 | Tests shall not require external service credentials, running web servers, queue brokers, live databases, database containers, migration execution, or stored procedure execution. |

## 6. Technical Requirements

### 6.1 Target Runtime and Project Placement

WP009 implementation shall use the repository-approved .NET target and the project layout created by WP001. The expected primary production projects are:

| Project | Responsibility |
| --- | --- |
| `Archon.Extractors.DataAccess` | LINQ to SQL, EF6, EF Core, ADO.NET, raw SQL, stored procedure, typed DataSet, data-access graph fact, confidence, and unknown extraction behavior. |
| `Archon.Extractors.Projects` | Project metadata and package/framework context consumed by data-access extractors. |
| `Archon.Extractors.Configuration` | Connection-string, provider, and configuration-key context consumed by data-access extractors. |
| `Archon.Extractors.DependencyInjection` | DbContext registration and service-registration context consumed by WP009 where available. |
| `Archon.Roslyn` and language-specific Roslyn projects | Shared semantic context, symbol resolution, invocation analysis, attribute analysis, XML documentation or generated-code support where applicable, and evidence projection support. |
| `Archon.Application` | Shared extraction contracts, snapshot accumulation contracts, graph fact contracts, and orchestration interfaces. |
| `Archon.Api.Extraction` | Coordination of extractor execution through the established API-triggered extraction path. |

Expected corresponding test projects are:

| Test Project | Responsibility |
| --- | --- |
| `Archon.Extractors.DataAccess.Tests` | LINQ to SQL, EF6, EF Core, ADO.NET, raw SQL, stored procedure, typed DataSet, evidence, confidence, redaction, unknown, and deduplication behavior. |
| `Archon.Extractors.Projects.Tests` | Any project metadata or package detection behavior introduced specifically to support WP009. |
| `Archon.Extractors.Configuration.Tests` | Connection-string key, provider, and data-access configuration correlation behavior introduced or adjusted for WP009. |
| `Archon.Extractors.DependencyInjection.Tests` | DbContext registration correlation and DI/data-access integration behavior introduced or adjusted for WP009. |
| `Archon.Api.Extraction.Tests` | Pipeline participation, orchestration integration, warning/error propagation, and snapshot accumulation behavior. |
| `Archon.Roslyn.Tests`, `Archon.Roslyn.CSharp.Tests`, `Archon.Roslyn.VisualBasic.Tests` | Any shared semantic helper behavior introduced specifically to support WP009. |

### 6.2 Dependency Direction

WP009 must preserve Onion Architecture dependency direction:

- Domain must not depend on application, extractors, infrastructure, or hosts.
- Application may define contracts and ports but must not depend on infrastructure or hosts.
- Extractors may depend on application and Roslyn abstractions according to existing solution direction.
- API extraction coordination may depend on extractor contracts but must not absorb extractor implementation details that belong in extractor projects.
- Infrastructure and hosts must not become a dumping ground for data-access extraction logic.

### 6.3 Data-Access Artifact Analysis

The implementation shall analyze data-access artifacts as source data. It shall not execute migrations, generate database schemas, connect to target databases, instantiate DbContexts, or run target application data-access code.

Data-access artifact analysis shall preserve:

- Repository-relative source or artifact file path.
- Data-access artifact kind.
- Context, entity, table, column, procedure, command, adapter, or query identifier where available.
- Project and target framework context.
- Source line span or XML artifact location where available.
- Confidence and detection mode.
- Unknown reason where a data-access fact is partial.

### 6.4 Model and Mapping Analysis

Model and mapping analysis shall use explicit model artifacts and Roslyn semantic information where available. It shall recognize contexts, entities, tables, columns, associations, and mappings by symbol identity when possible and by syntax or artifact fallback only where symbol identity is not available. Fallback detections must carry lower confidence and explicit metadata identifying the detection mode.

Model and mapping analysis shall preserve:

- Data-access technology.
- Context type or model name.
- Entity type.
- Database table name.
- Database column name.
- Stored procedure name.
- Association or relationship metadata.
- Provider hint.
- Configuration key reference.
- Dynamic or convention-based unknowns where exact values cannot be determined.

### 6.5 SQL and Command Analysis

SQL and command analysis shall use conservative static analysis. The implementation shall not attempt to fully interpret arbitrary SQL or evaluate runtime string construction. SQL parsing, when implemented, must be best-effort and evidence-preserving.

SQL and command analysis shall preserve:

- Command API or framework API.
- Command type where available.
- Static command text where safe after redaction.
- Stored procedure name where deterministic.
- Read/write hint.
- Affected table hint where deterministic.
- Dynamic SQL indicator.
- Configuration key reference.
- Confidence and unknown reason metadata.

### 6.6 Documentation Pass

WP009 shall include a documentation pass covering:

- Supported LINQ to SQL `.dbml` extraction patterns.
- Supported LINQ to SQL generated designer extraction patterns.
- Supported LINQ to SQL usage extraction patterns.
- Supported EF6 extraction patterns.
- Supported EF Core extraction patterns.
- Supported ADO.NET extraction patterns.
- Supported raw SQL and stored procedure extraction patterns.
- Supported typed DataSet extraction patterns.
- Confidence and unknown-state behavior.
- Secret redaction behavior for data-access configuration and SQL evidence.
- Testing and fixture guidance for data-access extraction.
- Limitations and known unsupported patterns, expressed as current implementation constraints rather than deferred mandatory requirements.

Internal and non-public implementation types introduced for WP009 shall be treated as requiring the same developer-level documentation standard as public types when documentation is necessary to understand the architecture or behavior.

## 7. Exclusions

WP009 shall not implement:

- Archon Discovery UI host, pages, components, assets, database explorer, schema viewer, stored-procedure viewer, data-access dashboard, graph view, or tests for UI behavior.
- API query endpoints for browsing data-access facts; those belong to the query API work package.
- MCP tools, MCP resources, MCP prompts, or Copilot workflows.
- Rule catalog evaluation, hotlist generation, finding suppression, or rule management.
- Broad external integration extraction beyond data-access provider and connection configuration hints explicitly required by WP009.
- Runtime endpoint, worker, queue consumer, or message handler extraction; those belong to WP008 except where already produced facts are consumed for correlation.
- .NET UI technology extraction.
- Markdown export.
- Snapshot diff.
- Direct Neo4j writes from extractor projects.
- Live database inspection, database reverse engineering, migration execution, stored procedure execution, query execution, or validation against a running database.
- Automatic remediation, code rewriting, ORM migration, database schema migration, or generated replacement data-access code.

## 8. Data and Integration Requirements

### 8.1 Required Graph Facts

WP009 shall contribute graph facts that fit the existing Archon graph model:

| Fact Type | Required Treatment |
| --- | --- |
| LINQ to SQL DataContext | Represent as `LinqToSqlDataContext` nodes with name, type, model file, generated file, project, metadata, evidence, confidence, and unknowns where applicable. |
| EF DbContext | Represent as `DbContext` nodes with framework, provider, type, project, metadata, evidence, confidence, and unknowns where applicable. |
| Entity | Represent as `Entity` nodes with type, context/model ownership, mapped table, metadata, evidence, confidence, and unknowns where applicable. |
| Database table | Represent as `DatabaseTable` nodes with database/schema/table identifiers where available, model source, metadata, evidence, confidence, and unknowns where applicable. |
| Database column | Represent as `DatabaseColumn` nodes with table, column, property mapping, metadata, evidence, confidence, and unknowns where applicable. |
| Stored procedure | Represent as `StoredProcedure` nodes with name, source technology, wrapper method, parameters where available, metadata, evidence, confidence, and unknowns where applicable. |
| Raw SQL execution | Represent through `EXECUTES_RAW_SQL` relationships and related metadata attached to methods, contexts, commands, or data-access usage facts. |
| Read/write relationships | Represent reads and writes through `READS_TABLE` and `WRITES_TABLE` relationships where evidence supports read/write classification. |
| Evidence | Link file, symbol, call site, XML element, configuration key, line span, snippet hash, snippet preview, confidence, and redaction metadata. |
| Unknown | Represent unresolved contexts, table names, column names, stored procedure names, providers, dynamic SQL, and convention-only mappings with explicit unknown reason. |

### 8.2 Metadata Requirements

WP009 metadata shall support later API and MCP consumption. Metadata shall include, where available:

- Data-access technology.
- Framework or provider.
- Project key.
- Target framework.
- Context type.
- Model file path.
- Generated file path.
- Entity type.
- Table name.
- Schema name.
- Database name.
- Column name.
- Property name.
- Association or relationship kind.
- Stored procedure name.
- Stored procedure parameter names.
- Command type.
- Command API.
- SQL text hash.
- Redacted SQL preview.
- Read/write hint.
- Dynamic SQL indicator.
- Migration name.
- Provider configuration call.
- Connection-string key.
- Configuration key reference.
- Detection mode.
- Confidence reason.
- Unknown reason.

### 8.3 Evidence Requirements

Evidence shall include enough information for later API and MCP consumers to show why the fact exists:

- Repository-relative file path.
- Line and column span where available.
- XML artifact path or element location where available.
- Symbol name where available.
- Containing symbol where available.
- Data-access artifact type.
- Context, entity, table, column, stored procedure, command, adapter, or query identifier where relevant.
- Configuration key path where relevant.
- Snippet hash.
- Snippet preview with secrets redacted.
- Detection mode.
- Confidence.

### 8.4 Integration with Earlier Work Packages

WP009 shall integrate with earlier outputs as follows:

- Use project and package facts from WP005 to identify candidate data-access technologies and target frameworks.
- Use semantic symbol facts from WP006 to identify types, methods, attributes, invocations, inheritance, generics, and interface/base-class relationships.
- Use configuration facts from WP007 to resolve connection-string keys, provider settings, and data-access configuration references where available.
- Use dependency-injection facts from WP007 to correlate registered DbContexts and data-access service abstractions where available.
- Use runtime facts from WP008 to correlate endpoints, workers, handlers, and entry points with downstream data-access usage where method-level call or dependency evidence exists.
- Reuse existing nodes and relationships when earlier work packages already emitted equivalent facts.

### 8.5 Integration with Later Work Packages

WP009 output shall be shaped so later work packages can:

- Query data-access facts by project, method, context, entity, table, column, stored procedure, provider, technology, and snapshot.
- Query which projects and methods read from or write to specific database tables.
- Query which projects and methods call specific stored procedures.
- Explain change impact from tables, stored procedures, contexts, entities, raw SQL, configuration keys, endpoints, workers, and service dependencies.
- Feed rule evaluation and hotlist findings for LINQ to SQL, typed DataSets, ADO.NET, raw SQL, stored procedure-heavy access, provider risk, and migration constraints.
- Expose evidence-backed data-access facts through MCP tools and resources.
- Include data-access maps in generated markdown.

## 9. Test Requirements

### 9.1 Required Test Coverage

| Test Area | Required Verification |
| --- | --- |
| Pipeline integration | Data-access extractors run through the existing extraction orchestration path and emit snapshot facts. |
| DBML parsing | `.dbml` DataContext, database, connection, table, column, association, function, stored procedure, and entity facts are detected. |
| Malformed DBML | Malformed or partial `.dbml` artifacts produce warnings and explicit unknowns where partial facts are available. |
| LINQ to SQL designer extraction | Generated DataContext classes, entity classes, `Table<T>` properties, mappings, associations, stored procedure methods, and parameters are detected. |
| LINQ to SQL usage | DataContext construction, table queries, `GetTable<T>()`, `SubmitChanges`, `InsertOnSubmit`, `DeleteOnSubmit`, `Attach`, `ExecuteQuery`, `ExecuteCommand`, and stored procedure wrapper calls are detected. |
| EF6 extraction | EF6 contexts, ObjectContexts, DbSets, entities, mappings, migrations, provider configuration, raw SQL APIs, `SaveChanges`, and usage sites are detected. |
| EF Core extraction | EF Core contexts, DbSets, entities, Fluent API mappings, relationships, migrations, provider configuration, raw SQL APIs, `SaveChanges`, and usage sites are detected. |
| ADO.NET command analysis | Connections, commands, readers, adapters, datasets, provider abstractions, OleDb, Odbc, execution methods, connection key references, and command types are detected. |
| Raw SQL analysis | Static SQL text, stored procedure calls, read/write hints, affected table hints, SQL previews, SQL hashes, and dynamic SQL indicators are handled. |
| Typed DataSets | `.xsd` files, typed DataSet classes, DataTables, TableAdapters, queries, stored procedures, generated source, and usage sites are detected. |
| Graph facts | `DbContext`, `LinqToSqlDataContext`, `Entity`, `DatabaseTable`, `DatabaseColumn`, `StoredProcedure`, `USES_DB_CONTEXT`, `USES_LINQ_TO_SQL_CONTEXT`, `MAPS_ENTITY`, `MAPS_TABLE`, `MAPS_COLUMN`, `READS_TABLE`, `WRITES_TABLE`, `CALLS_STORED_PROCEDURE`, and `EXECUTES_RAW_SQL` facts are emitted as applicable. |
| Evidence | Every non-derived fact has source evidence with file path, artifact location or line span where available, snippet hash, and redacted preview. |
| Confidence | High, medium, and low confidence cases are assigned consistently. |
| Unknowns | Dynamic SQL, unresolved contexts, unresolved tables, unresolved columns, unresolved stored procedures, provider ambiguity, and convention-only mappings produce explicit unknowns. |
| Redaction | Connection-string values, credential-like SQL literals, and secret-like configuration values are not present in metadata, evidence previews, warnings, errors, logs, or test output. |
| Deduplication | Duplicate facts from `.dbml`, generated designer files, source usage, migrations, and multiple detection paths do not create duplicate graph facts. |
| C# support | C# data-access extraction patterns are covered. |
| VB.NET support | VB.NET data-access extraction patterns are covered where Roslyn supports semantic detection. |

### 9.2 Test Fixtures

Tests shall include fixture repositories or in-memory source sets for:

- LINQ to SQL project with a `.dbml` file.
- LINQ to SQL generated designer source with DataContext, entities, mappings, associations, and stored procedure wrappers.
- LINQ to SQL usage with direct DataContext construction, queries, writes, raw SQL, and stored procedure wrapper calls.
- EF6 project with DbContext, ObjectContext where feasible, DbSets, mappings, migrations, provider configuration, raw SQL, and `SaveChanges` usage.
- EF Core project with DbContext, DbSets, Fluent API mappings, migrations, provider configuration, raw SQL, and `SaveChangesAsync` usage.
- ADO.NET source with `SqlConnection`, `SqlCommand`, readers, adapters, datasets, generic provider abstractions, OleDb, and Odbc examples.
- Raw SQL examples covering static `SELECT`, `INSERT`, `UPDATE`, `DELETE`, `MERGE`, stored procedures, unknown read/write command text, concatenated SQL, interpolated SQL, and computed SQL.
- Typed DataSet `.xsd` artifacts and generated source examples.
- Configuration examples containing connection-string keys and secret-like values for redaction verification.
- Mixed C# and VB.NET examples where feasible.
- Duplicate fact examples where `.dbml`, designer, and usage facts describe the same model elements.

### 9.3 Test Constraints

Automated verification must not start the Aspire AppHost as a blocking process. Tests should use extractor-level fixtures, application-layer orchestration seams, and targeted integration tests. For this work package, the full test suite should not be run unless explicitly requested; run targeted WP009 tests and a solution build as final validation.

## 10. Acceptance Criteria

WP009 is accepted when all of the following are true:

1. Data-access extractors are wired into the existing extraction orchestration path.
2. `.dbml` files are parsed and DataContext names, database names, connection information, tables, columns, associations, functions, stored procedures, and entity names are extracted.
3. Generated designer DataContext classes, entity classes, `Table<T>` properties, table mappings, column mappings, associations, and stored procedure methods are extracted.
4. LINQ to SQL usage including DataContext construction, table queries, `SubmitChanges`, `ExecuteQuery`, `ExecuteCommand`, and stored procedure wrapper calls is detected.
5. EF Classic / EF6 contexts, entities, mappings, migrations, relationships, provider configuration, `SaveChanges`, raw SQL APIs, and usage sites are detected.
6. EF Core contexts, entities, mappings, migrations, relationships, provider configuration, `SaveChanges`, raw SQL APIs, and usage sites are detected.
7. ADO.NET connections, commands, readers, adapters, datasets, SQL command text, stored procedure calls, read/write hints, dynamic SQL indicators, and affected tables are detected where deterministically possible.
8. Typed DataSets, `.xsd` files, table adapters, tables, queries, stored procedures, and usage sites are detected.
9. `DbContext`, `LinqToSqlDataContext`, `Entity`, `DatabaseTable`, `DatabaseColumn`, and `StoredProcedure` nodes are emitted through the snapshot contract.
10. `USES_DB_CONTEXT`, `USES_LINQ_TO_SQL_CONTEXT`, `MAPS_ENTITY`, `MAPS_TABLE`, `MAPS_COLUMN`, `READS_TABLE`, `WRITES_TABLE`, `CALLS_STORED_PROCEDURE`, and `EXECUTES_RAW_SQL` relationships are emitted through the snapshot contract where applicable.
11. API and MCP consumers will be able to identify projects, methods, entities, tables, and stored procedures involved in data access from the persisted graph facts.
12. LINQ to SQL is first-class and fully covered.
13. EF6, EF Core, ADO.NET, raw SQL, and typed DataSets are represented with evidence and confidence.
14. Unknowns and confidence are explicit for unresolved or inferred data-access facts.
15. Secret-like connection-string values, SQL literals, and configuration values are redacted before being stored or exposed in evidence, metadata, warnings, errors, or logs.
16. Tests cover DBML parsing, designer extraction, LINQ to SQL usage, EF usage, ADO.NET command analysis, dynamic SQL unknowns, read/write hints, and stored procedure mapping.
17. Documentation is updated for supported data-access extraction behavior and validation.
18. No Archon Discovery UI implementation is introduced.
19. The solution builds successfully.
20. Targeted WP009 tests pass.

## 11. Risks and Decisions

### 11.1 Risks

| Risk | Impact | Mitigation |
| --- | --- | --- |
| LINQ to SQL `.dbml` and generated designer files may disagree. | Duplicate or conflicting model facts could reduce graph trust. | Correlate by stable identifiers, preserve both evidence sources, deduplicate exact matches, and warn on conflicts. |
| EF conventions can map entities to tables without explicit source names. | Table names may be incomplete or misleading if inferred too aggressively. | Use explicit mappings where available and represent convention-only names with confidence metadata or unknowns. |
| EF model configuration can be split across extension methods and configuration classes. | Relationships and table mappings may be missed. | Follow source wrapper methods conservatively with recursion safeguards and emit warnings for unresolvable model-building paths. |
| Raw SQL may be dynamically constructed. | Read/write hints and affected tables may be incomplete. | Preserve dynamic SQL indicators, partial evidence, lower confidence, and explicit unknowns rather than speculative parsing. |
| Stored procedure names may be configuration-driven or computed. | Procedure-call facts may be partial. | Link to configuration keys where available and use unknown procedure names with evidence when unresolved. |
| SQL and connection metadata can contain secrets. | Persisted evidence could expose credentials. | Reuse or enforce redaction before evidence, metadata, warning, error, or log emission. |
| Typed DataSet generated code can be large and noisy. | Extraction could be slow or produce duplicate facts. | Prefer `.xsd` model artifacts when available, correlate generated code with model artifacts, and deduplicate through stable keys. |
| Multiple extraction slices may emit overlapping data-access dependency facts. | Duplicate graph relationships could reduce query quality. | Deduplicate by stable key and fingerprint through the snapshot accumulator. |
| VB.NET support may differ from C# for legacy data-access idioms. | Mixed-language estates may have uneven coverage. | Use Roslyn Visual Basic semantic support where available and document/test supported parity. |

### 11.2 Decisions

| Decision | Rationale |
| --- | --- |
| Use a single WP009 specification document. | User requested a single markdown document spec for WP009. |
| Create the documentation under `docs/009-Data-Access-Extraction/`. | This is the next incremental documentation work-package folder after WP008. |
| Do not create separate overview and component spec documents. | The user explicitly requested a single markdown document, overriding the multi-document collaboration pattern for this output. |
| Treat data-access extraction as extraction slices, not persistence services. | The work-package sequence requires extractors to contribute through the snapshot contract and keep Neo4j as the system of record. |
| Keep broad external integration extraction out of WP009 except provider and configuration facts. | Full external integration extraction belongs to WP010, while WP009 only needs data-access provider, connection, and database-related facts. |
| Preserve explicit unknowns rather than suppressing partial data-access facts. | The source brief requires unknowns to be represented instead of omitted or invented. |
| Use deterministic data-access stable-key inputs. | WP009 stable keys shall be deterministic and shall not depend on database IDs, absolute developer machine paths, enumeration order, generated temporary paths, or live database metadata. `DbContext` keys shall use project key plus fully qualified context type name. `LinqToSqlDataContext` keys shall use project key plus DataContext type name or normalized `.dbml` model identity. `Entity` keys shall use project key plus fully qualified entity type name or model entity name. `DatabaseTable` keys shall use normalized database/schema/table identity where available plus owning model/context key. `DatabaseColumn` keys shall use table key plus normalized column name. `StoredProcedure` keys shall use normalized database/schema/procedure identity where available plus owning model/context key. Raw SQL usage keys shall use method key plus normalized call-site location and SQL text hash when available. |
| Use ownership and dependency direction for data-access relationships. | `Project`, `Type`, or `Method` nodes shall point to data-access contexts through `USES_DB_CONTEXT` or `USES_LINQ_TO_SQL_CONTEXT`. Contexts shall point to entities through `MAPS_ENTITY`. Entities shall point to tables through `MAPS_TABLE`. Entities or tables shall point to columns through `MAPS_COLUMN` according to the established graph model. Methods or contexts shall point to tables through `READS_TABLE` and `WRITES_TABLE`. Methods, contexts, generated wrappers, or adapters shall point to stored procedures through `CALLS_STORED_PROCEDURE`. Methods or contexts shall point to raw SQL facts through `EXECUTES_RAW_SQL`. |
| Use lower camel case data-access metadata field names. | Data-access metadata fields shall use stable API-friendly lower camel case names, including `dataAccessTechnology`, `framework`, `provider`, `targetFramework`, `contextType`, `modelFilePath`, `generatedFilePath`, `entityType`, `tableName`, `schemaName`, `databaseName`, `columnName`, `propertyName`, `relationshipKind`, `storedProcedureName`, `commandType`, `commandApi`, `sqlTextHash`, `sqlPreview`, `readWriteHint`, `isDynamicSql`, `migrationName`, `providerConfigurationCall`, `connectionStringKey`, `configurationKey`, `detectionMode`, `confidenceReason`, and `unknownReason`. |
| Represent data-access subtypes as metadata, not new graph node kinds. | WP009 shall keep the core graph node kinds aligned with WP002 and Appendix E: `DbContext`, `LinqToSqlDataContext`, `Entity`, `DatabaseTable`, `DatabaseColumn`, and `StoredProcedure`, plus existing `Project`, `Type`, and `Method`. Finer distinctions shall be represented as metadata, such as `dataAccessTechnology` values `LinqToSql`, `EntityFramework6`, `EntityFrameworkCore`, `AdoNet`, `TypedDataSet`, and `RawSql`; `provider` values `SqlServer`, `Sqlite`, `PostgreSql`, `OleDb`, `Odbc`, and `Unknown`; and `readWriteHint` values `Read`, `Write`, `ReadWrite`, and `Unknown`. |

## 12. Manual Verification Requirements

The implementation documentation for WP009 shall instruct a developer to verify the work package by:

1. Restoring and building the solution.
2. Running targeted tests for LINQ to SQL `.dbml` parsing.
3. Running targeted tests for LINQ to SQL generated designer and usage extraction.
4. Running targeted tests for EF6 extraction.
5. Running targeted tests for EF Core extraction.
6. Running targeted tests for ADO.NET command analysis.
7. Running targeted tests for raw SQL, stored procedure, dynamic SQL, read/write hint, and affected table analysis.
8. Running targeted tests for typed DataSet extraction.
9. Running targeted extraction integration tests through the API extraction module seam without launching the blocking Aspire AppHost process.
10. Inspecting representative snapshot output to confirm `DbContext`, `LinqToSqlDataContext`, `Entity`, `DatabaseTable`, `DatabaseColumn`, `StoredProcedure`, `USES_DB_CONTEXT`, `USES_LINQ_TO_SQL_CONTEXT`, `MAPS_ENTITY`, `MAPS_TABLE`, `MAPS_COLUMN`, `READS_TABLE`, `WRITES_TABLE`, `CALLS_STORED_PROCEDURE`, and `EXECUTES_RAW_SQL` facts are emitted where applicable.
11. Confirming evidence includes redacted snippets and source locations.
12. Confirming secret-like data-access values are not present in test output, logs, warnings, errors, metadata, or evidence previews.
13. Confirming no Archon Discovery UI resource, page, component, or front-end asset was created.

Automated validation instructions shall explicitly state not to run the AppHost as a blocking process during agent-driven verification.

## 13. Traceability Matrix

| Source Requirement | Specification Coverage |
| --- | --- |
| Parse `.dbml` files and extract DataContext names, database names, connection information, tables, columns, associations, functions, stored procedures, and entity names | Sections 4.2, 8, 9, 10 |
| Extract generated designer DataContext classes, entity classes, `Table<T>` properties, table mappings, column mappings, associations, and stored procedure methods | Sections 4.3, 8, 9, 10 |
| Detect LINQ to SQL usage including DataContext construction, table queries, `SubmitChanges`, `ExecuteQuery`, `ExecuteCommand`, and stored procedure wrapper calls | Sections 4.4, 8, 9, 10 |
| Detect EF Classic / EF6 contexts, entities, mappings, migrations, relationships, provider configuration, `SaveChanges`, raw SQL APIs, and usage sites | Sections 4.5, 8, 9, 10 |
| Detect EF Core contexts, entities, mappings, migrations, relationships, provider configuration, `SaveChanges`, raw SQL APIs, and usage sites | Sections 4.6, 8, 9, 10 |
| Detect ADO.NET connections, commands, readers, adapters, datasets, SQL command text, stored procedure calls, read/write hints, dynamic SQL indicators, and affected tables where detectable | Sections 4.7, 4.8, 8, 9, 10 |
| Detect typed DataSets, `.xsd` files, table adapters, tables, queries, stored procedures, and usage sites | Sections 4.9, 8, 9, 10 |
| Persist `DbContext`, `LinqToSqlDataContext`, `Entity`, `DatabaseTable`, `DatabaseColumn`, and `StoredProcedure` nodes | Sections 4.10, 8.1, 10 |
| Persist `USES_DB_CONTEXT`, `USES_LINQ_TO_SQL_CONTEXT`, `MAPS_ENTITY`, `MAPS_TABLE`, `MAPS_COLUMN`, `READS_TABLE`, `WRITES_TABLE`, `CALLS_STORED_PROCEDURE`, and `EXECUTES_RAW_SQL` relationships | Sections 4.10, 8.1, 10 |
| API and MCP consumers can identify projects, methods, entities, tables, and stored procedures involved in data access | Sections 8.5, 10 |
| LINQ to SQL is first-class and fully covered | Sections 3.1, 3.2, 4.2 through 4.4, 9, 10 |
| EF6, EF Core, ADO.NET, raw SQL, and typed DataSets are represented with evidence and confidence | Sections 4.5 through 4.11, 5.1, 8.3, 9, 10 |
| Tests cover DBML parsing, designer extraction, LINQ to SQL usage, EF usage, ADO.NET command analysis, dynamic SQL unknowns, read/write hints, and stored procedure mapping | Sections 9, 10 |
| Repository documentation updated | Sections 6.6, 12, 10 |
| No Discovery UI implementation | Sections 1.3, 7, 10, 12 |

## 14. Open Questions

No open questions remain for WP009. Stable-key inputs, graph relationship direction, metadata field names, and data-access subtype representation are recorded as definitive decisions in section 11.2.

## 15. Change Log

| Date | Change |
| --- | --- |
| 2026-05-22 | Created initial single-document WP009 specification from `docs/foundation/work-packages.md` and the Archon source brief. |
