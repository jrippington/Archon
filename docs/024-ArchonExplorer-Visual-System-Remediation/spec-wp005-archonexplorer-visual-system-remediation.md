# WP005 Specification - ArchonExplorer Visual System Remediation

## Document Control

| Field | Value |
| --- | --- |
| Work Package | WP005 - ArchonExplorer Visual System Remediation |
| Output Path | `docs/024-ArchonExplorer-Visual-System-Remediation/spec-wp005-archonexplorer-visual-system-remediation.md` |
| Related Work Packages | `docs/020-ArchonExplorer-Foundation/`, `docs/021-API-Client-and-Runtime-Foundation/`, `docs/022-Workbench-Desktop-Shell/`, `docs/023-Extraction-Center/` |
| Specification Basis | Current ArchonExplorer UI implementation feedback, Visual Studio/Rider/CodeSee reference direction, repository work-package specification pattern. `spec-template_v1.1.md` was requested by the prompt but is not present in the workspace. |
| Status | Draft |
| Audience | Product owner, frontend implementer, architect, test engineer |

## 1. Overview

### 1.1 Purpose

This specification defines a broad visual-system remediation work package for ArchonExplorer. The goal is to correct the current browser-page-like user interface and establish a compact professional desktop workbench experience suitable for power users. The target experience should feel closer to Visual Studio and JetBrains Rider, with selective inspiration from CodeSee for future architecture workspace direction.

This work package is remediation of the existing ArchonExplorer visual and interaction model. It is not a new backend capability work package, and it must not expand extraction server behavior beyond what is already available through ArchonApi.

### 1.2 Background

ArchonExplorer currently has operational functionality for starting and monitoring extraction runs, but the UI presentation does not yet meet the desired product standard. The current experience still feels like a web page: browser-level scrolling appears, individual sections stack vertically, large headers and panels consume space, text density is too low, explanatory copy dominates the workspace, and icons or badges can appear decorative rather than useful.

The target product is a professional business application for technical power users. It should behave like a desktop IDE-style workbench even though it is browser-delivered through Aspire and Vite. Users should see compact tools, docked panes, grids, trees, property-style detail areas, terse status feedback, and focused workflows rather than card-heavy web sections.

### 1.3 High-Level Scope

This work package covers broad ArchonExplorer visual-system remediation, including the global workbench shell, activity navigation, sizing and density conventions, page-scroll elimination, layout primitives, panel usage, typography scale, text policy, icon and badge policy, snapshot workspace direction, and the existing extraction workflow’s placement inside that remediated model.

The first visible outcome should be a cohesive ArchonExplorer workbench where the Snapshot workspace becomes the main operational context, the new extraction workflow appears as a compact pane, extraction progress appears as a small focused snapshot update experience, and history/details use dense workbench components rather than page sections.

### 1.4 Out of Scope

This work package does not include graph visualization implementation, CodeSee-style graph canvas implementation, new ArchonApi endpoints, new extraction pipeline behavior, snapshot deletion, authentication, authorization, custom branding, custom color palettes, or a global output/log console.

## 2. System Context

### 2.1 Product Context

ArchonExplorer is the browser-delivered UI for architecture extraction, snapshot management, and future architectural investigation. Its long-term value depends on feeling like a serious technical workbench rather than a marketing site, dashboard, or generic admin page.

The visual system must support future dense workflows such as snapshot review, graph exploration, architecture search, rule findings, dependency maps, and technical evidence inspection. The remediation work must therefore establish conventions that can scale beyond Extraction Center.

### 2.2 Reference Applications

The target direction should use these references:

| Reference | Relevant Qualities |
| --- | --- |
| Visual Studio | Desktop workbench shell, compact tool windows, command bars, tabbed documents, properties/details areas, status surfaces. |
| JetBrains Rider | Compact density, professional technical UI, efficient tool window behavior, dense tables and navigation. |
| CodeSee | Architecture workspace direction, technical map/canvas readiness, left navigation/context with central architecture workspace. |

ArchonExplorer should not copy these applications directly. The references define the expected seriousness, density, and workbench behavior.

