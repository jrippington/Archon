# WP003 Specification - Neo4j Persistence Foundation

## Document Control

| Field | Value |
| --- | --- |
| Work Package | WP003 - Neo4j Persistence Foundation |
| Output Path | `docs/003-Neo4j-Persistence-Foundation/spec-wp003-neo4j-persistence-foundation.md` |
| Source Work Package | `docs/foundation/work-packages.md` WP003 |
| Source Brief | `docs/foundation/archon_full_concept_brief.md` |
| Specification Basis | `spec-template_v1.1.md` requested; template file was not present in the workspace at creation time, so this document follows the repository specification structure and documented work-package rules. |
| Status | Draft |
| Audience | Product owner, architect, implementation team, test engineer |

## 1. Overview

### 1.1 Purpose

This specification defines the requirements for WP003, the Archon work package that establishes Neo4j as the persistence foundation for the architecture semantic graph. WP003 turns the domain and application contracts from WP002 into durable graph storage behavior using Neo4j as the system of record.

The package provides graph initialization, constraints, indexes, lifecycle integration, snapshot writing, evidence deduplication, rule catalog persistence, finding persistence, metric persistence, generated summary persistence, and required supporting relationship patterns. It does not implement API extraction orchestration, Roslyn extraction, query APIs, MCP tools, markdown export, or Discovery UI behavior.

### 1.2 Background

Archon is a deterministic, evidence-backed architecture intelligence platform for modern and legacy .NET estates. The source brief requires Neo4j to store extracted architecture knowledge as a native graph so API and MCP consumers can traverse repositories, solutions, snapshots, nodes, relationships, evidence, findings, metrics, rules, and generated summaries.

WP003 follows WP002. It must persist the shared model without reshaping the domain contracts and without introducing database-generated identity as the logical identity of architecture facts. Stable keys and fingerprints remain the authoritative logical identities for graph records, while Neo4j internal IDs remain implementation details only.

### 1.3 High-Level Scope

WP003 covers the Neo4j persistence foundation:

- Neo4j connection configuration and health integration.
- Graph initialization, constraints, and indexes.
- Snapshot persistence for all graph primitives defined by WP002.
- Evidence deduplication within each snapshot.
- Rule catalog persistence and upsert behavior.
- Finding, metric, and generated summary persistence.
- Supporting relationship patterns among snapshots, solutions, nodes, edges, evidence, findings, and metrics.
- Graph recreation support for development and test workflows.
- Tests proving schema initialization, persistence, deduplication, indexing assumptions, and relationship creation.
- Documentation explaining the persistence model and operational expectations.

## 2. System Context

### 2.1 Product Context

Archon will accept extraction requests through API surfaces, assemble deterministic architecture snapshots from repository and solution analysis, persist those snapshots in Neo4j, and expose the resulting graph through later API and MCP work packages. WP003 provides the durable graph storage behavior required before API extraction orchestration and extractor packages can write real extraction output.

The persistence layer must support heterogeneous architecture facts without creating separate persistence models for each future extractor. Project, code, endpoint, UI, configuration, data-access, integration, rule, finding, metric, and generated-summary facts must all fit into one graph persistence foundation.

### 2.2 Source References

WP003 must align with these source materials:

- `docs/foundation/work-packages.md` WP003 objective, required implementation, and completion criteria.
- `docs/foundation/archon_full_concept_brief.md` section 8.1 for Neo4j Architecture Graph responsibilities.
- `docs/foundation/archon_full_concept_brief.md` section 11 for the Neo4j database choice and native graph reasoning.
- `docs/foundation/archon_full_concept_brief.md` section 12 for the full core data model.
- `docs/foundation/archon_full_concept_brief.md` section 13 for stable keys that do not depend on database IDs.
- `docs/foundation/archon_full_concept_brief.md` Appendix E sections E.5.1 through E.5.3 for persistence strategy, core graph elements, and supporting relationships.
- `docs/foundation/archon_full_concept_brief.md` Appendix E section E.7.1 for persistence foundation deliverables and acceptance criteria.
- `docs/foundation/archon_full_concept_brief.md` Appendix E section E.8.3 for query performance risk mitigations.
- `.github/instructions/documentation-pass.instructions.md` for mandatory developer documentation expectations in any coding implementation plan derived from this specification.

### 2.3 Users and Stakeholders

| Stakeholder | Interest |
| --- | --- |
| Product owner | Confirms that Archon has a durable graph persistence foundation aligned with the product vision. |
| Architect | Confirms graph labels, relationship patterns, constraints, indexes, stable identities, evidence linking, and Onion Architecture boundaries are coherent. |
| Developer | Implements extraction, query, MCP, markdown, diff, and rule behavior against a stable persistence adapter. |
| Test engineer | Verifies schema creation, persistence behavior, deduplication, constraints, indexes, and relationship creation. |
| Future API and MCP consumer | Depends on durable, evidence-backed, stable, queryable architecture facts. |

## 3. Component Summary

### 3.1 Neo4j Infrastructure Adapter

The Neo4j infrastructure adapter provides the outer persistence implementation for Archon's architecture graph. It translates application-layer snapshot contracts into Neo4j nodes, relationships, constraints, and indexes without leaking Neo4j-specific APIs into the domain layer.

### 3.2 Graph Initialization Component

The graph initialization component creates required constraints and indexes and verifies that a clean Neo4j database can become a valid Archon architecture graph store. It also supports explicitly authorized graph recreation for development and tests.

### 3.3 Snapshot Writer

The snapshot writer persists one complete architecture snapshot into Neo4j, including repositories, solutions, snapshot headers, architecture nodes, architecture relationships, evidence, rules, findings, metrics, generated summaries, and supporting relationships.

### 3.4 Evidence Deduplication Component

