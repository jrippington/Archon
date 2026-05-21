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

- [x] Work Item 1: Implement the minimal asynchronous extraction start and status path - Completed
  - **Completion Summary**: Implemented the first WP004 vertical slice across `src/Archon.Application`, `src/Archon.Api.Extraction`, and `src/ArchonApi`. The slice adds `StartExtractionRequest`, validation and resolution, run lifecycle models, replaceable in-memory run history, scheduler abstraction, no-op initial scheduler, application start/status service, `POST /extractions`, `GET /extractions/{runId}`, API response contracts, host registration, focused application tests, focused extraction API tests, and updated host tests. A pre-existing `SnapshotPersistenceCounts` constructor mismatch in `src/Archon.Infrastructure.Neo4j/Persistence/Neo4jArchitectureSnapshotWriter.cs` was fixed because it blocked changed host/test builds.
  - **Validation Summary**: `dotnet restore D:\Dev\Archon\Archon.slnx` succeeded; `dotnet build D:\Dev\Archon\src\Archon.Application\Archon.Application.csproj --no-restore` succeeded; `dotnet build D:\Dev\Archon\src\Archon.Api.Extraction\Archon.Api.Extraction.csproj --no-restore` succeeded; `dotnet build D:\Dev\Archon\src\Archon.Infrastructure.Neo4j\Archon.Infrastructure.Neo4j.csproj --no-restore` succeeded; `dotnet build D:\Dev\Archon\src\ArchonApi\ArchonApi.csproj --no-restore` succeeded; `dotnet build D:\Dev\Archon\test\Archon.Application.Tests\Archon.Application.Tests.csproj --no-restore` succeeded; `dotnet build D:\Dev\Archon\test\Archon.Api.Extraction.Tests\Archon.Api.Extraction.Tests.csproj --no-restore` succeeded; `dotnet build D:\Dev\Archon\test\ArchonApi.Tests\ArchonApi.Tests.csproj --no-restore` succeeded; `dotnet test D:\Dev\Archon\test\Archon.Application.Tests\Archon.Application.Tests.csproj --no-build --filter FullyQualifiedName~StartExtractionApplicationServiceTests` passed 10/10; `dotnet test D:\Dev\Archon\test\Archon.Api.Extraction.Tests\Archon.Api.Extraction.Tests.csproj --no-build --filter FullyQualifiedName~ExtractionEndpointTests` passed 4/4; `dotnet test D:\Dev\Archon\test\ArchonApi.Tests\ArchonApi.Tests.csproj --no-build --filter FullyQualifiedName~ArchonApiHealthEndpointTests` passed 2/2. Aspire AppHost was not started.
  - **Documentation and Wiki Review Result**: Source documentation pass completed for the changed hand-maintained C# files. Wiki review updated current-state contributor guidance by creating `wiki/api-extraction-workflow.md`, updating `wiki/home.md`, `wiki/runtime-foundation.md`, `wiki/validation-and-test-workflows.md`, and `wiki/glossary.md`. Page-structure decision: a new dedicated API extraction workflow page was required because asynchronous start/status behavior, validation boundaries, run lifecycle, and scheduler/run-history seams are conceptually dense and did not fit cleanly on existing runtime or persistence pages; `wiki/home.md` remains a concise landing page and link hub.
  - **Wiki Impact Matrix**:

	| Affected Concept | Pages Reviewed | Pages Updated | Pages Created | Pages Intentionally Unchanged | Page-Structure Decision |
	| --- | --- | --- | --- | --- | --- |
	| Asynchronous extraction start/status | `wiki/home.md`, `wiki/runtime-foundation.md`, `wiki/solution-architecture.md`, `wiki/validation-and-test-workflows.md`, `wiki/neo4j-persistence-foundation.md`, `wiki/glossary.md` | `wiki/home.md`, `wiki/runtime-foundation.md`, `wiki/validation-and-test-workflows.md`, `wiki/glossary.md` | `wiki/api-extraction-workflow.md` | `wiki/solution-architecture.md`, `wiki/neo4j-persistence-foundation.md` | New dedicated page owns detailed workflow explanation; home stays concise; runtime links to workflow; persistence remains focused on Neo4j system-of-record behavior. |
	| Validation and path policy | `wiki/validation-and-test-workflows.md`, `wiki/glossary.md` | `wiki/validation-and-test-workflows.md`, `wiki/glossary.md` | `wiki/api-extraction-workflow.md` | None | Validation policy is explained in the workflow page and commands are recorded on the validation page. |
	| Run lifecycle and scheduler seam | `wiki/runtime-foundation.md`, `wiki/glossary.md` | `wiki/runtime-foundation.md`, `wiki/glossary.md` | `wiki/api-extraction-workflow.md` | None | Workflow page defines lifecycle and seam responsibilities; glossary adds lookup terms. |
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
	- [x] Task 1: Discover existing solution, project, API, persistence, metadata, and test conventions - Completed. Active solution/project structure, marker-only extraction API module, health-probe API host, WP002 snapshot contracts, WP003 persistence ports, test style, and wiki pages were reviewed.
  - [x] Task 2: Add or align request and response contracts - Completed. Application request/result contracts and API start/status response contracts were added with documentation-pass comments.
  - [x] Task 3: Implement validation and resolution for the start path - Completed. Repository root, solution list, existence, `.sln` extension, inside-root containment, relative path resolution, normalization, and duplicate checks were implemented with safe validation messages.
  - [x] Task 4: Implement run lifecycle and in-memory run-history foundation - Completed. Run status, run identifiers, progress, warning/error models, request summary, run history abstraction, and deterministic in-memory store were implemented.
  - [x] Task 5: Implement asynchronous scheduler seam - Completed. `IExtractionWorkScheduler` and the initial no-op scheduler were added and used by the application service.
  - [x] Task 6: Add start and status endpoints - Completed. `POST /extractions` and `GET /extractions/{runId}` were mapped in the extraction API module and registered by `ArchonApi`.
  - [x] Task 7: Add tests for the first end-to-end slice - Completed. Application and API tests cover valid start, invalid start, validation before run creation/scheduling, path normalization failures, status retrieval, and not-found behavior.
  - [x] Task 8: Perform documentation and wiki review for the slice - Completed. Documentation-pass review and wiki impact review were completed; wiki updates are recorded above.
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

