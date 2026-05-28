# ArchonExplorer UI Work Packages

## Purpose

This document decomposes the [Archon UI Brief](archon_ui_brief.md) into an ordered series of implementation work packages for **ArchonExplorer**, the human-facing Archon UI.

ArchonExplorer is a React and TypeScript desktop-style workbench application hosted by the Archon Aspire composition. Use of **shadcn/ui is mandatory** for the normal application component system. Work packages must not substitute another component library for the workbench shell, forms, tables, dialogs, command palette, menus, badges, tooltips, popovers, tabs, or notification patterns.

The decomposition deliberately delivers **extraction-driving UI and snapshot management before graph visualisation, lenses, health maps, matrices, Sankey views, or other visual analytics**. Users must first be able to create architecture snapshots, monitor extraction runs, administer stored snapshots, and understand the active snapshot context. Visual investigation features depend on that operational foundation.

---

# 1. Ordering Principles

## 1.1 Operational UI before analytical UI

The first ArchonExplorer work packages must make the system operationally useful:

```text
Create or host ArchonExplorer.
Connect it to Archon API.
Start extraction runs.
Monitor extraction history and progress.
List and administer snapshots.
Choose the active snapshot.
Only then add search, lenses, and visualisations.
```

Graph rendering and lens-specific visual analytics must not be implemented before the extraction and snapshot administration experience exists.

## 1.2 Snapshot context is foundational

Every architecture query runs in a snapshot context. The identifier `current` means the **latest completed snapshot** available to the API for the relevant repository or solution scope.

ArchonExplorer must make snapshot context visible before deep investigation features are added:

- show active snapshot in the status bar;
- default new investigations to `current`;
- allow explicit snapshot selection;
- show no-snapshot and no-current-snapshot states;
- warn when a selected snapshot is deleted or unavailable;
- distinguish extraction run history from snapshot graph availability.

## 1.3 Repository route convention

Archon API route examples and client route constants must follow the repository convention of **no common `/api` prefix**.

Implemented operational endpoints that early work packages should consume are:

```http
POST   /extractions
GET    /extractions/{runId}
GET    /extractions
GET    /management/snapshots
DELETE /management/snapshots/{snapshotStableKey}
POST   /management/snapshots/delete-all
```

Future query endpoints should follow the same route convention.

## 1.4 Workbench, not admin console

Even operational features must live inside the ArchonExplorer workbench model. Extraction history and snapshot administration are not isolated admin pages; they are workbench areas with shared activity navigation, command palette behaviour, status bar context, notifications, and safe diagnostics.

## 1.5 Safety and evidence over spectacle

The UI should prefer safe, explainable, evidence-backed interactions. It must not expose raw stack traces, connection strings, environment variables, raw Cypher, Neo4j internal identifiers, driver-specific diagnostics, or arbitrary graph query consoles.

## 1.6 Mandatory UI toolkit styling constraint

Every ArchonExplorer work package must use the standard coloring, text sizing, spacing, and control styling provided by the selected UI toolkit and the existing ArchonExplorer component primitives. Work packages must not introduce custom colors, custom type scales, custom button or control treatments, card-like visual treatments, marketing-style hero styling, or other bespoke visual styling unless the user explicitly asks for that deviation in the active implementation request. The default presentation model is a plain desktop-IDE-style workbench, not a web-page-style product landing page.

---

# 2. Work Package Structure

Each work package should be implemented as a self-contained vertical slice with these sections in its eventual implementation plan:

```text
Goal
User value
Dependencies
Scope
Out of scope
Primary UI surfaces
API assumptions
State model impact
Acceptance criteria
Validation
Documentation/wiki impact
```

A work package may introduce placeholders for later areas, but placeholders must not pretend that unavailable graph, lens, or visualisation features are complete.

---

# 3. Roadmap Summary

| Order | Work Package | Category | Must precede visualisations? |
|---:|---|---|---|
| WP001 | ArchonExplorer Foundation | Application foundation | Yes |
| WP002 | API Client and Runtime Foundation | Frontend runtime | Yes |
| WP003 | Workbench Desktop Shell | Workbench foundation | Yes |
| WP004 | Extraction Center | Operational UI | Yes |
| WP005 | Snapshot Admin | Operational UI | Yes |
| WP006 | Snapshot Context and Dashboard | Operational bridge | Yes |
| WP007 | Global Search and Command Palette | Investigation foundation | Recommended before visualisations |
| WP008 | Node Overview and Evidence Inspector | Investigation foundation | Recommended before visualisations |
| WP009 | Project and Data Catalogues | Non-graph exploration | Recommended before graph visualisations |
| WP010 | Graph Projection Renderer Abstraction | Visualisation foundation | First visualisation-related package |
| WP011 | Dependency Lens | Lens visualisation | No |
| WP012 | Data Access Lens | Lens visualisation | No |
| WP013 | Findings and Rule Violation Workbench | Findings/lens UI | No |
| WP014 | Snapshot Diff Workbench | Timeline/diff UI | No |
| WP015 | Impact, Path, and Advanced Investigation Lenses | Advanced lens UI | No |
| WP016 | Export and Copilot Handoff | Reporting/handoff | No |
| WP017 | Polish, Accessibility, and Resilience | Hardening | No |

The first graph or visual analytics implementation should not begin until WP004 and WP005 are complete and WP006 has established active snapshot handling.

---

# 4. Work Packages

## WP001 - ArchonExplorer Foundation

### UI brief references

