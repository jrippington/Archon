# WP001 Specification - ArchonExplorer Foundation

## Document Control

| Field | Value |
| --- | --- |
| Work Package | WP001 - ArchonExplorer Foundation |
| Output Path | `docs/020-ArchonExplorer-Foundation/spec-wp001-archonexplorer-foundation.md` |
| Source Work Package | `docs/foundation/work-packages-ui.md` WP001 |
| Source Brief | `docs/foundation/archon_ui_brief.md` |
| Specification Basis | `spec-template_v1.1.md` requested; template file was not present in the workspace at creation time, so this document follows the repository specification structure and documented work-package rules. |
| Status | Draft |
| Audience | Product owner, architect, frontend implementer, Aspire implementer, test engineer |

## 1. Overview

### 1.1 Purpose

This specification defines WP001 for the ArchonExplorer UI work-package sequence. WP001 creates the first runnable ArchonExplorer application foundation: a React and TypeScript workbench shell, configured with shadcn/ui, hosted from the existing Archon Aspire composition, and prepared for later operational and investigation features.

The package establishes the visible application identity and the shell affordances required by the UI brief without implementing extraction submission, snapshot administration, search results, graph visualisation, lenses, evidence inspection, or other later feature behavior.

### 1.2 Background

ArchonExplorer is the human-facing user interface for Archon, a deterministic architecture intelligence platform backed by extracted architecture facts and Neo4j persistence. The UI brief requires ArchonExplorer to be a desktop-style investigation workbench in the browser, not a static documentation site, a thin admin console, or a generic graph browser.

WP001 exists to make that product direction concrete before feature-specific work begins. Later UI work packages will add API client runtime behavior, the complete workbench desktop shell, extraction operations, snapshot administration, search, evidence, and graph/lens experiences. WP001 must create a foundation that those packages can extend without replacing the application structure or component system.

### 1.3 High-Level Scope

WP001 covers:

- Creation of the ArchonExplorer frontend application under `src/ArchonExplorer`.
- Vite React and TypeScript application setup.
- npm package-management setup with a committed lockfile.
- shadcn/ui as the mandatory component system for normal application UI.
- Baseline styling and theme support compatible with shadcn/ui.
- TanStack Query provider setup for future server state usage.
- Aspire AppHost integration for local development hosting.
- Development-time API base URL configuration.
- A visible ArchonExplorer workbench foundation shell.
- Placeholder shell affordances aligned with the UI brief.
- Build/typecheck validation and manual Aspire-hosted smoke validation.

WP001 excludes functional data interactions and feature-complete workbench behavior. Placeholder affordances must clearly indicate that later work packages will implement the corresponding feature areas.

## 2. System Context

### 2.1 Product Context

ArchonExplorer is the browser-delivered workbench for architectural investigation over Archon data. It will eventually let users create extraction runs, monitor background work, administer snapshots, select active snapshot context, search architectural artefacts, open investigation tabs, inspect evidence, apply lenses, and view focused graph/table/path projections.

The UI sequence intentionally starts with operational and shell foundations before graph visualisation. WP001 creates the application foundation so that subsequent packages can add capabilities in a consistent frame.

### 2.2 Source References

WP001 shall align with these source materials:

- `docs/foundation/work-packages-ui.md` WP001 - ArchonExplorer Foundation.
- `docs/foundation/archon_ui_brief.md` binding UI product mandate for ArchonExplorer, React, TypeScript, shadcn/ui, and Aspire hosting.
- `docs/foundation/archon_ui_brief.md` section 6.5 for the workbench desktop shell model.
- `docs/foundation/archon_ui_brief.md` section 6.6 for snapshot-context shell visibility expectations.
- `docs/foundation/archon_ui_brief.md` section 14 for MVP UI scope.
- `docs/foundation/archon_ui_brief.md` section 14.1 for shadcn/ui primitive expectations.
- `docs/foundation/archon_ui_brief.md` section 14.2 for UI state ownership expectations.
- `docs/foundation/archon_ui_brief.md` section 14.3 for safe empty, loading, and error state expectations.
- `docs/foundation/work-packages-ui.md` section 1.3 for route-shape convention and no common `/api` prefix.
- Microsoft Learn documentation for Aspire JavaScript/Vite application hosting.

