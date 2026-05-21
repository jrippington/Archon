# WP005 Specification - Repository, Solution, Project, and Package Extraction

## Document Control

| Field | Value |
| --- | --- |
| Work Package | WP005 - Repository, Solution, Project, and Package Extraction |
| Output Path | `docs/005-Repository-Solution-Project-and-Package-Extraction/spec-wp005-repository-solution-project-and-package-extraction.md` |
| Source Work Package | `docs/foundation/work-packages.md` WP005 |
| Source Brief | `docs/foundation/archon_full_concept_brief.md` |
| Specification Basis | `spec-template_v1.1.md` requested; template file was not present in the workspace at creation time, so this document follows the repository specification structure and documented work-package rules. |
| Status | Draft |
| Audience | Product owner, architect, implementation team, test engineer |

## 1. Overview

### 1.1 Purpose

This specification defines the requirements for WP005, the Archon work package that implements repository, solution, project, package, project-reference, package-reference, analyzer-reference, target-framework, project-format, and application-type extraction for C# and VB.NET projects.

WP005 is the first real extraction slice built on the API-triggered orchestration path established by WP004. It must convert submitted repositories and solution files into deterministic, evidence-backed architecture graph facts without introducing semantic type, method, runtime, configuration, data-access, integration, markdown, MCP, or Discovery UI behavior that belongs to later work packages.

### 1.2 Background

Archon is a deterministic architecture intelligence platform for modern and legacy .NET estates. The source brief requires Archon to analyze `.sln`, `.csproj`, `.vbproj`, `.props`, `.targets`, `packages.config`, and related project artifacts, then persist architecture-wide repository, solution, project, package, dependency, and evidence facts into Neo4j.

WP005 follows WP004. The API can already accept an extraction request containing a repository root directory and explicit solution paths, validate the request, run a shared extraction pipeline, assemble an `ExtractedArchitectureSnapshot`, and hand that snapshot to Neo4j persistence. WP005 replaces placeholder project metadata behavior with real repository, solution, project, and package extraction while continuing to use the same orchestration, accumulation, stable-key, evidence, and persistence contracts.

### 1.3 High-Level Scope

WP005 covers the project metadata extraction slice:

- Loading submitted solutions through the shared WP004 extraction path.
- Extracting repository and solution graph facts.
- Extracting C# and VB.NET project graph facts.
- Extracting target frameworks, output type, assembly name, root namespace, SDK-style status, old-style project status, nullable setting, implicit usings, analyzer references, project references, package references, and `packages.config` references.
- Classifying application type indicators for supported .NET application categories.
- Persisting `Repository`, `Solution`, `Project`, `Package`, and relevant `FilePath` nodes.
- Persisting `CONTAINS`, `REFERENCES`, and `USES_PACKAGE` relationships.
- Capturing evidence from solution files, project files, package references, `packages.config`, imported project configuration artifacts, and relevant path artifacts.
- Testing multi-solution repositories, mixed C#/VB.NET solutions, project references, package references, old-style projects, SDK-style projects, and evidence spans.
- Updating contributor documentation for project extraction behavior and validation workflows.

## 2. System Context

### 2.1 Product Context

Archon consumers need to understand the structural shape of a .NET repository before deeper semantic analysis can be useful. Repository, solution, project, package, and project-reference facts provide the foundation for later code, runtime, configuration, UI, data-access, integration, rule, metric, query, diff, markdown, and MCP capabilities.

WP005 must model project-level facts in the same architecture-wide graph model used by earlier packages. The implementation must not create a separate project inventory store, separate dependency model, or one-off metadata format that bypasses snapshot, evidence, stable-key, confidence, unknown, or Neo4j persistence requirements.

### 2.2 Source References

WP005 must align with these source materials:

- `docs/foundation/work-packages.md` WP005 objective, required implementation, and completion criteria.
- `docs/foundation/archon_full_concept_brief.md` section 14.1 for extraction pipeline stages.
- `docs/foundation/archon_full_concept_brief.md` section 14.2 for extraction scope covering `.sln`, `.csproj`, `.vbproj`, `.props`, `.targets`, and `packages.config` artifacts.
- `docs/foundation/archon_full_concept_brief.md` section 15 for C# and VB.NET Roslyn language support and mixed-language solution support.
- `docs/foundation/archon_full_concept_brief.md` section 16 for project and package extraction fields and application type indicators.
- `docs/foundation/archon_full_concept_brief.md` section 17.1 for project-level dependency extraction.
- `docs/foundation/archon_full_concept_brief.md` section 35 phase 1 and section 36 Project Extraction epic.
- `docs/foundation/archon_full_concept_brief.md` Appendix E section E.6.1 for repository and solution modeling.
- `docs/foundation/archon_full_concept_brief.md` Appendix E section E.7.2 for project and code slice implementation acceptance criteria.
- `.github/instructions/documentation-pass.instructions.md` for mandatory developer documentation expectations, including internal and other non-public types.

### 2.3 Users and Stakeholders

| Stakeholder | Interest |
| --- | --- |
| Product owner | Confirms that Archon can produce a reliable project and dependency inventory for submitted .NET repositories. |
| Architect | Confirms repository, solution, project, package, and dependency facts are deterministic, evidence-backed, graph-native, and aligned with Onion Architecture boundaries. |
| Developer | Uses extracted project metadata as the foundation for later semantic, runtime, configuration, data-access, and API/MCP features. |
| Test engineer | Verifies extraction correctness across SDK-style projects, old-style projects, mixed-language solutions, multi-solution repositories, project references, package references, and evidence spans. |
| Future API and MCP consumer | Depends on accurate project and package graph facts when querying system composition, dependencies, and modernization risks. |

