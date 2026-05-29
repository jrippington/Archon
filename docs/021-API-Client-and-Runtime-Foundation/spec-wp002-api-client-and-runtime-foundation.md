# WP002 Specification - API Client and Runtime Foundation

## Document Control

| Field | Value |
| --- | --- |
| Work Package | WP002 - API Client and Runtime Foundation |
| Output Path | `docs/021-API-Client-and-Runtime-Foundation/spec-wp002-api-client-and-runtime-foundation.md` |
| Source Work Package | `docs/foundation/work-packages-ui.md` WP002 |
| Source Brief | `docs/foundation/archon_ui_brief.md` |
| Specification Basis | Same repository work-package specification pattern as `docs/020-ArchonExplorer-Foundation/spec-wp001-archonexplorer-foundation.md`. |
| API Route Basis | Existing ArchonApi endpoint mappings inspected in `src/ArchonApi`, `src/Archon.Api.Extraction`, `src/Archon.Api.Management`, and `src/Archon.Api.Query`. |
| Status | Draft |
| Audience | Product owner, architect, frontend implementer, API implementer, test engineer |

## 1. Overview

### 1.1 Purpose

This specification defines WP002 for the ArchonExplorer UI work-package sequence. WP002 creates the shared frontend API client and runtime foundation used by later ArchonExplorer features to communicate with ArchonApi consistently, safely, and without duplicating route strings, request shaping, response handling, polling behavior, or notification behavior.

The package establishes the browser-side runtime layer that sits between React feature components and the existing Archon API surface. It does not implement feature screens such as Extraction Center, Snapshot Admin, search results, project catalogues, graph views, findings workbench, lenses, or visual analytics.

### 1.2 Background

WP001 created the ArchonExplorer application foundation and visible workbench shell. WP002 turns that shell into a runtime-ready application by adding the typed API-client boundary, TanStack Query conventions, safe diagnostic shaping, notification infrastructure, API connectivity state, polling helpers, and test seams required by later operational work packages.

The Archon UI roadmap requires operational capabilities before analytical and graph capabilities. WP002 therefore focuses on reusable runtime infrastructure rather than user-facing feature workflows. Extraction submission, extraction run monitoring, snapshot listing, and snapshot deletion are consumed by later packages, but WP002 must make those operations straightforward to implement without each feature inventing its own HTTP conventions.

### 1.3 High-Level Scope

WP002 covers:

- A hand-authored typed Archon API client foundation for ArchonExplorer.
- Central route constants based on the current ArchonApi route mappings.
- Preservation of the repository convention that public API paths have no common `/api` prefix.
- TanStack Query runtime configuration and query/mutation helper conventions.
- Shared request execution, response parsing, cancellation, timeout, and correlation handling.
- Shared safe error and diagnostic shaping for user-visible UI states.
- Polling helpers for asynchronous operational workflows, especially extraction run status polling.
- shadcn/ui-compatible notification/toast runtime infrastructure.
- Global API base URL and connectivity state.
- Initial mocks or test seams for API-client and UI journey tests.

WP002 excludes feature-specific screens, generated OpenAPI client adoption, graph rendering, arbitrary query consoles, raw Cypher access, and authentication/authorization flows.

## 2. System Context

### 2.1 Product Context

ArchonExplorer is the browser-delivered workbench for architectural investigation over Archon data. The application depends on ArchonApi for extraction runs, snapshot lifecycle administration, dashboard summaries, project and symbol catalogues, runtime facts, search, evidence, findings, metrics, diff data, and operational health.

WP002 provides the client-side access layer for those API surfaces. Later UI work packages consume this layer rather than importing route strings, calling `fetch` directly, or handling backend diagnostics in feature components.

### 2.2 Source References

WP002 shall align with these source materials:

- `docs/foundation/work-packages-ui.md` WP002 - API Client and Runtime Foundation.
- `docs/foundation/work-packages-ui.md` section 1.3 for the no-common-`/api` route convention.
- `docs/foundation/work-packages-ui.md` section 1.2 for `current` snapshot semantics.
- `docs/foundation/archon_ui_brief.md` section 3.4 for search implementation notes and route-shape expectations.
- `docs/foundation/archon_ui_brief.md` section 6.6 for snapshot-context expectations.
- `docs/foundation/archon_ui_brief.md` section 13 for API implications.
- `docs/foundation/archon_ui_brief.md` section 14.2 for UI state architecture.
- `docs/foundation/archon_ui_brief.md` section 14.3 for safe empty, loading, and error states.
- `src/ArchonApi/Program.cs` for API module composition and development OpenAPI/Scalar behavior.
- `src/Archon.Api.Extraction/ExtractionEndpointRouteBuilderExtensions.cs` for extraction routes.
- `src/Archon.Api.Management/ManagementEndpointRouteBuilderExtensions.cs` for management, health, and readiness routes.
- `src/Archon.Api.Query/QueryEndpointRouteBuilderExtensions.cs` for existing query routes.

