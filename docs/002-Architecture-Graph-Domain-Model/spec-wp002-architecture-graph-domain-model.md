# WP002 Specification - Architecture Graph Domain Model and Shared Contracts

## Document Control

| Field | Value |
| --- | --- |
| Work Package | WP002 - Architecture Graph Domain Model and Shared Contracts |
| Output Path | `docs/002-Architecture-Graph-Domain-Model/spec-wp002-architecture-graph-domain-model.md` |
| Source Work Package | `docs/foundation/work-packages.md` WP002 |
| Source Brief | `docs/foundation/archon_full_concept_brief.md` |
| Specification Basis | `spec-template_v1.1.md` requested; template file was not present in the workspace at creation time, so this document follows the repository specification structure and documented work-package rules. |
| Status | Draft |
| Audience | Product owner, architect, implementation team, test engineer |

## 1. Overview

### 1.1 Purpose

This specification defines the requirements for WP002, the Archon work package that establishes the architecture graph domain model and shared application contracts. WP002 creates the complete in-memory and contract-level representation of snapshots, repositories, solutions, architecture nodes, architecture edges, evidence, rules, findings, metrics, generated summaries, stable keys, fingerprints, classifications, confidence, unknowns, and extraction accumulation.

The package provides the canonical model used by later extraction, persistence, query, MCP, markdown, diff, and hotlist work packages. It does not implement Neo4j persistence, Roslyn extraction, API-triggered orchestration, MCP tools, markdown export, or Discovery UI behavior.

### 1.2 Background

Archon is a .NET-first architecture intelligence platform that projects deterministic Roslyn and repository analysis into a persistent Architecture Semantic Graph. The source brief requires all architectural statements to be evidence-backed unless purely derived from persisted facts, all unknowns to be explicit, all identities to use deterministic stable keys, and Neo4j to remain the system of record for persisted extraction output.

WP002 sits immediately after the solution foundation. It turns the graph concepts from the foundation documents into domain and application contracts that later packages can populate and persist without redesigning the model.

### 1.3 High-Level Scope

WP002 covers the shared architecture graph contract layer:

- Domain value objects and smart-enum-style controlled value sets for graph classifications and controlled value sets.
- Snapshot-scoped models for repositories, solutions, snapshots, nodes, edges, evidence, rules, findings, metrics, and generated summaries.
- Stable-key generation contracts and implementation.
- Fingerprint generation contracts and implementation.
- Unknown-state, confidence, and knowledge classification contracts.
- Extraction accumulation contracts used by all future extractor slices.
- Unit tests for deterministic keys, deterministic fingerprints, serialization, metadata, and accumulation behavior.
- Documentation updates that explain the model and implementation expectations.

## 2. System Context

### 2.1 Product Context

Archon will accept API-driven extraction requests, analyze .NET repositories and solutions, assemble one architecture snapshot, persist the result to Neo4j, and expose evidence-backed graph knowledge through API and MCP surfaces. WP002 provides the shared contracts that allow those later capabilities to speak one consistent graph language.

The model must be broad enough to represent code, project metadata, packages, endpoints, UI concepts, data-access artifacts, configuration, external integrations, findings, metrics, summaries, and explicit unknowns without requiring a separate domain model per extractor.

### 2.2 Source References

WP002 must align with these source materials:

- `docs/foundation/work-packages.md` WP002 objective, required implementation, and completion criteria.
- `docs/foundation/archon_full_concept_brief.md` section 7 for Roslyn projection into the Architecture Semantic Graph.
- `docs/foundation/archon_full_concept_brief.md` section 12 for the core data model, evidence, rules, findings, metrics, generated summaries, classification, and unknowns.
- `docs/foundation/archon_full_concept_brief.md` section 13 for deterministic stable keys.
- `docs/foundation/archon_full_concept_brief.md` sections 32 and 33 for quality, confidence, classification, and metrics.
- `docs/foundation/archon_full_concept_brief.md` Appendix E sections E.2 through E.5.5 for target state, graph architecture, functional requirements, core graph elements, supporting relationships, stable-key strategy, and metadata strategy.
- `.github/instructions/documentation-pass.instructions.md` for mandatory developer documentation expectations in any coding implementation plan derived from this specification.

### 2.3 Users and Stakeholders

| Stakeholder | Interest |
| --- | --- |
| Product owner | Confirms the domain model fully supports the Archon product vision and later work-package sequence. |
| Architect | Confirms graph concepts, stable identity, evidence, unknowns, and model boundaries are coherent. |
| Developer | Implements later extractors, persistence, API, MCP, and markdown behavior against one shared contract. |
| Test engineer | Verifies deterministic behavior, serialization, metadata handling, unknown enforcement, and accumulation behavior. |
| Future API and MCP consumer | Depends on evidence-backed, stable, confidence-aware graph facts. |

