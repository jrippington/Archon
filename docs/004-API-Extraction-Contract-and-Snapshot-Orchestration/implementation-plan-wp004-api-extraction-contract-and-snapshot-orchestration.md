# Implementation Plan - WP004 API Extraction Contract and Snapshot Orchestration

## Document Control

| Field | Value |
| --- | --- |
| Work Package | WP004 - API Extraction Contract and Snapshot Orchestration |
| Related Specification | `docs/004-API-Extraction-Contract-and-Snapshot-Orchestration/spec-wp004-api-extraction-contract-and-snapshot-orchestration.md` |
| Target Output Path | `docs/004-API-Extraction-Contract-and-Snapshot-Orchestration/implementation-plan-wp004-api-extraction-contract-and-snapshot-orchestration.md` |
| Plan Type | Single implementation plan document with architecture appendix |
| Planning Basis | `spec.plan.prompt.md`, `.github/instructions/wiki.instructions.md`, `.github/instructions/documentation-pass.instructions.md`, repository coding standards, and WP004 specification |
| Status | Draft |

## Planning Standards and Non-Negotiable Gates

This implementation plan is governed by the repository instructions in `.github/copilot-instructions.md`, `.github/instructions/wiki.instructions.md`, and `.github/instructions/documentation-pass.instructions.md`. The executor must treat those files as active requirements, not optional guidance.

For every Work Item below, once implementation starts, execution must continue uninterrupted through implementation, validation, documentation review, wiki review, and plan-record updates until that Work Item is complete. The executor must not stop for step announcements, progress-only handoffs, ordinary fixable failures, build failures, test failures, or confirmation prompts. The only allowed stops during an active Work Item are full Work Item completion, explicit user interruption or change of direction, or a true blocker that cannot be resolved from the specification, this plan, repository guidance, or the codebase.

Any Work Item that creates or updates source code must comply fully with `.github/instructions/documentation-pass.instructions.md`. This includes developer-level comments for every class, method, and constructor, including internal and other non-public types and members; parameter comments for every public method and constructor parameter; comments for every property whose purpose is not obvious from its name; and enough inline or block comments for a developer to understand the purpose, logical flow, and any algorithms used.

Every Work Item must include wiki review under `.github/instructions/wiki.instructions.md`. The executor must update the wiki when developer-facing behavior, architecture, workflows, terminology, setup, validation, or contributor guidance changes or is materially clarified. If no wiki update is needed for a slice, the executor must record the specific pages reviewed and why no update was required. Contributor-facing explanation must go to the correct `./wiki` topic page, not to standalone implementation notes, implementation ledgers, or `wiki/home.md` as a dumping ground.

## Overall Project Structure

The implementation should preserve the Onion Architecture dependency direction already established in the repository:

```text
Hosts/API -> Infrastructure -> Application/Services -> Domain
```

The exact project names must be confirmed from the existing solution before implementation, but WP004 work is expected to be organized around these repository areas:

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
  Archon.Infrastructure/
	Extraction/
	  Runs/
	  Scheduling/
  ArchonApi/
	Extraction/
	  Endpoints/
	  Contracts/

test/
  Archon.Application.Tests/
	Extraction/
  Archon.Infrastructure.Tests/
	Extraction/
  ArchonApi.Tests/
	Extraction/

wiki/
  <existing or new topic pages selected during wiki review>
