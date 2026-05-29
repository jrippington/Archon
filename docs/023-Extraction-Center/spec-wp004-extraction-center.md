# WP004 Specification - Extraction Center

## Document Control

| Field | Value |
| --- | --- |
| Work Package | WP004 - Extraction Center |
| Output Path | `docs/023-Extraction-Center/spec-wp004-extraction-center.md` |
| Source Work Package | `docs/foundation/work-packages-ui.md` WP004 |
| Source Brief | `docs/foundation/archon_ui_brief.md` |
| Specification Basis | Same repository work-package specification pattern as `docs/020-ArchonExplorer-Foundation/spec-wp001-archonexplorer-foundation.md`, `docs/021-API-Client-and-Runtime-Foundation/spec-wp002-api-client-and-runtime-foundation.md`, and `docs/022-Workbench-Desktop-Shell/spec-wp003-workbench-desktop-shell.md`. `spec-template_v1.1.md` was requested by the prompt but is not present in the workspace. |
| API Route Basis | Existing ArchonApi endpoint mappings inspected in `src/Archon.Api.Extraction/ExtractionEndpointRouteBuilderExtensions.cs` and frontend API types inspected in `src/ArchonExplorer/src/api/archonApiTypes.ts`. |
| Status | Draft |
| Audience | Product owner, architect, frontend implementer, API implementer, test engineer |

## 1. Overview

### 1.1 Purpose

This specification defines WP004 for the ArchonExplorer UI work-package sequence. WP004 implements the first real operational workbench feature: an Extraction Center that allows users to start architecture extraction through ArchonApi, monitor run progress, review recent run history, inspect safe operational outcomes, and act on the snapshot identity produced by completed extraction runs.

The package turns the previously established ArchonExplorer shell and runtime foundations into a useful operational workflow while preserving the roadmap rule that extraction and snapshot administration must exist before graph visualisation, lenses, search-driven investigation, and other analytical features.

### 1.2 Background

Archon depends on extracted architecture snapshots before users can search, inspect, visualize, or compare architecture facts. The Extraction Center is therefore a foundational workbench area rather than a secondary administration page. It must make extraction visible and understandable: users should be able to submit explicit repository and solution inputs, see whether work has been accepted, follow queued/running/completed/failed/cancelled state, review safe diagnostics, and understand when a persisted snapshot is available.

WP001 created the ArchonExplorer application foundation. WP002 created the shared frontend API runtime, typed client foundation, route catalog, TanStack Query conventions, polling helpers, safe error shaping, and notification runtime. WP003 created the desktop-style workbench frame with activity navigation, tabs, bottom panel, status bar, command palette, and notification placement. WP004 uses those foundations to add extraction-specific UI and server state without replacing the shell or introducing an unrelated page model.

### 1.3 High-Level Scope

WP004 covers:

- Extraction Center workbench activity and default tab.
- New extraction request form.
- Explicit repository root directory input.
- One or more explicit solution path inputs.
- Optional branch name, commit SHA, requested-by, and metadata-key input support.
- Submission to `POST /extractions` through the WP002 API client.
- Run status polling from `GET /extractions/{runId}`.
- Recent run history from `GET /extractions`.
- Queued, running, completed, failed, cancelled, unavailable, loading, and empty states.
- Run detail display for accepted request summary, progress, timestamps, counts, timings, produced snapshot identity, and persistence diagnostics when available.
- Safe validation and failure messaging.
- Duplicate-previous-request behavior.
- Open-produced-snapshot affordance that hands off to the available snapshot-context path or an honest placeholder if WP006 is not yet implemented.
- Bottom-panel background extraction monitor integration.
- Completion and failure notifications.
- Focused Playwright validation for opening Extraction Center, submitting extraction, monitoring status, reviewing history, and showing safe validation errors.

WP004 excludes snapshot lifecycle deletion, graph queries, search over snapshots, graph rendering, lenses, visual analytics, automatic recursive solution discovery, authentication, authorization, and arbitrary filesystem browsing.

## 2. System Context

### 2.1 Product Context

ArchonExplorer is the browser-delivered workbench for architectural investigation over Archon data. The UI roadmap intentionally delivers operational capabilities before analytical capabilities. Extraction Center is the first feature that makes the workbench operationally useful because it creates the data snapshots that later search, dashboard, graph, evidence, and lens features consume.

The Extraction Center must fit inside the workbench model established by WP003. Starting or monitoring extraction should not feel like leaving the application for a separate admin console. A long-running extraction should remain visible while the user navigates to other workbench areas, and completion/failure should surface through standard workbench notifications and bottom-panel background work affordances.

### 2.2 Source References

WP004 shall align with these source materials:

- `docs/foundation/work-packages-ui.md` WP004 - Extraction Center.
- `docs/foundation/work-packages-ui.md` section 1.1 for operational UI before analytical UI.
- `docs/foundation/work-packages-ui.md` section 1.2 for snapshot context semantics.
- `docs/foundation/work-packages-ui.md` section 1.3 for the no-common-`/api` route convention.
- `docs/foundation/work-packages-ui.md` section 1.4 for workbench-not-admin-console behavior.
- `docs/foundation/work-packages-ui.md` section 1.6 for mandatory toolkit styling constraints.
- `docs/foundation/archon_ui_brief.md` binding UI product mandate for ArchonExplorer, React, TypeScript, shadcn/ui, and Aspire hosting.
- `docs/foundation/archon_ui_brief.md` section 6.5 for workbench desktop shell behavior.
- `docs/foundation/archon_ui_brief.md` section 6.6 for snapshot-context visibility expectations.
- `docs/foundation/archon_ui_brief.md` section 7.8 for Extraction Center behavior.
- `docs/foundation/archon_ui_brief.md` section 14.2 for UI state ownership.
- `docs/foundation/archon_ui_brief.md` section 14.3 for safe empty, loading, and error states.
- `docs/foundation/archon_ui_brief.md` section 14.4 for security and operational boundaries.
- `docs/foundation/archon_ui_brief.md` section 14.5 for UI validation expectations.
- `src/Archon.Api.Extraction/ExtractionEndpointRouteBuilderExtensions.cs` for implemented extraction routes and response shaping.
- `src/ArchonExplorer/src/api/archonApiTypes.ts` for current frontend extraction request and response contracts.

