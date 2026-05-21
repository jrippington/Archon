# WP004 Specification - API Extraction Contract and Snapshot Orchestration

## Document Control

| Field | Value |
| --- | --- |
| Work Package | WP004 - API Extraction Contract and Snapshot Orchestration |
| Output Path | `docs/004-API-Extraction-Contract-and-Snapshot-Orchestration/spec-wp004-api-extraction-contract-and-snapshot-orchestration.md` |
| Source Work Package | `docs/foundation/work-packages.md` WP004 |
| Source Brief | `docs/foundation/archon_full_concept_brief.md` |
| Specification Basis | `spec-template_v1.1.md` requested; template file was not present in the workspace at creation time, so this document follows the repository specification structure and documented work-package rules. |
| Status | Draft |
| Audience | Product owner, architect, implementation team, test engineer |

## 1. Overview

### 1.1 Purpose

This specification defines the requirements for WP004, the Archon work package that introduces the API-triggered extraction contract and the snapshot orchestration workflow. WP004 establishes the single request, validation, orchestration, run-history, and persistence handoff path that all later extractor packages must use.

The package produces a complete API and application orchestration foundation, but only placeholder extractor outputs are expected during this work package. Later work packages will fill the shared extraction pipeline with repository, Roslyn, runtime, configuration, data-access, integration, rule, metric, markdown, and MCP behavior.

### 1.2 Background

Archon is a deterministic, evidence-backed architecture intelligence platform for modern and legacy .NET estates. The source brief requires extraction to be initiated through an API request that submits a repository root directory and an explicit list of solution paths. The API extraction module must validate the request before Roslyn workspace loading, coordinate a shared extraction pipeline, assemble a generalized architecture snapshot, persist the result to Neo4j, and expose extraction run status and history.

WP004 follows WP003. The Neo4j persistence adapter exists as the system of record, and WP004 must hand complete generalized snapshot contracts to that persistence boundary rather than introducing a narrower project-only aggregate or a separate storage model.

### 1.3 High-Level Scope

WP004 covers the API extraction contract and orchestration foundation:

- Start extraction request contract.
- Request validation before any Roslyn workspace or solution loading begins.
- Repository root and submitted solution path resolution.
- Extraction run lifecycle tracking.
- Shared extraction orchestration path for all later extraction slices.
- Shared accumulation and snapshot assembly model.
- Placeholder extractor output sufficient to prove the orchestration and persistence handoff.
- Neo4j snapshot persistence handoff through the WP003 persistence adapter.
- API endpoints to start extraction, inspect run status, and retrieve run history.
- Tests for validation, orchestration order, warnings, errors, persistence handoff, and run-history behavior.
- Documentation for the API contract, orchestration workflow, and current placeholder-extractor boundary.

## 2. System Context

### 2.1 Product Context

Archon accepts a repository analysis request through its API host, analyzes the requested repository and solution list through a deterministic extraction pipeline, persists the resulting architecture snapshot in Neo4j, and later exposes the persisted knowledge through API query and MCP surfaces. WP004 provides the first production API entry point for extraction and the durable run-management workflow needed before real extractor slices are implemented.

The orchestration model must be architecture-wide from the start. It must support repositories, solutions, snapshot headers, nodes, edges, evidence, findings, metrics, generated summaries, warnings, and errors, even when many of those collections are empty or contain only placeholder facts in this package.

### 2.2 Source References

WP004 must align with these source materials:

- `docs/foundation/work-packages.md` WP004 objective, required implementation, and completion criteria.
- `docs/foundation/archon_full_concept_brief.md` section 8.1 for Archon API Host and API Extraction Module responsibilities.
- `docs/foundation/archon_full_concept_brief.md` section 14.1 for extraction pipeline stages.
- `docs/foundation/archon_full_concept_brief.md` Appendix E section E.5.6 for the API extraction contract and generalized snapshot shape.
- `docs/foundation/archon_full_concept_brief.md` Appendix E section E.5.7 for the API-driven extraction pipeline and shared accumulation model.
- `docs/foundation/archon_full_concept_brief.md` Appendix E section E.9 for POST-driven extraction and generalized snapshot acceptance criteria.
- `.github/instructions/documentation-pass.instructions.md` for implementation documentation expectations, including internal and other non-public types.

### 2.3 Users and Stakeholders

| Stakeholder | Interest |
| --- | --- |
| Product owner | Confirms that Archon can start and track API-driven extraction runs using the required repository and solution inputs. |
| Architect | Confirms the orchestration contract is generalized, evidence-ready, persistence-backed, and reusable by later extractor slices. |
| Developer | Implements future extractor stages against one shared extraction path and accumulation model. |
| Test engineer | Verifies validation, lifecycle transitions, orchestration order, error handling, and persistence handoff behavior. |
| Future API and MCP consumer | Depends on extraction run identifiers, statuses, warnings, errors, and persisted snapshots produced through one consistent workflow. |

## 3. Component Summary

### 3.1 API Extraction Endpoints