## 3. Component Summary

### 3.1 Domain Model

The domain model contains the stable core architecture concepts that are independent of delivery and storage. It defines the controlled value sets, value objects, and invariants needed to represent graph facts consistently.

### 3.2 Application Contracts

The application contracts define snapshot assembly and extractor-facing DTOs used by extraction slices and later orchestration packages. They provide the shape of the architecture snapshot before it is persisted to Neo4j.

### 3.3 Stable-Key Component

The stable-key component centralizes deterministic stable-key creation. Every extraction slice must use this component rather than constructing stable keys independently.

### 3.4 Fingerprint Component

The fingerprint component centralizes deterministic fingerprint creation for diffable records. Later diff and persistence packages rely on these fingerprints to detect changed graph facts without feature-specific comparison logic.

### 3.5 Unknown and Confidence Model

The unknown and confidence model ensures that uncertainty is explicit. Nodes, edges, evidence, and findings must carry knowledge classification, confidence, and unknown state in a consistent way.

### 3.6 Extraction Accumulation Model

The extraction accumulation model provides the shared collection surface through which all future extractor slices contribute nodes, edges, evidence, findings, metrics, generated summaries, warnings, and errors into one snapshot.

## 4. Functional Requirements

### 4.1 Domain Value Objects and Controlled Value Sets

| ID | Requirement |
| --- | --- |
| FR-001 | The implementation shall define domain value objects for stable keys, fingerprints, confidence values, unknown state, metadata payloads, snapshot identifiers, rule codes, rule versions, and evidence locations where applicable. |
| FR-002 | The implementation shall define a `NodeKind` controlled value set with all required node kinds from the source brief. |
| FR-003 | The implementation shall define an `EdgeKind` controlled value set with all required edge kinds from the source brief. |
| FR-004 | The implementation shall define an `EvidenceKind` controlled value set with all required evidence kinds from the source brief. |
| FR-005 | The implementation shall define a `RuleCategory` controlled value set with all required rule categories from the source brief. |
| FR-006 | The implementation shall define `FindingSeverity` values for critical, high, medium, low, and informational findings. |
| FR-007 | The implementation shall define `FindingStatus` values for open, acknowledged, suppressed, resolved, and unknown findings. |
| FR-008 | The implementation shall define `KnowledgeKind` values for fact, inference, unknown, and human-confirmed knowledge. |
| FR-009 | The implementation shall define metric scope and generated-summary kind value sets sufficient for snapshot-scoped, node-scoped, edge-scoped, graph-scoped, project-scoped, and modernization-oriented outputs. |
| FR-010 | Controlled value sets shall serialize deterministically as stable string values rather than numeric enum ordinals in external contracts. |
| FR-010A | Controlled value sets shall be implemented using a smart-enum/value-object style rather than ordinary numeric C# enums, so each value has a stable external string identity and can be extended with additional metadata or behavior later. |

### 4.2 Required Node Kinds

| ID | Requirement |
| --- | --- |
| FR-011 | `NodeKind` shall include `Repository`. |
| FR-012 | `NodeKind` shall include `Solution`. |
| FR-013 | `NodeKind` shall include `Project`. |
| FR-014 | `NodeKind` shall include `Package`. |
| FR-015 | `NodeKind` shall include `Namespace`. |
| FR-016 | `NodeKind` shall include `Type`. |
| FR-017 | `NodeKind` shall include `Method`. |
| FR-018 | `NodeKind` shall include `Property`. |
| FR-019 | `NodeKind` shall include `Field`. |
| FR-020 | `NodeKind` shall include `Endpoint`. |
| FR-021 | `NodeKind` shall include `Controller`. |
| FR-022 | `NodeKind` shall include `HostedService`. |
| FR-023 | `NodeKind` shall include `UiApplication`. |
| FR-024 | `NodeKind` shall include `UiComponent`. |
| FR-025 | `NodeKind` shall include `UiPage`. |
| FR-026 | `NodeKind` shall include `UiView`. |
| FR-027 | `NodeKind` shall include `UiLayout`. |
| FR-028 | `NodeKind` shall include `UiRoute`. |
| FR-029 | `NodeKind` shall include `UiControl`. |
| FR-030 | `NodeKind` shall include `UiResource`. |
| FR-031 | `NodeKind` shall include `UiStyle`. |
| FR-032 | `NodeKind` shall include `ViewModel`. |
| FR-033 | `NodeKind` shall include `Command`. |
| FR-034 | `NodeKind` shall include `Binding`. |
| FR-035 | `NodeKind` shall include `ConfigurationKey`. |
| FR-036 | `NodeKind` shall include `DbContext`. |
| FR-037 | `NodeKind` shall include `LinqToSqlDataContext`. |
| FR-038 | `NodeKind` shall include `Entity`. |
| FR-039 | `NodeKind` shall include `DatabaseTable`. |
| FR-040 | `NodeKind` shall include `DatabaseColumn`. |
| FR-041 | `NodeKind` shall include `StoredProcedure`. |
| FR-042 | `NodeKind` shall include `ExternalService`. |
| FR-043 | `NodeKind` shall include `Queue`. |
| FR-044 | `NodeKind` shall include `Topic`. |
| FR-045 | `NodeKind` shall include `FilePath`. |
| FR-046 | `NodeKind` shall include `Pipeline`. |
| FR-047 | `NodeKind` shall include `OpenApiDocument`. |
| FR-048 | `NodeKind` shall include `Dockerfile`. |
| FR-049 | `NodeKind` shall include `SqlScript`. |
| FR-050 | `NodeKind` shall include `GeneratedArtifact`. |