### 2.3 Users and Stakeholders

| Stakeholder | Interest |
| --- | --- |
| Product owner | Confirms the first operational UI slice enables users to create and monitor architecture extraction runs. |
| Architect | Confirms extraction server state, polling, diagnostics, bottom-panel integration, and snapshot handoff align with the workbench architecture. |
| Frontend implementer | Uses the specification to add Extraction Center components, TanStack Query usage, form behavior, bottom-panel integration, and focused tests. |
| API implementer | Confirms the UI consumes the implemented extraction API contract without inventing route shapes or exposing unsafe backend details. |
| Test engineer | Validates extraction submission, status polling, history, safe validation errors, notifications, and background monitor behavior. |
| Future ArchonExplorer user | Can start extraction, understand progress, see outcomes, and know when a produced snapshot can be used for later investigation. |

## 3. Component Summary

### 3.1 Extraction Center Activity

The Extraction Center activity is the main workbench surface for extraction operations. It shall be reachable through the WP003 activity model and should open or focus an Extraction Center tab in the main work area. It shall use the desktop-style workbench frame rather than browser page navigation away from ArchonExplorer.

The activity should organize the user journey around starting new extraction runs, reviewing active or recent runs, and inspecting selected run details. The exact layout may be selected during implementation, but the user must be able to see the new-run workflow and recent run history without confusing either with graph investigation features.

### 3.2 New Extraction Form

The new extraction form gathers the request values accepted by `POST /extractions`: repository root directory, explicit solution paths, optional branch name, optional commit SHA, optional requested-by value, and optional metadata values consistent with the API contract available at implementation time.

The form must not perform recursive repository scanning or automatic solution discovery. Solution paths are explicit user inputs. Relative solution paths should be presented as resolving against the submitted repository root so users understand what will be extracted.

### 3.3 Extraction API Client Integration

WP004 consumes the typed API client and route catalog introduced by WP002. Feature components shall not construct extraction route strings manually or call `fetch` directly. API requests shall use the no-common-`/api` paths implemented by ArchonApi:

```http
POST /extractions
GET  /extractions/{runId}
GET  /extractions
```

Server state shall be represented through TanStack Query queries and mutations using WP002 query-key, retry, cancellation, invalidation, safe-error, and polling conventions.

### 3.4 Run Status Monitor

The run status monitor shows the selected extraction run in detail. It shall display run identifier, lifecycle status, accepted timestamp, completed timestamp when available, progress stage, progress message, optional progress percentage, warning and error counts, timing summary, produced snapshot identity when available, and persistence diagnostics when available.

The monitor shall poll active runs using `GET /extractions/{runId}` and stop polling when the run reaches a terminal state or becomes unavailable according to WP002 polling conventions.

### 3.5 Run History

The run history surface lists recent extraction runs from `GET /extractions`. It shall use a compact table or equivalent workbench list. Users shall be able to select a history item to view details and duplicate a previous request into the new extraction form when the required request values are available from the current API response.

The current history response contains compact run summaries, not necessarily full submitted request bodies. If full duplication cannot be reconstructed from the history row alone, the implementation shall use the selected run status response where available or disable the duplicate action with a safe explanation.

### 3.6 Bottom-Panel Background Run Monitor

WP004 shall integrate active extraction runs into the WP003 bottom panel or background work surface. Long-running extraction should remain visible while users move elsewhere in the workbench. The bottom-panel monitor should show enough information to answer whether work is queued, running, completed, failed, cancelled, or needs attention.

The bottom-panel monitor shall not expose raw backend diagnostics, raw exception messages, connection strings, environment values, raw Cypher, Neo4j internal identifiers, or driver-specific diagnostics.

### 3.7 Notifications

WP004 shall use the WP002 notification runtime and WP003 notification placement for extraction events. Notifications should cover accepted extraction submissions, completed runs, failed runs, cancelled runs, and unavailable run states where those events are visible in the UI.

Notifications shall not be the only representation of persistent errors. The Extraction Center page or run detail surface must retain enough state for users to understand and retry or correct issues after a toast disappears.

### 3.8 Snapshot Handoff Affordance

Completed runs may include a produced snapshot identity. WP004 shall display that identity and provide an open-produced-snapshot action. Because WP006 owns the complete active snapshot context and dashboard behavior, WP004 shall either hand off to the available snapshot context mechanism if implemented or show an honest placeholder explaining that produced snapshot opening is completed by a later work package.

WP004 shall not implement graph queries, dashboard metrics, global search, or visual investigation over the produced snapshot.

## 4. Functional Requirements

### 4.1 Workbench Activity Integration

| ID | Requirement |
| --- | --- |
| FR-001 | ArchonExplorer shall include an Extraction Center workbench activity. |
| FR-002 | The Extraction Center activity shall be reachable from the workbench activity bar or equivalent WP003 activity registration surface. |
| FR-003 | Opening the Extraction Center shall keep the user inside the WP003 workbench frame. |
| FR-004 | The Extraction Center shall open or focus an Extraction Center tab in the main work area when the WP003 tab model is available. |
| FR-005 | The Extraction Center shall use existing shell, status, notification, command, and bottom-panel patterns rather than creating an isolated page model. |
| FR-006 | The activity label, tab title, commands, and empty states shall use the name `Extraction Center`. |
| FR-007 | Placeholder text shall distinguish extraction operations from later search, graph, lens, and dashboard features. |