The API extraction endpoints expose the HTTP surface for starting extraction, inspecting a single extraction run, and retrieving extraction run history. They translate HTTP payloads into application-layer commands and return stable response contracts without leaking infrastructure implementation details.

### 3.2 Extraction Request Validation

The validation component verifies that each start request contains one repository root directory and at least one submitted solution path. It validates shape, path normalization, path containment, duplicate solution paths, and invalid combinations before any Roslyn loading or extraction stage execution begins.

### 3.3 Repository and Solution Resolution

The resolution component normalizes the repository root and submitted solution paths into deterministic application-layer inputs. It verifies that submitted solutions are explicit, resolvable, and suitable for later Roslyn workspace loading while preserving enough warning and error detail for run reporting.

### 3.4 Extraction Run Lifecycle Store

The run lifecycle store records extraction run identifiers, request metadata, timestamps, status transitions, warnings, errors, persistence results, and snapshot identity. It supports retrieval of current status and run history independently of the full graph query model that arrives in later work packages.

### 3.5 Extraction Orchestrator

The extraction orchestrator is the single application path that all future extraction slices must use. It sequences validation, resolution, stage execution, snapshot assembly, persistence handoff, lifecycle state updates, warning capture, and error capture.

### 3.6 Extraction Stage Pipeline

The stage pipeline provides a composable execution model for extraction slices. During WP004, only placeholder stages are required; however, the stage interface and accumulation model must support nodes, edges, evidence, findings, metrics, generated summaries, warnings, and errors from future packages.

### 3.7 Snapshot Assembly Component

The snapshot assembly component converts validated request context, resolved repository and solution data, stage contributions, warnings, and errors into one `ExtractedArchitectureSnapshot` contract that can be persisted through the Neo4j snapshot writer.

### 3.8 Persistence Handoff

The persistence handoff invokes the WP003 Neo4j persistence adapter with the complete generalized snapshot contract. It records success or failure in the extraction run lifecycle without bypassing the application-layer persistence boundary.

### 3.9 Tests and Documentation

Tests and documentation prove that the new API workflow is deterministic, validated before analysis, generalized beyond project extraction, and ready for later extractor slices without requiring the Aspire AppHost to run during automated validation.

## 4. Functional Requirements

### 4.1 Start Extraction Request Contract

| ID | Requirement |
| --- | --- |
| FR-001 | The implementation shall define `StartExtractionRequest` as the API contract used to initiate extraction. |
| FR-002 | `StartExtractionRequest` shall include `RepositoryRootDirectory`. |
| FR-003 | `StartExtractionRequest` shall include `SolutionPaths` as an explicit list of submitted solution paths. |
| FR-004 | `StartExtractionRequest` shall include optional `BranchName`. |
| FR-005 | `StartExtractionRequest` shall include optional `CommitSha`. |
| FR-006 | `StartExtractionRequest` shall include optional `RequestedBy`. |
| FR-007 | `StartExtractionRequest` shall include metadata using the repository-approved metadata representation. |
| FR-008 | The API contract shall not infer solution paths by scanning the repository when the request omits them. |
| FR-009 | The API contract shall preserve submitted request values sufficiently for run-history auditing while also using normalized values for execution. |
| FR-010 | The API contract shall support JSON serialization and deserialization using the conventions used by the existing API host. |

### 4.2 Request Validation

| ID | Requirement |
| --- | --- |
| FR-011 | Extraction shall require exactly one non-empty repository root directory value. |
| FR-012 | Extraction shall require at least one non-empty solution path. |
| FR-013 | Validation shall run before any Roslyn workspace loading, solution loading, project loading, or extractor-stage execution. |
| FR-014 | Validation shall reject repository root paths that do not exist or are not directories. |
| FR-015 | Validation shall reject solution paths that do not exist or are not files. |
| FR-016 | Validation shall reject submitted solution paths that do not have a `.sln` extension unless a future documented solution format is explicitly added. |
| FR-017 | Validation shall normalize absolute and relative solution paths consistently. |
| FR-018 | Relative solution paths shall be resolved relative to the submitted repository root directory. |
| FR-019 | Validation shall reject solution paths that resolve outside the submitted repository root unless an explicit policy later allows external solution files. |
| FR-020 | Validation shall reject duplicate solution paths after normalization. |
| FR-021 | Validation shall preserve user-actionable error messages for all rejected requests. |
| FR-022 | Validation shall avoid including sensitive metadata values in logs or validation messages. |

### 4.3 Repository and Solution Resolution

| ID | Requirement |
| --- | --- |
| FR-023 | The implementation shall create a resolved extraction input model after request validation succeeds. |
| FR-024 | The resolved input model shall include the normalized repository root directory. |
| FR-025 | The resolved input model shall include normalized submitted solution paths. |
| FR-026 | The resolved input model shall include request branch name, commit SHA, requested-by value, and metadata. |
| FR-027 | The resolved input model shall include enough display data to create repository and solution records in the assembled snapshot. |
| FR-028 | Resolution shall not silently drop solution paths. |
| FR-029 | Resolution shall distinguish blocking errors from non-blocking warnings. |
| FR-030 | Resolution shall make path comparison behavior deterministic on Windows path inputs. |