The evidence deduplication component ensures identical evidence payloads collapse to one canonical evidence node within the same snapshot. This allows a single evidence record to support multiple architecture nodes, edges, findings, or metrics.

### 3.5 Rule Catalog Persistence

Rule catalog persistence stores versioned rule definitions as global catalog nodes. It supports upsert identity by rule code and version so findings can preserve historical rule context while the authored rule files remain the source of truth for later rule-loading work.

### 3.6 Persistence Tests

Persistence tests verify that the adapter creates the graph schema, writes mixed graph content, enforces uniqueness expectations, deduplicates evidence, creates supporting relationships, and exposes stable keys and fingerprints through queryable indexed properties.

## 4. Functional Requirements

### 4.1 Neo4j Connection Configuration

| ID | Requirement |
| --- | --- |
| FR-001 | The implementation shall provide Neo4j connection configuration for URI, database name where applicable, username, password or secret source, connection timeout, retry behavior, and optional encryption mode. |
| FR-002 | Neo4j configuration shall be represented through strongly typed options or equivalent validated configuration contracts in the infrastructure layer. |
| FR-003 | Required connection settings shall be validated before persistence operations execute. |
| FR-004 | Sensitive connection values such as passwords shall not be logged or included in exception messages. |
| FR-005 | The Neo4j adapter shall use the official Neo4j .NET driver or the repository-approved Neo4j access library if a later implementation decision replaces it. |
| FR-006 | Neo4j driver lifecycle shall be managed through dependency injection so host projects can create, reuse, and dispose connections safely. |
| FR-007 | Connection configuration shall support Aspire-provided configuration values from WP001 local orchestration. |
| FR-008 | The infrastructure adapter shall not require the Aspire AppHost to be running during unit tests that use seams or mocked persistence behavior. |

### 4.2 Health Checks and Lifecycle Integration

| ID | Requirement |
| --- | --- |
| FR-009 | The implementation shall expose a Neo4j health check that verifies the configured database can accept a lightweight query. |
| FR-010 | The Neo4j health check shall be registered through infrastructure composition consumed by the appropriate host or service defaults path. |
| FR-011 | The health check shall distinguish configuration failures, authentication failures, network failures, and query execution failures where practical. |
| FR-012 | The adapter shall support graceful disposal of Neo4j driver resources. |
| FR-013 | The adapter shall provide clear startup failure behavior when required graph initialization cannot complete. |
| FR-014 | Automated tests shall not start the Aspire AppHost as a blocking process. |

### 4.3 Graph Labels and Core Node Persistence

| ID | Requirement |
| --- | --- |
| FR-015 | The graph shall persist repository records as Neo4j nodes with an `ArchonRepository` label or an equivalent documented label. |
| FR-016 | The graph shall persist solution records as Neo4j nodes with an `ArchonSolution` label or an equivalent documented label. |
| FR-017 | The graph shall persist snapshot records as Neo4j nodes with an `ArchonSnapshot` label or an equivalent documented label. |
| FR-018 | The graph shall persist architecture concept records as Neo4j nodes with a shared `ArchonNode` label and a normalized node-kind property. |
| FR-019 | The graph shall persist evidence records as Neo4j nodes with an `ArchonEvidence` label or an equivalent documented label. |
| FR-020 | The graph shall persist rule records as global catalog nodes with an `ArchonRule` label or an equivalent documented label. |
| FR-021 | The graph shall persist finding records as Neo4j nodes with an `ArchonFinding` label or an equivalent documented label. |
| FR-022 | The graph shall persist metric records as Neo4j nodes with an `ArchonMetric` label or an equivalent documented label. |
| FR-023 | The graph shall persist generated summary records as Neo4j nodes with an `ArchonGeneratedSummary` label or an equivalent documented label. |
| FR-024 | Repository and solution architecture concepts shall also be materialized in the graph in a way that supports traversal and diff behavior required by the source brief. |
| FR-025 | Neo4j internal IDs shall not be used as logical identity in application contracts or stable graph relationships. |

### 4.4 Required Node Properties

| ID | Requirement |
| --- | --- |
| FR-026 | Repository nodes shall persist stable key, name, root path, nullable remote URL, nullable default branch, and metadata JSON. |
| FR-027 | Solution nodes shall persist repository stable key or equivalent repository association, stable key, name, path, and metadata JSON. |
| FR-028 | Snapshot nodes shall persist stable key, repository association, branch name, commit SHA, started UTC, completed UTC, extraction version, status, warnings JSON, errors JSON, and metadata JSON. |
| FR-029 | Architecture nodes shall persist snapshot association, stable key, node kind, display name, qualified name, search name, language, project stable key, parent node stable key, knowledge kind, ownership, external category, confidence, unknown-state fields, primary evidence association where applicable, metadata JSON, and fingerprint. |
| FR-030 | Evidence nodes shall persist snapshot association, stable key, evidence kind, file path, start line, end line, symbol name, containing symbol, snippet hash, snippet preview, knowledge kind, confidence, unknown-state fields, metadata JSON, and fingerprint. |
| FR-031 | Rule nodes shall persist rule code, name, category, severity, default status, enabled flag, version, description, definition JSON, source URLs JSON, built-in flag, owner scope, and metadata JSON. |
| FR-032 | Finding nodes shall persist snapshot association, stable key, rule code, rule version, severity, status, title, description, knowledge kind, confidence, primary node stable key where applicable, primary evidence association where applicable, first-seen snapshot, latest-seen snapshot, suppression reason, suppressed-by value, metadata JSON, and fingerprint. |
| FR-033 | Metric nodes shall persist snapshot association, stable key, metric kind, scope kind, node stable key where applicable, edge stable key where applicable, primary evidence association where applicable, name, numeric value, text value, unit, metadata JSON, and fingerprint. |
| FR-034 | Generated summary nodes shall persist snapshot association, stable key, summary kind, target stable key where applicable, format, title, content, metadata JSON, and fingerprint. |
| FR-035 | Metadata shall be persisted in a deterministic JSON-compatible representation. |
| FR-036 | Normalized query-critical properties identified by the source brief shall remain first-class graph properties and shall not be hidden only inside metadata JSON. |

