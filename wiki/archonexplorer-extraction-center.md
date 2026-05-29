# ArchonExplorer Extraction Center

The Extraction Center is the current operational workbench feature in ArchonExplorer. It is the browser surface for starting extraction runs, reviewing recent extraction history, monitoring one selected run, keeping selected or accepted runs visible in the workbench bottom-panel background monitor, duplicating a prior request when the API exposes enough safe request data, and recognizing the produced snapshot identity returned by completed runs. It stays inside the desktop-style workbench shell: selecting the activity opens or focuses the `Extraction Center` tab rather than navigating to a separate browser page.

Read this page with [ArchonExplorer frontend foundation](archonexplorer-frontend-foundation.md) for the browser runtime model, [API extraction workflow](api-extraction-workflow.md) for server-owned extraction behavior, [validation and test workflows](validation-and-test-workflows.md) for focused commands, and the [glossary](glossary.md) for terms such as explicit solution path, selected run detail, duplicate request, produced snapshot, and snapshot context.

Reader path: [Home](home.md) -> [ArchonExplorer frontend foundation](archonexplorer-frontend-foundation.md) -> ArchonExplorer Extraction Center -> [API extraction workflow](api-extraction-workflow.md).

## Operational purpose

ArchonExplorer needs extraction before later investigation features can show architecture facts. The Extraction Center therefore starts with operational work rather than graph exploration: it asks which repository and solution files should be extracted, submits that request to ArchonApi, follows the accepted run, and tells the user whether a persisted snapshot identity exists. It deliberately does not query graph data, dashboard metrics, search results, lenses, visualizations, evidence panels, or snapshot administration routes.

This boundary is important because a completed extraction run and a usable snapshot context are related but not identical. A **produced snapshot** is the public stable snapshot identity returned by a completed run after persistence succeeds. A **snapshot context** is the later workbench-wide selection model that lets dashboard, search, graph, lens, and visualization features operate against a chosen snapshot. The current Extraction Center displays the produced snapshot identity and offers an honest placeholder action, but WP006 owns full snapshot context activation.

## Start request workflow

The start form gathers the request body for `POST /extractions`: repository root directory, one or more explicit solution paths, optional branch name, optional commit SHA, optional requested-by text, and optional metadata entries. An **explicit solution path** is a path the user intentionally submits; it is not a directory scan, wildcard, or recursive discovery request. Relative solution paths resolve against the submitted repository root, but the browser does not validate filesystem existence. ArchonApi remains authoritative for path existence, inside-repository checks, duplicate solution detection, file extension validation, and request acceptance.

The form owns only browser interaction state. Values stay editable after client-side validation, API setup failure, or server validation failure so users can correct and resubmit. If the API base URL is not configured, activating `Submit extraction` keeps the user in the form and moves attention to persistent setup guidance instead of behaving like an inert disabled control. The feature uses the typed operational client and mutation hook rather than calling `fetch` from a component, and accepted responses invalidate relevant extraction cache keys. A successful accepted response shows the accepted run summary and selects that run for status monitoring.

Metadata has a deliberate safety split. The start request can submit metadata values, but run status exposes metadata keys only. That means a duplicated request can restore ordinary fields from a selected run status response, but it cannot reconstruct metadata values from history. If metadata keys were present on the previous request, the duplicate workflow explains that values must be re-entered before submitting if they are still needed.

An accepted start response also registers the run with the background monitor. Registration stores only the public run identifier, acknowledgement state, and notification-transition memory in browser-local feature state. The full accepted status response and every later status read remain server state owned by TanStack Query. This separation lets the shell keep a long-running run visible without turning the local workbench store into a hidden copy of ArchonApi history.

## History and selected-run monitoring

Recent history comes from `GET /extractions` and is intentionally compact. Each row can show run identifier, lifecycle status, started timestamp, completed timestamp when available, repository root, solution count, warning count, error count, and produced snapshot identity when present. The compact history row is enough to choose a run, but it is not enough to duplicate the original request because it does not expose explicit solution path values.

Selecting a history row starts the selected-run detail workflow. The detail panel reads `GET /extractions/{runId}` through the typed operational client and TanStack Query, then uses the extraction polling helper to keep active runs fresh. Polling stops for terminal statuses such as completed, failed, canceled or cancelled, unavailable, and unknown. The panel distinguishes empty selection, loading, background refetching, failed request, not-found, unavailable, active, and terminal states using safe text rather than raw transport details.

