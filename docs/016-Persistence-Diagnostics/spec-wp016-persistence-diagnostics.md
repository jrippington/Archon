# WP016 Specification - Persistence Diagnostic Breakdown for Extraction Status

## Document Control

| Field | Value |
| --- | --- |
| Work Package | WP016 - Persistence Diagnostic Breakdown for Extraction Status |
| Output Path | `docs/016-Persistence-Diagnostics/spec-wp016-persistence-diagnostics.md` |
| Source Work Package | New work package created from persistence performance review following WP015 |
| Source Brief | User-provided extraction timing result for run `5587751b-78f8-49dd-84f7-51fd1746960b` and recommendation to instrument persistence more finely |
| Specification Basis | `spec-template_v1.1.md` requested; template file was not present in the workspace at creation time, so this document follows the repository specification structure and documented work-package rules. |
| Status | Draft |
| Audience | Product owner, architect, implementation team, test engineer |

## 1. Overview

### 1.1 Purpose

This specification defines the requirements for WP016, the Archon work package that introduces a finer diagnostic breakdown around the extraction snapshot persistence stage. The goal is to make persistence performance transparent through the existing extraction status API surface so that slow persistence runs can be diagnosed without guessing which persistence sub-phase consumed time.

WP016 is a diagnostic and observability work package. It does not change extraction semantics, snapshot content, graph identity rules, or API workflow behavior beyond adding more detailed persistence timing and count data to the run status response.

### 1.2 Background

A completed extraction run for a roughly 60-project solution showed that extraction pipeline execution completed quickly, while persistence dominated total runtime. The recorded timings showed approximately 11.6 seconds for the pipeline and approximately 170.1 seconds for persistence, making persistence the controlling performance concern for that run.

The current top-level `Persistence` timing is too coarse to identify whether the bottleneck is serialization, identity normalization, write preparation, node writes, relationship writes, warning writes, metric writes, transaction commit, indexing, or another persistence responsibility. WP016 addresses this by recording persistence sub-stage diagnostics and exposing them through the same extraction status API shape that already reports run timings.

### 1.3 High-Level Scope

WP016 covers diagnostic visibility for snapshot persistence:

- Persistence sub-stage timing capture.
- Persistence entity and operation count capture.
- Integration with extraction run lifecycle status.
- API contract extension for get extraction status responses.
- Safe handling of in-progress, completed, and failed persistence diagnostics.
- Tests proving the diagnostics are captured, ordered, serialized, and returned consistently.
- Documentation updates for interpreting persistence diagnostics.

WP016 excludes persistence optimization, batching changes, Neo4j schema redesign, asynchronous projection redesign, queueing changes, extraction-stage behavior changes, and UI work.

## 2. System Context

### 2.1 Product Context

Archon accepts extraction requests, runs a deterministic extraction pipeline, assembles architecture snapshots, persists those snapshots, and exposes run state through extraction status APIs. As extraction coverage grows, snapshot persistence will handle larger numbers of nodes, edges, evidence records, findings, warnings, metrics, and generated summaries.

The system needs enough operational detail to determine whether persistence cost comes from payload preparation, storage I/O, relationship handling, indexing, transaction behavior, or high-volume entity groups. WP016 makes this information available to API consumers and developers without requiring direct infrastructure logs or debugger access.

### 2.2 Source References

WP016 must align with these repository materials:

- `docs/004-API-Extraction-Contract-and-Snapshot-Orchestration/spec-wp004-api-extraction-contract-and-snapshot-orchestration.md` for extraction run lifecycle, progress reporting, timings, and get extraction status API behavior.
- `docs/003-Neo4j-Persistence-Foundation/spec-wp003-neo4j-persistence-foundation.md` for Neo4j persistence boundary and snapshot persistence responsibilities.
- `docs/013-Metrics-Hotspots-Architecture-Rules-and-Snapshot-Diff/spec-wp013-metrics-hotspots-architecture-rules-and-snapshot-diff.md` for metric and snapshot result volume considerations.
- `.github/instructions/documentation-pass.instructions.md` for implementation documentation expectations, including internal and non-public types.