### 4.5 Architecture Relationship Persistence

| ID | Requirement |
| --- | --- |
| FR-037 | Architecture edges from the snapshot contract shall be persisted as Neo4j relationships between architecture nodes or as an equivalent relationship-node pattern if required to attach stable key, fingerprint, evidence, and metadata safely. |
| FR-038 | Persisted architecture relationships shall preserve stable key, edge kind, source node stable key, target node stable key, directness flag, knowledge kind, confidence, unknown-state fields, primary evidence association where applicable, metadata JSON, and fingerprint. |
| FR-039 | Relationship persistence shall support all edge kinds defined by WP002 without requiring a schema redesign. |
| FR-040 | Relationship persistence shall support multiple relationships between the same source and target when the stable key or edge kind differs. |
| FR-041 | Relationship persistence shall not assume all relationships are source-code method-call relationships. |
| FR-042 | Relationship persistence shall support relationships derived from project files, packages, configuration, UI artifacts, data access artifacts, generated artifacts, and inferred facts. |

### 4.6 Required Supporting Relationship Patterns

| ID | Requirement |
| --- | --- |
| FR-043 | The graph shall create a relationship from snapshot to each included solution. |
| FR-044 | The graph shall create relationships from architecture nodes to all supporting evidence records. |
| FR-045 | The graph shall create relationships from architecture relationships to all supporting evidence records through a documented relationship-node pattern when Neo4j relationship-to-node linking is not directly available. |
| FR-046 | The graph shall create relationships from metrics to all supporting evidence records. |
| FR-047 | The graph shall create relationships from findings to all supporting evidence records. |
| FR-048 | The graph shall create relationships from findings to associated architecture nodes. |
| FR-049 | The graph shall create relationships from findings to the rule version used to produce or classify the finding. |
| FR-050 | The graph shall create relationships from generated summaries to their target node, edge, snapshot, or other stable target where applicable. |
| FR-051 | Supporting relationship names shall be documented and stable enough for later query, MCP, markdown, and diff work packages to rely on them. |

### 4.7 Constraints and Uniqueness

| ID | Requirement |
| --- | --- |
| FR-052 | The graph initialization component shall create uniqueness constraints for repository stable keys. |
| FR-053 | The graph initialization component shall create uniqueness constraints for solution stable keys. |
| FR-054 | The graph initialization component shall create uniqueness constraints for snapshot stable keys. |
| FR-055 | The graph initialization component shall create snapshot-scoped uniqueness constraints or equivalent enforcement for architecture node stable keys. |
| FR-056 | The graph initialization component shall create snapshot-scoped uniqueness constraints or equivalent enforcement for architecture relationship stable keys when relationship modeling allows it. |
| FR-057 | The graph initialization component shall create snapshot-scoped uniqueness constraints or equivalent enforcement for evidence stable keys. |
| FR-058 | The graph initialization component shall create uniqueness constraints for rule code plus version. |
| FR-059 | The graph initialization component shall create snapshot-scoped uniqueness constraints or equivalent enforcement for finding stable keys. |
| FR-060 | The graph initialization component shall create snapshot-scoped uniqueness constraints or equivalent enforcement for metric stable keys. |
| FR-061 | The graph initialization component shall create snapshot-scoped uniqueness constraints or equivalent enforcement for generated summary stable keys. |
| FR-062 | Constraint creation shall be idempotent so repeated initialization does not fail when the graph already has the expected schema. |
| FR-063 | Constraint names shall be stable and documented for operational troubleshooting. |

### 4.8 Indexes and Queryability

| ID | Requirement |
| --- | --- |
| FR-064 | The graph initialization component shall create indexes that support lookup by stable key for repositories, solutions, snapshots, nodes, evidence, rules, findings, metrics, and generated summaries. |
| FR-065 | The graph initialization component shall create indexes that support snapshot-scoped lookup for architecture nodes, architecture relationships where applicable, evidence, findings, metrics, and generated summaries. |
| FR-066 | The graph initialization component shall create indexes that support lookup by node kind, edge kind where applicable, evidence kind, rule code, severity, status, confidence, and knowledge kind where practical. |
| FR-067 | The graph initialization component shall create indexes that support fingerprint lookup for snapshot nodes, architecture nodes, architecture relationships where applicable, evidence, findings, metrics, and generated summaries. |
| FR-068 | The graph initialization component shall create indexes needed for common follow-on query patterns such as project catalogues, neighborhood traversal, finding reports, evidence lookup, and snapshot diff. |
| FR-069 | Index creation shall be idempotent so repeated initialization does not fail when the graph already has the expected indexes. |
| FR-070 | Index names shall be stable and documented for operational troubleshooting. |

### 4.9 Snapshot Persistence Workflow

| ID | Requirement |
| --- | --- |
| FR-071 | The snapshot writer shall persist one complete snapshot contract in a single coordinated workflow. |
| FR-072 | The snapshot writer shall persist repository and solution records before creating snapshot-to-solution relationships. |
| FR-073 | The snapshot writer shall persist snapshot records before persisting snapshot-scoped nodes, edges, evidence, findings, metrics, and generated summaries. |
| FR-074 | The snapshot writer shall persist architecture nodes before architecture relationships that reference those nodes. |
| FR-075 | The snapshot writer shall persist evidence before creating node-to-evidence, edge-to-evidence, metric-to-evidence, or finding-to-evidence relationships. |
| FR-076 | The snapshot writer shall persist rule catalog entries before linking findings to rules. |
| FR-077 | The snapshot writer shall persist findings, metrics, and generated summaries after their referenced nodes, evidence, and rules are available. |
| FR-078 | The snapshot writer shall return a persistence result that records success, persisted counts, warnings, and errors. |
| FR-079 | The snapshot writer shall not silently drop unsupported snapshot sections. |
| FR-080 | The snapshot writer shall surface missing referenced nodes, missing referenced evidence, missing rules, duplicate stable-key conflicts, and invalid snapshot structure as explicit errors or warnings according to severity. |
| FR-081 | The snapshot writer shall be safe to run for snapshots containing mixed node kinds, mixed edge kinds, multiple evidence records, findings, metrics, and generated summaries. |

