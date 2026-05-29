# WP003 Specification - Workbench Desktop Shell

## Document Control

| Field | Value |
| --- | --- |
| Work Package | WP003 - Workbench Desktop Shell |
| Output Path | `docs/022-Workbench-Desktop-Shell/spec-wp003-workbench-desktop-shell.md` |
| Source Work Package | `docs/foundation/work-packages-ui.md` WP003 |
| Source Brief | `docs/foundation/archon_ui_brief.md` |
| Specification Basis | Same repository work-package specification pattern as `docs/020-ArchonExplorer-Foundation/spec-wp001-archonexplorer-foundation.md` and `docs/021-API-Client-and-Runtime-Foundation/spec-wp002-api-client-and-runtime-foundation.md`. `spec-template_v1.1.md` was requested by the prompt but is not present in the workspace. |
| Status | Draft |
| Audience | Product owner, architect, frontend implementer, test engineer |

## 1. Overview

### 1.1 Purpose

This specification defines WP003 for the ArchonExplorer UI work-package sequence. WP003 turns the initial ArchonExplorer foundation and shared runtime into a coherent desktop-style browser workbench that can host operational and investigative features without presenting them as disconnected pages.

The package establishes the top-level frame, activity navigation, sidebar, tabbed work area, resizable panel structure, bottom panel, status bar, command palette shell, notification placement, and persisted layout preferences required by later extraction, snapshot, search, evidence, and lens work packages.

### 1.2 Background

ArchonExplorer must behave like an investigation workbench in the browser. Users should eventually be able to keep multiple investigations open, monitor extraction work, administer snapshots, inspect evidence, preserve context, and pivot between architectural questions. WP003 provides the shell that makes those workflows possible while deliberately avoiding feature-specific behavior that belongs to later packages.

WP001 created the React, TypeScript, shadcn/ui, and Aspire-hosted application foundation. WP002 created the typed API client and frontend runtime foundations. WP003 uses those foundations to establish the durable workbench layout and local state model that later operational and analytical areas will plug into.

### 1.3 High-Level Scope

WP003 covers:

- Top-level ArchonExplorer app frame.
- Activity bar for major workbench areas.
- Primary sidebar region for explorers and contextual navigation.
- Tabbed document or investigation work area.
- Dockable or resizable panel layout.
- Bottom panel for extraction runs, diagnostics, and background work placeholders.
- Status bar slots for active snapshot, API connectivity, background work, and selected context.
- Command palette shell using shadcn/ui `Command` / cmdk.
- Notification and toast placement using the WP002 notification runtime.
- Local workbench state for active activity, open tabs, active tab, panel layout, command palette visibility, bottom panel visibility, and notification visibility.
- Persisted layout preferences for panel sizes and shell preferences.
- Focused Playwright and accessibility validation for opening the shell and using keyboard-driven shell affordances.

WP003 excludes real extraction submission, real snapshot deletion, search results, graph rendering, lens execution, evidence rendering, and feature-complete operational screens.

## 2. System Context

### 2.1 Product Context

ArchonExplorer is the browser-delivered workbench for architectural investigation over Archon data. The UI roadmap intentionally delivers operational UI and snapshot administration before graph visualisation and lens analytics. WP003 is the shared frame that allows those future areas to appear as workbench activities, tabs, panels, status items, commands, and notifications rather than as unrelated web pages.

The shell must support both operational work, such as extraction monitoring and snapshot administration, and later investigation work, such as search, node overview, evidence inspection, dependency lenses, data-access lenses, and diff views. It must make context visible without implying unavailable features are complete.

### 2.2 Source References

WP003 shall align with these source materials:

- `docs/foundation/work-packages-ui.md` WP003 - Workbench Desktop Shell.
- `docs/foundation/work-packages-ui.md` section 1.2 for snapshot context expectations.
- `docs/foundation/work-packages-ui.md` section 1.4 for workbench-not-admin-console behavior.
- `docs/foundation/work-packages-ui.md` section 1.6 for mandatory toolkit styling constraints.
- `docs/foundation/archon_ui_brief.md` binding UI product mandate for ArchonExplorer, React, TypeScript, shadcn/ui, and Aspire hosting.
- `docs/foundation/archon_ui_brief.md` section 6.5 for workbench desktop shell behavior.
- `docs/foundation/archon_ui_brief.md` section 6.6 for snapshot-context visibility and tab binding expectations.
- `docs/foundation/archon_ui_brief.md` section 7.2 for global search and command palette behavior.
- `docs/foundation/archon_ui_brief.md` section 7.3 for the investigation workspace layout model.
- `docs/foundation/archon_ui_brief.md` section 14.1 for shadcn/ui primitive expectations.
- `docs/foundation/archon_ui_brief.md` section 14.2 for UI state ownership.
- `docs/foundation/archon_ui_brief.md` section 14.3 for safe empty, loading, and error states.
- `docs/foundation/archon_ui_brief.md` section 14.5 for UI validation expectations.

### 2.3 Users and Stakeholders

| Stakeholder | Interest |
| --- | --- |
| Product owner | Confirms ArchonExplorer behaves like a workbench and remains aligned with the planned UI roadmap. |
| Architect | Confirms shell state, API runtime usage, safety boundaries, and future feature extension points are durable. |
| Frontend implementer | Uses the shell layout, state model, commands, tabs, panels, status slots, and notification placement for later features. |
| Test engineer | Validates shell rendering, keyboard access, command palette behavior, tab behavior, panel layout, and accessibility expectations. |
| Future ArchonExplorer user | Gets a stable desktop-style frame where operational work and investigations can coexist without losing context. |

## 3. Component Summary

### 3.1 Workbench App Frame

The app frame is the persistent top-level structure around all ArchonExplorer work. It owns the visual layout for the activity bar, sidebar, main work area, bottom panel, status bar, command palette host, and notification host.

The frame shall be desktop-IDE-style rather than marketing-page-style. It shall use existing shadcn/ui-compatible primitives and standard toolkit styling without introducing custom colors, custom type scales, card-like marketing treatments, or a second component library.

### 3.2 Activity Bar

The activity bar provides compact navigation between major workbench areas. In WP003 it shall expose placeholder activities aligned with the UI roadmap, such as Dashboard, Extraction Center, Snapshots, Search, Projects, Findings, and Settings or Diagnostics.

Activities may show placeholder states, but they must not claim that later feature workflows are implemented.

### 3.3 Primary Sidebar

The primary sidebar hosts contextual navigation for the selected activity. In WP003 it shall provide a consistent region and placeholder content for activity-specific explorers, filters, or navigation trees. Later work packages will replace placeholder content with real extraction history, snapshot lists, project catalogues, search refinements, and findings navigation.

### 3.4 Tabbed Work Area

The main work area hosts open workbench tabs or documents. WP003 shall implement the shell-level tab model and at least one default tab or start tab. Tabs shall support future investigation documents without requiring later packages to redesign the workbench frame.

Feature tabs introduced in later packages may represent extraction centers, snapshot administration, dashboards, node overviews, evidence views, graph slices, table views, or diff views.

### 3.5 Resizable Panel Layout

The shell shall include a resizable layout structure suitable for primary content, side content, and bottom content. WP003 shall establish panel size persistence and reset behavior without implementing advanced docking, arbitrary window management, or split-view comparison workflows unless those can be introduced as honest placeholders.

### 3.6 Bottom Panel

The bottom panel is the workbench region for background extraction runs, safe diagnostics, and background work placeholders. WP003 shall make the region available and controllable, but it shall not implement real extraction history or raw diagnostic consoles.

### 3.7 Status Bar

The status bar provides compact, persistent context. WP003 shall reserve slots for active snapshot, API connectivity, background work, and selected context. The active snapshot slot shall use `current` terminology consistently, while making clear when no real snapshot selection workflow is implemented yet.

### 3.8 Command Palette Shell

The command palette is the global command and search entry point. WP003 shall implement the shell, keyboard shortcut, grouped placeholder commands, focus behavior, and command execution model for shell actions such as opening activities, toggling panels, opening the default tab, or focusing shell regions.

Search results and architecture artefact selection belong to later packages and shall not be simulated as real data in WP003.

### 3.9 Notification Host

The notification host displays shadcn/ui-compatible toasts from the WP002 notification runtime. WP003 shall place the host in the shell so later packages can surface extraction, snapshot, API availability, destructive action, and background work notifications consistently.

### 3.10 Workbench State Store