## 3. Component Summary

### 3.1 Project Extraction Pipeline Stage

The project extraction pipeline stage plugs into the shared WP004 extraction orchestration path. It receives resolved repository and solution inputs, loads submitted solutions, extracts project-level metadata, contributes graph nodes and edges to the shared accumulation model, and records warnings or errors without bypassing the orchestrator.

### 3.2 Solution Loader

The solution loader opens each submitted solution through repository-approved .NET/MSBuild/Roslyn loading infrastructure. It must support C# and VB.NET projects and preserve enough solution-file evidence to identify which projects were declared by each submitted solution.

### 3.3 Project File Analyzer

The project file analyzer reads `.csproj` and `.vbproj` files to extract project identity, language, target frameworks, output type, assembly name, root namespace, SDK-style status, old-style project status, nullable setting, implicit usings, analyzer references, project references, package references, and imported configuration artifacts where available.

### 3.4 Legacy Package Analyzer

The legacy package analyzer reads `packages.config` files associated with old-style projects and contributes package nodes, package-use edges, and evidence records for packages that are not represented through SDK-style `PackageReference` items.

### 3.5 Application Type Classifier

The application type classifier evaluates project metadata, SDK identifiers, output type, known package references, project file indicators, source artifacts, and configuration indicators to classify each project as one of the source-brief application type categories. It must preserve `Unknown` when evidence is insufficient rather than guessing.

### 3.6 Graph Fact Contributor

The graph fact contributor maps extracted repository, solution, project, package, file path, and dependency data into the shared snapshot accumulation model using the stable keys, node kinds, edge kinds, metadata, confidence, unknown-state, and evidence contracts established by earlier packages.

### 3.7 Evidence Capture

The evidence capture component records source evidence from solution files, project files, package references, analyzer references, `packages.config`, relevant imported build files, and path artifacts. Evidence must be specific enough to support later API, MCP, markdown, diff, and troubleshooting behavior.

### 3.8 Tests and Documentation

The test and documentation scope verifies mixed-language project loading, dependency extraction, application-type classification, old-style and SDK-style project support, evidence capture, orchestration integration, and contributor-facing guidance.

## 4. Functional Requirements

### 4.1 Shared Orchestration Integration

| ID | Requirement |
| --- | --- |
| FR-001 | WP005 extraction shall run only through the shared extraction orchestration path established by WP004. |
| FR-002 | The implementation shall not introduce a second public project extraction entrypoint that bypasses WP004 validation, run lifecycle, accumulation, snapshot assembly, or persistence handoff. |
| FR-003 | The project extraction stage shall receive the resolved repository root and explicit solution paths produced by WP004 request validation and resolution. |
| FR-004 | The project extraction stage shall contribute facts through the shared extraction accumulation model. |
| FR-005 | The project extraction stage shall preserve warnings and recoverable errors in the shared run and snapshot warning/error contracts. |
| FR-006 | The project extraction stage shall fail the run for blocking failures that make a submitted solution unusable, unless the failure can be represented as a documented partial-extraction warning under a multi-solution policy. |
| FR-007 | The implementation shall preserve deterministic stage ordering relative to existing and future extraction stages. |
| FR-008 | The implementation shall not require API endpoint shape changes to support project extraction. |

### 4.2 Repository and Solution Extraction

| ID | Requirement |
| --- | --- |
| FR-009 | The implementation shall extract a repository architecture node for the submitted repository root. |
| FR-010 | The repository node shall use a deterministic stable key independent of database IDs and developer-machine absolute paths where repository-root-relative identity is required. |
| FR-011 | The repository node shall include display name, normalized root path metadata, branch name, commit SHA, requested-by value, and request metadata where supported by existing contracts. |
| FR-012 | The implementation shall extract a solution architecture node for every submitted solution path. |
| FR-013 | Solution nodes shall use deterministic stable keys derived from repository identity and repository-relative solution path. |
| FR-014 | Solution nodes shall include display name, repository-relative path, normalized full path metadata where appropriate, and language/project summary metadata where available. |
| FR-015 | The implementation shall create `CONTAINS` relationships from the repository node to each submitted solution node. |
| FR-016 | The implementation shall capture evidence from each solution file proving the solution's existence and project declarations. |
| FR-017 | The implementation shall not silently omit submitted solutions. |
| FR-018 | Multi-solution repositories shall preserve each submitted solution as a distinct solution node. |

### 4.3 Solution Loading

| ID | Requirement |
| --- | --- |
| FR-019 | The implementation shall load submitted solutions using repository-approved .NET solution-loading infrastructure. |
| FR-020 | Solution loading shall support C# projects declared in submitted solutions. |
| FR-021 | Solution loading shall support VB.NET projects declared in submitted solutions. |
| FR-022 | Solution loading shall support mixed C# and VB.NET solutions. |
| FR-023 | Solution loading shall preserve solution-to-project membership even when the same project appears in multiple submitted solutions. |
| FR-024 | Solution loading shall report unsupported project kinds as warnings with evidence when they are visible in a solution but not extractable by WP005. |
| FR-025 | Solution loading shall not scan the repository for additional solutions that were not submitted in the WP004 request. |
| FR-026 | Solution loading shall produce user-actionable errors when a submitted solution cannot be opened. |
| FR-027 | Solution loading shall avoid relying on Visual Studio-only state that is unavailable in automated validation. |

### 4.4 Project Node Extraction