### 2.3 Users and Stakeholders

| Stakeholder | Interest |
| --- | --- |
| Product owner | Confirms runtime foundations unlock later operational UI packages without prematurely implementing feature screens. |
| Architect | Confirms API boundaries, route conventions, state ownership, and diagnostic safety are centralized. |
| Frontend implementer | Uses typed client functions, route constants, query helpers, mutation helpers, notifications, and mocks. |
| API implementer | Confirms frontend route constants and contracts reflect the current ArchonApi surface. |
| Test engineer | Validates route construction, error shaping, polling behavior, notification behavior, and build/typecheck success. |
| Future ArchonExplorer user | Benefits indirectly from consistent loading, empty, error, retry, polling, and notification behavior across the workbench. |

## 3. Component Summary

### 3.1 Archon API Client Foundation

The API client foundation is the single browser-side abstraction for HTTP communication with ArchonApi. It owns base URL resolution, route construction, query-string serialization, request body serialization, response parsing, cancellation, timeout handling, safe error conversion, and common headers.

The foundation shall be hand-authored in WP002. A generated OpenAPI client is not selected for this package.

### 3.2 Route Catalog

The route catalog centralizes ArchonApi path construction. It shall be grouped by API area and based on existing endpoint mappings in ArchonApi, not on speculative examples. Constants and builders shall preserve the public route convention of no common `/api` prefix.

Routes that contain path parameters shall be exposed as functions that encode path values safely. Routes that use query parameters shall keep query construction separate from the base path so callers can pass typed query objects.

### 3.3 TanStack Query Runtime

TanStack Query is the server-state foundation for ArchonExplorer. WP002 shall define reusable query and mutation conventions, including stable query keys, retry defaults, stale-time defaults, cancellation behavior, invalidation helpers, and polling support.

Feature packages shall use these conventions for server state. They shall not create a competing server-state mechanism.

### 3.4 Safe Diagnostics and Error Model

The runtime shall convert transport failures, validation failures, server error payloads, unknown response shapes, cancellation, and unavailable API states into safe frontend error objects.

User-visible error objects shall not expose raw stack traces, connection strings, environment variables, raw Cypher, Neo4j internal identifiers, driver-specific details, or arbitrary backend exception text.

### 3.5 Notification Runtime

WP002 shall add notification/toast infrastructure compatible with shadcn/ui patterns. Feature packages can use this runtime for success, warning, failure, retry, and background-work messages without introducing another notification library or bespoke visual styling.

### 3.6 Connectivity and Readiness State

WP002 shall expose global API configuration and connectivity state. The state shall distinguish:

- API base URL is not configured.
- API base URL is configured but the API is unreachable.
- API responds to health/readiness checks.
- API responds but a specific feature request fails.

### 3.7 Test Seams and Mocks

WP002 shall provide test seams that allow component and journey tests to exercise feature behavior without running a live ArchonApi instance. Mocks shall use the same route catalog and response shapes as the client foundation where practical.

## 4. Functional Requirements

### 4.1 API Client Boundary

| ID | Requirement |
| --- | --- |
| FR-001 | ArchonExplorer shall expose a single API client foundation for browser-to-ArchonApi communication. |
| FR-002 | Feature components shall not call `fetch` directly for ArchonApi requests once the API client foundation exists. |
| FR-003 | The API client shall support typed request and response boundaries for implemented operational routes. |
| FR-004 | The API client shall allow later feature packages to add typed wrappers without changing the base request execution model. |
| FR-005 | The API client shall read the Archon API base URL from the WP001 runtime configuration mechanism. |
| FR-006 | The API client shall fail safely when the API base URL is absent. |
| FR-007 | The API client shall support abort/cancellation using browser-native request cancellation. |
| FR-008 | The API client shall support bounded timeout behavior or timeout-compatible cancellation for requests that should not hang indefinitely. |
| FR-009 | The API client shall centralize JSON serialization and parsing behavior. |
| FR-010 | The API client shall centralize handling for empty response bodies, malformed JSON, non-JSON responses, and unexpected content types. |

### 4.2 Route Catalog

| ID | Requirement |
| --- | --- |
| FR-011 | The route catalog shall define ArchonApi route constants and route builders in one shared frontend location. |
| FR-012 | Route constants shall not include a common `/api` prefix. |
| FR-013 | Route builders shall encode path parameters such as `runId`, `snapshotStableKey`, `projectStableKey`, `ruleCode`, `version`, `findingStableKey`, and `historyKey`. |
| FR-014 | Query-string construction shall be typed and centralized enough to avoid feature-specific string concatenation. |
| FR-015 | Route constants shall be grouped by API area such as operations, extraction, management, dashboard, projects, traversal, symbols, runtime, facts, evidence, rules, findings, metrics, diff, and search. |
| FR-016 | Route constants shall be based on existing ArchonApi endpoint mappings at implementation time. |
| FR-017 | If ArchonApi routes change before implementation, the implementation plan shall re-inspect ArchonApi before creating route constants. |

