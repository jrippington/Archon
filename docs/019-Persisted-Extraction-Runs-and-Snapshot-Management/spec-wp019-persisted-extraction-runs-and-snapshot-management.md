# WP019 Specification - Persisted Extraction Runs and Snapshot Management

## Document Control

| Field | Value |
| --- | --- |
| Work Package | WP019 - Persisted Extraction Runs and Snapshot Management |
| Output Path | `docs/019-Persisted-Extraction-Runs-and-Snapshot-Management/spec-wp019-persisted-extraction-runs-and-snapshot-management.md` |
| Source Brief | User request to confirm extraction run state is currently in-memory, persist all extraction runs, add management deletion for snapshots, update existing APIs to use the persisted mechanism, and remove the old in-memory store from production use. |
| Specification Basis | `spec-template_v1.1.md` requested; template file was not present in the workspace at creation time, so this document follows the repository specification structure and documented work-package rules. |
| Status | Draft |
| Audience | Product owner, architect, implementation team, test engineer |

## 1. Overview

### 1.1 Purpose

This specification defines WP019, the work package that moves Archon extraction run history and snapshot lifecycle management from process-local memory to durable Neo4j-backed persistence. The work package ensures that every extraction run has a durable operational record, that run status and history survive process restarts, and that management APIs can delete one or more persisted snapshots through controlled, auditable operations.

WP019 uses Neo4j as the system of record for extraction run history and snapshot lifecycle metadata. It does not introduce SQL Server or a separate system graph area. Operational run data is represented with first-class graph labels alongside the existing snapshot graph model.

### 1.2 Background

The current extraction run implementation stores run lifecycle state in memory. `IExtractionRunHistory` is registered to `InMemoryExtractionRunHistory` by the extraction API module, and that implementation stores runs in a private process-local dictionary keyed by `ExtractionRunId`. Extraction status, extraction history, and management run-history views therefore depend on current process memory and are lost on restart or when multiple API instances are involved.

The current snapshot lifecycle management path also contains process-local behavior. `InMemoryArchitectureSnapshotWriter` stores written snapshots in a private list, and `ManagementOperationsService.BuildSnapshotLifecycleRows()` can only build lifecycle rows when the active writer is that in-memory implementation. When a real infrastructure writer is active, the current default management service cannot list snapshot lifecycle rows from Neo4j through a dedicated lifecycle abstraction.

WP019 addresses these gaps by introducing durable graph-backed run history, graph-backed snapshot lifecycle query/deletion ports, production dependency-injection changes, API updates, and tests that prove in-memory state is no longer the production mechanism for extraction status, run history, and snapshot management.

### 1.3 High-Level Scope

WP019 covers:

- Persisting a durable record for every accepted extraction run.
- Updating run lifecycle progress, diagnostics, terminal status, and produced snapshot identity in durable storage.
- Representing run records in Neo4j using `:ExtractionRun`, `:ExtractionRunRequest`, and `:ExtractionRunDiagnostic` nodes linked to the existing `:Snapshot` model where applicable.
- Keeping operational run data in the same Neo4j database as architecture snapshots, without introducing a separate system graph area or SQL Server dependency.
- Adding graph-backed query and deletion abstractions for snapshot lifecycle management.
- Adding management APIs to delete a specific snapshot or all snapshots from the database with safe validation controls.
- Updating existing extraction and management APIs to use the persisted mechanism.
- Removing the old in-memory extraction run store from production registration.
- Retaining test fakes or in-memory implementations only where they are explicitly test-local or development-only.

WP019 excludes user interface work, external queue durability, long-term audit retention policy beyond the minimum records described here, authentication/authorization design beyond endpoint safety requirements, and broad graph model redesign unrelated to extraction runs and snapshot lifecycle management.

## 2. System Context

### 2.1 Product Context

Archon accepts extraction requests, runs a deterministic extraction pipeline, assembles architecture snapshots, persists those snapshots to Neo4j, and exposes query, extraction, management, and MCP surfaces over the resulting architecture knowledge. Extraction runs are operational records that explain when work was requested, how it progressed, whether it succeeded or failed, and which snapshot was produced.

A run record is not the same thing as a snapshot. A failed or cancelled run can exist without a produced snapshot. A successful run should link to the snapshot it produced. Snapshot deletion should not automatically erase the historical fact that a run occurred unless a separate retention policy explicitly requires run-history deletion.