- [x] Work Item 2: Implement recent run history and observable progress reporting - Completed
  - **Completion Summary**: Implemented recent run history and progress reporting across `src/Archon.Application` and `src/Archon.Api.Extraction`. The application service now exposes recent runs through `GetRecentRunsAsync` and a progress update seam through `UpdateRunProgressAsync`, while `ExtractionRun` can immutably append warning and error diagnostics. The API module now maps `GET /extractions`, returns compact `ExtractionRunHistoryResponse` and `ExtractionRunSummaryResponse` contracts, reports run identifier, status, timestamps, repository root summary, solution count, warning count, error count, and snapshot identity, and supports a bounded optional `limit` query parameter. Focused application and API tests cover newest-first ordering, progress/warning/error visibility, missing-run progress updates, history summaries, and limit behavior.
  - **Validation Summary**: `dotnet build D:\Dev\Archon\src\Archon.Application\Archon.Application.csproj --no-restore` succeeded; `dotnet build D:\Dev\Archon\src\Archon.Api.Extraction\Archon.Api.Extraction.csproj --no-restore` succeeded; `dotnet build D:\Dev\Archon\src\ArchonApi\ArchonApi.csproj --no-restore` succeeded; `dotnet build D:\Dev\Archon\test\Archon.Application.Tests\Archon.Application.Tests.csproj --no-restore` succeeded; `dotnet build D:\Dev\Archon\test\Archon.Api.Extraction.Tests\Archon.Api.Extraction.Tests.csproj --no-restore` succeeded; `dotnet test D:\Dev\Archon\test\Archon.Application.Tests\Archon.Application.Tests.csproj --no-build --filter FullyQualifiedName~StartExtractionApplicationServiceTests` passed 13/13; `dotnet test D:\Dev\Archon\test\Archon.Api.Extraction.Tests\Archon.Api.Extraction.Tests.csproj --no-build --filter FullyQualifiedName~ExtractionEndpointTests` passed 6/6; `dotnet test D:\Dev\Archon\test\ArchonApi.Tests\ArchonApi.Tests.csproj --no-build --filter FullyQualifiedName~ArchonApiHealthEndpointTests` passed 2/2. Aspire AppHost was not started and the full test suite was not run.
  - **Documentation and Wiki Review Result**: Source documentation pass completed for changed hand-maintained source and test files. Wiki review updated `wiki/api-extraction-workflow.md`, `wiki/runtime-foundation.md`, `wiki/validation-and-test-workflows.md`, and `wiki/glossary.md`. Page-structure decision: the existing API extraction workflow page remained the correct home because Work Item 2 deepens the same start/status/history workflow rather than introducing a new independent concept; no new page was created; `wiki/home.md` remained unchanged and concise.
  - **Wiki Impact Matrix**:

	| Affected Concept | Pages Reviewed | Pages Updated | Pages Created | Pages Intentionally Unchanged | Page-Structure Decision |
	| --- | --- | --- | --- | --- | --- |
	| Recent run history | `wiki/api-extraction-workflow.md`, `wiki/runtime-foundation.md`, `wiki/validation-and-test-workflows.md`, `wiki/glossary.md`, `wiki/home.md` | `wiki/api-extraction-workflow.md`, `wiki/runtime-foundation.md`, `wiki/validation-and-test-workflows.md`, `wiki/glossary.md` | None | `wiki/home.md` | Existing API extraction workflow page owns the detailed history behavior; home did not need a new reader path because it already links to the workflow page. |
	| Progress reporting and diagnostic counts | `wiki/api-extraction-workflow.md`, `wiki/validation-and-test-workflows.md`, `wiki/glossary.md` | `wiki/api-extraction-workflow.md`, `wiki/validation-and-test-workflows.md` | None | `wiki/glossary.md` beyond recent-history term addition already has run lifecycle wording | Progress remains part of the run lifecycle and workflow page rather than a separate topic page. |
	| API validation workflow for history | `wiki/validation-and-test-workflows.md` | `wiki/validation-and-test-workflows.md` | None | None | Targeted validation commands remain on the validation workflow page with current behavior updated. |
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
	- [x] Task 1: Complete run-history API contracts - Completed. `ExtractionRunSummaryResponse` and `ExtractionRunHistoryResponse` were added with documented summary fields and count-based diagnostic exposure.
  - [x] Task 2: Implement recent run retrieval - Completed. `GetRecentRunsAsync` was added to the application service and uses deterministic newest-first in-memory ordering with bounded API limit support.
  - [x] Task 3: Implement progress update behavior - Completed. `UpdateRunProgressAsync` updates status/progress and appends warnings/errors while preserving immutable run snapshots and snapshot identity support.
  - [x] Task 4: Add history endpoint - Completed. `GET /extractions` maps to recent-run retrieval and returns compact run summaries.
  - [x] Task 5: Add tests - Completed. Application and API tests cover deterministic ordering, progress visibility, warning/error counts, history response shape, limit behavior, and redaction-oriented response checks.
  - [x] Task 6: Perform documentation and wiki review for the slice - Completed. Source documentation and wiki review were completed; wiki updates are recorded above.
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