### 2.3 Theme Constraint

ArchonExplorer must continue to use the standard shadcn/ui theme. The remediation must not introduce bespoke product colors, marketing colors, custom palette overrides, or custom brand color systems. Visual hierarchy should come from layout, density, typography scale, borders, muted foreground/background tokens, and existing shadcn/ui design tokens.

## 3. Component Summary

### 3.1 Global Workbench Shell

The global shell is the persistent desktop-style frame for ArchonExplorer. It should fill the browser viewport, prevent browser document scrolling, and provide compact navigation, tabs, command surfaces, and workspace regions.

### 3.2 Activity Rail

The activity rail provides compact access to primary workspaces. It should be small, stable, and tool-window-like. Labels should not create a large navigation layout. Icons may be used only when meaningful and paired with accessible names and tooltips.

### 3.3 Snapshot Workspace

The Snapshot workspace is the primary landing and operational context for extraction and snapshot-related work. It should present the current snapshot context, extraction/update status, history, and future architecture investigation surfaces without becoming a web dashboard.

### 3.4 New Extraction Pane

The New Extraction pane is a compact docked pane used to submit extraction requests. It replaces the current large page-section feel of the extraction form. It should be visible when needed, pinnable or collapsible if supported by the shell, and optimized for fast repeated use.

### 3.5 Run History Grid

Run history should be represented as a dense grid or workbench list. It should avoid card layouts and avoid large badge-heavy rows. The grid should support fast scanning by power users.

### 3.6 Selected Run Details

Selected run details should use compact property/details presentation. They should show operational facts from the API without large prose blocks. Details should appear in a docked or split region rather than as stacked page sections.

### 3.7 Snapshot Update Status

Extraction progress should appear as a small focused update/status experience in the Snapshot workspace. It should use API feedback to show what is happening, but it must not become a global output pane, log console, or build-output clone.

### 3.8 Help and Guidance

Help should move out of primary visible layout. Most explanatory text should become tooltips, concise inline hints, status text, or documentation links. The default workspace should prioritize operational controls and data.

## 4. Functional Requirements

### 4.1 Global Workbench Behavior

| ID | Requirement |
| --- | --- |
| FR-001 | ArchonExplorer shall render as a fixed viewport workbench rather than a browser document page. |
| FR-002 | The browser body/document shall not be the primary scrolling surface during normal workbench use. |
| FR-003 | Scrolling shall be contained inside specific regions such as grids, trees, details panes, or forms. |
| FR-004 | The global shell shall keep activity navigation, workspace tabs, and the active workspace visible without requiring page scrolling. |
| FR-005 | The shell shall prefer split panes, docked panes, grids, trees, toolbars, and property views over stacked page sections. |
| FR-006 | The shell shall avoid card-heavy layouts for primary work areas. |
| FR-007 | The shell shall avoid marketing-style hero headers, oversized page titles, and decorative empty-state blocks. |
| FR-008 | The shell shall use compact workbench surfaces consistent with Visual Studio and Rider. |
| FR-009 | The shell shall remain browser-delivered and compatible with the existing Aspire/Vite hosting model. |

### 4.2 Density and Sizing

| ID | Requirement |
| --- | --- |
| FR-010 | The visual system shall choose compact sizing over spacious web-page sizing. |
| FR-011 | The target density shall be closer to Rider compact than to a default marketing or dashboard layout. |
| FR-012 | The first implementation pass shall use shadcn/ui default component sizing where practical rather than prematurely creating custom smaller variants. |
| FR-013 | Later implementation passes may go smaller than shadcn/ui defaults when a specific workbench surface still feels too spacious. |
| FR-014 | Major workbench bars should generally target a compact IDE-like visual height while preserving shadcn/ui default interaction behavior in the first pass. |
| FR-015 | Activity rail width should remain compact unless accessibility or content requires otherwise. |
| FR-016 | Dense table/list rows should prioritize shadcn/ui default table/list behavior in the first pass and may be tightened later if still too spacious. |
| FR-017 | Form controls should use shadcn/ui defaults in the first pass, arranged in compact workbench layouts rather than large page sections. |
| FR-018 | Section gaps and padding shall be reduced compared with the current page-like layout. |
| FR-019 | Large vertical whitespace shall be treated as a defect unless it serves a clear workbench purpose. |