### 2.2 Source References

WP019 must align with these existing repository materials and implementation areas:

- `docs/003-Neo4j-Persistence-Foundation/spec-wp003-neo4j-persistence-foundation.md` for the Neo4j persistence boundary and schema initialization approach.
- `docs/004-API-Extraction-Contract-and-Snapshot-Orchestration/spec-wp004-api-extraction-contract-and-snapshot-orchestration.md` for extraction start, status, history, run lifecycle, and snapshot orchestration behavior.
- `docs/014-Query-API-Product-Surface/spec-wp014-query-api-product-surface.md` for query API expectations and snapshot selection behavior.
- `docs/016-Persistence-Diagnostics/spec-wp016-persistence-diagnostics.md` for persistence diagnostic data attached to extraction runs.
- `docs/017-Neo4j-Persistence-Performance/spec-wp017-neo4j-persistence-performance.md` for Neo4j persistence performance and batching considerations.
- `.github/copilot-instructions.md` for Onion Architecture, documentation workflow, coding standards, and work-package completion rules.
- `.github/instructions/documentation-pass.instructions.md` for documentation expectations that future implementation plans must treat as mandatory for modified source code.

### 2.3 Users and Stakeholders

| Stakeholder | Interest |
| --- | --- |
| Product owner | Needs extraction history and snapshot management to work reliably across restarts and production deployments. |
| Architect | Confirms that durable run history fits the existing Onion Architecture and Neo4j persistence strategy without adding unnecessary storage technology. |
| Developer | Needs clear ports, graph model, schema names, and API contracts for implementing persisted run and snapshot lifecycle behavior. |
| Test engineer | Verifies persistence behavior, deletion safety, API compatibility, restart resilience, and removal of production in-memory dependencies. |
| Operator | Needs management APIs for snapshot cleanup and reliable run-history diagnostics. |
| API consumer | Uses extraction and management APIs to poll run status, list prior runs, and manage snapshot lifecycle data. |

## 3. Component Summary

### 3.1 Persisted Extraction Run History

The persisted extraction run history component replaces production use of `InMemoryExtractionRunHistory` with a Neo4j-backed implementation of the existing `IExtractionRunHistory` contract or a compatible evolved contract. It creates a run record at acceptance time, updates the record as orchestration progresses, and reads status/history from durable storage.

### 3.2 Neo4j Operational Run Model

The graph model uses first-class labels in the existing Neo4j database:

| Label | Purpose |
| --- | --- |
| `:ExtractionRun` | Durable operational lifecycle record for one extraction run. |
| `:ExtractionRunRequest` | Normalized submitted request summary and safe request metadata for the run. |
| `:ExtractionRunDiagnostic` | Warnings, errors, timings, persistence diagnostic summaries, or other structured diagnostic records associated with a run. |
| existing `:Snapshot` | Existing persisted architecture snapshot produced by a successful run. |

No separate `:ArchonSystem` label, separate system graph, or SQL Server database is required for WP019.

### 3.3 Snapshot Lifecycle Query and Deletion Store

Snapshot lifecycle management is represented through application-layer ports that can list persisted snapshots and delete persisted snapshots from Neo4j. These ports must not expose Neo4j driver types, raw Cypher, internal graph identifiers, or arbitrary mutation capabilities to the application or API layers.

### 3.4 Management Snapshot Deletion API

The management API gains controlled deletion operations for one specific snapshot and for all snapshots. The operations require explicit validation, safe confirmation for deleting all snapshots, bounded result reporting, and credential-safe response diagnostics. WP019 deliberately excludes scoped deletion by date, repository, solution, status, or retention rules because the immediate goal is keeping local development databases to a sensible size.

### 3.5 Existing API Integration

Existing extraction and management endpoints continue to present stable public behavior while reading from persistent storage:

- `POST /extractions` persists the accepted run before scheduling work.
- `GET /extractions/{runId}` reads the durable run record.
- `GET /extractions` reads durable recent run history.
- `GET /management/runs` reads durable run history.
- `GET /management/snapshots` reads graph-backed snapshot lifecycle rows.
- Existing retention behavior is superseded by explicit single-snapshot and delete-all snapshot management operations.

### 3.6 Production Composition and Test Support

Production host composition registers Neo4j-backed run history and snapshot lifecycle/deletion stores when Neo4j is configured. In-memory implementations may remain only as explicit test fakes or local fallback components, and their names and registrations must make clear that they are not the production persistence mechanism.