### 4.3 Implemented Operational Route Coverage

| ID | Requirement |
| --- | --- |
| FR-018 | The route catalog shall include `GET /health`. |
| FR-019 | The route catalog shall include `GET /ready`. |
| FR-020 | The route catalog shall include `POST /extractions`. |
| FR-021 | The route catalog shall include `GET /extractions`. |
| FR-022 | The route catalog shall include `GET /extractions/{runId}`. |
| FR-023 | The route catalog shall include `GET /management/snapshots`. |
| FR-024 | The route catalog shall include `DELETE /management/snapshots/{snapshotStableKey}`. |
| FR-025 | The route catalog shall include `POST /management/snapshots/delete-all`. |
| FR-026 | The route catalog shall include `GET /management/runs`. |
| FR-027 | The route catalog shall include other implemented management routes as constants even when feature wrappers are deferred. |

### 4.4 Implemented Query Route Coverage

| ID | Requirement |
| --- | --- |
| FR-028 | The route catalog shall include the implemented dashboard route `GET /dashboard-summary`. |
| FR-029 | The route catalog shall include implemented project routes under `/projects`. |
| FR-030 | The route catalog shall include implemented graph traversal routes under `/dependencies`, `/dependents`, `/dependency-path`, and `/graph-neighbourhood`. |
| FR-031 | The route catalog shall include implemented symbol routes under `/symbols`. |
| FR-032 | The route catalog shall include implemented runtime routes under `/runtime`. |
| FR-033 | The route catalog shall include implemented facts routes `/data-access`, `/configuration`, `/integrations`, and `/ui-technologies`. |
| FR-034 | The route catalog shall include implemented evidence routes under `/evidence`. |
| FR-035 | The route catalog shall include implemented rule and hotlist routes under `/rules` and `/hotlist`. |
| FR-036 | The route catalog shall include implemented metric, cycle, hotspot, architecture-rule, and snapshot-diff routes. |
| FR-037 | The route catalog shall include implemented search route `GET /search`. |
| FR-038 | The route catalog shall include implemented finding routes under `/findings` and `/finding-history`. |
| FR-039 | WP002 shall not implement full feature-specific UI behavior for these query routes. |

### 4.5 Typed Operational Client Methods

| ID | Requirement |
| --- | --- |
| FR-040 | The API client shall provide a typed method or equivalent wrapper for checking API health. |
| FR-041 | The API client shall provide a typed method or equivalent wrapper for checking API readiness. |
| FR-042 | The API client shall provide a typed method or equivalent wrapper for starting an extraction run. |
| FR-043 | The API client shall provide a typed method or equivalent wrapper for reading extraction run status by run ID. |
| FR-044 | The API client shall provide a typed method or equivalent wrapper for reading recent extraction run history. |
| FR-045 | The API client shall provide a typed method or equivalent wrapper for listing snapshot lifecycle rows. |
| FR-046 | The API client shall provide a typed method or equivalent wrapper for deleting one snapshot by stable key. |
| FR-047 | The API client shall provide a typed method or equivalent wrapper for deleting all snapshots through the explicit confirmation contract. |
| FR-048 | Feature-specific typed wrappers for deep query routes may be deferred to the packages that implement those features, provided their routes are represented in the route catalog. |

### 4.6 TanStack Query Server State

| ID | Requirement |
| --- | --- |
| FR-049 | WP002 shall define shared query-key conventions for ArchonApi requests. |
| FR-050 | Query keys shall include route area and relevant scope values such as repository stable key, solution stable key, snapshot selector, run ID, filters, pagination, and search text where applicable. |
| FR-051 | Query-key construction shall be centralized enough to avoid duplicate literal key arrays in feature code. |
| FR-052 | WP002 shall define default retry behavior suitable for safe idempotent reads. |
| FR-053 | WP002 shall avoid automatic retries for destructive mutations unless a later implementation explicitly proves the operation is idempotent and safe to retry. |
| FR-054 | WP002 shall define cache invalidation helpers for extraction runs and snapshot lifecycle state. |
| FR-055 | WP002 shall preserve cancellation behavior when components unmount or query inputs change. |
| FR-056 | WP002 shall define conventions for representing loading, empty, stale, refetching, and error states. |

### 4.7 Polling Helpers

