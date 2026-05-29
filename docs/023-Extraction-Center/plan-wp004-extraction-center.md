# Implementation Plan - WP004 Extraction Center

## Document Control

| Field | Value |
| --- | --- |
| Work Package | WP004 - Extraction Center |
| Plan Output Path | `docs/023-Extraction-Center/plan-wp004-extraction-center.md` |
| Related Specification | `docs/023-Extraction-Center/spec-wp004-extraction-center.md` |
| Source UI Roadmap | `docs/foundation/work-packages-ui.md` WP004 |
| Mandatory Wiki Instruction | `./.github/instructions/wiki.instructions.md` |
| Mandatory Documentation-Pass Instruction | `./.github/instructions/documentation-pass.instructions.md` |
| Status | Draft |
| Date | 2026-05-29 |

## Planning Principles and Completion Gates

This plan implements WP004 as a sequence of runnable vertical slices. Each Work Item must leave ArchonExplorer in a usable state and must preserve all previous slices. The implementation is expected to extend the existing Vite, React, TypeScript, shadcn/ui-compatible, TanStack Query, and Playwright frontend under `src/ArchonExplorer` while consuming the existing extraction API surface:

```http
POST /extractions
GET  /extractions/{runId}
GET  /extractions
```

The implementation must not create standalone implementation notes, implementation ledgers, architecture notes, or similar narrative records for contributor-facing detail. Current-state design rationale, architecture guidance, setup steps, validation workflows, troubleshooting guidance, terminology, and contributor-facing behavior must be written into `./wiki` according to `./.github/instructions/wiki.instructions.md`. `wiki/home.md` must remain a concise landing page and must not become the destination for detailed Extraction Center guidance.

Every Work Item that creates or updates source code must treat `./.github/instructions/documentation-pass.instructions.md` as a hard Definition of Done gate. All new or modified code must include developer-level comments for every class, function, method, constructor, hook, reducer, and non-trivial callback. Public method and constructor parameters must be documented where applicable. Internal and other non-public types must receive the same developer-level documentation standard as public types. Properties whose meaning is not obvious from the name must be documented, and inline or block comments must explain purpose, logical flow, and non-obvious algorithms.

Once execution of an active Work Item starts, the executor must continue through all tasks and steps for that Work Item, including implementation, validation, documentation/wiki review, and plan-record updates. Status messages, step announcements, ordinary fixable build/test failures, and routine uncertainty are not stopping points. The only allowed stops during an active Work Item are full Work Item completion, explicit user interruption/change of direction, or a true blocker that cannot be resolved from the specification, this plan, the codebase, or repository guidance.

## Overall Project Structure

The implementation should preserve and extend the current frontend structure rather than introducing a separate application or route system:

```text
src/ArchonExplorer/
  package.json
  playwright.config.ts
  src/
	App.tsx
	api/
	  archonApiClient.ts
	  archonApiRoutes.ts
	  archonApiTypes.ts
	  polling.ts
	  queryKeys.ts
	  testDoubles.ts
	components/
	  ui/
	  workbench/
	  extraction-center/
	hooks/
	  useApiConnectivity.ts
	  useExtractionRunPolling.ts
	  useExtractionHistory.ts
	  useStartExtraction.ts
	state/
	  workbenchStore.tsx
	  extractionCenterStore.tsx
	test/
	  api/
	  extraction-center/
	  workbench/
	test-e2e/
	  workbench-shell.spec.ts
	  extraction-center.spec.ts
```

Exact file names may be adjusted during implementation if the existing codebase has a better convention, but the implementation must keep extraction feature code separate from generic workbench shell code. Shared API client and runtime utilities must remain under `src/ArchonExplorer/src/api` or existing runtime locations. Feature UI components should live under `src/ArchonExplorer/src/components/extraction-center` or an equivalent feature folder, while only shell integration points should touch `src/ArchonExplorer/src/components/workbench` and `src/ArchonExplorer/src/state/workbenchStore.tsx`.

Naming conventions should preserve the repository vocabulary from the specification: `Extraction Center`, `extraction run`, `run history`, `run status`, `background run monitor`, `produced snapshot`, `safe diagnostics`, `terminal status`, and `current` snapshot context.

## Work Items

## 1. Extraction Center Entry and API-Backed History Slice