## 4. Functional Requirements

### 4.1 Current-State Confirmation

| ID | Requirement |
| --- | --- |
| FR-001 | The implementation plan shall recognize that extraction run records are currently stored only in memory through `InMemoryExtractionRunHistory`. |
| FR-002 | The implementation plan shall recognize that production extraction API registration currently binds `IExtractionRunHistory` to the in-memory implementation. |
| FR-003 | The implementation plan shall recognize that current management run-history behavior reads through `IExtractionRunHistory` and therefore observes the same in-memory state. |
| FR-004 | The implementation plan shall recognize that current snapshot lifecycle listing is coupled to `InMemoryArchitectureSnapshotWriter` and does not provide a graph-backed lifecycle store. |

### 4.2 Storage Decision

| ID | Requirement |
| --- | --- |
| FR-005 | Extraction run records shall be persisted to Neo4j. |
| FR-006 | WP019 shall not introduce SQL Server for extraction run history or snapshot lifecycle management. |
| FR-007 | WP019 shall not introduce a separate physical graph or separate system area for extraction run data. |
| FR-008 | Operational run records shall coexist with existing architecture snapshot records in the same Neo4j database using explicit labels and relationships. |
| FR-009 | The graph model shall use `:ExtractionRun`, `:ExtractionRunRequest`, `:ExtractionRunDiagnostic`, and existing `:Snapshot` labels as the preferred representation. |

### 4.3 Extraction Run Creation

| ID | Requirement |
| --- | --- |
| FR-010 | A durable `:ExtractionRun` record shall be created for every accepted extraction request before the request is reported as accepted to the caller. |
| FR-011 | The run record shall include a stable public run identifier equivalent to the current `ExtractionRunId`. |
| FR-012 | The run record shall include status, started UTC, optional completed UTC, progress stage, progress message, optional progress percentage, and progress last-updated UTC. |
| FR-013 | The run record shall include safe aggregate warning and error counts. |
| FR-014 | The run record shall support an optional produced snapshot stable key. |
| FR-015 | The creation operation shall remain cancellable before durable state is committed. |
| FR-016 | The start request path shall not schedule background extraction work until the accepted run has been durably recorded. |

### 4.4 Extraction Run Request Storage

| ID | Requirement |
| --- | --- |
| FR-017 | A normalized `:ExtractionRunRequest` record shall be associated with each `:ExtractionRun`. |
| FR-018 | The request record shall include repository root directory, submitted solution paths, optional branch name, optional commit SHA, optional requested-by value, and metadata keys. |
| FR-019 | Metadata values shall not be persisted in the run request record unless they are explicitly approved as safe for operational history. |
| FR-020 | Request storage shall preserve enough information for existing extraction history responses to be produced without process memory. |
| FR-021 | Request storage shall avoid secrets, connection strings, credentials, environment variable dumps, and raw unbounded request payloads. |

### 4.5 Extraction Run Progress and Terminal Updates

| ID | Requirement |
| --- | --- |
| FR-022 | Run progress updates shall update the durable `:ExtractionRun` record. |
| FR-023 | Terminal run updates shall persist final status, completed UTC, final progress, produced snapshot identity when available, warning count, error count, timings, and persistence diagnostics. |
| FR-024 | A successful run shall link the `:ExtractionRun` record to the produced `:Snapshot` record. |
| FR-025 | Failed and cancelled runs shall remain queryable even when no snapshot was produced. |
| FR-026 | Updating diagnostics shall not erase existing request summary, run identity, or produced snapshot identity. |
| FR-027 | Repeated updates for the same run identifier shall be idempotent at the graph identity level. |
| FR-028 | Concurrent readers shall never observe malformed partial records that cannot be mapped to an API response. |

### 4.6 Extraction Run Diagnostics

| ID | Requirement |
| --- | --- |
| FR-029 | `:ExtractionRunDiagnostic` records shall represent structured run diagnostics where child records are preferable to large opaque properties. |
| FR-030 | Diagnostic records shall support warning, error, timing, and persistence diagnostic categories. |
| FR-031 | Diagnostic records shall include a stable category or kind value. |
| FR-032 | Timing diagnostics shall preserve stage name, elapsed milliseconds, and completed UTC where those values are available. |
| FR-033 | Persistence diagnostic summaries from WP016 shall be persisted sufficiently to reconstruct current extraction status responses. |
| FR-034 | Diagnostic records shall not expose raw stack traces, raw driver exceptions, raw Cypher statements, credentials, or connection strings through public API responses. |
| FR-035 | The persistence design may use compact serialized diagnostic properties for rarely queried diagnostic details when that remains compatible with API response requirements and test expectations. |