### 2.3 Users and Stakeholders

| Stakeholder | Interest |
| --- | --- |
| Product owner | Confirms that extraction status exposes enough information to understand why runs are slow. |
| Architect | Confirms that diagnostics respect application boundaries and do not leak persistence implementation details unnecessarily. |
| Developer | Uses persistence sub-stage timing and count data to identify optimization targets. |
| Test engineer | Verifies diagnostic capture, serialization, status response shape, and failure behavior. |
| API consumer | Reads the extraction status response to observe high-level and persistence-specific progress and duration. |
| Operator | Uses run status output to distinguish extraction bottlenecks from persistence bottlenecks. |

## 3. Component Summary

### 3.1 Persistence Diagnostic Collector

The persistence diagnostic collector records named sub-stage timings and count values during snapshot persistence. It is responsible for producing structured diagnostics that can be attached to the extraction run lifecycle without coupling inward application contracts to Neo4j driver types.

### 3.2 Persistence Sub-Stage Timing Model

The timing model represents each persistence sub-stage in a consistent shape compatible with the existing extraction timing output. Each item has a stage name, elapsed duration, and completion timestamp. The model supports completed and failed runs and should allow partial data when persistence fails before all sub-stages complete.

### 3.3 Persistence Count Model

The count model records volume and operation indicators that help explain timing results. Counts include snapshot entity counts, relationship counts, warning counts, metric counts, operation counts, batch counts, and serialized payload size where available.

### 3.4 Extraction Run Lifecycle Integration

The extraction run lifecycle stores persistence diagnostics alongside existing run status, progress, warnings, errors, snapshot identity, and top-level timings. Diagnostics must be available from get extraction status after completion and after controlled persistence failure.

### 3.5 Get Extraction Status API Contract

The get extraction status API returns persistence diagnostics in the same style as existing run timing information. Existing API consumers must continue to receive current top-level fields, while new consumers can inspect a dedicated persistence diagnostic breakdown.

### 3.6 Tests and Documentation

Tests verify that persistence diagnostics are recorded, retained, serialized, and exposed through status retrieval. Documentation explains how to interpret the new fields and clarifies that WP016 adds observability before optimization.

## 4. Functional Requirements

### 4.1 Persistence Diagnostic Capture

| ID | Requirement |
| --- | --- |
| FR-001 | The implementation shall capture fine-grained diagnostic data for the extraction snapshot persistence stage. |
| FR-002 | Diagnostic capture shall begin before persistence preparation starts and end after the persistence stage reaches success or controlled failure. |
| FR-003 | Diagnostic capture shall not change the persisted snapshot content. |
| FR-004 | Diagnostic capture shall not change extraction stage execution order. |
| FR-005 | Diagnostic capture shall not require API consumers to enable a special debug mode before diagnostics are recorded. |
| FR-006 | Diagnostic capture shall preserve partial diagnostic data when persistence fails after one or more sub-stages complete. |
| FR-007 | Diagnostic capture shall be safe when persistence fails before any sub-stage completes. |
| FR-008 | Diagnostic capture shall avoid recording secrets, connection strings, credentials, raw environment variables, or unredacted infrastructure internals. |

### 4.2 Required Persistence Sub-Stage Timings