- [x] Work Item 3: Implement the placeholder extraction pipeline and generalized snapshot assembly - Completed
  - **Completion Summary**: Implemented the WP004 placeholder pipeline and generalized snapshot assembly path in `src/Archon.Application`. Added `IExtractionStage`, `ExtractionStageContext`, `ExtractionStageResult`, `ExtractionPipelineResult`, `ExtractionPipelineRunner`, `PlaceholderExtractionStage`, and `ExtractionSnapshotAssembler`. The pipeline executes stages sequentially in supplied order, preserves warning diagnostics, stops on controlled blocking errors, and keeps execution application-layer only. The placeholder stage contributes only a warning and no fabricated architecture facts. Snapshot assembly merges accumulated contributions with deterministic repository, solution, and snapshot-header boundary facts, preserves warnings/errors, represents unsupported sections as explicit empty collections, uses stable logical keys, and avoids database IDs and sensitive metadata values.
  - **Validation Summary**: `dotnet build D:\Dev\Archon\src\Archon.Application\Archon.Application.csproj --no-restore` initially exposed metadata dictionary typing errors in `ExtractionSnapshotAssembler`; after correction it succeeded. `dotnet build D:\Dev\Archon\test\Archon.Application.Tests\Archon.Application.Tests.csproj --no-restore` succeeded. `dotnet test D:\Dev\Archon\test\Archon.Application.Tests\Archon.Application.Tests.csproj --no-build --filter "FullyQualifiedName~ExtractionPipelineRunnerTests|FullyQualifiedName~ExtractionSnapshotAssemblerTests"` passed 6/6. `dotnet test D:\Dev\Archon\test\Archon.Application.Tests\Archon.Application.Tests.csproj --no-build --filter FullyQualifiedName~ArchitectureSnapshotAccumulatorTests` passed 5/5. Aspire AppHost was not started and the full test suite was not run.
  - **Documentation and Wiki Review Result**: Source documentation pass completed for changed hand-maintained source and test files. Wiki review updated `wiki/api-extraction-workflow.md`, `wiki/graph-domain-model.md`, `wiki/validation-and-test-workflows.md`, and `wiki/glossary.md`. Page-structure decision: no new page was needed because the existing API extraction workflow page owns the runtime workflow explanation and the graph domain model page owns accumulation/snapshot shape guidance; `wiki/home.md` remained unchanged and concise.
  - **Wiki Impact Matrix**:

	| Affected Concept | Pages Reviewed | Pages Updated | Pages Created | Pages Intentionally Unchanged | Page-Structure Decision |
	| --- | --- | --- | --- | --- | --- |
	| Extraction pipeline and stage model | `wiki/api-extraction-workflow.md`, `wiki/graph-domain-model.md`, `wiki/glossary.md`, `wiki/home.md` | `wiki/api-extraction-workflow.md`, `wiki/glossary.md` | None | `wiki/home.md` | Pipeline behavior extends the API extraction workflow and does not need a separate page yet. |
	| Snapshot assembly and accumulation | `wiki/graph-domain-model.md`, `wiki/api-extraction-workflow.md`, `wiki/glossary.md` | `wiki/graph-domain-model.md`, `wiki/api-extraction-workflow.md` | None | None | Graph domain model remains the correct home for accumulator/snapshot contract explanation. |
	| Placeholder extractor boundary | `wiki/api-extraction-workflow.md`, `wiki/glossary.md`, `wiki/validation-and-test-workflows.md` | `wiki/api-extraction-workflow.md`, `wiki/glossary.md`, `wiki/validation-and-test-workflows.md` | None | None | Placeholder behavior is documented as current non-final workflow behavior, not as final extraction capability. |
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
	- [x] Task 1: Define extraction stage and accumulation contracts - Completed. Added the stage interface, stage context, stage result, pipeline result, and reused `ArchitectureSnapshotAccumulator` as the generalized accumulation model.
  - [x] Task 2: Implement deterministic stage pipeline - Completed. Added sequential pipeline execution with deterministic supplied ordering, cancellation checks, warning retention, controlled blocking errors, and credential-safe logging.
  - [x] Task 3: Implement placeholder stage behavior - Completed. Added warning-only placeholder behavior that does not invent future extractor facts.
  - [x] Task 4: Implement snapshot assembly - Completed. Added repository, solution, and snapshot-header boundary assembly using existing stable-key and metadata contracts and preserving accumulated sections and diagnostics.
  - [x] Task 5: Add tests - Completed. Added tests for stage ordering, stop-on-blocking-error, non-blocking warnings, placeholder boundary, complete minimal snapshot shape, explicit empty collections, warning/error retention, and database-id avoidance.
  - [x] Task 6: Perform documentation and wiki review for the slice - Completed. Documentation-pass and wiki review were completed; wiki updates are recorded above.
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