### 4.2 New Extraction Form

| ID | Requirement |
| --- | --- |
| FR-008 | The Extraction Center shall provide a form for starting a new extraction run. |
| FR-009 | The form shall capture repository root directory. |
| FR-010 | The form shall capture one or more explicit solution paths. |
| FR-011 | The form shall support adding and removing solution path rows. |
| FR-012 | The form shall capture optional branch name. |
| FR-013 | The form shall capture optional commit SHA. |
| FR-014 | The form shall capture optional requested-by value. |
| FR-015 | The form shall support optional metadata input consistent with the API contract available at implementation time. |
| FR-016 | The form shall explain that solution paths are explicit and are not discovered by recursively scanning the repository. |
| FR-017 | The form shall explain how relative solution paths resolve against the submitted repository root. |
| FR-018 | The form shall provide user-actionable client-side validation for obvious missing values before submission. |
| FR-019 | Client-side validation shall not replace server validation. |
| FR-020 | Validation messages shall not expose raw exceptions, stack traces, connection strings, environment variables, raw Cypher, Neo4j internal identifiers, driver-specific details, or arbitrary backend exception text. |
| FR-021 | The submit action shall be disabled or guarded while a submission is already in progress. |
| FR-022 | The form shall preserve entered values after validation failure so users can correct them. |
| FR-023 | The form shall make clear when the Archon API base URL or API readiness state prevents submission. |

### 4.3 Start Extraction Submission

| ID | Requirement |
| --- | --- |
| FR-024 | Submitting a valid form shall call `POST /extractions` through the WP002 API client. |
| FR-025 | Feature components shall not call `fetch` directly for extraction submission. |
| FR-026 | The request body shall match the implemented extraction API contract. |
| FR-027 | The route path shall not include a common `/api` prefix. |
| FR-028 | A successful accepted response shall display the accepted run identity. |
| FR-029 | A successful accepted response shall select or focus the accepted run in the run detail monitor. |
| FR-030 | A successful accepted response shall invalidate or refresh recent extraction history according to WP002 TanStack Query conventions. |
| FR-031 | A successful accepted response shall register the run with the background run monitor when the run is not already terminal. |
| FR-032 | Submission failure shall be shown using the WP002 safe error model. |
| FR-033 | Server validation problem responses shall be shown as form-level or field-level errors when enough information is available. |
| FR-034 | Unexpected transport or response failures shall produce safe retry-oriented messages. |

### 4.4 Run Status Polling

| ID | Requirement |
| --- | --- |
| FR-035 | The Extraction Center shall read selected run status from `GET /extractions/{runId}` through the WP002 API client. |
| FR-036 | The route builder shall safely encode the run ID path parameter. |
| FR-037 | The route path shall not include a common `/api` prefix. |
| FR-038 | Active non-terminal runs shall poll for status using WP002 polling conventions. |
| FR-039 | Polling shall stop when a run reaches completed, failed, cancelled, or another terminal state recognized by the implementation. |
| FR-040 | Polling shall stop when the selected monitor unmounts unless the run remains explicitly tracked by the background monitor. |
| FR-041 | Polling shall use bounded intervals and shall not create tight request loops. |
| FR-042 | Polling shall support request cancellation through the WP002 request execution model. |
| FR-043 | If a run is no longer found, the UI shall show an unavailable run state rather than raw HTTP details. |
| FR-044 | The UI shall distinguish a stale/refetching run from a fatal run failure. |

### 4.5 Run Detail Display

| ID | Requirement |
| --- | --- |
| FR-045 | The run detail monitor shall display the run identifier. |
| FR-046 | The run detail monitor shall display lifecycle status. |
| FR-047 | The run detail monitor shall display the accepted or started UTC timestamp. |
| FR-048 | The run detail monitor shall display the completed UTC timestamp when available. |
| FR-049 | The run detail monitor shall display submitted request summary values available from the API response. |
| FR-050 | The run detail monitor shall display progress stage. |
| FR-051 | The run detail monitor shall display progress message. |
| FR-052 | The run detail monitor shall display progress percentage when available. |
| FR-053 | The run detail monitor shall display warning count. |
| FR-054 | The run detail monitor shall display error count. |
| FR-055 | The run detail monitor shall display timing summary when available. |
| FR-056 | The run detail monitor shall display produced snapshot identity when available. |
| FR-057 | The run detail monitor shall display persistence diagnostics when available. |
| FR-058 | Persistence diagnostics shall be presented as safe summarized counts and timings rather than backend internals. |
| FR-059 | If the API exposes only diagnostic counts and not individual diagnostic detail, the UI shall not fabricate warning or error details. |
| FR-060 | If a later API response exposes safe diagnostic details, the UI may display those details using the same safe diagnostic rules. |
| FR-061 | The run detail surface shall provide loading, empty, unavailable, failed-request, and refetching states. |

### 4.6 Run History

| ID | Requirement |
| --- | --- |
| FR-062 | The Extraction Center shall list recent extraction runs from `GET /extractions`. |
| FR-063 | Feature components shall not call `fetch` directly for extraction history. |
| FR-064 | The route path shall not include a common `/api` prefix. |
| FR-065 | The history query shall use WP002 TanStack Query conventions for query keys, cancellation, retry, and invalidation. |
| FR-066 | The history list shall show newest-first run summaries returned by the API. |
| FR-067 | The history list shall show run identifier, status, started timestamp, completed timestamp when available, repository root, solution count, warning count, error count, and snapshot identity when available if those fields are present in the response. |
| FR-068 | Selecting a history row shall open or focus the run detail monitor for that run. |
| FR-069 | The history list shall provide loading, empty, error, and refetching states. |
| FR-070 | The history list shall avoid displaying raw backend diagnostics. |
| FR-071 | The history list may expose a bounded history limit if supported by the API client. |