The selected-run detail panel is the source for follow-up actions because it has the richer status response. It displays the submitted request summary, progress stage and message, optional progress percentage, timestamps, warnings, errors, top-level timings, produced snapshot identity, and persistence diagnostics when present. Metadata values remain hidden; only metadata keys are shown.

## Bottom-panel background monitor

The bottom-panel background monitor is the workbench-native way to keep extraction work visible after the user leaves the Extraction Center tab. A **background monitor** is a compact shell surface for work that continues asynchronously while the user uses another activity. In the current implementation, selecting a history run or starting a new extraction stores the run identifier in the shared Extraction Center workflow state. The bottom panel then reads the current status for that identifier through the same typed `GET /extractions/{runId}` client path and exact TanStack Query key used by page-level polling.

The monitor shows the run identifier, lifecycle status as text, progress stage and message, optional percentage, refresh state, and terminal state. It intentionally does not display raw backend diagnostics, exception messages, configured endpoints, stack traces, connection strings, raw Cypher, Neo4j driver text, or metadata values. If status cannot be read, the row says that status is unavailable and directs the user back to the run detail for persistent retry context. That wording is intentionally frontend-authored because transient or compact monitor surfaces should not become a place where arbitrary server detail is expanded.

Terminal rows use an explicit acknowledgement rule. Completed, failed, cancelled, unavailable, and unknown runs remain visible in the monitor until the user activates `Acknowledge`. Acknowledgement hides the row from the bottom panel but does not delete the TanStack Query cache entry, erase history, or call any ArchonApi cleanup route. Active queued or running rows continue to poll from the bottom panel even when the selected workbench activity is Search, Snapshots, Diagnostics, or another future area. Activating `Open run` selects the run in the shared feature workflow and opens or focuses Extraction Center so the durable selected-run detail panel can show the full safe context.

This division gives contributors a clear mental model: the page detail is the durable explanation surface, while the bottom panel is a shell-level visibility and acknowledgement surface. If a user needs to understand request values, duplicate the request, inspect persistence diagnostics, or follow produced-snapshot identity, they should open the run detail. If they only need to know that background work is still running or that a terminal outcome needs acknowledgement, the bottom panel is sufficient.

## Notifications and command palette integration

Extraction Center uses the existing notification runtime for supplemental operation feedback. An accepted run publishes an informational notification, active status transitions can publish safe information, completed runs publish success, failed or cancelled runs publish warnings, and unavailable status reads publish normalized safe error notifications with the persistent-display flag. Notifications are deduplicated by remembered run status so the same polling state does not create repeated toast spam. The status memory is intentionally local workflow state; it stores the last announced status string, not the full server response.

Notifications do not replace persistent page or bottom-panel state. If a selected run fails to load, the selected-run detail still shows durable not-found or unavailable copy. If a run completes, the bottom-panel row remains until acknowledged. If a produced-snapshot placeholder is activated, the selected-run detail still explains the WP006 snapshot-context boundary after the transient notification appears. This matters for accessibility and troubleshooting because toast messages can be dismissed or missed, while the owning page region keeps retry and interpretation context visible.

The command palette now includes Extraction Center commands in addition to shell commands. `Open Extraction Center` opens or focuses the feature tab. `Focus Extraction Center New Request Form` opens the feature and requests focus on the persistent form summary. `Refresh Extraction Center History` opens the feature and requests a TanStack Query refresh of recent history. `Focus Active Extraction Background Run` opens the feature and selects the first visible tracked run when one exists; when no run is tracked, the command gives safe informational feedback instead of fabricating a run or querying history. These commands remain shell-local actions. They do not perform browser navigation, execute arbitrary API routes, query graph data, search snapshots, or open dashboard, lens, visualization, or snapshot-delete workflows.

## Duplicate request workflow

The duplicate request action copies available values from the selected run status response into the start form. It prefers selected-run status data because that response exposes the submitted request summary, including repository root and explicit solution paths. The action never submits automatically. After duplication, the user can review and edit the copied fields, add missing metadata values, correct any validation issues, and explicitly press `Submit extraction` if a new run should be started.

