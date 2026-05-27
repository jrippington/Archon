# WP017 Specification - Neo4j Persistence Performance Optimization

## Document Control

| Field | Value |
| --- | --- |
| Work Package | WP017 - Neo4j Persistence Performance Optimization |
| Output Path | `docs/017-Neo4j-Persistence-Performance/spec-wp017-neo4j-persistence-performance.md` |
| Source Work Package | New work package created after WP016 persistence diagnostics and follow-on performance analysis |
| Source Brief | User-provided completed extraction status for run `3a3b116f-eb69-4a80-bf5c-06647da54a94`, workspace inspection of `Neo4jArchitectureSnapshotWriter`, and current wiki guidance in `wiki/neo4j-persistence-foundation.md` and `wiki/graph-domain-model.md` |
| Specification Basis | `spec-template_v1.1.md` requested; template file was not present in the workspace at creation time, so this document follows the repository specification structure and documented work-package rules. |
| Status | Draft |
| Audience | Product owner, architect, implementation team, test engineer |

## Change Log

| Date | Change |
| --- | --- |
| 2026-05-27 | Initial draft created to specify Neo4j snapshot persistence throughput improvements using the observed WP016 diagnostic output and inspected persistence code. |

## 1. Overview

### 1.1 Purpose

This specification defines the requirements for WP017, the Archon work package that improves Neo4j snapshot persistence throughput for completed extraction runs. The work package focuses on reducing the time spent writing already-assembled architecture snapshots to Neo4j while preserving Archon's stable-key graph model, evidence-first modeling, Neo4j schema semantics, and existing extraction API behavior.

WP017 is a performance and implementation-shape work package. It does not change what Archon extracts, what facts appear in a completed snapshot, how stable identities are generated, or how API and MCP consumers interpret persisted graph content.

### 1.2 Background