- [x] Work Item 1: Add an Extraction Center activity that displays real recent extraction history - Completed
  - **Purpose**: Deliver the smallest meaningful end-to-end UI slice: the user can open Extraction Center from the workbench, the UI reads `GET /extractions` through the existing typed client and TanStack Query, and the page displays loading, empty, error, and populated history states without submitting new work yet.
  - **Acceptance Criteria**:
	- Extraction Center is reachable from the workbench activity rail and opens inside the existing desktop shell.
	- The central work area renders an Extraction Center feature surface instead of the generic placeholder when the Extraction Center activity/tab is active.
	- Recent extraction history is loaded through the WP002 API client method for `GET /extractions` and never through direct `fetch` calls in feature components.
	- The route shape remains `/extractions` with no common `/api` prefix.
	- The history UI shows run identifier, status, started timestamp, completed timestamp when available, repository root, solution count, warning count, error count, and snapshot identity when available.
	- Loading, empty, refetching, API-unavailable, and safe error states are visible and do not expose raw backend diagnostics.
  - **Definition of Done**:
	- Code implemented across UI, query hook, workbench integration, and tests.
	- `./.github/instructions/documentation-pass.instructions.md` followed in full for every new or modified source file.
	- Developer-level comments added for every new or modified component, hook, helper, type, reducer, method, constructor, and non-trivial callback, including internal/non-public constructs.
	- Unit/component tests cover query-state mapping and history rendering with deterministic test doubles.
	- Focused Playwright test covers opening Extraction Center and rendering a mocked history or empty state.
	- Logging and error handling use existing safe frontend error abstractions; no raw diagnostics are rendered.
	- Wiki review completed for this slice; update `wiki/archonexplorer-frontend-foundation.md`, create/update an Extraction Center topic page if the page-structure review finds it necessary, or record an explicit no-change result with rationale.
	- Foundational documentation retains book-like narrative depth, defines technical terms, and includes examples or walkthrough support where the subject matter is conceptually dense.
	- Can execute end-to-end via: `cd .\src\ArchonExplorer; npm run typecheck; npm run test; npm run test:e2e -- src/test-e2e/extraction-center.spec.ts`.
	- Executor must not stop mid-Work Item except for full completion, explicit user interruption, or a true blocker.
	- [x] Task 1: Inspect current frontend and API runtime seams before editing - Completed
	- [x] Confirm current method signatures in `src/ArchonExplorer/src/api/archonApiClient.ts` for `getExtractionHistory`.
	- [x] Confirm current query-key builders in `src/ArchonExplorer/src/api/queryKeys.ts` for extraction history.
	- [x] Confirm current workbench activity, tab, bottom-panel, command, and store extension patterns.
	- [x] Confirm current test-double support for extraction history in `src/ArchonExplorer/src/api/testDoubles.ts`.
  - [x] Task 2: Add the feature entry point and history query - Completed
	- [x] Create an Extraction Center feature folder with a top-level `ExtractionCenter` component.
	- [x] Add a documented query hook such as `useExtractionHistory` that calls the typed API client and uses existing query-key conventions.
	- [x] Ensure the hook accepts cancellation through TanStack Query and does not duplicate server state in local component state.
	- [x] Map normalized API failures into persistent safe page-level messages.
  - [x] Task 3: Integrate Extraction Center into the workbench shell - Completed
	- [x] Update activity/tab rendering so selecting Extraction Center shows the feature surface in the main work area.
	- [x] Preserve the existing Workbench Start tab fallback and shell recovery behavior.
	- [x] Avoid browser page navigation or a separate routing framework unless a later architecture decision explicitly introduces one.
	- [x] Keep the UI plain, desktop-IDE-style, and shadcn/ui-compatible without custom theme colors or marketing-style cards.
  - [x] Task 4: Render history states - Completed
	- [x] Render loading, empty, refetching, safe error, and populated states.
	- [x] Show status text in words rather than relying on color alone.
	- [x] Render compact history rows with accessible row selection affordances reserved for later run-detail work.
	- [x] Include safe explanation when no extraction runs exist.
  - [x] Task 5: Add focused tests - Completed
	- [x] Add unit or component tests using deterministic API test doubles for empty, error, and populated history states.
	- [x] Add Playwright coverage for opening Extraction Center from the activity rail.
	- [x] Assert that the UI does not display `/api/extractions`, raw stack traces, connection strings, raw Cypher, Neo4j internals, or driver details.
  - **Completion Summary**: Implemented the API-backed Extraction Center history slice with `src/ArchonExplorer/src/hooks/useExtractionHistory.ts`, `src/ArchonExplorer/src/components/extraction-center/ExtractionCenter.tsx`, shell activity/tab integration in `src/ArchonExplorer/src/state/workbenchStore.tsx` and `src/ArchonExplorer/src/components/workbench/*`, focused unit/component tests in `src/ArchonExplorer/src/test/extraction-center/extractionCenter.test.tsx`, and focused browser tests in `src/ArchonExplorer/src/test-e2e/extraction-center.spec.ts`. The feature uses the typed `GET /extractions` client path, TanStack Query keys and cancellation, safe normalized page-level errors, and plain workbench styling.
	- **Validation Performed**: `cd .\src\ArchonExplorer; npm run typecheck` passed; `npm run test -- extraction-center` passed; `npm run test` passed after scoping Vitest discovery to authored `src/test/**/*.test.{ts,tsx}` files so dependency package tests under `node_modules` are not collected; `npm run build` passed; `npm run test:e2e -- src/test-e2e/extraction-center.spec.ts` passed; `npm run test:e2e -- workbench-shell` passed as a regression check for existing shell browser behavior.
  - **Wiki Review Result**: Updated `wiki/archonexplorer-frontend-foundation.md`, `wiki/home.md`, `wiki/glossary.md`, and `wiki/validation-and-test-workflows.md`. Wiki impact matrix: affected concepts were Extraction Center history, frontend server-state hooks, shell activity/tab behavior, safe history presentation, and focused frontend validation; pages reviewed were `wiki/archonexplorer-frontend-foundation.md`, `wiki/home.md`, `wiki/glossary.md`, and `wiki/validation-and-test-workflows.md`; pages updated were those four pages; pages created were none; pages intentionally unchanged included separate Extraction Center topic pages because the current slice is limited to history loading and fits the frontend foundation page; page-structure decision was to keep `wiki/home.md` as a concise landing page and defer a dedicated Extraction Center page until later submission/monitoring/background-work slices add enough workflow depth.
  - **Files**:
	- `src/ArchonExplorer/src/components/extraction-center/ExtractionCenter.tsx`: New feature entry surface.
	- `src/ArchonExplorer/src/hooks/useExtractionHistory.ts`: New documented TanStack Query hook.
	- `src/ArchonExplorer/src/components/workbench/*`: Minimal shell integration if needed.
	- `src/ArchonExplorer/src/state/workbenchStore.tsx`: Minimal tab/activity state extension if needed.
	- `src/ArchonExplorer/src/test/extraction-center/*`: Focused unit/component tests.
	- `src/ArchonExplorer/src/test-e2e/extraction-center.spec.ts`: Focused browser journey.
	- `wiki/archonexplorer-frontend-foundation.md`: Likely update to describe the new API-backed Extraction Center entry point or link to a new topic page.
  - **Work Item Dependencies**: Depends on WP001-WP003 foundations and existing WP002 API client runtime.
  - **Run / Verification Instructions**:
	- `cd .\src\ArchonExplorer`
	- `npm run typecheck`
	- `npm run test`
	- `npm run build`
	- `npm run test:e2e -- src/test-e2e/extraction-center.spec.ts`
  - **User Instructions**: Do not run the Aspire AppHost for smoke testing during automated execution; ask the user to perform any Aspire-hosted smoke test after implementation if needed.

## 2. Start Extraction Form and Accepted Run Slice