| ID | Requirement |
| --- | --- |
| FR-057 | WP002 shall provide polling helper conventions for asynchronous Archon operations. |
| FR-058 | Polling helpers shall support extraction run status checks using `GET /extractions/{runId}`. |
| FR-059 | Polling helpers shall support stop conditions for completed, failed, canceled, or unavailable runs. |
| FR-060 | Polling helpers shall support bounded intervals and shall avoid tight request loops. |
| FR-061 | Polling helpers shall support cancellation when the consuming component unmounts or the user navigates away. |
| FR-062 | Polling helpers shall surface safe timeout or stalled-operation states without exposing backend internals. |

### 4.8 Safe Error and Diagnostic Shaping

| ID | Requirement |
| --- | --- |
| FR-063 | The runtime shall normalize validation problem responses into safe field or form errors. |
| FR-064 | The runtime shall normalize safe query error responses into user-visible messages and diagnostic codes. |
| FR-065 | The runtime shall normalize network failures into safe API unavailable messages. |
| FR-066 | The runtime shall normalize malformed or unexpected responses into safe unexpected-response messages. |
| FR-067 | The runtime shall preserve server-provided trace identifiers only when they are already safe for user display. |
| FR-068 | The runtime shall not display raw exception messages unless explicitly categorized as safe. |
| FR-069 | The runtime shall not display raw stack traces. |
| FR-070 | The runtime shall not display connection strings, environment variable values, credentials, tokens, raw Cypher, Neo4j internal identifiers, or driver-specific diagnostics. |
| FR-071 | The runtime shall support developer logging in browser console only when the logged content is safe and useful. |

### 4.9 Notification Infrastructure

| ID | Requirement |
| --- | --- |
| FR-072 | WP002 shall add shadcn/ui-compatible notification/toast infrastructure. |
| FR-073 | Notifications shall support success, information, warning, and error categories. |
| FR-074 | Notifications shall use existing shadcn/ui-compatible styling and shall not introduce custom marketing-style visual treatments. |
| FR-075 | Notifications shall be safe by default and shall use the normalized frontend error model. |
| FR-076 | Notifications shall support later feature packages notifying users about extraction start, extraction completion, extraction failure, snapshot deletion, and API availability changes. |
| FR-077 | Notifications shall not be used as the only representation of persistent page-level errors. |

### 4.10 Connectivity State

| ID | Requirement |
| --- | --- |
| FR-078 | WP002 shall provide a shared API connectivity state model. |
| FR-079 | Connectivity state shall distinguish configured, unconfigured, checking, reachable, not ready, unreachable, and unknown states. |
| FR-080 | Connectivity checks shall use existing ArchonApi operational routes rather than invented endpoints. |
| FR-081 | Connectivity state shall be consumable by the status bar and later dashboard or diagnostics surfaces. |
| FR-082 | Connectivity state shall not expose unsafe backend failure details. |

### 4.11 Test Seams and Mocks

| ID | Requirement |
| --- | --- |
| FR-083 | WP002 shall provide unit-testable route builders. |
| FR-084 | WP002 shall provide unit-testable error shaping helpers. |
| FR-085 | WP002 shall provide test seams or mocks for API-client consumers. |
| FR-086 | Mocks shall use stable route and contract names aligned with ArchonApi. |
| FR-087 | Mocks shall support at least health/readiness, extraction run status, extraction history, snapshot lifecycle listing, snapshot deletion, and delete-all snapshot behavior. |

### 4.12 Out-of-Scope Functional Behavior

| ID | Requirement |
| --- | --- |
| FR-088 | WP002 shall not implement the Extraction Center screen. |
| FR-089 | WP002 shall not implement extraction form UX or extraction run history UI. |
| FR-090 | WP002 shall not implement Snapshot Admin screens. |
| FR-091 | WP002 shall not implement active snapshot selection UI beyond runtime support required by later packages. |
| FR-092 | WP002 shall not implement global search UI or search result rendering. |
| FR-093 | WP002 shall not implement project, data, evidence, finding, metric, graph, or lens workbench screens. |
| FR-094 | WP002 shall not introduce an arbitrary graph query console, raw Cypher execution surface, SQL console, shell command surface, or filesystem command surface. |
| FR-095 | WP002 shall not implement authentication or authorization. |
| FR-096 | WP002 shall not adopt a generated OpenAPI client unless a later approved implementation plan explicitly changes that decision. |

## 5. Non-Functional Requirements

### 5.1 Maintainability

| ID | Requirement |
| --- | --- |
| NFR-001 | API client code shall be organized so route catalog, transport execution, error shaping, query keys, hooks/helpers, mocks, and runtime state are separable. |
| NFR-002 | Feature packages shall be able to add typed endpoint wrappers without editing unrelated route areas. |
| NFR-003 | Route names and grouping shall use ArchonApi and UI brief vocabulary consistently. |
| NFR-004 | Shared runtime utilities shall avoid circular dependencies with feature components. |
| NFR-005 | The client foundation shall remain browser-compatible and shall not depend on Node-only APIs for runtime behavior. |

### 5.2 Safety and Diagnostics