| ID | Requirement |
| --- | --- |
| FR-009 | The implementation shall record a `persistence-prepare-snapshot` or equivalent timing for converting the assembled snapshot into the persistence-ready representation. |
| FR-010 | The implementation shall record a `persistence-serialize` or equivalent timing when serialization or payload materialization is performed as part of persistence. |
| FR-011 | The implementation shall record a `persistence-normalize-identities` or equivalent timing for stable key, identity, or lookup preparation performed during persistence. |
| FR-012 | The implementation shall record a `persistence-write-snapshot-header` or equivalent timing for writing snapshot header data. |
| FR-013 | The implementation shall record a `persistence-write-repositories` or equivalent timing for repository records when those records are written separately. |
| FR-014 | The implementation shall record a `persistence-write-solutions` or equivalent timing for solution records when those records are written separately. |
| FR-015 | The implementation shall record a `persistence-write-projects` or equivalent timing for project records when those records are written separately. |
| FR-016 | The implementation shall record a `persistence-write-files` or equivalent timing for file or document records when those records are written separately. |
| FR-017 | The implementation shall record a `persistence-write-nodes` or equivalent timing for generalized node writes. |
| FR-018 | The implementation shall record a `persistence-write-relationships` or equivalent timing for edge or relationship writes. |
| FR-019 | The implementation shall record a `persistence-write-evidence` or equivalent timing for evidence writes. |
| FR-020 | The implementation shall record a `persistence-write-findings` or equivalent timing for finding or rule result writes. |
| FR-021 | The implementation shall record a `persistence-write-warnings` or equivalent timing for warning writes. |
| FR-022 | The implementation shall record a `persistence-write-metrics` or equivalent timing for metric writes. |
| FR-023 | The implementation shall record a `persistence-write-metadata` or equivalent timing for metadata writes. |
| FR-024 | The implementation shall record a `persistence-commit` or equivalent timing for transaction commit or durable write finalization. |
| FR-025 | The implementation shall record a `persistence-indexing` or equivalent timing when indexing, constraint maintenance, or read-model update work is explicitly part of synchronous persistence. |
| FR-026 | The implementation shall record a `persistence-total` or equivalent timing for the full persistence operation, in addition to the existing top-level `Persistence` timing if that timing remains part of the existing run timing list. |
| FR-027 | Sub-stage names shall be stable enough for tests, scripts, and API consumers to compare across runs. |
| FR-028 | Sub-stage timings that are not applicable to a concrete persistence implementation may be omitted, but omitted stages shall not prevent other persistence diagnostics from being returned. |

### 4.3 Required Persistence Counts

| ID | Requirement |
| --- | --- |
| FR-029 | The implementation shall capture the number of repositories included in the persisted snapshot. |
| FR-030 | The implementation shall capture the number of solutions included in the persisted snapshot. |
| FR-031 | The implementation shall capture the number of projects included in the persisted snapshot. |
| FR-032 | The implementation shall capture the number of files or documents included in the persisted snapshot where that concept exists. |
| FR-033 | The implementation shall capture the number of generalized nodes included in the persisted snapshot. |
| FR-034 | The implementation shall capture the number of generalized edges or relationships included in the persisted snapshot. |
| FR-035 | The implementation shall capture the number of evidence records included in the persisted snapshot. |
| FR-036 | The implementation shall capture the number of findings included in the persisted snapshot. |
| FR-037 | The implementation shall capture the number of warnings included in the persisted snapshot. |
| FR-038 | The implementation shall capture the number of errors included in the snapshot or failed run output where applicable. |
| FR-039 | The implementation shall capture the number of metric records included in the persisted snapshot. |
| FR-040 | The implementation shall capture the number of generated summaries included in the persisted snapshot. |
| FR-041 | The implementation shall capture the number of metadata entries included in the persisted snapshot where available. |
| FR-042 | The implementation shall capture the total number of persistence operations where the persistence adapter can measure or estimate it accurately. |
| FR-043 | The implementation shall capture the total number of persistence batches where batching exists. |
| FR-044 | The implementation shall capture serialized payload byte size where serialization produces a measurable payload. |
| FR-045 | Count fields shall be numeric and shall use zero for known empty collections. |
| FR-046 | Count fields that cannot be measured accurately shall be nullable or omitted according to the existing API serialization convention. |
| FR-047 | Count names shall remain stable enough for API consumers to compare extraction runs over time. |

### 4.4 Extraction Run Lifecycle Integration