### 4.3 Required Edge Kinds

| ID | Requirement |
| --- | --- |
| FR-051 | `EdgeKind` shall include `CONTAINS`. |
| FR-052 | `EdgeKind` shall include `REFERENCES`. |
| FR-053 | `EdgeKind` shall include `CALLS`. |
| FR-054 | `EdgeKind` shall include `IMPLEMENTS`. |
| FR-055 | `EdgeKind` shall include `INHERITS`. |
| FR-056 | `EdgeKind` shall include `INJECTS`. |
| FR-057 | `EdgeKind` shall include `EXPOSES`. |
| FR-058 | `EdgeKind` shall include `HANDLES`. |
| FR-059 | `EdgeKind` shall include `USES_CONFIG`. |
| FR-060 | `EdgeKind` shall include `USES_DB_CONTEXT`. |
| FR-061 | `EdgeKind` shall include `USES_LINQ_TO_SQL_CONTEXT`. |
| FR-062 | `EdgeKind` shall include `MAPS_ENTITY`. |
| FR-063 | `EdgeKind` shall include `MAPS_TABLE`. |
| FR-064 | `EdgeKind` shall include `MAPS_COLUMN`. |
| FR-065 | `EdgeKind` shall include `READS_TABLE`. |
| FR-066 | `EdgeKind` shall include `WRITES_TABLE`. |
| FR-067 | `EdgeKind` shall include `CALLS_STORED_PROCEDURE`. |
| FR-068 | `EdgeKind` shall include `EXECUTES_RAW_SQL`. |
| FR-069 | `EdgeKind` shall include `CALLS_EXTERNAL_SERVICE`. |
| FR-070 | `EdgeKind` shall include `USES_PACKAGE`. |
| FR-071 | `EdgeKind` shall include `DECLARES_ENDPOINT`. |
| FR-072 | `EdgeKind` shall include `DECLARES_COMPONENT`. |
| FR-073 | `EdgeKind` shall include `DECLARES_UI_ROUTE`. |
| FR-074 | `EdgeKind` shall include `USES_COMPONENT`. |
| FR-075 | `EdgeKind` shall include `USES_LAYOUT`. |
| FR-076 | `EdgeKind` shall include `USES_CONTROL`. |
| FR-077 | `EdgeKind` shall include `USES_UI_RESOURCE`. |
| FR-078 | `EdgeKind` shall include `USES_STYLE`. |
| FR-079 | `EdgeKind` shall include `BINDS_TO`. |
| FR-080 | `EdgeKind` shall include `USES_COMMAND`. |
| FR-081 | `EdgeKind` shall include `USES_VIEW_MODEL`. |
| FR-082 | `EdgeKind` shall include `NAVIGATES_TO`. |
| FR-083 | `EdgeKind` shall include `HANDLES_UI_EVENT`. |
| FR-084 | `EdgeKind` shall include `CALLS_API`. |
| FR-085 | `EdgeKind` shall include `REGISTERED_AS_SERVICE`. |
| FR-086 | `EdgeKind` shall include `DEPENDS_ON`. |

### 4.4 Snapshot-Scoped Models