### 2.3 Users and Stakeholders

| Stakeholder | Interest |
| --- | --- |
| Product owner | Confirms the first UI slice visibly establishes ArchonExplorer and preserves the planned work-package sequence. |
| Architect | Confirms the frontend application is separately hosted, Aspire-integrated, and does not blur API or domain boundaries. |
| Frontend implementer | Uses the initial Vite/shadcn/ui foundation to implement later workbench slices. |
| Aspire implementer | Wires the frontend into the existing distributed application without replacing existing API or persistence resources. |
| Test engineer | Verifies the app builds, typechecks, launches through Aspire, and renders the required shell identity and placeholders. |
| Future ArchonExplorer user | Sees that a workbench shell exists and understands which areas are not yet available. |

## 3. Component Summary

### 3.1 ArchonExplorer Vite Application

ArchonExplorer is a Vite-powered React and TypeScript application located at `src/ArchonExplorer`. It owns the browser UI, static assets, frontend package manifest, npm lockfile, Vite configuration, TypeScript configuration, stylesheet entry points, and React application bootstrap.

The application must not be implemented as an ASP.NET Core-hosted SPA for WP001. Aspire should orchestrate the Vite development server as a JavaScript/Vite resource for local development.

### 3.2 Aspire AppHost Integration

The existing Archon Aspire AppHost is the local composition root. WP001 adds ArchonExplorer as an Aspire-hosted frontend resource and passes any required development-time API base URL configuration to the frontend through environment/configuration conventions suitable for Vite.

The AppHost remains orchestration-only. It must not contain frontend UI logic, API client logic, or workbench state behavior.

### 3.3 shadcn/ui Component Foundation

shadcn/ui is the mandatory component foundation for normal application components. WP001 must configure the styling, utility, and component conventions needed for future shell, command, tab, menu, badge, tooltip, popover, form, table, dialog, and notification patterns.

WP001 does not need to add every future shadcn/ui component, but it must establish the configuration and at least enough real components or primitives to prove the component system is usable.

### 3.4 Workbench Foundation Shell

The WP001 shell is a visible desktop-style workbench foundation. It should include placeholders for the major shell affordances required later: activity rail, primary work area, command/search affordance, status bar, theme control, API configuration indicator, and safe unavailable-feature states.

The shell must not pretend that extraction, search, snapshot management, graph rendering, lenses, or evidence inspection are complete.

### 3.5 Frontend Runtime Providers

WP001 establishes the application runtime provider structure used by later features. This includes React application bootstrap, theme preference handling, TanStack Query provider setup, and any minimal local state needed for shell readiness.

TanStack Query is included during WP001 to prevent later API and polling work from introducing a second server-state pattern.

### 3.6 Validation Surface

WP001 validation focuses on buildability and launchability. It includes package install consistency, frontend build/typecheck scripts, .NET solution or affected-project build validation for Aspire wiring, and manual Aspire-hosted smoke validation that loads ArchonExplorer.

Automated Playwright journeys are not required in WP001. They may be introduced by later UI work packages when functional journeys exist.

## 4. Functional Requirements

### 4.1 Application Creation

| ID | Requirement |
| --- | --- |
| FR-001 | The repository shall contain an ArchonExplorer frontend application at `src/ArchonExplorer`. |
| FR-002 | ArchonExplorer shall be implemented as a Vite React application using TypeScript. |
| FR-003 | ArchonExplorer shall use npm as its package manager. |
| FR-004 | ArchonExplorer shall include a committed npm lockfile to make dependency restoration deterministic. |
| FR-005 | ArchonExplorer shall expose npm scripts for development hosting, production build, and static type checking. |
| FR-006 | ArchonExplorer shall identify itself visibly as `ArchonExplorer` in the rendered UI. |
| FR-007 | ArchonExplorer shall not be implemented as a Next.js application in WP001. |
| FR-008 | ArchonExplorer shall not be implemented as an ASP.NET Core-hosted React application in WP001. |