- [x] Work Item 2: Submit a new extraction request and display the accepted run - Completed
  - **Purpose**: Add the first mutation-driven vertical slice: the user can enter repository and explicit solution values, submit to `POST /extractions`, receive an accepted run response, and see that run selected in the Extraction Center.
  - **Acceptance Criteria**:
	- The new extraction form captures repository root directory, one or more explicit solution paths, optional branch name, optional commit SHA, optional requested-by, and optional metadata values aligned with the implementation-time API contract.
	- The form explains that solution paths are explicit and are not discovered by recursive repository scanning.
	- Obvious missing-value validation appears before submission, while server validation remains authoritative.
	- The submit mutation calls the typed API client method for `POST /extractions` and never calls `fetch` directly from components.
	- A successful HTTP 202-style response displays the accepted run ID and selected run summary.
	- Submission failure preserves form values and displays safe field-level or form-level validation feedback.
  - **Definition of Done**:
	- Form, mutation hook, selected-run state, and accepted-run display implemented.
	- `./.github/instructions/documentation-pass.instructions.md` followed in full for all code-writing changes.
	- Developer-level comments added for every new or modified component, hook, validation helper, type, method, constructor, and non-trivial callback.
	- Tests cover successful submission, client-side validation, server validation problems, API-unconfigured state, and safe unexpected failure messages.
	- Playwright journey submits a valid mocked extraction request and sees the accepted run identity.
	- Wiki review/update completed for the new submission workflow and explicit-solution-path terminology.
	- Can execute end-to-end via focused frontend test and Playwright commands.
	- Executor must not stop mid-Work Item except for full completion, explicit user interruption, or a true blocker.
	- [x] Task 1: Design form state and validation behavior - Completed
	- [x] Define a documented form state shape for repository root, solution paths, optional context fields, metadata, validation messages, and submission state.
	- [x] Add helper functions for trimming, normalizing empty optional values, and validating required values without making filesystem claims.
	- [x] Keep client-side validation limited to convenience checks; do not duplicate server path-existence rules in browser code.
  - [x] Task 2: Render the new extraction form - Completed
	- [x] Add accessible labels for every field.
	- [x] Add keyboard-operable add/remove controls for solution path rows.
	- [x] Add explanatory text that relative solution paths resolve against the submitted repository root.
	- [x] Disable or guard submit while a mutation is in flight.
	- [x] Show API-unconfigured or not-ready status using existing connectivity state without exposing raw configuration values.
  - [x] Task 3: Add mutation hook and state handoff - Completed
	- [x] Create a documented hook such as `useStartExtraction` that calls `archonApiClient.startExtraction`.
	- [x] Invalidate or refresh extraction history after accepted submission.
	- [x] Select or focus the accepted run in local Extraction Center state.
	- [x] Register the accepted run for later background monitoring if it is non-terminal.
  - [x] Task 4: Display accepted run summary - Completed
	- [x] Reuse the run detail shell from Work Item 1 or create a minimal documented accepted-run summary.
	- [x] Show run ID, status, started timestamp, progress stage/message, warning count, error count, and snapshot identity if present.
	- [x] Avoid fabricating warning/error details when only counts are present.
  - [x] Task 5: Add tests - Completed
	- [x] Unit-test form validation and request mapping.
	- [x] Component-test successful accepted response and validation problem response.
	- [x] Playwright-test the valid submit path with mocked API responses.
	- [x] Assert unsafe diagnostic redaction in validation and submission failure states.
  - **Completion Summary**: Implemented the mutation-driven Extraction Center start slice with a documented form-state/request-mapping helper, start-extraction mutation hook, accessible request form, accepted-run summary, safe server validation mapping, API connectivity guard, local accepted-run selection, history/run cache invalidation, and safe notification feedback. The form submits through the typed `POST /extractions` API client path and preserves values after validation or submission failure.
  - **Validation Performed**: `cd .\src\ArchonExplorer; npm run typecheck` passed; `npm run test -- src/test/extraction-center` passed; `npm run build` passed; `npm run test:e2e -- src/test-e2e/extraction-center.spec.ts` passed; final `npm run typecheck` passed after fixes.
  - **Wiki Review Result**: Updated `wiki/archonexplorer-frontend-foundation.md`, `wiki/api-extraction-workflow.md`, `wiki/validation-and-test-workflows.md`, and `wiki/glossary.md`. Wiki impact matrix: affected concepts were Extraction Center start submission, explicit solution paths, accepted run summary, browser/server validation boundary, safe submission feedback, and focused frontend validation; pages reviewed were `wiki/archonexplorer-frontend-foundation.md`, `wiki/api-extraction-workflow.md`, `wiki/validation-and-test-workflows.md`, `wiki/glossary.md`, and `wiki/home.md`; pages updated were the first four; pages created were none; pages intentionally unchanged included `wiki/home.md` because it remains a concise landing page and the current reader path already links to the correct topic pages; page-structure decision was to keep the current submission/history guidance in `wiki/archonexplorer-frontend-foundation.md` and API validation guidance in `wiki/api-extraction-workflow.md`, deferring a dedicated Extraction Center topic page until selected-run polling, background monitoring, duplicate request, or snapshot handoff behavior adds enough workflow depth.
  - **Files**:
	- `src/ArchonExplorer/src/components/extraction-center/ExtractionRequestForm.tsx`: New request form.
	- `src/ArchonExplorer/src/components/extraction-center/ExtractionRunSummary.tsx`: Accepted-run or selected-run summary.
	- `src/ArchonExplorer/src/hooks/useStartExtraction.ts`: New mutation hook.
	- `src/ArchonExplorer/src/components/extraction-center/extractionFormState.ts`: Form-state and validation helpers if useful.
	- `src/ArchonExplorer/src/test/extraction-center/*`: Form, mutation, and safe-error tests.
	- `src/ArchonExplorer/src/test-e2e/extraction-center.spec.ts`: Submission journey expansion.
	- `wiki/api-extraction-workflow.md`: Review for explicit-solution-path and request-contract alignment.
	- `wiki/archonexplorer-frontend-foundation.md` or a new `wiki/archonexplorer-extraction-center.md`: Update with current UI workflow guidance as appropriate.
  - **Work Item Dependencies**: Depends on Work Item 1.
  - **Run / Verification Instructions**:
	- `cd .\src\ArchonExplorer`
	- `npm run typecheck`
	- `npm run test -- src/test/extraction-center`
	- `npm run build`
	- `npm run test:e2e -- src/test-e2e/extraction-center.spec.ts`
  - **User Instructions**: If manual backend validation is desired after automated work completes, the user should run the Aspire AppHost and submit a disposable local repository/solution path through the UI.

## 3. Selected Run Detail and Polling Slice