### 4.7 Snapshot Lifecycle Listing

| ID | Requirement |
| --- | --- |
| FR-036 | Snapshot lifecycle listing shall read from a graph-backed lifecycle store rather than `InMemoryArchitectureSnapshotWriter`. |
| FR-037 | The lifecycle store shall list snapshot stable key, repository stable key, optional solution stable key, status, branch name, commit SHA, started UTC, completed UTC, warning count, and error count where available. |
| FR-038 | Lifecycle listing may preserve existing safe read filters for repository stable key, solution stable key, status, date range, commit SHA, and take limit, but these filters shall not imply support for scoped deletion. |
| FR-039 | Lifecycle listing shall preserve deterministic newest-first ordering with stable tie-breaking. |
| FR-040 | Lifecycle listing shall not expose Neo4j internal node IDs. |

### 4.8 Specific Snapshot Deletion

| ID | Requirement |
| --- | --- |
| FR-041 | The management API shall provide an operation to delete one specific snapshot by snapshot stable key. |
| FR-042 | Deleting one snapshot shall delete the complete snapshot-scoped subgraph for that snapshot, including the `:Snapshot` node and all nodes and relationships whose lifecycle is scoped to the snapshot stable key. |
| FR-043 | Deleting one snapshot shall not delete repository or solution records that are shared across other snapshots unless a later explicit cleanup policy is introduced. |
| FR-044 | Deleting one snapshot shall not delete the associated extraction run record by default. |
| FR-045 | After deleting a snapshot, any associated run record shall either retain the deleted snapshot stable key with a deleted-snapshot indicator or expose that the produced snapshot is no longer available. |
| FR-046 | Deleting a nonexistent snapshot shall return a controlled not-found or no-op response according to the management API convention selected during implementation planning. |

### 4.9 Delete-All Snapshot Deletion

| ID | Requirement |
| --- | --- |
| FR-047 | The management API shall provide an operation to delete all snapshots from the database. |
| FR-048 | Delete-all snapshot deletion shall require an explicit confirmation value to prevent accidental destructive calls. |
| FR-049 | WP019 shall not provide scoped deletion by repository stable key, solution stable key, before UTC, status, commit SHA, or other lifecycle filters. |
| FR-050 | WP019 shall not provide dry-run deletion because the expected graph volume is too large for a user to make a useful manual decision from a preview. |
| FR-051 | Delete-all snapshot deletion shall report deleted snapshot count, deleted node count where practical, deleted relationship count where practical, and safe warnings. |
| FR-052 | Deletion shall reject raw Cypher, arbitrary labels, arbitrary property names, filesystem commands, database administration commands, or caller-provided mutation expressions. |
| FR-053 | Deletion shall be bounded to either one validated public snapshot stable identity or all persisted snapshot-scoped subgraphs after explicit delete-all confirmation. |

### 4.10 Retention API Direction

| ID | Requirement |
| --- | --- |
| FR-054 | Existing retention behavior shall not be expanded as part of WP019. |
| FR-055 | If an existing retention endpoint remains during transition, it shall not be treated as the primary snapshot cleanup mechanism for WP019. |
| FR-056 | Snapshot cleanup for WP019 shall be implemented through explicit delete-one and delete-all operations. |
| FR-057 | Date-based, status-based, repository-scoped, and solution-scoped deletion policies are out of scope for WP019. |
| FR-058 | Any future retention policy shall be specified in a later work package if it becomes necessary. |

### 4.11 Existing Extraction API Update

| ID | Requirement |
| --- | --- |
| FR-059 | `POST /extractions` shall continue to return accepted run status after durable run creation and scheduling. |
| FR-060 | `GET /extractions/{runId}` shall read run status from persistent run history. |
| FR-061 | `GET /extractions` shall read recent run history from persistent run history. |
| FR-062 | Existing response fields for extraction status and history shall remain compatible unless an implementation plan explicitly documents a versioned breaking change. |
| FR-063 | Existing clients that read status, progress, warning count, error count, timings, snapshot identity, and persistence diagnostics shall continue to receive those fields. |
| FR-064 | Invalid run identifier text shall continue to be handled safely without leaking parsing exceptions. |