### 4.10 Transaction and Failure Behavior

| ID | Requirement |
| --- | --- |
| FR-082 | Snapshot persistence shall use Neo4j transaction boundaries that prevent partially persisted snapshots from being reported as complete. |
| FR-083 | If full atomic persistence is not practical for large snapshots, the implementation shall persist explicit status and recovery information so incomplete snapshots are not mistaken for complete snapshots. |
| FR-084 | Persistence failures shall preserve enough diagnostic information for developers and operators to identify the failed stage without exposing secrets. |
| FR-085 | Retry behavior shall be used only for transient failures where retrying is safe and cannot duplicate logical records. |
| FR-086 | Stable-key based merge behavior shall be deterministic across retries. |
| FR-087 | Completed snapshot records shall not be overwritten by a failed retry unless the retry is explicitly writing the same logical snapshot as an idempotent operation. |

### 4.11 Evidence Deduplication

| ID | Requirement |
| --- | --- |
| FR-088 | Evidence shall be deduplicated per snapshot. |
| FR-089 | Identical evidence payloads within a snapshot shall collapse to one canonical evidence node. |
| FR-090 | Evidence deduplication shall use stable key, fingerprint, or a documented canonical evidence identity derived from snapshot-scoped evidence content. |
| FR-091 | Evidence deduplication shall not merge evidence across different snapshots. |
| FR-092 | One evidence record shall be able to support multiple architecture nodes within a snapshot. |
| FR-093 | One evidence record shall be able to support multiple architecture relationships within a snapshot. |
| FR-094 | One evidence record shall be able to support multiple findings within a snapshot. |
| FR-095 | One evidence record shall be able to support multiple metrics within a snapshot. |
| FR-096 | Deduplication behavior shall be covered by tests using duplicate evidence submitted through multiple graph facts. |

### 4.12 Rule Catalog Persistence

| ID | Requirement |
| --- | --- |
| FR-097 | Rule catalog records shall be persisted as global catalog nodes rather than snapshot-scoped copies. |
| FR-098 | Rule catalog upsert identity shall be based on rule code and version. |
| FR-099 | Rule catalog persistence shall preserve enabled status, default status, severity, category, source URLs, built-in status, owner scope, definition JSON, and metadata JSON. |
| FR-100 | Rule catalog persistence shall allow multiple versions of the same rule code to coexist. |
| FR-101 | Findings shall record the exact rule code and rule version used at evaluation time. |
| FR-102 | The persistence model shall not require destructive deletion of historical rules or findings when a rule is removed or superseded later. |
| FR-103 | WP003 shall persist rules supplied by application-layer contracts but shall not implement disk-backed rule loading from `./rules`; that behavior belongs to a later rule or hotlist work package unless already assigned elsewhere. |

### 4.13 Findings Persistence

| ID | Requirement |
| --- | --- |
| FR-104 | Findings shall be persisted as first-class snapshot-scoped graph records. |
| FR-105 | Findings shall link to the rule code and version that produced or classified the finding. |
| FR-106 | Findings shall link to their primary architecture node where one is supplied. |
| FR-107 | Findings shall link to all supporting evidence records supplied by the snapshot contract. |
| FR-108 | Findings shall preserve severity, status, confidence, knowledge kind, first-seen snapshot, latest-seen snapshot, suppression fields, metadata, and fingerprint. |
| FR-109 | Finding persistence shall support later hotlist, suppression, historical comparison, query, MCP, and markdown export requirements without schema redesign. |

### 4.14 Metrics Persistence

| ID | Requirement |
| --- | --- |
| FR-110 | Metrics shall be persisted as first-class snapshot-scoped graph records. |
| FR-111 | Metrics shall support graph-scoped, snapshot-scoped, project-scoped, node-scoped, edge-scoped, and modernization-oriented scopes as defined by WP002 contracts. |
| FR-112 | Metrics shall persist numeric value, text value, unit, metadata, evidence linkage, and fingerprint where supplied. |
| FR-113 | Metrics shall link to target architecture nodes or relationships where applicable. |
| FR-114 | Metrics shall be persisted durably rather than recomputed only at query time. |
| FR-115 | Metric persistence shall support later snapshot diff behavior across metric values and fingerprints. |

### 4.15 Generated Summary Persistence

| ID | Requirement |
| --- | --- |
| FR-116 | Generated summaries shall be persisted as first-class snapshot-scoped graph records. |
| FR-117 | Generated summaries shall preserve summary kind, target stable key, format, title, content, metadata, and fingerprint. |
| FR-118 | Generated summaries shall link to the snapshot and to their target record where a target stable key is supplied and resolvable. |
| FR-119 | Generated summary persistence shall support later markdown export, report, API, MCP, and diff requirements without schema redesign. |

### 4.16 Graph Recreation Support

| ID | Requirement |
| --- | --- |
| FR-120 | The implementation shall provide an explicitly named graph recreation operation for development and test workflows. |
| FR-121 | Graph recreation may drop and recreate the Archon graph because the source brief does not require migration from older persistence designs. |
| FR-122 | Graph recreation shall be guarded so it cannot be invoked accidentally through ordinary snapshot persistence. |
| FR-123 | Graph recreation shall recreate constraints and indexes after clearing graph data. |
| FR-124 | Graph recreation shall be documented as destructive. |
| FR-125 | Graph recreation support shall not be exposed through production API endpoints in WP003. |