### 4.2 React and TypeScript Foundation

| ID | Requirement |
| --- | --- |
| FR-009 | The application shall bootstrap through a conventional React root entry point. |
| FR-010 | The application shall use TypeScript for application code. |
| FR-011 | TypeScript settings shall support strict frontend development suitable for later API-client and workbench-state code. |
| FR-012 | The application shall avoid introducing non-TypeScript JavaScript source files for application logic unless generated tooling requires them. |
| FR-013 | The application shall keep source files organized so later work packages can add components, layout, runtime providers, utilities, and feature areas without flattening all code into the root source folder. |

### 4.3 shadcn/ui Foundation

| ID | Requirement |
| --- | --- |
| FR-014 | shadcn/ui shall be configured as the mandatory component system for normal application UI. |
| FR-015 | Styling shall include the baseline CSS variables, theme tokens, utility setup, and path aliases required by shadcn/ui. |
| FR-016 | WP001 shall not introduce another ordinary UI component library for shell, form, table, dialog, command, tab, menu, badge, tooltip, popover, or notification patterns. |
| FR-017 | Any initial button, card, badge, status, or layout primitives shall follow shadcn/ui-compatible conventions. |
| FR-018 | Custom CSS may be used for layout foundations where appropriate, but it shall not replace the shadcn/ui component-system decision. |

### 4.4 Workbench Shell Affordances

| ID | Requirement |
| --- | --- |
| FR-019 | The initial UI shall render a desktop-style workbench foundation rather than a single generic welcome page. |
| FR-020 | The shell shall include a top-level app frame. |
| FR-021 | The shell shall include an activity rail placeholder for major workbench areas. |
| FR-022 | The shell shall include a main workspace area with a clear empty-state or start-state panel. |
| FR-023 | The shell shall include a global command/search affordance placeholder. |
| FR-024 | The shell shall include a status bar placeholder. |
| FR-025 | The status bar shall show a placeholder for active snapshot context. |
| FR-026 | The status bar shall show API configuration or API connectivity placeholder state. |
| FR-027 | The shell shall include a theme preference affordance if the selected theme implementation supports it without adding unrelated scope. |
| FR-028 | The shell shall use safe not-yet-available messaging for future areas such as search, extraction center, snapshot admin, project explorer, lenses, and graph views. |
| FR-029 | The shell shall not implement a complete tabbed investigation/document area in WP001. |
| FR-030 | The shell shall not implement dockable or resizable inspector behavior in WP001. |
| FR-031 | The shell shall not implement a real notification center in WP001 unless required by the selected shadcn/ui setup. |

### 4.5 Future-Affordance Alignment

| ID | Requirement |
| --- | --- |
| FR-032 | Placeholder activity items shall align with later UI brief areas such as Dashboard, Extraction Center, Snapshot Admin, Search, Project Explorer, Findings, and Settings or Diagnostics. |
| FR-033 | Placeholder command/search affordance text shall make clear that functional search and command execution are delivered later. |
| FR-034 | Placeholder snapshot status shall use the `current` terminology consistently with the UI brief. |
| FR-035 | Placeholder API status shall distinguish API base URL configuration from successful functional API querying. |
| FR-036 | The initial shell shall not render graph visualisation placeholders in a way that implies graph rendering is complete. |
| FR-037 | The initial shell shall not expose arbitrary query consoles, raw Cypher entry points, or unsafe diagnostics. |

### 4.6 Aspire Hosting