### 4.4 Extraction Run Lifecycle

| ID | Requirement |
| --- | --- |
| FR-031 | The implementation shall create an extraction run record when a valid extraction start request is accepted. |
| FR-032 | Each extraction run shall have a stable run identifier returned to the API caller. |
| FR-033 | Each extraction run shall record submitted request metadata needed for auditing and troubleshooting. |
| FR-034 | Each extraction run shall record started UTC. |
| FR-035 | Each extraction run shall record completed UTC when the run reaches a terminal state. |
| FR-036 | Each extraction run shall support at least `Accepted`, `Queued`, `Running`, `Completed`, and `Failed` lifecycle states, with `Cancelled` supported if cancellation is implemented in WP004. |
| FR-037 | Each extraction run shall collect warnings emitted during validation, resolution, orchestration, placeholder extraction, assembly, or persistence. |
| FR-038 | Each extraction run shall collect errors emitted during validation, resolution, orchestration, placeholder extraction, assembly, or persistence. |
| FR-039 | Failed runs shall preserve the failure stage where practical. |
| FR-040 | Completed runs shall preserve the persisted snapshot stable key or equivalent snapshot identity returned by persistence. |
| FR-041 | Run lifecycle updates shall be ordered so status endpoints never report success before persistence handoff succeeds. |
| FR-042 | Run lifecycle behavior shall be testable without running the Aspire AppHost. |

### 4.5 Shared Extraction Orchestration Path

| ID | Requirement |
| --- | --- |
| FR-043 | The implementation shall provide one application-layer extraction orchestration path that all later extractor packages must use. |
| FR-044 | The orchestrator shall execute validation before resolution. |
| FR-045 | The orchestrator shall execute resolution before stage execution. |
| FR-046 | The orchestrator shall execute stage contributions before snapshot assembly. |
| FR-047 | The orchestrator shall execute snapshot assembly before persistence handoff. |
| FR-048 | The orchestrator shall update run lifecycle state throughout the workflow. |
| FR-049 | The orchestrator shall capture non-blocking warnings without failing the run. |
| FR-050 | The orchestrator shall capture blocking errors and mark the run failed. |
| FR-051 | The orchestrator shall expose dependencies through application interfaces so tests can replace validation, stage execution, assembly, persistence, and run-history components. |
| FR-052 | The orchestrator shall not depend directly on Neo4j driver types, ASP.NET Core controller types, or host-specific objects. |

### 4.6 Extraction Stage Pipeline

| ID | Requirement |
| --- | --- |
| FR-053 | The implementation shall define an extraction stage abstraction for current placeholder stages and future extractor slices. |
| FR-054 | Each stage shall have a stable stage name or identifier. |
| FR-055 | Each stage shall receive the resolved extraction context and shared accumulation model. |
| FR-056 | Each stage shall be able to add nodes, edges, evidence, findings, metrics, generated summaries, warnings, and errors to the shared accumulation model. |
| FR-057 | The stage pipeline shall preserve deterministic execution ordering. |
| FR-058 | The stage pipeline shall stop execution when a blocking stage error requires run failure. |
| FR-059 | The stage pipeline shall allow later stages to be added without changing the API request contract. |
| FR-060 | WP004 shall provide placeholder stage behavior only where needed to prove orchestration, assembly, and persistence handoff. |
| FR-061 | Placeholder stages shall not be documented or represented as final extractor behavior. |
| FR-062 | Placeholder stages shall not invent architecture facts beyond minimal repository, solution, snapshot, warning, or no-op data required to prove the contract. |

### 4.7 Extracted Architecture Snapshot Contract

| ID | Requirement |
| --- | --- |
| FR-063 | The implementation shall define or align `ExtractedArchitectureSnapshot` as the durable generalized extraction result contract. |
| FR-064 | `ExtractedArchitectureSnapshot` shall include repositories. |
| FR-065 | `ExtractedArchitectureSnapshot` shall include solutions. |
| FR-066 | `ExtractedArchitectureSnapshot` shall include snapshot header data. |
| FR-067 | `ExtractedArchitectureSnapshot` shall include nodes. |
| FR-068 | `ExtractedArchitectureSnapshot` shall include edges. |
| FR-069 | `ExtractedArchitectureSnapshot` shall include evidence. |
| FR-070 | `ExtractedArchitectureSnapshot` shall include findings. |
| FR-071 | `ExtractedArchitectureSnapshot` shall include metrics. |
| FR-072 | `ExtractedArchitectureSnapshot` shall include generated summaries. |
| FR-073 | `ExtractedArchitectureSnapshot` shall include warnings. |
| FR-074 | `ExtractedArchitectureSnapshot` shall include errors. |
| FR-075 | The snapshot contract shall be generalized and shall not be a project-only aggregate. |
| FR-076 | Empty collections shall be represented explicitly where no facts exist yet. |
| FR-077 | The snapshot contract shall be compatible with the WP003 snapshot persistence adapter. |

### 4.8 Snapshot Assembly