| ID | Requirement |
| --- | --- |
| FR-048 | The extraction run lifecycle model shall retain persistence diagnostics for each run. |
| FR-049 | Persistence diagnostics shall be associated with exactly one extraction run. |
| FR-050 | Persistence diagnostics shall be updated before a run is marked completed. |
| FR-051 | A run shall not be marked completed before persistence succeeds and persistence diagnostics available at that point have been recorded. |
| FR-052 | A failed persistence run shall retain all diagnostics collected before failure. |
| FR-053 | Persistence diagnostic updates shall not erase existing warnings, errors, progress, top-level timings, submitted request data, or snapshot identity. |
| FR-054 | The run lifecycle store shall remain testable without running the Aspire AppHost. |
| FR-055 | The run lifecycle integration shall remain replaceable and shall not require API host code to know Neo4j driver details. |

### 4.5 Get Extraction Status API Response

| ID | Requirement |
| --- | --- |
| FR-056 | The get extraction status API shall return persistence diagnostics for a run when diagnostics have been captured. |
| FR-057 | The get extraction status API shall continue returning existing top-level run status fields. |
| FR-058 | The get extraction status API shall continue returning the existing top-level `timings` collection or equivalent current timing field. |
| FR-059 | Persistence diagnostics shall be output in the get extraction status API in the same style as existing timing values are output today. |
| FR-060 | The response shall expose persistence sub-stage timings as a collection of timing items with stage name, elapsed milliseconds, and completed UTC values, matching existing naming and serialization conventions where practical. |
| FR-061 | The response shall expose persistence counts as structured fields grouped under a persistence diagnostics section or equivalent response property. |
| FR-062 | The response shall allow API consumers to distinguish top-level pipeline timings from persistence sub-stage timings. |
| FR-063 | The response shall include persistence diagnostics for completed runs. |
| FR-064 | The response shall include available partial persistence diagnostics for failed runs. |
| FR-065 | The response shall handle runs that have not reached persistence by returning no persistence diagnostics, an empty diagnostics object, or null according to the existing API convention. |
| FR-066 | The response shall not break existing clients that only read the current top-level status, warning count, error count, progress, timings, and snapshot identity fields. |
| FR-067 | The response shall not expose raw Cypher statements, raw driver exceptions, connection strings, or Neo4j endpoint credentials. |

### 4.6 Progress Reporting Behavior

| ID | Requirement |
| --- | --- |
| FR-068 | During persistence, extraction progress shall continue to indicate that the run is in the persistence stage. |
| FR-069 | When a persistence sub-stage starts, the run progress message should identify the current persistence activity where existing progress update patterns support this. |
| FR-070 | Persistence sub-stage progress updates shall not flood the lifecycle store with excessive write frequency. |
| FR-071 | The final completed status shall include both existing top-level timing data and the new persistence diagnostic breakdown. |

### 4.7 Error and Warning Handling

| ID | Requirement |
| --- | --- |
| FR-072 | Persistence diagnostic failures shall not mask the original persistence failure. |
| FR-073 | Failure to collect optional count values shall not fail an otherwise successful extraction run. |
| FR-074 | Failure to record required diagnostic timing data shall be captured as a controlled warning when persistence otherwise succeeds. |
| FR-075 | Controlled diagnostic warnings shall be visible through the existing warning model where appropriate. |
| FR-076 | Persistence diagnostic errors shall not include sensitive payload data. |

### 4.8 Documentation Requirements

| ID | Requirement |
| --- | --- |
| FR-077 | Documentation shall explain the purpose of persistence diagnostics and their relationship to the existing top-level `Persistence` timing. |
| FR-078 | Documentation shall describe every emitted persistence sub-stage timing name. |
| FR-079 | Documentation shall describe every emitted persistence count field. |
| FR-080 | Documentation shall explain how to interpret missing or null diagnostic values. |
| FR-081 | Documentation shall clarify that WP016 adds diagnostic visibility and does not itself optimize persistence throughput. |
| FR-082 | Documentation shall include a non-sensitive sample get extraction status response fragment showing persistence diagnostics. |