| ID | Requirement |
| --- | --- |
| FR-028 | The implementation shall extract project nodes for C# `.csproj` projects. |
| FR-029 | The implementation shall extract project nodes for VB.NET `.vbproj` projects. |
| FR-030 | Project nodes shall use deterministic stable keys derived from repository identity and repository-relative project path. |
| FR-031 | Project nodes shall include project name. |
| FR-032 | Project nodes shall include repository-relative project path. |
| FR-033 | Project nodes shall include language. |
| FR-034 | Project nodes shall include target framework or target frameworks. |
| FR-035 | Project nodes shall include output type. |
| FR-036 | Project nodes shall include assembly name. |
| FR-037 | Project nodes shall include root namespace where available. |
| FR-038 | Project nodes shall include SDK-style status. |
| FR-039 | Project nodes shall include old-style project status. |
| FR-040 | Project nodes shall include nullable setting where applicable. |
| FR-041 | Project nodes shall include implicit usings setting where applicable. |
| FR-042 | Project nodes shall include application type classification. |
| FR-043 | Project nodes shall include metadata for relevant SDK identifiers, project GUIDs, default namespace behavior, and project-file characteristics where available. |
| FR-044 | The implementation shall create `CONTAINS` relationships from solution nodes to their project nodes. |
| FR-045 | Project extraction shall avoid creating duplicate project nodes within one snapshot when the same project appears in multiple submitted solutions. |
| FR-046 | Project extraction shall preserve evidence from the project file for every extracted project node. |

### 4.5 Target Framework Extraction

| ID | Requirement |
| --- | --- |
| FR-047 | The implementation shall extract single-target frameworks from `TargetFramework`. |
| FR-048 | The implementation shall extract multi-target frameworks from `TargetFrameworks`. |
| FR-049 | The implementation shall preserve target framework values exactly as declared where practical. |
| FR-050 | The implementation shall normalize target framework values into a query-friendly metadata representation where supported by existing contracts. |
| FR-051 | The implementation shall support legacy target framework declarations used by old-style projects. |
| FR-052 | Missing or unresolved target framework information shall be represented explicitly as unknown with appropriate confidence or warning data. |
| FR-053 | Target framework evidence shall point to the project file location or evaluated project metadata source where practical. |

### 4.6 Project Format and Build Metadata Extraction

| ID | Requirement |
| --- | --- |
| FR-054 | The implementation shall identify SDK-style projects. |
| FR-055 | The implementation shall identify old-style projects. |
| FR-056 | The implementation shall extract the project SDK value for SDK-style projects where present. |
| FR-057 | The implementation shall extract output type values such as library, executable, web executable, or equivalent project output classification. |
| FR-058 | The implementation shall extract assembly name from explicit project metadata or apply documented project-system defaults when explicit metadata is absent. |
| FR-059 | The implementation shall extract root namespace for VB.NET projects where available. |
| FR-060 | The implementation shall extract root namespace for C# projects when explicitly declared. |
| FR-061 | The implementation shall extract nullable setting for SDK-style projects where present. |
| FR-062 | The implementation shall extract implicit usings setting for SDK-style projects where present. |
| FR-063 | The implementation shall capture relevant imported `.props` and `.targets` references where they are visible through project evaluation or project file declarations. |
| FR-064 | The implementation shall record warnings when build metadata cannot be evaluated deterministically. |

### 4.7 Project Reference Extraction

| ID | Requirement |
| --- | --- |
| FR-065 | The implementation shall extract project references declared by C# and VB.NET projects. |
| FR-066 | Project references shall create `REFERENCES` relationships from the referencing project node to the referenced project node when the target project is part of the snapshot. |
| FR-067 | Project references to projects outside the submitted solution set but inside the submitted repository shall be represented with a project or file-path target according to existing graph contract capabilities. |
| FR-068 | Project references that cannot be resolved shall be represented as warnings and evidence-backed unresolved reference metadata. |
| FR-069 | Project-reference stable keys shall be deterministic and independent of database IDs. |
| FR-070 | Project-reference relationships shall preserve directness, confidence, and evidence. |
| FR-071 | Duplicate project references shall be deduplicated deterministically within a snapshot. |
| FR-072 | Project-reference evidence shall identify the source project file and referenced path where practical. |

### 4.8 Package Reference Extraction

| ID | Requirement |
| --- | --- |
| FR-073 | The implementation shall extract SDK-style `PackageReference` items. |
| FR-074 | The implementation shall extract package ID. |
| FR-075 | The implementation shall extract package version where declared directly. |
| FR-076 | The implementation shall represent package version as unknown, inherited, or centrally managed when a version is not directly declared and cannot be resolved deterministically in WP005. |
| FR-077 | The implementation shall extract relevant package metadata such as private assets, include assets, exclude assets, and aliases where available. |
| FR-078 | The implementation shall create package nodes for referenced NuGet packages. |
| FR-079 | Package nodes shall use deterministic stable keys derived from normalized package identity and version state where required by the existing graph contract. |
| FR-080 | The implementation shall create `USES_PACKAGE` relationships from project nodes to package nodes. |
| FR-081 | Package-use relationships shall preserve package version, version source, directness, confidence, and evidence metadata where supported. |
| FR-082 | Package references declared in imported `.props` or `.targets` files shall be extracted when visible through the chosen project evaluation approach. |
| FR-083 | Package references that cannot be fully resolved shall be retained with explicit unknowns rather than omitted. |

### 4.9 `packages.config` Extraction

| ID | Requirement |
| --- | --- |
| FR-084 | The implementation shall detect `packages.config` files associated with old-style projects. |
| FR-085 | The implementation shall parse `packages.config` package entries. |
| FR-086 | The implementation shall extract package ID from each `packages.config` entry. |
| FR-087 | The implementation shall extract package version from each `packages.config` entry. |
| FR-088 | The implementation shall extract target framework from each `packages.config` entry where present. |
| FR-089 | The implementation shall create package nodes and `USES_PACKAGE` relationships for `packages.config` dependencies. |
| FR-090 | The implementation shall distinguish `packages.config` dependencies from SDK-style `PackageReference` dependencies in metadata. |
| FR-091 | Malformed `packages.config` files shall produce controlled warnings or errors with evidence rather than unhandled exceptions. |
| FR-092 | `packages.config` evidence shall identify the package entry location where practical. |