| ID | Requirement |
| --- | --- |
| NFR-006 | User-visible diagnostics shall be safe by default. |
| NFR-007 | The runtime shall fail closed when it cannot determine whether a diagnostic is safe. |
| NFR-008 | Error objects shall preserve enough safe metadata for support and testing, such as error category, endpoint area, HTTP status, and safe trace identifier where available. |
| NFR-009 | Destructive operation failures shall be communicated clearly without exposing backend implementation details. |
| NFR-010 | Client-side logs shall not leak request bodies that may contain paths, actor metadata, or future sensitive values. |

### 5.3 Performance

| ID | Requirement |
| --- | --- |
| NFR-011 | The API runtime shall not add graph rendering, visualization, data-grid, or heavy analytics libraries. |
| NFR-012 | Polling helpers shall avoid tight loops and shall stop when terminal conditions are reached. |
| NFR-013 | Query defaults shall avoid unnecessary refetch storms when the user changes workbench tabs or panels. |
| NFR-014 | Route construction and error shaping shall be lightweight and synchronous except for request execution. |

### 5.4 Developer Experience

| ID | Requirement |
| --- | --- |
| NFR-015 | The API client foundation shall be discoverable to frontend contributors. |
| NFR-016 | Runtime APIs shall use clear TypeScript names and exported types where appropriate. |
| NFR-017 | Tests shall make route and error behavior understandable without requiring a running backend. |
| NFR-018 | Validation commands shall fit the existing Visual Studio and PowerShell-friendly workflow. |

### 5.5 Accessibility and Usability Foundation

| ID | Requirement |
| --- | --- |
| NFR-019 | Runtime error and notification state shall support accessible UI presentation by later components. |
| NFR-020 | Notification semantics shall not rely on color alone. |
| NFR-021 | Long-running polling state shall be representable through status-bar and page-level UI, not only transient toast messages. |

## 6. User Experience Requirements

### 6.1 API Connectivity Indicator Support

WP002 shall provide the state and helpers needed for the status bar to show API configuration and connectivity. The visible UI may remain minimal in WP002, but the runtime model shall be suitable for WP003 and later packages to render consistent status text, badges, tooltips, or popovers using shadcn/ui-compatible components.

### 6.2 Error Presentation Support

The runtime shall make it easy for feature components to present safe error states. It should support at least:

- Inline form validation errors.
- Page-level unavailable or failed-load states.
- Toast notifications for short-lived operation outcomes.
- Status-bar indicators for connectivity and background activity.
- Retry affordance metadata where retry is safe.

### 6.3 Background Work Support

The runtime shall support extraction polling and background-work status without implementing the full Extraction Center UI. Later packages shall be able to show an extraction run as accepted, running, completed, failed, canceled, unknown, or unavailable using the API client and polling helpers.

### 6.4 Snapshot Context Support

The runtime shall preserve the UI brief semantics that `current` means the latest completed snapshot available to the API for the relevant repository or solution scope. WP002 shall support passing snapshot selector values through query helpers and route wrappers where applicable, but it shall not implement the complete active snapshot selection UI.

## 7. Technical Requirements

### 7.1 Frontend Runtime Stack

| Concern | Selection |
| --- | --- |
| API client implementation | Hand-authored TypeScript client foundation |
| Request transport | Browser-native `fetch` or an equivalent thin wrapper around it |
| Server state | TanStack Query |
| Notifications | shadcn/ui-compatible toast/notification pattern |
| Route constants | Central TypeScript route catalog based on ArchonApi mappings |
| Test seams | Mockable client interface or equivalent request adapter boundary |
| Generated OpenAPI client | Deferred and not selected for WP002 |

### 7.2 Route Convention

All route constants and route builders shall preserve the repository convention of no common `/api` prefix.

Examples of correct base route constants include:

| Area | Method | Route |
| --- | --- | --- |
| Operations | GET | `/health` |
| Operations | GET | `/ready` |
| Extraction | GET | `/extractions` |
| Extraction | POST | `/extractions` |
| Extraction | GET | `/extractions/{runId}` |
| Management | GET | `/management/snapshots` |
| Management | DELETE | `/management/snapshots/{snapshotStableKey}` |
| Management | POST | `/management/snapshots/delete-all` |
| Search | GET | `/search` |
| Dashboard | GET | `/dashboard-summary` |

Incorrect examples include `/api/extractions`, `/api/management/snapshots`, and `/api/search`.

### 7.3 Current ArchonApi Route Inventory

The WP002 route catalog shall be created from the current ArchonApi endpoint mappings. At specification time, the implemented route surface includes:

| Area | Methods and Routes |
| --- | --- |
| Operations | `GET /health`; `GET /ready` |
| Extraction | `GET /extractions`; `POST /extractions`; `GET /extractions/{runId}` |
| Management | `POST /management/repositories`; `POST /management/solutions`; `PATCH /management/metadata`; `GET /management/snapshots`; `DELETE /management/snapshots/{snapshotStableKey}`; `POST /management/snapshots/delete-all`; `POST /management/retention`; `GET /management/runs`; `PUT /management/rules/enablement`; `POST /management/maintenance` |
| Dashboard | `GET /dashboard-summary` |
| Projects | `GET /projects`; `GET /projects/detail`; `GET /projects/{*projectStableKey}` |
| Graph traversal | `GET /dependencies/direct`; `GET /dependents/direct`; `GET /dependencies/transitive`; `GET /dependents/transitive`; `GET /dependency-path`; `GET /graph-neighbourhood` |
| Symbols | `GET /symbols`; `GET /symbols/detail`; `GET /symbols/usages` |
| Runtime | `GET /runtime/endpoints`; `GET /runtime/controllers`; `GET /runtime/entry-points`; `GET /runtime/workers` |
| Facts | `GET /data-access`; `GET /configuration`; `GET /integrations`; `GET /ui-technologies` |
| Evidence | `GET /evidence/detail`; `GET /evidence/related` |
| Rules and findings summary | `GET /rules`; `GET /rules/{ruleCode}/{version}`; `GET /hotlist` |
| Metrics and analysis | `GET /snapshots/{snapshotStableKey}/metrics`; `GET /snapshot-metrics`; `GET /snapshot-cycles`; `GET /snapshot-hotspots`; `GET /snapshot-architecture-rules` |
| Snapshot diff | `GET /snapshot-diff`; `GET /snapshot-diff/latest` |
| Search | `GET /search` |
| Finding details | `GET /findings/detail`; `GET /findings/{snapshotStableKey}/{*findingStableKey}`; `GET /finding-history`; `GET /findings/history/{*historyKey}`; `POST /findings/suppressions` |

WP002 may create typed client methods only for operational routes needed by near-term packages, but the route catalog shall represent the current ArchonApi surface so later packages do not introduce duplicate route strings.

### 7.4 Request and Response Contracts

WP002 shall model TypeScript request and response shapes for the operational client methods it exposes. These shapes shall align with existing ArchonApi contracts, including extraction run status, extraction run history, snapshot lifecycle responses, delete snapshot responses, delete-all snapshot responses, health, readiness, validation problems, and safe query error responses.

For later query areas, WP002 may define generic response envelope types and defer detailed DTO modeling until the corresponding feature package implements UI behavior.

### 7.5 Error Categories

The frontend runtime shall classify failures into categories such as:

| Category | Meaning |
| --- | --- |
| `configuration` | API base URL or required runtime configuration is missing or invalid. |
| `network` | Request could not reach ArchonApi or was blocked by transport failure. |
| `timeout` | Request exceeded the configured timeout or polling limit. |
| `validation` | ArchonApi returned a validation problem or caller supplied invalid input. |
| `notFound` | ArchonApi returned a controlled not-found response. |
| `conflict` | ArchonApi returned a controlled conflict or disambiguation response. |
| `server` | ArchonApi returned a safe server error response. |
| `unexpectedResponse` | Response body or content type did not match the expected contract. |
| `cancelled` | Request was intentionally aborted. |
| `unknown` | Failure could not be classified safely. |

### 7.6 Destructive Operation Safeguards

The runtime shall treat destructive routes differently from reads. Snapshot deletion and delete-all snapshot wrappers shall avoid unsafe automatic retry, preserve confirmation body requirements, and return safe error results that feature components can present in dialogs or page-level states.

WP002 shall not implement the final destructive-action dialogs. It shall provide safe client primitives for WP005 to consume.

## 8. Data and State Requirements

### 8.1 Server State

TanStack Query owns server state for ArchonApi responses. Server state includes health/readiness checks, extraction run status, extraction run history, snapshot lifecycle lists, and later query results.

### 8.2 Local Workbench State

Local workbench state remains separate from server state. WP002 may expose local state for API base URL configuration, connectivity state, notification queue, and runtime preferences. It shall not store architecture facts, snapshots, extraction runs, or query results outside the server-state cache unless a later package explicitly requires derived local view state.

### 8.3 Notification State

Notification state shall contain only safe, user-presentable messages and optional safe metadata. It shall not store raw request bodies, raw backend responses, stack traces, connection strings, credentials, or other unsafe diagnostics.

### 8.4 Persisted Preferences

WP002 may support persisted preferences only where they are runtime-level and low risk, such as whether connectivity notifications are minimized. Persisted active snapshot, tab state, saved searches, and panel layout preferences belong to later packages unless already established by WP001 or WP003.

### 8.5 Snapshot Selector State

WP002 shall provide type support for snapshot selector values, including explicit stable keys and the `current` selector. It shall not decide the active snapshot for the user or implement deletion/unavailability warnings; those are WP006 responsibilities.

## 9. Integration Requirements