| ID | Requirement |
| --- | --- |
| FR-038 | The existing Archon Aspire AppHost shall compose ArchonExplorer as a local development resource. |
| FR-039 | Aspire integration shall use the Aspire JavaScript/Vite hosting model appropriate for a Vite application. |
| FR-040 | The AppHost shall provide or surface the Archon API base URL to ArchonExplorer through Vite-compatible development-time configuration. |
| FR-041 | ArchonExplorer shall be reachable from the Aspire dashboard/resource list during local development. |
| FR-042 | ArchonExplorer hosting shall not require manual static-file serving outside the Aspire orchestration for local development. |
| FR-043 | The AppHost shall preserve existing Archon API, MCP, Neo4j, and service-default behavior while adding the UI resource. |
| FR-044 | AppHost changes shall remain orchestration-only and shall not introduce UI feature logic. |

### 4.7 API Configuration Placeholder

| ID | Requirement |
| --- | --- |
| FR-045 | The frontend shall read a development-time API base URL configuration value or expose a clear placeholder when the value is absent. |
| FR-046 | The UI shall indicate whether API configuration is present. |
| FR-047 | WP001 shall not require functional API calls beyond optional smoke or configuration checks. |
| FR-048 | Any route examples, constants, or labels introduced in WP001 shall preserve the repository convention of no common `/api` prefix. |
| FR-049 | The UI shall not display connection strings, environment variables, raw exception details, or other unsafe runtime diagnostics. |

### 4.8 Runtime Provider Foundation

| ID | Requirement |
| --- | --- |
| FR-050 | The React root shall include a TanStack Query `QueryClient` provider. |
| FR-051 | TanStack Query shall be present for future server state, API caching, polling, search results, snapshot lists, and lens results. |
| FR-052 | WP001 shall not implement a full typed Archon API client; that belongs to WP002. |
| FR-053 | WP001 shall not implement polling helpers; those belong to WP002 and later operational work packages. |
| FR-054 | Local UI state in WP001 shall be limited to shell readiness, theme preference, and placeholder affordance state. |

### 4.9 Out-of-Scope Functional Behavior

| ID | Requirement |
| --- | --- |
| FR-055 | WP001 shall not implement extraction form submission. |
| FR-056 | WP001 shall not implement extraction run history. |
| FR-057 | WP001 shall not implement snapshot listing, snapshot selection, snapshot deletion, or delete-all behavior. |
| FR-058 | WP001 shall not implement global search results. |
| FR-059 | WP001 shall not implement investigation tabs with persisted per-tab state. |
| FR-060 | WP001 shall not implement project, data, findings, or snapshot catalogues. |
| FR-061 | WP001 shall not implement graph projection rendering. |
| FR-062 | WP001 shall not implement dependency, data access, impact, path, rule violation, or evidence lenses. |
| FR-063 | WP001 shall not implement authentication or authorization. |

## 5. Non-Functional Requirements

### 5.1 Buildability and Developer Experience

| ID | Requirement |
| --- | --- |
| NFR-001 | A developer shall be able to restore frontend dependencies using npm from `src/ArchonExplorer`. |
| NFR-002 | A developer shall be able to build the frontend application using an npm script. |
| NFR-003 | A developer shall be able to typecheck the frontend application using an npm script. |
| NFR-004 | A developer shall be able to build the affected .NET solution or project set after Aspire integration. |
| NFR-005 | The application structure shall be understandable to contributors who primarily use Visual Studio and PowerShell. |
| NFR-006 | The package scripts and documentation shall avoid requiring WSL-only workflows. |

### 5.2 Maintainability

| ID | Requirement |
| --- | --- |
| NFR-007 | Frontend source organization shall support later feature folders or slices without requiring a structural rewrite. |
| NFR-008 | Runtime provider setup shall be centralized rather than duplicated in feature components. |
| NFR-009 | Workbench shell layout code shall be separable from future feature implementation code. |
| NFR-010 | The initial shell shall use clear names that map to the UI brief vocabulary. |

### 5.3 Safety and Diagnostics