| ID | Requirement |
| --- | --- |
| FR-087 | The implementation shall define a repository model with stable key, name, root path, optional remote URL, optional default branch, and metadata. |
| FR-088 | The implementation shall define a solution model with repository association, stable key, name, path, and metadata. |
| FR-089 | The implementation shall define a snapshot header model with stable key, repository association, optional branch name, optional commit SHA, started timestamp, optional completed timestamp, extraction version, status, warnings, errors, and metadata. |
| FR-090 | The implementation shall define an architecture node model with snapshot scope, stable key, node kind, display name, optional qualified name, search name, optional language, optional project stable key, optional parent node stable key, knowledge kind, ownership, external category, confidence, unknown state, optional primary evidence reference, metadata, and fingerprint. |
| FR-091 | The implementation shall define an architecture edge model with snapshot scope, stable key, edge kind, source node stable key, target node stable key, directness flag, knowledge kind, confidence, unknown state, optional primary evidence reference, metadata, and fingerprint. |
| FR-092 | The implementation shall define an evidence model with snapshot scope, stable key, evidence kind, file path, optional start line, optional end line, optional symbol name, optional containing symbol, optional snippet hash, optional snippet preview, knowledge kind, confidence, unknown state, metadata, and fingerprint. |
| FR-093 | The implementation shall define a rule model with rule code, name, category, severity, default status, enabled flag, version, description, definition JSON, source URLs, built-in flag, owner scope, and metadata. |
| FR-094 | The implementation shall define a finding model with snapshot scope, stable key, rule code, rule version, severity, status, title, description, knowledge kind, confidence, optional primary node stable key, optional primary evidence reference, optional first-seen snapshot, optional latest-seen snapshot, optional suppression reason, optional suppressed-by value, metadata, and fingerprint. |
| FR-095 | The implementation shall define a metric model with snapshot scope, stable key, metric kind, scope kind, optional node stable key, optional edge stable key, optional primary evidence reference, name, optional numeric value, optional text value, optional unit, metadata, and fingerprint. |
| FR-096 | The implementation shall define a generated summary model with snapshot scope, stable key, summary kind, optional target stable key, format, title, content, metadata, and fingerprint. |
| FR-097 | Snapshot-owned graph fact models shall not require Neo4j database IDs. |

### 4.5 Evidence, Unknowns, and Confidence

| ID | Requirement |
| --- | --- |
| FR-098 | Nodes, edges, evidence, and findings shall require knowledge classification. |
| FR-099 | Nodes, edges, evidence, and findings shall require confidence. |
| FR-100 | Nodes, edges, evidence, and findings shall require explicit unknown-state representation. |
| FR-101 | A model instance classified as unknown shall carry a non-empty unknown reason. |
| FR-102 | A model instance that has unknown data shall carry a non-empty unknown reason. |
| FR-103 | Unknowns shall be represented explicitly rather than through null omission alone. |
| FR-104 | Confidence shall support deterministic comparison and serialization. |
| FR-105 | Evidence records shall be capable of representing project files, source code, configuration, DBML files, designer-generated code, SQL scripts, pipeline files, OpenAPI documents, Dockerfiles, generated artifacts, package references, compiler symbols, compiler diagnostics, inferences, and manual annotations. |

### 4.6 Stable-Key Generation

| ID | Requirement |
| --- | --- |
| FR-106 | Stable-key generation shall be implemented in one shared component. |
| FR-107 | Stable keys shall be deterministic for equivalent logical input. |
| FR-108 | Stable keys shall be independent of database IDs. |
| FR-109 | Stable keys shall normalize repository-relative paths consistently. |
| FR-110 | Stable keys shall use stable string prefixes defined by the source brief. |
| FR-111 | The shared component shall generate `repository://` keys. |
| FR-112 | The shared component shall generate `solution://` keys. |
| FR-113 | The shared component shall generate `project://` keys. |
| FR-114 | The shared component shall generate `package://` keys. |
| FR-115 | The shared component shall generate `namespace://` keys. |
| FR-116 | The shared component shall generate `type://` keys. |
| FR-117 | The shared component shall generate `method://` keys. |
| FR-118 | The shared component shall generate `property://` keys. |
| FR-119 | The shared component shall generate `field://` keys. |
| FR-120 | The shared component shall generate `endpoint://` keys. |
| FR-121 | The shared component shall generate `controller://` keys. |
| FR-122 | The shared component shall generate `hostedservice://` keys. |
| FR-123 | The shared component shall generate `config://` keys. |
| FR-124 | The shared component shall generate `dbcontext://` keys. |
| FR-125 | The shared component shall generate `linqtosql://` keys. |
| FR-126 | The shared component shall generate `entity://` keys. |
| FR-127 | The shared component shall generate `dbtable://` keys. |
| FR-128 | The shared component shall generate `dbcolumn://` keys. |
| FR-129 | The shared component shall generate `storedprocedure://` keys. |
| FR-130 | The shared component shall generate `externalservice://` keys. |
| FR-131 | The shared component shall generate `queue://` keys. |
| FR-132 | The shared component shall generate `topic://` keys. |
| FR-133 | The shared component shall generate `file://` keys. |
| FR-134 | The shared component shall generate `pipeline://` keys. |
| FR-135 | The shared component shall generate `rule://` keys. |
| FR-136 | The shared component shall generate `finding://` keys. |
| FR-137 | The shared component shall generate `metric://` keys. |
| FR-138 | The shared component shall generate `summary://` keys. |
| FR-139 | Stable-key generation shall be documented so future extraction slices do not invent divergent formats. |

### 4.7 Fingerprint Generation