## 5. Non-Functional Requirements

### 5.1 Architecture and Boundaries

| ID | Requirement |
| --- | --- |
| NFR-001 | Persistence diagnostic contracts used by the API shall live at an application or shared contract boundary, not in the API host or Neo4j driver implementation. |
| NFR-002 | The API host shall only serialize status data returned by application services and shall not compute persistence diagnostics itself. |
| NFR-003 | Infrastructure persistence implementations may collect implementation-specific measurements but must map them to stable application diagnostic contracts before returning them. |
| NFR-004 | Domain projects shall not depend on API host, Neo4j driver, or infrastructure-specific diagnostic types. |
| NFR-005 | The implementation shall preserve Onion Architecture dependency direction. |

### 5.2 Performance and Overhead

| ID | Requirement |
| --- | --- |
| NFR-006 | Diagnostic capture shall have low overhead relative to the persistence operation being measured. |
| NFR-007 | Diagnostic capture shall avoid materializing large duplicate snapshot payloads solely for measurement. |
| NFR-008 | Diagnostic capture shall avoid adding per-entity logging or lifecycle writes that materially worsen the persistence performance problem. |
| NFR-009 | Count collection shall reuse already-available collection sizes or operation counters where practical. |
| NFR-010 | Timing collection shall use monotonic elapsed-time measurement for durations where available. |

### 5.3 Compatibility

| ID | Requirement |
| --- | --- |
| NFR-011 | Existing extraction status API consumers shall continue to work when they ignore the new persistence diagnostics fields. |
| NFR-012 | Existing run status records created before WP016 shall remain readable. |
| NFR-013 | Missing persistence diagnostics on older runs shall not be treated as run corruption. |
| NFR-014 | Serialization naming shall follow existing API conventions for casing and date/time formatting. |

### 5.4 Reliability and Observability

| ID | Requirement |
| --- | --- |
| NFR-015 | Persistence diagnostics shall be retained after a completed run. |
| NFR-016 | Persistence diagnostics shall be retained after a failed persistence attempt when partial data is available. |
| NFR-017 | Diagnostic data shall be suitable for comparing runs of different repository sizes by including both durations and count values. |
| NFR-018 | Diagnostic data shall be suitable for identifying likely N+1 write behavior, excessive relationship writes, heavy serialization, or expensive commit/indexing behavior. |

### 5.5 Security and Privacy

| ID | Requirement |
| --- | --- |
| NFR-019 | Diagnostics shall not include secret values, credentials, access tokens, connection strings, raw environment variable values, or raw database endpoint credentials. |
| NFR-020 | Diagnostics shall avoid exposing raw local filesystem paths beyond values already approved for existing extraction status responses. |
| NFR-021 | Diagnostic error messages shall follow the same redaction standards as existing extraction errors. |

### 5.6 Documentation Standard

| ID | Requirement |
| --- | --- |
| NFR-022 | Implementation work derived from this specification shall apply the repository documentation-pass standard to public, internal, private, and other non-public types. |
| NFR-023 | Developer-level documentation shall explain diagnostic contracts, collector behavior, lifecycle integration, status response serialization, and interpretation guidance. |
| NFR-024 | Documentation shall not claim that persistence has been optimized unless a separate optimization work package implements and validates throughput improvements. |

## 6. API Contract Requirements

### 6.1 Get Extraction Run Status

| Field | Requirement |
| --- | --- |
| Method | Existing get extraction status retrieval method and route. |
| Purpose | Retrieve current lifecycle state for one extraction run, including persistence diagnostics when available. |
| Input | Extraction run identifier. |
| Existing Success Response | Run identifier, status, submitted request summary, timestamps, progress, warnings, errors, top-level timings, and snapshot identity where available. |
| New Response Content | Persistence diagnostics containing sub-stage timings and count values. |
| Not Found | Existing not-found behavior remains unchanged. |
| Compatibility | Existing fields remain stable; new fields are additive. |