| ID | Requirement |
| --- | --- |
| FR-078 | Snapshot assembly shall create a snapshot header for each extraction run. |
| FR-079 | Snapshot assembly shall include repository identity derived from the validated repository root and request metadata. |
| FR-080 | Snapshot assembly shall include solution identity for each submitted solution path. |
| FR-081 | Snapshot assembly shall include all stage-contributed nodes, edges, evidence, findings, metrics, generated summaries, warnings, and errors. |
| FR-082 | Snapshot assembly shall preserve explicit unknown and confidence data when present in contributed facts. |
| FR-083 | Snapshot assembly shall preserve deterministic stable keys and fingerprints produced by the shared WP002 components. |
| FR-084 | Snapshot assembly shall not create database IDs or use database IDs as stable identity. |
| FR-085 | Snapshot assembly shall include extraction version or equivalent producer metadata where the existing model supports it. |
| FR-086 | Snapshot assembly shall produce a complete contract suitable for persistence even when later extractor stages are not implemented yet. |

### 4.9 Persistence Handoff

| ID | Requirement |
| --- | --- |
| FR-087 | The orchestrator shall persist assembled snapshot output through the application-layer snapshot persistence abstraction implemented by the Neo4j adapter. |
| FR-088 | Persistence shall receive the complete generalized snapshot contract. |
| FR-089 | Persistence shall not receive a narrowed project-only projection. |
| FR-090 | Persistence success shall update extraction run state to completed and record persisted snapshot identity. |
| FR-091 | Persistence failure shall update extraction run state to failed and record a user-actionable error. |
| FR-092 | Persistence handoff shall preserve warnings generated earlier in the run. |
| FR-093 | Persistence handoff shall avoid re-validating API request shape as a substitute for orchestrator validation. |
| FR-094 | The implementation shall keep Neo4j as the system of record for extraction output. |

### 4.10 API Endpoints

| ID | Requirement |
| --- | --- |
| FR-095 | The API host shall expose an endpoint to start extraction. |
| FR-096 | The start extraction endpoint shall accept `StartExtractionRequest`. |
| FR-097 | The start extraction endpoint shall return an extraction run identifier and current run status. |
| FR-098 | The API host shall expose an endpoint to inspect extraction run status by run identifier. |
| FR-099 | The run status endpoint shall return current state, timestamps, warnings, errors, and snapshot identity when available. |
| FR-100 | The API host shall expose an endpoint to retrieve extraction run history. |
| FR-101 | The run history endpoint shall support a deterministic ordering, with newest-first ordering preferred unless existing API conventions require otherwise. |
| FR-102 | API endpoints shall return validation errors with appropriate client-error HTTP status behavior. |
| FR-103 | API endpoints shall return failure details without leaking secrets or infrastructure internals. |
| FR-104 | API endpoints shall be documented sufficiently for manual testing. |

### 4.11 Error and Warning Handling

| ID | Requirement |
| --- | --- |
| FR-105 | Validation errors shall prevent run creation when the request cannot be accepted. |
| FR-106 | Accepted runs that fail during orchestration shall be marked failed. |
| FR-107 | Accepted runs that fail during persistence shall be marked failed. |
| FR-108 | Errors shall include a code or category where the existing error model supports one. |
| FR-109 | Warnings shall be retained in both run status and snapshot output where applicable. |
| FR-110 | The implementation shall distinguish validation errors, resolution errors, stage errors, assembly errors, and persistence errors where practical. |
| FR-111 | The implementation shall preserve enough error detail for tests and developers to identify the failing stage. |
| FR-112 | Error handling shall not silently swallow unexpected exceptions. |
| FR-113 | Unexpected exceptions shall be converted to controlled run failure responses. |

### 4.12 Run History

| ID | Requirement |
| --- | --- |
| FR-114 | The implementation shall provide a run-history abstraction. |
| FR-115 | The run-history abstraction shall support creating a run, updating a run, retrieving a run by identifier, and retrieving recent runs. |
| FR-116 | The initial run-history implementation may use the repository-approved storage approach for WP004, provided it remains replaceable and testable. |
| FR-117 | Run history shall not replace Neo4j snapshot persistence as the system of record for extraction output. |
| FR-118 | Run history shall retain enough request context to troubleshoot which repository and solution paths were submitted. |
| FR-119 | Run history shall retain warnings and errors. |
| FR-120 | Run history shall be deterministic under unit and integration tests. |

### 4.13 Asynchronous Execution and Progress Reporting