### 4.12 Existing Management API Update

| ID | Requirement |
| --- | --- |
| FR-065 | `GET /management/runs` shall read from persistent run history. |
| FR-066 | `GET /management/snapshots` shall read from graph-backed lifecycle storage. |
| FR-067 | `POST /management/retention` shall evaluate and optionally delete persisted snapshots through graph-backed deletion storage. |
| FR-068 | Management readiness shall report the graph-backed snapshot lifecycle and extraction run history dependencies. |
| FR-069 | Readiness shall no longer degrade solely because the active snapshot writer is not `InMemoryArchitectureSnapshotWriter`. |

### 4.13 Production Dependency Injection

| ID | Requirement |
| --- | --- |
| FR-070 | Production composition shall register the Neo4j-backed extraction run history implementation when Neo4j infrastructure is configured. |
| FR-071 | Production composition shall register graph-backed snapshot lifecycle and deletion stores when Neo4j infrastructure is configured. |
| FR-072 | The extraction API module shall not force production hosts to use `InMemoryExtractionRunHistory`. |
| FR-073 | In-memory implementations may remain available for focused tests, but production host behavior shall not depend on them for run history or snapshot lifecycle management. |
| FR-074 | Dependency registration shall preserve Onion Architecture dependency direction. |

### 4.14 Removal of Production In-Memory Store Use

| ID | Requirement |
| --- | --- |
| FR-075 | The old in-memory extraction run store shall be removed from production registration. |
| FR-076 | If `InMemoryExtractionRunHistory` remains in source, it shall be clearly positioned as a test or local fallback implementation rather than the production mechanism. |
| FR-077 | Tests that currently rely on process-local run history shall be updated to use explicit test fakes, controlled in-memory test services, or Neo4j integration fixtures as appropriate. |
| FR-078 | No production API behavior shall require data from `InMemoryExtractionRunHistory` after WP019 is complete. |
| FR-079 | No production management snapshot lifecycle behavior shall require data from `InMemoryArchitectureSnapshotWriter` after WP019 is complete. |

## 5. Non-Functional Requirements

### 5.1 Reliability and Durability

| ID | Requirement |
| --- | --- |
| NFR-001 | Accepted extraction run records shall survive API process restart. |
| NFR-002 | Extraction run status and history shall be visible across multiple API instances that use the same Neo4j database. |
| NFR-003 | Snapshot lifecycle listing and deletion shall operate against durable graph data. |
| NFR-004 | Failed and cancelled runs shall remain durable operational records. |

### 5.2 Consistency

| ID | Requirement |
| --- | --- |
| NFR-005 | A run shall not be reported as accepted unless its durable run record has been created. |
| NFR-006 | A successful run shall not be marked completed until snapshot persistence succeeds and the produced snapshot identity is recorded. |
| NFR-007 | The run-to-snapshot relationship shall be created or updated consistently with terminal run status where a snapshot is produced. |
| NFR-008 | Snapshot deletion shall leave run history in a consistent, explainable state. |

### 5.3 Security and Safety

| ID | Requirement |
| --- | --- |
| NFR-009 | Management deletion endpoints shall be constrained and shall not expose arbitrary database mutation. |
| NFR-010 | Public API responses shall not expose credentials, connection strings, raw driver exceptions, raw Cypher statements, stack traces, or internal Neo4j node IDs. |
| NFR-011 | Delete-all snapshot deletion shall require explicit confirmation and shall not support dry-run preview in WP019. |
| NFR-012 | Deletion responses shall be safe for operational use and shall include only public stable identities and safe diagnostics. |

### 5.4 Performance

| ID | Requirement |
| --- | --- |
| NFR-013 | Run history writes shall add minimal overhead compared with extraction and snapshot persistence. |
| NFR-014 | Recent run history queries shall use indexed graph lookups and deterministic ordering. |
| NFR-015 | Snapshot lifecycle queries shall use indexed graph lookups for common filters. |
| NFR-016 | Snapshot deletion shall use bounded, explicit graph operations and shall avoid loading unnecessary full graph payloads into application memory. |

### 5.5 Maintainability