### 4.3 Theme and Styling Constraints

| ID | Requirement |
| --- | --- |
| FR-020 | The implementation shall use the standard shadcn/ui theme. |
| FR-021 | The implementation shall not introduce custom brand color palettes. |
| FR-022 | The implementation shall not alter the shadcn/ui color system to create bespoke product colors. |
| FR-023 | Visual hierarchy shall use standard theme tokens, borders, spacing, typography, and component state. |
| FR-024 | Custom CSS shall be limited to layout, density, sizing, and workbench behavior when shadcn/ui primitives do not directly provide the required structure. |
| FR-025 | The remediation shall not use color as the only signal for status, selection, validation, or progress. |

### 4.4 Activity Rail and Navigation

| ID | Requirement |
| --- | --- |
| FR-026 | The activity rail shall be compact and IDE-like. |
| FR-027 | Activity items shall not create a large sidebar navigation experience by default. |
| FR-028 | Activity items may use icons only when the icon communicates a meaningful workspace concept. |
| FR-029 | Decorative or random icons shall be removed. |
| FR-030 | Icon-only activity items shall provide accessible names and tooltips. |
| FR-031 | The activity rail shall support fast switching between primary workspaces without changing the browser page model. |
| FR-032 | The selected activity state shall be clear using standard shadcn/ui theme affordances. |

### 4.5 Snapshot Workspace

| ID | Requirement |
| --- | --- |
| FR-033 | The Snapshot workspace should become the default landing workspace for ArchonExplorer. |
| FR-034 | The Snapshot workspace shall be the primary context for current snapshot state and snapshot-producing extraction activity. |
| FR-035 | The Snapshot workspace shall avoid a dashboard-card layout. |
| FR-036 | The Snapshot workspace shall prepare for future architecture maps, grids, and details without implementing graph visualization in this work package. |
| FR-037 | The Snapshot workspace shall present important snapshot/extraction state in compact workbench regions. |
| FR-038 | The Snapshot workspace shall not require browser-level scrolling for its default layout. |
| FR-039 | The Snapshot workspace shall provide clear empty/current/unavailable states using terse operational language. |

### 4.6 New Extraction Pane

| ID | Requirement |
| --- | --- |
| FR-040 | The existing extraction start form shall be remediated into a compact New Extraction pane. |
| FR-041 | The New Extraction pane shall behave like a docked workbench pane rather than a web page section. |
| FR-042 | The New Extraction pane should be available from the Snapshot workspace. |
| FR-043 | The New Extraction pane may be collapsible or pinnable if the shell supports that behavior. |
| FR-044 | The default New Extraction pane state should keep the most important request fields visible without requiring page scrolling. |
| FR-045 | The pane shall prioritize repository root, explicit solution paths, and submit action. |
| FR-046 | Optional fields such as branch, commit SHA, requested-by, and metadata may be grouped compactly. |
| FR-047 | Repeated solution path entry shall use compact rows. |
| FR-048 | The submit action shall remain clearly available but shall not use oversized promotional styling. |
| FR-049 | API-unconfigured or API-unavailable state shall be shown with concise operational feedback. |

### 4.7 Extraction and Snapshot Relationship

| ID | Requirement |
| --- | --- |
| FR-050 | The existing Extraction Center concept shall be reframed as part of the Snapshot workflow rather than a standalone page-like destination. |
| FR-051 | Existing extraction API behavior shall remain unchanged. |
| FR-052 | The UI shall continue to submit extraction requests through the typed ArchonApi client. |
| FR-053 | The UI shall continue to use `POST /extractions`, `GET /extractions`, and `GET /extractions/{runId}` without inventing a common `/api` prefix. |
| FR-054 | The remediation shall not add recursive solution discovery, filesystem browsing, graph querying, or new snapshot administration behavior. |
| FR-055 | A completed extraction that produces a snapshot identity shall update the Snapshot workspace state in a compact, visible way. |