| ID | Requirement |
| --- | --- |
| FR-121 | The start extraction endpoint shall validate the request, create a run record, schedule extraction work, and return quickly with the run identifier and current status. |
| FR-122 | The start extraction endpoint shall not wait for stage execution, snapshot assembly, or persistence handoff to complete before responding. |
| FR-123 | The implementation shall provide an application-level abstraction for scheduling or dispatching asynchronous extraction work so the HTTP host does not contain orchestration workflow logic. |
| FR-124 | The asynchronous execution abstraction shall allow the initial WP004 implementation to use an in-process background mechanism while preserving a replacement path for durable queues or distributed workers later. |
| FR-125 | Extraction progress shall be recorded on the run lifecycle model and exposed through the run status endpoint. |
| FR-126 | The progress model shall include the current stage name, a progress message, last updated UTC, and an optional or nullable percentage. |
| FR-127 | The progress model shall expose warnings and errors recorded so far. |
| FR-128 | The run status endpoint shall be the initial progress-reporting mechanism for WP004 API consumers. |
| FR-129 | Asynchronous failures shall be captured as controlled run failures and made visible through the run status endpoint without exposing stack traces, secrets, or infrastructure internals. |
| FR-130 | A run shall not report `Completed` until snapshot persistence succeeds and the persisted snapshot stable key or equivalent stable identity is recorded. |

## 5. Non-Functional Requirements

### 5.1 Architecture and Boundaries

| ID | Requirement |
| --- | --- |
| NFR-001 | Domain and application contracts shall remain independent of API host, Neo4j driver, ASP.NET Core controller, and infrastructure implementation details. |
| NFR-002 | API host code shall delegate orchestration to application services rather than containing extraction workflow logic directly. |
| NFR-003 | Infrastructure components may implement persistence and run-history adapters but shall not be referenced by inward layers. |
| NFR-004 | The implementation shall preserve Onion Architecture dependency direction established in WP001. |
| NFR-005 | Future extractor packages shall be able to add stages without changing API endpoint shapes. |

### 5.2 Determinism and Evidence Readiness

| ID | Requirement |
| --- | --- |
| NFR-006 | Stable keys shall be deterministic and independent of database IDs. |
| NFR-007 | Snapshot assembly shall preserve evidence collections even when WP004 itself produces minimal or no source-code evidence. |
| NFR-008 | Unknowns and confidence shall not be bypassed when persisted fact contracts require them. |
| NFR-009 | The orchestration pipeline shall avoid nondeterministic stage ordering. |
| NFR-010 | Metadata serialization shall be deterministic where it affects fingerprints, persistence, or test assertions. |

### 5.3 Security and Privacy

| ID | Requirement |
| --- | --- |
| NFR-011 | Repository paths, solution paths, and requested-by values shall be treated as potentially sensitive operational data in logs. |
| NFR-012 | Metadata values shall not be blindly written to logs. |
| NFR-013 | Error responses shall not expose secrets, environment variables, connection strings, access tokens, or raw stack traces. |
| NFR-014 | The API shall not execute arbitrary user-provided commands as part of WP004 extraction orchestration. |
| NFR-015 | Path validation shall prevent accidental analysis outside the submitted repository root under the default policy. |

### 5.4 Reliability and Observability

| ID | Requirement |
| --- | --- |
| NFR-016 | The orchestrator shall log major lifecycle transitions with credential-safe structured data. |
| NFR-017 | The orchestrator shall log validation, resolution, stage, assembly, and persistence failures with enough context for troubleshooting. |
| NFR-018 | Run status shall remain inspectable after a run fails. |
| NFR-019 | Cancellation support shall be included where existing application conventions support cancellation tokens. |
| NFR-020 | The implementation shall avoid blocking the request thread on unnecessary long-running synchronous operations when asynchronous APIs are available. |
| NFR-021 | The asynchronous extraction execution model shall be replaceable without changing the public API contract, so later durable queues or distributed workers can be introduced without breaking API consumers. |

### 5.5 Documentation Standard

| ID | Requirement |
| --- | --- |
| NFR-022 | Implementation work derived from this specification shall apply the repository documentation-pass standard to public, internal, private, and other non-public types. |
| NFR-023 | Developer-level documentation shall explain orchestration stages, run lifecycle transitions, validation boundaries, placeholder extractor limits, asynchronous execution behavior, progress reporting, and persistence handoff responsibilities. |
| NFR-024 | Documentation shall not describe placeholder extractor output as final extraction capability. |
| NFR-025 | API documentation shall include request and response examples using non-sensitive sample paths. |

## 6. API Contract Requirements

### 6.1 Start Extraction

| Field | Requirement |
| --- | --- |
| Method | `POST` or the existing repository API convention for command-style creation. |
| Purpose | Start a new architecture extraction run for one repository root and one or more explicit solution paths. |
| Request Body | `StartExtractionRequest`. |
| Success Response | Run identifier, current status, accepted request summary, and links or route values for status retrieval where existing conventions support them. |
| Validation Failure | Client-error response with validation details and no created extraction run. |
| Runtime Failure | If a run is accepted and later fails, status retrieval shall expose the failed state and errors. |
| Execution Behavior | The endpoint shall return after validation, run creation, and asynchronous scheduling; it shall not wait for extraction or persistence completion. |

### 6.2 Get Extraction Run Status

| Field | Requirement |
| --- | --- |
| Method | `GET` or the existing repository API convention for retrieval. |
| Purpose | Retrieve current lifecycle state for one extraction run. |
| Input | Extraction run identifier. |
| Success Response | Run identifier, status, submitted repository root summary, submitted solution path summary, started UTC, completed UTC where applicable, current stage, progress message, optional progress percentage, progress last updated UTC, warnings, errors, and snapshot identity where applicable. |
| Not Found | Client-error response when no run exists for the identifier. |