| ID | Requirement |
| --- | --- |
| FR-140 | Fingerprint generation shall be implemented for architecture nodes. |
| FR-141 | Fingerprint generation shall be implemented for architecture edges. |
| FR-142 | Fingerprint generation shall be implemented for evidence. |
| FR-143 | Fingerprint generation shall be implemented for findings. |
| FR-144 | Fingerprint generation shall be implemented for metrics. |
| FR-145 | Fingerprint generation shall be implemented for generated summaries. |
| FR-146 | Fingerprints shall be deterministic for equivalent logical input. |
| FR-147 | Fingerprints shall change when normalized diff-relevant model content changes. |
| FR-148 | Fingerprints shall not include process-local, database-local, or machine-local values. |
| FR-149 | Fingerprint generation shall use a stable canonical representation of metadata payloads. |
| FR-150 | Fingerprints shall support later snapshot diff across nodes, edges, findings, metrics, evidence where needed, and generated summaries. |

### 4.8 Metadata Handling

| ID | Requirement |
| --- | --- |
| FR-151 | Metadata shall support arbitrary extraction-specific structured data without weakening normalized graph properties. |
| FR-152 | Metadata shall preserve deterministic serialization order for fingerprinting and tests. |
| FR-153 | Metadata shall not be used for core fields that the source brief identifies as normalized graph properties. |
| FR-154 | Metadata shall support route templates, HTTP verb sets, options binding details, configuration provider names, connection string names, SQL classification hints, queue or topic transport details, DI registration details, table schema details, provider-specific database mapping payloads, and extraction-specific classification annotations. |
| FR-155 | Metadata APIs shall make empty metadata explicit and safe to serialize. |

### 4.9 Extraction Accumulation Contracts

| ID | Requirement |
| --- | --- |
| FR-156 | The implementation shall define `ExtractedArchitectureSnapshot` or an equivalent authoritative snapshot assembly contract. |
| FR-157 | The snapshot assembly contract shall contain repositories. |
| FR-158 | The snapshot assembly contract shall contain solutions. |
| FR-159 | The snapshot assembly contract shall contain snapshot header data. |
| FR-160 | The snapshot assembly contract shall contain architecture nodes. |
| FR-161 | The snapshot assembly contract shall contain architecture edges. |
| FR-162 | The snapshot assembly contract shall contain evidence. |
| FR-163 | The snapshot assembly contract shall contain findings. |
| FR-164 | The snapshot assembly contract shall contain metrics. |
| FR-165 | The snapshot assembly contract shall contain generated summaries. |
| FR-166 | The snapshot assembly contract shall contain warnings. |
| FR-167 | The snapshot assembly contract shall contain errors. |
| FR-168 | The accumulation contract shall allow multiple extractor slices to contribute to one snapshot without owning separate top-level persistence models. |
| FR-169 | The accumulation contract shall prevent accidental mutation patterns that break deterministic fingerprinting or stable-key behavior. |
| FR-170 | The accumulation contract shall provide predictable duplicate handling for equivalent stable keys within the same snapshot. |
| FR-171 | The accumulation contract shall preserve all warnings and errors emitted by extractor slices. |

### 4.10 Project and Layer Placement

| ID | Requirement |
| --- | --- |
| FR-172 | Pure graph concepts, value objects, enums, and domain invariants shall be implemented in `Archon.Domain`. |
| FR-173 | Extractor-facing contracts, snapshot assembly DTOs, and accumulation abstractions shall be implemented in `Archon.Application` when they represent cross-module application contracts. |
| FR-174 | WP002 shall not introduce infrastructure dependencies into `Archon.Domain` or `Archon.Application`. |
| FR-175 | WP002 shall not implement Neo4j persistence. |
| FR-176 | WP002 shall not implement Roslyn loading or extractor behavior. |
| FR-177 | WP002 shall not implement API endpoints beyond any unchanged existing bootstrap behavior. |
| FR-178 | WP002 shall not implement MCP tools, resources, or prompts. |
| FR-179 | WP002 shall not introduce Archon Discovery UI behavior. |

## 5. Non-Functional Requirements

### 5.1 Determinism

| ID | Requirement |
| --- | --- |
| NFR-001 | Stable-key generation shall produce identical output for identical logical input across repeated executions. |
| NFR-002 | Fingerprint generation shall produce identical output for identical logical input across repeated executions. |
| NFR-003 | Serialization tests shall not depend on dictionary insertion order, runtime enum ordinals, machine paths, or database IDs. |
| NFR-004 | Repository-relative path normalization shall produce deterministic identities across different developer machine root paths. |

### 5.2 Extensibility

| ID | Requirement |
| --- | --- |
| NFR-005 | The domain model shall allow future extractor slices to add metadata without redesigning core graph primitives. |
| NFR-006 | The model shall support all work-package sequence domains without forcing per-feature persistence structures at the domain contract layer. |
| NFR-007 | The controlled value sets shall be explicit enough for current requirements while allowing safe future extension through ordinary code changes. |