- [x] Work Item 3: Poll selected extraction status and show detailed terminal output - Completed
  - **Purpose**: Turn accepted and selected runs into a live operational monitor. The UI polls `GET /extractions/{runId}` for active runs, stops at terminal statuses, and displays progress, timings, produced snapshot identity, and safe persistence diagnostics.
  - **Acceptance Criteria**:
	- Selecting a run from history or successful submission loads run status through the typed API client and existing polling conventions.
	- Active non-terminal statuses poll with bounded intervals and cancellation.
	- Polling stops for completed, failed, cancelled/canceled, unavailable, unknown terminal, or other terminal statuses selected during implementation.
	- The detail panel displays run ID, status, submitted request summary, timestamps, progress stage/message/percentage, warning count, error count, top-level timings, produced snapshot identity, and persistence diagnostics when available.
	- The UI distinguishes loading, refetching, failed-request, run-not-found, unavailable, active, and terminal states.
	- The UI does not fabricate individual warning/error details when the API exposes only counts.
  - **Definition of Done**:
	- Run detail component and polling integration implemented.
	- `./.github/instructions/documentation-pass.instructions.md` followed in full for code-writing changes.
	- Developer-level comments added for every new or modified component, hook, helper, type, method, constructor, and non-trivial callback.
	- Unit tests cover terminal-status detection, status rendering, persistence diagnostics display, unavailable states, and safe error output.
	- Playwright journey covers queued/running to completed transition with mocked status responses.
	- Wiki review/update completed for polling, terminal status, produced snapshot identity, and safe diagnostics terminology.
	- Can execute end-to-end through focused tests and browser journey.
	- Executor must not stop mid-Work Item except for full completion, explicit user interruption, or a true blocker.
	- [x] Task 1: Re-inspect and extend polling hooks only where needed - Completed
	- [x] Confirm `useExtractionRunPolling` already supports selected-run polling and cancellation.
	- [x] Add wrapper or feature hook only if the feature needs selected-run-specific state that the existing hook should not own.
	- [x] Preserve bounded interval behavior and terminal stop conditions.
  - [x] Task 2: Implement selected-run detail surface - Completed
	- [x] Render submitted request summary without exposing metadata values if only metadata keys are intended to be safe.
	- [x] Render progress stage, message, percentage, and last-updated timestamp using accessible text and progress semantics when applicable.
	- [x] Render timings in a compact table/list with safe stage names and durations.
	- [x] Render persistence diagnostic counts and timings when present.
	- [x] Render a safe explanation when diagnostic details are unavailable beyond counts.
  - [x] Task 3: Wire selection from history and accepted submission - Completed
	- [x] Selecting a history row updates selected run ID and focuses the run detail region.
	- [x] Successful submission selects the accepted run and starts polling when appropriate.
	- [x] Missing or malformed run IDs produce safe unavailable states instead of uncaught errors.
  - [x] Task 4: Add focused tests - Completed
	- [x] Unit-test active and terminal status mapping.
	- [x] Component-test detail rendering for queued, running, completed, failed, not-found, and persistence-diagnostic cases.
	- [x] Playwright-test status transition from running to completed.
	- [x] Verify no unsafe diagnostics appear in rendered output.
  - **Completion Summary**: Implemented selected-run monitoring for Extraction Center with local selected-run ID state, history row selection, accepted-run auto-selection, `useExtractionRunPolling` integration, safe polling error exposure, and the new documented `ExtractionRunDetail` component. The detail surface shows loading, active, refetching, terminal, not-found/unavailable, request summary, progress, top-level timings, produced snapshot identity, and persistence diagnostics without displaying metadata values or fabricating warning/error details.
  - **Validation Performed**: `cd .\src\ArchonExplorer; npm run typecheck` passed; `npm run test -- extraction-center` passed; `npm run test:e2e -- src/test-e2e/extraction-center.spec.ts` passed with the running-to-completed polling journey; `npm run build` passed.
  - **Wiki Review Result**: Updated `wiki/archonexplorer-frontend-foundation.md`, `wiki/api-extraction-workflow.md`, `wiki/glossary.md`, and `wiki/validation-and-test-workflows.md`. Wiki impact matrix: affected concepts were selected run detail, bounded polling, terminal status, produced snapshot identity, persistence diagnostics, metadata-key-only request summaries, deterministic polling test doubles, and focused browser validation; pages reviewed were `wiki/archonexplorer-frontend-foundation.md`, `wiki/api-extraction-workflow.md`, `wiki/glossary.md`, `wiki/validation-and-test-workflows.md`, and `wiki/home.md`; pages updated were the first four; pages created were none; pages intentionally unchanged included `wiki/home.md` because the existing reader path already links to the frontend and API workflow topic pages; page-structure decision was to keep current selected-run monitoring guidance in the frontend foundation and API extraction workflow pages because the feature is still one page-level monitor, while deferring a dedicated Extraction Center topic page until later background monitoring, duplicate request, produced-snapshot handoff, or cross-feature snapshot context workflows add enough workflow depth.
  - **Files**:
	- `src/ArchonExplorer/src/components/extraction-center/ExtractionRunDetail.tsx`: Detailed monitor.
	- `src/ArchonExplorer/src/components/extraction-center/ExtractionRunTimings.tsx`: Timing summary if separated.
	- `src/ArchonExplorer/src/components/extraction-center/ExtractionPersistenceDiagnostics.tsx`: Persistence diagnostics if separated.
	- `src/ArchonExplorer/src/hooks/useExtractionRunPolling.ts`: Reuse or minimal extension if needed.
	- `src/ArchonExplorer/src/test/extraction-center/*`: Polling/detail tests.
	- `src/ArchonExplorer/src/test-e2e/extraction-center.spec.ts`: Status transition journey.
	- `wiki/api-extraction-workflow.md`: Review for status/persistence diagnostic wording.
	- `wiki/glossary.md`: Review for terms such as terminal status, polling helper, and produced snapshot.
  - **Work Item Dependencies**: Depends on Work Items 1 and 2.
  - **Run / Verification Instructions**:
	- `cd .\src\ArchonExplorer`
	- `npm run typecheck`
	- `npm run test -- src/test/extraction-center`
	- `npm run build`
	- `npm run test:e2e -- src/test-e2e/extraction-center.spec.ts`
  - **User Instructions**: Manual live polling can be verified later against a disposable local extraction run through Aspire if the user chooses.

## 4. Duplicate Request and Produced Snapshot Handoff Slice

- [x] Work Item 4: Duplicate prior requests and expose safe produced-snapshot handoff - Completed
  - **Purpose**: Add operational follow-up actions that help users iterate on extraction requests and recognize the snapshot produced by completed runs without implementing WP005 Snapshot Admin or WP006 Snapshot Context.
  - **Acceptance Criteria**:
	- Users can duplicate a previous request into the form when enough submitted request data is available from selected run status.
	- Duplicate action never automatically submits the copied request.
	- Duplicate behavior never scans the repository for solution files.
	- If history alone lacks enough data, the duplicate action uses selected run status or explains which values must be re-entered.
	- Completed runs with `snapshotIdentity` display an open-produced-snapshot action.
	- If full snapshot context is unavailable, open-produced-snapshot shows an honest placeholder or notification explaining the later WP006 boundary.
	- The action does not query graph data, dashboard metrics, search, lenses, or visualizations.
  - **Definition of Done**:
	- Duplicate request and snapshot handoff actions implemented with safe copy and honest boundaries.
	- `./.github/instructions/documentation-pass.instructions.md` followed in full for code-writing changes.
	- Developer-level comments added for every new or modified component, hook, helper, method, constructor, and non-trivial callback.
	- Tests cover complete duplication, unavailable duplication, metadata-key/value safety, produced-snapshot placeholder, and no accidental graph/search calls.
	- Playwright journey covers duplication from selected run and produced-snapshot placeholder action.
	- Wiki review/update completed for duplicate-request workflow and snapshot handoff terminology.
	- Can execute end-to-end through focused tests and browser journey.
	- Executor must not stop mid-Work Item except for full completion, explicit user interruption, or a true blocker.
	- [x] Task 1: Implement duplicate request source selection - Completed
	- [x] Prefer selected run status response when reconstructing a previous request.
	- [x] Fall back to history row only if it safely contains enough values.
	- [x] Disable the duplicate action with explanatory text when required values are absent.
	- [x] Preserve optional context fields only when they are available and safe.
  - [x] Task 2: Populate the form without submitting - Completed
	- [x] Copy repository root and explicit solution paths into form state.
	- [x] Copy optional branch, commit, requested-by, and metadata values only according to current API safety rules.
	- [x] Move focus to the form summary or first changed field after duplication.
	- [x] Keep validation visible if duplicated values are incomplete and require user correction.
  - [x] Task 3: Implement produced-snapshot affordance - Completed
	- [x] Render produced snapshot identity for completed runs when present.
	- [x] Add an open-produced-snapshot action that uses any existing snapshot context provider only if one exists at implementation time.
	- [x] Otherwise publish a safe notification and persistent inline explanation that WP006 owns complete snapshot context.
	- [x] Ensure the action does not call dashboard, search, graph, lens, or snapshot-delete routes.
  - [x] Task 4: Add focused tests - Completed
	- [x] Component-test duplication from selected run status.
	- [x] Component-test disabled duplication from compact history when values are insufficient.
	- [x] Component-test produced-snapshot placeholder and notification behavior.
	- [x] Playwright-test duplicate action and snapshot handoff placeholder.
  - **Completion Summary**: Implemented duplicate-request and produced-snapshot handoff actions inside the existing Extraction Center detail/form flow. The duplicate workflow maps selected-run status into editable form state without submitting, disables duplication when only compact history is available or required values are absent, preserves safe optional context fields, omits metadata values because status exposes metadata keys only, and returns focus to the persistent form summary when possible. Completed runs with `snapshotIdentity` now show an open-produced-snapshot placeholder action that publishes safe notification feedback and persistent inline WP006 boundary copy without calling graph, search, dashboard, lens, visualization, or snapshot-delete routes.
  - **Validation Performed**: `cd .\src\ArchonExplorer; npm run typecheck` passed; `npm run test -- src/test/extraction-center` passed; `npm run build` passed; `npm run test:e2e -- src/test-e2e/extraction-center.spec.ts` passed with six focused browser journeys covering history, duplicate, snapshot placeholder, polling, empty history, and submission.
  - **Wiki Review Result**: Created `wiki/archonexplorer-extraction-center.md` and updated `wiki/archonexplorer-frontend-foundation.md`, `wiki/api-extraction-workflow.md`, `wiki/glossary.md`, `wiki/validation-and-test-workflows.md`, and `wiki/home.md`. Wiki impact matrix: affected concepts were duplicate request reconstruction, selected-run-status source selection, metadata-key/value safety, compact-history insufficiency, produced snapshot identity, snapshot context boundary, safe placeholder notification, and focused frontend/browser validation; pages reviewed were `wiki/archonexplorer-frontend-foundation.md`, `wiki/api-extraction-workflow.md`, `wiki/glossary.md`, `wiki/validation-and-test-workflows.md`, and `wiki/home.md`; pages updated were those five reviewed pages; page created was `wiki/archonexplorer-extraction-center.md`; pages intentionally unchanged included unrelated architecture/runtime/persistence pages because this slice changes frontend workflow behavior rather than backend layering or storage semantics; page-structure decision was to create a dedicated Extraction Center topic because duplicate request and produced-snapshot handoff added enough workflow depth to outgrow the frontend foundation page, while `wiki/home.md` remained a concise landing page with only a reader-path link.
  - **Files**:
	- `src/ArchonExplorer/src/components/extraction-center/ExtractionRunActions.tsx`: Duplicate and snapshot actions if separated.
	- `src/ArchonExplorer/src/components/extraction-center/extractionRequestMapping.ts`: Safe mapping helpers if useful.
	- `src/ArchonExplorer/src/components/extraction-center/ExtractionRequestForm.tsx`: Form population integration.
	- `src/ArchonExplorer/src/test/extraction-center/*`: Action and mapping tests.
	- `src/ArchonExplorer/src/test-e2e/extraction-center.spec.ts`: Duplicate and snapshot placeholder journey.
	- `wiki/archonexplorer-frontend-foundation.md` or `wiki/archonexplorer-extraction-center.md`: Update with current action behavior.
	- `wiki/glossary.md`: Review for `produced snapshot` and `snapshot context` terms.
  - **Work Item Dependencies**: Depends on Work Items 2 and 3.
  - **Run / Verification Instructions**:
	- `cd .\src\ArchonExplorer`
	- `npm run typecheck`
	- `npm run test -- src/test/extraction-center`
	- `npm run build`
	- `npm run test:e2e -- src/test-e2e/extraction-center.spec.ts`
  - **User Instructions**: None beyond optional manual Aspire smoke testing after implementation.