### 6.3 Get Extraction Run History

| Field | Requirement |
| --- | --- |
| Method | `GET` or the existing repository API convention for collection retrieval. |
| Purpose | Retrieve recent extraction runs for operations and troubleshooting. |
| Input | Optional paging or limit values if supported by existing API conventions. |
| Success Response | Ordered run summaries including run identifier, status, timestamps, repository root summary, solution count, warning count, error count, and snapshot identity where applicable. |

## 7. Data and Contract Model Requirements

### 7.1 Required Application Contracts

The implementation shall define or align application-layer contracts for the following concepts:

| Contract | Purpose |
| --- | --- |
| `StartExtractionRequest` | API request body for starting extraction. |
| `StartExtractionCommand` or equivalent | Application-layer command created from the API request. |
| `ResolvedExtractionInput` or equivalent | Validated and normalized repository and solution execution input. |
| `ExtractionRun` or equivalent | Run lifecycle state and audit summary. |
| `ExtractionRunStatus` or equivalent | Lifecycle status enumeration. |
| `ExtractionRunWarning` or equivalent | Warning detail emitted by validation, resolution, stages, assembly, or persistence. |
| `ExtractionRunError` or equivalent | Error detail emitted by validation, resolution, stages, assembly, or persistence. |
| `ExtractionRunProgress` or equivalent | Progress detail containing current stage, progress message, optional percentage, last updated UTC, and warning or error counts exposed through run status. |
| `ExtractionStage` abstraction or equivalent | Interface used by placeholder and future extraction stages. |
| `ExtractionAccumulation` or equivalent | Shared contribution model for nodes, edges, evidence, findings, metrics, summaries, warnings, and errors. |
| `ExtractionWorkScheduler` or equivalent | Application-level abstraction used to schedule asynchronous extraction execution outside the HTTP request. |
| `ExtractedArchitectureSnapshot` | Generalized snapshot contract handed to persistence. |
| `ExtractionRunSummary` or equivalent | Lightweight run-history response model. |

### 7.2 Snapshot Content Rules

| ID | Requirement |
| --- | --- |
| DCR-001 | A snapshot shall be associated with exactly one accepted extraction run. |
| DCR-002 | A snapshot shall include at least one repository record derived from the submitted repository root. |
| DCR-003 | A snapshot shall include every submitted solution path as a solution record. |
| DCR-004 | Snapshot warnings shall include non-blocking warnings from every orchestration phase. |
| DCR-005 | Snapshot errors shall include errors that are part of a failed assembled snapshot when assembly occurs before failure. |
| DCR-006 | Persistence shall not be attempted for invalid requests rejected before run creation. |
| DCR-007 | Persistence shall not be marked successful unless the WP003 persistence adapter returns success. |

## 8. Validation and Test Requirements

### 8.1 Unit Tests

| ID | Requirement |
| --- | --- |
| TR-001 | Tests shall prove a request with a missing repository root is rejected before orchestration continues. |
| TR-002 | Tests shall prove a request with an empty solution list is rejected before orchestration continues. |
| TR-003 | Tests shall prove a non-existent repository root is rejected. |
| TR-004 | Tests shall prove a non-existent solution path is rejected. |
| TR-005 | Tests shall prove duplicate solution paths are rejected after normalization. |
| TR-006 | Tests shall prove solution paths outside the repository root are rejected under the default policy. |
| TR-007 | Tests shall prove validation occurs before any stage execution. |
| TR-008 | Tests shall prove orchestration order is validation, resolution, stage execution, snapshot assembly, persistence handoff, and lifecycle completion. |
| TR-009 | Tests shall prove warnings are retained and do not fail a run. |
| TR-010 | Tests shall prove stage errors fail a run and prevent success reporting. |
| TR-011 | Tests shall prove persistence failures fail a run. |
| TR-012 | Tests shall prove persistence receives the full generalized snapshot contract. |
| TR-013 | Tests shall prove run status retrieval returns completed and failed runs. |
| TR-014 | Tests shall prove run history returns deterministic ordering. |
| TR-015 | Tests shall prove the start extraction endpoint or application command returns after scheduling asynchronous work and does not wait for extraction persistence to complete. |
| TR-016 | Tests shall prove progress updates include current stage, progress message, optional percentage, and last updated UTC. |
| TR-017 | Tests shall prove asynchronous execution failures are recorded as failed runs and are visible through run status retrieval. |

### 8.2 API Tests

| ID | Requirement |
| --- | --- |
| TR-018 | API tests shall verify the start extraction endpoint accepts a valid request and returns a run identifier. |
| TR-019 | API tests shall verify invalid request payloads return validation responses. |
| TR-020 | API tests shall verify the run status endpoint returns state, timestamps, current progress, warnings, errors, and snapshot identity where applicable. |
| TR-021 | API tests shall verify the run history endpoint returns run summaries. |
| TR-022 | API tests shall verify endpoint responses do not expose stack traces or secrets. |