| ID | Requirement |
| --- | --- |
| NFR-011 | User-visible diagnostics shall be safe and shall not expose raw stack traces. |
| NFR-012 | User-visible diagnostics shall not expose connection strings, environment variables, raw Cypher, Neo4j internal identifiers, or driver-specific details. |
| NFR-013 | Unavailable-feature states shall be explicit and non-deceptive. |
| NFR-014 | API unconfigured or unavailable states shall be represented as operational state, not as application failure. |

### 5.4 Accessibility and Usability Foundation

| ID | Requirement |
| --- | --- |
| NFR-015 | The initial shell shall use semantic HTML and accessible labels where interactive placeholders are present. |
| NFR-016 | Keyboard focus states shall remain visible for any interactive controls added in WP001. |
| NFR-017 | Color choices shall support readable contrast in the default theme. |
| NFR-018 | Theme support shall not make status or unavailable states depend on color alone. |

### 5.5 Performance

| ID | Requirement |
| --- | --- |
| NFR-019 | The initial shell shall avoid loading graph-rendering libraries in WP001. |
| NFR-020 | The initial shell shall avoid unnecessary runtime dependencies beyond the selected frontend foundation, shadcn/ui requirements, and TanStack Query. |
| NFR-021 | The production build shall complete without requiring unavailable API services. |

## 6. User Experience Requirements

### 6.1 Initial Workbench Frame

When a user opens ArchonExplorer after WP001, the UI shall communicate that the application is a workbench foundation. It should show a stable application frame, a left-side activity affordance area, a main work area with explanatory empty state, and a bottom status area.

The experience should avoid a marketing landing page tone. It should feel like the start of an architecture investigation desktop, even while the actual investigation capabilities are pending later packages.

### 6.2 Suggested Initial Activity Areas

The initial activity rail may include placeholder entries for:

- Dashboard.
- Extraction Center.
- Snapshots.
- Search.
- Projects.
- Findings.
- Settings or Diagnostics.

The labels may be refined during implementation, but they must remain aligned with later UI brief work areas. Placeholder entries should not navigate to functional pages unless those pages are implemented as honest empty states.

### 6.3 Suggested Status Bar Fields

The initial status bar should reserve space for:

- Active snapshot: `current` or no-snapshot placeholder.
- API configuration status.
- Background work status placeholder.
- Selection context placeholder.

Only API configuration presence is required to be meaningful in WP001. Other fields may be inactive placeholders if clearly marked.

### 6.4 Theme Support

WP001 should provide baseline light/dark theme capability compatible with shadcn/ui. If implementation complexity requires choosing one default in WP001, the specification preference is to establish the theme-token foundation first and keep the visible toggle simple.

### 6.5 Empty and Unavailable States

The shell shall use empty and unavailable states that guide future users and contributors. Messages should explain that extraction, snapshots, search, and investigations are delivered by later work packages rather than failing silently or displaying raw technical errors.

## 7. Technical Requirements

### 7.1 Frontend Stack

The selected frontend stack for WP001 is:

| Concern | Selection |
| --- | --- |
| Application toolchain | Vite |
| UI framework | React |
| Language | TypeScript |
| Package manager | npm |
| Component system | shadcn/ui |
| Styling foundation | shadcn/ui-compatible CSS variables and utility classes |
| Server state foundation | TanStack Query |
| Local development hosting | Aspire JavaScript/Vite hosting |

### 7.2 Project Location

The frontend application shall live at:

```text
src/ArchonExplorer
```

This location makes ArchonExplorer a production source asset under the repository's existing source structure while keeping it independent from .NET host projects.

### 7.3 Configuration

WP001 shall define how ArchonExplorer receives the Archon API base URL during local Aspire hosting. The exact environment variable name may be selected during implementation, but it should be Vite-compatible and documented. If a placeholder is used before functional API calls exist, it must still be clear enough for WP002 to extend.

### 7.4 Route Convention