### 4.7 Duplicate Previous Request

| ID | Requirement |
| --- | --- |
| FR-072 | The Extraction Center shall provide a duplicate-previous-request action where enough submitted request values are available. |
| FR-073 | Duplicating a previous request shall populate the new extraction form without automatically submitting it. |
| FR-074 | Duplicating a previous request shall allow the user to adjust repository root, solution paths, branch name, commit SHA, requested-by, and metadata values before submission where those values are available. |
| FR-075 | If the current API response does not expose enough values to reconstruct a prior request safely, the duplicate action shall be disabled or shall explain which values must be re-entered. |
| FR-076 | Duplicate behavior shall not infer solution paths by scanning the repository. |

### 4.8 Produced Snapshot Handoff

| ID | Requirement |
| --- | --- |
| FR-077 | Completed runs with a snapshot identity shall show the produced snapshot identity. |
| FR-078 | The run detail surface shall provide an open-produced-snapshot action when a snapshot identity is available. |
| FR-079 | If an active snapshot selection mechanism exists at implementation time, the action shall use that mechanism to select or open the produced snapshot. |
| FR-080 | If complete snapshot context behavior is not yet implemented, the action shall show an honest placeholder or notification explaining that snapshot opening is completed by a later work package. |
| FR-081 | The open-produced-snapshot action shall not perform graph queries, search queries, dashboard metric loading, or visualization work in WP004. |
| FR-082 | If a produced snapshot is unavailable or deleted, the UI shall show a safe unavailable state rather than switching silently to another snapshot. |

### 4.9 Bottom-Panel Background Monitor

| ID | Requirement |
| --- | --- |
| FR-083 | Active extraction runs shall be visible in the workbench bottom panel or background work surface. |
| FR-084 | The bottom-panel monitor shall show at least run identity, status, progress stage or message, and completion/failure state for active tracked runs. |
| FR-085 | Users shall be able to navigate away from the Extraction Center while a long-running extraction remains visible in the bottom panel. |
| FR-086 | Selecting a run from the bottom-panel monitor shall return focus to the Extraction Center run detail or open the relevant workbench tab. |
| FR-087 | Terminal runs may remain visible until acknowledged or until a clear retention rule selected during implementation removes them from the background monitor. |
| FR-088 | The bottom-panel monitor shall use safe diagnostics and shall not expose backend internals. |
| FR-089 | The bottom-panel monitor shall use the WP003 shell state model rather than a separate global page model. |

### 4.10 Notifications

| ID | Requirement |
| --- | --- |
| FR-090 | The Extraction Center shall use the WP002 notification runtime for extraction-related notifications. |
| FR-091 | The UI shall notify when a run is accepted if the acceptance is not already obvious from the focused page state. |
| FR-092 | The UI shall notify when a background run completes. |
| FR-093 | The UI shall notify when a background run fails or is cancelled. |
| FR-094 | Notifications shall include safe, concise messages and stable run context where useful. |
| FR-095 | Notifications shall not be the only place where persistent extraction errors or validation failures are represented. |
| FR-096 | Notifications shall not expose raw stack traces, connection strings, environment variable values, raw Cypher, Neo4j internal identifiers, driver-specific details, or arbitrary backend exception text. |

### 4.11 Command Palette Integration

| ID | Requirement |
| --- | --- |
| FR-097 | WP004 shall add Extraction Center commands to the workbench command palette if the WP003 command model is available. |
| FR-098 | Commands should include opening Extraction Center, focusing the new extraction form, refreshing extraction history, and focusing active background extraction runs where practical. |
| FR-099 | Command execution shall use the local workbench state and feature state models rather than browser page navigation. |
| FR-100 | Commands that require API readiness or selected run context shall show disabled or unavailable states when prerequisites are missing. |

### 4.12 Out-of-Scope Functional Behavior

| ID | Requirement |
| --- | --- |
| FR-101 | WP004 shall not implement snapshot deletion. |
| FR-102 | WP004 shall not implement delete-all snapshot behavior. |
| FR-103 | WP004 shall not implement graph queries over produced snapshots. |
| FR-104 | WP004 shall not implement global search over produced snapshots. |
| FR-105 | WP004 shall not implement dashboard metrics over produced snapshots. |
| FR-106 | WP004 shall not implement graph rendering, graph projection rendering, visual lenses, matrices, Sankey views, or visual analytics. |
| FR-107 | WP004 shall not implement automatic recursive repository scanning for solution files. |
| FR-108 | WP004 shall not implement arbitrary filesystem browsing or server-side file picker behavior. |
| FR-109 | WP004 shall not implement authentication or authorization. |

## 5. Non-Functional Requirements

### 5.1 Styling and Toolkit Consistency

| ID | Requirement |
| --- | --- |
| NFR-001 | WP004 shall use shadcn/ui-compatible primitives for forms, inputs, buttons, tables or lists, dialogs, badges, tooltips, popovers, notifications, and ordinary workbench controls where those controls are required. |
| NFR-002 | WP004 shall not introduce another ordinary UI component library for shell, forms, tables, dialogs, command palette, menus, badges, tooltips, popovers, tabs, or notification patterns. |
| NFR-003 | WP004 shall use standard toolkit coloring, text sizing, spacing, and control styling. |
| NFR-004 | WP004 shall not introduce custom theme colors, custom type scales, custom button treatments, card-like marketing visual treatments, or bespoke web-page styling unless explicitly requested in the active implementation request. |
| NFR-005 | Custom CSS may be used for practical workbench layout mechanics where necessary, but it shall not replace the selected component system or create a product-landing-page treatment. |

### 5.2 Accessibility and Keyboard Support