### 4.17 Layer Placement and Onion Boundaries

| ID | Requirement |
| --- | --- |
| FR-126 | Neo4j implementation code shall live in `Archon.Infrastructure.Neo4j`. |
| FR-127 | Domain projects shall not reference Neo4j packages, infrastructure projects, host projects, or API module projects. |
| FR-128 | Application contracts shall define ports or abstractions needed by persistence consumers without depending on Neo4j implementation details. |
| FR-129 | Host projects may compose the Neo4j infrastructure adapter through dependency injection where required for health checks or future persistence use. |
| FR-130 | WP003 shall not place domain logic in `Archon.Infrastructure.Neo4j`. |
| FR-131 | WP003 shall not implement extraction, query, MCP tool, markdown export, or UI behavior. |

## 5. Non-Functional Requirements

### 5.1 Deterministic Identity

| ID | Requirement |
| --- | --- |
| NFR-001 | Persisted graph records shall use stable keys from WP002 as logical identity. |
| NFR-002 | Persistence behavior shall be deterministic for equivalent snapshot contracts. |
| NFR-003 | Neo4j internal IDs shall not appear in application-layer stable identity, API-facing contracts, MCP-facing contracts, fingerprints, or documentation examples as logical identifiers. |
| NFR-004 | Snapshot-scoped stable keys and fingerprints shall remain queryable through indexed graph properties. |

### 5.2 Evidence-First Integrity

| ID | Requirement |
| --- | --- |
| NFR-005 | Persisted architectural statements shall preserve their evidence links unless the statement is purely derived from persisted facts and explicitly classified as such. |
| NFR-006 | Unknown-state fields shall be persisted as first-class graph properties for nodes, relationships where applicable, evidence, and findings. |
| NFR-007 | Knowledge kind and confidence shall be persisted as first-class graph properties for nodes, relationships where applicable, evidence, and findings. |
| NFR-008 | Missing evidence references shall be treated as persistence validation issues rather than silently ignored. |

### 5.3 Performance and Query Readiness

| ID | Requirement |
| --- | --- |
| NFR-009 | Constraints and indexes shall be designed for later query patterns, including stable-key lookup, snapshot-scoped lookup, graph traversal, evidence lookup, finding reports, and diff operations. |
| NFR-010 | Snapshot persistence shall avoid per-record database round trips where batching is practical and safe. |
| NFR-011 | Persistence tests shall include representative mixed snapshot sizes sufficient to catch obvious relationship creation and batching errors. |
| NFR-012 | The persistence model shall avoid schema choices that make common traversals require parsing metadata JSON for core dimensions. |

### 5.4 Reliability and Observability

| ID | Requirement |
| --- | --- |
| NFR-013 | Persistence operations shall log stage-level progress and failures through `ILogger` abstractions without logging sensitive values. |
| NFR-014 | Persistence result objects shall expose counts for repositories, solutions, snapshots, nodes, relationships, evidence, rules, findings, metrics, summaries, and supporting relationships where practical. |
| NFR-015 | Exceptions raised by persistence operations shall include actionable context such as operation name and record kind while avoiding secrets and excessive payload dumps. |
| NFR-016 | Health checks shall provide enough status detail for operational troubleshooting without exposing credentials. |

### 5.5 Extensibility

| ID | Requirement |
| --- | --- |
| NFR-017 | The graph model shall support later extraction slices by adding node kinds, edge kinds, metadata, and evidence without redesigning top-level persistence primitives. |
| NFR-018 | The persistence adapter shall not contain extractor-specific branching beyond generic handling needed for graph primitives. |
| NFR-019 | Rule, finding, metric, and generated summary persistence shall support later hotlist, markdown, MCP, query, and diff features without schema replacement. |

### 5.6 Security

| ID | Requirement |
| --- | --- |
| NFR-020 | Neo4j credentials shall be read from configuration or secret providers and shall not be hard-coded. |
| NFR-021 | Neo4j credentials shall not be written to logs, test output, exception messages, generated documentation examples, or persistence result payloads. |
| NFR-022 | Destructive graph recreation shall require an explicit method, command, option, or test-only seam and shall not be reachable accidentally from ordinary host startup. |

### 5.7 Developer Documentation

| ID | Requirement |
| --- | --- |
| NFR-023 | Any implementation plan derived from this specification shall treat `.github/instructions/documentation-pass.instructions.md` as mandatory. |
| NFR-024 | Internal and other non-public types created or updated for WP003 shall receive developer-level documentation to the same standard as public API surface. |
| NFR-025 | Public and internal persistence adapter types, constructors, methods, options, result types, and test fixtures shall include explicit local documentation sufficient for later maintainers to understand the graph persistence behavior. |

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
| TR-007 | Package references added for Neo4j persistence shall be placed in `.csproj` item groups that contain only package references. |

### 6.2 Neo4j Schema Requirements

| ID | Requirement |
| --- | --- |
| TR-008 | Schema initialization shall use idempotent Cypher such as `CREATE CONSTRAINT IF NOT EXISTS` or the Neo4j-driver-supported equivalent. |
| TR-009 | Schema initialization shall use stable constraint and index names. |
| TR-010 | Schema initialization shall be callable independently from snapshot writing. |
| TR-011 | Schema initialization shall be testable against a Neo4j seam or test container. |
| TR-012 | Schema initialization shall not require domain or application projects to reference Neo4j packages. |

### 6.3 Cypher and Data Mapping Requirements