### 4.8 Snapshot Update Status

| ID | Requirement |
| --- | --- |
| FR-056 | Extraction progress shall appear as a small focused update/status experience in the Snapshot workspace. |
| FR-057 | The update/status experience shall use API feedback from accepted run status and polling. |
| FR-058 | The update/status experience shall show queued, running, completed, failed, cancelled, unavailable, and unknown states when applicable. |
| FR-059 | The update/status experience shall show the current API progress stage when available. |
| FR-060 | The update/status experience shall show the current API progress message when available. |
| FR-061 | The update/status experience shall show warning and error counts when available. |
| FR-062 | The update/status experience shall show produced snapshot identity when available. |
| FR-063 | The update/status experience shall provide a clear working indication for active extraction without becoming visually dominant. |
| FR-064 | The update/status experience shall not be implemented as a global output pane. |
| FR-065 | The update/status experience shall not be implemented as a log console or build-output clone. |
| FR-066 | The update/status experience shall avoid verbose event streams unless a later work package explicitly adds diagnostic log viewing. |

### 4.9 Run History and Details

| ID | Requirement |
| --- | --- |
| FR-067 | Recent extraction runs shall be displayed in a dense grid, table, or workbench list. |
| FR-068 | Run history shall not be displayed as large cards. |
| FR-069 | Run status shall be represented using terse text and compact state treatment rather than oversized badges. |
| FR-070 | Badges shall be used only when they communicate actionable state and remain visually compact. |
| FR-071 | History rows shall support fast scanning of run ID, status, repository, solution count, started/completed time, warning count, error count, and snapshot identity where available. |
| FR-072 | Selecting a run shall show details in a compact docked or split details region. |
| FR-073 | Run details shall use property-grid, definition-list, compact table, or similarly dense presentation. |
| FR-074 | Run details shall avoid long explanatory paragraphs. |
| FR-075 | Missing or unavailable detail state shall use concise operational messages. |

### 4.10 Text, Help, and Tooltips

| ID | Requirement |
| --- | --- |
| FR-076 | Visible UI text shall be reduced to operational labels, short status messages, compact validation messages, and concise action text. |
| FR-077 | The implementation shall be strict about not spraying prose throughout the UI. |
| FR-078 | Long explanations shall move to tooltips, documentation links, or help affordances. |
| FR-079 | Tooltips shall be used for field explanation, route behavior, explicit solution path meaning, and status interpretation where needed. |
| FR-080 | Tooltips shall not be required to complete the primary workflow. |
| FR-081 | Empty states shall be short and task-oriented. |
| FR-082 | Repeated safety caveats shall be removed from primary layout once the user has enough context to proceed. |
| FR-083 | Status text shall be written for technical users and avoid marketing language. |

### 4.11 Icons and Badges

| ID | Requirement |
| --- | --- |
| FR-084 | Decorative icons shall be removed from primary workbench content. |
| FR-085 | Icons shall be retained only when they improve recognition of a command, workspace, or state. |
| FR-086 | Every icon-only action shall have an accessible name. |
| FR-087 | Every icon-only action should have a tooltip. |
| FR-088 | Random or non-obvious icons shall be replaced by text labels or removed. |
| FR-089 | Badges shall not be used as decorative labels. |
| FR-090 | Status badges, if retained, shall be compact and semantically meaningful. |

### 4.12 Panel Reduction

| ID | Requirement |
| --- | --- |
| FR-091 | Existing panel-heavy layouts shall be reviewed and flattened where possible. |
| FR-092 | Primary workspace areas shall avoid nested panels inside panels. |
| FR-093 | Borders and separators shall be used sparingly to define workbench regions. |
| FR-094 | Cards shall not be used as the default container for every section. |
| FR-095 | The remediation shall prefer one coherent split-pane layout over multiple stacked panels. |
| FR-096 | Headers inside panes shall be thin and functional. |

### 4.13 Accessibility