### 5.3 Evidence-First Integrity

| ID | Requirement |
| --- | --- |
| NFR-008 | Persistable fact contracts shall make evidence linkage explicit. |
| NFR-009 | A fact that lacks direct evidence shall still classify its knowledge source explicitly as derived, inferred, unknown, or human-confirmed as appropriate. |
| NFR-010 | Unknown data shall be queryable and serializable rather than hidden in null fields. |

### 5.4 Onion Architecture

| ID | Requirement |
| --- | --- |
| NFR-011 | `Archon.Domain` shall not reference application, API module, infrastructure, extractor, Roslyn implementation, or host projects. |
| NFR-012 | `Archon.Application` shall not reference infrastructure or host projects. |
| NFR-013 | Domain and application contracts shall not depend on Neo4j, ASP.NET Core endpoint hosting, MCP hosting, or filesystem-specific implementation APIs. |

### 5.5 Developer Documentation

| ID | Requirement |
| --- | --- |
| NFR-014 | Any implementation plan derived from this specification shall treat `.github/instructions/documentation-pass.instructions.md` as mandatory. |
| NFR-015 | Internal and other non-public types created or updated for WP002 shall receive developer-level documentation to the same standard as public API surface. |
| NFR-016 | Public and internal domain model types, constructors, methods, properties, and enum members shall include explicit local documentation sufficient for later extractor authors to use the contracts correctly. |

## 6. Technical Requirements

### 6.1 C# and .NET Requirements

| ID | Requirement |
| --- | --- |
| TR-001 | The implementation shall target the repository's current .NET target framework. |
| TR-002 | C# files shall use block-scoped namespaces. |
| TR-003 | C# code shall use Allman braces style. |
| TR-004 | New C# files shall contain one public type per file. |
| TR-005 | Private fields shall use underscore-prefixed names where private fields are required. |
| TR-006 | The implementation shall not use top-level statements. |

### 6.2 Serialization Requirements

| ID | Requirement |
| --- | --- |
| TR-007 | Controlled values shall serialize to stable string names suitable for API, MCP, persistence, and markdown consumers. |
| TR-007A | Controlled value parsing shall round-trip stable external string values through the smart-enum/value-object implementation without relying on numeric ordinals. |
| TR-008 | Metadata shall serialize to JSON-compatible structures. |
| TR-009 | Serialization shall preserve nullability and explicit unknown-state fields. |
| TR-010 | Serialization shall be covered by tests for representative nodes, edges, evidence, findings, metrics, and generated summaries. |

### 6.3 Validation Requirements

| ID | Requirement |
| --- | --- |
| TR-011 | Stable-key value objects shall reject null, empty, or whitespace-only values. |
| TR-012 | Fingerprint value objects shall reject null, empty, or whitespace-only values. |
| TR-013 | Required model fields shall be enforced by constructors, factory methods, required members, or equivalent compile-time and runtime protections. |
| TR-014 | Unknown-state invariants shall be enforced where a model has unknown data or a knowledge kind of unknown. |
| TR-015 | Snapshot-scoped records shall require snapshot identity or snapshot stable key as appropriate. |
| TR-016 | Edge records shall require source and target node stable keys. |
| TR-017 | Finding records shall require rule code and rule version. |
| TR-018 | Metric records shall require either numeric value or text value unless the metric kind explicitly represents an unknown metric state. |

### 6.4 Accumulation Requirements

| ID | Requirement |
| --- | --- |
| TR-019 | Accumulation APIs shall expose clear add or merge operations for repositories, solutions, nodes, edges, evidence, findings, metrics, generated summaries, warnings, and errors. |
| TR-020 | Accumulation APIs shall define behavior for duplicate stable keys. |
| TR-021 | Accumulation APIs shall avoid hidden I/O, persistence, or Roslyn dependencies. |
| TR-022 | Accumulation APIs shall be unit-testable without starting hosts, Neo4j, Roslyn workspaces, or Aspire. |

## 7. Data and Contract Model

### 7.1 Repository

A repository contract represents a source repository independently of any one extraction snapshot. Its stable key identifies the logical repository, and metadata may hold extraction-specific details that are not normalized fields.

### 7.2 Solution

A solution contract represents a solution file submitted for extraction. It is associated with a repository and uses a repository-relative path for deterministic identity.

### 7.3 Snapshot

A snapshot contract represents one extraction run. It scopes all graph facts, evidence, findings, metrics, and summaries produced by that run.

### 7.4 Architecture Node

An architecture node contract represents an extracted architecture concept such as a project, type, endpoint, UI route, DbContext, external service, configuration key, Dockerfile, SQL script, or generated artifact.

### 7.5 Architecture Edge