| ID | Requirement |
| --- | --- |
| NFR-006 | Extraction Center regions shall use semantic structure and accessible labels where appropriate. |
| NFR-007 | Form fields shall have associated labels. |
| NFR-008 | Dynamic solution path rows shall be keyboard operable. |
| NFR-009 | Validation messages shall be programmatically associated with the relevant field or form area where practical. |
| NFR-010 | History rows, run-detail actions, bottom-panel run items, and command-palette actions shall be keyboard reachable. |
| NFR-011 | Focus shall move predictably after submission, validation failure, row selection, and open-produced-snapshot actions. |
| NFR-012 | Status indicators shall not rely on color alone to communicate queued, running, completed, failed, cancelled, unavailable, or warning states. |
| NFR-013 | Progress percentage shall use accessible progress semantics when represented as a progress control. |

### 5.3 Safety and Diagnostics

| ID | Requirement |
| --- | --- |
| NFR-014 | User-visible diagnostics shall use the WP002 safe error and diagnostic model. |
| NFR-015 | User-visible diagnostics shall not expose raw stack traces. |
| NFR-016 | User-visible diagnostics shall not expose connection strings, environment variables, credentials, tokens, raw Cypher, Neo4j internal identifiers, driver-specific diagnostics, or arbitrary backend exception text. |
| NFR-017 | The UI shall not expose raw storage diagnostics beyond safe counts, timings, and safe messages returned by the public API contract. |
| NFR-018 | The UI shall not expose metadata values if the API contract intentionally exposes only metadata keys. |
| NFR-019 | The UI shall not provide arbitrary graph query consoles, arbitrary Cypher input, or backend diagnostic consoles. |

### 5.4 Performance and Responsiveness

| ID | Requirement |
| --- | --- |
| NFR-020 | Polling active extraction runs shall use bounded intervals and cancellation. |
| NFR-021 | The UI shall avoid unnecessary polling for terminal runs. |
| NFR-022 | The UI shall not block interaction while a run is being polled or history is refetching. |
| NFR-023 | The production UI build shall not require a running ArchonApi. |
| NFR-024 | WP004 shall not add graph rendering libraries, visualization libraries, or heavy data-grid dependencies. |
| NFR-025 | History rendering shall remain responsive for the bounded history size returned by the API. |

### 5.5 Maintainability

| ID | Requirement |
| --- | --- |
| NFR-026 | Extraction feature code shall be separated from generic workbench shell code. |
| NFR-027 | Extraction API calls shall be centralized through WP002 client functions and query hooks or equivalent conventions. |
| NFR-028 | Extraction server state shall remain in TanStack Query rather than duplicated in local component state. |
| NFR-029 | Local UI state shall be limited to form values, selected run, visible panels, acknowledged notifications, and background monitor preferences. |
| NFR-030 | Component and test organization shall allow WP005 and WP006 to integrate snapshot administration and snapshot context without rewriting Extraction Center. |
| NFR-031 | Names and labels shall match UI brief vocabulary, especially `Extraction Center`, `run`, `snapshot`, `current`, `queued`, `running`, `completed`, `failed`, and `cancelled`. |

## 6. User Experience Requirements

### 6.1 Default User Journey

A user should be able to open ArchonExplorer, select Extraction Center, enter a repository root directory and explicit solution path values, submit an extraction request, see the accepted run identifier, observe status updates without refreshing the page, and later see whether a produced snapshot identity is available.

The experience should feel operational and workbench-native. Extraction should be understandable as background work that creates snapshots for later investigation, not as a hidden backend task or a disconnected administration page.

### 6.2 Extraction Center Layout

The Extraction Center should visibly contain:

- new extraction form;
- recent run history;
- selected run detail panel;
- status, progress, timings, and diagnostics summary;
- produced snapshot affordance when available;
- safe loading, empty, validation, failure, unavailable, and terminal states.

The implementation may choose exact proportions and responsive behavior, but the page must remain plain desktop-IDE-style and aligned with the existing shell.

### 6.3 Form Experience

The form should guide users toward valid inputs without pretending to know the file system. It should support repeated solution path entries and preserve user input across validation errors. Relative path explanation should be visible enough to prevent users from assuming that the UI scans the repository automatically.

Optional branch, commit, requested-by, and metadata fields should be presented as useful run context, not as required extraction inputs.

### 6.4 Monitoring Experience

Queued and running work should show current progress stage and message. Completed work should emphasize the produced snapshot identity when present. Failed or cancelled work should show safe reasons or counts where available and should offer a clear path to correct inputs or duplicate the request.

The monitor must not overpromise diagnostic detail if the API exposes only counts. When only counts are available, the UI should say that detailed safe diagnostic messages are not available from the current response rather than fabricating warning/error rows.

### 6.5 Bottom-Panel Experience

The bottom panel should let users keep an eye on long-running extraction work while navigating elsewhere. It should avoid becoming a separate full extraction page; selecting an item should bring the user back to the Extraction Center or the associated run detail.

### 6.6 Snapshot Handoff Experience

When a run produces a snapshot identity, the user should understand that the snapshot is the output of extraction and will be used by later dashboard, search, and investigation features. If full snapshot opening is not yet implemented, the UI should be honest and explain that snapshot context is completed by WP006.

## 7. Technical Requirements

### 7.1 Frontend Stack

WP004 shall continue using the frontend stack established by WP001 through WP003:

| Concern | Selection |
| --- | --- |
| Application toolchain | Vite |
| UI framework | React |
| Language | TypeScript |
| Package manager | npm |
| Component system | shadcn/ui |
| Server state | TanStack Query from WP001/WP002 |
| API runtime | WP002 Archon API client/runtime foundation |
| Workbench shell | WP003 activity, tab, bottom-panel, status, command, and notification patterns |
| Test approach | Focused Playwright journeys for UI behavior |