WP003 introduces local workbench state for shell behavior. This state is separate from TanStack Query server state and must not invent architecture facts, extraction runs, snapshots, graph nodes, evidence, metrics, or findings.

## 4. Functional Requirements

### 4.1 Top-Level App Frame

| ID | Requirement |
| --- | --- |
| FR-001 | ArchonExplorer shall render a persistent top-level workbench app frame. |
| FR-002 | The app frame shall include an activity bar, primary sidebar, main work area, bottom panel, status bar, command palette host, and notification host. |
| FR-003 | The app frame shall preserve the visible `ArchonExplorer` application identity established by WP001. |
| FR-004 | The app frame shall feel like a desktop-style investigation workbench, not a conventional admin console or marketing landing page. |
| FR-005 | The app frame shall be extensible so later work packages can add operational and investigative activities without replacing the shell. |
| FR-006 | The app frame shall avoid graph, lens, evidence, extraction, and snapshot feature behavior that is assigned to later work packages. |

### 4.2 Activity Bar

| ID | Requirement |
| --- | --- |
| FR-007 | The shell shall include an activity bar for major workbench areas. |
| FR-008 | The activity bar shall track the active activity in local workbench state. |
| FR-009 | The activity bar shall include placeholder activities aligned with the roadmap, including Dashboard, Extraction Center, Snapshots, Search, Projects, Findings, and Settings or Diagnostics unless implementation planning selects equivalent labels. |
| FR-010 | Selecting an activity shall update the primary sidebar and any activity-specific placeholder context. |
| FR-011 | Activity items shall be keyboard reachable and have accessible names. |
| FR-012 | Activity items shall not navigate away from the workbench frame. |
| FR-013 | Activity placeholders shall clearly distinguish unavailable future feature behavior from implemented shell navigation. |

### 4.3 Primary Sidebar

| ID | Requirement |
| --- | --- |
| FR-014 | The shell shall include a primary sidebar region. |
| FR-015 | The sidebar shall display contextual placeholder content for the active activity. |
| FR-016 | The sidebar shall support collapsed and expanded states if practical within the selected shell layout. |
| FR-017 | Sidebar state shall be part of local workbench state or persisted layout preferences as appropriate. |
| FR-018 | Sidebar content shall avoid raw backend diagnostics, arbitrary query input, and misleading mock architecture data. |
| FR-019 | Sidebar labels shall use UI brief vocabulary so later work packages can replace placeholders with real explorers. |

### 4.4 Tabbed Work Area

| ID | Requirement |
| --- | --- |
| FR-020 | The shell shall include a tabbed document or work area. |
| FR-021 | The tab model shall track open tabs and the active tab in local workbench state. |
| FR-022 | WP003 shall provide at least one default start or welcome tab. |
| FR-023 | Tabs shall support readable titles and stable tab identifiers. |
| FR-024 | Tabs shall be keyboard reachable and shall preserve focus behavior consistent with accessible tab patterns. |
| FR-025 | The shell shall support opening placeholder workbench tabs from shell actions or placeholder commands. |
| FR-026 | The shell shall support closing non-required placeholder tabs if implementation planning determines closable tabs are needed for later feature parity. |
| FR-027 | The tab state model shall allow later tabs to bind to selected artefact, selected lens, filters, graph settings, selected node or edge, inspector state, and snapshot context. |
| FR-028 | WP003 shall not implement real search-result tabs, graph tabs, evidence tabs, snapshot diff tabs, or lens execution tabs. |

### 4.5 Resizable Panel Layout

| ID | Requirement |
| --- | --- |
| FR-029 | The shell shall include a resizable panel structure for workbench regions. |
| FR-030 | The layout shall support resizing at least the sidebar/main area and the main/bottom-panel area where practical. |
| FR-031 | Panel sizes and visibility preferences shall be persisted locally. |
| FR-032 | The shell shall provide a safe reset path for layout preferences, either through a visible command, settings placeholder, or implementation-level fallback. |
| FR-033 | Panel resizing shall not require custom visual styling beyond standard toolkit-compatible affordances. |
| FR-034 | WP003 shall not implement full arbitrary docking, detachable windows, or advanced split-view comparison unless represented only as future placeholders. |

### 4.6 Bottom Panel