| ID | Requirement |
| --- | --- |
| TR-013 | Cypher statements shall be parameterized. |
| TR-014 | Cypher statements shall not concatenate untrusted graph property values into executable query text. |
| TR-015 | Dynamic relationship types or labels, if used, shall be constrained to controlled values from application contracts and mapped through a safe whitelist. |
| TR-016 | Metadata JSON shall be serialized consistently before being passed to Neo4j. |
| TR-017 | Date and time values shall be persisted in an unambiguous UTC representation. |
| TR-018 | Nullable fields shall be mapped consistently so absent optional values do not corrupt fingerprints or query behavior. |
| TR-019 | Batch persistence shall preserve stable-key based merge semantics. |

### 6.4 Ports and Result Contracts

| ID | Requirement |
| --- | --- |
| TR-020 | Application-layer persistence abstractions shall define snapshot persistence without exposing Neo4j driver types. |
| TR-021 | Application-layer persistence abstractions shall define graph initialization without exposing Neo4j driver types where the abstraction is needed outside infrastructure. |
| TR-022 | Persistence result contracts shall include success state, persisted counts, warnings, and errors. |
| TR-023 | Persistence error contracts shall identify the failed graph section, stable key where available, and error category. |
| TR-024 | Persistence abstractions shall be asynchronous where I/O is performed and shall support cancellation tokens. |

### 6.5 Configuration Requirements

| ID | Requirement |
| --- | --- |
| TR-025 | Neo4j options shall support configuration binding from the repository's host configuration model. |
| TR-026 | Neo4j options validation shall fail fast for missing required URI, username, password, or database settings where applicable. |
| TR-027 | Test configuration shall avoid writing secrets or machine-local paths to repository files. |
| TR-028 | Local development configuration examples shall use placeholders for credentials. |

## 7. Data and Graph Model

### 7.1 Repository Node

A repository node represents a source repository independently of any one extraction snapshot. It is identified by its stable key and stores repository metadata that later extraction and query packages use to group solutions and snapshots.

### 7.2 Solution Node

A solution node represents a solution file associated with a repository. It is identified by a stable key derived from repository-relative solution identity and participates in snapshot-to-solution relationships.

### 7.3 Snapshot Node

A snapshot node represents one extraction run. It scopes architecture nodes, architecture relationships, evidence, findings, metrics, and generated summaries for that run and records lifecycle status, warnings, errors, and extraction metadata.

### 7.4 Architecture Node

An architecture node represents an extracted architecture concept such as a project, package, namespace, type, method, endpoint, UI route, configuration key, DbContext, database table, external service, queue, Dockerfile, SQL script, or generated artifact.

### 7.5 Architecture Relationship

An architecture relationship represents a directed architecture fact between two architecture nodes. Depending on the final Neo4j modeling choice, it may be stored directly as a typed relationship or through a relationship-node pattern when stable keys, fingerprints, metadata, and evidence links require first-class relationship records.

### 7.6 Evidence Node

An evidence node represents the explanation source for a graph fact, finding, or metric. Evidence is deduplicated per snapshot and can support multiple nodes, relationships, findings, or metrics.

### 7.7 Rule Node

A rule node represents a versioned global rule catalog entry. WP003 persists rule contracts supplied to it; later rule-loading packages remain responsible for reading authored files under `./rules` and producing rule contracts.

### 7.8 Finding Node

A finding node represents rule evaluation output or another architecture concern associated with a snapshot. Findings preserve rule version, severity, status, confidence, evidence linkage, suppression state, and fingerprint.

### 7.9 Metric Node

A metric node represents persisted quantitative or textual architecture data. Metrics can be scoped to a snapshot, graph, project, node, edge, modernization concern, or equivalent domain defined by WP002.

### 7.10 Generated Summary Node

A generated summary node represents persisted narrative or exported content associated with a snapshot or target graph element. WP003 stores the content and relationships; later markdown and summarization packages produce the content.

### 7.11 Supporting Relationships

The graph shall include stable supporting relationship patterns for snapshot-to-solution membership, node evidence, relationship evidence, finding evidence, finding-to-node association, finding-to-rule association, metric evidence, metric target association, and generated-summary target association.

## 8. Testing Requirements

### 8.1 Unit and Integration Test Coverage