### 7.2 Project Location

The WP004 implementation shall extend the ArchonExplorer application at:

```text
src/ArchonExplorer
```

The specification does not require a precise source-folder layout, but implementation planning should separate extraction feature components, form state, query hooks, API mapping, tests, and workbench integration so the feature remains maintainable.

### 7.3 API Contract

WP004 shall consume the implemented extraction API surface:

```http
POST /extractions
GET  /extractions/{runId}
GET  /extractions
```

The current API request and response types include these concepts:

| API concept | UI treatment |
| --- | --- |
| `StartExtractionApiRequest.repositoryRootDirectory` | Repository root input. |
| `StartExtractionApiRequest.solutionPaths` | Explicit solution path list input. |
| `StartExtractionApiRequest.branchName` | Optional branch input. |
| `StartExtractionApiRequest.commitSha` | Optional commit SHA input. |
| `StartExtractionApiRequest.requestedBy` | Optional requested-by input. |
| `StartExtractionApiRequest.metadata` | Optional metadata input consistent with implementation-time contract. |
| `ExtractionRunStatusResponse.runId` | Stable run identity in monitor, history selection, bottom panel, and notifications. |
| `ExtractionRunStatusResponse.status` | Lifecycle state display and polling stop condition. |
| `ExtractionRunStatusResponse.submittedRequest` | Accepted request summary and duplicate-source values where complete enough. |
| `ExtractionRunStatusResponse.progress` | Stage, message, optional percentage, and last-updated display. |
| `ExtractionRunStatusResponse.warningCount` | Warning count display. |
| `ExtractionRunStatusResponse.errorCount` | Error count display. |
| `ExtractionRunStatusResponse.timings` | Timing summary display. |
| `ExtractionRunStatusResponse.snapshotIdentity` | Produced snapshot identity and open-produced-snapshot affordance. |
| `ExtractionRunStatusResponse.persistenceDiagnostics` | Safe persistence counts, timings, and completion display. |
| `ExtractionRunHistoryResponse.runs` | Recent run history list. |

Implementation planning shall re-inspect the API and frontend type definitions before coding. If the API contract changes, the implementation shall align with the current code rather than this draft's historical observations.

### 7.4 Server State Model

Extraction server state shall live in TanStack Query. Suggested query and mutation domains include:

- start extraction mutation;
- extraction history query;
- selected run status query;
- background tracked run status queries;
- invalidation for history and selected run after submission or terminal state.

TanStack Query state shall remain the source of truth for server responses. Local component state may hold form edits, selected run ID, local acknowledgement state, and view preferences.

### 7.5 Polling Model

WP004 shall use WP002 polling conventions for asynchronous runs. Polling shall be tied to active selected run details and explicitly tracked background runs. Polling shall stop for terminal states and shall clean up when components unmount or tracked runs are removed.

Terminal status names shall be normalized by the frontend implementation using the actual API status values available at implementation time. The UI shall be tolerant of unknown statuses by showing a safe unknown or in-progress state rather than failing rendering.

### 7.6 Workbench Integration Model

WP004 shall integrate with WP003 through available shell extension points:

- activity registration for Extraction Center;
- tab opening or focusing for Extraction Center and selected run detail;
- bottom-panel background work contribution;
- status bar background-work count or message where supported;
- command palette contribution for extraction actions;
- notification runtime for accepted/completed/failed/cancelled events.

If a WP003 extension point is not sufficiently formal at implementation time, WP004 may add the minimum shell integration necessary for Extraction Center while preserving the existing shell architecture.

### 7.7 Safe Error Handling

All API errors, validation failures, unavailable states, unexpected response shapes, cancellation events, and polling failures shall pass through the WP002 safe error model. User-facing messages shall be actionable and safe. Developer console logging, if used, shall also avoid secrets and unsafe backend diagnostics.

### 7.8 Documentation-Pass Expectation for Implementation

Any implementation plan or coding task created from this specification shall reference `.github/instructions/documentation-pass.instructions.md` as mandatory. If implementation introduces or modifies source code, internal and other non-public types must receive the same developer-level documentation standard as public types. Documentation requirements must not be scoped only to public API surface.

## 8. Data and State Requirements

### 8.1 Form State

The new extraction form state shall include:

- repository root directory;
- one or more solution paths;
- optional branch name;
- optional commit SHA;
- optional requested-by value;
- optional metadata values consistent with the implementation-time API contract;
- client-side validation messages;
- submission state.

Form state may be reset after successful submission only if the accepted run remains visible and the user has a clear way to duplicate or re-enter values. Preserving form values after submission is also acceptable if it better supports iterative extraction.

### 8.2 Selected Run State

The UI shall track the selected run ID locally. The selected run's server response shall be read through TanStack Query. Selecting a run from history, submission success, notification action, bottom-panel item, or command-palette action shall update selected run state and focus the relevant monitor.

### 8.3 Background Run State

Background run state shall track run IDs that should continue to be visible outside the Extraction Center page. It may include local acknowledgement or retention information, but it shall not duplicate full server response payloads outside TanStack Query.

### 8.4 History State

Extraction history shall be server state. The UI may store table selection, filter text, or sort preference locally if implementation planning chooses to add them, but WP004 does not require advanced history filtering or paging beyond the implemented API contract.

### 8.5 Snapshot Handoff State

WP004 may store only the selected produced snapshot identity needed to trigger a handoff or placeholder action. It shall not create a competing active snapshot context model. WP006 owns complete active snapshot context and dashboard behavior.

## 9. Integration Requirements

### 9.1 WP001 Foundation Integration

WP004 shall preserve the WP001 React, TypeScript, Vite, shadcn/ui, theme, TanStack Query provider, and Aspire-hosted development foundation.

### 9.2 WP002 Runtime Integration