An architecture edge contract represents a relationship between two architecture nodes, such as containment, reference, invocation, inheritance, data access, UI navigation, API call, package usage, or dependency.

### 7.6 Evidence

An evidence contract represents the explanation source for a graph fact, finding, or metric. Evidence can come from code, project files, configuration, generated artifacts, diagnostics, inference, or manual annotation.

### 7.7 Rule

A rule contract represents a versioned catalog entry. WP002 defines the shared model shape; later work packages implement disk-backed rule loading, rule evaluation, and persistence.

### 7.8 Finding

A finding contract represents rule evaluation output or another architecture concern associated with a snapshot. Findings preserve rule code, rule version, severity, status, confidence, and evidence linkage.

### 7.9 Metric

A metric contract represents persisted quantitative or textual architecture data scoped to a snapshot, graph, project, node, edge, modernization concern, or equivalent domain.

### 7.10 Generated Summary

A generated summary contract represents generated architecture narrative or exported summary content associated with a snapshot or target stable key. Later markdown and summarization packages populate this contract.

## 8. Testing Requirements

### 8.1 Unit Test Coverage

| ID | Requirement |
| --- | --- |
| TEST-001 | Tests shall verify every required node kind exists. |
| TEST-002 | Tests shall verify every required edge kind exists. |
| TEST-003 | Tests shall verify every required evidence kind exists. |
| TEST-004 | Tests shall verify every required rule category exists. |
| TEST-005 | Tests shall verify every required finding severity exists. |
| TEST-006 | Tests shall verify every required finding status exists. |
| TEST-007 | Tests shall verify every required knowledge kind exists. |
| TEST-008 | Tests shall verify every required stable-key prefix is generated by the shared component. |
| TEST-009 | Tests shall verify stable-key generation is deterministic. |
| TEST-010 | Tests shall verify stable-key generation normalizes repository-relative paths. |
| TEST-011 | Tests shall verify fingerprint generation is deterministic for nodes. |
| TEST-012 | Tests shall verify fingerprint generation is deterministic for edges. |
| TEST-013 | Tests shall verify fingerprint generation is deterministic for evidence. |
| TEST-014 | Tests shall verify fingerprint generation is deterministic for findings. |
| TEST-015 | Tests shall verify fingerprint generation is deterministic for metrics. |
| TEST-016 | Tests shall verify fingerprint generation changes when diff-relevant content changes. |
| TEST-017 | Tests shall verify enum or controlled-value serialization uses stable string names. |
| TEST-018 | Tests shall verify metadata serialization is deterministic. |
| TEST-019 | Tests shall verify unknown-state invariants cannot be bypassed for nodes. |
| TEST-020 | Tests shall verify unknown-state invariants cannot be bypassed for edges. |
| TEST-021 | Tests shall verify unknown-state invariants cannot be bypassed for evidence. |
| TEST-022 | Tests shall verify unknown-state invariants cannot be bypassed for findings. |
| TEST-023 | Tests shall verify extraction accumulation accepts contributions for all snapshot sections. |
| TEST-024 | Tests shall verify extraction accumulation preserves warnings and errors. |
| TEST-025 | Tests shall verify duplicate stable-key behavior in accumulation is deterministic and documented by tests. |

### 8.2 Test Placement

| Test Area | Project |
| --- | --- |
| Domain value objects, controlled values, stable keys, fingerprints, unknowns | `test/Archon.Domain.Tests` |
| Application snapshot contracts and accumulation behavior | `test/Archon.Application.Tests` |
| Cross-project architecture-boundary checks if needed | Existing cross-cutting test project established in WP001 |

### 8.3 Validation Commands

The implementation plan for WP002 shall include validation through targeted test execution for the changed projects and a solution build. The full test suite is not required for this work package unless the implementation plan or repository guidance is later changed.

## 9. Documentation Requirements

| ID | Requirement |
| --- | --- |
| DOC-001 | WP002 implementation documentation shall describe stable-key generation rules and required prefixes. |
| DOC-002 | WP002 implementation documentation shall describe fingerprint inputs and deterministic behavior. |
| DOC-003 | WP002 implementation documentation shall describe explicit unknown and confidence semantics. |
| DOC-004 | WP002 implementation documentation shall describe extraction accumulation responsibilities and duplicate handling. |
| DOC-005 | Documentation shall not present Neo4j persistence, Roslyn extraction, API extraction orchestration, MCP tools, markdown export, or UI behavior as complete in WP002. |
| DOC-006 | Documentation shall reference this specification as the source for WP002 requirements. |

## 10. Out of Scope

WP002 shall not implement:

- Neo4j connection configuration, constraints, indexes, or persistence behavior.
- API-triggered extraction request handling or orchestration.
- Roslyn workspace loading, compilation creation, syntax extraction, or semantic extraction.
- Project, package, ASP.NET, UI, data-access, configuration, integration, or hotlist extractor behavior.
- Disk-backed rule loading from `./rules`.
- Rule evaluation.
- API query endpoints.
- MCP tools, MCP resources, or MCP prompts.
- Markdown export generation.
- Discovery UI pages, components, assets, or user-facing flows.