## 5. Background Monitor, Notifications, and Command Palette Slice

- [x] Work Item 5: Surface active extraction runs in the workbench bottom panel and commands - Completed
  - **Purpose**: Complete the workbench-native behavior for long-running extraction. Active runs remain visible outside the Extraction Center, completion/failure appears through safe notifications, and the command palette can open or focus extraction workflows.
  - **Acceptance Criteria**:
	- Non-terminal accepted or selected runs appear in the bottom-panel background monitor.
	- The bottom-panel monitor shows run ID, status, progress stage or message, and completion/failure state using safe text.
	- Users can navigate away from Extraction Center while a tracked run remains visible.
	- Selecting a bottom-panel run returns to Extraction Center and focuses that run detail.
	- Terminal runs remain until acknowledged or until a documented retention rule removes them.
	- Completion, failure, cancellation, and unavailable states can publish safe notifications through the existing notification runtime.
	- Command palette includes Extraction Center commands such as open Extraction Center, focus new request form, refresh history, and focus active background run where practical.
  - **Definition of Done**:
	- Bottom-panel monitor, notification triggers, and command palette integration implemented.
	- `./.github/instructions/documentation-pass.instructions.md` followed in full for code-writing changes.
	- Developer-level comments added for every new or modified component, hook, helper, type, method, constructor, and non-trivial callback.
	- Tests cover tracking, acknowledgment/retention, bottom-panel navigation, notifications, and command execution.
	- Playwright journey keeps a long-running mocked extraction visible while another activity is selected.
	- Accessibility checks cover keyboard navigation for bottom-panel run items and command palette extraction commands.
	- Wiki review/update completed for background monitor, notification, command, and workbench extension behavior.
	- Can execute end-to-end through focused tests and browser journey.
	- Executor must not stop mid-Work Item except for full completion, explicit user interruption, or a true blocker.
	- [x] Task 1: Add background run tracking state - Completed
	- [x] Extend the workbench store or add a documented feature store for tracked extraction run IDs and acknowledgement state.
	- [x] Keep full server responses in TanStack Query rather than duplicating them in local state.
	- [x] Define a clear terminal-run retention or acknowledgement rule.
  - [x] Task 2: Render bottom-panel extraction run monitor - Completed
	- [x] Replace or extend the existing Extraction Runs placeholder in `BottomPanel` with safe active-run summaries.
	- [x] Display status and progress text without relying on color alone.
	- [x] Provide keyboard-accessible run selection and acknowledgement controls.
	- [x] Preserve safe diagnostics boundary and do not display raw backend details.
  - [x] Task 3: Add notification behavior - Completed
	- [x] Detect accepted, completed, failed, cancelled, and unavailable transitions without duplicate notification spam.
	- [x] Use the existing notification runtime helpers rather than introducing another toast package.
	- [x] Keep persistent errors visible in Extraction Center and use notifications only as supplemental transient feedback.
  - [x] Task 4: Add command palette integration - Completed
	- [x] Add extraction commands to the existing workbench command model.
	- [x] Implement open/focus Extraction Center and focus form commands.
	- [x] Implement refresh history and focus active background run commands where current shell extension points permit.
	- [x] Disable or explain commands whose prerequisites are missing.
  - [x] Task 5: Add focused tests - Completed
	- [x] Unit/component-test background tracking and terminal acknowledgement.
	- [x] Component-test bottom-panel run selection and safe display.
	- [x] Component-test notification transition logic.
	- [x] Playwright-test background monitor while navigating away from Extraction Center.
	- [x] Playwright-test command palette Extraction Center commands.
  - **Completion Summary**: Implemented shared Extraction Center workflow state in `src/ArchonExplorer/src/state/extractionCenterStore.tsx`, bottom-panel monitoring in `src/ArchonExplorer/src/components/extraction-center/ExtractionBackgroundMonitor.tsx` and `src/ArchonExplorer/src/components/workbench/BottomPanel.tsx`, store composition in `src/ArchonExplorer/src/components/workbench/WorkbenchShell.tsx`, command registration in `src/ArchonExplorer/src/components/workbench/workbenchCommands.ts` and `src/ArchonExplorer/src/components/workbench/CommandPalette.tsx`, and page integration in `src/ArchonExplorer/src/components/extraction-center/ExtractionCenter.tsx`. Local feature state stores run identifiers, acknowledgement flags, focus/refresh intents, and notification status memory only; status responses remain TanStack Query server state. Terminal tracked runs remain visible in the bottom panel until acknowledged. Extraction Center commands now open the feature, focus the form, refresh history, and focus a visible tracked run with safe unavailable feedback when prerequisites are missing.
  - **Validation Performed**: `cd .\src\ArchonExplorer; npm run test -- extraction-center` passed with focused background tracking, acknowledgement, monitor, command, and existing Extraction Center coverage; `npm run typecheck` passed; `npm run test:e2e -- src/test-e2e/extraction-center.spec.ts` passed with eight focused browser journeys covering history, bottom-panel background monitoring while navigating away, command palette actions, duplicate, snapshot placeholder, polling, empty history, and submission.
  - **Wiki Review Result**: Updated `wiki/archonexplorer-extraction-center.md`, `wiki/archonexplorer-frontend-foundation.md`, `wiki/validation-and-test-workflows.md`, and `wiki/glossary.md`. Reviewed `wiki/api-extraction-workflow.md` and `wiki/home.md`; `api-extraction-workflow.md` remained unchanged because Work Item 5 changed browser shell behavior over existing extraction routes rather than API contracts, and `home.md` remained unchanged because it already links to the dedicated Extraction Center topic and must stay concise. Wiki impact matrix: affected concepts were background run monitor, tracked run identifier state, terminal acknowledgement, bottom-panel polling, safe transition notifications, Extraction Center command palette actions, command prerequisites, and focused validation; pages reviewed were `wiki/archonexplorer-extraction-center.md`, `wiki/archonexplorer-frontend-foundation.md`, `wiki/api-extraction-workflow.md`, `wiki/validation-and-test-workflows.md`, `wiki/glossary.md`, and `wiki/home.md`; pages updated were `wiki/archonexplorer-extraction-center.md`, `wiki/archonexplorer-frontend-foundation.md`, `wiki/validation-and-test-workflows.md`, and `wiki/glossary.md`; pages created were none; pages intentionally unchanged were `wiki/api-extraction-workflow.md` and `wiki/home.md`; page-structure decision was to keep detailed behavior on the existing dedicated Extraction Center topic because this work expands the same operational workflow, while `wiki/home.md` remains only a landing page and table of contents.
  - **Files**:
	- `src/ArchonExplorer/src/components/workbench/BottomPanel.tsx`: Bottom-panel monitor integration.
	- `src/ArchonExplorer/src/components/extraction-center/ExtractionBackgroundMonitor.tsx`: Monitor component if separated.
	- `src/ArchonExplorer/src/state/workbenchStore.tsx` or `src/ArchonExplorer/src/state/extractionCenterStore.tsx`: Background tracking state.
	- `src/ArchonExplorer/src/components/workbench/workbenchCommands.ts`: Extraction command registration.
	- `src/ArchonExplorer/src/test/extraction-center/*`: Background monitor and notification tests.
	- `src/ArchonExplorer/src/test-e2e/extraction-center.spec.ts`: Navigation and command journeys.
	- `wiki/archonexplorer-frontend-foundation.md` or `wiki/archonexplorer-extraction-center.md`: Update with bottom-panel and command behavior.
  - **Work Item Dependencies**: Depends on Work Items 1 through 4.
  - **Run / Verification Instructions**:
	- `cd .\src\ArchonExplorer`
	- `npm run typecheck`
	- `npm run test -- src/test/extraction-center`
	- `npm run build`
	- `npm run test:e2e -- src/test-e2e/extraction-center.spec.ts`
  - **User Instructions**: Ask the user to perform Aspire-hosted smoke testing only after all automated validation and wiki/documentation gates complete.