### 4.10 Analyzer Reference Extraction

| ID | Requirement |
| --- | --- |
| FR-093 | The implementation shall extract analyzer references declared by project files. |
| FR-094 | Analyzer references shall be represented as metadata on project nodes, file-path nodes, package nodes, or relationships according to the existing graph contract. |
| FR-095 | Analyzer references shall preserve path, package-derived identity, or unresolved identity where available. |
| FR-096 | Analyzer references shall include evidence from the project file or evaluated project metadata source. |
| FR-097 | Missing or unresolved analyzer paths shall be represented as warnings or unknown metadata when relevant. |

### 4.11 File Path Nodes and Imported Artifact Extraction

| ID | Requirement |
| --- | --- |
| FR-098 | The implementation shall create relevant `FilePath` nodes for solution files. |
| FR-099 | The implementation shall create relevant `FilePath` nodes for project files. |
| FR-100 | The implementation shall create relevant `FilePath` nodes for `packages.config` files. |
| FR-101 | The implementation shall create relevant `FilePath` nodes for `.props` and `.targets` artifacts when those artifacts provide extracted facts or evidence. |
| FR-102 | File-path nodes shall use repository-relative paths when the file is inside the submitted repository root. |
| FR-103 | File-path nodes shall not expose database IDs as logical identity. |
| FR-104 | The implementation shall link file-path nodes to repository, solution, project, package, or evidence facts where existing graph contracts support such relationships. |

### 4.12 Application Type Classification

| ID | Requirement |
| --- | --- |
| FR-105 | The implementation shall classify ASP.NET Core Web App projects. |
| FR-106 | The implementation shall classify ASP.NET Core Web API projects. |
| FR-107 | The implementation shall classify Classic ASP.NET Web App projects. |
| FR-108 | The implementation shall classify Web Forms App projects. |
| FR-109 | The implementation shall classify MVC App projects. |
| FR-110 | The implementation shall classify Web API 2 App projects. |
| FR-111 | The implementation shall classify Console App projects. |
| FR-112 | The implementation shall classify Worker Service projects. |
| FR-113 | The implementation shall classify Class Library projects. |
| FR-114 | The implementation shall classify Test Project projects. |
| FR-115 | The implementation shall classify Tooling Project projects. |
| FR-116 | The implementation shall classify projects as Unknown when evidence is insufficient or contradictory. |
| FR-117 | Application type classification shall be evidence-backed. |
| FR-118 | Application type classification shall use deterministic rules based on project SDKs, output type, known package references, project type GUIDs, target frameworks, source/configuration indicators, and repository-approved heuristics. |
| FR-119 | Contradictory indicators shall be represented with confidence and warning metadata rather than arbitrary guessing. |
| FR-120 | The classification implementation shall be designed so later runtime and UI extraction packages can refine or add evidence without changing the project node identity model. |

### 4.13 Evidence Capture

| ID | Requirement |
| --- | --- |
| FR-121 | Every repository, solution, project, package, project-reference, package-reference, analyzer-reference, and application-type fact shall have supporting evidence unless the fact is purely derived from already-evidenced persisted facts. |
| FR-122 | Evidence shall identify the source artifact path. |
| FR-123 | Evidence shall include line span data when the source artifact and parser can provide it. |
| FR-124 | Evidence shall include a snippet hash or preview where supported by existing evidence contracts. |
| FR-125 | Evidence from solution files shall support solution-to-project membership facts. |
| FR-126 | Evidence from project files shall support project metadata, project references, package references, analyzer references, and project format facts. |
| FR-127 | Evidence from `packages.config` files shall support legacy package facts. |
| FR-128 | Evidence from imported `.props` and `.targets` files shall support facts extracted from imported build metadata. |
| FR-129 | Evidence shall use stable keys and fingerprints consistent with WP002 and WP003 behavior. |
| FR-130 | Evidence capture shall avoid storing large full-file contents in metadata. |

### 4.14 Snapshot Persistence Output

| ID | Requirement |
| --- | --- |
| FR-131 | The project extraction slice shall contribute `Repository`, `Solution`, `Project`, `Package`, and relevant `FilePath` nodes to the generalized snapshot contract. |
| FR-132 | The project extraction slice shall contribute `CONTAINS`, `REFERENCES`, and `USES_PACKAGE` relationships to the generalized snapshot contract. |
| FR-133 | The project extraction slice shall contribute evidence records for extracted facts. |
| FR-134 | The project extraction slice shall contribute warnings and errors through the generalized snapshot contract. |
| FR-135 | The project extraction slice shall preserve confidence and unknown-state data where extraction is incomplete or inferred. |
| FR-136 | The project extraction slice shall not write directly to Neo4j outside the WP004 persistence handoff. |
| FR-137 | The persisted output shall remain queryable through the architecture-wide graph model, not a project-only aggregate. |

## 5. Non-Functional Requirements

### 5.1 Architecture and Boundaries

| ID | Requirement |
| --- | --- |
| NFR-001 | Domain and application contracts shall remain independent of API host, Neo4j driver, Visual Studio automation, and host-specific implementation details. |
| NFR-002 | Project extraction behavior shall be implemented in the appropriate application, Roslyn abstraction, extractor, or infrastructure slices while preserving Onion Architecture dependency direction. |
| NFR-003 | API host code shall not contain project extraction logic. |
| NFR-004 | Neo4j infrastructure code shall not contain project file parsing or application-type classification policy unless explicitly required by an adapter boundary. |
| NFR-005 | Future extraction slices shall be able to reuse project, solution, and package facts without duplicating extraction logic. |