| ID | Requirement |
| --- | --- |
| FR-035 | The shell shall include a bottom panel region. |
| FR-036 | Bottom panel visibility shall be controlled by local workbench state. |
| FR-037 | The bottom panel shall provide placeholder sections or tabs for background work, extraction runs, and diagnostics. |
| FR-038 | Bottom panel placeholders shall not display real extraction run history in WP003. |
| FR-039 | Bottom panel diagnostics shall be safe and shall not expose raw stack traces, connection strings, environment variables, raw Cypher, Neo4j internal identifiers, or driver-specific details. |
| FR-040 | The bottom panel shall be openable through shell controls and the command palette. |

### 4.7 Status Bar

| ID | Requirement |
| --- | --- |
| FR-041 | The shell shall include a persistent status bar. |
| FR-042 | The status bar shall reserve a slot for active snapshot context. |
| FR-043 | The active snapshot slot shall use `current` terminology consistently with the UI brief. |
| FR-044 | The status bar shall reserve a slot for API connectivity state from WP002. |
| FR-045 | The status bar shall reserve a slot for background work state. |
| FR-046 | The status bar shall reserve a slot for selected context. |
| FR-047 | The status bar shall distinguish placeholder state from real feature state where later functionality is not yet implemented. |
| FR-048 | Status bar items shall not rely on color alone to communicate state. |

### 4.8 Command Palette Shell

| ID | Requirement |
| --- | --- |
| FR-049 | The shell shall include a global command palette using shadcn/ui `Command` / cmdk. |
| FR-050 | The command palette shall open by keyboard shortcut. |
| FR-051 | The command palette shall be openable through an on-screen affordance. |
| FR-052 | The command palette shall support grouped placeholder commands. |
| FR-053 | Commands shall include shell-level actions such as switching activities, toggling the bottom panel, opening or focusing a start tab, and resetting layout preferences if implemented. |
| FR-054 | Command execution shall use the local workbench state model rather than browser page navigation. |
| FR-055 | Command palette focus shall move into the palette on open and return to a sensible location on close. |
| FR-056 | The command palette shall not display real search results or architecture artefacts in WP003. |
| FR-057 | Placeholder search text shall make clear that global architecture search is implemented in a later work package. |

### 4.9 Notification Placement

| ID | Requirement |
| --- | --- |
| FR-058 | The shell shall include the notification/toast host provided by the WP002 runtime. |
| FR-059 | Notifications shall use shadcn/ui-compatible styling and placement. |
| FR-060 | Notifications shall be available for shell-level feedback such as layout reset or unavailable placeholder actions. |
| FR-061 | Notifications shall not be the only representation of persistent page-level or shell-level errors. |
| FR-062 | Notifications shall use safe messages and normalized diagnostic content. |

### 4.10 Layout Persistence

| ID | Requirement |
| --- | --- |
| FR-063 | WP003 shall persist panel sizes and shell preferences locally. |
| FR-064 | Persisted preferences shall include enough versioning or fallback behavior to recover from incompatible stored layout data. |
| FR-065 | Persisted shell preferences may include active activity, sidebar collapsed state, bottom panel visibility, and panel sizes. |
| FR-066 | Persisted preferences shall not include API secrets, connection strings, environment variable values, raw backend diagnostics, or architecture facts. |
| FR-067 | If stored preferences are invalid, the shell shall recover to safe defaults. |

### 4.11 Out-of-Scope Functional Behavior

| ID | Requirement |
| --- | --- |
| FR-068 | WP003 shall not implement real extraction submission. |
| FR-069 | WP003 shall not implement extraction run history from the API. |
| FR-070 | WP003 shall not implement snapshot listing, snapshot selection, snapshot deletion, or delete-all behavior. |
| FR-071 | WP003 shall not implement global search results. |
| FR-072 | WP003 shall not implement node overview, evidence inspection, graph rendering, graph projection rendering, or lens execution. |
| FR-073 | WP003 shall not introduce arbitrary Cypher input, graph query consoles, filesystem browsing, or unsafe diagnostic consoles. |
| FR-074 | WP003 shall not implement authentication or authorization. |

## 5. Non-Functional Requirements

### 5.1 Styling and Toolkit Consistency