## 6. Final Validation, Documentation Pass, and Wiki Completion Gate

- [x] Work Item 6: Complete WP004 validation and record the mandatory wiki impact outcome - Completed
  - **Purpose**: Close the work package by validating the full Extraction Center feature, applying the mandatory documentation-pass standard, and completing the wiki information-architecture review required by `./.github/instructions/wiki.instructions.md`.
  - **Acceptance Criteria**:
	- Frontend typecheck, focused tests, production build, and focused Playwright journeys pass.
	- The final implementation contains no direct component-level `fetch` calls for ArchonApi extraction operations.
	- The UI never renders raw stack traces, connection strings, environment variables, credentials, raw Cypher, Neo4j internal identifiers, driver-specific diagnostics, or arbitrary backend exception text.
	- The final implementation does not introduce snapshot deletion, graph queries, search, dashboard metrics, visualisations, lenses, automatic solution scanning, arbitrary filesystem browsing, authentication, or authorization.
	- Source-code documentation satisfies `./.github/instructions/documentation-pass.instructions.md` for every file touched by the work package.
	- Wiki review is complete and recorded with a page-structure decision, pages reviewed, pages updated, pages created, pages intentionally unchanged, and rationale.
  - **Definition of Done**:
	- All Work Items 1 through 5 complete and stable.
	- `./.github/instructions/documentation-pass.instructions.md` compliance verified for all new and modified source files.
	- `./.github/instructions/wiki.instructions.md` compliance verified and recorded.
	- Wiki pages updated or a precise no-change result recorded; `wiki/home.md` remains a concise landing page.
	- Any new or updated wiki content for architecture, runtime, workflow, setup, extension, or other conceptually dense topics uses book-like narrative prose, defines technical terms, and includes relevant examples or walkthroughs.
	- Final plan-status entry or completion record links to wiki guidance instead of duplicating contributor-facing guidance.
	- Executor must not stop mid-Work Item except for full completion, explicit user interruption, or a true blocker.
	- [x] Task 1: Run final focused validation - Completed
	- [x] Run `npm run typecheck` from `src/ArchonExplorer`.
	- [x] Run `npm run test` from `src/ArchonExplorer`.
	- [x] Run `npm run build` from `src/ArchonExplorer`.
	- [x] Run `npm run test:e2e -- src/test-e2e/extraction-center.spec.ts` from `src/ArchonExplorer`.
	- [x] Run or inspect existing workbench shell Playwright coverage if shell integration changed significantly.
  - [x] Task 2: Perform documentation-pass verification - Completed
	- [x] Review every touched source file against `./.github/instructions/documentation-pass.instructions.md`.
	- [x] Confirm every new/modified class, component, hook, helper, method, constructor, reducer, and non-trivial callback has developer-level documentation.
	- [x] Confirm public parameters and non-obvious properties are documented where applicable.
	- [x] Confirm inline/block comments explain non-obvious flow, validation, polling, notification, and safety decisions.
  - [x] Task 3: Perform wiki information-architecture review - Completed
	- [x] Review `wiki/archonexplorer-frontend-foundation.md` for current frontend/runtime/shell guidance.
	- [x] Review `wiki/api-extraction-workflow.md` for extraction request, status, polling, and diagnostics alignment.
	- [x] Review `wiki/validation-and-test-workflows.md` for frontend and Playwright validation command coverage.
	- [x] Review `wiki/glossary.md` for Extraction Center, background monitor, produced snapshot, terminal status, and safe diagnostic terminology.
	- [x] Review `wiki/home.md` only to determine whether it needs a short link/orientation update; do not place detailed feature guidance there.
	- [x] Decide whether to create `wiki/archonexplorer-extraction-center.md` as a dedicated topic page or keep Extraction Center guidance in `wiki/archonexplorer-frontend-foundation.md` with clear cross-links.
  - [x] Task 4: Apply required wiki updates - Completed
	- [x] Update or create the selected topic page with current-state, book-like narrative guidance for Extraction Center if the implementation changes contributor-facing behavior.
	- [x] Include examples or walkthroughs showing how a contributor opens Extraction Center, starts a run with explicit solution paths, watches polling, uses the bottom panel, and interprets produced snapshot identity.
	- [x] Define technical terms inline or link to `wiki/glossary.md`.
	- [x] Add cross-links between frontend, API extraction workflow, validation, glossary, and any new Extraction Center topic page.
	- [x] Keep `wiki/home.md` concise and only add a short topic link if a new page is created or the reading path must change.
  - [x] Task 5: Record final wiki impact matrix in the plan-status/completion record - Completed
	- [x] Record affected concepts.
	- [x] Record pages reviewed.
	- [x] Record pages updated.
	- [x] Record pages created.
	- [x] Record pages intentionally unchanged with rationale.
	- [x] Record the page-structure decision and why `wiki/home.md` remains readable.
	- [x] Record any retired implementation-note-style artifacts if discovered.
  - **Completion Summary**: Completed the WP004 final gate by validating the full ArchonExplorer Extraction Center feature, checking direct route/fetch and excluded-feature boundaries, verifying documentation-pass coverage for WP004 source and test files, correcting the isolated bottom-panel provider setup in `src/ArchonExplorer/src/test/workbench/workbenchShell.test.tsx`, and correcting the focused Extraction Center test grouping in `src/ArchonExplorer/src/test/extraction-center/extractionCenter.test.tsx`. The final implementation keeps extraction operations behind typed client hooks, preserves safe diagnostic presentation, and does not introduce snapshot deletion, graph/search/dashboard/lens/visualization routes, automatic scanning, arbitrary filesystem browsing, authentication, or authorization.
  - **Validation Performed**: `cd .\src\ArchonExplorer; npm run typecheck` passed; `npm run test` passed with 127 Vitest tests; `npm run build` passed; `npm run test:e2e -- src/test-e2e/extraction-center.spec.ts` passed with 8 focused browser journeys. Existing workbench shell browser coverage was reviewed through the current Extraction Center Playwright journey because Work Item 6 did not add new shell behavior beyond validating the already-implemented background monitor and command palette integration.
  - **Wiki Review Result**: Updated `wiki/archonexplorer-extraction-center.md`, `wiki/archonexplorer-frontend-foundation.md`, `wiki/glossary.md`, and `wiki/home.md`. Reviewed `wiki/api-extraction-workflow.md` and `wiki/validation-and-test-workflows.md`; both remained structurally sufficient because their current extraction route, polling, diagnostics, frontend validation, and Playwright command guidance already matched the final implementation. Wiki impact matrix: affected concepts were final Extraction Center validation gate, provider-backed bottom-panel monitor composition, full current Extraction Center capability summary, workbench shell boundary wording, safe request duplication, produced snapshot placeholder handoff, and no-common-`/api` route/safety boundaries; pages reviewed were `wiki/archonexplorer-extraction-center.md`, `wiki/archonexplorer-frontend-foundation.md`, `wiki/api-extraction-workflow.md`, `wiki/validation-and-test-workflows.md`, `wiki/glossary.md`, and `wiki/home.md`; pages updated were `wiki/archonexplorer-extraction-center.md`, `wiki/archonexplorer-frontend-foundation.md`, `wiki/glossary.md`, and `wiki/home.md`; pages created were none because the dedicated Extraction Center topic already exists; pages intentionally unchanged were `wiki/api-extraction-workflow.md` and `wiki/validation-and-test-workflows.md` because they already cover the current server and validation contracts; page-structure decision was to keep detailed workflow guidance on `wiki/archonexplorer-extraction-center.md`, keep foundational runtime/shell guidance on `wiki/archonexplorer-frontend-foundation.md`, and keep `wiki/home.md` as a concise landing page with only a current-capability summary adjustment. No implementation-note-style artifacts were discovered or retired.
  - **Files**:
	- `docs/023-Extraction-Center/plan-wp004-extraction-center.md`: Concise status and validation outcomes only; no contributor-facing implementation ledger.
	- `wiki/archonexplorer-frontend-foundation.md`: Review/update likely required.
	- `wiki/api-extraction-workflow.md`: Review/update likely required.
	- `wiki/validation-and-test-workflows.md`: Review/update if commands or test workflow changed.
	- `wiki/glossary.md`: Review/update likely required for new terms.
	- `wiki/home.md`: Review only; update only with concise link/orientation if a new topic page is created.
	- `wiki/archonexplorer-extraction-center.md`: Create only if information-architecture review determines a dedicated topic page is clearer.
  - **Work Item Dependencies**: Depends on Work Items 1 through 5.
  - **Run / Verification Instructions**:
	- `cd .\src\ArchonExplorer`
	- `npm run typecheck`
	- `npm run test`
	- `npm run build`
	- `npm run test:e2e -- src/test-e2e/extraction-center.spec.ts`
  - **User Instructions**: After the work package is complete, ask the user to perform an Aspire-hosted manual smoke test if they want live local integration validation. Do not run the Aspire AppHost automatically for smoke testing.