| ID | Requirement |
| --- | --- |
| TEST-001 | Tests shall verify Neo4j options validation rejects missing required configuration. |
| TEST-002 | Tests shall verify Neo4j health check behavior for a successful lightweight query through a seam or test container. |
| TEST-003 | Tests shall verify graph initialization creates required repository constraints. |
| TEST-004 | Tests shall verify graph initialization creates required solution constraints. |
| TEST-005 | Tests shall verify graph initialization creates required snapshot constraints. |
| TEST-006 | Tests shall verify graph initialization creates required architecture node constraints or equivalent uniqueness enforcement. |
| TEST-007 | Tests shall verify graph initialization creates required evidence constraints or equivalent uniqueness enforcement. |
| TEST-008 | Tests shall verify graph initialization creates required rule constraints. |
| TEST-009 | Tests shall verify graph initialization creates required finding constraints or equivalent uniqueness enforcement. |
| TEST-010 | Tests shall verify graph initialization creates required metric constraints or equivalent uniqueness enforcement. |
| TEST-011 | Tests shall verify graph initialization creates required generated summary constraints or equivalent uniqueness enforcement. |
| TEST-012 | Tests shall verify stable-key indexes exist or are requested by schema initialization. |
| TEST-013 | Tests shall verify snapshot-scoped indexes exist or are requested by schema initialization. |
| TEST-014 | Tests shall verify fingerprint indexes exist or are requested by schema initialization. |
| TEST-015 | Tests shall verify one snapshot can persist mixed node kinds and mixed edge kinds. |
| TEST-016 | Tests shall verify repository, solution, and snapshot records are persisted with required properties. |
| TEST-017 | Tests shall verify architecture nodes persist required properties including knowledge kind, confidence, unknown-state fields, metadata, and fingerprint. |
| TEST-018 | Tests shall verify architecture relationships persist required properties including stable key, edge kind, confidence, metadata, evidence linkage, and fingerprint. |
| TEST-019 | Tests shall verify evidence records persist required properties including file path, line span, symbol data, snippet data, confidence, unknown-state fields, metadata, and fingerprint. |
| TEST-020 | Tests shall verify duplicate evidence within a snapshot is deduplicated. |
| TEST-021 | Tests shall verify identical evidence across different snapshots is not incorrectly merged. |
| TEST-022 | Tests shall verify one evidence record can support multiple architecture nodes. |
| TEST-023 | Tests shall verify one evidence record can support multiple architecture relationships. |
| TEST-024 | Tests shall verify one evidence record can support multiple findings. |
| TEST-025 | Tests shall verify one evidence record can support multiple metrics. |
| TEST-026 | Tests shall verify rule catalog upsert uses rule code and version. |
| TEST-027 | Tests shall verify multiple versions of the same rule code can coexist. |
| TEST-028 | Tests shall verify findings link to rule versions, primary nodes, and evidence. |
| TEST-029 | Tests shall verify metrics link to evidence and target nodes or relationships where applicable. |
| TEST-030 | Tests shall verify generated summaries link to snapshots and target records where applicable. |
| TEST-031 | Tests shall verify persistence result counts reflect persisted repositories, solutions, snapshots, nodes, relationships, evidence, rules, findings, metrics, summaries, and supporting relationships where practical. |
| TEST-032 | Tests shall verify missing referenced nodes or evidence produce explicit persistence errors or warnings rather than silent drops. |
| TEST-033 | Tests shall verify graph recreation clears data and recreates constraints and indexes when explicitly invoked. |
| TEST-034 | Tests shall verify Onion Architecture boundaries are preserved after adding Neo4j infrastructure references. |

### 8.2 Test Placement

| Test Area | Project |
| --- | --- |
| Neo4j options, health check seams, schema generation, persistence mapping, graph recreation, and adapter behavior | `test/Archon.Infrastructure.Neo4j.Tests` |
| Application persistence abstractions and result contracts if changed | `test/Archon.Application.Tests` |
| Cross-project architecture-boundary checks if needed | Existing cross-cutting test project established in WP001 |
| Host composition of Neo4j infrastructure health checks if changed | `test/ArchonApi.Tests` or host-specific tests established in WP001 |

### 8.3 Test Strategy

WP003 shall use a hybrid test strategy. Most persistence behavior shall be covered through seams, fakes, and contract tests so mapping decisions, options validation, persistence ordering, error handling, batching behavior, result counts, and graph recreation guard behavior remain fast and deterministic.

WP003 shall also include required targeted Neo4j integration tests using Testcontainers. Docker may always be assumed to be available for this work package, so these tests are not optional and shall not be skipped solely because they require Docker. The targeted Testcontainers suite shall verify that schema initialization creates the required constraints and indexes, one mixed snapshot persists successfully, evidence deduplication works against a real Neo4j graph, supporting relationships are queryable, and rule code/version upsert behavior works as specified.

Automated validation must not start the Aspire AppHost because it blocks the executing agent.

### 8.4 Validation Commands

The implementation plan for WP003 shall include validation through targeted tests for changed projects and a solution build. The full test suite is not required for this work package unless the implementation plan or repository guidance is later changed.

## 9. Documentation Requirements

| ID | Requirement |
| --- | --- |
| DOC-001 | WP003 implementation documentation shall describe the Neo4j graph labels and relationship patterns used by Archon. |
| DOC-002 | WP003 implementation documentation shall describe required constraints and indexes. |
| DOC-003 | WP003 implementation documentation shall describe snapshot persistence ordering and failure behavior. |
| DOC-004 | WP003 implementation documentation shall describe evidence deduplication semantics. |
| DOC-005 | WP003 implementation documentation shall describe rule catalog persistence and rule code/version identity. |
| DOC-006 | WP003 implementation documentation shall describe graph recreation behavior and clearly mark it destructive. |
| DOC-007 | WP003 implementation documentation shall describe local development configuration for Neo4j without hard-coded secrets. |
| DOC-008 | Documentation shall not present API extraction orchestration, Roslyn extraction, query APIs, MCP tools, markdown export, or UI behavior as complete in WP003. |
| DOC-009 | Documentation shall reference this specification as the source for WP003 requirements. |

## 10. Out of Scope

WP003 shall not implement:

- API-triggered extraction request handling or orchestration.
- Roslyn workspace loading, compilation creation, syntax extraction, or semantic extraction.
- Project, package, ASP.NET, UI, data-access, configuration, integration, or hotlist extractor behavior.
- Disk-backed rule loading from `./rules`.
- Rule evaluation.
- API query endpoints.
- MCP tools, MCP resources, or MCP prompts.
- Markdown export generation.
- Discovery UI pages, components, assets, or user-facing flows.
- Data migration from any older graph schema.
- Production API endpoints for destructive graph recreation.

## 11. Assumptions

- WP001 has already created the solution, production projects, test projects, Aspire composition, service defaults, host skeletons, and architecture-boundary skeletons needed by WP003.
- WP002 has already created or will create the domain and application contracts for repositories, solutions, snapshots, nodes, edges, evidence, rules, findings, metrics, generated summaries, stable keys, fingerprints, confidence, unknowns, and extraction accumulation.
- The source brief remains authoritative for Neo4j as the system of record, stable keys as logical identity, evidence-first behavior, explicit unknowns, and graph recreation without migration.
- `spec-template_v1.1.md` was requested by the prompt but was not found in the workspace, so this specification follows the established WP001 and WP002 specification structure.
- Docker may always be assumed to be available for WP003 validation.
- WP003 shall use seams and fakes for fast coverage of mapping, validation, ordering, error handling, batching, persistence result counts, and recreation guard behavior.
- WP003 shall use required targeted Neo4j Testcontainers integration tests for real schema initialization, mixed snapshot persistence, evidence deduplication, supporting relationship queryability, and rule code/version upsert behavior.