| ID | Requirement |
| --- | --- |
| NFR-017 | Application-layer ports shall hide Neo4j driver types from application and API projects. |
| NFR-018 | Infrastructure implementations shall own Cypher, schema statements, mapping, and Neo4j session handling. |
| NFR-019 | The graph schema additions shall use explicit names that are visible in code review and tests. |
| NFR-020 | Future implementation shall follow repository coding standards, including Allman braces, block-scoped namespaces, one public type per file, and underscore-prefixed private fields. |
| NFR-021 | Future implementation plans shall treat `.github/instructions/documentation-pass.instructions.md` as mandatory for modified source code, including internal and non-public types. |

## 6. Data and Graph Model Requirements

### 6.1 Nodes

| Node Label | Required Identity | Required Data |
| --- | --- | --- |
| `:ExtractionRun` | `runId` | status, timestamps, progress, counts, optional snapshot stable key, audit-safe lifecycle metadata. |
| `:ExtractionRunRequest` | run-scoped stable identity or relationship from run | repository root directory, solution paths, optional branch, optional commit SHA, optional requested-by, metadata keys. |
| `:ExtractionRunDiagnostic` | run-scoped diagnostic identity | diagnostic kind, stage or code where applicable, safe message or numeric values, ordering/timestamp data where applicable. |
| `:Snapshot` | existing snapshot stable key | existing snapshot lifecycle and architecture data. |

### 6.2 Relationships

| Relationship | Source | Target | Purpose |
| --- | --- | --- | --- |
| `HAS_REQUEST` | `:ExtractionRun` | `:ExtractionRunRequest` | Associates a run with its normalized submitted request summary. |
| `HAS_DIAGNOSTIC` | `:ExtractionRun` | `:ExtractionRunDiagnostic` | Associates a run with warnings, errors, timings, and persistence diagnostic records. |
| `PRODUCED_SNAPSHOT` | `:ExtractionRun` | `:Snapshot` | Links a successful run to the snapshot it produced. |

Relationship names may be adjusted during implementation planning to match existing Neo4j naming conventions, but the model shall preserve the same semantics.

### 6.3 Constraints and Indexes

| Schema Item | Purpose |
| --- | --- |
| Unique constraint on `ExtractionRun.runId` | Enforces one durable run record per public run identifier. |
| Index on `ExtractionRun.status` | Supports management filtering and operational views. |
| Index on `ExtractionRun.startedUtc` | Supports newest-first history queries. |
| Index on `ExtractionRun.snapshotStableKey` | Supports lookup between runs and produced snapshots. |
| Run-scoped uniqueness for diagnostics where applicable | Prevents duplicate diagnostic rows for idempotent updates. |

## 7. API Requirements

### 7.1 Extraction API Compatibility

The public extraction API shall continue to support the current start, status, and history workflow. The primary behavior change is that all state comes from durable storage instead of process memory.

### 7.2 Management Snapshot Deletion API Direction

The implementation plan shall define exact routes and request/response contracts, but the management API shall provide these capabilities:

| Capability | Direction |
| --- | --- |
| Delete one snapshot | Delete by public snapshot stable key. |
| Delete all snapshots | Delete every persisted snapshot-scoped subgraph after explicit confirmation. |

The route design shall remain consistent with the existing no-common-`/api` repository convention and the current `/management` route group.

### 7.3 Response Safety

Deletion and lifecycle responses shall include public stable keys, counts, warnings, and audit metadata where appropriate. They shall not include dry-run state, raw Cypher, driver exception details, internal Neo4j IDs, credentials, or stack traces.

## 8. Implementation Guidance

### 8.1 Application Layer

The application layer should define or evolve ports for:

- Extraction run history creation, update, single-run lookup, and recent-history lookup.
- Snapshot lifecycle listing.
- Snapshot deletion by identity and delete-all snapshot cleanup.

The application layer should not reference Neo4j driver types or infrastructure implementation classes.

### 8.2 Infrastructure Layer

The Neo4j infrastructure layer should implement the application ports and own:

- Cypher statements.
- Schema initialization additions.
- Mapping between application models and graph parameters.
- Transaction boundaries.
- Error translation to safe application diagnostics.
- Integration tests for graph persistence behavior.

### 8.3 API Layer

The API layer should remain transport-focused. It should bind route inputs, delegate validation and behavior to application services, and map application results to safe HTTP responses.

### 8.4 Test Strategy

Tests should cover:

- Run creation persists durable graph state.
- Run progress and terminal updates persist and can be re-read.
- Successful runs link to produced snapshots.
- Failed runs remain queryable without snapshots.
- Extraction API status/history read from persistent run history.
- Management run history reads from persistent run history.
- Snapshot lifecycle listing reads from graph-backed lifecycle storage.
- Specific snapshot deletion removes snapshot-scoped graph data.
- Delete-all snapshot deletion requires explicit confirmation.
- Delete-all snapshot deletion removes all persisted snapshot-scoped subgraphs.
- Snapshot cleanup does not depend on dry-run or retention semantics.
- Production DI does not bind `IExtractionRunHistory` to `InMemoryExtractionRunHistory` when Neo4j is configured.

For this work package, implementation plans should avoid running the full test suite unless repository guidance at execution time requires it. Targeted project tests and relevant integration tests should be specified first.

## 9. Risks and Open Decisions

| ID | Risk or Decision | Direction |
| --- | --- | --- |
| R-001 | Run diagnostics may become large if every warning, error, and timing is represented as a separate node. | Use child nodes for query-relevant diagnostics and compact serialized properties for details that are only reconstructed for status responses, if needed. |
| R-002 | Snapshot deletion may orphan run records that reference deleted snapshots. | Preserve run records and expose deleted-snapshot state or retained snapshot stable key clearly. |
| R-003 | Delete-all snapshot deletion is destructive. | Require explicit confirmation and safe response counts; do not provide dry-run because the graph volume is too large for a useful manual preview. |
| R-004 | Existing management lifecycle behavior is coupled to the in-memory writer. | Introduce graph-backed lifecycle ports rather than extending the writer abstraction beyond its responsibility. |
| R-005 | Updating dependency injection could affect test setup. | Keep explicit test fakes or in-memory test implementations but remove production dependence on them. |

## 10. Acceptance Criteria

| ID | Criterion |
| --- | --- |
| AC-001 | A new extraction run remains visible through status and history APIs after the API process restarts, assuming the same Neo4j database remains available. |
| AC-002 | `GET /extractions/{runId}` returns persisted run state rather than process-local state. |
| AC-003 | `GET /extractions` returns recent persisted runs in deterministic newest-first order. |
| AC-004 | `GET /management/runs` returns persisted run history. |
| AC-005 | `GET /management/snapshots` returns graph-backed snapshot lifecycle rows when Neo4j persistence is active. |
| AC-006 | A successful extraction run is linked to its produced `:Snapshot`. |
| AC-007 | A failed extraction run remains queryable even when no snapshot was produced. |
| AC-008 | A management caller can delete one specific snapshot by public snapshot stable key. |
| AC-009 | A management caller can delete all snapshots through an explicit delete-all operation. |
| AC-010 | Delete-all snapshot deletion requires explicit confirmation. |
| AC-011 | Snapshot deletion removes the complete snapshot-scoped subgraph for each deleted snapshot. |
| AC-012 | Production service registration no longer uses `InMemoryExtractionRunHistory` as the active run-history implementation when Neo4j is configured. |
| AC-013 | Public API responses do not expose internal Neo4j IDs, raw Cypher, credentials, connection strings, raw stack traces, or raw driver exception details. |

## 11. Out of Scope

WP019 does not cover:

- Introducing SQL Server or any non-Neo4j database for run history.
- Introducing a separate physical system graph or `:ArchonSystem` label area.
- Durable external queueing for background extraction work.
- User interface changes.
- Authentication and authorization policy design beyond endpoint safety and destructive-operation confirmation.
- Snapshot archival outside Neo4j.
- Deleting extraction run history as a separate retention policy.
- Scoped snapshot deletion by date, status, repository, solution, commit SHA, or similar lifecycle filters.
- Dry-run snapshot deletion preview.
- Optimizing snapshot persistence throughput beyond what is necessary to safely add run-history and deletion persistence.

## 12. Change Log

| Date | Change |
| --- | --- |
| 2026-05-28 | Initial draft created from discussion confirming current in-memory run history and selecting Neo4j-backed `:ExtractionRun`, `:ExtractionRunRequest`, `:ExtractionRunDiagnostic`, and existing `:Snapshot` model without a separate system area. |
| 2026-05-28 | Recorded final snapshot deletion agreement: WP019 supports delete-one and delete-all only, excludes scoped deletion and dry-run, and requires deleting complete snapshot-scoped subgraphs to manage local development database size. |