- [Binding UI product mandate](archon_ui_brief.md#archon-ui-brief) - ArchonExplorer is the required React and TypeScript UI, hosted by Aspire, with mandatory shadcn/ui.
- [Workbench requirement](archon_ui_brief.md#archon-ui-brief) - the application must behave like a desktop-style investigation workbench.
- [Suggested React UI stack](archon_ui_brief.md#102-suggested-react-ui-stack) - establishes React, TypeScript, TanStack Query, shadcn/ui, cmdk, and resizable panel expectations.
- [MVP UI Scope](archon_ui_brief.md#14-mvp-ui-scope) - includes the ArchonExplorer Aspire-hosted shell and workbench frame in the first useful UI.
- [Suggested shadcn/ui workbench primitives](archon_ui_brief.md#141-suggested-shadcnui-workbench-primitives) - makes shadcn/ui mandatory for normal application components.

### Goal

Create the ArchonExplorer React and TypeScript application and host it through the Archon Aspire composition.

### User value

Users and contributors can launch a visible ArchonExplorer shell from the local distributed application environment, even before functional workbench areas are complete.

### Dependencies

- Existing Archon Aspire AppHost.
- Existing Archon API host.
- Repository frontend conventions established by this work package.

### Mandatory UI toolkit styling constraint

This work package MUST use the standard coloring, text sizing, spacing, and control styling provided by the selected UI toolkit and existing ArchonExplorer component primitives. It MUST NOT introduce custom colors, custom type scales, custom button/control treatments, card-like visual treatments, marketing-style hero styling, or other bespoke visual styling unless the user explicitly asks for that deviation in the active implementation request. Prefer plain desktop-IDE-style composition over web-page-style presentation.

### Scope

- Create the `ArchonExplorer` UI application in the appropriate source location.
- Configure React and TypeScript.
- Configure shadcn/ui as the mandatory component system.
- Add baseline styling and theme support required by shadcn/ui.
- Host ArchonExplorer from the Aspire AppHost.
- Wire development-time API base URL configuration.
- Render an initial placeholder workbench frame.
- Add a visible application identity: `ArchonExplorer`.
- Add a minimal health or connectivity placeholder showing whether API configuration is present.

### Out of scope

- Extraction form submission.
- Snapshot listing.
- Search.
- Graph rendering.
- Lenses.
- Authentication and authorization.

### Primary UI surfaces

- Empty app shell.
- Placeholder activity area.
- Placeholder status area.

### API assumptions

No functional API calls are required beyond optional connectivity smoke checks.

### State model impact

Introduce the minimal application bootstrap state only:

- API base URL configuration;
- theme preference if simple to include;
- application readiness.

### Acceptance criteria

- ArchonExplorer launches from Aspire.
- The app renders a React/TypeScript UI.
- shadcn/ui is configured and usable.
- The application identifies itself as ArchonExplorer.
- No alternative component library is introduced for ordinary UI.
- The shell can be reached during local development without manual static-file setup outside Aspire.

### Validation

- Build the relevant solution or project set.
- Run the UI package install/build command selected by the implementation.
- Run an Aspire-hosted smoke test or manual smoke validation that loads ArchonExplorer.

---

## WP002 - API Client and Runtime Foundation

### UI brief references

- [Search implementation notes](archon_ui_brief.md#34-search-implementation-notes) - establishes no common `/api` prefix and `snapshotId=current` semantics for route examples.
- [Snapshot context model](archon_ui_brief.md#66-snapshot-context-model) - defines `current` as the latest completed snapshot and requires visible snapshot context.
- [API Implications](archon_ui_brief.md#13-api-implications) - lists the query API areas and route-shape conventions required by ArchonExplorer.
- [UI state architecture](archon_ui_brief.md#142-ui-state-architecture) - defines TanStack Query, local workbench state, per-tab state, persisted preferences, and notification state.
- [Safety, empty, loading, and error states](archon_ui_brief.md#143-safety-empty-loading-and-error-states) - requires safe user-visible diagnostics and consistent API failure states.

### Goal

Create the shared frontend runtime used by all ArchonExplorer features.

### User value

Future work packages can consume Archon API consistently without duplicating route strings, polling logic, error handling, or unsafe diagnostic rendering.

### Dependencies

- WP001 ArchonExplorer Foundation.

### Mandatory UI toolkit styling constraint

This work package MUST use the standard coloring, text sizing, spacing, and control styling provided by the selected UI toolkit and existing ArchonExplorer component primitives. It MUST NOT introduce custom colors, custom type scales, custom button/control treatments, card-like visual treatments, marketing-style hero styling, or other bespoke visual styling unless the user explicitly asks for that deviation in the active implementation request. Prefer plain desktop-IDE-style composition over web-page-style presentation.

### Scope

- Add typed API client foundation.
- Add route constants using the no-common-`/api` route convention.
- Add TanStack Query setup for server state.
- Add shared request, response, and error handling helpers.
- Add polling helpers for asynchronous operations.
- Add safe diagnostic shaping for user-visible errors.
- Add notification/toast infrastructure using shadcn/ui-compatible patterns.
- Add global API connectivity state.
- Add initial test seams or mocks for UI journey tests.

### Out of scope

- Feature-specific extraction or snapshot screens.
- Graph query models.
- Renderer integration.
- Full generated OpenAPI client unless explicitly selected by implementation planning.

### Primary UI surfaces

- API connectivity indicator placeholder.
- Error and notification presentation primitives.

### API assumptions

Route constants should include at least:

```http
POST   /extractions
GET    /extractions/{runId}
GET    /extractions
GET    /management/snapshots
DELETE /management/snapshots/{snapshotStableKey}
POST   /management/snapshots/delete-all
```

Future route placeholders may exist for search and graph queries but should be clearly marked as future if not implemented.

### State model impact

Establish the split between:

- TanStack Query server state;
- local workbench state;
- notification state;
- persisted preferences.

### Acceptance criteria

- API route constants do not include a common `/api` prefix.
- Feature work can use a single API client abstraction.
- User-visible errors are safe by default.
- Polling helpers can support extraction run status checks.
- Notification infrastructure is available to later work packages.

### Validation

- Unit or component tests for route construction where practical.
- Tests for safe error shaping.
- UI build succeeds.

---

## WP003 - Workbench Desktop Shell

### UI brief references

- [Workbench requirement](archon_ui_brief.md#archon-ui-brief) - requires a desktop-style investigation workbench in the browser.
- [Workbench desktop shell](archon_ui_brief.md#65-workbench-desktop-shell) - defines the top-level frame, activity bar, sidebar, tabs, panels, status bar, command palette, and notifications.
- [Investigation Workspace](archon_ui_brief.md#73-investigation-workspace) - describes the main lens workspace layout with controls, slice view, and inspector regions.
- [Global Search / Command Palette](archon_ui_brief.md#72-global-search--command-palette) - identifies the command palette as a primary entry point.
- [Suggested shadcn/ui workbench primitives](archon_ui_brief.md#141-suggested-shadcnui-workbench-primitives) - maps shell, tabs, command, panels, menus, badges, and notifications to required UI primitives.

### Goal

Implement the desktop-style workbench frame that hosts operational and investigative features.

### User value

Users get a coherent workbench rather than disconnected pages. Extraction runs, snapshot context, future investigations, and diagnostics can coexist without losing context.

### Dependencies

- WP001 ArchonExplorer Foundation.
- WP002 API Client and Runtime Foundation.

### Mandatory UI toolkit styling constraint

This work package MUST use the standard coloring, text sizing, spacing, and control styling provided by the selected UI toolkit and existing ArchonExplorer component primitives. It MUST NOT introduce custom colors, custom type scales, custom button/control treatments, card-like visual treatments, marketing-style hero styling, or other bespoke visual styling unless the user explicitly asks for that deviation in the active implementation request. Prefer plain desktop-IDE-style composition over web-page-style presentation.

### Scope

- Implement top-level app frame.
- Implement activity bar for major areas.
- Implement primary sidebar region.
- Implement tabbed document or work area.
- Implement dockable or resizable panels.
- Implement bottom panel for extraction runs, diagnostics, and background work.
- Implement status bar placeholders for active snapshot, API connectivity, background work, and selected context.
- Implement command palette shell using shadcn/ui `Command` / cmdk.
- Implement notification/toast placement.
- Implement layout persistence for panel sizes and shell preferences.

### Out of scope

- Real extraction submission.
- Real snapshot deletion.
- Search results.
- Graph rendering.
- Lens execution.

### Primary UI surfaces

- Activity bar.
- Sidebar.
- Main work area.
- Bottom panel.
- Status bar.
- Command palette.
- Notifications.

### API assumptions

Only API connectivity state from WP002 is required.

### State model impact

Introduce local workbench state:

- active activity;
- open workbench tabs;
- active tab;
- panel layout;
- command palette visibility;
- bottom panel visibility;
- notification list.

### Acceptance criteria

- Operational areas can be added as workbench activities.
- Bottom panel can show background job placeholders.
- Status bar has slots for snapshot and API state.
- Command palette opens and can show placeholder commands.
- Layout uses shadcn/ui-compatible primitives and does not introduce another component library.

### Validation

- Playwright journey opens the workbench shell.
- Keyboard shortcut opens command palette.
- Basic accessibility checks for shell landmarks and keyboard focus.
- UI build succeeds.

---

## WP004 - Extraction Center

### UI brief references

- [Workbench requirement](archon_ui_brief.md#archon-ui-brief) - requires users to monitor extraction work inside the workbench.
- [Workbench desktop shell](archon_ui_brief.md#65-workbench-desktop-shell) - requires background extraction runs to surface without forcing users away from investigations.
- [Snapshot context model](archon_ui_brief.md#66-snapshot-context-model) - requires completed extraction snapshots to be openable and refreshable deliberately.
- [Extraction Center](archon_ui_brief.md#78-extraction-center) - defines extraction submission, monitoring, history, diagnostics, produced snapshots, and the implemented extraction endpoints.
- [Safety, empty, loading, and error states](archon_ui_brief.md#143-safety-empty-loading-and-error-states) - requires validation, failed run, and produced-snapshot-deleted states.
- [Testing and validation expectations](archon_ui_brief.md#145-testing-and-validation-expectations) - calls for Playwright coverage for starting extraction and viewing extraction history.

### Goal

Implement the first real operational ArchonExplorer feature: driving extraction through the Archon API.

### User value

Users can start architecture extraction from the UI, monitor progress, review history, inspect safe diagnostics, and open the produced snapshot when extraction completes.

### Dependencies

- WP001 ArchonExplorer Foundation.
- WP002 API Client and Runtime Foundation.
- WP003 Workbench Desktop Shell.

### Mandatory UI toolkit styling constraint

This work package MUST use the standard coloring, text sizing, spacing, and control styling provided by the selected UI toolkit and existing ArchonExplorer component primitives. It MUST NOT introduce custom colors, custom type scales, custom button/control treatments, card-like visual treatments, marketing-style hero styling, or other bespoke visual styling unless the user explicitly asks for that deviation in the active implementation request. Prefer plain desktop-IDE-style composition over web-page-style presentation.

### Scope

- Add Extraction Center activity.
- Add new extraction form.
- Capture repository root directory.
- Capture one or more explicit solution paths.
- Capture optional branch name.
- Capture optional commit SHA.
- Capture optional requested-by value.
- Capture optional metadata keys.
- Submit extraction requests to `POST /extractions`.
- Poll run status from `GET /extractions/{runId}`.
- Display recent run history from `GET /extractions`.
- Show queued, running, completed, failed, and cancelled states.
- Show progress stage, progress message, optional percentage, and timestamps.
- Show warning count and safe warning details.
- Show error count and safe error details.
- Show timing summary.
- Show produced snapshot identity when available.
- Show persistence diagnostics when available.
- Add open-produced-snapshot action.
- Add duplicate-previous-request action.
- Surface active runs in the workbench bottom panel.
- Notify when background extraction completes or fails.

### Out of scope

- Snapshot deletion.
- Graph queries over the produced snapshot.
- Search over the produced snapshot.
- Visualisations.
- Automatic repository scanning for solution files.

### Primary UI surfaces

- Extraction Center activity page.
- New extraction form.
- Run history table.
- Run detail panel.
- Bottom-panel background run monitor.
- Completion/failure notifications.

### API assumptions

Implemented endpoints:

```http
POST /extractions
GET  /extractions/{runId}
GET  /extractions
```

The UI form must reflect the API contract: solution paths are explicit and are not inferred by recursively scanning the repository.

### State model impact

- Extraction server state belongs in TanStack Query.
- Active polling subscriptions should be tied to visible run details and background run monitor state.
- Completed run notifications should not mutate architecture facts; they should prompt user action.

### Acceptance criteria

- User can submit a valid extraction request.
- UI displays accepted run identity and status.
- UI polls and updates status without page refresh.
- UI shows safe validation or failure messages.
- UI shows produced snapshot identity for completed runs.
- UI offers to open or select the produced snapshot.
- UI shows recent extraction history.
- UI can duplicate a previous request into the new extraction form.
- Long-running extraction remains visible in the bottom panel while users navigate elsewhere.

### Validation

- Playwright journey for opening Extraction Center.
- Playwright journey for submitting a valid extraction request.
- Playwright journey for viewing a queued/running/completed run.
- Playwright journey for extraction history.
- Playwright journey for safe validation error display.
- UI build succeeds.

---

## WP005 - Snapshot Admin

### UI brief references

- [Workbench requirement](archon_ui_brief.md#archon-ui-brief) - requires users to administer snapshots inside the workbench.
- [Snapshot context model](archon_ui_brief.md#66-snapshot-context-model) - defines active snapshot visibility, explicit selection, current changes, and deleted/unavailable snapshot states.
- [Snapshot Diff Explorer](archon_ui_brief.md#77-snapshot-diff-explorer) - distinguishes architectural drift comparison from lifecycle administration.
- [Snapshot Admin](archon_ui_brief.md#79-snapshot-admin) - defines snapshot listing, filtering, lifecycle metadata, delete-one, delete-all, and unavailable states.
- [Security and operational boundaries](archon_ui_brief.md#144-security-and-operational-boundaries) - requires strong confirmation and safe operational handling.
- [Testing and validation expectations](archon_ui_brief.md#145-testing-and-validation-expectations) - calls for Playwright coverage for snapshot listing and one-snapshot deletion confirmation.

### Goal

Implement snapshot lifecycle management before any graph visualisation work begins.

### User value

Users can see which snapshots exist, understand lifecycle metadata, choose snapshots for investigation, and safely delete one or all snapshots when cleaning local or development data.

### Dependencies

- WP001 ArchonExplorer Foundation.
- WP002 API Client and Runtime Foundation.
- WP003 Workbench Desktop Shell.
- WP004 Extraction Center is strongly preferred so snapshot records can be understood alongside run history.

### Mandatory UI toolkit styling constraint

This work package MUST use the standard coloring, text sizing, spacing, and control styling provided by the selected UI toolkit and existing ArchonExplorer component primitives. It MUST NOT introduce custom colors, custom type scales, custom button/control treatments, card-like visual treatments, marketing-style hero styling, or other bespoke visual styling unless the user explicitly asks for that deviation in the active implementation request. Prefer plain desktop-IDE-style composition over web-page-style presentation.

### Scope

- Add Snapshot Admin activity.
- List snapshots from management API.
- Filter by repository stable key.
- Filter by solution stable key.
- Filter by status.
- Filter by started timestamp range.
- Filter by commit SHA.
- Display snapshot lifecycle metadata.
- Display diagnostic counts where available.
- Display produced-by run identifier when available.
- Link from snapshot to extraction run detail when available.
- Open snapshot in the active workbench context.
- Mark selected snapshot as active.
- Provide compare placeholder action for future Snapshot Diff Workbench.
- Delete one snapshot with strong confirmation.
- Delete all snapshots with exact confirmation phrase.
- Explain that extraction run history remains after snapshot graph data is deleted.
- Show no-snapshots state.
- Show selected snapshot deleted/unavailable state.
- Show deletion failure using safe diagnostics.

### Out of scope

- Snapshot diff visualisation.
- Dashboard metrics over a selected snapshot.
- Search over snapshots.
- Graph rendering.
- Retention policy automation unless already available from the API.

### Primary UI surfaces

- Snapshot Admin activity page.
- Snapshot filter bar.
- Snapshot table.
- Snapshot lifecycle detail panel.
- Delete-one confirmation dialog.
- Delete-all confirmation dialog.
- Snapshot unavailable state.

### API assumptions

Implemented endpoints:

```http
GET    /management/snapshots
DELETE /management/snapshots/{snapshotStableKey}
POST   /management/snapshots/delete-all
```

The delete-all request must use the exact confirmation required by the API.

### State model impact

- Snapshot list and lifecycle data belong in TanStack Query.
- Active snapshot selection belongs in workbench state.
- Deleted snapshot state must invalidate relevant snapshot queries and update active snapshot if needed.
- A tab pinned to a deleted snapshot should show unavailable state rather than silently switching snapshots.

### Acceptance criteria

- User can list snapshots.
- User can filter snapshot list.
- User can inspect lifecycle metadata.
- User can select a snapshot as active.
- User can see when no snapshots exist.
- User can delete one snapshot only after strong confirmation.
- User can delete all snapshots only after exact strong confirmation.
- UI explains what is preserved and what is removed during deletion.
- UI does not expose raw storage diagnostics.

### Validation

- Playwright journey for listing snapshots.
- Playwright journey for filtering snapshots.
- Playwright journey for selecting active snapshot.
- Playwright journey for one-snapshot deletion confirmation.
- Playwright journey for delete-all confirmation guard.
- Playwright journey for no-snapshots state.
- UI build succeeds.

---

## WP006 - Snapshot Context and Dashboard

### UI brief references

- [Snapshot context model](archon_ui_brief.md#66-snapshot-context-model) - defines `current`, active snapshot visibility, tab binding, stale warnings, and deleted/unavailable snapshot behaviour.
- [Dashboard](archon_ui_brief.md#71-dashboard) - defines latest snapshot, commit SHA, analysis date, summary metrics, hotspots, latest changes, and clickable widgets.
- [Extraction Center](archon_ui_brief.md#78-extraction-center) - requires completed extraction runs to offer opening the produced snapshot.
- [Snapshot Admin](archon_ui_brief.md#79-snapshot-admin) - provides snapshot list and lifecycle data used by active snapshot selection.
- [Safety, empty, loading, and error states](archon_ui_brief.md#143-safety-empty-loading-and-error-states) - requires no-snapshot and no-latest-completed-snapshot states.

### Goal

Create the snapshot-aware dashboard and finalize the workbench-level active snapshot model before investigation features are built.

### User value

Users can understand the current architecture data set, see whether a latest completed snapshot exists, and navigate to extraction or snapshot administration when data is missing or stale.

### Dependencies

- WP004 Extraction Center.
- WP005 Snapshot Admin.

### Mandatory UI toolkit styling constraint

This work package MUST use the standard coloring, text sizing, spacing, and control styling provided by the selected UI toolkit and existing ArchonExplorer component primitives. It MUST NOT introduce custom colors, custom type scales, custom button/control treatments, card-like visual treatments, marketing-style hero styling, or other bespoke visual styling unless the user explicitly asks for that deviation in the active implementation request. Prefer plain desktop-IDE-style composition over web-page-style presentation.

### Scope

- Implement active snapshot status in the status bar.
- Define `current` as latest completed snapshot in UI copy and behaviour.
- Add snapshot selector using snapshot data from Snapshot Admin queries.
- Add no-current-snapshot state.
- Add stale/non-current snapshot warning.
- Add deleted/unavailable active snapshot warning.
- Implement Dashboard activity.
- Show latest snapshot identity.
- Show repository and solution context where available.
- Show commit SHA and analysis time where available.
- Show extraction run summary and recent completion/failure signals.
- Show snapshot count and operational health summary.
- Add dashboard actions to start extraction, open Snapshot Admin, or select a snapshot.
- Add placeholders for future architecture metrics without implementing lens visualisations.

### Out of scope

- Graph visualisations.
- Search.
- Project explorer.
- Lens execution.
- Snapshot diff charts.

### Primary UI surfaces

- Dashboard activity.
- Status bar snapshot segment.
- Snapshot selector.
- Empty states and stale states.

### API assumptions

Uses extraction and snapshot endpoints from WP004 and WP005. No future query endpoints are required unless dashboard metric endpoints already exist.

### State model impact

- Active snapshot becomes a first-class workbench state value.
- Tabs opened after this package should bind to the active snapshot value.
- `current` refresh behaviour should be explicit and not silently disruptive.

### Acceptance criteria

- Dashboard clearly handles no snapshots.
- Dashboard clearly identifies latest completed snapshot when available.
- User can select active snapshot.
- Status bar reflects active snapshot.
- UI warns when active snapshot is not current.
- UI warns when active snapshot is unavailable.
- Dashboard provides clear entry points to Extraction Center and Snapshot Admin.

### Validation

- Playwright journey for no-snapshot dashboard state.
- Playwright journey for selecting active snapshot.
- Playwright journey for stale/non-current warning.
- Playwright journey for deleted snapshot warning if testable.
- UI build succeeds.

---

## WP007 - Global Search and Command Palette

### UI brief references

- [Search-First Selection Experience](archon_ui_brief.md#3-search-first-selection-experience) - establishes search as the primary select-a-thing experience.
- [Search goals](archon_ui_brief.md#31-search-goals) - lists the artefact kinds search must eventually support.
- [Search result grouping](archon_ui_brief.md#32-search-result-grouping) - requires grouped results by node kind.
- [Search result actions](archon_ui_brief.md#33-search-result-actions) - requires contextual lens actions on search results.
- [Search implementation notes](archon_ui_brief.md#34-search-implementation-notes) - defines deterministic Neo4j-backed search and the `/search` endpoint shape.
- [Global Search / Command Palette](archon_ui_brief.md#72-global-search--command-palette) - lists keyboard shortcut, grouped results, badges, recent selections, saved searches, and open-in-new-tab behaviour.

### Goal

Implement deterministic snapshot-scoped search and command palette navigation after operational snapshot handling exists.

### User value

Users can find architecture artefacts in the active snapshot and open them in workbench investigation tabs.

### Dependencies

- WP006 Snapshot Context and Dashboard.

### Mandatory UI toolkit styling constraint

This work package MUST use the standard coloring, text sizing, spacing, and control styling provided by the selected UI toolkit and existing ArchonExplorer component primitives. It MUST NOT introduce custom colors, custom type scales, custom button/control treatments, card-like visual treatments, marketing-style hero styling, or other bespoke visual styling unless the user explicitly asks for that deviation in the active implementation request. Prefer plain desktop-IDE-style composition over web-page-style presentation.

### Scope

- Implement global command palette search using shadcn/ui `Command` / cmdk.
- Query search endpoint using active snapshot or `current`.
- Group results by node kind.
- Show badges and available lens actions from the API response.
- Open a result in an investigation tab.
- Open a result action in an investigation tab when supported.
- Show recent selections.
- Show saved-search placeholder if persistence is not yet implemented.
- Show no-current-snapshot state.
- Show no-results state.
- Show safe API error state.

### Out of scope

- Graph rendering.
- Lens execution beyond opening placeholders.
- Semantic/vector search.
- Saved investigations unless separately planned.

### Primary UI surfaces

- Command palette.
- Search result groups.
- Recent selections.
- Investigation tab placeholder.

### API assumptions

Future route shape from the brief:

```http
GET /search?q={query}&snapshotId={snapshotId}
```

### State model impact

- Recent artefacts may be stored in local workbench state or persisted preferences.
- Search results remain server state.
- Open investigation tabs bind to the active snapshot at creation time.

### Acceptance criteria

- Search is unavailable or clearly disabled when there is no active snapshot.
- Search sends the selected snapshot identifier.
- Results are grouped by node kind.
- User can open a result in a tab.
- Recent selections update after opening results.

### Validation

- Playwright journey for opening command palette.
- Playwright journey for searching with an active snapshot.
- Playwright journey for no-current-snapshot search state.
- Playwright journey for opening a result in a tab.
- UI build succeeds.

---

## WP008 - Node Overview and Evidence Inspector

### UI brief references

- [Overview Lens](archon_ui_brief.md#51-overview-lens) - defines the default detail view for selected artefacts.
- [Evidence-first inspection](archon_ui_brief.md#64-evidence-first-inspection) - defines node and edge inspection content and evidence fields.
- [Explain this slice](archon_ui_brief.md#63-explain-this-slice) - requires users to understand why artefacts appear in a slice.
- [Lens Availability Matrix](archon_ui_brief.md#11-lens-availability-matrix) - identifies overview and evidence availability by artefact kind.
- [API Implications](archon_ui_brief.md#13-api-implications) - lists node overview, available lenses, and evidence lookup API needs.
- [Design Principles](archon_ui_brief.md#15-design-principles) - requires evidence everywhere and unknowns as first-class concepts.

### Goal

Implement the first evidence-backed investigation surface without graph visualisation.

### User value

Users can select an artefact, understand what it is, inspect confidence and unknowns, and follow primary evidence before visual graph slices are introduced.

### Dependencies

- WP007 Global Search and Command Palette.

### Mandatory UI toolkit styling constraint

This work package MUST use the standard coloring, text sizing, spacing, and control styling provided by the selected UI toolkit and existing ArchonExplorer component primitives. It MUST NOT introduce custom colors, custom type scales, custom button/control treatments, card-like visual treatments, marketing-style hero styling, or other bespoke visual styling unless the user explicitly asks for that deviation in the active implementation request. Prefer plain desktop-IDE-style composition over web-page-style presentation.

### Scope

- Implement investigation tab for node overview.
- Show display name, stable key, node kind, owning project/repository/solution, primary evidence, confidence, unknown flags, metrics, findings, and available follow-up lenses.
- Implement evidence inspector panel.
- Show file path, line span, symbol name, containing symbol, snippet preview, evidence kind, confidence, and unknown reason.
- Show related artefact placeholders or links where API supports them.
- Show safe unavailable state when evidence cannot be loaded.
- Keep the UI table/detail oriented; do not add graph rendering yet.

### Out of scope

- Graph visualisation.
- Full lens execution.
- Export generation.

### Primary UI surfaces

- Node overview tab.
- Evidence inspector.
- Related actions list.

### API assumptions

Future route shapes from the brief:

```http
GET /nodes/{stableKey}?snapshotId={snapshotId}
GET /nodes/{stableKey}/lenses?snapshotId={snapshotId}
GET /evidence/{evidenceId}
```

### State model impact

- Selected node and selected evidence are per-tab state.
- Node and evidence payloads are server state.

### Acceptance criteria

- User can open a searched artefact in overview.
- UI shows stable key and snapshot context.
- UI displays evidence details when available.
- UI visibly distinguishes high-confidence, inferred, and unknown evidence.
- UI does not expose unsafe diagnostics.

### Validation

- Playwright journey for opening node overview from search.
- Playwright journey for opening evidence inspector.
- Playwright journey for evidence unavailable state.
- UI build succeeds.

---

## WP009 - Project and Data Catalogues

### UI brief references

- [Project Explorer](archon_ui_brief.md#74-project-explorer) - defines project catalogue purpose and required columns.
- [Data Access Explorer](archon_ui_brief.md#75-data-access-explorer) - defines table-first data access catalogue views.
- [Data Access Lens](archon_ui_brief.md#53-data-access-lens) - defines read/write distinction, table usage, stored procedures, raw SQL, and unknown/dynamic SQL concerns.
- [Dashboard](archon_ui_brief.md#71-dashboard) - identifies project, endpoint, data context, database table, and hotspot summaries used by catalogue navigation.
- [Design Principles](archon_ui_brief.md#15-design-principles) - requires scoped views, evidence, and unknowns to remain visible.

### Goal

Add non-graph catalogue views that help users browse projects and data access facts before visual analytics are introduced.

### User value

Users can explore architecture facts in tables and drill into overview/evidence without needing graph rendering.

### Dependencies

- WP008 Node Overview and Evidence Inspector.

### Mandatory UI toolkit styling constraint

This work package MUST use the standard coloring, text sizing, spacing, and control styling provided by the selected UI toolkit and existing ArchonExplorer component primitives. It MUST NOT introduce custom colors, custom type scales, custom button/control treatments, card-like visual treatments, marketing-style hero styling, or other bespoke visual styling unless the user explicitly asks for that deviation in the active implementation request. Prefer plain desktop-IDE-style composition over web-page-style presentation.

### Scope

- Implement Project Explorer table.
- Implement Data Access Explorer table-first views where API support exists.
- Show project name, path, language, project type, target framework, project format, reference counts, package count, endpoint count, data access indicators, hotlist count, and risk indicators when available.
- Show tables by project and projects by table where available.
- Link rows to node overview.
- Respect active snapshot selection.
- Show no-current-snapshot state.

### Out of scope

- Project dependency graph.
- Read/write matrix visualisation if it requires visual renderer work.
- Endpoint-to-table path graph.
- Sankey, matrix, or graph visualisations.

### Primary UI surfaces

- Project Explorer.
- Data Access Explorer table views.

### API assumptions

May use future query endpoints or graph slice endpoints if available, but should render table projections only.

### State model impact

- Table filters and sorting may be local per-tab state.
- Catalogue payloads are server state.

### Acceptance criteria

- User can browse project catalogue for active snapshot.
- User can open project overview from catalogue.
- User can browse available data access table projections.
- No graph visualisation is introduced.

### Validation

- Playwright journey for Project Explorer.
- Playwright journey for table filtering/sorting where implemented.
- Playwright journey for opening overview from catalogue row.
- UI build succeeds.

---

## WP010 - Graph Projection Renderer Abstraction

### UI brief references

- [Graph Slicing Model](archon_ui_brief.md#4-graph-slicing-model) - defines graph slices as scoped projections generated for specific questions.
- [Graph Slice Definition](archon_ui_brief.md#41-graph-slice-definition) - defines slice inputs, traversal settings, filters, grouping, overlays, and evidence mode.
- [Visual Language](archon_ui_brief.md#8-visual-language) - defines consistent node, edge, badge, halo, opacity, and risk encodings.
- [Visualisation Types](archon_ui_brief.md#9-visualisation-types) - identifies graph, layered graph, path, matrix, Sankey, health map, and timeline forms.
- [Renderer Strategy](archon_ui_brief.md#10-renderer-strategy) - requires separation of graph projection from graph rendering.
- [Neo4j NVL](archon_ui_brief.md#101-neo4j-nvl) - identifies NVL as a candidate first graph prototype while keeping renderers replaceable.

### Goal

Introduce the visualisation foundation only after extraction, snapshot administration, snapshot context, search, and evidence inspection are in place.

### User value

Future lens work can render scoped graph slices consistently without coupling product logic to a single renderer.

### Dependencies

- WP004 Extraction Center.
- WP005 Snapshot Admin.
- WP006 Snapshot Context and Dashboard.
- WP008 Node Overview and Evidence Inspector.

### Mandatory UI toolkit styling constraint

This work package MUST use the standard coloring, text sizing, spacing, and control styling provided by the selected UI toolkit and existing ArchonExplorer component primitives. It MUST NOT introduce custom colors, custom type scales, custom button/control treatments, card-like visual treatments, marketing-style hero styling, or other bespoke visual styling unless the user explicitly asks for that deviation in the active implementation request. Prefer plain desktop-IDE-style composition over web-page-style presentation.

### Scope

- Define UI visual model for graph projections.
- Add renderer adapter boundary.
- Add first renderer integration candidate, such as Neo4j NVL or Sigma.js, if selected during implementation planning.
- Add graph slice loading shell.
- Add large-slice guardrails.
- Add empty, loading, and too-large states.
- Add node and edge selection wiring to inspector.
- Add consistent visual vocabulary foundations for node colour, edge colour, badges, halos, and opacity.

### Out of scope

- Full Dependency Lens.
- Full Data Access Lens.
- Health maps.
- Sankey views.
- Timeline views.

### Primary UI surfaces

- Generic graph projection tab.
- Renderer host component.
- Inspector selection integration.

### API assumptions

Future route shape from the brief:

```http
POST /graph-slices
```

### State model impact

- Graph layout and selection are per-tab state.
- Graph slice result is server state.
- Renderer-specific state must remain behind the adapter boundary where possible.

### Acceptance criteria

- UI can render a scoped graph projection payload.
- User can select nodes and edges.
- Selection appears in inspector.
- Too-large slices are guarded and explained.
- Renderer implementation details do not leak into lens feature code.

### Validation

- Playwright journey for loading a graph projection fixture or test API response.
- Playwright journey for selecting a node and edge.
- Playwright journey for too-large graph state.
- UI build succeeds.

---

## WP011 - Dependency Lens

### UI brief references

- [Dependency Lens](archon_ui_brief.md#52-dependency-lens) - defines dependency questions, visual forms, and controls.
- [Graph Slicing Model](archon_ui_brief.md#4-graph-slicing-model) - defines the reusable graph slice basis for dependency queries.
- [Interactive Graph View](archon_ui_brief.md#92-interactive-graph-view) - identifies scoped dependency and reverse usage graphs as appropriate graph use cases.
- [Layer Compliance Lens](archon_ui_brief.md#58-layer-compliance-lens) - identifies forbidden or upward dependency concerns that dependency views may surface.
- [Lens Availability Matrix](archon_ui_brief.md#11-lens-availability-matrix) - identifies artefact kinds where dependency lenses are available.

### Goal

Implement the first full graph-backed architectural lens: dependencies and dependents.

### User value

Users can answer what an artefact depends on, what depends on it, and which dependency edges are risky or cross boundaries.

### Dependencies

- WP010 Graph Projection Renderer Abstraction.

### Mandatory UI toolkit styling constraint

This work package MUST use the standard coloring, text sizing, spacing, and control styling provided by the selected UI toolkit and existing ArchonExplorer component primitives. It MUST NOT introduce custom colors, custom type scales, custom button/control treatments, card-like visual treatments, marketing-style hero styling, or other bespoke visual styling unless the user explicitly asks for that deviation in the active implementation request. Prefer plain desktop-IDE-style composition over web-page-style presentation.

### Scope

- Add Dependency Lens action for supported artefacts.
- Support inbound, outbound, and both directions.
- Support depth control.
- Support node kind filters.
- Support edge kind filters.
- Support hide tests, hide generated code, hide external packages where API supports it.
- Show risky or violating edges where available.
- Show dependency table alternative if provided by API.
- Integrate evidence inspector for selected nodes and edges.

### Out of scope

- Data access read/write semantics.
- Impact simulation.
- Snapshot diff.

### Primary UI surfaces

- Dependency lens tab.
- Lens controls panel.
- Graph/table projection area.
- Inspector integration.

### API assumptions

May use `POST /graph-slices` or future dependency-specific query endpoints.

### State model impact

- Lens controls are per-tab state.
- Results are server state keyed by snapshot, selected artefact, and lens parameters.

### Acceptance criteria

- User can open dependency lens from supported artefact.
- User can switch direction and depth.
- User can inspect dependency evidence.
- User can distinguish direct and transitive dependencies where supported.

### Validation

- Playwright journey for opening dependency lens.
- Playwright journey for changing direction/depth.
- Playwright journey for evidence inspection from dependency edge.
- UI build succeeds.

---

## WP012 - Data Access Lens

### UI brief references

- [Data Access Lens](archon_ui_brief.md#53-data-access-lens) - defines data access questions, read/write separation, visual forms, and unknown/dynamic SQL requirements.
- [Endpoint-to-Data Lens](archon_ui_brief.md#59-endpoint-to-data-lens) - defines runtime entry point to data questions and path forms.
- [Data Access Explorer](archon_ui_brief.md#75-data-access-explorer) - lists table, project, read/write matrix, context, stored procedure, raw SQL, and path views.
- [Matrix View](archon_ui_brief.md#95-matrix-view) - identifies read/write table access and table usage by project as matrix candidates.
- [Sankey / Flow View](archon_ui_brief.md#96-sankey--flow-view) - identifies endpoint/service/database and UI/API/table flow candidates.
- [Lens Availability Matrix](archon_ui_brief.md#11-lens-availability-matrix) - identifies artefact kinds where data access lenses are available.

### Goal

Implement evidence-backed data access investigation for projects, types, methods, endpoints, and database artefacts.

### User value

Users can answer which code reads or writes which tables, where stored procedures are called, and where unknown or dynamic SQL reduces confidence.

### Dependencies

- WP010 Graph Projection Renderer Abstraction.
- WP009 Project and Data Catalogues is recommended.

### Mandatory UI toolkit styling constraint

This work package MUST use the standard coloring, text sizing, spacing, and control styling provided by the selected UI toolkit and existing ArchonExplorer component primitives. It MUST NOT introduce custom colors, custom type scales, custom button/control treatments, card-like visual treatments, marketing-style hero styling, or other bespoke visual styling unless the user explicitly asks for that deviation in the active implementation request. Prefer plain desktop-IDE-style composition over web-page-style presentation.

### Scope

- Add Data Access Lens action for supported artefacts.
- Show read and write relationships separately.
- Show unknown/dynamic SQL visibly.
- Show grouped table of tables read/written.
- Show project-to-table path graph where available.
- Show endpoint-to-table path graph where available.
- Show table usage projection.
- Show evidence for table access relationships.
- Support pivot from table to readers, writers, endpoints, UI paths, impact, and evidence where available.

### Out of scope

- Full Sankey visualisation unless included by a later visualisation package.
- Full impact analysis.
- Simulated schema-change what-if.

### Primary UI surfaces

- Data Access Lens tab.
- Data access controls.
- Table projection.
- Graph projection where supported.
- Evidence inspector.

### API assumptions

May use `POST /graph-slices`, future data access query endpoints, or table projection endpoints.

### State model impact

- Data access filters are per-tab state.
- Results are server state keyed by snapshot, artefact, and lens parameters.

### Acceptance criteria

- User can open Data Access Lens for supported artefact.
- UI separately displays reads and writes.
- UI shows unknown/dynamic SQL rather than hiding it.
- User can inspect evidence for a table access relationship.
- User can pivot from table to readers or writers where supported.

### Validation

- Playwright journey for opening Data Access Lens.
- Playwright journey for read/write distinction.
- Playwright journey for dynamic SQL unknown display.
- Playwright journey for evidence inspection.
- UI build succeeds.

---

## WP013 - Findings and Rule Violation Workbench

### UI brief references

- [Rule Violation Lens](archon_ui_brief.md#56-rule-violation-lens) - defines rule violation questions, visual forms, and encodings.
- [Hotlist / Findings Explorer](archon_ui_brief.md#76-hotlist--findings-explorer) - defines filtering, grouping, evidence, impact, rule violation, and first/latest-seen needs.
- [Legacy Technology Lens](archon_ui_brief.md#57-legacy-technology-lens) - identifies modernization and legacy hotspot concerns that can appear as findings.
- [Visual Language](archon_ui_brief.md#8-visual-language) - defines severity, violation, risk, badge, and red-edge conventions.
- [Lens Availability Matrix](archon_ui_brief.md#11-lens-availability-matrix) - identifies finding and rule lenses.

### Goal

Implement findings, hotlist, and rule violation exploration.

### User value

Users can prioritize modernization and architecture risks, inspect evidence, and pivot into relevant lenses.

### Dependencies

- WP008 Node Overview and Evidence Inspector.
- WP010 Graph Projection Renderer Abstraction is recommended for graph overlays.

### Mandatory UI toolkit styling constraint

This work package MUST use the standard coloring, text sizing, spacing, and control styling provided by the selected UI toolkit and existing ArchonExplorer component primitives. It MUST NOT introduce custom colors, custom type scales, custom button/control treatments, card-like visual treatments, marketing-style hero styling, or other bespoke visual styling unless the user explicitly asks for that deviation in the active implementation request. Prefer plain desktop-IDE-style composition over web-page-style presentation.

### Scope

- Implement Hotlist / Findings Explorer.
- Filter by category.
- Filter by severity.
- Filter by status.
- Group by project.
- Group by technology.
- Group by rule.
- Show first seen / latest seen where available.
- Show evidence.
- Open rule violation lens.
- Overlay findings on dependency or layer graph where renderer support exists.

### Out of scope

- Editing rule catalog unless separately planned.
- Tolerated exception management unless separately planned.
- Snapshot trend charts unless included in Snapshot Diff Workbench.

### Primary UI surfaces

- Findings Explorer.
- Rule Violation Lens.
- Evidence inspector.

### API assumptions

May use future findings, rule, and graph slice endpoints.

### State model impact

- Filters and grouping are per-view or per-tab state.
- Findings payloads are server state.

### Acceptance criteria

- User can browse findings for active snapshot.
- User can filter by severity and category.
- User can inspect finding evidence.
- User can pivot from finding to affected artefact.

### Validation

- Playwright journey for findings list.
- Playwright journey for filtering.
- Playwright journey for evidence inspection.
- UI build succeeds.

---

## WP014 - Snapshot Diff Workbench

### UI brief references

- [Timeline / Snapshot Diff Lens](archon_ui_brief.md#512-timeline--snapshot-diff-lens) - defines drift questions, changed dependencies, new/resolved violations, coupling, legacy usage, and hotspot movement.
- [Snapshot Diff Explorer](archon_ui_brief.md#77-snapshot-diff-explorer) - lists compare snapshots, new/removed projects, dependencies, findings, centrality, data access, legacy footprint, and layer compliance changes.
- [Timeline / Diff View](archon_ui_brief.md#97-timeline--diff-view) - identifies timeline and diff visualisation purposes.
- [Snapshot Admin](archon_ui_brief.md#79-snapshot-admin) - provides lifecycle and comparison entry points.
- [Snapshot context model](archon_ui_brief.md#66-snapshot-context-model) - requires deleted or unavailable snapshot states to be visible.

### Goal

Implement architecture drift and snapshot comparison workflows.

### User value

Users can compare snapshots to see what changed, which violations are new or resolved, and whether architecture health is improving or degrading.

### Dependencies

- WP005 Snapshot Admin.
- WP006 Snapshot Context and Dashboard.
- WP013 Findings and Rule Violation Workbench is recommended for findings-specific diff pivots.

### Mandatory UI toolkit styling constraint

This work package MUST use the standard coloring, text sizing, spacing, and control styling provided by the selected UI toolkit and existing ArchonExplorer component primitives. It MUST NOT introduce custom colors, custom type scales, custom button/control treatments, card-like visual treatments, marketing-style hero styling, or other bespoke visual styling unless the user explicitly asks for that deviation in the active implementation request. Prefer plain desktop-IDE-style composition over web-page-style presentation.

### Scope

- Select two snapshots for comparison.
- Show new and removed projects.
- Show new and removed dependencies.
- Show new and resolved findings.
- Show changed centrality where available.
- Show changed data access where available.
- Show changed legacy footprint where available.
- Show changed layer compliance where available.
- Add before/after graph or table projections where renderer support exists.
- Provide clear unavailable state when one snapshot was deleted.

### Out of scope

- Simulated what-if.
- Long-term trend forecasting.
- Export report unless included in WP016.

### Primary UI surfaces

- Snapshot Diff Workbench.
- Snapshot comparison selector.
- Diff tables.
- Optional before/after graph area.

### API assumptions

May use future snapshot diff query endpoints.

### State model impact

- Comparison snapshot pair is per-tab state.
- Diff result is server state keyed by the selected pair.

### Acceptance criteria

- User can select two snapshots.
- UI shows changed entities in table-first form.
- UI handles deleted or unavailable comparison snapshots.
- User can pivot changed artefacts to overview where supported.

### Validation

- Playwright journey for selecting comparison snapshots.
- Playwright journey for changed item display.
- Playwright journey for unavailable snapshot in comparison.
- UI build succeeds.

---

## WP015 - Impact, Path, and Advanced Investigation Lenses

### UI brief references

- [Impact Lens](archon_ui_brief.md#54-impact-lens) - defines evidence-backed likely impact questions, rings, controls, and export entry points.
- [Path Lens](archon_ui_brief.md#55-path-lens) - defines how-A-reaches-B questions, ranked paths, confidence, and edge-by-edge evidence.
- [Configuration Lens](archon_ui_brief.md#510-configuration-lens) - defines configuration usage questions and unknown environment-supplied values.
- [Legacy Technology Lens](archon_ui_brief.md#57-legacy-technology-lens) - defines modernization blocker and legacy island questions.
- [Layer Compliance Lens](archon_ui_brief.md#58-layer-compliance-lens) - defines swimlane and forbidden-edge compliance questions.
- [UI Flow Lens](archon_ui_brief.md#511-ui-flow-lens) - defines UI-to-backend and UI-to-data questions after UI extraction exists.
- [Investigative What-If vs Simulated What-If](archon_ui_brief.md#12-investigative-what-if-vs-simulated-what-if) - requires initial support to remain evidence-backed rather than graph mutation simulation.

### Goal

Implement advanced investigative lenses after core operational, search, evidence, graph, dependency, and data access foundations are stable.

### User value

Users can ask deeper architectural questions such as what might be affected by a change and how one artefact reaches another.

### Dependencies

- WP011 Dependency Lens.
- WP012 Data Access Lens.
- WP013 Findings and Rule Violation Workbench where findings are involved.

### Mandatory UI toolkit styling constraint

This work package MUST use the standard coloring, text sizing, spacing, and control styling provided by the selected UI toolkit and existing ArchonExplorer component primitives. It MUST NOT introduce custom colors, custom type scales, custom button/control treatments, card-like visual treatments, marketing-style hero styling, or other bespoke visual styling unless the user explicitly asks for that deviation in the active implementation request. Prefer plain desktop-IDE-style composition over web-page-style presentation.

### Scope

- Implement Impact Lens.
- Implement Path Lens.
- Add endpoint-to-data path workflows where API supports them.
- Add configuration lens where API supports it.
- Add legacy technology lens where API supports it.
- Add layer compliance lens where API supports it.
- Add UI flow lens after UI extraction data is available.
- Support evidence trail for paths and impact rings.

### Out of scope

- Simulated what-if with temporary graph mutation.
- Automated remediation.
- Code changes from the UI.

### Primary UI surfaces

- Impact Lens.
- Path Lens.
- Configuration Lens.
- Legacy Technology Lens.
- Layer Compliance Lens.
- UI Flow Lens.

### API assumptions

May use future routes such as:

```http
POST /paths
POST /impact
POST /graph-slices
```

### State model impact

- Lens parameters are per-tab state.
- Results are server state keyed by snapshot, artefacts, and parameters.

### Acceptance criteria

- User can open supported advanced lenses.
- UI explains traversal and included/excluded relationship kinds.
- User can inspect evidence for each path or impact edge.
- UI distinguishes investigative what-if from simulated what-if.

### Validation

- Playwright journey for Impact Lens.
- Playwright journey for Path Lens.
- Playwright journey for explanation panel.
- UI build succeeds.

---

## WP016 - Export and Copilot Handoff

### UI brief references

- [Core interaction model](archon_ui_brief.md#archon-ui-brief) - ends the investigation flow with export or hand off.
- [Graph Slice Definition](archon_ui_brief.md#41-graph-slice-definition) - requires slices to be reusable by Markdown exports, impact reports, and AI prompt context generation.
- [Impact Lens](archon_ui_brief.md#54-impact-lens) - includes export impact report as a primary control.
- [API Implications](archon_ui_brief.md#13-api-implications) - identifies export generation and `/exports/impact-report` route shape.
- [Human decision, AI assistance](archon_ui_brief.md#157-human-decision-ai-assistance) - requires evidence-backed context for humans and Copilot.
- [Open Design Questions](archon_ui_brief.md#16-open-design-questions) - identifies exported impact report structure and MCP reuse as design questions.

### Goal

Allow users to export evidence-backed reports and prepare context for Copilot or MCP workflows.

### User value

Users can share findings, produce impact reports, and hand off scoped architectural context for implementation planning.

### Dependencies

- WP008 Node Overview and Evidence Inspector.
- WP011 Dependency Lens or later lens packages depending on export scope.

### Mandatory UI toolkit styling constraint

This work package MUST use the standard coloring, text sizing, spacing, and control styling provided by the selected UI toolkit and existing ArchonExplorer component primitives. It MUST NOT introduce custom colors, custom type scales, custom button/control treatments, card-like visual treatments, marketing-style hero styling, or other bespoke visual styling unless the user explicitly asks for that deviation in the active implementation request. Prefer plain desktop-IDE-style composition over web-page-style presentation.

### Scope

- Export impact reports where API supports it.
- Export evidence bundles for selected artefacts.
- Export graph slice summaries.
- Generate Copilot-ready context from selected snapshot, artefact, lens, evidence, and unknowns.
- Preserve route convention and safe diagnostic rules.
- Make export output explain snapshot identity and query/lens parameters.

### Out of scope

- Arbitrary Cypher export.
- Writing directly into source code.
- Automated remediation.

### Primary UI surfaces

- Export actions in investigation tabs.
- Export dialog.
- Generated report status.

### API assumptions

Future route shape from the brief:

```http
POST /exports/impact-report
```

### State model impact

- Export job state may use server state or background job monitoring depending on API design.
- Generated local preview state is per-tab or modal state.

### Acceptance criteria

- User can request an export from supported lens or artefact.
- Export identifies snapshot and parameters.
- Export does not include unsafe diagnostics.
- User can copy or download generated context where supported.

### Validation

- Playwright journey for export dialog.
- Playwright journey for safe report generation or mocked response.
- UI build succeeds.

---

## WP017 - Polish, Accessibility, and Resilience

### UI brief references

- [Workbench desktop shell](archon_ui_brief.md#65-workbench-desktop-shell) - defines shell behaviours that need final keyboard, persistence, and resilience review.
- [Visual Language](archon_ui_brief.md#8-visual-language) - requires consistent visual meaning across views.
- [Suggested shadcn/ui workbench primitives](archon_ui_brief.md#141-suggested-shadcnui-workbench-primitives) - requires consistency with mandatory shadcn/ui primitives.
- [Safety, empty, loading, and error states](archon_ui_brief.md#143-safety-empty-loading-and-error-states) - defines common operational states to harden.
- [Security and operational boundaries](archon_ui_brief.md#144-security-and-operational-boundaries) - defines safe destructive actions, no Cypher console, and safe diagnostics.
- [Testing and validation expectations](archon_ui_brief.md#145-testing-and-validation-expectations) - defines Playwright, keyboard, and accessibility validation expectations.
- [Design Principles](archon_ui_brief.md#15-design-principles) - defines the cross-cutting quality bar for scoped views, evidence, unknowns, renderer agnosticism, and human decision support.

### Goal

Harden ArchonExplorer after core operational and investigation workflows exist.

### User value

Users get a reliable, accessible, keyboard-friendly workbench that handles slow APIs, missing data, large slices, and destructive operations consistently.

### Dependencies

- Prior feature packages.

### Mandatory UI toolkit styling constraint

This work package MUST use the standard coloring, text sizing, spacing, and control styling provided by the selected UI toolkit and existing ArchonExplorer component primitives. It MUST NOT introduce custom colors, custom type scales, custom button/control treatments, card-like visual treatments, marketing-style hero styling, or other bespoke visual styling unless the user explicitly asks for that deviation in the active implementation request. Prefer plain desktop-IDE-style composition over web-page-style presentation.

### Scope

- Review keyboard navigation.
- Review screen-reader labels and focus management.
- Review destructive action dialogs.
- Review empty, loading, stale, and error states.
- Review large graph and large table performance.
- Add telemetry hooks if repository guidance supports them.
- Improve command palette discoverability.
- Improve status bar clarity.
- Improve saved preferences and layout restoration.
- Review route convention and API error consistency.
- Review shadcn/ui consistency.

### Out of scope

- New major feature areas.
- New API backend capabilities unless needed to fix UX defects.

### Primary UI surfaces

All ArchonExplorer surfaces.

### API assumptions

No new API assumptions by default.

### State model impact

May refine persisted preferences, caching, retries, and background refresh policies.

### Acceptance criteria

- Core journeys are keyboard accessible.
- Destructive actions require clear confirmation.
- API unavailable states are understandable.
- No-current-snapshot and deleted-snapshot states are consistent.
- Large-slice guardrails are in place.
- shadcn/ui remains the mandatory component foundation.

### Validation

- Playwright regression journey set.
- Accessibility checks for shell, forms, tables, dialogs, and command palette.
- UI build succeeds.
- Focused manual smoke validation in Aspire.

---

# 5. Explicit Deferral of Visualisation Work

The following features are intentionally deferred until after WP004 Extraction Center, WP005 Snapshot Admin, and WP006 Snapshot Context and Dashboard are complete:

```text
Interactive graph rendering
Health maps
Dependency graphs
Data access path graphs
Layered graph views
Matrix views
Sankey views
Timeline visualisations
Snapshot diff charts
Advanced impact/path visualisations
```

This deferral is not a reduction in product ambition. It protects the implementation sequence: ArchonExplorer must first make it easy to create, monitor, select, and administer the snapshots that all visualisations depend on.

---

# 6. First Tranche Recommendation

The first tranche should stop after operational UI is useful:

```text
WP001 ArchonExplorer Foundation
WP002 API Client and Runtime Foundation
WP003 Workbench Desktop Shell
WP004 Extraction Center
WP005 Snapshot Admin
WP006 Snapshot Context and Dashboard
```

At the end of this tranche, ArchonExplorer should be able to answer:

```text
Can I launch the UI from Aspire?
Can I see API connectivity?
Can I start an extraction run?
Can I monitor extraction progress?
Can I inspect extraction history?
Can I see produced snapshot identities?
Can I list snapshots?
Can I select the active snapshot?
Can I safely delete snapshots?
Can I tell whether current means the latest completed snapshot?
```

Only after these questions are answered should the project move to search, evidence, lenses, and visualisations.

---

# 7. Documentation and Wiki Expectations

Each work package should update contributor-facing documentation only where it changes how contributors build, run, validate, or understand ArchonExplorer.

Documentation should explain current behaviour, not planned behaviour. Planning detail belongs in work-package documents; durable contributor guidance belongs in the wiki.

For UI work packages, likely documentation updates include:

- how to run ArchonExplorer through Aspire;
- how frontend package restore/build/test works;
- how API base URL configuration is resolved;
- how extraction and snapshot management are validated;
- how shadcn/ui components are added and maintained;
- how Playwright journeys are run.

---

# 8. Non-Negotiable Constraints

The following constraints apply across all UI work packages:

```text
ArchonExplorer is React and TypeScript.
Use of shadcn/ui is mandatory for normal application UI.
ArchonExplorer is hosted by the Archon Aspire composition.
Archon API routes do not use a common /api prefix.
The snapshot identifier current means latest completed snapshot.
Extraction-driving UI is implemented before visualisation work.
Snapshot management is implemented before visualisation work.
No arbitrary Cypher console is exposed in the UI.
User-visible diagnostics are safe and do not expose secrets or infrastructure internals.
Graph rendering is scoped and renderer-agnostic once it is introduced.
Evidence and unknowns remain first-class throughout investigation features.
```