| ID | Requirement |
| --- | --- |
| NFR-001 | WP003 shall use shadcn/ui-compatible primitives for command palette, tabs, menus, badges, tooltips, popovers, notifications, and ordinary shell controls where those controls are required. |
| NFR-002 | WP003 shall not introduce another ordinary UI component library for shell, forms, tables, dialogs, command palette, tabs, menus, badges, tooltips, popovers, or notification patterns. |
| NFR-003 | WP003 shall use standard toolkit coloring, text sizing, spacing, and control styling. |
| NFR-004 | WP003 shall not introduce custom theme colors, custom type scales, custom button treatments, card-like marketing visual treatments, or bespoke web-page styling unless explicitly requested in the active implementation request. |
| NFR-005 | Custom CSS may be used for desktop workbench layout mechanics where necessary, but it shall not replace the selected component system. |

### 5.2 Accessibility and Keyboard Support

| ID | Requirement |
| --- | --- |
| NFR-006 | Shell regions shall use semantic landmarks or accessible labels where appropriate. |
| NFR-007 | Activity bar items, tabs, command palette controls, bottom panel controls, and reset controls shall be keyboard reachable. |
| NFR-008 | Keyboard focus shall remain visible throughout the shell. |
| NFR-009 | The command palette shall manage focus correctly when opened and closed. |
| NFR-010 | The shell shall support basic screen-reader understanding of major regions and selected states. |
| NFR-011 | Status indicators shall not communicate state by color alone. |

### 5.3 Maintainability

| ID | Requirement |
| --- | --- |
| NFR-012 | Workbench shell layout code shall be separable from feature implementation code. |
| NFR-013 | Local workbench state shall be centralized enough to avoid duplicate activity, tab, panel, and command-palette state scattered across feature components. |
| NFR-014 | Shell action registration shall allow later packages to add commands without rewriting the command palette. |
| NFR-015 | Shell activity registration shall allow later packages to add activity content without replacing the app frame. |
| NFR-016 | The layout shall use names and concepts that match the UI brief vocabulary. |

### 5.4 Safety and Diagnostics

| ID | Requirement |
| --- | --- |
| NFR-017 | User-visible diagnostics shall be safe and shall not expose raw stack traces. |
| NFR-018 | User-visible diagnostics shall not expose connection strings, environment variables, credentials, tokens, raw Cypher, Neo4j internal identifiers, driver-specific diagnostics, or arbitrary backend exception text. |
| NFR-019 | Placeholder feature states shall be explicit and non-deceptive. |
| NFR-020 | API unavailable or no-current-snapshot placeholder states shall be represented as workbench context, not as fatal shell failures. |

### 5.5 Performance and Responsiveness

| ID | Requirement |
| --- | --- |
| NFR-021 | WP003 shall not add graph rendering libraries, visualization libraries, or heavy data-grid dependencies. |
| NFR-022 | Shell interactions such as switching activities, opening the command palette, toggling panels, and switching tabs shall be responsive without requiring API calls. |
| NFR-023 | Persisted layout reads shall not block initial rendering for an unreasonable time. |
| NFR-024 | The production UI build shall complete without requiring a running Archon API. |

## 6. User Experience Requirements

### 6.1 Workbench Feel

When a user opens ArchonExplorer after WP003, the application shall present a stable desktop-style workbench. The frame should make it clear that users are inside an investigation environment where work can accumulate in activities, tabs, panels, status context, and background work areas.

The experience shall avoid a web-page or product-landing presentation. It shall also avoid making operational areas feel like a separate admin console.

### 6.2 Shell Regions

The default shell should visibly contain:

- activity bar;
- primary sidebar;
- tabbed work area;
- bottom panel;
- status bar;
- command palette affordance;
- notification host.

The implementation may choose exact proportions and labels, but the regions must be recognizable and aligned with the UI brief.

### 6.3 Placeholder Activities

Placeholder activities should help users and contributors understand where later capabilities will appear. Suggested activities are:

- Dashboard.
- Extraction Center.
- Snapshots.
- Search.
- Projects.
- Findings.
- Settings or Diagnostics.

Placeholders shall use plain, safe unavailable-feature messaging. They shall not show fabricated architecture data.

### 6.4 Default Tabs

WP003 should include a default start tab that explains the shell state and available placeholder activities. Additional placeholder tabs may be opened through the command palette or activity actions if they help validate tab behavior.

Tabs should be treated as workbench documents. Later packages should be able to bind tabs to snapshot context, selected artefacts, selected lenses, filters, layout preferences, and inspector state.