### 6.2 Persistence Diagnostics Response Shape

The response shall expose the new diagnostic data as an additive section using repository API naming conventions. The exact property names may follow the existing response model, but the content shall support this logical shape:

| Property | Purpose |
| --- | --- |
| `persistenceDiagnostics` or equivalent | Container for persistence-specific diagnostic data. |
| `timings` or equivalent | Ordered persistence sub-stage timing collection. |
| `counts` or equivalent | Structured persistence count values. |
| `warnings` or equivalent optional field | Diagnostic-specific warnings if the existing warning model requires local grouping. |

A representative non-binding JSON fragment is shown for clarity:

```json
{
  "timings": [
	{
	  "stage": "Persistence",
	  "elapsedMilliseconds": 170067,
	  "completedUtc": "2026-05-27T07:47:40.5223117+00:00"
	}
  ],
  "persistenceDiagnostics": {
	"timings": [
	  {
		"stage": "persistence-prepare-snapshot",
		"elapsedMilliseconds": 120,
		"completedUtc": "2026-05-27T07:44:50.5740000+00:00"
	  },
	  {
		"stage": "persistence-write-warnings",
		"elapsedMilliseconds": 31000,
		"completedUtc": "2026-05-27T07:46:10.0000000+00:00"
	  },
	  {
		"stage": "persistence-commit",
		"elapsedMilliseconds": 8500,
		"completedUtc": "2026-05-27T07:47:40.0000000+00:00"
	  }
	],
	"counts": {
	  "repositoryCount": 1,
	  "solutionCount": 1,
	  "projectCount": 60,
	  "warningCount": 3308,
	  "nodeCount": 0,
	  "relationshipCount": 0,
	  "metricCount": 0,
	  "persistenceOperationCount": 0,
	  "persistenceBatchCount": 0,
	  "serializedPayloadBytes": null
	}
  }
}
```

The example is illustrative only. Implementation shall use the repository's actual response types and casing conventions.

## 7. Data and Contract Model Requirements

### 7.1 Required Application Contracts

The implementation shall define or align application-layer contracts for these concepts:

| Contract | Purpose |
| --- | --- |
| `PersistenceDiagnostics` or equivalent | Container for persistence-specific diagnostic output associated with an extraction run. |
| `PersistenceDiagnosticTiming` or equivalent | A single persistence sub-stage timing item with stage name, elapsed duration, and completion timestamp. |
| `PersistenceDiagnosticCounts` or equivalent | Count values that describe snapshot volume and persistence operation volume. |
| `PersistenceDiagnosticCollector` or equivalent | Component or abstraction used by persistence implementation to record timings and counts. |
| `PersistenceDiagnosticResult` or equivalent | Immutable result returned by persistence or lifecycle integration after persistence completes or fails. |

### 7.2 Timing Data Rules

| ID | Requirement |
| --- | --- |
| DCR-001 | Each completed timing item shall include a non-empty stage name. |
| DCR-002 | Each completed timing item shall include elapsed milliseconds greater than or equal to zero. |
| DCR-003 | Each completed timing item shall include a completed UTC timestamp. |
| DCR-004 | Timing items shall be ordered by completion sequence unless the existing API timing convention requires a different deterministic order. |
| DCR-005 | Timing item stage names shall not include run-specific identifiers, file paths, or secret values. |

### 7.3 Count Data Rules

| ID | Requirement |
| --- | --- |
| DCR-006 | Known collection counts shall use zero rather than null when the collection is known to be empty. |
| DCR-007 | Unknown or unmeasured optional counts shall use null or omission according to existing serialization conventions. |
| DCR-008 | Count values shall not be negative. |
| DCR-009 | Count values shall be captured from the snapshot or persistence operation represented by the same extraction run. |
| DCR-010 | Count values shall not require reading the persisted graph back from Neo4j solely to populate status diagnostics. |