Any client-side constants, labels, or examples introduced during WP001 must respect the Archon API convention of no common `/api` prefix. Functional route constants are expected in WP002; WP001 should avoid introducing speculative endpoint lists unless needed for configuration display.

### 7.5 Dependency Constraints

WP001 should avoid adding libraries for graph rendering, data grids, routing frameworks, drag/drop frameworks, state machines, or visualization. Those choices belong to later packages when specific feature needs exist.

## 8. Data and State Requirements

### 8.1 Server State

TanStack Query shall be configured but does not need to execute functional Archon API queries in WP001. Its presence establishes the server-state boundary for later API client, extraction polling, snapshot lists, search results, and lens results.

### 8.2 Local Workbench State

WP001 local state may include:

- selected or default theme;
- shell readiness;
- command/search placeholder open state, if implemented;
- selected placeholder activity item, if implemented;
- API base URL configured/unconfigured status.

WP001 local state shall not invent architecture facts, extraction runs, snapshots, search results, graph nodes, evidence, metrics, or findings.

### 8.3 Persisted Preferences

Persisting theme preference is acceptable in WP001 if implemented simply. Persisting tabs, saved searches, recent artefacts, panel sizes, or investigations is out of scope for WP001.

## 9. Integration Requirements

### 9.1 Aspire Resource Integration

The AppHost shall treat ArchonExplorer as an orchestrated resource. The integration should support local launch from the Aspire dashboard/resource list and should make the frontend's local development URL visible to the developer.

### 9.2 API Host Relationship

ArchonExplorer depends on Archon API configuration, but WP001 does not require successful functional API calls. The UI should make the configuration state visible so contributors can diagnose missing local setup before WP002 introduces typed API calls.

### 9.3 Existing Solution Preservation

Adding ArchonExplorer shall not remove, rename, or weaken existing Archon solution projects, service defaults, API host behavior, MCP host behavior, persistence resources, or test projects.

## 10. Validation Requirements

### 10.1 Required Validation

WP001 implementation shall be validated with:

| ID | Validation |
| --- | --- |
| VAL-001 | Restore frontend dependencies from `src/ArchonExplorer` using npm. |
| VAL-002 | Run the frontend typecheck script successfully. |
| VAL-003 | Run the frontend production build script successfully. |
| VAL-004 | Build the affected .NET solution or project set after Aspire AppHost changes. |
| VAL-005 | Manually launch the Aspire AppHost and confirm ArchonExplorer appears as a hosted resource. |
| VAL-006 | Manually open ArchonExplorer from the Aspire-hosted resource URL and confirm the shell renders. |
| VAL-007 | Confirm the rendered shell visibly identifies the app as `ArchonExplorer`. |
| VAL-008 | Confirm the shell displays API configuration status without unsafe diagnostics. |

### 10.2 Deferred Validation

Playwright end-to-end journeys are not mandatory in WP001. They remain expected for later UI work packages when the workbench has functional user journeys such as extraction submission, snapshot listing, global search, investigation tabs, and evidence inspection.

### 10.3 Manual Smoke Checklist

The WP001 manual smoke validation should confirm:

- Aspire starts successfully with existing backend resources and the new ArchonExplorer resource.
- The ArchonExplorer URL opens in a browser.
- The workbench frame renders without console-blocking fatal errors.
- The shell shows the activity rail, command/search affordance, main workspace placeholder, and status bar.
- The shell does not expose raw stack traces, environment variables, connection strings, or raw backend diagnostics.

## 11. Acceptance Criteria

WP001 is complete when:

1. `src/ArchonExplorer` exists as a Vite React TypeScript application.
2. npm is used for frontend package management and a lockfile is present.
3. shadcn/ui is configured and demonstrably usable.
4. TanStack Query is configured at the application provider level.
5. The existing Aspire AppHost hosts ArchonExplorer for local development.
6. ArchonExplorer can receive or display development-time Archon API base URL configuration state.
7. The rendered UI visibly identifies itself as `ArchonExplorer`.
8. The rendered UI presents a desktop-style workbench foundation with placeholder activity, command/search, workspace, status, theme, and API status affordances.
9. Placeholder areas clearly indicate that extraction, snapshots, search, graph views, lenses, and evidence are later work-package capabilities.
10. No alternative ordinary UI component library is introduced.
11. No graph rendering, extraction, snapshot management, search results, lens, or evidence feature is implemented.
12. Required frontend and affected .NET build validations pass.
13. Manual Aspire-hosted smoke validation confirms the shell is reachable.