| ID | Requirement |
| --- | --- |
| FR-097 | Compact density shall not remove keyboard accessibility. |
| FR-098 | All interactive controls shall remain reachable by keyboard. |
| FR-099 | Focus indicators shall remain visible using standard shadcn/ui theme behavior. |
| FR-100 | Tooltips shall supplement but not replace accessible names or form labels. |
| FR-101 | Tables and lists shall retain usable accessible names and row/selection semantics. |
| FR-102 | Status updates driven by API progress shall be announced or exposed in an accessible way where appropriate. |
| FR-103 | Color shall not be the only indicator of active, selected, failed, warning, or completed state. |

## 5. Non-Functional Requirements

### 5.1 Usability

| ID | Requirement |
| --- | --- |
| NFR-001 | The UI shall optimize for power-user efficiency over first-run marketing explanation. |
| NFR-002 | A returning user should be able to start a new extraction without scrolling a page. |
| NFR-003 | A returning user should be able to scan recent runs quickly. |
| NFR-004 | A returning user should be able to understand current extraction progress from the Snapshot workspace at a glance. |
| NFR-005 | The interface shall feel like a professional business application rather than a consumer website. |

### 5.2 Maintainability

| ID | Requirement |
| --- | --- |
| NFR-006 | Visual remediation shall establish reusable layout conventions for future ArchonExplorer work packages. |
| NFR-007 | Shared workbench layout primitives should be preferred over one-off CSS for each feature. |
| NFR-008 | The implementation shall avoid duplicating server state into local stores except for UI selection, pane visibility, acknowledgement, and workflow state. |
| NFR-009 | Existing typed API client and TanStack Query conventions shall remain the source of server state. |
| NFR-010 | Documentation-pass expectations shall apply to internal and non-public types, functions, hooks, reducers, callbacks, and helpers as well as public APIs when code is later implemented. |

### 5.3 Performance

| ID | Requirement |
| --- | --- |
| NFR-011 | Compact layout changes shall not introduce expensive render loops. |
| NFR-012 | Run history grids should be structured so future virtualization can be added if run counts grow. |
| NFR-013 | API polling shall remain bounded and shall not become tighter because progress is shown in the Snapshot workspace. |
| NFR-014 | Layout containers shall avoid unnecessary nested scroll regions that make rendering and input behavior unpredictable. |

### 5.4 Compatibility

| ID | Requirement |
| --- | --- |
| NFR-015 | The remediation shall preserve the existing React, TypeScript, Vite, TanStack Query, shadcn/ui-compatible, and Aspire-hosted frontend stack. |
| NFR-016 | The remediation shall not require replacing shadcn/ui primitives. |
| NFR-017 | The remediation shall remain compatible with Chromium-based browser execution used by Playwright tests. |
| NFR-018 | The remediation shall not require native desktop wrappers. |

## 6. UX Standards

### 6.1 Required Layout Model

The preferred first-pass layout model is a left-pane plus main-workspace structure:

```text
┌──────────────────────────────────────────────────────────────┐
│ compact workbench title/command/tab area                     │
├──────┬───────────────┬───────────────────────────────────────┤
│ rail │ left pane     │ main Snapshot workspace               │
│      │ New Extraction│                                       │
│      │ compact tools │ run history / selected details /      │
│      │               │ compact snapshot update status        │
└──────┴───────────────┴───────────────────────────────────────┘
```

The browser document must not scroll to reveal the rest of the workflow. Any scrolling must be inside the left pane, history grid, or selected details region.

### 6.2 Visual Density Guidance

Implementation should use shadcn/ui default component sizing for the first pass where practical, then evaluate whether specific workbench regions still need smaller sizing. The first remediation pass should focus on removing page-like structure, excessive panels, oversized headers, and loose spacing before introducing custom compact variants.

Implementation should still bias toward:

- small controls over large controls;
- compact headers over page headers;
- single-line labels over paragraph descriptions;
- grids over card lists;
- panes over stacked sections;
- terse statuses over badges with long copy;
- tooltips over explanatory blocks.