### 6.5 Command Palette Behavior

The command palette should feel like the future global entry point without pretending search is implemented. Placeholder commands should be grouped by shell area, such as navigation, layout, panels, and help or diagnostics.

The keyboard shortcut should be documented in the UI and covered by validation.

### 6.6 Status Bar Content

The status bar should reserve space for:

- active snapshot, with `current` terminology;
- API connectivity or readiness state from WP002;
- background work placeholder state;
- selected context placeholder state.

A user should be able to distinguish shell-level context from feature data that is intentionally unavailable until later packages.

## 7. Technical Requirements

### 7.1 Frontend Stack

WP003 shall continue using the frontend stack established by WP001 and WP002:

| Concern | Selection |
| --- | --- |
| Application toolchain | Vite |
| UI framework | React |
| Language | TypeScript |
| Package manager | npm |
| Component system | shadcn/ui |
| Server state | TanStack Query from WP001/WP002 |
| API runtime | WP002 Archon API client/runtime foundation |
| Command palette | shadcn/ui `Command` / cmdk |
| Desktop-style panes | Resizable panel primitives compatible with the selected frontend stack |

### 7.2 Project Location

The WP003 implementation shall extend the ArchonExplorer application at:

```text
src/ArchonExplorer
```

The specification does not require a precise source-folder layout, but implementation planning should separate shell, layout, state, command, runtime, and feature-placeholder concerns so later packages can extend them cleanly.

### 7.3 Workbench State Model

WP003 shall introduce local workbench state for:

- active activity;
- open workbench tabs;
- active tab;
- sidebar state;
- panel sizes;
- command palette visibility;
- bottom panel visibility;
- notification visibility or integration state.

This local state shall remain separate from TanStack Query server state. It shall not invent architecture facts or duplicate API source-of-truth state.

### 7.4 Command Model

WP003 shall define a shell command model sufficient for placeholder commands and future extension. Commands should have stable identifiers, labels, grouping, optional keyboard hints, disabled or unavailable states, and actions that operate on the local shell state.

The command model shall avoid hard-coding every command directly inside rendering code if a small registration pattern can support later work packages more cleanly.

### 7.5 Activity Model

WP003 shall define a shell activity model sufficient for placeholder activities and future extension. Activities should have stable identifiers, labels, accessible names, optional icons, selected state, sidebar content, and optional default tab behavior.

The activity model shall allow WP004 and WP005 to add Extraction Center and Snapshot Admin behavior without replacing the shell.

### 7.6 Tab Model

WP003 shall define a tab model sufficient for shell tabs and future investigation tabs. At minimum, tabs should include:

- stable tab identifier;
- title;
- type or kind;
- closeability where applicable;
- associated activity or shell area where useful;
- placeholder content state.

The model should be able to evolve later to include selected snapshot, selected artefact, selected lens, filters, graph settings, selected node or edge, and inspector state.

### 7.7 Persistence

WP003 shall persist shell preferences using browser-local persistence suitable for frontend preferences. Stored values shall be limited to UI preferences and shall recover safely if incompatible or malformed.

Implementation planning may choose exact persistence keys, but keys should be scoped to ArchonExplorer and versioned or otherwise recoverable.

### 7.8 API Connectivity Integration

WP003 shall consume API connectivity state from WP002 for status bar display. It shall not create a competing API health-check model.

If WP002 connectivity state is unavailable during implementation, WP003 shall use the established runtime abstraction or an honest placeholder and record the integration gap in the implementation plan.

## 8. Data and State Requirements

### 8.1 Server State

WP003 shall not add new server-state domains. TanStack Query remains the source for API-backed state, but WP003 uses only API connectivity state from WP002.

Extraction runs, snapshots, search results, graph slices, evidence, findings, and dashboard summaries remain out of scope for WP003 server state.

### 8.2 Local Workbench State

Local workbench state shall include shell interaction state only. The state may contain activity identifiers, tab descriptors, panel sizes, collapsed or expanded flags, command palette visibility, bottom panel visibility, and selected placeholder region.

Local workbench state shall not contain fabricated snapshots, fabricated graph nodes, fabricated evidence, or mock extraction histories presented as real application data.

### 8.3 Persisted Preferences