## 11. Assumptions

- WP001 has already created the solution, production projects, test projects, and architecture-boundary skeletons needed by WP002.
- The source brief remains authoritative for graph concepts, stable-key prefixes, evidence-first behavior, unknowns, and Neo4j as the eventual persistence system of record.
- `spec-template_v1.1.md` was requested by the prompt but was not found in the workspace, so this specification follows the established WP001 specification structure.
- Controlled value sets will use a smart-enum/value-object style for stable external string values and future extensibility rather than ordinary numeric C# enums.

## 12. Risks and Technical Challenges

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Overly narrow domain contracts | Later extractors require redesign. | Include the complete node, edge, evidence, finding, metric, and summary model from the source brief. |
| Divergent stable-key generation | Snapshot diff and graph linking become unreliable. | Centralize stable-key generation and test every required prefix. |
| Non-deterministic metadata serialization | Fingerprints change between executions. | Canonicalize metadata before fingerprinting and test deterministic output. |
| Unknowns represented by null omission | API and MCP consumers receive incomplete or misleading facts. | Enforce explicit unknown state and unknown reason invariants. |
| Numeric enum drift | API, MCP, persistence, and markdown outputs become unstable. | Use smart-enum/value-object controlled values, serialize stable strings, and test round-trip serialization. |
| Domain layer dependency leakage | Onion Architecture is weakened. | Keep domain and application contracts free of infrastructure and host dependencies. |

## 13. Acceptance Criteria

WP002 is complete when:

1. Every required node kind from `docs/foundation/archon_full_concept_brief.md` sections 12.3 and E.4.2 exists in code.
2. Every required edge kind from `docs/foundation/archon_full_concept_brief.md` sections 12.4 and E.4.3 exists in code.
3. Every required evidence kind, rule category, finding severity, finding status, knowledge kind, metric scope, and summary kind needed by WP002 exists in code.
4. Snapshot-scoped models exist for repositories, solutions, snapshots, nodes, edges, evidence, rules, findings, metrics, and generated summaries.
5. Every required stable-key prefix from `docs/foundation/archon_full_concept_brief.md` Appendix E section E.5.4 is implemented and tested.
6. Fingerprint generation exists and is tested for nodes, edges, evidence, findings, metrics, and generated summaries.
7. Unknown-state and confidence requirements cannot be bypassed for persisted fact contracts.
8. Extraction accumulation contracts allow all future extractor slices to contribute nodes, edges, evidence, findings, metrics, generated summaries, warnings, and errors into one snapshot.
9. Tests cover stable-key determinism, fingerprint determinism, smart-enum/value-object controlled-value string serialization, metadata handling, unknown invariants, and extraction accumulation.
10. Documentation explains stable keys, fingerprints, unknowns, confidence, and accumulation behavior.
11. The solution builds after implementation.
12. Targeted tests for changed domain and application projects pass after implementation.

## 14. Traceability Matrix

| Source Requirement | Specification Coverage |
| --- | --- |
| WP002 objective | Sections 1, 3, 4, 13 |
| WP002 required implementation: domain value objects and controlled value sets | Sections 4.1, 4.2, 4.3, 5, 6 |
| WP002 required implementation: snapshot-scoped models | Sections 4.4, 7 |
| WP002 required implementation: stable-key generation | Section 4.6 |
| WP002 required implementation: fingerprint generation | Section 4.7 |
| WP002 required implementation: unknown-state and confidence | Sections 4.5, 5.3 |
| WP002 required implementation: extraction accumulation contracts | Sections 4.9, 6.4 |
| WP002 completion criteria: required node and edge kinds | Sections 4.2, 4.3, 8, 13 |
| WP002 completion criteria: required stable-key prefixes | Sections 4.6, 8, 13 |
| WP002 completion criteria: unknowns and confidence cannot be bypassed | Sections 4.5, 6.3, 8, 13 |
| WP002 completion criteria: tests | Section 8 |
| Source brief section 7 | Sections 1, 2, 3 |
| Source brief section 12 | Sections 4, 7 |
| Source brief section 13 | Section 4.6 |
| Source brief sections 32 and 33 | Sections 4.5, 4.8, 5.3 |
| Source brief Appendix E.2-E.5.5 | Sections 4 through 8 |

## 15. Change Log

| Date | Change |
| --- | --- |
| 2026-05-20 | Initial WP002 specification created as a single markdown document under `docs/002-Architecture-Graph-Domain-Model/`. |
| 2026-05-20 | Confirmed smart-enum/value-object style for controlled value sets to preserve stable external string values and future extensibility. |

End of File.