### 5.2 Determinism and Identity

| ID | Requirement |
| --- | --- |
| NFR-006 | Stable keys shall be deterministic and independent of database IDs. |
| NFR-007 | Project identity shall be based on repository-relative project paths and repository identity rather than machine-specific absolute paths where practical. |
| NFR-008 | Solution identity shall be based on repository-relative solution paths and repository identity. |
| NFR-009 | Package identity shall normalize package IDs consistently and preserve version state deterministically. |
| NFR-010 | Duplicate extraction inputs and duplicate references shall be deduplicated deterministically. |
| NFR-011 | Metadata serialization shall be deterministic where it affects fingerprints, persistence, or test assertions. |

### 5.3 Legacy and Mixed Estate Support

| ID | Requirement |
| --- | --- |
| NFR-012 | The implementation shall support SDK-style .NET projects. |
| NFR-013 | The implementation shall support old-style .NET Framework project files where practical for WP005 scope. |
| NFR-014 | The implementation shall support C# and VB.NET projects. |
| NFR-015 | The implementation shall support mixed-language solutions. |
| NFR-016 | The implementation shall support `packages.config` package management. |
| NFR-017 | The implementation shall avoid requiring repository modernization before extraction can run. |

### 5.4 Reliability and Observability

| ID | Requirement |
| --- | --- |
| NFR-018 | Extraction shall log major project extraction lifecycle events using credential-safe structured logging. |
| NFR-019 | Extraction shall log solution loading failures, project loading failures, malformed project files, malformed package files, and unresolved references without exposing secrets. |
| NFR-020 | Extraction shall convert expected project-system errors into controlled warnings or run errors. |
| NFR-021 | Extraction shall not fail the entire process for one unsupported project type unless the configured multi-solution/project policy requires failure. |
| NFR-022 | Extraction shall remain testable without starting the Aspire AppHost process. |
| NFR-023 | Extraction shall remain testable without requiring Visual Studio to be installed. |

### 5.5 Security and Privacy

| ID | Requirement |
| --- | --- |
| NFR-024 | Repository paths, project paths, solution paths, package metadata, and requested-by values shall be treated as potentially sensitive operational data in logs. |
| NFR-025 | Error responses and run errors shall not expose secrets, environment variables, access tokens, connection strings, or raw stack traces. |
| NFR-026 | Project extraction shall not execute arbitrary repository scripts, build targets, commands, or package restore operations unless a later explicitly approved design requires it. |
| NFR-027 | Project evaluation shall minimize side effects and avoid build execution. |
| NFR-028 | Metadata values shall not be blindly written to logs. |

### 5.6 Documentation Standard

| ID | Requirement |
| --- | --- |
| NFR-029 | Implementation work derived from this specification shall apply the repository documentation-pass standard to public, internal, private, and other non-public types. |
| NFR-030 | Developer-level documentation shall explain solution loading, project file analysis, package extraction, application-type classification, evidence capture, warning/error handling, and orchestration integration. |
| NFR-031 | Documentation shall clearly distinguish WP005 project metadata extraction from later Roslyn semantic extraction in WP006. |

## 6. Data and Contract Model Requirements

### 6.1 Required Graph Concepts

The implementation shall use or align existing graph contracts for the following concepts:

| Concept | Purpose |
| --- | --- |
| Repository node | Represents the submitted repository root for the extraction run. |
| Solution node | Represents each submitted solution file. |
| Project node | Represents each C# or VB.NET project included by submitted solutions. |
| Package node | Represents each referenced NuGet package from `PackageReference` or `packages.config`. |
| FilePath node | Represents relevant files that participate in discovery, evidence, or relationship traversal. |
| `CONTAINS` relationship | Links repository-to-solution, solution-to-project, and other ownership/containment facts where appropriate. |
| `REFERENCES` relationship | Links project-to-project references and related dependency facts where appropriate. |
| `USES_PACKAGE` relationship | Links project nodes to package nodes. |
| Evidence record | Supports project metadata, dependency, package, analyzer, classification, and path facts. |
| Warning/error record | Captures non-blocking and blocking extraction issues. |

### 6.2 Project Metadata Fields

| Field | Requirement |
| --- | --- |
| Stable key | Deterministic project identity independent of database IDs. |
| Display name | Human-readable project name. |
| Project path | Repository-relative path preferred for graph identity and display. |
| Language | C# or VB.NET for WP005-supported project types. |
| Target frameworks | One or more declared or evaluated target frameworks. |
| Output type | Project output type from project metadata or documented defaults. |
| Assembly name | Explicit or defaulted assembly name. |
| Root namespace | VB.NET root namespace and C# explicit root namespace where present. |
| SDK style | Indicates SDK-style project format. |
| Old-style project | Indicates legacy project format. |
| Nullable setting | Nullable context setting where present. |
| Implicit usings | Implicit usings setting where present. |
| Application type | One supported application classification value or Unknown. |
| Metadata | Deterministic extra metadata for project-system facts not represented as first-class properties. |

### 6.3 Package Metadata Fields

| Field | Requirement |
| --- | --- |
| Package ID | NuGet package identifier, normalized for identity and preserving display casing where useful. |
| Version | Declared version, resolved version, unknown, inherited, or centrally managed state as appropriate. |
| Source type | `PackageReference`, `packages.config`, imported build file, or equivalent source category. |
| Target framework | Package target framework from `packages.config` where present. |
| Asset metadata | Include assets, exclude assets, private assets, aliases, and related metadata where available. |
| Evidence | Project file or `packages.config` evidence for the dependency. |