## 8. Validation and Test Requirements

### 8.1 Unit Tests

| ID | Requirement |
| --- | --- |
| TR-001 | Tests shall verify that the diagnostic collector records named sub-stage timings. |
| TR-002 | Tests shall verify that timing items include elapsed milliseconds and completion timestamps. |
| TR-003 | Tests shall verify that known empty counts are represented as zero. |
| TR-004 | Tests shall verify that unknown optional counts can be represented without failing serialization. |
| TR-005 | Tests shall verify that partial diagnostics are retained when persistence fails after at least one sub-stage completes. |
| TR-006 | Tests shall verify that diagnostic capture does not mark a failed persistence run as completed. |
| TR-007 | Tests shall verify that diagnostic warnings do not erase existing run warnings. |

### 8.2 Application and API Contract Tests

| ID | Requirement |
| --- | --- |
| TR-008 | Tests shall verify that get extraction status returns persistence diagnostics for a completed run. |
| TR-009 | Tests shall verify that get extraction status returns available partial persistence diagnostics for a failed persistence run. |
| TR-010 | Tests shall verify that get extraction status handles runs with no persistence diagnostics. |
| TR-011 | Tests shall verify that existing top-level timing response fields remain present. |
| TR-012 | Tests shall verify that persistence sub-stage timings are serialized in the same style as existing timing values. |
| TR-013 | Tests shall verify that persistence counts are serialized with stable names. |
| TR-014 | Tests shall verify that older or manually seeded run records without diagnostics remain readable. |

### 8.3 Integration Tests

| ID | Requirement |
| --- | --- |
| TR-015 | Integration tests shall exercise the persistence handoff path with a representative snapshot and verify diagnostics are attached to the run status. |
| TR-016 | Integration tests shall verify that status retrieval after persistence completion includes persistence sub-stage data. |
| TR-017 | Integration tests shall avoid requiring the full external Aspire AppHost to run unless existing repository test conventions already provide a suitable fixture. |
| TR-018 | Integration tests shall use non-sensitive sample paths and metadata. |

### 8.4 Documentation Validation

| ID | Requirement |
| --- | --- |
| TR-019 | Documentation review shall confirm that every emitted persistence sub-stage name is described. |
| TR-020 | Documentation review shall confirm that every emitted persistence count field is described. |
| TR-021 | Documentation review shall confirm that a sample get extraction status response fragment is included. |
| TR-022 | Documentation review shall confirm that the work package is described as diagnostic visibility, not persistence optimization. |

## 9. Acceptance Criteria

| ID | Criterion |
| --- | --- |
| AC-001 | A completed extraction run exposes persistence diagnostics through the get extraction status API. |
| AC-002 | The get extraction status response includes persistence sub-stage timings in the same style as existing timing output. |
| AC-003 | The get extraction status response includes persistence count values that explain snapshot and persistence volume. |
| AC-004 | Existing status response fields remain available and compatible. |
| AC-005 | Failed persistence runs retain available partial diagnostics. |
| AC-006 | Runs that have not entered persistence are handled cleanly without false diagnostic data. |
| AC-007 | Tests cover collector behavior, lifecycle integration, status serialization, completed runs, failed runs, and older runs without diagnostics. |
| AC-008 | Documentation explains how to interpret the new persistence diagnostic breakdown. |

## 10. Out of Scope

The following items are explicitly out of scope for WP016:

- Optimizing persistence throughput.
- Changing Neo4j schema design.
- Introducing bulk write behavior.
- Introducing asynchronous projection or indexing workers.
- Changing extraction pipeline semantics.
- Changing snapshot identity rules.
- Changing route paths for existing extraction APIs.
- Adding UI visualization for persistence diagnostics.
- Adding benchmark tooling beyond tests needed to verify diagnostic capture.

## 11. Risks and Open Questions