## Cross-Cutting Implementation Requirements

### Source-Code Documentation Requirements

Every code-writing Work Item must comply with `./.github/instructions/documentation-pass.instructions.md`. The executor must:

- add or preserve developer-level comments on every new or modified class, component, hook, helper, method, constructor, reducer, and non-trivial callback;
- document public parameters where applicable;
- document internal and non-public types with the same care as public types;
- comment properties whose meaning is not obvious from the name;
- explain polling, validation, notification, request mapping, and safety flow with inline or block comments where needed;
- avoid broad formatting-only edits unrelated to the task;
- treat missing documentation as a Definition of Done failure, not optional polish.

### Wiki Maintenance Requirements

Every Work Item must follow `./.github/instructions/wiki.instructions.md`. The implementation must:

- perform a wiki review for every Work Item;
- update the wiki whenever developer-facing behavior, architecture, runtime composition, workflow, terminology, setup, validation, or contributor guidance changes or is materially clarified;
- record when no wiki update is needed, with the pages reviewed and the reason existing guidance remains sufficient;
- route contributor-facing guidance to the correct topic page or a newly created page;
- keep `wiki/home.md` limited to orientation and links;
- avoid standalone implementation notes or ledgers;
- use long-form, book-like narrative prose for dense architecture, runtime, workflow, setup, extension, and validation topics;
- define technical terms when first introduced or link to glossary entries;
- include examples or walkthroughs when they materially improve contributor understanding;
- include a final wiki impact matrix or equivalent final record.