A completed extraction run for repository root `D:\Dev\Mandala.OLD\` and solution `Mandala.slnx` completed the extraction pipeline quickly but spent most of the end-to-end runtime in persistence. The run reported approximately 12.2 seconds for the extraction pipeline and approximately 171.4 seconds for persistence, with persistence representing roughly 93 percent of total elapsed time.

The WP016 diagnostic breakdown showed that the largest persistence costs were concentrated in metric writes and relationship writes. The same run reported approximately 45,169 metrics, 96,347 persisted support relationships, 6,366 nodes, 6,389 canonical evidence records, 154,274 persistence operations, and one persistence batch. Inspection of the Neo4j writer showed that the current implementation uses one Neo4j write transaction but executes many individual sequential Cypher statements inside that transaction. Each node, metric, evidence record, and support relationship is written by a separate `RunAsync` call, and each cursor is consumed before the next statement starts.

This write shape is the primary performance concern. The observed timings are consistent with high statement count and sequential Bolt/Cypher execution overhead rather than with extraction work, relational ORM behavior, or metadata being stored as separate key/value rows.

### 1.3 High-Level Scope

WP017 covers throughput improvements for Neo4j snapshot persistence:

- Batched Cypher write paths for high-volume snapshot sections.
- `UNWIND`-based node, metric, evidence, and support-relationship persistence where appropriate.
- Preservation of the current stable-key `MERGE` semantics and snapshot-scoped identity model.
- Preservation of the current single-transaction completion semantics unless a later explicitly accepted design decision introduces safe transaction chunking.
- Improved operation and batch count diagnostics that reflect the optimized write shape.
- More granular diagnostics for support relationship families where useful for future performance analysis.
- Tests proving persisted graph content remains equivalent to the current writer behavior.
- Tests proving reduced Cypher execution counts or equivalent observable batching behavior.
- Wiki updates explaining the optimized Neo4j persistence model and how to interpret post-optimization diagnostics.

WP017 excludes extractor changes, domain graph model redesign, query API feature work, MCP feature work, UI work, Neo4j database replacement, graph recreation workflow changes, and any optimization that exposes Neo4j internal IDs through application or API contracts.

## 2. System Context

### 2.1 Product Context

Archon accepts extraction requests, builds deterministic architecture snapshots from repository and solution analysis, persists those snapshots into Neo4j, and exposes persisted architecture knowledge through application, API, and MCP surfaces. As extraction coverage grows, snapshot persistence must handle large numbers of architecture nodes, metric nodes, evidence records, relationship-node records, and support relationships without turning persistence into the dominant cost of every completed run.

The current graph model intentionally uses stable logical identities rather than database-local identifiers. Neo4j is the durable graph store, but repository, solution, snapshot, architecture node, evidence, metric, finding, rule, and generated-summary identities remain stable-key based. WP017 must optimize persistence without weakening that product contract.

### 2.2 Source References

WP017 must align with these repository materials:

- `wiki/neo4j-persistence-foundation.md` for current Neo4j persistence responsibilities, write order, stable-key identity rules, evidence deduplication, relationship-node pattern, diagnostics, and troubleshooting guidance.
- `wiki/graph-domain-model.md` for stable keys, fingerprints, metadata, graph facts, evidence-first modeling, metrics, and snapshot assembly semantics.
- `docs/003-Neo4j-Persistence-Foundation/spec-wp003-neo4j-persistence-foundation.md` for the Neo4j persistence foundation requirements.
- `docs/016-Persistence-Diagnostics/spec-wp016-persistence-diagnostics.md` for the persistence diagnostic model and status response expectations.
- `src/Archon.Infrastructure.Neo4j/Persistence/Neo4jArchitectureSnapshotWriter.cs` for the current writer implementation that WP017 optimizes.
- `src/Archon.Infrastructure.Neo4j/Persistence/Neo4jSnapshotPersistenceMapper.cs` for the current mapping of domain graph facts into Neo4j parameter dictionaries.
- `src/Archon.Infrastructure.Neo4j/Persistence/Neo4jPersistenceDiagnosticCollector.cs` for current persistence timing and count capture.
- `src/Archon.Infrastructure.Neo4j/Schema/Neo4jSchemaStatementCatalog.cs` for current Neo4j constraints and indexes.
- `.github/instructions/documentation-pass.instructions.md` for mandatory developer documentation expectations in any implementation plan derived from this specification.

### 2.3 Users and Stakeholders

| Stakeholder | Interest |
| --- | --- |
| Product owner | Confirms completed extraction runs persist at practical speed for medium and large .NET repositories. |
| Architect | Confirms optimization preserves stable-key graph semantics, evidence-first modeling, transaction behavior, and Onion Architecture boundaries. |
| Developer | Implements batched Neo4j write paths without changing snapshot semantics or leaking Neo4j driver details inward. |
| Test engineer | Verifies graph equivalence, batching behavior, diagnostics, failure behavior, and performance acceptance criteria. |
| API consumer | Continues receiving completed extraction status and snapshot identities with no contract-breaking behavior change. |
| Operator | Uses persistence diagnostics to compare pre- and post-optimization runs and identify any remaining bottlenecks. |

## 3. Component Summary

### 3.1 Neo4j Snapshot Writer Optimization

The Neo4j snapshot writer remains the infrastructure adapter responsible for persisting one assembled `ExtractedArchitectureSnapshot`. WP017 changes the writer's high-volume execution strategy from per-record Cypher execution to set-oriented batched execution while keeping the same application-facing `IArchitectureSnapshotWriter` contract.

### 3.2 Batched Parameter Materialization

The optimized writer prepares collections of parameter dictionaries or equivalent immutable parameter payloads for homogeneous record groups. These payloads are passed to static, parameterized Cypher statements using list parameters rather than one statement per record.

### 3.3 Batched Node and Record Upserts

Repositories, solutions, architecture nodes, metrics, evidence records, and any other supported snapshot records are upserted through `UNWIND`-based Cypher where the expected row volume justifies batching. The snapshot header may remain a single statement because only one header exists per snapshot.

### 3.4 Batched Support Relationship Creation

Support relationships are created through grouped `UNWIND` statements that match source and target records by stable-key properties and then `MERGE` the required Neo4j relationship. The high-volume relationship families include node-to-evidence support relationships, metric-to-evidence support relationships, and metric-to-node target relationships.

### 3.5 Persistence Diagnostics Enhancement

Diagnostics continue to report top-level and nested persistence timings. WP017 refines operation and batch counts so they reflect actual Cypher statement execution after batching. It may also split relationship write timings into clearer relationship-family timings so future slow runs can distinguish metric support links from node evidence links and other support relationships.

### 3.6 Persistence Equivalence Tests

Tests verify that the optimized writer produces the same durable graph facts and support relationships as the current writer for representative snapshots. Tests also verify batching behavior through driver seams, integration tests, diagnostics, or other repository-approved observable evidence.

## 4. Functional Requirements

### 4.1 Batched Persistence Strategy

| ID | Requirement |
| --- | --- |
| FR-001 | The implementation shall reduce the number of Cypher executions required to persist high-volume snapshot sections. |
| FR-002 | The implementation shall use `UNWIND`-based or equivalent set-oriented Neo4j write statements for high-volume record groups where batching is safe. |
| FR-003 | Batched writes shall preserve the existing write order dependencies between repositories, solutions, snapshot headers, nodes, metrics, evidence records, and support relationships. |
| FR-004 | The implementation shall keep snapshot persistence behind the existing application-layer `IArchitectureSnapshotWriter` port. |
| FR-005 | The implementation shall not require domain or application projects to reference Neo4j driver types. |
| FR-006 | The implementation shall keep all Cypher static and parameterized. |
| FR-007 | The implementation shall not build Cypher by concatenating untrusted labels, relationship types, property names, stable keys, metadata values, file paths, or user-supplied text. |
| FR-008 | The implementation shall retain the existing safe failure behavior that returns application-owned persistence errors instead of raw Neo4j infrastructure details. |

### 4.2 Transaction Semantics

| ID | Requirement |
| --- | --- |
| FR-009 | The optimized writer shall preserve the current single coordinated persistence workflow for one snapshot. |
| FR-010 | The optimized writer shall continue to report a completed persistence result only after the snapshot persistence workflow succeeds. |
| FR-011 | The optimized writer shall not return a successful result for partially persisted snapshot data. |
| FR-012 | Batched writes shall initially execute inside one Neo4j write transaction unless implementation evidence proves that transaction size makes this unworkable. |
| FR-013 | If a later design introduces transaction chunking, the design shall include an explicit finalization or recovery model that prevents partially persisted snapshots from being reported as completed. |
| FR-014 | Any transaction chunking decision shall be documented in this work package or a follow-on work package before implementation. |

### 4.3 Stable-Key Identity Preservation

| ID | Requirement |
| --- | --- |
| FR-015 | Repository records shall continue to merge by repository stable key. |
| FR-016 | Solution records shall continue to merge by solution stable key. |
| FR-017 | Snapshot records shall continue to merge by snapshot stable key. |
| FR-018 | Architecture nodes shall continue to merge by snapshot stable key plus architecture node stable key. |
| FR-019 | Evidence records shall continue to merge by snapshot stable key plus evidence stable key after canonical evidence deduplication. |
| FR-020 | Metric records shall continue to merge by snapshot stable key plus metric stable key. |
| FR-021 | Support relationships shall continue to match source and target records using stable-key properties rather than Neo4j internal IDs exposed through application contracts. |
| FR-022 | The optimized writer shall not expose Neo4j internal IDs in persistence results, API responses, tests, documentation examples, or domain/application models. |
| FR-023 | The optimized writer shall preserve deterministic metadata JSON and fingerprint values generated by the existing graph contracts and mapper behavior. |

### 4.4 Batched Repository, Solution, and Snapshot Writes

| ID | Requirement |
| --- | --- |
| FR-024 | Repository persistence may be converted to a batched write even when repository count is usually low. |
| FR-025 | Solution persistence may be converted to a batched write even when solution count is usually low. |
| FR-026 | Snapshot header persistence may remain a single-record statement because each extraction run has one snapshot header. |
| FR-027 | Repository, solution, and snapshot writes shall continue to set the same normalized properties currently persisted by the writer. |
| FR-028 | Repository and solution batched writes shall remain idempotent for repeated persistence attempts with the same stable keys. |

### 4.5 Batched Architecture Node Writes

| ID | Requirement |
| --- | --- |
| FR-029 | The implementation shall provide a batched architecture node write path for `ArchonNode` records. |
| FR-030 | Batched architecture node writes shall set the same normalized properties currently set by `NodeMergeCypher`. |
| FR-031 | Batched architecture node writes shall preserve snapshot stable key, stable key, node kind, display name, qualified name, search name, language, project stable key, parent node stable key, knowledge kind, ownership, external category, confidence, unknown-state fields, primary evidence stable key, metadata JSON, and fingerprint. |
| FR-032 | Batched architecture node writes shall handle nullable properties consistently with the current per-record writer. |
| FR-033 | Batched architecture node writes shall use the existing snapshot-scoped uniqueness constraint for efficient merge identity. |

### 4.6 Batched Metric Writes

| ID | Requirement |
| --- | --- |
| FR-034 | The implementation shall provide a batched metric write path for `ArchonMetric` records. |
| FR-035 | Batched metric writes shall set the same normalized properties currently set by `MetricMergeCypher`. |
| FR-036 | Batched metric writes shall preserve snapshot stable key, stable key, metric kind, scope kind, node stable key, edge stable key, primary evidence stable key, name, numeric value, text value, unit, confidence, unknown-state fields, metadata JSON, and fingerprint. |
| FR-037 | Batched metric writes shall handle numeric-only, text-only, and mixed metric value shapes consistently with the current writer. |
| FR-038 | Batched metric writes shall use the existing snapshot-scoped uniqueness constraint for efficient merge identity. |
| FR-039 | Metric write batching shall be treated as a primary optimization target because the observed run spent approximately 49.4 seconds writing 45,169 metric records. |

### 4.7 Batched Evidence Writes

| ID | Requirement |
| --- | --- |
| FR-040 | The implementation shall preserve current canonical evidence deduplication before evidence records are written. |
| FR-041 | The implementation shall provide a batched evidence write path for canonical `ArchonEvidence` records. |
| FR-042 | Batched evidence writes shall set the same normalized properties currently set by `EvidenceMergeCypher`. |
| FR-043 | Batched evidence writes shall preserve snapshot stable key, stable key, evidence kind, file path, start line, end line, symbol name, containing symbol, snippet hash, snippet preview, knowledge kind, confidence, unknown-state fields, metadata JSON, and fingerprint. |
| FR-044 | Batched evidence writes shall handle nullable source-location and symbol properties consistently with the current writer. |
| FR-045 | Batched evidence writes shall use the existing snapshot-scoped uniqueness constraint for efficient merge identity. |

### 4.8 Batched Support Relationship Writes

| ID | Requirement |
| --- | --- |
| FR-046 | The implementation shall provide batched support relationship creation for snapshot-to-solution relationships where practical. |
| FR-047 | The implementation shall provide batched support relationship creation for node-to-evidence `SUPPORTED_BY_EVIDENCE` relationships. |
| FR-048 | The implementation shall provide batched support relationship creation for metric-to-evidence `SUPPORTED_BY_EVIDENCE` relationships. |
| FR-049 | The implementation shall provide batched support relationship creation for metric-to-node `MEASURES_NODE` relationships. |
| FR-050 | Batched support relationship creation shall match records by stable-key properties and snapshot scope. |
| FR-051 | Batched support relationship creation shall use `MERGE` or an equivalent idempotent operation so rerunning persistence for the same snapshot does not create duplicate relationships. |
| FR-052 | Batched support relationship creation shall preserve the current canonical evidence stable-key remapping for duplicate evidence inputs. |
| FR-053 | Batched support relationship creation shall not silently create relationships when the required source or target graph record is missing. |
| FR-054 | Missing source or target matches in batched relationship creation shall fail, warn, or be validated according to the current writer's equivalent behavior and the repository's safe error handling conventions. |
| FR-055 | Relationship write batching shall be treated as the primary optimization target because the observed run spent approximately 99.7 seconds writing 96,347 support relationships. |

### 4.9 Architecture Relationship Nodes and Future Relationship Families

| ID | Requirement |
| --- | --- |
| FR-056 | WP017 shall not remove or weaken the relationship-node pattern described in the wiki. |
| FR-057 | If architecture edge persistence is present or added in the current codebase before WP017 implementation begins, architecture relationship node writes shall use the same batched-write principles where safe. |
| FR-058 | If architecture relationship node writes are optimized, they shall preserve relationship stable key, edge kind, source node stable key, target node stable key, directness, knowledge kind, confidence, unknown-state fields, primary evidence stable key, metadata JSON, and fingerprint. |
| FR-059 | If relationship endpoint links are optimized, source and target endpoint relationships shall be batched separately or in clearly measured grouped statements. |
| FR-060 | If finding, rule, generated-summary, or summary-target relationship families are present in the writer at implementation time, they shall be reviewed for the same N+1 statement pattern and optimized when row volume justifies it. |

### 4.10 Batch Size Behavior

| ID | Requirement |
| --- | --- |
| FR-061 | The implementation shall define an explicit batch size for high-volume `UNWIND` writes. |
| FR-062 | The initial batch size shall balance fewer Cypher executions against Neo4j transaction memory and parameter payload size. |
| FR-063 | Batch size should be configurable through infrastructure options if implementation evidence shows that repository size, Neo4j edition, container resources, or deployment environment requires tuning. |
| FR-064 | The default batch size shall be documented. |
| FR-065 | The writer shall handle empty batches without executing unnecessary Cypher statements. |
| FR-066 | The writer shall handle final partial batches correctly. |
| FR-067 | Batching shall not require callers to sort or pre-partition snapshot sections. |

### 4.11 Operation and Batch Count Diagnostics

| ID | Requirement |
| --- | --- |
| FR-068 | `persistenceOperationCount` shall reflect actual Neo4j statements executed or a clearly documented equivalent count after batching. |
| FR-069 | `persistenceOperationCount` shall decrease materially compared with the current per-record implementation for large snapshots. |
| FR-070 | `persistenceBatchCount` shall continue to report the number of write transactions or the clearly documented batching concept used by the writer. |
| FR-071 | If `persistenceBatchCount` continues to mean transaction count, it shall remain `1` for the default single-transaction optimized writer. |
| FR-072 | If a separate batch-count field is needed to describe Cypher list batches, that field shall be introduced through the application/API diagnostic contract in a backward-compatible way. |
| FR-073 | Diagnostics shall not count individual rows inside a batched `UNWIND` statement as separate persistence operations unless explicitly documented as a logical row-operation count. |
| FR-074 | Diagnostics shall remain safe and shall not include raw Cypher, Bolt endpoint details, credentials, connection strings, raw driver exception text, or full parameter payloads. |

### 4.12 Persistence Timing Diagnostics

| ID | Requirement |
| --- | --- |
| FR-075 | The implementation shall continue to emit persistence diagnostic timings compatible with WP016 status responses. |
| FR-076 | The implementation shall preserve top-level `Persistence` timing semantics in extraction status responses. |
| FR-077 | The implementation shall preserve `Persistence.Total` as the total nested persistence diagnostic timing. |
| FR-078 | The implementation shall preserve `Persistence.Commit` as the timing for the Neo4j write transaction wrapper unless the wiki and diagnostic documentation are updated together with a semantics change. |
| FR-079 | The implementation should split `Persistence.WriteRelationships` into more granular relationship-family timings where this can be done without excessive diagnostic noise. |
| FR-080 | Recommended relationship-family timings include `Persistence.WriteSnapshotSolutionRelationships`, `Persistence.WriteNodeEvidenceRelationships`, `Persistence.WriteMetricEvidenceRelationships`, and `Persistence.WriteMetricTargetRelationships`. |
| FR-081 | Relationship-family timing names shall remain stable once introduced. |
| FR-082 | New timings shall be nested under persistence diagnostics and shall not be added as separate top-level extraction pipeline stages. |

### 4.13 Error Handling and Validation

| ID | Requirement |
| --- | --- |
| FR-083 | Batched writes shall preserve controlled validation failures for invalid snapshots before any Neo4j transaction begins. |
| FR-084 | Batched writes shall propagate Neo4j failures through the same safe `SnapshotPersistenceResult.Failure` behavior as the current writer. |
| FR-085 | Batched writes shall surface statement failures before the writer returns success. |
| FR-086 | Batched writes shall not hide partial diagnostic timings collected before a controlled failure. |
| FR-087 | The implementation shall preserve cancellation behavior before schema initialization and before transaction execution starts. |
| FR-088 | The implementation should preserve or improve cancellation responsiveness during long parameter materialization and batch execution where practical. |
| FR-089 | Batched relationship writes shall not convert missing target matches into false successful writes unless the current model explicitly accepts optional links for that relationship family. |

### 4.14 Backward Compatibility

| ID | Requirement |
| --- | --- |
| FR-090 | Existing extraction request and status API contracts shall remain backward compatible unless a separate API contract change is explicitly specified. |
| FR-091 | Existing persisted graph records shall remain queryable after the optimized writer is introduced. |
| FR-092 | Re-running persistence for an already persisted snapshot shall remain idempotent according to existing merge semantics. |
| FR-093 | Existing tests that verify stable keys, normalized properties, evidence deduplication, and support relationships shall continue to pass after optimization. |
| FR-094 | Any new optional diagnostic fields shall be additive and shall not break clients that ignore unknown fields. |

## 5. Non-Functional Requirements

### 5.1 Performance

| ID | Requirement |
| --- | --- |
| NFR-001 | The optimized writer shall materially reduce persistence duration for large snapshots dominated by metrics and support relationships. |
| NFR-002 | For a snapshot similar to run `3a3b116f-eb69-4a80-bf5c-06647da54a94`, the target persistence duration should be less than 40 seconds in the same local environment, subject to Neo4j container and workstation variability. |
| NFR-003 | The stretch target for a similar snapshot should be less than 20 seconds in the same local environment, subject to Neo4j container and workstation variability. |
| NFR-004 | The optimized writer should reduce Cypher executions by at least one order of magnitude for large snapshots compared with the current per-record writer. |
| NFR-005 | The optimized writer should avoid creating large duplicate serialized snapshot payloads solely for persistence. |
| NFR-006 | Parameter materialization overhead should remain small compared with Neo4j write time after batching. |
| NFR-007 | The implementation shall avoid per-record logging and per-record lifecycle updates in high-volume persistence loops. |

### 5.2 Reliability

| ID | Requirement |
| --- | --- |
| NFR-008 | The optimized writer shall preserve current success/failure semantics under transient and permanent Neo4j failures. |
| NFR-009 | The optimized writer shall avoid reporting a completed run when the graph is only partially persisted. |
| NFR-010 | The optimized writer shall remain safe for repeated local development and integration test runs against the same database. |
| NFR-011 | The optimized writer shall preserve evidence deduplication and canonical evidence remapping for all supported relationship families. |

### 5.3 Architecture and Boundaries

| ID | Requirement |
| --- | --- |
| NFR-012 | The implementation shall preserve Onion Architecture dependency direction. |
| NFR-013 | Domain projects shall remain independent of Neo4j infrastructure details. |
| NFR-014 | Application contracts shall not expose Neo4j driver types. |
| NFR-015 | Host projects shall not contain graph persistence logic. |
| NFR-016 | The Neo4j infrastructure adapter shall own Cypher statements, batching mechanics, driver interaction, and safe infrastructure error translation. |

### 5.4 Security

| ID | Requirement |
| --- | --- |
| NFR-017 | Batched write diagnostics shall not expose credentials, connection strings, Bolt endpoint secrets, raw driver exception messages, raw Cypher text in API responses, or full parameter payloads. |
| NFR-018 | Static Cypher statements shall avoid injection risks by using repository-owned labels, relationship types, and property names. |
| NFR-019 | User-controlled values shall be passed only as parameters. |
| NFR-020 | Metadata JSON, warning text, source paths, and stable keys shall not be copied into logs beyond existing safe logging conventions. |

### 5.5 Maintainability and Documentation

| ID | Requirement |
| --- | --- |
| NFR-021 | The optimized writer shall keep write-group responsibilities readable and testable. |
| NFR-022 | Shared batching helpers, if introduced, shall use repository C# coding standards, including block-scoped namespaces, Allman braces, and one public type per file. |
| NFR-023 | Implementation work derived from this specification shall follow `.github/instructions/documentation-pass.instructions.md`, including developer-level documentation for internal and non-public types touched by the work. |
| NFR-024 | Wiki updates shall explain the optimized write shape and post-optimization diagnostic interpretation without duplicating implementation-plan detail. |
| NFR-025 | The implementation shall avoid introducing broad generic abstraction layers unless they clearly reduce duplication in the Neo4j adapter without obscuring Cypher behavior. |

## 6. Technical Requirements

### 6.1 Cypher Shape

| ID | Requirement |
| --- | --- |
| TR-001 | Batched write statements shall use list parameters such as `$nodes`, `$metrics`, `$evidence`, or equivalent names. |
| TR-002 | Batched write statements shall use `UNWIND` or an equivalent Neo4j-supported set expansion mechanism. |
| TR-003 | Batched `MERGE` identities shall use the same properties as the current uniqueness constraints. |
| TR-004 | Batched `SET` clauses shall update the same properties as the current per-record statements. |
| TR-005 | Batched relationship statements shall `MATCH` source and target nodes by indexed stable-key properties before `MERGE`ing relationships. |
| TR-006 | Batched statements shall consume the cursor result before proceeding to the next persistence stage. |
| TR-007 | Batched statements shall avoid returning large result sets to the client. |
| TR-008 | Batched statements may return aggregate write counts only when those counts are needed for diagnostics and do not materially increase overhead. |

### 6.2 Parameter Mapping

| ID | Requirement |
| --- | --- |
| TR-009 | The mapper shall continue to produce deterministic parameter values for normalized properties and metadata JSON. |
| TR-010 | Parameter materialization shall avoid mutating domain snapshot objects. |
| TR-011 | Parameter materialization shall preserve ordinal stable-key behavior where existing snapshot ordering depends on stable keys. |
| TR-012 | Nullable values shall be represented in a Neo4j driver-compatible way consistent with the current writer. |
| TR-013 | Parameter payloads shall not include domain objects directly when plain dictionaries or simple parameter records are safer and clearer. |

### 6.3 Schema and Index Considerations

| ID | Requirement |
| --- | --- |
| TR-014 | The existing uniqueness constraints shall be reviewed for compatibility with batched `MERGE` statements. |
| TR-015 | The existing secondary indexes shall not be removed solely as part of WP017 unless profiling evidence proves they are a primary bottleneck and downstream query requirements are still satisfied. |
| TR-016 | WP017 shall not introduce schema changes unless batching exposes a clear missing lookup index or constraint. |
| TR-017 | Any schema change shall be idempotent and added through `Neo4jSchemaStatementCatalog` using stable names. |
| TR-018 | Any schema change shall be reflected in wiki guidance and schema tests. |

## 7. Testing Requirements

### 7.1 Unit and Component Tests

| ID | Requirement |
| --- | --- |
| TEST-001 | Tests shall verify that batched parameter materialization preserves the same values produced by the current mapper for nodes, metrics, and evidence. |
| TEST-002 | Tests shall verify that empty high-volume sections do not execute unnecessary batched statements. |
| TEST-003 | Tests shall verify that final partial batches execute correctly. |
| TEST-004 | Tests shall verify that operation counts reflect batched Cypher executions rather than per-row execution where the diagnostic contract says so. |
| TEST-005 | Tests shall verify that relationship-family counters remain accurate after relationship batching. |
| TEST-006 | Tests shall verify that cancellation and safe failure behavior remain compatible with existing expectations. |

### 7.2 Integration Tests

| ID | Requirement |
| --- | --- |
| TEST-007 | Neo4j integration tests shall verify that representative snapshots persist successfully with the optimized writer. |
| TEST-008 | Integration tests shall verify that persisted repositories, solutions, snapshots, nodes, metrics, evidence records, and support relationships can be found by stable-key properties. |
| TEST-009 | Integration tests shall verify that evidence deduplication still collapses equivalent evidence payloads within one snapshot. |
| TEST-010 | Integration tests shall verify idempotent rerun behavior for the same snapshot stable keys. |
| TEST-011 | Integration tests shall verify that no duplicate support relationships are created by repeated persistence attempts. |
| TEST-012 | Integration tests shall verify that missing required relationship endpoints cause controlled failure or equivalent documented behavior. |

### 7.3 Performance Validation

| ID | Requirement |
| --- | --- |
| TEST-013 | A performance validation path shall compare pre-optimization and post-optimization persistence diagnostics for a large representative snapshot. |
| TEST-014 | The validation shall record total persistence duration, write-node duration, write-metric duration, write-evidence duration, relationship-family durations, operation count, and batch count. |
| TEST-015 | The validation shall not require running the full repository test suite for every local performance check. |
| TEST-016 | The validation shall document environmental factors such as local Neo4j container use, database state, workstation variability, and whether the graph was clean or already populated. |
| TEST-017 | Performance validation shall avoid committing large generated benchmark payloads or temporary output files to the repository root. |

## 8. Documentation Requirements

| ID | Requirement |
| --- | --- |
| DOC-001 | The `wiki/neo4j-persistence-foundation.md` page shall be updated to describe the optimized batched write model after implementation. |
| DOC-002 | The wiki shall continue to explain that stable keys are the logical identity and Neo4j internal IDs are not public contract values. |
| DOC-003 | The wiki shall explain how to interpret post-optimization `persistenceOperationCount` and `persistenceBatchCount`. |
| DOC-004 | The wiki shall explain any newly introduced relationship-family diagnostic timings. |
| DOC-005 | The wiki shall clarify that `Persistence.Commit` wraps the write transaction unless implementation changes alter that meaning. |
| DOC-006 | The implementation plan for WP017 shall reference `.github/instructions/documentation-pass.instructions.md` as a mandatory requirement for all touched code, including internal and non-public types. |

## 9. Acceptance Criteria

| ID | Criterion |
| --- | --- |
| AC-001 | Large snapshot persistence uses batched Cypher execution for metrics and the high-volume support relationship families. |
| AC-002 | The optimized writer preserves existing stable-key merge semantics for repositories, solutions, snapshots, nodes, metrics, evidence records, and support relationships. |
| AC-003 | Existing Neo4j persistence tests pass after optimization, with updates only where diagnostics intentionally change. |
| AC-004 | New tests prove batching behavior, graph equivalence, idempotency, and diagnostic count behavior. |
| AC-005 | A representative large snapshot shows materially reduced `Persistence.WriteMetrics` and support relationship write durations compared with the per-record implementation. |
| AC-006 | `persistenceOperationCount` decreases materially for large snapshots and is documented according to its post-optimization meaning. |
| AC-007 | The extraction status API remains backward compatible for existing consumers. |
| AC-008 | The wiki is updated to describe the optimized persistence behavior and diagnostic interpretation. |
| AC-009 | No implementation exposes Neo4j internal IDs through domain, application, API, MCP, test assertion contracts, or documentation examples. |
| AC-010 | The implementation remains compliant with repository coding, architecture, testing, and documentation-pass instructions. |

## 10. Risks and Technical Challenges

| Risk | Mitigation |
| --- | --- |
| Very large `UNWIND` parameter lists could increase transaction memory pressure. | Use explicit batch sizes, test large snapshots, and make batch size configurable if needed. |
| Batching can hide which row failed inside a large statement. | Preserve safe failure behavior, keep batch sizes bounded, and add contextual logs that identify stage and batch group without exposing sensitive payloads. |
| Relationship batching may silently skip rows when `MATCH` does not find endpoints. | Validate required endpoint assumptions, consider aggregate matched-row checks, and test missing-endpoint behavior. |
| Changing diagnostic operation counts may confuse comparison with WP016 runs. | Update wiki guidance to distinguish pre-optimization per-record operations from post-optimization statement executions. |
| Secondary indexes may still dominate write cost after statement batching. | Optimize statement shape first, then use diagnostics/profiling to decide whether schema/index changes are justified. |
| Multiple transaction chunks could break atomic completed-run semantics. | Keep one transaction by default and require explicit design approval before chunking. |
| Parameter materialization could become the next bottleneck. | Measure post-batching timings and optimize mapper/materialization only after Neo4j statement count is reduced. |

## 11. Out of Scope

The following items are explicitly out of scope for WP017:

- Replacing Neo4j with another database.
- Changing extraction algorithms or adding new extractor facts.
- Redesigning the domain graph model.
- Exposing Neo4j internal IDs through public contracts.
- Removing evidence-first modeling.
- Removing stable-key merge semantics.
- Introducing asynchronous background projection without a separate specification.
- Introducing destructive graph recreation changes.
- Building UI features for performance visualization.
- Running or requiring Stryker mutation testing.

## 12. Decisions and Open Questions

### 12.1 Recorded Decisions

| ID | Question | Answer |
| --- | --- | --- |
| OQ-001 | What default batch size should be used for local development and CI? | Use a default batch size of 1,000 rows for high-volume Neo4j `UNWIND` writes. Tune only after diagnostics show batching overhead remains material. |
| OQ-002 | Should batch size be configurable in `Neo4jOptions` in the first implementation? | Add an optional `PersistenceBatchSize` setting to `Neo4jOptions`, defaulting to 1,000 rows. Validate it defensively and use the default when unset. |
| OQ-003 | Should `persistenceBatchCount` continue to mean transaction count or should a new field describe Cypher list batches? | Preserve `persistenceBatchCount` as the write transaction count for WP017. Use `persistenceOperationCount` to report actual Cypher executions after batching. Defer a new Cypher-batch-count field unless diagnostics prove it necessary. |
| OQ-004 | Should relationship-family timings be introduced in WP017 or deferred? | Introduce relationship-family timings in WP017 because the relationship stage is the largest current bottleneck and contains multiple independently optimizable relationship groups. |

### 12.2 Open Validation Question

| ID | Question | Answer |
| --- | --- | --- |
| OQ-005 | What representative large snapshot should be used for repeatable validation? | Use the same real repository extraction manually to measure the effect of the optimizations. Do not create a synthetic large repository or synthetic large-snapshot fixture for WP017 validation. |

## 13. Traceability to Observed Performance Evidence

| Observed Evidence | WP017 Response |
| --- | --- |
| `Persistence` took approximately 171.4 seconds of approximately 183.8 seconds total. | WP017 targets persistence throughput directly rather than extraction pipeline behavior. |
| `Persistence.WriteMetrics` took approximately 49.4 seconds for 45,169 metrics. | WP017 requires batched metric writes as a primary optimization target. |
| `Persistence.WriteRelationships` took approximately 99.7 seconds for 96,347 support relationships. | WP017 requires batched support relationship writes and recommends relationship-family timing splits. |
| `persistenceOperationCount` was 154,274. | WP017 requires reducing actual Cypher execution count and updating diagnostics to reflect batched execution. |
| `persistenceBatchCount` was 1. | WP017 preserves one transaction by default but distinguishes transaction count from statement batching semantics. |
| Code inspection showed sequential `RunAsync` calls inside one `ExecuteWriteAsync` transaction. | WP017 specifies `UNWIND`-based or equivalent set-oriented statements inside the coordinated transaction. |
| Wiki requires stable keys and no public Neo4j internal IDs. | WP017 preserves stable-key `MERGE` semantics and prohibits exposing internal IDs. |

End of File.