WP004 shall use WP002 runtime foundations for:

- route constants and route builders;
- typed extraction API methods;
- request execution;
- cancellation and timeout behavior;
- safe error shaping;
- TanStack Query conventions;
- polling helpers;
- notification infrastructure;
- API connectivity state.

WP004 shall not duplicate those mechanisms inside feature components.

### 9.3 WP003 Workbench Shell Integration

WP004 shall use WP003 workbench foundations for:

- activity navigation;
- tabbed work area;
- bottom-panel background work surface;
- status bar background-work or API context display;
- command palette shell;
- notification host;
- desktop-style layout and styling constraints.

### 9.4 WP005 Snapshot Admin Integration

WP004 shall not implement snapshot deletion, listing, filtering, or lifecycle administration. It shall display produced snapshot identity and may link or hand off to snapshot administration once WP005 is implemented.

### 9.5 WP006 Snapshot Context Integration

WP004 shall not own complete active snapshot context. If WP006 or an equivalent context provider exists at implementation time, the open-produced-snapshot action shall use it. Otherwise, the action shall remain an honest placeholder.

### 9.6 Future Investigation Feature Integration

WP004 shall prepare, but not implement, future handoffs to dashboard, search, node overview, evidence inspector, graph projections, lenses, findings, and diff features by preserving run and snapshot identities in safe UI state.

## 10. Validation Requirements

### 10.1 Required Validation

WP004 implementation shall be validated with:

| ID | Validation |
| --- | --- |
| VAL-001 | Restore frontend dependencies from `src/ArchonExplorer` using npm if package dependencies changed. |
| VAL-002 | Run the frontend typecheck script successfully. |
| VAL-003 | Run the frontend production build script successfully. |
| VAL-004 | Run focused Playwright coverage that opens Extraction Center from the workbench shell. |
| VAL-005 | Run focused Playwright coverage that submits a valid extraction request through a mocked or controlled API response. |
| VAL-006 | Run focused Playwright coverage that shows accepted run identity and selected run detail. |
| VAL-007 | Run focused Playwright coverage that displays queued or running status and updates to completed status. |
| VAL-008 | Run focused Playwright coverage that displays recent extraction history. |
| VAL-009 | Run focused Playwright coverage that selects a history row and opens run detail. |
| VAL-010 | Run focused Playwright coverage that duplicates a previous request where the response contract supports enough values. |
| VAL-011 | Run focused Playwright coverage that shows safe validation error display. |
| VAL-012 | Run focused Playwright coverage that keeps a long-running extraction visible in the bottom panel while navigating elsewhere. |
| VAL-013 | Run focused Playwright coverage that emits or displays completion/failure notification behavior where practical. |
| VAL-014 | Run focused accessibility checks for form labels, validation messages, keyboard navigation, status indicators, progress semantics, and bottom-panel run items. |
| VAL-015 | Confirm no raw stack traces, connection strings, environment variables, raw Cypher, Neo4j internal identifiers, driver-specific diagnostics, or arbitrary backend exception text appear in Extraction Center UI states. |

### 10.2 Manual Smoke Checklist

The WP004 manual smoke validation should confirm:

- ArchonExplorer opens inside the existing Aspire-hosted development flow.
- Extraction Center is visible as a workbench activity.
- The new extraction form accepts repository root and explicit solution paths.
- The form does not imply automatic recursive solution discovery.
- Submitting a valid request shows an accepted run ID.
- Status polling updates progress without a page refresh.
- Recent extraction history renders.
- Selecting a history row opens the run detail monitor.
- A produced snapshot identity appears for completed runs when the API returns one.
- Open-produced-snapshot behavior either uses the available snapshot context path or clearly explains that full snapshot context arrives later.
- Long-running extraction remains visible in the bottom panel while another activity is selected.
- Safe validation and failure states do not expose backend internals.

### 10.3 Full Suite Guidance

The implementation should not run the full test suite for this work package unless the active implementation request explicitly asks for it or focused validation reveals a cross-cutting problem that requires broader verification.

## 11. Acceptance Criteria

WP004 is complete when:

1. ArchonExplorer includes an Extraction Center workbench activity.
2. The activity provides a new extraction form for repository root, explicit solution paths, optional branch, optional commit SHA, optional requested-by, and optional metadata values aligned with the implemented API contract.
3. The form explains that solution paths are explicit and are not inferred through recursive repository scanning.
4. Submitting a valid request calls `POST /extractions` through the WP002 API client.
5. The UI displays accepted run identity and selected run status.
6. The UI polls `GET /extractions/{runId}` for active selected or background runs and stops polling terminal runs.
7. The UI lists recent extraction history from `GET /extractions`.
8. The UI can select a recent run and show run details.
9. The UI shows queued, running, completed, failed, cancelled, unavailable, loading, empty, validation, and refetching states safely.
10. The UI shows progress stage, progress message, optional percentage, timestamps, warning count, error count, timing summary, produced snapshot identity, and persistence diagnostics when available.
11. The UI does not fabricate diagnostic details when the API exposes only counts.
12. The UI offers duplicate-previous-request behavior where enough request data is available and otherwise explains why duplication is unavailable.
13. The UI offers an open-produced-snapshot action when a snapshot identity exists, using existing snapshot context if available or an honest placeholder if not.
14. Active long-running extraction remains visible in the bottom panel while users navigate elsewhere.
15. Extraction completion and failure can surface through standard workbench notifications without making notifications the only error representation.
16. WP004 uses shadcn/ui-compatible primitives and does not introduce another ordinary UI component library.
17. WP004 does not implement snapshot deletion, graph queries, search, dashboard metrics, visualisations, lenses, automatic solution scanning, arbitrary filesystem browsing, or authentication.
18. Focused Playwright coverage validates opening Extraction Center, submitting extraction, viewing status, viewing history, safe validation errors, and bottom-panel monitoring.
19. Frontend typecheck and production build pass.