## 12. Risks and Technical Challenges

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Incorrect graph identity strategy | Snapshot diff and graph linking become unreliable. | Use WP002 stable keys and enforce uniqueness through constraints or equivalent validation. |
| Weak evidence modeling | API and MCP consumers cannot explain graph facts. | Persist evidence as first-class nodes and create explicit supporting relationships. |
| Overuse of metadata JSON | Common query patterns become slow or brittle. | Keep source-brief normalized properties as first-class graph properties and index query-critical fields. |
| Relationship evidence modeling complexity | Neo4j relationships cannot directly own relationships to evidence nodes. | Use a documented relationship-node pattern or equivalent mapping for relationship evidence when needed. |
| Non-idempotent schema initialization | Repeated startup or test setup becomes fragile. | Use idempotent schema creation and stable constraint/index names. |
| Partial snapshot persistence | Consumers may read incomplete snapshots as valid. | Use transactions or explicit incomplete status and recovery/error reporting. |
| Large snapshot performance | Persistence may become too slow for real repositories. | Batch writes, index stable keys, and avoid per-record round trips where practical. |
| Destructive graph recreation misuse | Production data could be lost accidentally. | Isolate recreation behind explicit destructive APIs or test-only seams and document it clearly. |
| Secret leakage | Credentials could appear in logs or errors. | Validate and log configuration safely without outputting sensitive values. |
| Onion Architecture leakage | Domain or application layers could become coupled to Neo4j. | Keep Neo4j packages and driver types inside infrastructure; expose application ports only. |

## 13. Acceptance Criteria

WP003 is complete when:

1. Neo4j connection configuration, options validation, health checks, and lifecycle integration exist in the infrastructure layer.
2. Graph initialization creates required constraints and indexes idempotently for repositories, solutions, snapshots, architecture nodes, architecture relationships where applicable, evidence, rules, findings, metrics, and generated summaries.
3. A Neo4j graph can be created from scratch and used as the sole persistence model for extraction output.
4. One snapshot can persist mixed node and edge kinds, multiple evidence records, findings, metrics, rules, and generated summaries.
5. Evidence is deduplicated per snapshot.
6. One evidence record can support multiple nodes, edges, findings, or metrics within a snapshot.
7. Snapshot-scoped stable keys and fingerprints are queryable through indexes or equivalent indexed graph properties.
8. Rule catalog records persist by rule code and version and can support findings that reference the exact evaluated rule version.
9. Supporting relationships exist for snapshot-to-solution, node-to-evidence, edge-to-evidence, metric-to-evidence, finding-to-evidence, finding-to-node, and finding-to-rule patterns.
10. Graph recreation support exists, is explicitly destructive, and recreates required constraints and indexes.
11. Tests prove constraints, indexes, persistence, deduplication, relationship creation, rule upsert behavior, persistence results, failure handling, and Onion Architecture boundaries.
12. Documentation explains the graph model, initialization, indexes, constraints, evidence deduplication, persistence ordering, graph recreation, and local configuration expectations.
13. The solution builds after implementation.
14. Targeted tests for changed Neo4j infrastructure, application contracts, and host composition projects pass after implementation.

## 14. Traceability Matrix

| Source Requirement | Specification Coverage |
| --- | --- |
| WP003 objective | Sections 1, 3, 4, 13 |
| WP003 required implementation: Neo4j connection configuration, health checks, and lifecycle integration | Sections 4.1, 4.2, 5.4, 6.5 |
| WP003 required implementation: constraints and indexes | Sections 4.7, 4.8, 6.2, 8, 13 |
| WP003 required implementation: snapshot persistence for all graph primitives | Sections 4.3 through 4.6, 4.9 through 4.15, 7 |
| WP003 required implementation: evidence deduplication per snapshot | Sections 4.11, 8, 13 |
| WP003 required implementation: supporting relationships | Sections 4.6, 7.11, 8, 13 |
| WP003 required implementation: graph recreation support | Sections 4.16, 5.6, 8, 9, 13 |
| WP003 required implementation: tests against Neo4j seams or test containers | Section 8 |
| WP003 completion criteria: graph can be created from scratch | Sections 4.7, 4.8, 4.16, 13 |
| WP003 completion criteria: one snapshot can persist mixed content | Sections 4.9, 8, 13 |
| WP003 completion criteria: evidence supports multiple records | Sections 4.11, 8, 13 |
| WP003 completion criteria: stable keys and fingerprints queryable through indexes | Sections 4.8, 5.1, 8, 13 |
| WP003 completion criteria: tests prove constraints, indexes, persistence, deduplication, and relationship creation | Section 8 |
| Source brief section 8.1 | Sections 1, 2, 3 |
| Source brief section 11 | Sections 1, 2, 4, 5 |
| Source brief section 12 | Sections 4, 7 |
| Source brief section 13 | Sections 4.7, 4.8, 5.1 |
| Source brief Appendix E.5.1-E.5.3 | Sections 4, 7, 13 |
| Source brief Appendix E.7.1 | Sections 8, 13 |
| Source brief Appendix E.8.3 | Sections 4.8, 5.3, 12 |

## 15. Change Log

| Date | Change |
| --- | --- |
| 2026-05-20 | Initial WP003 specification created as a single markdown document under `docs/003-Neo4j-Persistence-Foundation/`. |
| 2026-05-20 | Recorded the agreed WP003 testing answer: use a hybrid strategy with required targeted Neo4j Testcontainers integration tests, assuming Docker is always available. |

End of File.