- [x] Work Item 4: Connect asynchronous scheduled work to orchestration and WP003 snapshot persistence - Completed
  - **Completion Summary**: Implemented the asynchronous orchestration and persistence handoff slice across `src/Archon.Application`, `src/Archon.Api.Extraction`, and `src/Archon.Infrastructure.Neo4j`. Added `ExtractionOrchestrator`, `InProcessExtractionWorkScheduler`, and `InMemoryArchitectureSnapshotWriter`; wired the extraction module to the placeholder stage pipeline, assembler, orchestrator, and scheduler; and ensured host-level Neo4j registration overrides the in-memory writer with `Neo4jArchitectureSnapshotWriter`. The orchestrator reconstructs accepted input from credential-safe run history, updates progress through validation/context preparation, pipeline, assembly, persistence, and terminal states, hands the complete `ExtractedArchitectureSnapshot` to `IArchitectureSnapshotWriter`, records `SnapshotStableKey` only after persistence succeeds, and converts pipeline errors, persistence failures, cancellation, and unexpected exceptions into controlled run failures without exposing stack traces or driver details.
  - **Validation Summary**: `dotnet build D:\Dev\Archon\src\Archon.Application\Archon.Application.csproj --no-restore` initially failed with CS1061 because `ExtractionRunRequestSummary` intentionally retains metadata keys rather than metadata values; after reconstructing safe metadata placeholders from `MetadataKeys`, it succeeded. `dotnet build D:\Dev\Archon\src\Archon.Api.Extraction\Archon.Api.Extraction.csproj --no-restore` succeeded; `dotnet build D:\Dev\Archon\src\Archon.Infrastructure.Neo4j\Archon.Infrastructure.Neo4j.csproj --no-restore` succeeded; `dotnet build D:\Dev\Archon\src\ArchonApi\ArchonApi.csproj --no-restore` succeeded; `dotnet build D:\Dev\Archon\test\Archon.Application.Tests\Archon.Application.Tests.csproj --no-restore` succeeded; `dotnet build D:\Dev\Archon\test\Archon.Api.Extraction.Tests\Archon.Api.Extraction.Tests.csproj --no-restore` succeeded; `dotnet build D:\Dev\Archon\test\ArchonApi.Tests\ArchonApi.Tests.csproj --no-restore` succeeded. `dotnet test D:\Dev\Archon\test\Archon.Application.Tests\Archon.Application.Tests.csproj --no-build --filter FullyQualifiedName~ExtractionOrchestratorTests` passed 5/5 after aligning the persistence-stage assertion with the application-owned `SnapshotPersistence` stage. `dotnet test D:\Dev\Archon\test\Archon.Application.Tests\Archon.Application.Tests.csproj --no-build --filter "FullyQualifiedName~StartExtractionApplicationServiceTests|FullyQualifiedName~ExtractionPipelineRunnerTests|FullyQualifiedName~ExtractionSnapshotAssemblerTests"` passed 19/19. `dotnet test D:\Dev\Archon\test\Archon.Api.Extraction.Tests\Archon.Api.Extraction.Tests.csproj --no-build --filter FullyQualifiedName~ExtractionEndpointTests` passed 6/6 after updating tests to allow immediate polling to observe `Queued`, `Running`, or `Completed` and placeholder warning counts. `dotnet test D:\Dev\Archon\test\ArchonApi.Tests\ArchonApi.Tests.csproj --no-build --filter FullyQualifiedName~ArchonApiHealthEndpointTests` passed 2/2. Aspire AppHost was not started and the full test suite was not run.
  - **Documentation and Wiki Review Result**: Source documentation pass completed for changed hand-maintained source and test files. Wiki review updated `wiki/api-extraction-workflow.md`, `wiki/runtime-foundation.md`, `wiki/validation-and-test-workflows.md`, and `wiki/glossary.md`. Page-structure decision: the existing API extraction workflow page remains the correct detailed home for the asynchronous sequence because Work Item 4 deepens the same API workflow; runtime foundation needed only composition-oriented wording; validation workflow needed command coverage; glossary needed `orchestrator` and `persistence handoff` terms. No new page was created, `wiki/home.md` remained unchanged and concise, and `wiki/neo4j-persistence-foundation.md` remained intentionally unchanged because the Neo4j writer behavior itself did not change.
  - **Wiki Impact Matrix**:

	| Affected Concept | Pages Reviewed | Pages Updated | Pages Created | Pages Intentionally Unchanged | Page-Structure Decision |
	| --- | --- | --- | --- | --- | --- |
	| Asynchronous orchestration sequence | `wiki/api-extraction-workflow.md`, `wiki/runtime-foundation.md`, `wiki/home.md` | `wiki/api-extraction-workflow.md`, `wiki/runtime-foundation.md` | None | `wiki/home.md` | Existing workflow page owns the detailed start-to-terminal sequence; runtime page only summarizes host composition; home remains a landing page. |
	| Persistence handoff and stable snapshot identity | `wiki/api-extraction-workflow.md`, `wiki/neo4j-persistence-foundation.md`, `wiki/glossary.md` | `wiki/api-extraction-workflow.md`, `wiki/glossary.md` | None | `wiki/neo4j-persistence-foundation.md` | Handoff behavior belongs with the API workflow because the Neo4j persistence writer contract did not change. |
	| Run lifecycle terminal states and controlled failures | `wiki/api-extraction-workflow.md`, `wiki/validation-and-test-workflows.md`, `wiki/glossary.md` | `wiki/api-extraction-workflow.md`, `wiki/validation-and-test-workflows.md` | None | `wiki/glossary.md` beyond added orchestration terms | Lifecycle explanation remains on the workflow page; validation commands document how contributors verify failures. |
	| Validation workflow for orchestration tests | `wiki/validation-and-test-workflows.md` | `wiki/validation-and-test-workflows.md` | None | None | Existing validation page is the correct home for focused build/test commands. |
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
	- [x] Task 1: Implement orchestration service - Completed. Added the application orchestrator with run-history, stage pipeline, snapshot assembler, snapshot persistence abstraction, and logger dependencies; progress updates now cover the major orchestration stages and terminal states.
  - [x] Task 2: Connect scheduler to orchestrator - Completed. Added an in-process scheduler that dispatches accepted run identifiers to the orchestrator on a background task while the HTTP start path returns after scheduling.
  - [x] Task 3: Integrate WP003 persistence abstraction - Completed. The orchestrator calls `IArchitectureSnapshotWriter` with the full generalized snapshot, and host-level Neo4j composition overrides the in-memory writer fallback without exposing Neo4j driver types to application or API module code.
  - [x] Task 4: Implement controlled failure behavior - Completed. Pipeline blocking errors, persistence failures, cancellation, and unexpected exceptions become controlled failed run diagnostics without raw stack traces or infrastructure details in run status.
  - [x] Task 5: Add orchestration and persistence tests - Completed. Added focused application tests for persistence success, full snapshot handoff, pipeline failure, persistence failure, unexpected exception redaction, and no persistence on validation failure.
  - [x] Task 6: Perform documentation and wiki review for the slice - Completed. Source documentation pass and wiki review were completed; wiki updates and page-structure decisions are recorded above.
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