Duplicate request does not infer missing data. It does not scan the repository for solution files, use the compact history row to invent solution paths, or restore metadata values that the API intentionally withheld. If the selected run status is still loading or unavailable, the action is disabled with guidance explaining that history rows only contain compact summaries. If selected status is loaded but lacks a required repository root or explicit solution paths, the action remains unavailable and the page explains which values must be re-entered.

For keyboard and screen-reader users, duplication returns focus to the persistent form summary when available. That summary explains important follow-up work, such as the metadata value omission boundary, and keeps validation context visible before any new submission.

## Produced-snapshot handoff boundary

Completed runs that include `snapshotIdentity` display that identity and show an `Open produced snapshot` action. The current action is intentionally a placeholder because the complete snapshot context is owned by a later work package. Activating it publishes a safe informational notification and leaves persistent inline guidance in the selected-run detail panel. The message explains that WP006 owns opening produced snapshots for dashboards, search, graph views, and lenses.

The placeholder does not call dashboard, search, graph, lens, visualization, or snapshot-delete routes. It does not query architecture facts and does not mark a global current snapshot. This honest boundary prevents users and contributors from mistaking a public produced snapshot identity for an implemented investigation workspace.

## Safe presentation rules

Extraction Center surfaces must remain safe even when API operations fail. Page-level errors, submission errors, validation messages, polling failures, and notifications use normalized safe messages. They must not render raw stack traces, connection strings, configured endpoint values, environment-variable values, raw Cypher, Neo4j internals, driver details, bearer tokens, arbitrary backend exception text, or a browser-invented `/api` route prefix.

Notifications supplement persistent page state; they do not replace it. A produced-snapshot placeholder notification announces that the user activated the handoff action, but the selected-run detail still retains durable text explaining the WP006 boundary. Submission and polling errors remain visible in the form or detail panel so users have retry and correction context after any transient notification is dismissed. Background monitor rows also retain active or terminal state until a user acknowledges them, so a terminal outcome is not represented only by a transient toast.

## Validation

Focused unit and component validation for the Extraction Center runs from the frontend project:

```powershell
cd .\src\ArchonExplorer
npm run test -- src/test/extraction-center
```

This suite covers request mapping, duplicate request reconstruction, metadata value omission, compact-history unavailable duplication guidance, selected-run rendering, background tracking state, terminal acknowledgement, bottom-panel monitor rendering, command registration, persistence diagnostics, produced-snapshot placeholder copy, safe error rendering, and route-safety assertions.

Focused browser validation runs through Playwright:

```powershell
cd .\src\ArchonExplorer
npm run test:e2e -- src/test-e2e/extraction-center.spec.ts
```

The browser journey opens Extraction Center from the activity rail, renders mocked history, keeps a tracked active run visible in the bottom panel while another activity is selected, exercises Extraction Center command palette actions, duplicates selected request values without auto-submitting, activates the produced-snapshot placeholder without graph/search calls, polls a selected run from running to completed, renders empty history, and submits a mocked valid extraction request. These tests use mocked HTTP responses; they do not start ArchonApi, Neo4j, MCP, or the Aspire AppHost.

Before closing a frontend work item that changes Extraction Center behavior, run the full focused frontend gate from the same project directory:

```powershell
cd .\src\ArchonExplorer
npm run typecheck
npm run test
npm run build
npm run test:e2e -- src/test-e2e/extraction-center.spec.ts
```

This final sequence confirms that TypeScript still understands the shared typed client and query hooks, all Vitest component and runtime tests still pass, the production Vite build still emits browser assets, and the focused Extraction Center Playwright journeys still prove the workbench-visible flow. It remains separate from manual Aspire smoke testing because the AppHost is a long-running local composition process.

## Page-structure note

This dedicated topic page exists because the Extraction Center now has a multi-step workflow that is larger than a frontend foundation example: start request, history, selected-run polling, bottom-panel background monitoring, terminal acknowledgement, command palette integration, duplicate request reconstruction, metadata safety, produced-snapshot placeholder handoff, notifications, and focused validation all form one contributor-facing operational journey. The frontend foundation page remains the home for Vite, React, provider, shell, API runtime, and component-system guidance, while this page teaches the current feature workflow. `wiki/home.md` remains a concise landing page and links here only as part of the reader path.