Persisted preferences may include:

- active activity;
- sidebar collapsed state;
- bottom panel visibility;
- panel sizes;
- last active shell tab where safe;
- theme preference if already supported by WP001.

Persisted preferences shall not include operational secrets, connection strings, raw diagnostics, repository path values beyond those already safely handled by future feature specs, or architecture fact data.

### 8.4 Notification State

Notification state shall use the WP002 notification infrastructure. WP003 may create shell-level informational notifications only where useful for validation or user feedback, such as confirming a layout reset or explaining that a placeholder feature is not yet implemented.

## 9. Integration Requirements

### 9.1 WP001 Foundation Integration

WP003 shall preserve the WP001 application foundation, shadcn/ui setup, theme foundation, TanStack Query provider setup, and Aspire-hosted local development behavior.

### 9.2 WP002 Runtime Integration

WP003 shall use WP002 runtime foundations for API connectivity state, safe errors, and notification infrastructure. It shall not duplicate route constants, API clients, error shaping, polling helpers, or notification runtimes.

### 9.3 Future Work Package Integration

WP003 shall provide clear extension points for:

- WP004 Extraction Center as an activity, tab, bottom-panel source, command set, status item, and notification source.
- WP005 Snapshot Admin as an activity, tab, status item, command set, and destructive-dialog consumer.
- WP006 Snapshot Context and Dashboard as the real active snapshot context provider and dashboard activity.
- WP007 Global Search and Command Palette as real search results and artefact-opening commands.
- Later investigation work packages as tabbed node overview, evidence inspector, graph, lens, and diff experiences.

## 10. Validation Requirements

### 10.1 Required Validation

WP003 implementation shall be validated with:

| ID | Validation |
| --- | --- |
| VAL-001 | Restore frontend dependencies from `src/ArchonExplorer` using npm if package dependencies changed. |
| VAL-002 | Run the frontend typecheck script successfully. |
| VAL-003 | Run the frontend production build script successfully. |
| VAL-004 | Run focused Playwright coverage that opens the workbench shell. |
| VAL-005 | Run focused Playwright coverage that opens the command palette by keyboard shortcut. |
| VAL-006 | Run focused Playwright coverage that switches activities. |
| VAL-007 | Run focused Playwright coverage that toggles the bottom panel if implemented as a user action. |
| VAL-008 | Run focused accessibility checks for shell landmarks, keyboard focus, tabs, and command palette behavior. |
| VAL-009 | Confirm status bar slots render for active snapshot, API connectivity, background work, and selected context. |
| VAL-010 | Confirm no raw stack traces, connection strings, environment variables, raw Cypher, Neo4j internal identifiers, or driver-specific diagnostics appear in shell placeholders. |

### 10.2 Manual Smoke Checklist

The WP003 manual smoke validation should confirm:

- ArchonExplorer opens inside the existing Aspire-hosted development flow.
- The workbench frame renders with activity bar, sidebar, tabbed area, bottom panel, status bar, command palette affordance, and notification host.
- Activity selection changes sidebar context without leaving the workbench frame.
- The command palette opens with the documented keyboard shortcut and returns focus safely when closed.
- The bottom panel can be shown or hidden if that control is implemented.
- Layout preferences persist across reload and can recover to defaults if invalid.
- Placeholder areas are honest about future work package capabilities.

### 10.3 Full Suite Guidance

The implementation should not run the full test suite for this work package unless the active implementation request explicitly asks for it or focused validation reveals a cross-cutting problem that requires broader verification.

## 11. Acceptance Criteria

WP003 is complete when:

1. ArchonExplorer renders a persistent desktop-style workbench frame.
2. The frame includes activity bar, sidebar, tabbed work area, bottom panel, status bar, command palette host, and notification host.
3. Activity navigation updates shell state and contextual sidebar placeholders.
4. The tabbed work area has a default start tab and a state model suitable for future investigation tabs.
5. The bottom panel can host background work, extraction-run, and diagnostics placeholders without exposing unsafe diagnostics.
6. The status bar reserves slots for active snapshot, API connectivity, background work, and selected context.
7. The command palette opens by keyboard shortcut and contains shell-level placeholder commands.
8. Layout preferences for panel sizes and shell state persist locally and recover safely from invalid stored data.
9. WP003 uses shadcn/ui-compatible primitives and does not introduce another ordinary UI component library.
10. WP003 does not implement real extraction submission, snapshot deletion, search results, graph rendering, lens execution, or evidence inspection.
11. Focused Playwright coverage validates opening the shell and opening the command palette.
12. Basic accessibility validation covers shell landmarks, keyboard focus, tabs, and command palette behavior.
13. Frontend typecheck and production build pass.