```

Naming should follow current repository conventions discovered during implementation. C# source must use block-scoped namespaces, Allman braces, one public type per file, underscore-prefixed private fields, and no top-level statements. `.csproj` edits must keep `PackageReference` entries in `ItemGroup` blocks that contain only package references.

## Vertical Slice Strategy

WP004 must be delivered as incremental runnable slices rather than disconnected horizontal layers. Each Work Item below produces a demonstrable API or application capability that can be validated without running the Aspire AppHost. The first slice establishes the smallest useful end-to-end path: submit a valid extraction request, create a run, schedule asynchronous work through a seam, and retrieve status. Later slices deepen validation, background orchestration, snapshot assembly, persistence handoff, error handling, and documentation.

The term asynchronous execution means the API start endpoint validates and accepts work, creates or updates operational run state, schedules extraction work, and returns before extraction and persistence complete. The term run lifecycle means the operational status model that reports `Accepted`, `Queued`, `Running`, `Completed`, `Failed`, and optionally `Cancelled` states together with progress, warnings, errors, timestamps, and persisted snapshot identity.

## Work Items

## Slice 1 - Async Start and Status Foundation

- [ ] Work Item 1: Implement the minimal asynchronous extraction start and status path
  - **Purpose**: Establish the first runnable vertical slice for WP004 by allowing an API consumer to submit a valid extraction request, receive a run identifier quickly, and inspect the run state through the status endpoint without waiting for extraction or persistence to complete.
  - **Acceptance Criteria**:
	- `POST /extractions` accepts a valid `StartExtractionRequest` with repository root and explicit solution paths.
	- The request is validated enough to prevent missing repository root, empty solution list, non-existent repository root, non-existent solution file, invalid extension, outside-root path, and normalized duplicate path failures.
	- A run record is created with a stable run identifier, submitted request summary, `Accepted` or `Queued` status, started UTC, initial progress, warnings, and errors collections.
	- The API returns quickly after validation, run creation, and scheduling through an application-level scheduler abstraction.
	- `GET /extractions/{runId}` returns the current status for the created run.
	- Validation failures return client-error responses and do not create run records.
  - **Definition of Done**:
	- Code implemented across API contract, application command, validation, run store abstraction, in-memory run store, scheduler abstraction, and status endpoint.
	- Unit tests and API tests cover valid start, invalid start, no run creation on validation failure, and status retrieval.
	- Logging and error handling are added with credential-safe structured data and without exposing raw stack traces, secrets, or metadata values.
	- All source-code work follows `.github/instructions/documentation-pass.instructions.md` in full, including developer-level comments on public, internal, and non-public types, methods, constructors, and non-obvious properties.
	- Wiki review is completed under `.github/instructions/wiki.instructions.md`; relevant wiki or repository guidance is updated, or a specific no-change review result is recorded.
	- Foundational documentation uses book-like narrative depth where the slice changes architecture, runtime behavior, or workflow understanding; technical terms are defined on first use or linked to glossary guidance.
	- No standalone implementation notes, implementation ledgers, or architecture-note files are created for contributor-facing detail.
	- Can execute end-to-end via the API test host or project-level tests without starting Aspire AppHost.
	- Executor must not stop mid-Work Item; execution continues through implementation, validation, documentation/wiki review, and plan-record updates unless the Work Item is complete, the user explicitly interrupts, or a true blocker prevents further autonomous progress.
  - [ ] Task 1: Discover existing solution, project, API, persistence, metadata, and test conventions
	- [ ] Identify the active solution file and the established project names for application, infrastructure, API host, and tests.
	- [ ] Identify existing endpoint style, route registration, JSON serialization conventions, problem-details or validation response conventions, logging conventions, and dependency injection conventions.
	- [ ] Identify existing WP002 metadata and stable-key contracts and the WP003 snapshot persistence abstraction so new contracts align instead of duplicating existing models.
	- [ ] Identify relevant wiki pages for API, architecture, persistence, runtime, validation, glossary, and work-package workflow topics.
  - [ ] Task 2: Add or align request and response contracts
	- [ ] Create or align `StartExtractionRequest` with repository root, explicit solution paths, optional branch name, optional commit SHA, optional requested-by value, and repository-approved metadata representation.
	- [ ] Create start, status, and validation response contracts for `POST /extractions` and `GET /extractions/{runId}`.
	- [ ] Ensure contracts serialize using existing API host JSON conventions and do not expose infrastructure details.
	- [ ] Add XML and developer-level documentation required by `.github/instructions/documentation-pass.instructions.md`.
  - [ ] Task 3: Implement validation and resolution for the start path
	- [ ] Validate exactly one non-empty repository root directory value.
	- [ ] Validate at least one non-empty solution path.
	- [ ] Normalize absolute and relative solution paths, resolving relative paths against the submitted repository root.
	- [ ] Reject missing repository directories, missing solution files, non-`.sln` solution paths unless existing repository guidance explicitly supports another format, paths outside the repository root, and duplicate solution paths after normalization.
	- [ ] Preserve user-actionable validation errors without logging or echoing sensitive metadata values.
  - [ ] Task 4: Implement run lifecycle and in-memory run-history foundation
	- [ ] Define `ExtractionRunStatus` with `Accepted`, `Queued`, `Running`, `Completed`, `Failed`, and `Cancelled` if cancellation is implemented.
	- [ ] Define `ExtractionRun`, `ExtractionRunProgress`, `ExtractionRunWarning`, `ExtractionRunError`, and run summary contracts or align equivalent existing models.
	- [ ] Create a replaceable run-history abstraction supporting create, update, get by identifier, and recent-runs retrieval.
	- [ ] Implement an in-memory run-history store with deterministic ordering and test-safe behavior.
  - [ ] Task 5: Implement asynchronous scheduler seam
	- [ ] Define an application-level `ExtractionWorkScheduler` or equivalent abstraction for scheduling extraction outside the HTTP request.
	- [ ] Implement an initial in-process scheduling adapter suitable for tests and local API execution.
	- [ ] Ensure the API host delegates to the scheduler abstraction instead of embedding orchestration workflow logic.
	- [ ] Keep the abstraction replaceable by durable queues or distributed workers without changing the API contract.
  - [ ] Task 6: Add start and status endpoints
	- [ ] Map `POST /extractions` to request validation, run creation, scheduling, and quick response.
	- [ ] Map `GET /extractions/{runId}` to run-status retrieval.
	- [ ] Return not-found behavior for unknown run identifiers.
	- [ ] Ensure endpoint responses avoid raw exceptions, secrets, connection strings, environment variables, and infrastructure internals.
  - [ ] Task 7: Add tests for the first end-to-end slice
	- [ ] Add application tests for validation, path normalization, duplicate detection, and outside-root rejection.
	- [ ] Add application tests proving validation happens before scheduling.
	- [ ] Add API tests proving valid start returns a run identifier and invalid start returns validation errors without run creation.
	- [ ] Add status endpoint tests for existing and missing run identifiers.
  - [ ] Task 8: Perform documentation and wiki review for the slice
	- [ ] Apply `.github/instructions/documentation-pass.instructions.md` to all source code created or changed in this slice.
	- [ ] Review wiki topic pages selected during discovery and update them if the start/status async workflow changes contributor-facing architecture or API behavior.
	- [ ] Record pages reviewed, pages updated or intentionally unchanged, and page-structure decision in the Work Item completion record.
  - **Files**:
	- `src/Archon.Application/Extraction/Requests/*`: Start command and request-aligned application contracts.
	- `src/Archon.Application/Extraction/Validation/*`: Start request validation and path normalization.
	- `src/Archon.Application/Extraction/Resolution/*`: Resolved extraction input model.
	- `src/Archon.Application/Extraction/Runs/*`: Run lifecycle, progress, warnings, errors, summaries, and abstractions.
	- `src/Archon.Application/Extraction/Scheduling/*`: Scheduler abstraction.
	- `src/Archon.Infrastructure/Extraction/Runs/*`: In-memory run-history implementation if infrastructure is the existing home for replaceable adapters.
	- `src/Archon.Infrastructure/Extraction/Scheduling/*`: Initial in-process scheduler adapter if infrastructure is the existing home for background adapters.
	- `src/ArchonApi/Extraction/*`: API endpoints and HTTP response contracts.
	- `test/Archon.Application.Tests/Extraction/*`: Application validation and run lifecycle tests.
	- `test/ArchonApi.Tests/Extraction/*`: API endpoint tests.
	- `wiki/*`: Relevant topic pages selected by wiki review.
  - **Work Item Dependencies**: None beyond existing WP001-WP003 solution structure and persistence abstractions.
  - **Run / Verification Instructions**:
	- Run targeted application tests for extraction validation and run lifecycle.
	- Run targeted API tests for extraction start and status endpoints.
	- Run a project-level build for changed projects.
	- Do not start the Aspire AppHost.
  - **User Instructions**: None expected unless local test execution requires repository-specific environment variables already documented elsewhere.

## Slice 2 - Run History and Progress Reporting

- [ ] Work Item 2: Implement recent run history and observable progress reporting
  - **Purpose**: Make asynchronous extraction observable by exposing deterministic run history and progress updates that API consumers can poll while a run is queued, running, completed, or failed.
  - **Acceptance Criteria**:
	- `GET /extractions` returns recent extraction runs in deterministic newest-first order unless existing API conventions require a different order.
	- Run status includes current stage, progress message, optional percentage, progress last updated UTC, warnings, errors, timestamps, submitted request summary, and snapshot identity when available.
	- The run-history abstraction records submitted request context, warning count, error count, solution count, status, timestamps, and snapshot identity.
	- Progress updates can be written by asynchronous execution and read by API status/history endpoints.
	- Progress reporting does not expose sensitive metadata values or infrastructure internals.
  - **Definition of Done**:
	- Code implemented for run-history endpoint, progress model updates, deterministic run summaries, and API response shaping.
	- Tests pass for deterministic ordering, progress visibility, warning/error visibility, missing run behavior, and response redaction.
	- Logging and error handling are implemented with credential-safe structured data.
	- All source-code work follows `.github/instructions/documentation-pass.instructions.md` in full.
	- Wiki review is completed under `.github/instructions/wiki.instructions.md`; relevant async progress or API usage guidance is updated, or a specific no-change result is recorded.
	- Detailed contributor-facing explanation goes to the correct wiki topic page, not standalone implementation notes or `wiki/home.md`.
	- Can execute end-to-end by starting an extraction through tests or test host seams, polling status, and listing recent runs.
	- Executor must not stop mid-Work Item; execution continues through implementation, validation, documentation/wiki review, and plan-record updates unless the Work Item is complete, the user explicitly interrupts, or a true blocker prevents further autonomous progress.
  - [ ] Task 1: Complete run-history API contracts
	- [ ] Define or align `ExtractionRunSummary` response shape.
	- [ ] Include run identifier, status, timestamps, repository root summary, solution count, warning count, error count, and snapshot identity when available.
	- [ ] Add documentation comments and developer-level flow comments per `.github/instructions/documentation-pass.instructions.md`.
  - [ ] Task 2: Implement recent run retrieval
	- [ ] Add recent-runs query method to the application surface if not already present.
	- [ ] Implement deterministic newest-first ordering in the in-memory store.
	- [ ] Add optional limit or paging only if existing API conventions already support it cleanly.
  - [ ] Task 3: Implement progress update behavior
	- [ ] Add run-store update operations for stage name, message, optional percentage, and last updated UTC.
	- [ ] Ensure warning and error counts remain accurate as warnings and errors are appended.
	- [ ] Preserve monotonic lifecycle ordering so `Completed` cannot appear before persistence succeeds.
  - [ ] Task 4: Add history endpoint
	- [ ] Map `GET /extractions` to recent-run retrieval.
	- [ ] Shape the response according to existing API conventions.
	- [ ] Ensure the endpoint does not leak raw paths beyond the approved request summary format.
  - [ ] Task 5: Add tests
	- [ ] Test newest-first run ordering with deterministic timestamps.
	- [ ] Test progress updates are visible through status retrieval.
	- [ ] Test warning and error counts appear in status and history responses.
	- [ ] Test history responses avoid stack traces, secrets, and infrastructure internals.
  - [ ] Task 6: Perform documentation and wiki review for the slice
	- [ ] Review whether existing wiki API or runtime pages now need current-state explanation for polling-based progress reporting.
	- [ ] Update selected topic pages with narrative explanation and examples if progress reporting is contributor-facing.
	- [ ] Record pages reviewed, changed, created, or intentionally unchanged.
  - **Files**:
	- `src/Archon.Application/Extraction/Runs/*`: Progress and history query behavior.
	- `src/Archon.Infrastructure/Extraction/Runs/*`: In-memory deterministic history implementation.
	- `src/ArchonApi/Extraction/*`: History endpoint and response contracts.
	- `test/Archon.Application.Tests/Extraction/*`: Progress and history tests.
	- `test/ArchonApi.Tests/Extraction/*`: API history and status tests.
	- `wiki/*`: Relevant API/runtime topic pages selected by wiki review.
  - **Work Item Dependencies**: Work Item 1.
  - **Run / Verification Instructions**:
	- Run targeted application tests for run progress and history.
	- Run targeted API tests for `GET /extractions` and `GET /extractions/{runId}`.
	- Run a project-level build for changed projects.
	- Do not start the Aspire AppHost.
  - **User Instructions**: None expected.

## Slice 3 - Placeholder Pipeline and Snapshot Assembly

- [ ] Work Item 3: Implement the placeholder extraction pipeline and generalized snapshot assembly
  - **Purpose**: Prove the shared extraction pipeline and generalized snapshot contract without implementing real repository or Roslyn extraction. This slice creates the accumulation path that future extractor slices must use.
  - **Acceptance Criteria**:
	- The application layer defines a deterministic extraction stage abstraction with stable stage identifiers.
	- The shared accumulation model can collect nodes, edges, evidence, findings, metrics, generated summaries, warnings, and errors.
	- Placeholder stage behavior contributes only minimal no-op, repository, solution, snapshot, or warning data required to prove orchestration and persistence handoff.
	- Snapshot assembly produces a generalized `ExtractedArchitectureSnapshot` contract that includes repositories, solutions, snapshot header, nodes, edges, evidence, findings, metrics, generated summaries, warnings, and errors collections.
	- Empty collections are represented explicitly.
	- Snapshot assembly uses deterministic stable keys or fingerprints from existing WP002 components where available and does not create database IDs.
  - **Definition of Done**:
	- Code implemented for stage abstraction, accumulation model, placeholder stage, and snapshot assembler.
	- Tests pass for deterministic stage ordering, accumulation behavior, placeholder boundary, complete snapshot shape, empty collection representation, warnings retention, and no database IDs in stable identity.
	- Logging and error handling are added for stage and assembly failures.
	- All source-code work follows `.github/instructions/documentation-pass.instructions.md` in full.
	- Wiki review is completed; pipeline, accumulation, and placeholder-boundary guidance is updated in the correct topic page if developer-facing understanding changes.
	- Conceptually dense wiki content uses long-form narrative prose, defines terms such as extraction stage, accumulation model, and snapshot assembly, and includes examples or walkthroughs where useful.
	- Can execute end-to-end in tests by invoking the pipeline and assembling a snapshot from a valid resolved extraction input.
	- Executor must not stop mid-Work Item; execution continues through implementation, validation, documentation/wiki review, and plan-record updates unless the Work Item is complete, the user explicitly interrupts, or a true blocker prevents further autonomous progress.
  - [ ] Task 1: Define extraction stage and accumulation contracts
	- [ ] Create an extraction stage interface with stable stage name or identifier, asynchronous execution method, cancellation token support where existing conventions support it, and access to resolved input plus accumulation.
	- [ ] Create `ExtractionAccumulation` or equivalent with collections for all generalized snapshot contribution categories.
	- [ ] Add warning and error contribution behavior that distinguishes non-blocking warnings from blocking errors.
  - [ ] Task 2: Implement deterministic stage pipeline
	- [ ] Register stages in deterministic order using existing dependency injection conventions.
	- [ ] Execute stages sequentially for WP004 unless repository guidance already provides a deterministic parallel pattern.
	- [ ] Stop execution when a blocking stage error requires run failure.
	- [ ] Record current stage and progress message before and after stage execution.
  - [ ] Task 3: Implement placeholder stage behavior
	- [ ] Add only minimal placeholder contributions required to prove orchestration.
	- [ ] Avoid inventing architecture facts that belong to later work packages.
	- [ ] Ensure placeholder behavior is not documented or represented as final extractor capability.
  - [ ] Task 4: Implement snapshot assembly
	- [ ] Build a snapshot header associated with exactly one accepted extraction run.
	- [ ] Include repository identity from validated repository root and request metadata.
	- [ ] Include every submitted solution as a solution record.
	- [ ] Include all accumulated nodes, edges, evidence, findings, metrics, generated summaries, warnings, and errors.
	- [ ] Use existing stable-key and metadata contracts from WP002 where available.
  - [ ] Task 5: Add tests
	- [ ] Test stage ordering and stop-on-blocking-error behavior.
	- [ ] Test warnings remain non-blocking and are present in run state and snapshot output.
	- [ ] Test snapshot shape includes every required collection.
	- [ ] Test placeholder output remains minimal and does not represent future extraction features as complete.
  - [ ] Task 6: Perform documentation and wiki review for the slice
	- [ ] Review architecture and glossary wiki pages for extraction stage, accumulation model, snapshot assembly, and placeholder boundary terminology.
	- [ ] Update correct topic pages or create a new extraction orchestration topic page if the concepts do not fit existing pages.
	- [ ] Keep `wiki/home.md` concise and limited to orientation and links.
  - **Files**:
	- `src/Archon.Application/Extraction/Pipeline/*`: Stage abstraction, pipeline runner, accumulation model, placeholder stage.
	- `src/Archon.Application/Extraction/Snapshots/*`: Snapshot assembly contracts and implementation.
	- `src/Archon.Application/Extraction/Runs/*`: Progress and error updates used during pipeline execution.
	- `test/Archon.Application.Tests/Extraction/*`: Pipeline, accumulation, and snapshot assembly tests.
	- `wiki/*`: Selected architecture/glossary/extraction topic pages.
  - **Work Item Dependencies**: Work Items 1 and 2.
  - **Run / Verification Instructions**:
	- Run targeted application tests for pipeline and snapshot assembly.
	- Run project-level build for changed projects.
	- Do not start the Aspire AppHost.
  - **User Instructions**: None expected.

## Slice 4 - Async Orchestration and Persistence Handoff

- [ ] Work Item 4: Connect asynchronous scheduled work to orchestration and WP003 snapshot persistence
  - **Purpose**: Complete the runnable asynchronous extraction workflow by executing the pipeline in background work, assembling the generalized snapshot, handing it to the WP003 persistence abstraction, and updating run state to `Completed` or `Failed` based on persistence outcome.
  - **Acceptance Criteria**:
	- Scheduled extraction work transitions run state from `Queued` to `Running` and updates progress throughout validation, resolution, stage execution, assembly, and persistence handoff.
	- The orchestrator uses one application-layer path for validation, resolution, stage execution, snapshot assembly, persistence handoff, lifecycle updates, warning capture, and error capture.
	- The orchestrator does not depend directly on Neo4j driver types, ASP.NET Core controller types, or host-specific objects.
	- Persistence receives the complete generalized snapshot contract, not a project-only projection.
	- Persistence success records `Completed`, completed UTC, and persisted snapshot stable key or equivalent stable identity.
	- Persistence failure records `Failed`, completed UTC, failure stage, and user-actionable error without reporting success early.
	- Asynchronous exceptions are converted to controlled run failures visible through status retrieval.
  - **Definition of Done**:
	- Code implemented for orchestrator, scheduled-work handler, persistence handoff integration, terminal status updates, and controlled failure handling.
	- Tests pass for orchestration order, progress transitions, warning retention, stage failure, assembly failure, persistence success, persistence failure, full snapshot handoff, and no persistence on validation failure.
	- Logging and error handling are implemented with credential-safe structured data.
	- All source-code work follows `.github/instructions/documentation-pass.instructions.md` in full.
	- Wiki review is completed; asynchronous orchestration, persistence handoff, and run lifecycle guidance is updated where contributor-facing.
	- Wiki page-structure review confirms the selected topic page is correct, whether a new page is needed, whether `wiki/home.md` remains concise, and whether cross-links or glossary entries are sufficient.
	- Can execute end-to-end in tests by starting extraction, draining or invoking scheduled work through a test seam, observing progress, and confirming completed or failed status.
	- Executor must not stop mid-Work Item; execution continues through implementation, validation, documentation/wiki review, and plan-record updates unless the Work Item is complete, the user explicitly interrupts, or a true blocker prevents further autonomous progress.
  - [ ] Task 1: Implement orchestration service
	- [ ] Create the application orchestrator with dependencies on validator, resolver, stage pipeline, snapshot assembler, snapshot persistence abstraction, run-history abstraction, and logger.
	- [ ] Ensure validation precedes resolution, resolution precedes stages, stages precede assembly, assembly precedes persistence, and completion follows persistence success.
	- [ ] Update run progress at each major stage.
  - [ ] Task 2: Connect scheduler to orchestrator
	- [ ] Ensure scheduled work calls the orchestrator with the accepted run identifier and resolved command context.
	- [ ] Preserve cancellation token support where existing conventions support cancellation.
	- [ ] Ensure scheduling failures are reported as controlled failures or client errors depending on whether a run was accepted.
  - [ ] Task 3: Integrate WP003 persistence abstraction
	- [ ] Use the existing application-layer snapshot persistence abstraction implemented by Neo4j infrastructure.
	- [ ] Avoid direct references to Neo4j driver types in application or API host code.
	- [ ] Capture the persisted snapshot stable key or equivalent stable identity returned or confirmed by the adapter.
  - [ ] Task 4: Implement controlled failure behavior
	- [ ] Convert unexpected exceptions into run errors with stage/category data.
	- [ ] Avoid raw stack traces in API responses.
	- [ ] Preserve enough error detail for developers and tests to identify failing stage.
  - [ ] Task 5: Add orchestration and persistence tests
	- [ ] Use test doubles for stage pipeline, assembler, and persistence to prove exact orchestration order.
	- [ ] Verify persistence receives the full generalized snapshot contract once on success.
	- [ ] Verify persistence is not invoked for validation failures.
	- [ ] Verify persistence failure results in failed status and visible error.
	- [ ] Verify status never reports `Completed` before persistence success.
  - [ ] Task 6: Perform documentation and wiki review for the slice
	- [ ] Update or create the correct wiki topic page for extraction orchestration if this slice introduces developer-facing workflow knowledge.
	- [ ] Explain the current asynchronous sequence in narrative form and include a walkthrough of a successful and failed run if materially useful.
	- [ ] Define technical terms such as persistence handoff, stable snapshot identity, and run lifecycle if not already defined or linked.
  - **Files**:
	- `src/Archon.Application/Extraction/Orchestration/*`: Orchestrator implementation and interfaces.
	- `src/Archon.Application/Extraction/Scheduling/*`: Scheduled work contract and handler.
	- `src/Archon.Application/Extraction/Snapshots/*`: Persistence handoff models if needed.
	- `src/Archon.Infrastructure/Extraction/Scheduling/*`: In-process background scheduling adapter.
	- `test/Archon.Application.Tests/Extraction/*`: Orchestration and persistence handoff tests.
	- `test/Archon.Infrastructure.Tests/Extraction/*`: Scheduler adapter tests if infrastructure tests exist.
	- `wiki/*`: Selected orchestration/persistence/runtime topic pages.
  - **Work Item Dependencies**: Work Items 1, 2, and 3.
  - **Run / Verification Instructions**:
	- Run targeted application tests for orchestration and persistence handoff.
	- Run targeted infrastructure tests for scheduling if added.
	- Run targeted API tests confirming terminal status visibility through status endpoint.
	- Run project-level build for changed projects.
	- Do not start the Aspire AppHost.
  - **User Instructions**: None expected. Real Neo4j credentials should not be required for orchestration tests because persistence can be replaced by test doubles.

## Slice 5 - API Contract Hardening and Manual Verification Documentation

- [ ] Work Item 5: Harden API behavior and document manual verification flows
  - **Purpose**: Ensure the HTTP surface is stable, secure, and understandable for manual testing, while preserving the asynchronous execution contract and avoiding accidental leakage of sensitive data.
  - **Acceptance Criteria**:
	- `POST /extractions`, `GET /extractions/{runId}`, and `GET /extractions` use the resolved route names: `POST /extractions`, `GET /extractions/{runId}`, and `GET /extractions`.
	- Responses use existing API host conventions for success, validation failure, not found, and runtime failure surfaces.
	- Validation errors are actionable and do not include sensitive metadata values.
	- Runtime failures expose controlled error details through run status without raw stack traces, secrets, connection strings, environment variables, or infrastructure internals.
	- Manual verification documentation includes non-sensitive sample paths and explicitly states automated validation must not start Aspire AppHost.
  - **Definition of Done**:
	- API contracts and endpoint behavior are hardened against the WP004 functional and non-functional requirements.
	- API tests pass for success responses, validation responses, not found responses, failure redaction, status progress, and history summaries.
	- Documentation and examples are updated in the correct repository location.
	- All source-code work follows `.github/instructions/documentation-pass.instructions.md` in full.
	- Wiki review is completed; API usage, manual verification, and troubleshooting guidance is updated in topic pages where contributor-facing.
	- Manual verification explanation uses narrative prose and examples where useful rather than terse bullet-only treatment for dense workflows.
	- Can execute end-to-end via API tests or a local test host path without starting Aspire AppHost for automated validation.
	- Executor must not stop mid-Work Item; execution continues through implementation, validation, documentation/wiki review, and plan-record updates unless the Work Item is complete, the user explicitly interrupts, or a true blocker prevents further autonomous progress.
  - [ ] Task 1: Confirm endpoint route and response conventions
	- [ ] Ensure there is no common `/api` prefix unless existing host routing infrastructure requires it and the deviation is documented.
	- [ ] Confirm response status codes match existing repository API conventions.
	- [ ] Ensure validation failures do not create run records.
  - [ ] Task 2: Harden redaction and error responses
	- [ ] Review all validation, scheduling, orchestration, and persistence errors returned through API responses.
	- [ ] Remove or transform raw exception details, stack traces, secrets, connection strings, environment variables, and sensitive metadata values.
	- [ ] Preserve useful error categories, failure stage, and user-actionable messages.
  - [ ] Task 3: Add API contract tests
	- [ ] Test exact route behavior for start, status, and history.
	- [ ] Test valid request returns run identifier and current status.
	- [ ] Test invalid payloads return validation responses.
	- [ ] Test not-found status retrieval.
	- [ ] Test failure responses do not expose stack traces or secrets.
  - [ ] Task 4: Add manual verification documentation
	- [ ] Document example `POST /extractions` request with non-sensitive sample paths.
	- [ ] Document status polling and history retrieval examples.
	- [ ] Document expected lifecycle transitions and progress fields.
	- [ ] State that automated validation must not start Aspire AppHost; include manual Aspire verification only if relevant files are touched.
  - [ ] Task 5: Perform documentation and wiki review for the slice
	- [ ] Review API, setup, runtime, and troubleshooting wiki pages for required updates.
	- [ ] Update topic pages with manual verification examples if API workflow is contributor-facing.
	- [ ] Confirm `wiki/home.md` remains a landing page and link hub only.
  - **Files**:
	- `src/ArchonApi/Extraction/*`: Endpoint route and response behavior.
	- `test/ArchonApi.Tests/Extraction/*`: API contract hardening tests.
	- `wiki/*`: Selected API/manual verification/troubleshooting pages.
	- `docs/004-API-Extraction-Contract-and-Snapshot-Orchestration/*`: Concise plan status updates only, if needed; contributor-facing guidance belongs in wiki.
  - **Work Item Dependencies**: Work Items 1 through 4.
  - **Run / Verification Instructions**:
	- Run targeted API tests.
	- Run project-level build for changed API and test projects.
	- Do not start Aspire AppHost during automated validation.
  - **User Instructions**: If manual Aspire verification is documented, the user may run it separately after automated validation; the implementation executor must not start AppHost in automated runs.

## Slice 6 - Validation Completion and Regression Coverage

- [ ] Work Item 6: Complete validation, test coverage, and regression safety for WP004
  - **Purpose**: Close remaining WP004 acceptance criteria by ensuring validation, orchestration, progress, persistence handoff, run history, and documentation behavior are covered by focused automated tests without running the full test suite or Aspire AppHost.
  - **Acceptance Criteria**:
	- Tests cover all WP004 validation requirements TR-001 through TR-029 from the specification.
	- Tests prove validation happens before any stage execution, solution loading, project loading, or persistence attempt.
	- Tests prove accepted-run failures are visible through status retrieval.
	- Tests prove run history remains deterministic.
	- Tests prove snapshot persistence receives the full generalized contract and records returned stable snapshot identity.
	- Validation commands are documented in the plan completion record with outcomes.
  - **Definition of Done**:
	- Focused unit, integration, and API tests pass for changed WP004 areas.
	- Project-level builds pass for changed production and test projects.
	- Existing unrelated failures, if any, are documented with evidence and not hidden.
	- Full test suite is not run for this work package unless the user explicitly requests it, consistent with repository guidance.
	- All source-code work follows `.github/instructions/documentation-pass.instructions.md` in full.
	- Wiki review is completed; validation workflow or troubleshooting updates are made if contributor-facing behavior changed.
	- Can verify all WP004 acceptance criteria through targeted test commands and API-level tests without Aspire AppHost.
	- Executor must not stop mid-Work Item; execution continues through implementation, validation, documentation/wiki review, and plan-record updates unless the Work Item is complete, the user explicitly interrupts, or a true blocker prevents further autonomous progress.
  - [ ] Task 1: Map specification requirements to tests
	- [ ] Create a lightweight traceability checklist inside this implementation plan or update existing plan status entries without duplicating wiki guidance.
	- [ ] Ensure every test requirement in specification sections 8.1 through 8.4 has corresponding test coverage or a documented rationale.
  - [ ] Task 2: Add missing application tests
	- [ ] Cover missing validation, resolution, lifecycle, progress, pipeline, assembly, orchestration, and persistence handoff behavior.
	- [ ] Use test doubles for persistence and stages where real Neo4j behavior is not required.
	- [ ] Avoid test patterns that require Aspire AppHost.
  - [ ] Task 3: Add missing API tests
	- [ ] Cover start, status, history, validation, not-found, progress, snapshot identity, and redaction behavior.
	- [ ] Prefer existing API integration test infrastructure and avoid new test frameworks unless necessary.
  - [ ] Task 4: Run focused validation
	- [ ] Build changed production and test projects.
	- [ ] Run targeted tests for application, infrastructure, and API areas touched by WP004.
	- [ ] Record exact commands and outcomes in the plan completion/status section.
  - [ ] Task 5: Perform documentation and wiki review for validation workflows
	- [ ] Review whether wiki validation or troubleshooting pages need updates based on new commands, test seams, or failure modes.
	- [ ] Update the correct topic pages if needed and include walkthrough detail where it materially improves contributor understanding.
  - **Files**:
	- `test/Archon.Application.Tests/Extraction/*`: Validation, orchestration, pipeline, assembly, and persistence tests.
	- `test/Archon.Infrastructure.Tests/Extraction/*`: Scheduler/run-store adapter tests if needed.
	- `test/ArchonApi.Tests/Extraction/*`: API contract tests.
	- `docs/004-API-Extraction-Contract-and-Snapshot-Orchestration/implementation-plan-wp004-api-extraction-contract-and-snapshot-orchestration.md`: Concise validation outcome updates.
	- `wiki/*`: Selected validation/troubleshooting topic pages if needed.
  - **Work Item Dependencies**: Work Items 1 through 5.
  - **Run / Verification Instructions**:
	- Run focused test commands for changed test projects.
	- Run project-level builds for changed production and test projects.
	- Do not run the full test suite unless explicitly requested.
	- Do not start Aspire AppHost.
  - **User Instructions**: None expected.

## Slice 7 - Final Documentation and Wiki Completion Gate

- [ ] Work Item 7: Complete mandatory documentation pass and final wiki impact review
  - **Purpose**: Ensure WP004 is complete as a repository work package by closing source-code documentation requirements, updating current-state wiki guidance, retiring any improper implementation-note-style artifacts, and recording an explicit wiki impact matrix.
  - **Acceptance Criteria**:
	- Every source file created or changed for WP004 has been reviewed against `.github/instructions/documentation-pass.instructions.md`.
	- Public, internal, and other non-public types, constructors, methods, parameters, and non-obvious properties meet the required developer-level documentation standard.
	- Wiki review has identified affected concepts, selected correct topic pages, determined whether new pages are needed, avoided `wiki/home.md` dumping, and reviewed cross-links and glossary coverage.
	- Contributor-facing architecture, runtime, async orchestration, extraction workflow, validation, API usage, persistence handoff, and progress-reporting guidance is updated in the wiki where needed.
	- The final work-package record includes a wiki impact matrix or equivalent prose covering affected concepts, pages reviewed, pages updated, pages created, pages intentionally unchanged, and the page-structure decision.
  - **Definition of Done**:
	- Documentation-pass review is complete and any missing source comments are added without changing behavior.
	- Wiki maintenance is complete under `.github/instructions/wiki.instructions.md`.
	- Any stale standalone implementation notes, implementation ledgers, architecture notes, or similar contributor-facing substitute artifacts found in scope are retired after current information is moved to the wiki.
	- Final plan status records validation commands, outcomes, wiki impact matrix, pages updated or unchanged, and links to current wiki guidance without duplicating contributor-facing detail.
	- Foundational wiki content is written in book-like narrative prose, defines technical terms on first use or links to glossary entries, and includes examples or walkthroughs where useful.
	- `wiki/home.md` remains concise as a landing page and table of contents.
	- Executor must not stop mid-Work Item; execution continues through documentation, wiki review, validation, and plan-record updates unless the Work Item is complete, the user explicitly interrupts, or a true blocker prevents further autonomous progress.
  - [ ] Task 1: Perform final source documentation pass
	- [ ] Inspect every hand-maintained `.cs` file changed by WP004.
	- [ ] Add or improve XML comments for public APIs and developer-level comments for internal and non-public types, constructors, methods, and non-obvious properties.
	- [ ] Preserve behavior and avoid formatting-only cleanup unrelated to comments.
  - [ ] Task 2: Perform final wiki information-architecture review
	- [ ] Identify all affected concepts: asynchronous extraction, run lifecycle, progress reporting, API extraction endpoints, request validation, snapshot assembly, placeholder extractor boundary, persistence handoff, run history, and validation workflow.
	- [ ] Review existing wiki topic pages and glossary pages that should own those concepts.
	- [ ] Decide whether a new extraction orchestration page is needed or whether existing architecture/API/persistence pages are the correct homes.
	- [ ] Confirm `wiki/home.md` remains concise and is not used as a catch-all page.
	- [ ] Confirm cross-links and glossary entries are sufficient.
  - [ ] Task 3: Update wiki pages where required
	- [ ] Write current-state guidance in narrative prose for conceptually dense topics.
	- [ ] Define technical terms when first introduced or link to glossary entries.
	- [ ] Add examples or walkthroughs for start/status/history usage and successful/failed asynchronous extraction where useful.
	- [ ] Remove or rewrite stale phase-oriented wording that conflicts with current behavior.
  - [ ] Task 4: Retire improper substitute artifacts if found
	- [ ] Search the WP004 scope for implementation-note-style files that duplicate contributor-facing wiki content.
	- [ ] Move still-current guidance to the correct wiki topic page.
	- [ ] Remove or retire stale substitute artifacts as appropriate under repository rules.
  - [ ] Task 5: Record final wiki impact matrix and validation outcomes
	- [ ] Record affected concepts.
	- [ ] Record pages reviewed.
	- [ ] Record pages updated, created, retired, or intentionally unchanged.
	- [ ] Record the page-structure decision and why the selected structure remains readable.
	- [ ] Record validation commands and outcomes.
  - **Files**:
	- `src/**/*.cs` scoped to WP004 changes: Documentation-pass corrections only.
	- `test/**/*.cs` scoped to WP004 changes: Test documentation-pass corrections only.
	- `wiki/*`: Current-state contributor guidance pages selected by review.
	- `docs/004-API-Extraction-Contract-and-Snapshot-Orchestration/implementation-plan-wp004-api-extraction-contract-and-snapshot-orchestration.md`: Final validation and wiki impact record.
  - **Work Item Dependencies**: Work Items 1 through 6.
  - **Run / Verification Instructions**:
	- Run a build for all changed projects after documentation-only source comment changes.
	- Run focused tests affected by documentation-only changes if required by local workflow; documentation-pass instruction requires validation to include build and test execution after documentation-only source updates.
	- Do not start Aspire AppHost.
  - **User Instructions**: None expected.

## Cross-Cutting Requirements for Every Work Item

- Preserve Onion Architecture dependency direction.
- Keep API host code as HTTP translation and composition only; orchestration belongs in the application layer.
- Keep Neo4j as the system of record for extraction output; run history is operational state and must not become a competing architecture graph store.
- Use the existing WP002 metadata and stable-key/value-object contracts when discovered; do not invent a new metadata shape unless implementation discovery proves no suitable existing contract exists.
- Expose persisted snapshot stable key or equivalent stable identity through status responses; do not expose database IDs.
- Preserve absolute and relative solution path support only when normalized final paths remain inside the submitted repository root.
- Do not implement real Roslyn, runtime, data-access, UI, markdown, MCP, rule, or full repository extraction behavior in WP004.
- Do not run the full test suite for this work package unless explicitly requested.
- Do not run Aspire AppHost during automated validation.
- Do not create standalone implementation notes, implementation ledgers, or architecture-note files for contributor-facing detail.
- Do not put detailed contributor-facing guidance in `wiki/home.md`; use appropriate topic pages and keep `home.md` as orientation and links.

## Appendix A - Architecture

### Overall Technical Approach

WP004 introduces an asynchronous API-driven extraction workflow for Archon. The API host receives a start request, translates it into an application command, validates and normalizes repository and solution paths, creates operational run state, schedules extraction work, and returns a run identifier before extraction completes. Background execution then invokes the application orchestrator, which runs the shared extraction pipeline, assembles a generalized architecture snapshot, persists it through the existing WP003 snapshot persistence abstraction, and updates run status.

The implementation should keep the first asynchronous mechanism deliberately simple and replaceable. An in-process scheduler and in-memory run store are acceptable for WP004 because they prove the API contract, lifecycle, progress, and orchestration behavior without introducing distributed infrastructure prematurely. The scheduler abstraction must remain stable enough that later work can replace the implementation with a durable queue or distributed worker without changing `POST /extractions`, `GET /extractions/{runId}`, or `GET /extractions`.

The following diagram summarizes the intended flow:

```mermaid
flowchart TD
	Client[API Consumer] -->|POST /extractions| ApiStart[API Start Endpoint]
	ApiStart --> Validator[Request Validation]
	Validator --> Resolver[Repository and Solution Resolution]
	Resolver --> RunStore[Run Lifecycle Store]
	RunStore --> Scheduler[Extraction Work Scheduler]
	Scheduler -->|returns quickly| ApiStart
	ApiStart -->|run id and current status| Client

	Scheduler --> Worker[Background Extraction Work]
	Worker --> Orchestrator[Application Orchestrator]
	Orchestrator --> Pipeline[Extraction Stage Pipeline]
	Pipeline --> Accumulation[Shared Accumulation Model]
	Accumulation --> Assembler[Snapshot Assembly]
	Assembler --> Persistence[WP003 Snapshot Persistence Abstraction]
	Persistence --> Neo4j[(Neo4j System of Record)]
	Orchestrator --> RunStore

	Client -->|GET /extractions/{runId}| ApiStatus[API Status Endpoint]
	ApiStatus --> RunStore
	Client -->|GET /extractions| ApiHistory[API History Endpoint]
	ApiHistory --> RunStore
```

A run starts in `Accepted` or `Queued`, moves to `Running` when background extraction begins, and ends in `Completed` only after snapshot persistence succeeds. Any validation failure before acceptance returns a client-error response and does not create a run. Any accepted-run failure during scheduling, orchestration, stage execution, assembly, or persistence becomes a controlled `Failed` run that is visible through the status endpoint.

### Frontend

WP004 does not introduce the Archon Discovery UI or any frontend pages. The consumer surface for this work package is the HTTP API. Future UI work can use the same API contract by starting extraction through `POST /extractions`, polling `GET /extractions/{runId}` for progress, and using `GET /extractions` for recent run history.

Although there is no frontend implementation in this work package, the API responses should be shaped with future UI needs in mind. The status response should provide a stable run identifier, lifecycle state, current stage, progress message, optional percentage, last updated UTC, warnings, errors, and snapshot identity when available. This gives a future UI enough information to show a progress panel without needing direct access to background worker internals.

### Backend

The backend is split into API, application, and infrastructure responsibilities. The API host owns HTTP route mapping, request/response translation, status code behavior, and dependency injection composition. It must not contain extraction workflow logic. The application layer owns validation, resolution, orchestration, pipeline contracts, accumulation, snapshot assembly, run lifecycle abstractions, scheduling abstractions, and persistence abstractions. Infrastructure owns replaceable implementations such as the in-memory run-history store, in-process scheduler, and Neo4j persistence adapter already introduced by WP003.

The run lifecycle store is operational state, not the system of record for architecture knowledge. Neo4j remains the system of record for persisted extraction output. The run store exists so API consumers can understand what happened to an asynchronous run, including request summary, status transitions, progress, warnings, errors, completed UTC, and stable snapshot identity.

The extraction stage pipeline is the extension point for later extractor packages. A stage is a deterministic unit of extraction work with a stable name that receives the resolved extraction input and shared accumulation model. The accumulation model is the temporary in-memory collection of candidate snapshot facts gathered during a run. Snapshot assembly turns that accumulation plus request context into a generalized `ExtractedArchitectureSnapshot` suitable for persistence.

### Data and Persistence Flow

The data flow begins with submitted request values and ends with a persisted generalized snapshot. Request values are preserved sufficiently for audit and troubleshooting, while normalized paths are used for execution. Relative solution paths resolve against the submitted repository root. Absolute and relative solution paths are accepted only if the normalized final path remains inside the repository root.

During extraction, stages contribute warnings, errors, and placeholder facts to the accumulation model. WP004 must not invent real architecture facts that belong to later extraction packages. Snapshot assembly creates repository and solution records from the validated input, includes all required generalized snapshot collections, and preserves deterministic stable identity. Persistence receives the complete generalized snapshot through the WP003 abstraction and returns or confirms the stable snapshot identity that status responses expose.

### Testing Approach

Testing should be focused and layered. Application tests cover validation, resolution, lifecycle, scheduler seams, pipeline ordering, snapshot assembly, orchestration order, warning/error handling, and persistence handoff with test doubles. API tests cover route behavior, request/response shape, validation failures, not-found behavior, status progress, history summaries, and redaction. Infrastructure tests cover in-memory run store and in-process scheduler behavior if those adapters are non-trivial.

Automated validation must not start Aspire AppHost because it blocks the executing agent. The full test suite should not be run for this work package unless explicitly requested. Focused project-level builds and targeted tests are sufficient when they cover changed production and test projects.

## Final Wiki Impact Matrix Template

The final Work Item must update this section or an equivalent completion record before WP004 is considered complete.

| Affected Concept | Pages Reviewed | Pages Updated | Pages Created | Pages Intentionally Unchanged | Page-Structure Decision |
| --- | --- | --- | --- | --- | --- |
| Asynchronous extraction execution | To be recorded during execution | To be recorded during execution | To be recorded during execution | To be recorded during execution | To be recorded during execution |
| Run lifecycle and progress reporting | To be recorded during execution | To be recorded during execution | To be recorded during execution | To be recorded during execution | To be recorded during execution |
| Extraction API contract | To be recorded during execution | To be recorded during execution | To be recorded during execution | To be recorded during execution | To be recorded during execution |
| Validation and path policy | To be recorded during execution | To be recorded during execution | To be recorded during execution | To be recorded during execution | To be recorded during execution |
| Snapshot assembly and persistence handoff | To be recorded during execution | To be recorded during execution | To be recorded during execution | To be recorded during execution | To be recorded during execution |
| Placeholder extractor boundary | To be recorded during execution | To be recorded during execution | To be recorded during execution | To be recorded during execution | To be recorded during execution |

## Summary

This plan delivers WP004 through seven vertical slices. The first slice proves the minimal asynchronous start and status path. Subsequent slices add run history, progress reporting, the placeholder pipeline, generalized snapshot assembly, persistence handoff, API hardening, validation coverage, and final documentation/wiki completion. The key implementation consideration is to make the asynchronous model real from the start while keeping the initial scheduler and run store simple, in-process, testable, and replaceable. The API contract must remain stable, Neo4j must remain the system of record for extraction output, and every coding slice must satisfy both the mandatory documentation-pass standard and the mandatory wiki-maintenance workflow.