- [x] Work Item 5: Harden API behavior and document manual verification flows - Completed
  - **Completion Summary**: Hardened the extraction API HTTP contract in `src/Archon.Api.Extraction` and `test/Archon.Api.Extraction.Tests`. The endpoint surface remains the resolved direct route family `POST /extractions`, `GET /extractions/{runId}`, and `GET /extractions` with no `/api` prefix. Added a final API response-layer diagnostic sanitizer so validation, warning, and error messages that contain obvious stack-trace, exception, connection-string, or secret-like fragments are replaced before leaving the HTTP boundary. Expanded endpoint tests to cover direct route behavior, prefixed-route absence, metadata-value redaction for rejected and accepted requests, not-found behavior, success responses, history summaries, and accepted-run persistence failure redaction through a deterministic failing persistence writer.
  - **Validation Summary**: `dotnet build D:\Dev\Archon\src\Archon.Api.Extraction\Archon.Api.Extraction.csproj --no-restore` succeeded; `dotnet build D:\Dev\Archon\test\Archon.Api.Extraction.Tests\Archon.Api.Extraction.Tests.csproj --no-restore` succeeded; `dotnet test D:\Dev\Archon\test\Archon.Api.Extraction.Tests\Archon.Api.Extraction.Tests.csproj --no-build --filter FullyQualifiedName~ExtractionEndpointTests` passed 9/9 after correcting one test assertion to expect the application-owned `SnapshotPersistence` stage; `dotnet build D:\Dev\Archon\src\ArchonApi\ArchonApi.csproj --no-restore` succeeded; `dotnet build D:\Dev\Archon\test\ArchonApi.Tests\ArchonApi.Tests.csproj --no-restore` succeeded; `dotnet test D:\Dev\Archon\test\ArchonApi.Tests\ArchonApi.Tests.csproj --no-build --filter FullyQualifiedName~ArchonApiHealthEndpointTests` passed 2/2. Aspire AppHost was not started and the full test suite was not run.
  - **Documentation and Wiki Review Result**: Source documentation pass completed for changed hand-maintained source and test files. Wiki review updated `wiki/api-extraction-workflow.md` with diagnostic sanitization behavior and manual `POST /extractions`, status polling, and history retrieval examples using non-sensitive sample paths; updated `wiki/validation-and-test-workflows.md` with route-hardening, redaction, and manual-verification validation guidance. `wiki/home.md` remained intentionally unchanged as a concise landing page and link hub. `wiki/glossary.md` remained intentionally unchanged because the slice clarified existing API contract and redaction behavior without introducing a new durable term.
  - **Wiki Impact Matrix**:

	| Affected Concept | Pages Reviewed | Pages Updated | Pages Created | Pages Intentionally Unchanged | Page-Structure Decision |
	| --- | --- | --- | --- | --- | --- |
	| Extraction API route contract | `wiki/api-extraction-workflow.md`, `wiki/validation-and-test-workflows.md`, `wiki/home.md` | `wiki/api-extraction-workflow.md`, `wiki/validation-and-test-workflows.md` | None | `wiki/home.md` | Existing workflow and validation pages are the correct homes; home already links to the workflow and remains concise. |
	| Diagnostic redaction and runtime failure surface | `wiki/api-extraction-workflow.md`, `wiki/validation-and-test-workflows.md`, `wiki/glossary.md` | `wiki/api-extraction-workflow.md`, `wiki/validation-and-test-workflows.md` | None | `wiki/glossary.md` | Redaction behavior belongs with the API workflow and validation guidance; no new glossary term was needed. |
	| Manual verification flow | `wiki/api-extraction-workflow.md`, `wiki/validation-and-test-workflows.md`, `wiki/home.md` | `wiki/api-extraction-workflow.md`, `wiki/validation-and-test-workflows.md` | None | `wiki/home.md` | Manual workflow examples fit the existing API workflow page; validation page links contributors back to it and reinforces that automated validation must not start AppHost. |
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
	- [x] Task 1: Confirm endpoint route and response conventions - Completed. Confirmed direct no-`/api` routes, 202 accepted start responses, 400 validation problem responses, 404 missing/malformed status responses, 200 status/history responses, and no run creation on validation failure through existing and new API tests.
  - [x] Task 2: Harden redaction and error responses - Completed. Reviewed validation and run diagnostics and added response-layer sanitization for unsafe diagnostic fragments while preserving stable codes, stages, and safe user-actionable messages.
  - [x] Task 3: Add API contract tests - Completed. Added tests for exact route behavior, successful start/status/history behavior, validation response redaction, metadata-value redaction, not-found retrieval, and accepted-run failure redaction.
  - [x] Task 4: Add manual verification documentation - Completed. Added non-sensitive manual request, status polling, and history examples to `wiki/api-extraction-workflow.md` and reiterated that automated validation must not start Aspire AppHost.
  - [x] Task 5: Perform documentation and wiki review for the slice - Completed. Reviewed API workflow, validation workflow, glossary, and home reader paths; updated the API workflow and validation pages; left `wiki/home.md` and `wiki/glossary.md` intentionally unchanged for the reasons recorded above.
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