### 6.4 Application Type Values

The implementation shall support these application type values from the source brief:

| Value | Notes |
| --- | --- |
| ASP.NET Core Web App | Typically identified by SDK/package/source indicators for ASP.NET Core UI or web app hosting. |
| ASP.NET Core Web API | Typically identified by ASP.NET Core SDK/package/source indicators and API/controller endpoint patterns where available in WP005 scope. |
| Classic ASP.NET Web App | Identified through legacy web project metadata, configuration, project type GUIDs, or package indicators. |
| Web Forms App | Identified through legacy Web Forms indicators such as project type GUIDs, `.aspx` artifacts, or related metadata where available. |
| MVC App | Identified through ASP.NET MVC package/project indicators where available. |
| Web API 2 App | Identified through classic ASP.NET Web API package/project indicators where available. |
| Console App | Identified through executable output type and absence of stronger app-type indicators. |
| Worker Service | Identified through worker SDK/package/source indicators where available in WP005 scope. |
| Class Library | Identified through library output type and absence of stronger app-type indicators. |
| Test Project | Identified through known test SDK/packages, naming, or metadata indicators. |
| Tooling Project | Identified through build/tooling/package metadata or repository-approved naming and package indicators. |
| Unknown | Used when evidence is absent, insufficient, unsupported, or contradictory. |

## 7. Validation and Test Requirements

### 7.1 Unit Tests

| ID | Requirement |
| --- | --- |
| TR-001 | Tests shall prove repository and solution nodes are extracted from submitted solution inputs. |
| TR-002 | Tests shall prove C# projects are extracted from submitted solutions. |
| TR-003 | Tests shall prove VB.NET projects are extracted from submitted solutions. |
| TR-004 | Tests shall prove mixed C# and VB.NET solutions are extracted. |
| TR-005 | Tests shall prove multi-solution repositories preserve each submitted solution. |
| TR-006 | Tests shall prove a project appearing in multiple submitted solutions is deduplicated as a project node while preserving solution membership. |
| TR-007 | Tests shall prove SDK-style project format detection. |
| TR-008 | Tests shall prove old-style project format detection. |
| TR-009 | Tests shall prove single target framework extraction. |
| TR-010 | Tests shall prove multi-target framework extraction. |
| TR-011 | Tests shall prove output type extraction. |
| TR-012 | Tests shall prove assembly name extraction and documented defaults. |
| TR-013 | Tests shall prove root namespace extraction. |
| TR-014 | Tests shall prove nullable setting extraction. |
| TR-015 | Tests shall prove implicit usings extraction. |
| TR-016 | Tests shall prove project reference extraction and `REFERENCES` relationship creation. |
| TR-017 | Tests shall prove unresolved project references produce warnings or unknown metadata. |
| TR-018 | Tests shall prove SDK-style `PackageReference` extraction and `USES_PACKAGE` relationship creation. |
| TR-019 | Tests shall prove `packages.config` extraction and `USES_PACKAGE` relationship creation. |
| TR-020 | Tests shall prove analyzer reference extraction. |
| TR-021 | Tests shall prove relevant `FilePath` node creation. |
| TR-022 | Tests shall prove evidence records are created for solution membership, project metadata, project references, package references, and `packages.config` entries. |
| TR-023 | Tests shall prove evidence span behavior where line information is available. |
| TR-024 | Tests shall prove malformed project or package files produce controlled warnings or errors. |

### 7.2 Application Type Classification Tests

| ID | Requirement |
| --- | --- |
| TR-025 | Tests shall cover ASP.NET Core Web App classification. |
| TR-026 | Tests shall cover ASP.NET Core Web API classification. |
| TR-027 | Tests shall cover Classic ASP.NET Web App classification. |
| TR-028 | Tests shall cover Web Forms App classification. |
| TR-029 | Tests shall cover MVC App classification. |
| TR-030 | Tests shall cover Web API 2 App classification. |
| TR-031 | Tests shall cover Console App classification. |
| TR-032 | Tests shall cover Worker Service classification. |
| TR-033 | Tests shall cover Class Library classification. |
| TR-034 | Tests shall cover Test Project classification. |
| TR-035 | Tests shall cover Tooling Project classification. |
| TR-036 | Tests shall cover Unknown classification for insufficient or contradictory evidence. |
| TR-037 | Tests shall prove classification is deterministic for the same project inputs. |

### 7.3 Orchestration and Persistence Handoff Tests

| ID | Requirement |
| --- | --- |
| TR-038 | Tests shall prove WP005 extraction runs through the WP004 shared orchestration path. |
| TR-039 | Tests shall prove extracted project facts are contributed to the generalized snapshot contract. |
| TR-040 | Tests shall prove persistence receives `Repository`, `Solution`, `Project`, `Package`, and relevant `FilePath` nodes. |
| TR-041 | Tests shall prove persistence receives `CONTAINS`, `REFERENCES`, and `USES_PACKAGE` relationships. |
| TR-042 | Tests shall prove persistence receives evidence, warnings, and errors from the project extraction slice. |
| TR-043 | Tests shall prove project extraction does not write directly to Neo4j outside the snapshot persistence handoff. |

### 7.4 Automated Validation Constraints

| ID | Requirement |
| --- | --- |
| TR-044 | Automated validation shall not start the Aspire AppHost process because it blocks the executing agent. |
| TR-045 | Tests shall run through project-level or solution-level test commands appropriate to the repository. |
| TR-046 | Tests may use temporary in-test repositories and solution/project fixtures stored outside the repository root or under suitable test fixture directories. |
| TR-047 | Tests shall not require Visual Studio automation. |
| TR-048 | For this work package, do not run the full test suite unless repository guidance is later changed. |