### UI Styling and Accessibility Requirements

The implementation must:

- use existing shadcn/ui-compatible primitives and local component conventions;
- avoid introducing another ordinary UI component library;
- avoid custom theme colors, custom type scales, custom button treatments, card-like marketing visual treatments, or bespoke web-page styling;
- keep the presentation plain and desktop-IDE-style;
- use semantic HTML and accessible names for form fields, run rows, action buttons, progress/status indicators, bottom-panel items, and command-palette actions;
- avoid relying on color alone for lifecycle states;
- preserve visible focus and predictable focus transitions.

### Safety Requirements

The implementation must not expose:

- raw stack traces;
- connection strings;
- environment variable values;
- credentials, tokens, bearer values, or API keys;
- raw Cypher;
- Neo4j internal identifiers;
- driver-specific diagnostics;
- arbitrary backend exception text;
- metadata values when the API intentionally exposes only metadata keys.

The implementation must not add:

- snapshot deletion or delete-all behavior;
- graph queries or graph rendering;
- global search over snapshots;
- dashboard metric loading;
- visual lenses, matrices, Sankey views, or visual analytics;
- automatic recursive repository scanning;
- arbitrary filesystem browsing;
- authentication or authorization.

## Appendix A - Architecture

### Overall Technical Approach

WP004 is a frontend vertical-slice implementation over the existing ArchonApi operational extraction endpoints. The backend already exposes extraction start, status, and history contracts, and the frontend already contains a route catalog, typed operational client, safe request executor, TanStack Query provider, polling helper, safe error model, notification runtime, deterministic test doubles, and desktop workbench shell. The technical approach is therefore to add feature UI and workbench integration while reusing the runtime foundations.

The main control flow is:

```mermaid
flowchart LR
  User[User] --> Shell[Workbench Shell]
  Shell --> Center[Extraction Center]
  Center --> Form[New Extraction Form]
  Form --> Mutation[useStartExtraction]
  Mutation --> Client[ArchonApiClient]
  Client --> Api[POST /extractions]
  Api --> Accepted[Accepted Run Status]
  Accepted --> Detail[Run Detail]
  Detail --> Polling[useExtractionRunPolling]
  Polling --> StatusApi[GET /extractions/{runId}]
  Center --> History[Run History]
  History --> HistoryHook[useExtractionHistory]
  HistoryHook --> HistoryApi[GET /extractions]
  Detail --> Bottom[Bottom Panel Monitor]
  Detail --> Notify[Notification Runtime]
```

The browser owns local UI state such as form edits, selected run ID, active workbench activity, open workbench tabs, and tracked background run IDs. ArchonApi owns extraction server state such as accepted runs, lifecycle status, progress, timings, warning/error counts, produced snapshot identity, and run history. TanStack Query stores cached copies of server state and remains the only server-state mechanism for the feature.

### Frontend

The frontend implementation lives in `src/ArchonExplorer`. It should introduce a focused Extraction Center feature area under `src/ArchonExplorer/src/components/extraction-center` and feature hooks under `src/ArchonExplorer/src/hooks` where that matches current conventions. The workbench shell under `src/ArchonExplorer/src/components/workbench` should receive only the integration changes needed to render the feature, show background run summaries, and expose command palette actions.

Primary frontend components and responsibilities:

- `ExtractionCenter`: feature composition root that arranges the request form, history list, selected run detail, and local feature state.
- `ExtractionRequestForm`: accessible form that maps user input to `StartExtractionRequest` without scanning the repository.
- `ExtractionRunHistory`: API-backed history list from `GET /extractions`.
- `ExtractionRunDetail`: selected-run monitor from `GET /extractions/{runId}`.
- `ExtractionPersistenceDiagnostics`: optional display for safe persistence counts and timings.
- `ExtractionBackgroundMonitor`: bottom-panel contribution for tracked active and terminal runs.
- `useExtractionHistory`: TanStack Query hook for recent history.
- `useStartExtraction`: TanStack Query mutation hook for `POST /extractions`.
- `useExtractionRunPolling`: existing hook to reuse or minimally extend for selected/background status polling.

The form and history surfaces should be useful at the end of each Work Item. The first slice displays API-backed history, the second starts runs, the third monitors status, the fourth adds duplicate/snapshot actions, and the fifth completes workbench background behavior.

### Backend

No backend implementation is planned for WP004. The work package consumes the existing ArchonApi extraction endpoints and current frontend API contracts. Implementation must re-inspect the backend endpoint and frontend type definitions before coding because the specification records observed contract shape at plan time, not a license to ignore current code.

Backend data flow currently relevant to WP004:

```mermaid
flowchart TB
  HttpStart[POST /extractions] --> Validate[Application validation]
  Validate --> Accepted[Accepted extraction run]
  Accepted --> Scheduler[Extraction scheduler]
  Scheduler --> Pipeline[Extraction pipeline]
  Pipeline --> Snapshot[Snapshot assembly]
  Snapshot --> Persistence[Persistence writer]
  Persistence --> Status[Run status and snapshot identity]
  HttpStatus[GET /extractions/{runId}] --> Status
  HttpHistory[GET /extractions] --> History[Recent run summaries]
```

If implementation discovers that the backend contract no longer supports a required UI behavior, the executor must first prefer aligning the UI to the current implemented contract. Backend changes are out of scope for this plan unless a true blocker is discovered and the plan is explicitly adapted.

### State and Data Boundaries

Server state belongs in TanStack Query. Local state may include form values, selected run ID, background tracked run IDs, acknowledgement flags, focused region, and view preferences. Local state must not duplicate full API response payloads, raw diagnostics, secrets, architecture facts, graph records, or snapshot lifecycle data.

The open-produced-snapshot action is a boundary marker. WP004 may show the produced snapshot identity and provide an honest handoff affordance, but WP006 owns complete active snapshot context. The action must not query dashboards, search, graph projections, lenses, or snapshot lifecycle deletion routes.

### Test Architecture

Tests should combine:

- Vitest unit tests for validation helpers, request mapping, status normalization, terminal-state behavior, and safe text decisions.
- Component tests or focused React tests for form behavior, history states, run detail, persistence diagnostics, bottom-panel monitor, and command integration where the current test stack supports them.
- Playwright journeys for opening Extraction Center, submitting a mocked valid request, viewing status transitions, viewing history, duplicating a previous request, seeing safe validation errors, and keeping a background run visible while navigating elsewhere.

The implementation should not run the full repository test suite for this work package unless focused validation reveals a cross-cutting problem that requires broader verification.

## Final Summary

This plan delivers WP004 through five implementation slices plus one final validation/wiki gate. The slices intentionally begin with the smallest useful end-to-end behavior, API-backed extraction history, then add submission, polling/detail display, duplicate/snapshot actions, and workbench background integration. The plan preserves the existing API/runtime foundations, avoids premature graph or snapshot-administration scope, and makes wiki maintenance and source-code documentation hard completion gates rather than optional cleanup.