## 12. Documentation and Wiki Impact

### 12.1 Required Documentation Updates

The WP004 implementation plan shall include a documentation pass. Contributor-facing documentation should explain:

- how Extraction Center fits into the ArchonExplorer workbench;
- how extraction request values map to the API contract;
- why solution paths are explicit and not automatically discovered;
- how extraction server state is represented through TanStack Query;
- how polling is started, stopped, and cancelled;
- how active runs surface in the bottom panel;
- how produced snapshot identity is handed off to later snapshot-context features;
- how safe diagnostics differ from backend logs;
- which behavior remains out of scope until WP005, WP006, and later analytical work packages.

### 12.2 Wiki Guidance

If existing wiki pages describe ArchonExplorer architecture, local development, frontend API usage, workbench shell patterns, runtime state management, or UI validation workflows, they should be updated during implementation. Contributor guidance should live in the wiki rather than in standalone implementation notes.

### 12.3 Documentation Standard

Any implementation work that introduces or modifies code shall treat internal and other non-public types as requiring the same developer-level documentation standard as public types. Documentation expectations must not be scoped only to public API surfaces. Implementation planning shall reference `.github/instructions/documentation-pass.instructions.md` as a mandatory requirement.

## 13. Risks and Decisions

### 13.1 Decisions

| Decision | Rationale |
| --- | --- |
| Implement Extraction Center before Snapshot Admin, dashboard, search, and graph visualisation | The UI roadmap requires operational extraction and snapshot foundations before analytical features. |
| Use existing `POST /extractions`, `GET /extractions/{runId}`, and `GET /extractions` routes | These routes are implemented and follow the repository convention of no common `/api` prefix. |
| Keep solution paths explicit | The API contract requires explicit solution paths, and the UI brief forbids recursive repository scanning for this workflow. |
| Use TanStack Query for extraction server state | WP002 establishes TanStack Query as the frontend server-state mechanism. |
| Keep complete snapshot context out of WP004 | WP006 owns active snapshot context and dashboard behavior; WP004 only displays produced snapshot identity and provides a handoff. |
| Avoid fabricating warning/error details | Current inspected response types expose counts and safe summaries; the UI must reflect the actual API contract. |

### 13.2 Risks

| Risk | Mitigation |
| --- | --- |
| Extraction UI could become a disconnected admin page. | Require WP003 activity, tab, bottom-panel, command, status, and notification integration. |
| Polling could overload the API or leak after navigation. | Use WP002 polling conventions, bounded intervals, terminal stop conditions, and cancellation. |
| Users may expect automatic solution discovery. | Explain explicit solution path behavior in the form and do not add recursive scanning. |
| API response contract may change before implementation. | Require implementation planning to re-inspect API endpoints and frontend types before coding. |
| Duplicate-previous-request may be incomplete from compact history. | Use full selected run status when available or disable duplication with a safe explanation. |
| Open-produced-snapshot may imply dashboard/search functionality exists. | Use actual snapshot context only if available; otherwise show an honest placeholder. |
| Diagnostic display could expose backend internals. | Use WP002 safe diagnostic shaping and explicitly prohibit unsafe content in UI states. |
| UI could drift into visual analytics too early. | Explicitly exclude graph queries, search, dashboard metrics, visualisations, and lenses. |

## 14. Traceability

| Source expectation | WP004 treatment |
| --- | --- |
| Add Extraction Center activity | Required by FR-001 through FR-007. |
| Add new extraction form | Required by FR-008 through FR-023. |
| Capture repository root directory | Required by FR-009. |
| Capture explicit solution paths | Required by FR-010 through FR-017. |
| Capture optional branch, commit, requested-by, metadata | Required by FR-012 through FR-015. |
| Submit to `POST /extractions` | Required by FR-024 through FR-034. |
| Poll `GET /extractions/{runId}` | Required by FR-035 through FR-044. |
| Display recent history from `GET /extractions` | Required by FR-062 through FR-071. |
| Show queued/running/completed/failed/cancelled states | Required by FR-035 through FR-044, FR-045 through FR-061, and acceptance criteria. |
| Show progress, percentage, timestamps, warnings, errors, timings, snapshot identity, persistence diagnostics | Required by FR-045 through FR-061. |
| Duplicate previous request | Required by FR-072 through FR-076. |
| Open produced snapshot action | Required by FR-077 through FR-082. |
| Surface active runs in bottom panel | Required by FR-083 through FR-089. |
| Notify background completion/failure | Required by FR-090 through FR-096. |
| Workbench-not-admin-console behavior | Required by FR-001 through FR-007 and integration requirements. |
| Mandatory shadcn/ui and styling constraint | Required by NFR-001 through NFR-005. |
| Safe diagnostics and operational boundaries | Required by FR-020, FR-032 through FR-034, FR-058 through FR-061, FR-088, FR-096, and NFR-014 through NFR-019. |
| No snapshot deletion, graph queries, search, visualisations, lenses, or automatic solution scanning | Enforced by FR-101 through FR-109. |

## 15. Open Questions for Implementation Planning

No blocking product questions remain for WP004 specification creation. Implementation planning may still decide exact component decomposition, form field layout, metadata editing model, polling interval values, terminal status normalization, bottom-panel retention behavior, command identifiers, Playwright test file organization, and whether the duplicate action uses history rows or selected run status, provided those decisions satisfy this specification and repository instructions.

## 16. Change Log

| Date | Change |
| --- | --- |
| 2026-05-29 | Initial WP004 Extraction Center specification created from `docs/foundation/work-packages-ui.md`, `docs/foundation/archon_ui_brief.md`, existing WP001-WP003 spec patterns, and inspected extraction API/frontend type contracts. |