## 8. Documentation Requirements

| ID | Requirement |
| --- | --- |
| DR-001 | Documentation shall describe WP005 project extraction scope and explicitly distinguish it from WP006 semantic extraction. |
| DR-002 | Documentation shall describe supported solution and project file types. |
| DR-003 | Documentation shall describe SDK-style and old-style project support. |
| DR-004 | Documentation shall describe C# and VB.NET support, including mixed-language solutions. |
| DR-005 | Documentation shall describe package extraction from `PackageReference` and `packages.config`. |
| DR-006 | Documentation shall describe project-reference and analyzer-reference extraction. |
| DR-007 | Documentation shall describe application type classification rules and Unknown behavior. |
| DR-008 | Documentation shall describe evidence capture for solution files, project files, package references, `packages.config`, `.props`, and `.targets`. |
| DR-009 | Documentation shall describe warning and error behavior for unsupported projects, unresolved references, and malformed artifacts. |
| DR-010 | Documentation shall describe how WP005 integrates with the WP004 extraction request, run lifecycle, accumulation, snapshot assembly, and persistence handoff. |
| DR-011 | Documentation shall include manual verification guidance where relevant without requiring automated validation to start the Aspire AppHost. |
| DR-012 | Documentation shall include the output path of this specification: `docs/005-Repository-Solution-Project-and-Package-Extraction/spec-wp005-repository-solution-project-and-package-extraction.md`. |

## 9. Out of Scope

WP005 shall not implement the following capabilities:

- Compiler-grade semantic extraction of namespaces, types, methods, fields, properties, calls, inheritance, interface implementation, constructor dependencies, attributes, or diagnostics beyond project-loading metadata needed for WP005.
- Runtime endpoint, controller, hosted service, API route, middleware, or dependency-injection extraction.
- Configuration key extraction except where project files, imported build files, or package files are direct evidence for WP005 facts.
- Data-access extraction from `.dbml`, `.edmx`, `.xsd`, SQL scripts, entities, or DbContext definitions.
- External integration extraction for HTTP clients, queues, topics, or service dependencies.
- .NET UI extraction for Razor, XAML, AXAML, Windows Forms, WPF, WinUI, MAUI, Avalonia, or related UI artifacts beyond app-type indicators available from project metadata.
- Hotlist rule evaluation, rule catalog loading, findings generation, or metric calculation beyond warnings/errors required for extraction behavior.
- Markdown generation.
- MCP tool or resource refresh behavior.
- Query APIs, graph traversal APIs, snapshot diff APIs, or evidence viewer APIs.
- Discovery UI host, pages, dashboard, explorer, graph view, evidence viewer, hotlist viewer, prompt panel, or front-end assets.
- Package vulnerability analysis, license analysis, package restore, package download, or NuGet feed calls.
- Executing repository build targets or arbitrary scripts as part of extraction.

## 10. Acceptance Criteria

WP005 is complete when all of the following are true:

1. Submitted solutions are loaded through the shared WP004 extraction path.
2. Repository and solution nodes are extracted and persisted through the generalized snapshot contract.
3. C# projects are extracted from submitted solutions.
4. VB.NET projects are extracted from submitted solutions.
5. Mixed C# and VB.NET solutions are supported.
6. Multi-solution repositories preserve every submitted solution and deduplicate shared project nodes deterministically.
7. Project nodes include target frameworks, output type, assembly name, root namespace, SDK-style status, old-style project status, nullable setting, implicit usings, analyzer references, project references, package references, `packages.config` references, and application type classification where available.
8. SDK-style and old-style project formats are both represented.
9. `PackageReference` dependencies are represented as package nodes and `USES_PACKAGE` relationships.
10. `packages.config` dependencies are represented as package nodes and `USES_PACKAGE` relationships.
11. Project references are represented as `REFERENCES` relationships with evidence.
12. Analyzer references are extracted and represented according to the graph contract.
13. Application type classification covers ASP.NET Core Web App, ASP.NET Core Web API, Classic ASP.NET Web App, Web Forms App, MVC App, Web API 2 App, Console App, Worker Service, Class Library, Test Project, Tooling Project, and Unknown.
14. Relevant `FilePath` nodes are persisted for solution files, project files, package files, and imported build artifacts that support extracted facts.
15. Evidence is captured from project files, solution files, package references, `packages.config`, and relevant configuration artifacts.
16. Warnings and unknowns are represented explicitly for unresolved or unsupported project facts.
17. Tests cover multi-solution repositories, mixed C#/VB.NET solutions, project references, package references, old-style projects, SDK-style projects, application type classifications, and evidence spans.
18. Documentation is updated for project extraction behavior, classification rules, evidence capture, warning/error behavior, and orchestration integration.
19. No Archon Discovery UI implementation is introduced.
20. No later semantic, runtime, configuration, data-access, integration, markdown, MCP, query, diff, or rule capability is marked as deferred without being assigned to its later existing work package in `docs/foundation/work-packages.md`.

## 11. Implementation Guidance

### 11.1 Expected Project Areas

Implementation derived from this specification is expected to work primarily in these areas, adjusted as needed to match the repository structure established by WP001 through WP004:

```text
src/
  Archon.Application/
	Extraction/
	  Pipeline/
	  Projects/
	  Snapshots/
  Archon.Roslyn.Abstractions/
	Solutions/
	Projects/
  Archon.Extractors.Projects/ or equivalent project extraction slice
	Solutions/
	Projects/
	Packages/
	Classification/
	Evidence/
  Archon.Infrastructure/ or equivalent adapters
	ProjectSystem/

test/
  Archon.Application.Tests/
	Extraction/
  Archon.Roslyn.Abstractions.Tests/
	Solutions/
	Projects/
  Archon.Extractors.Projects.Tests/ or equivalent
	Solutions/
	Projects/
	Packages/
	Classification/
	Evidence/
```