## 12. Documentation and Wiki Impact

### 12.1 Required Documentation Updates

The WP001 implementation plan shall include a documentation pass. At minimum, contributor-facing documentation should explain:

- where ArchonExplorer lives;
- how to restore frontend dependencies;
- how to build/typecheck the frontend;
- how to launch ArchonExplorer through Aspire;
- how the API base URL is configured during development;
- which UI features are placeholders until later work packages.

### 12.2 Wiki Guidance

If existing wiki pages describe solution structure, local development, Aspire startup, or UI architecture, they should be updated during implementation. Contributor guidance should live in the wiki rather than in standalone implementation notes.

### 12.3 Documentation Standard

Any implementation work that introduces code documentation requirements shall treat internal and non-public types as requiring the same developer-level documentation quality as public types. Documentation expectations must not be scoped only to public API surfaces.

## 13. Risks and Decisions

### 13.1 Decisions

| Decision | Rationale |
| --- | --- |
| Use Vite React TypeScript | Best fit for Aspire JavaScript/Vite hosting and the browser workbench model. |
| Use npm | Lowest setup burden for Visual Studio and Node.js contributors. |
| Place app under `src/ArchonExplorer` | Keeps the UI in the production source tree while preserving independence from .NET hosts. |
| Include TanStack Query in WP001 | Establishes server-state runtime foundation before API client and polling packages. |
| Defer Playwright | WP001 has shell smoke validation only; journey tests become more valuable once functional workflows exist. |

### 13.2 Risks

| Risk | Mitigation |
| --- | --- |
| The shell could become too feature-rich and drift into later work packages. | Keep WP001 placeholders honest and prohibit functional extraction, snapshot, search, lens, graph, and evidence behavior. |
| Aspire Vite hosting may require package/version alignment. | Use current Aspire JavaScript/Vite hosting guidance and validate through affected .NET build plus manual Aspire launch. |
| shadcn/ui setup may be incomplete if only styling is added. | Require at least enough configured component usage to demonstrate the component system is usable. |
| API status could expose unsafe diagnostics. | Limit UI output to safe configured/unconfigured or unavailable states. |
| Later WP003 shell work may need richer layout primitives. | WP001 reserves shell affordances but does not lock in final panel/tab/resizable implementation details. |

## 14. Traceability

| Source expectation | WP001 treatment |
| --- | --- |
| ArchonExplorer is React and TypeScript | Required by FR-001 through FR-013. |
| shadcn/ui is mandatory | Required by FR-014 through FR-018. |
| Hosted by Archon Aspire composition | Required by FR-038 through FR-044. |
| Desktop-style workbench shell | Established as foundation by FR-019 through FR-031. |
| Global command/search affordance | Placeholder required by FR-023 and FR-033. |
| Active snapshot visibility | Placeholder required by FR-025 and FR-034. |
| API connectivity/configuration visibility | Required by FR-026 and FR-045 through FR-047. |
| Safe diagnostics | Required by FR-037, FR-049, and NFR-011 through NFR-014. |
| TanStack Query state architecture | Required by FR-050 through FR-053. |
| No graph visualisation before operational foundation | Enforced by FR-036 and FR-061. |

## 15. Open Questions for Implementation Planning

No blocking product questions remain for WP001 specification creation. Implementation planning may still decide exact package versions, shadcn/ui component subset, theme toggle mechanics, Vite environment variable name, and Aspire resource naming, provided those decisions satisfy this specification and repository instructions.