### 11.1 Risks

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Diagnostic collection adds overhead to an already slow persistence path. | Persistence gets slower and measurements become less useful. | Use lightweight timers and already-available counts; avoid per-entity lifecycle writes. |
| Too many sub-stage names become implementation-specific. | API consumers depend on details that later change. | Use stable logical sub-stage names and allow non-applicable stages to be omitted. |
| Partial diagnostics are lost during persistence failure. | Failed runs remain difficult to diagnose. | Attach diagnostics to lifecycle state before terminal failure where practical. |
| Existing clients are sensitive to response shape changes. | Additive fields could still affect strict clients. | Keep existing fields unchanged and add diagnostics as an optional additive section. |

### 11.2 Resolved Open Questions

| ID | Question | Answer |
| --- | --- | --- |
| OQ-001 | Should persistence diagnostics be stored permanently with run history or only retained while run status is available? | Persistence diagnostics shall be retained with the existing extraction run lifecycle or status record according to the current run-history retention behavior. WP016 shall not introduce new durable run-history persistence solely to store diagnostics. If the current implementation retains run status in memory, diagnostics shall follow that same retention model. If a later work package introduces durable run history, the persistence diagnostic model shall be compatible with being retained there. |
| OQ-002 | Should operation and batch counts be exact or best-effort when the adapter cannot cheaply measure them? | Operation and batch counts shall be exact when the persistence adapter already knows them or can obtain them cheaply. Counts shall be nullable or omitted according to API convention when exact measurement would require extra graph reads, expensive traversal, or duplicate payload materialization. Diagnostic capture must not worsen the persistence performance problem. |
| OQ-003 | Should diagnostic sub-stage names use PascalCase like existing top-level stages or kebab-case to distinguish nested diagnostics? | Diagnostic sub-stage names shall use the repository's existing timing stage naming convention. If current top-level timings use display-style names such as `Persistence`, nested persistence stages shall use clear scoped names such as `Persistence.PrepareSnapshot`, `Persistence.Serialize`, `Persistence.NormalizeIdentities`, `Persistence.WriteSnapshotHeader`, `Persistence.WriteRepositories`, `Persistence.WriteSolutions`, `Persistence.WriteProjects`, `Persistence.WriteFiles`, `Persistence.WriteNodes`, `Persistence.WriteRelationships`, `Persistence.WriteEvidence`, `Persistence.WriteFindings`, `Persistence.WriteWarnings`, `Persistence.WriteMetrics`, `Persistence.WriteMetadata`, `Persistence.Commit`, `Persistence.Indexing`, and `Persistence.Total`. |
| OQ-004 | Should persistence diagnostics appear only under a dedicated property or also be flattened into the top-level `timings` collection? | Persistence diagnostics shall appear under a dedicated additive property such as `persistenceDiagnostics`. The existing top-level `timings` collection shall remain the summary view and shall continue to include the top-level `Persistence` timing. Detailed sub-stage timings shall not be flattened into the top-level timing collection. |

## 12. Implementation Guidance

Implementation should begin by identifying the existing extraction status response model, run lifecycle store, persistence handoff abstraction, and top-level timing capture mechanism. The new diagnostic contracts should be added at the application boundary and populated by the persistence adapter through a lightweight collector.

The implementation should favor additive changes. Existing top-level timing entries should remain unchanged, including the current `Persistence` stage timing. The new diagnostic breakdown should explain that top-level value rather than replace it.

A useful first implementation can capture the following minimum set:

- `persistence-prepare-snapshot`
- `persistence-write-nodes`
- `persistence-write-relationships`
- `persistence-write-warnings`
- `persistence-write-metrics`
- `persistence-commit`
- `persistence-total`
- repository, solution, project, warning, node, relationship, metric, operation, batch, and serialized payload counts where available

If the concrete persistence implementation does not have all logical sub-stages yet, it should emit the stages it can measure accurately and leave non-applicable or unmeasured values absent or null according to API convention.