The exact folder and project names may be adjusted to match existing repository conventions. The architectural placement must remain unchanged: extraction policy belongs outside hosts, project-system adapters remain behind abstractions, HTTP translation remains in the API host, and Neo4j implementation details remain outside inward layers.

### 11.2 Sequencing Guidance

A recommended implementation sequence is:

1. Align project extraction interfaces with the WP004 stage and accumulation model.
2. Implement solution loading for submitted solution paths.
3. Implement repository and solution graph fact contribution.
4. Implement C# and VB.NET project metadata extraction.
5. Implement target framework, output type, assembly name, root namespace, project format, nullable, and implicit-usings extraction.
6. Implement project-reference extraction.
7. Implement `PackageReference` extraction.
8. Implement `packages.config` extraction.
9. Implement analyzer-reference extraction.
10. Implement relevant file-path node contribution.
11. Implement application type classification.
12. Implement evidence capture and warning/error behavior.
13. Add unit, orchestration, and persistence handoff tests.
14. Update wiki and contributor documentation required by the documentation pass.

### 11.3 Technical Challenges and Decisions

| Area | Guidance |
| --- | --- |
| MSBuild and project evaluation | The implementation should extract useful evaluated metadata without executing arbitrary build targets or requiring Visual Studio automation. |
| Old-style project support | Legacy project files and `packages.config` are first-class requirements, not optional edge cases. |
| Central package management | If package versions are inherited from central management and cannot be deterministically resolved in WP005, represent the version source or unknown state explicitly. |
| Multi-solution duplication | Project nodes should be deduplicated by deterministic project identity while solution membership remains explicit. |
| Application type confidence | Classification should prefer evidence-backed `Unknown` over overconfident guesses when indicators conflict. |
| Evidence line spans | XML artifacts should include line spans where the selected parser supports line information; otherwise evidence should still identify the artifact and fact source. |
| Future semantic extraction | WP005 should not preempt WP006 by extracting type or method semantic facts, but it should preserve enough project metadata for WP006 to build on. |

## 12. Traceability Matrix

| Source Requirement | Covered By |
| --- | --- |
| `work-packages.md` WP005 objective | Sections 1, 3, 4, 10 |
| `work-packages.md` WP005 required implementation | Sections 4, 6, 7, 10 |
| `work-packages.md` WP005 completion criteria | Sections 7, 8, 10 |
| Source brief section 14.1 pipeline stages | Sections 3, 4.1, 11 |
| Source brief section 14.2 extraction scope | Sections 1, 4, 9 |
| Source brief section 15 C# and VB.NET support | Sections 2, 4.3, 4.4, 7 |
| Source brief section 16 project and package extraction | Sections 4.4 through 4.12, 6, 10 |
| Source brief section 17.1 project-level dependencies | Sections 4.7, 4.8, 4.9, 7 |
| Source brief Appendix E.6.1 repository and solution modeling | Sections 4.2, 4.14, 6 |
| Source brief Appendix E.7.2 project and code slice implementation | Sections 4, 7, 10 |
| Documentation-pass requirement for internal and non-public types | Sections 5.6, 8 |

## 13. Open Questions for Implementation Planning

The following implementation-planning questions have been resolved and shall guide WP005 implementation:

1. **Project-system loading approach**: WP005 shall use a hybrid approach. Use `MSBuildWorkspace` or Roslyn where solution and project loading are needed for C# and VB.NET compatibility, and use lower-level XML/MSBuild project evaluation for deterministic metadata extraction from `.csproj`, `.vbproj`, `.props`, `.targets`, and `packages.config`. Keep this behavior behind a repository-specific abstraction so WP006 can reuse the loaded project context.
2. **Central package management resolution**: WP005 shall resolve centrally managed versions when they are deterministic and local, including support for `Directory.Packages.props`. If the version cannot be resolved without package restore or external feed access, represent it as centrally managed, inherited, or unknown with evidence. WP005 shall not call NuGet feeds or perform package restore.
3. **Unsupported project policy**: WP005 shall continue extraction with warnings by default when submitted solutions contain unsupported project types. Unsupported project declarations shall be captured as evidence-backed warnings. The run shall fail only when a submitted solution itself cannot be opened or when no supported projects can be extracted from any submitted solution.
4. **Application type confidence thresholds**: WP005 shall use a deterministic confidence model. Direct SDK, project type GUID, output type, or explicit package indicators shall be high confidence. Strong source or configuration artifact indicators shall be medium confidence. Naming conventions or weak heuristics shall be low confidence. Insufficient or contradictory evidence shall result in `Unknown`, and `Unknown` is preferred over low-confidence guessing where classification affects downstream behavior.
5. **Imported build file scope**: WP005 shall inspect only local, repository-contained imports. This includes `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props`, and explicitly imported `.props` or `.targets` files inside the repository. WP005 shall not execute targets and shall not traverse external SDK or package import chains beyond what is safely exposed by evaluated metadata.
6. **Evidence span granularity**: WP005 shall capture exact XML element spans where practical. Project files and `packages.config` evidence should point to the relevant XML element, and solution-file evidence should point to project declarations where feasible. If exact spans are unavailable, file-level evidence plus stable snippet hash or preview is acceptable where supported by existing evidence contracts.

## 14. Target Output Path

This specification is created at:

`docs/005-Repository-Solution-Project-and-Package-Extraction/spec-wp005-repository-solution-project-and-package-extraction.md`