## 12. Documentation and Wiki Impact

### 12.1 Required Documentation Updates

The WP003 implementation plan shall include a documentation pass. Contributor-facing documentation should explain:

- the ArchonExplorer workbench shell regions;
- how activities, tabs, panels, commands, status items, and notifications fit together;
- how shell state differs from TanStack Query server state;
- how later work packages should register or add activities and commands;
- how layout preferences are persisted and reset;
- which shell regions remain placeholders until later work packages.

### 12.2 Wiki Guidance

If existing wiki pages describe ArchonExplorer architecture, local development, UI shell patterns, or frontend state management, they should be updated during implementation. Contributor guidance should live in the wiki rather than in standalone implementation notes.

### 12.3 Documentation Standard

Any implementation work that introduces code documentation requirements shall treat internal and non-public types as requiring the same developer-level documentation quality as public types. Documentation expectations must not be scoped only to public API surfaces.

## 13. Risks and Decisions

### 13.1 Decisions

| Decision | Rationale |
| --- | --- |
| Establish the full shell before operational features | WP004 and WP005 need a durable frame for extraction and snapshot administration. |
| Use shadcn/ui `Command` / cmdk for command palette shell | Required by the UI brief and consistent with WP001/WP002 component-system choices. |
| Keep WP003 feature placeholders honest | Prevents the shell package from drifting into extraction, snapshot, search, graph, lens, or evidence scope. |
| Persist layout preferences locally | Supports desktop-style workbench expectations without requiring backend persistence. |
| Treat API connectivity as WP002-owned state | Avoids duplicating runtime and diagnostic behavior in shell code. |

### 13.2 Risks

| Risk | Mitigation |
| --- | --- |
| Shell implementation could become too visually bespoke. | Require standard toolkit colors, spacing, text sizing, and control styling; avoid marketing-page treatment. |
| Shell state could become tangled with future feature data. | Keep WP003 local state limited to activities, tabs, panels, commands, and shell preferences. |
| Placeholder commands could be mistaken for real search. | Label architecture search as future functionality and restrict WP003 commands to shell actions. |
| Layout persistence could break after future schema changes. | Use versioned or recoverable stored preferences and provide a reset path. |
| Bottom-panel diagnostics could expose unsafe details. | Use safe placeholder diagnostics and WP002 diagnostic shaping conventions. |
| Later work packages could need different registration mechanics. | Define simple, extensible activity and command models rather than hard-coding all shell behavior in one component. |

## 14. Traceability

| Source expectation | WP003 treatment |
| --- | --- |
| Desktop-style workbench shell | Required by FR-001 through FR-006. |
| Activity bar for major areas | Required by FR-007 through FR-013. |
| Primary sidebar | Required by FR-014 through FR-019. |
| Tabbed investigation/document area | Required by FR-020 through FR-028. |
| Dockable or resizable panels | Required as resizable panel layout by FR-029 through FR-034. |
| Bottom panel for extraction runs, diagnostics, and background work | Required by FR-035 through FR-040. |
| Status bar for snapshot, API, background work, and selection context | Required by FR-041 through FR-048. |
| Global command palette | Required by FR-049 through FR-057. |
| Notification and toast center | Required by FR-058 through FR-062. |
| Layout persistence | Required by FR-063 through FR-067. |
| Mandatory shadcn/ui and styling constraint | Required by NFR-001 through NFR-005. |
| Safe diagnostics | Required by FR-039, FR-062, and NFR-017 through NFR-020. |
| No extraction, snapshot deletion, search results, graph rendering, or lens execution | Enforced by FR-068 through FR-074. |

## 15. Open Questions for Implementation Planning

No blocking product questions remain for WP003 specification creation. Implementation planning may still decide exact component decomposition, command identifiers, activity identifiers, keyboard shortcut details, layout persistence keys, default panel proportions, and Playwright test file organization, provided those decisions satisfy this specification and repository instructions.