- [x] Work Item 6: Complete validation, test coverage, and regression safety for WP004 - Completed
  - **Completion Summary**: Completed the WP004 validation and regression safety slice across focused application and API tests. Added explicit orchestration regression assertions in `test/Archon.Application.Tests/Extraction/Orchestration/ExtractionOrchestratorTests.cs` proving the persistence writer is invoked exactly once for successful accepted runs, not invoked for validation, pipeline, or unexpected-exception failures, and receives the complete generalized snapshot shape including snapshot header, repositories, solutions, nodes, edges, evidence, rules, findings, metrics, generated summaries, warnings, and errors. Added API terminal-status coverage in `test/Archon.Api.Extraction.Tests/ExtractionEndpointTests.cs` proving completed run status responses include completed UTC, progress stage/message/percentage/last-updated UTC, retained warnings, empty errors, and persisted snapshot identity.
  - **Traceability Summary**: TR-001 through TR-006 are covered by `StartExtractionApplicationServiceTests` validation rejection scenarios; TR-007, TR-026, and DCR-006 are covered by validation-failure no-run/no-scheduler/no-persistence tests; TR-008 through TR-012 and TR-023 through TR-025 are covered by `ExtractionOrchestratorTests`, `ExtractionPipelineRunnerTests`, `ExtractionSnapshotAssemblerTests`, and `ArchitectureSnapshotAccumulatorTests`; TR-013 through TR-017 are covered by start/status/progress/orchestration tests; TR-018 through TR-022 are covered by `ExtractionEndpointTests`, including valid start, validation response, status/history, terminal completion, failure visibility, and redaction; TR-027 through TR-029 are satisfied by the focused no-AppHost validation commands and test doubles recorded below.
  - **Validation Summary**: `dotnet build D:\Dev\Archon\src\Archon.Application\Archon.Application.csproj --no-restore` succeeded; `dotnet build D:\Dev\Archon\src\Archon.Api.Extraction\Archon.Api.Extraction.csproj --no-restore` succeeded; `dotnet build D:\Dev\Archon\src\ArchonApi\ArchonApi.csproj --no-restore` succeeded; `dotnet build D:\Dev\Archon\test\Archon.Application.Tests\Archon.Application.Tests.csproj --no-restore` succeeded; `dotnet build D:\Dev\Archon\test\Archon.Api.Extraction.Tests\Archon.Api.Extraction.Tests.csproj --no-restore` succeeded; `dotnet build D:\Dev\Archon\test\ArchonApi.Tests\ArchonApi.Tests.csproj --no-restore` succeeded; `dotnet test D:\Dev\Archon\test\Archon.Application.Tests\Archon.Application.Tests.csproj --no-build --filter FullyQualifiedName~StartExtractionApplicationServiceTests` passed 13/13; `dotnet test D:\Dev\Archon\test\Archon.Application.Tests\Archon.Application.Tests.csproj --no-build --filter "FullyQualifiedName~ExtractionPipelineRunnerTests|FullyQualifiedName~ExtractionSnapshotAssemblerTests"` passed 6/6; `dotnet test D:\Dev\Archon\test\Archon.Application.Tests\Archon.Application.Tests.csproj --no-build --filter FullyQualifiedName~ExtractionOrchestratorTests` passed 5/5; `dotnet test D:\Dev\Archon\test\Archon.Application.Tests\Archon.Application.Tests.csproj --no-build --filter FullyQualifiedName~ArchitectureSnapshotAccumulatorTests` passed 5/5; `dotnet test D:\Dev\Archon\test\Archon.Api.Extraction.Tests\Archon.Api.Extraction.Tests.csproj --no-build --filter FullyQualifiedName~ExtractionEndpointTests` passed 10/10; `dotnet test D:\Dev\Archon\test\ArchonApi.Tests\ArchonApi.Tests.csproj --no-build --filter FullyQualifiedName~ArchonApiHealthEndpointTests` passed 2/2. Aspire AppHost was not started and the full test suite was not run.
  - **Documentation and Wiki Review Result**: Source documentation pass completed for changed hand-maintained test files. Wiki review updated `wiki/validation-and-test-workflows.md` to describe the completed regression coverage for exact persistence writer invocation, full generalized snapshot handoff, terminal completed API status with snapshot identity, and continued no-AppHost validation. Reviewed `wiki/api-extraction-workflow.md`, `wiki/home.md`, and `wiki/glossary.md`; no changes were needed because Work Item 6 clarified validation coverage rather than changing the extraction workflow, reader path, or durable terminology.
  - **Wiki Impact Matrix**:

	| Affected Concept | Pages Reviewed | Pages Updated | Pages Created | Pages Intentionally Unchanged | Page-Structure Decision |
	| --- | --- | --- | --- | --- | --- |
	| WP004 validation and regression workflow | `wiki/validation-and-test-workflows.md`, `wiki/api-extraction-workflow.md`, `wiki/home.md` | `wiki/validation-and-test-workflows.md` | None | `wiki/api-extraction-workflow.md`, `wiki/home.md` | Validation workflow page is the correct home for focused test coverage and commands; API workflow behavior itself did not change; home remains a concise landing page. |
	| Persistence handoff test coverage | `wiki/validation-and-test-workflows.md`, `wiki/api-extraction-workflow.md`, `wiki/glossary.md` | `wiki/validation-and-test-workflows.md` | None | `wiki/api-extraction-workflow.md`, `wiki/glossary.md` | Handoff behavior was already explained on the workflow page; this slice only clarified how contributors validate it, so no new glossary term or page was needed. |
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
	- [x] Task 1: Map specification requirements to tests - Completed. Added the traceability summary above for TR-001 through TR-029 without duplicating contributor-facing guidance outside the wiki.
	- [x] Create a lightweight traceability checklist inside this implementation plan or update existing plan status entries without duplicating wiki guidance.
	- [x] Ensure every test requirement in specification sections 8.1 through 8.4 has corresponding test coverage or a documented rationale.
  - [x] Task 2: Add missing application tests - Completed. Added focused application assertions for complete generalized snapshot handoff and exact persistence writer invocation counts using existing stage and persistence test doubles; no Aspire AppHost dependency was introduced.
	- [x] Cover missing validation, resolution, lifecycle, progress, pipeline, assembly, orchestration, and persistence handoff behavior.
	- [x] Use test doubles for persistence and stages where real Neo4j behavior is not required.
	- [x] Avoid test patterns that require Aspire AppHost.
  - [x] Task 3: Add missing API tests - Completed. Added terminal completed status coverage for progress, warnings, errors, completed UTC, and snapshot identity using the existing in-memory API integration test host.
	- [x] Cover start, status, history, validation, not-found, progress, snapshot identity, and redaction behavior.
	- [x] Prefer existing API integration test infrastructure and avoid new test frameworks unless necessary.
  - [x] Task 4: Run focused validation - Completed. Built changed production and test projects and ran targeted application, API extraction, and API host tests; exact commands and outcomes are recorded in the validation summary above.
	- [x] Build changed production and test projects.
	- [x] Run targeted tests for application, infrastructure, and API areas touched by WP004.
	- [x] Record exact commands and outcomes in the plan completion/status section.
  - [x] Task 5: Perform documentation and wiki review for validation workflows - Completed. Reviewed validation, API workflow, home, and glossary pages; updated `wiki/validation-and-test-workflows.md` with the new regression coverage explanation.
	- [x] Review whether wiki validation or troubleshooting pages need updates based on new commands, test seams, or failure modes.
	- [x] Update the correct topic pages if needed and include walkthrough detail where it materially improves contributor understanding.
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