### 8.3 Persistence Handoff Tests

| ID | Requirement |
| --- | --- |
| TR-023 | Tests shall verify the Neo4j persistence adapter abstraction is invoked once for a successful run. |
| TR-024 | Tests shall verify the persisted snapshot contains repositories, solutions, snapshot header, nodes, edges, evidence, findings, metrics, generated summaries, warnings, and errors collections. |
| TR-025 | Tests shall verify snapshot identity returned from persistence is recorded in run lifecycle state. |
| TR-026 | Tests shall verify persistence is not invoked for validation failures. |

### 8.4 Automated Validation Constraints

| ID | Requirement |
| --- | --- |
| TR-027 | Automated validation shall not start the Aspire AppHost process because it blocks the executing agent. |
| TR-028 | Tests shall run through project-level or solution-level test commands appropriate to the repository. |
| TR-029 | Tests may replace persistence and stage implementations with seams where real Neo4j behavior is not required for the orchestration concern under test. |

## 9. Documentation Requirements

| ID | Requirement |
| --- | --- |
| DR-001 | Documentation shall describe the start extraction request contract. |
| DR-002 | Documentation shall describe valid and invalid path examples. |
| DR-003 | Documentation shall describe extraction run lifecycle states. |
| DR-004 | Documentation shall describe run status and run history endpoints. |
| DR-005 | Documentation shall describe the shared extraction orchestration sequence. |
| DR-006 | Documentation shall describe asynchronous extraction behavior, including quick start responses, background scheduling, progress reporting through run status, and failure visibility. |
| DR-007 | Documentation shall describe the placeholder extractor boundary for WP004. |
| DR-008 | Documentation shall describe that future extractor packages must use the same orchestration path and accumulation model. |
| DR-009 | Documentation shall describe that Neo4j remains the system of record for extraction output. |
| DR-010 | Documentation shall provide manual verification instructions for starting Aspire and confirming API/MCP/Neo4j composition only if relevant files are touched; automated validation shall not run AppHost. |
| DR-011 | Documentation shall include the output path of this specification: `docs/004-API-Extraction-Contract-and-Snapshot-Orchestration/spec-wp004-api-extraction-contract-and-snapshot-orchestration.md`. |

## 10. Out of Scope

WP004 shall not implement the following capabilities:

- Full repository, project, package, or Roslyn semantic extraction.
- Runtime, ASP.NET, worker, console, configuration, dependency-injection, data-access, integration, UI-technology, markdown, MCP, hotlist, query, diff, or rule-evaluation behavior beyond placeholders required to prove orchestration.
- Discovery UI host, pages, dashboard, explorer, graph view, evidence viewer, hotlist viewer, prompt panel, or front-end assets.
- Disk-backed rule loading from `./rules`, unless an existing dependency already makes a no-op seam necessary.
- Markdown generation.
- MCP resource refresh.
- Production-grade distributed job processing unless the existing architecture already provides it and it is required for the minimal API contract.
- Graph migration behavior.
- Replacing Neo4j snapshot persistence with run history storage.

## 11. Acceptance Criteria

WP004 is complete when all of the following are true:

1. The API exposes a POST-driven extraction start contract accepting a repository root directory and explicit solution path list.
2. Extraction cannot start with invalid paths or an empty solution list.
3. Validation occurs before Roslyn workspace loading, solution loading, or extractor-stage execution.
4. The start extraction endpoint returns after validation, run creation, and asynchronous work scheduling rather than waiting for extraction and persistence to complete.
5. The run status endpoint exposes current asynchronous progress, including stage, message, optional percentage, last updated UTC, warnings, errors, and terminal snapshot identity when available.
6. The orchestration workflow uses a single shared application path for validation, resolution, stage execution, snapshot assembly, persistence handoff, and lifecycle updates.
7. The workflow produces a generalized `ExtractedArchitectureSnapshot` contract rather than a project-only aggregate.
8. The generalized snapshot contract includes repositories, solutions, snapshot header, nodes, edges, evidence, findings, metrics, generated summaries, warnings, and errors.
9. Snapshot persistence receives the complete generalized contract through the WP003 persistence abstraction.
10. Extraction run lifecycle state records accepted, queued, running, completed, failed, warnings, errors, and progress.
11. API consumers can inspect extraction run status by run identifier.
12. API consumers can retrieve extraction run history.
13. Tests cover validation, asynchronous scheduling, progress reporting, orchestration order, error handling, warnings, persistence handoff, and run-history behavior.
14. Documentation is updated for request shape, asynchronous lifecycle behavior, progress reporting, endpoint usage, validation rules, placeholder extractor limits, and manual verification guidance.
15. No Archon Discovery UI implementation is introduced.
16. No later extraction, query, MCP, markdown, or rule capability is marked as deferred without being assigned to its later existing work package in `docs/foundation/work-packages.md`.

## 12. Implementation Guidance

### 12.1 Expected Project Areas

Implementation derived from this specification is expected to work primarily in these areas, adjusted as needed to match the repository structure established by WP001 through WP003:

```text
src/
  Archon.Application/
	Extraction/
	  Requests/
	  Validation/
	  Resolution/
	  Runs/
	  Scheduling/
	  Pipeline/
	  Orchestration/
	  Snapshots/
  ArchonApi/
	Extraction/
	  Endpoints or Controllers
	  Contracts

test/
  Archon.Application.Tests/
	Extraction/
  ArchonApi.Tests/
	Extraction/
```

The exact folder names may be adjusted to match existing repository conventions. The architectural placement must remain unchanged: orchestration belongs in the application layer, HTTP translation belongs in the API host, and Neo4j implementation details remain outside inward layers.

### 12.2 Sequencing Guidance

A recommended implementation sequence is:

1. Add or align request, response, run lifecycle, validation, resolution, accumulation, stage, and snapshot assembly contracts.
2. Implement request validation and path normalization.
3. Implement run lifecycle, progress, and run-history abstractions.
4. Implement asynchronous extraction scheduling through an application-level abstraction.
5. Implement placeholder stage pipeline and shared accumulation behavior.
6. Implement snapshot assembly into the generalized contract.
7. Implement the orchestrator and persistence handoff.
8. Add API start, status, and history endpoints.
9. Add unit and API tests for validation, asynchronous scheduling, progress, lifecycle, orchestration, handoff, and run-history behavior.
10. Update developer and API documentation.

### 12.3 Technical Challenges and Decisions

| Area | Guidance |
| --- | --- |
| Long-running extraction | WP004 must use an asynchronous execution model from the start. The start endpoint should return quickly after validation, run creation, and scheduling, while status retrieval reports progress and terminal outcomes. |
| Path security | Default behavior should keep solution paths inside the submitted repository root to prevent accidental analysis outside the intended scope. |
| Placeholder output | Placeholder stages should prove orchestration without pretending that real extraction exists. |
| Run history storage | Run history is operational state and must not become a competing architecture graph store. |
| Error boundaries | Validation failures should differ from accepted-run failures so API callers understand whether a run was created. |
| Future stages | Stage contracts should be broad enough for later packages but not over-engineered with unneeded runtime scheduling features. |

## 13. Traceability Matrix

| Source Requirement | Covered By |
| --- | --- |
| `work-packages.md` WP004 objective | Sections 1, 3, 4, 11 |
| `work-packages.md` WP004 required implementation | Sections 4, 6, 7, 8 |
| `work-packages.md` WP004 completion criteria | Sections 8, 9, 11 |
| Source brief section 8.1 API Host and Extraction Module responsibilities | Sections 2, 3, 4, 6 |
| Source brief section 14.1 pipeline stages | Sections 3, 4.5, 4.6, 12 |
| Source brief Appendix E.5.6 API extraction contract | Sections 4.1, 4.7, 6, 7 |
| Source brief Appendix E.5.7 extraction pipeline | Sections 4.5, 4.6, 4.8, 12 |
| Source brief Appendix E.9 acceptance criteria | Sections 8, 11 |
| Documentation-pass requirement for internal and non-public types | Sections 5.5, 9 |

## 14. Open Questions for Implementation Planning

The following implementation-planning questions have been resolved and shall guide the WP004 implementation:

1. **Execution model**: WP004 shall build an asynchronous extraction execution model from the start. Extraction will be a long-running process, and it may become too difficult to retrofit asynchronous orchestration after synchronous assumptions are embedded. Progress reporting will be important, and even if the first progress model is simple, the implementation must include a mechanism that can keep API consumers informed about what extraction is doing.
2. **Run-history storage**: WP004 shall use an in-memory, replaceable application or infrastructure run-history store for the initial implementation. This keeps the run lifecycle testable and avoids making run history a competing graph persistence model. The abstraction shall remain clean so a durable store can replace it later.
3. **API route naming**: WP004 shall use resource-oriented extraction routes: `POST /extractions`, `GET /extractions/{runId}`, and `GET /extractions`. These routes avoid a common `/api` prefix and align with the repository guidance for WP005-style APIs.
4. **Default path policy**: WP004 shall allow absolute or relative solution paths only when the normalized final path is inside the submitted repository root. Paths outside the repository root shall be rejected by default. This supports explicit absolute paths such as `D:\Dev\Archon\Archon.slnx` when they normalize under the repository root while preserving the default security boundary.
5. **Metadata contract**: WP004 shall use the existing WP002 stable metadata or value-object contract discovered during implementation rather than creating a new metadata shape. If WP002 provides a general metadata dictionary or deterministic metadata representation, that model shall be reused for request metadata and run metadata.
6. **Snapshot identity exposed by status responses**: WP004 status responses shall expose the persisted snapshot stable key returned or confirmed by the WP003 persistence abstraction. Database identifiers shall not be exposed. If the adapter returns a richer persistence result, only the stable snapshot identity from that result shall be exposed while database or internal identifiers remain private.

## 15. Target Output Path

This specification is created at:

`docs/004-API-Extraction-Contract-and-Snapshot-Orchestration/spec-wp004-api-extraction-contract-and-snapshot-orchestration.md`