### 9.1 ArchonApi Integration

The client foundation shall integrate with the ArchonApi routes currently mapped by the API host. The implementation plan shall re-check route mappings before coding if the API has changed since this specification was written.

### 9.2 WP001 Integration

WP002 shall build on the ArchonExplorer application foundation from WP001. It shall not replace the Vite React TypeScript application, shadcn/ui setup, TanStack Query provider, or Aspire-hosted base URL configuration established by WP001.

### 9.3 Later Work Package Integration

WP002 shall provide runtime primitives consumed by:

| Later package | WP002 support |
| --- | --- |
| WP003 Workbench Desktop Shell | Connectivity status, notifications, runtime state, and safe error primitives. |
| WP004 Extraction Center | Start extraction, extraction history, extraction status polling, background-work notifications. |
| WP005 Snapshot Admin | Snapshot list, delete-one snapshot, delete-all snapshots, destructive operation error shaping. |
| WP006 Snapshot Context and Dashboard | Snapshot selector typing, dashboard route constants, connectivity and no-current states. |
| WP007 and later query packages | Route catalog, query-key conventions, generic response/error handling, safe diagnostics. |

### 9.4 Aspire and Development Hosting

WP002 shall use the API base URL provided through the WP001 Aspire/Vite development-time configuration. It shall not require developers to hard-code local ports in feature components.

### 9.5 OpenAPI and Scalar

ArchonApi maps OpenAPI and Scalar only in development. WP002 shall not depend on the Scalar UI at runtime. The implementation may use the development OpenAPI document as a reference during coding, but the selected WP002 deliverable remains a hand-authored API client foundation.

## 10. Validation Requirements

### 10.1 Required Validation

WP002 implementation shall be validated with:

| ID | Validation |
| --- | --- |
| VAL-001 | Run frontend typecheck successfully. |
| VAL-002 | Run frontend build successfully. |
| VAL-003 | Run route-builder unit tests for static routes and path-parameter encoding. |
| VAL-004 | Run unit tests confirming no route constant includes a common `/api` prefix. |
| VAL-005 | Run unit tests for query-string construction where practical. |
| VAL-006 | Run unit tests for safe error shaping across validation, not-found, network, timeout, server, unexpected-response, and cancellation scenarios. |
| VAL-007 | Run unit tests for polling stop conditions and cancellation behavior. |
| VAL-008 | Run component or runtime tests for notification creation using safe messages. |
| VAL-009 | Build the affected .NET solution or project set if API contract references or AppHost configuration are touched. |
| VAL-010 | Manually confirm ArchonExplorer can resolve configured/unconfigured API base URL state without unsafe diagnostics. |

### 10.2 Manual Smoke Checklist

The WP002 manual smoke validation should confirm:

- ArchonExplorer still launches after the runtime foundation is added.
- The app can show an API configured or unconfigured state.
- Health/readiness checks use existing ArchonApi routes.
- Failed API calls produce safe user-visible messages.
- Notifications render through the selected shadcn/ui-compatible pattern.
- No user-visible state exposes stack traces, environment values, connection strings, raw Cypher, Neo4j internals, or driver diagnostics.

### 10.3 Deferred Validation

Full journey tests for extraction submission, extraction history monitoring, snapshot administration, search, dashboard, project catalogues, evidence inspection, findings, graph rendering, and lenses are deferred to the work packages that implement those user journeys.

## 11. Acceptance Criteria

WP002 is complete when:

1. ArchonExplorer has a single shared API client foundation.
2. Route constants and builders are centralized and based on current ArchonApi endpoint mappings.
3. No route constant uses a common `/api` prefix.
4. Operational routes for health, readiness, extraction, extraction history, snapshot list, delete-one snapshot, and delete-all snapshots are represented.
5. Existing query routes are represented in the route catalog for later feature packages.
6. Typed operational client methods exist for near-term WP004, WP005, and WP006 consumers.
7. TanStack Query key, retry, cancellation, invalidation, and polling conventions are defined and usable.
8. Safe error shaping prevents raw backend diagnostics from reaching user-visible UI.
9. Notification/toast infrastructure is available through shadcn/ui-compatible patterns.
10. API connectivity state distinguishes unconfigured, checking, reachable, not ready, unreachable, and unknown states.
11. Test seams or mocks allow API-consuming components to be tested without a live backend.
12. Frontend typecheck and build validations pass.
13. Unit tests cover route construction, no-`/api` route convention, safe error shaping, and polling helper behavior where practical.
14. No feature-specific extraction, snapshot, search, project, evidence, findings, graph, or lens screen is implemented as part of WP002.
15. No generated OpenAPI client is adopted unless a later approved implementation plan changes that decision.

## 12. Documentation and Wiki Impact

### 12.1 Required Documentation Updates

The WP002 implementation plan shall include a documentation pass. At minimum, contributor-facing documentation should explain:

- where the ArchonExplorer API client foundation lives;
- how the API base URL is configured;
- how route constants and route builders are organized;
- how to add a typed wrapper for a new ArchonApi endpoint;
- how to use TanStack Query keys and invalidation helpers;
- how to use polling helpers safely;
- how to present safe errors and notifications;
- how to add or update API-client mocks for tests.

### 12.2 Wiki Guidance

If existing wiki pages describe UI architecture, API usage, local development, or testing, they should be updated during implementation. Contributor guidance should live in the wiki rather than in standalone implementation notes when the repository has an established wiki location.

### 12.3 Documentation Standard

Any implementation work that introduces code documentation requirements shall treat internal and non-public types as requiring the same developer-level documentation quality as public types. Documentation expectations must not be scoped only to public API surfaces.

## 13. Risks and Decisions

### 13.1 Decisions

| Decision | Rationale |
| --- | --- |
| Use the existing numbered work-package documentation pattern | The repository already uses numbered folders and WP001 provides the applicable spec style. |
| Create a single WP002 spec document | The requested deliverable is one markdown specification document for WP002. |
| Place the document under `docs/021-API-Client-and-Runtime-Foundation` | Existing numbered work-package folders run through `020-ArchonExplorer-Foundation`; WP002 UI work follows WP001. |
| Treat WP002 as specification work only | The request is for a spec; no application code changes are part of this task. |
| Inspect ArchonApi routes as the source of truth | Route constants must reflect existing API mappings rather than relying only on roadmap examples. |
| Use a hand-authored typed API client foundation | WP002 explicitly excludes a full generated OpenAPI client unless explicitly selected later. |
| Preserve no common `/api` prefix | This is a repository route convention and is visible in current ArchonApi route mappings. |
| Represent all current ArchonApi routes in the route catalog | Prevents later feature packages from duplicating route strings or inventing route shapes. |
| Implement typed wrappers first for operational routes | Operational UI packages WP004, WP005, and WP006 are next in the required sequence. |
| Use TanStack Query for server state | Aligns with the UI brief and WP001 provider foundation. |
| Keep local workbench state separate from server state | Prevents duplicating API data in local UI state and supports predictable cache invalidation. |
| Use notification state only for safe user messages | Prevents accidental leakage of backend diagnostics through transient UI. |
| Support `current` as a snapshot selector value | Matches the UI brief definition of latest completed snapshot for the relevant scope. |

### 13.2 Risks

| Risk | Mitigation |
| --- | --- |
| API routes may change before implementation. | Re-inspect ArchonApi endpoint mappings during implementation planning and update the route catalog accordingly. |
| The route catalog could become too broad for WP002. | Include route constants for all current routes, but limit typed feature wrappers to near-term operational consumers. |
| Feature teams may bypass the shared client. | Require feature packages to use the API client foundation and avoid direct `fetch` calls. |
| Error shaping may hide useful support information. | Preserve safe metadata such as category, status, endpoint area, and trace identifier while blocking unsafe internals. |
| Polling could overload local development services. | Use bounded intervals, terminal stop conditions, cancellation, and sensible defaults. |
| Destructive operations could be retried accidentally. | Disable automatic retries for destructive mutations unless a later plan proves retry safety. |
| Notification infrastructure could drift into bespoke styling. | Require shadcn/ui-compatible notification patterns and existing toolkit styling. |

## 14. Traceability

| Source expectation | WP002 treatment |
| --- | --- |
| Shared frontend runtime | Required by FR-001 through FR-010. |
| Route constants with no `/api` prefix | Required by FR-011 through FR-017 and FR-018 through FR-039. |
| Existing extraction endpoints | Required by FR-020 through FR-022 and FR-042 through FR-044. |
| Existing snapshot management endpoints | Required by FR-023 through FR-027 and FR-045 through FR-047. |
| Existing query API surface | Represented by FR-028 through FR-039 and route inventory in section 7.3. |
| TanStack Query state architecture | Required by FR-049 through FR-056. |
| Polling for asynchronous operations | Required by FR-057 through FR-062. |
| Safe diagnostics | Required by FR-063 through FR-071 and NFR-006 through NFR-010. |
| Notification infrastructure | Required by FR-072 through FR-077. |
| API connectivity state | Required by FR-078 through FR-082. |
| Test seams and mocks | Required by FR-083 through FR-087. |
| No feature-specific screens in WP002 | Enforced by FR-088 through FR-096. |
| Documentation standard for non-public types | Required by section 12.3. |

## 15. Open Questions for Implementation Planning

No blocking product questions remain for WP002 specification creation. Implementation planning may still decide exact file names, TypeScript type organization, route catalog module boundaries, timeout defaults, polling intervals, toast component selection, and mock strategy, provided those decisions satisfy this specification and re-check the current ArchonApi routes before implementation.