- [x] Work Item 7: Complete mandatory documentation pass and final wiki impact review - Completed
  - **Completion Summary**: Completed the final WP004 documentation and wiki completion gate. Reviewed the hand-maintained WP004 C# source and test files under `src/Archon.Application/Extraction`, `src/Archon.Api.Extraction`, `test/Archon.Application.Tests/Extraction`, and `test/Archon.Api.Extraction.Tests`; generated `obj` files were excluded. Existing XML and developer-level comments already covered the scoped production and test types and methods, so no comment-only source edits were required. Completed the final wiki information-architecture review across extraction workflow, validation, runtime, glossary, and landing-page reader paths. Updated `wiki/home.md` only to correct stale current-state orientation about completed asynchronous orchestration and persistence handoff while keeping detailed guidance on topic pages.
  - **Validation Summary**: `dotnet build D:\Dev\Archon\src\Archon.Application\Archon.Application.csproj --no-restore` succeeded; `dotnet build D:\Dev\Archon\src\Archon.Api.Extraction\Archon.Api.Extraction.csproj --no-restore` succeeded; `dotnet build D:\Dev\Archon\src\ArchonApi\ArchonApi.csproj --no-restore` succeeded; `dotnet build D:\Dev\Archon\test\Archon.Application.Tests\Archon.Application.Tests.csproj --no-restore` succeeded; `dotnet build D:\Dev\Archon\test\Archon.Api.Extraction.Tests\Archon.Api.Extraction.Tests.csproj --no-restore` succeeded; `dotnet build D:\Dev\Archon\test\ArchonApi.Tests\ArchonApi.Tests.csproj --no-restore` succeeded; `dotnet test D:\Dev\Archon\test\Archon.Application.Tests\Archon.Application.Tests.csproj --no-build --filter FullyQualifiedName~StartExtractionApplicationServiceTests` passed 13/13; `dotnet test D:\Dev\Archon\test\Archon.Application.Tests\Archon.Application.Tests.csproj --no-build --filter "FullyQualifiedName~ExtractionPipelineRunnerTests|FullyQualifiedName~ExtractionSnapshotAssemblerTests"` passed 6/6; `dotnet test D:\Dev\Archon\test\Archon.Application.Tests\Archon.Application.Tests.csproj --no-build --filter FullyQualifiedName~ExtractionOrchestratorTests` passed 5/5; `dotnet test D:\Dev\Archon\test\Archon.Application.Tests\Archon.Application.Tests.csproj --no-build --filter FullyQualifiedName~ArchitectureSnapshotAccumulatorTests` passed 5/5; `dotnet test D:\Dev\Archon\test\Archon.Api.Extraction.Tests\Archon.Api.Extraction.Tests.csproj --no-build --filter FullyQualifiedName~ExtractionEndpointTests` passed 10/10; `dotnet test D:\Dev\Archon\test\ArchonApi.Tests\ArchonApi.Tests.csproj --no-build --filter FullyQualifiedName~ArchonApiHealthEndpointTests` passed 2/2. Aspire AppHost was not started and the full test suite was not run, consistent with WP004 validation guidance.
  - **Documentation and Wiki Review Result**: Documentation-pass review completed for WP004-scoped hand-maintained source and test files. Wiki review updated `wiki/home.md` to align the concise landing-page capability summary with the completed WP004 asynchronous orchestration and persistence handoff. Reviewed `wiki/api-extraction-workflow.md`, `wiki/validation-and-test-workflows.md`, `wiki/runtime-foundation.md`, and `wiki/glossary.md`; these topic pages already contained the required current-state narrative guidance, examples, cross-links, and terminology coverage. No new wiki page was created because the existing API extraction workflow page remains the correct home for detailed asynchronous extraction behavior, the validation page remains the correct home for commands and regression coverage, the runtime foundation page remains the correct home for composition overview, and the glossary remains sufficient for durable terms. Searched WP004 docs/wiki scope for implementation-note-style substitute artifacts; none were found, so no retirement was required.
  - **Wiki Impact Matrix**:

	| Affected Concept | Pages Reviewed | Pages Updated | Pages Created | Pages Intentionally Unchanged | Page-Structure Decision |
	| --- | --- | --- | --- | --- | --- |
	| Asynchronous extraction execution | `wiki/home.md`, `wiki/api-extraction-workflow.md`, `wiki/runtime-foundation.md`, `wiki/glossary.md` | `wiki/home.md` | None | `wiki/api-extraction-workflow.md`, `wiki/runtime-foundation.md`, `wiki/glossary.md` | Existing API workflow page owns detailed execution guidance; home needed only concise current-state correction and remains a landing page. |
	| Run lifecycle and progress reporting | `wiki/api-extraction-workflow.md`, `wiki/validation-and-test-workflows.md`, `wiki/glossary.md`, `wiki/home.md` | `wiki/home.md` | None | `wiki/api-extraction-workflow.md`, `wiki/validation-and-test-workflows.md`, `wiki/glossary.md` | Existing workflow and validation pages already describe lifecycle, progress, diagnostics, and focused coverage with sufficient glossary support. |
	| Extraction API contract | `wiki/api-extraction-workflow.md`, `wiki/validation-and-test-workflows.md`, `wiki/home.md` | `wiki/home.md` | None | `wiki/api-extraction-workflow.md`, `wiki/validation-and-test-workflows.md` | Direct route and manual usage detail remains on the workflow page; validation commands remain on the validation page; home only orients readers. |
	| Validation and path policy | `wiki/api-extraction-workflow.md`, `wiki/validation-and-test-workflows.md`, `wiki/glossary.md` | None | None | `wiki/api-extraction-workflow.md`, `wiki/validation-and-test-workflows.md`, `wiki/glossary.md` | Existing validation boundary and command guidance remained current; no new page or glossary term was needed. |
	| Snapshot assembly and persistence handoff | `wiki/api-extraction-workflow.md`, `wiki/graph-domain-model.md`, `wiki/neo4j-persistence-foundation.md`, `wiki/runtime-foundation.md`, `wiki/home.md`, `wiki/glossary.md` | `wiki/home.md` | None | `wiki/api-extraction-workflow.md`, `wiki/graph-domain-model.md`, `wiki/neo4j-persistence-foundation.md`, `wiki/runtime-foundation.md`, `wiki/glossary.md` | Workflow page owns handoff behavior; graph and Neo4j pages own snapshot/persistence foundations; home needed only current-state correction. |
	| Placeholder extractor boundary | `wiki/api-extraction-workflow.md`, `wiki/validation-and-test-workflows.md`, `wiki/glossary.md`, `wiki/home.md` | `wiki/home.md` | None | `wiki/api-extraction-workflow.md`, `wiki/validation-and-test-workflows.md`, `wiki/glossary.md` | Placeholder limitations were already explained on the workflow page; home now concisely distinguishes placeholder persistence from real repository/Roslyn extraction. |
	| Substitute artifact retirement | `docs/004-API-Extraction-Contract-and-Snapshot-Orchestration/*`, `wiki/*` | None | None | All reviewed files | Search found no prohibited implementation notes, implementation ledgers, architecture notes, or completion-record substitutes; no retirement or migration was required. |
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
	- [x] Task 1: Perform final source documentation pass - Completed. Reviewed WP004-scoped hand-maintained source and test files; existing local XML/developer comments met the scoped documentation-pass need, generated artifacts were excluded, and no behavior or formatting-only source changes were made.
	- [x] Inspect every hand-maintained `.cs` file changed by WP004.
	- [x] Add or improve XML comments for public APIs and developer-level comments for internal and non-public types, constructors, methods, and non-obvious properties.
	- [x] Preserve behavior and avoid formatting-only cleanup unrelated to comments.
  - [x] Task 2: Perform final wiki information-architecture review - Completed. Reviewed affected WP004 concepts and current wiki ownership; existing topic pages remain the correct homes and no new extraction orchestration page was needed.
	- [x] Identify all affected concepts: asynchronous extraction, run lifecycle, progress reporting, API extraction endpoints, request validation, snapshot assembly, placeholder extractor boundary, persistence handoff, run history, and validation workflow.
	- [x] Review existing wiki topic pages and glossary pages that should own those concepts.
	- [x] Decide whether a new extraction orchestration page is needed or whether existing architecture/API/persistence pages are the correct homes.
	- [x] Confirm `wiki/home.md` remains concise and is not used as a catch-all page.
	- [x] Confirm cross-links and glossary entries are sufficient.
  - [x] Task 3: Update wiki pages where required - Completed. Updated only `wiki/home.md` to remove stale current-state wording; narrative detail, examples, and terminology remained on the existing topic pages.
	- [x] Write current-state guidance in narrative prose for conceptually dense topics.
	- [x] Define technical terms when first introduced or link to glossary entries.
	- [x] Add examples or walkthroughs for start/status/history usage and successful/failed asynchronous extraction where useful.
	- [x] Remove or rewrite stale phase-oriented wording that conflicts with current behavior.
  - [x] Task 4: Retire improper substitute artifacts if found - Completed. Searched WP004 docs/wiki scope and found no prohibited substitute artifacts.
	- [x] Search the WP004 scope for implementation-note-style files that duplicate contributor-facing wiki content.
	- [x] Move still-current guidance to the correct wiki topic page.
	- [x] Remove or retire stale substitute artifacts as appropriate under repository rules.
  - [x] Task 5: Record final wiki impact matrix and validation outcomes - Completed. Recorded final validation commands, outcomes, pages reviewed, updated/unchanged pages, and page-structure decisions above.
	- [x] Record affected concepts.
	- [x] Record pages reviewed.
	- [x] Record pages updated, created, retired, or intentionally unchanged.
	- [x] Record the page-structure decision and why the selected structure remains readable.
	- [x] Record validation commands and outcomes.
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
| Asynchronous extraction execution | `wiki/home.md`, `wiki/api-extraction-workflow.md`, `wiki/runtime-foundation.md`, `wiki/glossary.md` | `wiki/home.md` | None | `wiki/api-extraction-workflow.md`, `wiki/runtime-foundation.md`, `wiki/glossary.md` | Existing API workflow page owns detailed execution guidance; home remains concise and now accurately orients readers to completed orchestration. |
| Run lifecycle and progress reporting | `wiki/api-extraction-workflow.md`, `wiki/validation-and-test-workflows.md`, `wiki/glossary.md`, `wiki/home.md` | `wiki/home.md` | None | `wiki/api-extraction-workflow.md`, `wiki/validation-and-test-workflows.md`, `wiki/glossary.md` | Existing workflow and validation pages already describe lifecycle, progress, diagnostics, and coverage. |
| Extraction API contract | `wiki/api-extraction-workflow.md`, `wiki/validation-and-test-workflows.md`, `wiki/home.md` | `wiki/home.md` | None | `wiki/api-extraction-workflow.md`, `wiki/validation-and-test-workflows.md` | Route details, examples, and validation commands remain on topic pages; home only summarizes capability. |
| Validation and path policy | `wiki/api-extraction-workflow.md`, `wiki/validation-and-test-workflows.md`, `wiki/glossary.md` | None | None | `wiki/api-extraction-workflow.md`, `wiki/validation-and-test-workflows.md`, `wiki/glossary.md` | Existing validation boundary and command guidance remained current. |
| Snapshot assembly and persistence handoff | `wiki/api-extraction-workflow.md`, `wiki/graph-domain-model.md`, `wiki/neo4j-persistence-foundation.md`, `wiki/runtime-foundation.md`, `wiki/home.md`, `wiki/glossary.md` | `wiki/home.md` | None | `wiki/api-extraction-workflow.md`, `wiki/graph-domain-model.md`, `wiki/neo4j-persistence-foundation.md`, `wiki/runtime-foundation.md`, `wiki/glossary.md` | Workflow page owns handoff behavior; graph and Neo4j pages own foundations; home needed only current-state correction. |
| Placeholder extractor boundary | `wiki/api-extraction-workflow.md`, `wiki/validation-and-test-workflows.md`, `wiki/glossary.md`, `wiki/home.md` | `wiki/home.md` | None | `wiki/api-extraction-workflow.md`, `wiki/validation-and-test-workflows.md`, `wiki/glossary.md` | Placeholder limitations remain on the workflow page; home now distinguishes placeholder persistence from real extraction. |

## Summary

This plan delivers WP004 through seven vertical slices. The first slice proves the minimal asynchronous start and status path. Subsequent slices add run history, progress reporting, the placeholder pipeline, generalized snapshot assembly, persistence handoff, API hardening, validation coverage, and final documentation/wiki completion. The key implementation consideration is to make the asynchronous model real from the start while keeping the initial scheduler and run store simple, in-process, testable, and replaceable. The API contract must remain stable, Neo4j must remain the system of record for extraction output, and every coding slice must satisfy both the mandatory documentation-pass standard and the mandatory wiki-maintenance workflow.