### 6.3 Standard shadcn/ui Theme Use

The UI must use standard shadcn/ui theme tokens and component behavior. The remediation may adjust sizing and layout, but it must not create a custom visual identity through new colors. If a design need cannot be solved without new colors, the implementation should reconsider the layout or component hierarchy first.

### 6.4 Prohibited UX Patterns

The remediated UI shall not use:

- browser-page vertical workflow as the primary layout;
- large marketing-style hero headers;
- card stacks for every section;
- decorative icon rows;
- oversized badges for ordinary state;
- global output/log pane for extraction progress;
- long explanatory paragraphs in primary work areas;
- custom color palette overrides;
- page scroll as a normal way to reach core controls.

## 7. Data and API Interaction

### 7.1 Existing API Contracts

This work package consumes existing extraction API feedback. The relevant routes remain:

```http
POST /extractions
GET  /extractions
GET  /extractions/{runId}
```

The remediation must not invent route prefixes, new endpoints, or new server-side extraction behavior.

### 7.2 API Feedback in Snapshot Update Status

The compact Snapshot update status should use the same accepted run and polling feedback that the existing Extraction Center already receives. The status should be derived from server state managed through TanStack Query and the typed ArchonApi client.

### 7.3 Safe Diagnostics

API feedback shown in the remediated UI must remain safe. The UI must not display raw stack traces, connection strings, environment variables, raw Cypher, Neo4j internal identifiers, driver details, access tokens, or arbitrary backend exception text.

## 8. Acceptance Criteria

| ID | Acceptance Criterion |
| --- | --- |
| AC-001 | ArchonExplorer no longer presents the primary workflow as a vertically scrolling browser page. |
| AC-002 | The app root fills the viewport and contains scrolling to specific workbench regions. |
| AC-003 | The header/title/command area is visibly thinner and more IDE-like than the current implementation. |
| AC-004 | The activity rail is compact and does not read as a large web sidebar. |
| AC-005 | The Snapshot workspace is the default landing context or is otherwise clearly promoted as the primary operational workspace. |
| AC-006 | New extraction appears as a compact pane rather than a large stacked page form. |
| AC-007 | Run history appears as a dense grid/table/list rather than cards. |
| AC-008 | Selected run detail appears as compact properties/details rather than large prose panels. |
| AC-009 | Extraction progress appears as a small focused Snapshot update UI using API feedback. |
| AC-010 | No global output/log pane is introduced for extraction progress. |
| AC-011 | Long explanatory text is removed from primary workspace layout and moved to tooltips/help/docs where still needed. |
| AC-012 | The remediated UI is strict about not spraying prose throughout normal workbench surfaces. |
| AC-013 | Decorative/random icons are removed or replaced with meaningful actions. |
| AC-014 | Badges are removed or compacted unless they communicate meaningful state. |
| AC-015 | The implementation uses standard shadcn/ui theme behavior and does not add custom theme colors. |
| AC-016 | The first pass uses shadcn/ui default component sizing where practical and achieves compactness primarily through layout remediation. |
| AC-017 | The UI feels materially closer to Visual Studio/Rider compact workbench density. |
| AC-018 | Existing extraction submission, history, and polling behavior still works through the typed API client. |
| AC-019 | Existing safe diagnostic boundaries are preserved. |
| AC-020 | Focused automated tests are updated to cover the remediated layout and extraction workflow at a behavior level. |

## 9. Testing and Validation Requirements

### 9.1 Automated Validation

Implementation of this specification should include focused frontend validation that proves:

- the app shell does not rely on browser document scrolling for core workbench areas;
- the Snapshot workspace renders in the workbench shell;
- the New Extraction pane can submit a valid mocked extraction request;
- accepted extraction status appears in the compact Snapshot update UI;
- run history renders as a dense grid/list surface;
- selected run details render without unsafe diagnostics;
- tooltips or accessible labels exist for icon-only controls;
- no `/api/extractions` route prefix is introduced;
- type checking and production build still pass.

Existing tests that assert the old page-like structure must be updated as part of implementation. The remediation is expected to change layout structure, terminology, and interaction placement; tests should move with the product decision and assert the new workbench behavior rather than preserving obsolete DOM shape.

### 9.2 Suggested Validation Commands

The implementation work package should continue to use the existing ArchonExplorer validation style:

```powershell
cd .\src\ArchonExplorer
npm run typecheck
npm run test
npm run build
npm run test:e2e -- src/test-e2e/extraction-center.spec.ts
npm run test:e2e -- src/test-e2e/workbench-shell.spec.ts
```

Specific test names and files may change during implementation if the existing test structure is refactored to match the Snapshot workspace terminology.

### 9.3 Manual Review Checklist

A manual review should confirm:

- the interface no longer looks like a web page;
- browser-level scrollbars are not visible during normal use;
- header, rail, form controls, table rows, and pane spacing are compact;
- primary workflows are visible without scrolling down through sections;
- text is terse and operational;
- explanatory material is available through tooltips or docs rather than always visible;
- standard shadcn/ui theme is preserved;
- extraction progress is visible but not noisy;
- the result is closer to Visual Studio/Rider than to a dashboard or marketing UI.

## 10. Implementation Guidance

### 10.1 Remediation Order

A recommended implementation order is:

1. Fix global viewport and shell scrolling behavior.
2. Reduce global typography, header, rail, tab, and control density.
3. Establish reusable split-pane/workbench layout primitives.
4. Create or promote the Snapshot workspace as the default operational context.
5. Move extraction submission into a compact New Extraction pane.
6. Convert run history to a dense grid/list surface.
7. Convert selected run details to compact properties/details.
8. Add the compact Snapshot update status using existing API feedback.
9. Remove or relocate verbose text, decorative icons, badges, and card-heavy containers.
10. Update tests and documentation for the remediated workbench model.

### 10.2 Documentation Expectations

If implementation changes source code, documentation-pass requirements apply to all new or modified classes, components, hooks, reducers, helper functions, callbacks, and non-public/internal types. Documentation expectations must not be scoped only to public API surface.

Contributor-facing behavioral changes should be reflected in the wiki where relevant, especially if terminology shifts from Extraction Center as a standalone destination to Snapshot workspace plus New Extraction pane.

### 10.3 Backward Compatibility

The remediation should preserve existing extraction behavior and API semantics. If names or navigation labels change, compatibility should be maintained for tests and commands where practical, or migrated deliberately with focused test updates.

## 11. Risks and Open Decisions

| ID | Risk or Decision | Notes |
| --- | --- | --- |
| R-001 | Over-compaction could harm accessibility. | Validate keyboard access, focus visibility, labels, and readable text. |
| R-002 | Snapshot terminology may overlap with existing Extraction Center terminology. | Implementation should deliberately choose navigation labels and update docs/tests consistently. |
| R-003 | Prose could creep back into the UI during remediation. | Be strict: do not spray prose across workbench surfaces. Keep visible text terse and operational; use tooltips or docs for explanation. |
| R-004 | shadcn/ui defaults may be larger than the eventual ideal density. | Use shadcn/ui defaults for the first pass, fix layout and page structure first, and only go smaller in later passes where defaults still feel too spacious. |
| R-005 | Existing tests may assert old page structure. | Tests must be updated to assert the new workbench behavior and layout intent rather than preserving obsolete DOM structure. |

## 12. Glossary

| Term | Meaning |
| --- | --- |
| Workbench | The fixed desktop-style application shell containing navigation, tabs, panes, and active workspace. |
| Snapshot workspace | The primary ArchonExplorer workspace for current snapshot context, extraction update state, and future architecture investigation surfaces. |
| New Extraction pane | Compact docked pane for submitting extraction requests. |
| Snapshot update status | Small focused UI region showing extraction/snapshot-producing activity using API feedback. |
| Dense grid | Compact table or list optimized for scanning many records. |
| Standard shadcn/ui theme | The existing shadcn/ui theme/token system without custom product color alterations. |
| Browser document scrolling | Scrolling the entire web page/body rather than scrolling a specific internal workbench region. |
