# WP011 Specification - .NET Client and UI-Technology Extraction for API/MCP Facts

## Document Control

| Field | Value |
| --- | --- |
| Work Package | WP011 - .NET Client and UI-Technology Extraction for API/MCP Facts |
| Output Path | `docs/011-.NET-Client-and-UI-Technology-Extraction-for-API-MCP-Facts/spec-wp011-dotnet-client-and-ui-technology-extraction.md` |
| Source Work Package | `docs/foundation/work-packages.md` WP011 |
| Source Brief | `docs/foundation/archon_full_concept_brief.md` |
| Specification Basis | `spec-template_v1.1.md` requested; template file was not present in the workspace at creation time, so this document follows the repository specification structure and documented work-package rules. |
| Status | Draft |
| Audience | Product owner, architect, implementation team, test engineer |

## 1. Overview

### 1.1 Purpose

This specification defines the requirements for WP011, the Archon work package that extracts .NET client and UI-technology architecture facts from analyzed repositories for API and MCP consumers. The package treats user-facing .NET technologies as first-class architecture facts because routes, components, pages, windows, views, controls, bindings, commands, navigation, event handlers, API usage, service usage, and data-access usage often define practical change-impact paths.

WP011 is a backend extraction work package only. It must not introduce Archon's own Discovery UI, dashboard, explorer, graph view, prompt panel, visual component browser, or any human-facing product UI surface.

### 1.2 Background

Archon is an API-first and MCP-first architecture intelligence platform for modern and legacy .NET estates. Earlier work packages establish project/package extraction, Roslyn semantic extraction, configuration and dependency-injection extraction, runtime extraction, data-access extraction, external integration extraction, snapshot orchestration, and Neo4j persistence.

WP011 adds the .NET client and UI-technology slice to that deterministic graph. It must normalize framework-specific findings into common graph concepts, persist evidence-backed facts through the established snapshot contract, represent unknowns explicitly, and shape output for later API query, MCP tool, rule, metric, diff, and markdown-export work packages.

### 1.3 High-Level Scope

WP011 covers backend extraction for these .NET UI and client technologies in analyzed repositories:

- Blazor `.razor` components, routes, layouts, dependency injection, parameters, render modes, authorization, component usage, API/client usage, and configuration usage.
- Razor Pages and MVC Razor views, including `.cshtml`, page models, handlers, layouts, partials, view components, tag helpers, forms, links, route metadata, and controller/action linkage where detectable.
- Windows Forms applications, forms, user controls, designer files, resources, controls, event wiring, data bindings, startup forms, code-behind dependencies, service usage, and data-access usage.
- WPF applications, windows, pages, user controls, resource dictionaries, styles, templates, bindings, commands, routed events, navigation, view-model relationships, service usage, and data-access usage.
- WinUI applications, windows, pages, user controls, resources, styles, bindings, commands, navigation, app startup, packaged desktop metadata, service usage, and data-access usage.
- .NET MAUI applications, pages, views, Shell routes, handlers, resources, styles, bindings, commands, view-model relationships, platform-specific heads, navigation targets, service usage, and data-access usage.
- Avalonia applications, AXAML views, windows, user controls, resources, styles, bindings, commands, view locators, view-model relationships, navigation targets, service usage, and data-access usage.
- Common UI graph facts, relationships, evidence, metadata, confidence, warnings, and unknowns.
- Tests for all production behavior introduced by this work package.
- Documentation explaining supported .NET UI extraction behavior, evidence, confidence, unknowns, limitations, and validation.

WP011 excludes Archon Discovery UI implementation, JavaScript and TypeScript frontend framework extraction, API query product-surface implementation, MCP tools/resources/prompts, rule-engine evaluation, markdown export, snapshot diff, live application execution, browser automation, UI rendering, or screenshot capture.

## 2. System Context

### 2.1 Product Context

Archon accepts API-triggered extraction requests, loads submitted repositories and explicit solution paths, extracts deterministic architecture facts, persists them in Neo4j, and later exposes them through API and MCP surfaces. WP011 contributes the .NET UI/client slice of the architecture graph.

The package must use the existing extraction orchestration path and shared snapshot accumulator. It must not scan arbitrary directories independently of the submitted request, bypass the snapshot contract, execute analyzed applications, render UI frameworks, compile or launch target apps outside the established Roslyn/MSBuild analysis flow, or persist data directly outside the graph persistence adapter.

### 2.2 Source References

WP011 must align with these source materials:

- `docs/foundation/work-packages.md` WP011 objective, required implementation, and completion criteria.
- `docs/foundation/archon_full_concept_brief.md` section 19 for .NET UI extraction across Blazor, Razor Pages/views, Windows Forms, WPF, WinUI, .NET MAUI, and Avalonia.
- `docs/foundation/archon_full_concept_brief.md` section 12.3 and section 12.4 for UI-related node and edge kinds.
- `docs/foundation/archon_full_concept_brief.md` Appendix E sections E.4.1 through E.4.3 and E.6.4 for graph model and UI extraction support.
- `docs/foundation/archon_full_concept_brief.md` section 35 phase 5 and section 36 .NET UI Extraction epic, interpreted strictly as backend extraction rather than Archon product UI delivery.
- `docs/foundation/work-packages.md` completion rules for evidence-backed facts, explicit unknowns, deterministic stable keys, Neo4j as the system of record, tests, documentation, and no Discovery UI.

### 2.3 Users and Stakeholders

| Stakeholder | Interest |
| --- | --- |
| Product owner | Confirms WP011 covers the required UI-technology extraction scope without introducing Archon Discovery UI. |
| Architect | Confirms UI/client facts are normalized consistently and preserve evidence, confidence, and unknowns. |
| Developer | Uses extracted facts to understand user-facing change impact, UI-to-service paths, UI-to-data paths, routing, commands, bindings, and navigation. |
| Test engineer | Verifies detection coverage, framework parity, evidence quality, confidence assignment, unknown handling, and no UI product delivery. |
| Future API consumer | Depends on persisted UI facts being complete enough for query endpoints in later work packages. |
| Future MCP consumer | Depends on evidence-backed UI facts for impact analysis and Copilot workflows in later work packages. |

## 3. Component Summary

### 3.1 Blazor Extractor

The Blazor extractor detects `.razor` components, `@page` routes, `@layout`, `@inject`, `[Parameter]`, `[CascadingParameter]`, `EventCallback`, `RenderFragment`, `AuthorizeView`, `[Authorize]`, interactive render modes, hosting models, component references, route parameters, forms, validation components, API/client usage, and configuration usage. It emits normalized UI application, component, route, layout, parameter, service-dependency, event, authorization, component-usage, API-call, configuration, evidence, confidence, and unknown facts.

### 3.2 Razor Pages and MVC Razor Extractor

The Razor extractor detects `.cshtml` files, Razor Pages, MVC Razor views, `_ViewImports`, `_ViewStart`, layouts, partials, view components, tag helpers, page models, handler methods, form posts, anchor tag helpers, route conventions, authorization metadata, and controller/action linkage where detectable. It emits normalized page, view, layout, partial, view-component, tag-helper, handler, route, form, navigation, controller/action, evidence, confidence, and unknown facts.

### 3.3 Windows Forms Extractor

The Windows Forms extractor detects `System.Windows.Forms` references, Windows Forms project settings, `Application.Run`, forms, user controls, designer files, `.resx` resources, control fields, `InitializeComponent`, event handler subscriptions, data bindings, startup forms, service usage, and data-access usage. It emits UI application, form, user-control, control-hierarchy, resource, event, binding, startup, dependency, evidence, confidence, and unknown facts.

### 3.4 WPF Extractor

The WPF extractor detects `PresentationFramework` references, `ApplicationDefinition`, `.xaml` files, windows, pages, user controls, resource dictionaries, styles, control templates, data templates, bindings, commands, routed events, navigation services, and view-model conventions. It emits UI application, window, page, view, layout/resource/style/template, binding, command, event, navigation, view-model, dependency, evidence, confidence, and unknown facts.

### 3.5 WinUI Extractor

The WinUI extractor detects `Microsoft.UI.Xaml` references, WinUI project properties, `.xaml` files, windows, pages, user controls, resources, styles, bindings, commands, navigation frame usage, app startup, and packaged desktop metadata. It emits UI application, window, page, view, resource, style, binding, command, navigation, view-model, packaging, dependency, evidence, confidence, and unknown facts.

### 3.6 .NET MAUI Extractor

The .NET MAUI extractor detects `UseMaui`, `MauiProgram`, `.xaml` files, `ContentPage`, `ContentView`, Shell, Shell routes, handlers, resources, styles, bindings, commands, platform-specific heads, view-model relationships, navigation targets, service usage, and data-access usage. It emits UI application, page, view, route, handler, resource, style, binding, command, platform-head, navigation, view-model, dependency, evidence, confidence, and unknown facts.

### 3.7 Avalonia Extractor

The Avalonia extractor detects Avalonia package references, AXAML files, `App.axaml`, windows, user controls, resources, styles, bindings, commands, view locator patterns, ReactiveUI usage where present, application startup, service usage, and data-access usage. It emits UI application, window, user-control, resource, style, binding, command, view-model, view-locator, navigation, dependency, evidence, confidence, and unknown facts.

### 3.8 UI Graph Integration

WP011 uses repository/project/package facts, Roslyn semantic outputs, configuration facts, dependency-injection facts, runtime facts, data-access facts, and external integration facts from earlier work packages. It must emit normalized UI facts through the shared snapshot contract. Neo4j remains the only system of record, and extractor projects must not write directly to Neo4j.

## 4. Functional Requirements

### 4.1 Extraction Pipeline Participation

| ID | Requirement |
| --- | --- |
| FR-001 | WP011 shall register .NET UI/client extractors with the existing extraction orchestration path. |
| FR-002 | WP011 extractors shall run only as part of an API-triggered extraction using a repository root directory and explicit solution path list. |
| FR-003 | WP011 extractors shall consume repository, solution, project, package, semantic symbol, configuration, dependency-injection, runtime, data-access, external-integration, and file artifact context produced by earlier stages where available. |
| FR-004 | WP011 extractors shall contribute nodes, relationships, evidence, metadata, warnings, and errors to the shared snapshot accumulator. |
| FR-005 | WP011 extractors shall not persist directly to Neo4j, write sidecar extraction files, execute analyzed applications, render UI, launch browsers, take screenshots, mutate target projects, or require live services. |
| FR-006 | WP011 output shall be snapshot-scoped and compatible with deterministic stable keys and fingerprints established by prior work packages. |

### 4.2 Blazor Extraction

| ID | Requirement |
| --- | --- |
| FR-007 | The extractor shall detect `.razor` files as Blazor component candidates. |
| FR-008 | The extractor shall detect `@page` directives and route templates. |
| FR-009 | The extractor shall detect `@layout` directives and layout inheritance. |
| FR-010 | The extractor shall detect `@inject` directives and injected service types. |
| FR-011 | The extractor shall detect `[Parameter]` properties. |
| FR-012 | The extractor shall detect `[CascadingParameter]` properties. |
| FR-013 | The extractor shall detect `EventCallback` parameters and event callback usage. |
| FR-014 | The extractor shall detect `RenderFragment` parameters and child content slots. |
| FR-015 | The extractor shall detect `AuthorizeView` usage and `[Authorize]` metadata. |
| FR-016 | The extractor shall detect interactive render modes where statically available. |
| FR-017 | The extractor shall classify Blazor hosting as Server, WebAssembly, Web App, Hybrid, or Unknown where evidence supports classification. |
| FR-018 | The extractor shall detect child component usage relationships. |
| FR-019 | The extractor shall detect route parameters and route constraints where statically available. |
| FR-020 | The extractor shall detect forms and validation component usage. |
| FR-021 | The extractor shall detect API/client usage from components where semantic or integration facts support the link. |
| FR-022 | The extractor shall detect configuration usage from components where semantic or configuration facts support the link. |
| FR-023 | The extractor shall represent unresolved component types, dynamic render fragments, computed routes, unknown render modes, and ambiguous hosting as explicit unknowns. |

### 4.3 Razor Pages and MVC Razor Extraction

| ID | Requirement |
| --- | --- |
| FR-024 | The extractor shall detect `.cshtml` files. |
| FR-025 | The extractor shall distinguish Razor Pages from MVC Razor views where project structure, directives, page models, routing, or runtime facts support classification. |
| FR-026 | The extractor shall detect `_ViewImports.cshtml` and inherited namespaces/tag helpers. |
| FR-027 | The extractor shall detect `_ViewStart.cshtml` and default layout behavior where statically available. |
| FR-028 | The extractor shall detect layouts and layout usage. |
| FR-029 | The extractor shall detect partial views and partial usage. |
| FR-030 | The extractor shall detect view components and view-component invocation. |
| FR-031 | The extractor shall detect tag helper usage. |
| FR-032 | The extractor shall detect page models and link Razor Pages to page model types. |
| FR-033 | The extractor shall detect handler methods such as `OnGet`, `OnPost`, and related Razor Page handler variants. |
| FR-034 | The extractor shall detect form posts and form action metadata where statically available. |
| FR-035 | The extractor shall detect anchor tag helpers and navigation targets where statically available. |
| FR-036 | The extractor shall detect route conventions and route metadata where available. |
| FR-037 | The extractor shall detect controller/action linkage for MVC Razor views where deterministic route, view name, controller, action, or runtime facts support the link. |
| FR-038 | The extractor shall detect authorization metadata inherited from pages, controllers, actions, conventions, or attributes where evidence exists. |
| FR-039 | The extractor shall represent unresolved view models, dynamic partial names, dynamic view names, unknown controller/action links, and computed navigation targets as explicit unknowns. |

### 4.4 Windows Forms Extraction

| ID | Requirement |
| --- | --- |
| FR-040 | The extractor shall detect Windows Forms projects from references, package/project metadata, application settings, and source symbols. |
| FR-041 | The extractor shall detect `Application.Run` and startup form candidates. |
| FR-042 | The extractor shall detect forms. |
| FR-043 | The extractor shall detect user controls. |
| FR-044 | The extractor shall detect designer files and correlate designer partial classes to form or user-control types. |
| FR-045 | The extractor shall detect `.resx` resources associated with forms and controls. |
| FR-046 | The extractor shall detect controls declared as fields or initialized in `InitializeComponent`. |
| FR-047 | The extractor shall detect control hierarchy where designer code provides deterministic parent/child evidence. |
| FR-048 | The extractor shall detect event handler subscriptions. |
| FR-049 | The extractor shall detect data bindings where statically available. |
| FR-050 | The extractor shall detect code-behind dependencies on services, configuration, integrations, and data-access facts where prior semantic facts support the link. |
| FR-051 | The extractor shall represent unresolved designer associations, dynamically created controls, computed event wiring, and runtime-generated bindings as explicit unknowns. |

### 4.5 WPF Extraction

| ID | Requirement |
| --- | --- |
| FR-052 | The extractor shall detect WPF projects from `PresentationFramework`, project metadata, `ApplicationDefinition`, XAML files, and source symbols. |
| FR-053 | The extractor shall detect WPF applications and startup URI or startup object where available. |
| FR-054 | The extractor shall detect windows. |
| FR-055 | The extractor shall detect pages. |
| FR-056 | The extractor shall detect user controls. |
| FR-057 | The extractor shall detect resource dictionaries and merged dictionaries. |
| FR-058 | The extractor shall detect styles. |
| FR-059 | The extractor shall detect control templates and data templates. |
| FR-060 | The extractor shall detect bindings and binding paths where statically available. |
| FR-061 | The extractor shall detect commands and command bindings where statically available. |
| FR-062 | The extractor shall detect routed events and event handlers. |
| FR-063 | The extractor shall detect navigation services and navigation targets where statically available. |
| FR-064 | The extractor shall detect view-model relationships through DataContext assignment, conventions, dependency injection, or binding context evidence where available. |
| FR-065 | The extractor shall detect service usage and data-access usage from code-behind or view models where prior semantic facts support the link. |
| FR-066 | The extractor shall represent unresolved binding paths, dynamic resources, runtime template selection, convention-only view models, and computed navigation as explicit unknowns. |

### 4.6 WinUI Extraction

| ID | Requirement |
| --- | --- |
| FR-067 | The extractor shall detect WinUI projects from `Microsoft.UI.Xaml` references, project properties, package references, XAML files, and source symbols. |
| FR-068 | The extractor shall detect WinUI application startup. |
| FR-069 | The extractor shall detect windows. |
| FR-070 | The extractor shall detect pages. |
| FR-071 | The extractor shall detect user controls. |
| FR-072 | The extractor shall detect resources and resource dictionaries. |
| FR-073 | The extractor shall detect styles. |
| FR-074 | The extractor shall detect bindings and binding paths where statically available. |
| FR-075 | The extractor shall detect commands where statically available. |
| FR-076 | The extractor shall detect navigation frame usage and navigation targets where statically available. |
| FR-077 | The extractor shall detect view-model relationships where evidence exists. |
| FR-078 | The extractor shall extract packaged desktop metadata where available. |
| FR-079 | The extractor shall detect service usage and data-access usage from code-behind or view models where prior semantic facts support the link. |
| FR-080 | The extractor shall represent unresolved binding paths, dynamic resources, runtime navigation, packaging ambiguity, and convention-only view models as explicit unknowns. |

### 4.7 .NET MAUI Extraction

| ID | Requirement |
| --- | --- |
| FR-081 | The extractor shall detect .NET MAUI projects from `UseMaui`, `MauiProgram`, package/project metadata, XAML files, and source symbols. |
| FR-082 | The extractor shall detect .NET MAUI applications. |
| FR-083 | The extractor shall detect pages including `ContentPage` and related page types. |
| FR-084 | The extractor shall detect views including `ContentView` and related view types. |
| FR-085 | The extractor shall detect Shell usage. |
| FR-086 | The extractor shall detect Shell routes and route registration where statically available. |
| FR-087 | The extractor shall detect handlers where statically available. |
| FR-088 | The extractor shall detect resources and resource dictionaries. |
| FR-089 | The extractor shall detect styles. |
| FR-090 | The extractor shall detect bindings and binding paths where statically available. |
| FR-091 | The extractor shall detect commands where statically available. |
| FR-092 | The extractor shall detect view-model relationships where evidence exists. |
| FR-093 | The extractor shall detect platform-specific heads and target platform metadata where available. |
| FR-094 | The extractor shall detect navigation targets where statically available. |
| FR-095 | The extractor shall detect service usage and data-access usage from pages, views, code-behind, or view models where prior semantic facts support the link. |
| FR-096 | The extractor shall represent unresolved Shell routes, dynamic navigation, platform-specific ambiguity, unresolved binding paths, and convention-only view models as explicit unknowns. |

### 4.8 Avalonia Extraction

| ID | Requirement |
| --- | --- |
| FR-097 | The extractor shall detect Avalonia projects from package references, AXAML files, `App.axaml`, source symbols, and startup code. |
| FR-098 | The extractor shall detect Avalonia applications. |
| FR-099 | The extractor shall detect windows. |
| FR-100 | The extractor shall detect user controls. |
| FR-101 | The extractor shall detect AXAML resources. |
| FR-102 | The extractor shall detect styles. |
| FR-103 | The extractor shall detect bindings and binding paths where statically available. |
| FR-104 | The extractor shall detect commands where statically available. |
| FR-105 | The extractor shall detect view locator patterns. |
| FR-106 | The extractor shall detect ReactiveUI usage where present. |
| FR-107 | The extractor shall detect view-model relationships through view locators, DataContext assignment, naming conventions, dependency injection, or binding evidence where available. |
| FR-108 | The extractor shall detect navigation targets where statically available. |
| FR-109 | The extractor shall detect service usage and data-access usage from views, code-behind, or view models where prior semantic facts support the link. |
| FR-110 | The extractor shall represent unresolved bindings, dynamic styles, convention-only view locators, dynamic navigation, and ambiguous ReactiveUI relationships as explicit unknowns. |

### 4.9 Graph Nodes and Relationships

| ID | Requirement |
| --- | --- |
| FR-111 | The extractor shall emit `UiApplication` nodes through the snapshot contract for detected UI applications. |
| FR-112 | The extractor shall emit `UiComponent` nodes for components or component-like UI elements. |
| FR-113 | The extractor shall emit `UiPage` nodes for pages. |
| FR-114 | The extractor shall emit `UiView` nodes for views. |
| FR-115 | The extractor shall emit `UiLayout` nodes for layouts. |
| FR-116 | The extractor shall emit `UiRoute` nodes for UI routes. |
| FR-117 | The extractor shall emit `UiControl` nodes for controls. |
| FR-118 | The extractor shall emit `UiResource` nodes for resources and resource dictionaries. |
| FR-119 | The extractor shall emit `UiStyle` nodes for styles and templates where represented as styles. |
| FR-120 | The extractor shall emit `ViewModel` nodes for view-model facts where evidence exists. |
| FR-121 | The extractor shall emit `Command` nodes for command facts where evidence exists. |
| FR-122 | The extractor shall emit `Binding` nodes for binding facts where evidence exists. |
| FR-123 | The extractor shall reuse existing `Project`, `Method`, `Type`, `ConfigurationKey`, `ExternalService`, data-access, integration, and file-path nodes rather than creating duplicate conceptual nodes. |
| FR-124 | The extractor shall emit `DECLARES_COMPONENT` relationships where projects, applications, pages, views, or components declare UI components. |
| FR-125 | The extractor shall emit `DECLARES_UI_ROUTE` relationships for UI routes. |
| FR-126 | The extractor shall emit `USES_COMPONENT` relationships for component usage. |
| FR-127 | The extractor shall emit `USES_LAYOUT` relationships for layout usage. |
| FR-128 | The extractor shall emit `USES_CONTROL` relationships for control usage and hierarchy. |
| FR-129 | The extractor shall emit `USES_UI_RESOURCE` relationships for resource usage. |
| FR-130 | The extractor shall emit `USES_STYLE` relationships for style or template usage. |
| FR-131 | The extractor shall emit `BINDS_TO` relationships for bindings. |
| FR-132 | The extractor shall emit `USES_COMMAND` relationships for command usage. |
| FR-133 | The extractor shall emit `USES_VIEW_MODEL` relationships for view-model relationships. |
| FR-134 | The extractor shall emit `NAVIGATES_TO` relationships for navigation where evidence exists. |
| FR-135 | The extractor shall emit `HANDLES_UI_EVENT` relationships for UI event wiring. |
| FR-136 | The extractor shall emit `CALLS_API` relationships for UI-to-API or UI-to-client usage where prior integration/runtime evidence supports the link. |
| FR-137 | The extractor shall emit `USES_CONFIG` relationships where UI facts depend on configuration keys. |
| FR-138 | The extractor shall emit `DEPENDS_ON` relationships for UI-related dependencies that are not better represented by a more specific relationship. |
| FR-139 | The extractor shall attach evidence to every non-derived UI/client fact. |

### 4.10 Metadata, Confidence, Unknowns, Warnings, and Errors

| ID | Requirement |
| --- | --- |
| FR-140 | The extractor shall store UI framework, UI artifact kind, file path, project key, target framework, language, type name, method name, route template, component name, layout name, control name, binding path, command name, event name, navigation target, view-model type, resource key, style key, render mode, hosting model, platform head, detection mode, confidence reason, and unknown reason in metadata where available. |
| FR-141 | The extractor shall assign high confidence to symbol-resolved facts, exact project metadata matches, exact package/reference matches, exact XAML/AXAML/Razor directive matches, exact designer associations, and explicit route declarations. |
| FR-142 | The extractor shall assign medium confidence to strongly supported syntax, naming, file-pattern, project-structure, or convention detections that are not fully symbol-resolved. |
| FR-143 | The extractor shall assign low confidence to convention-only view-model relationships, inferred navigation, naming-only framework classification, and partially dynamic UI relationships. |
| FR-144 | The extractor shall represent unresolved component references, computed routes, dynamic views, dynamic controls, dynamic resources, unresolved bindings, runtime navigation, convention-only view models, and unknown UI framework classification as explicit unknowns with unknown reason. |
| FR-145 | The extractor shall produce warnings for unreadable Razor/XAML/AXAML/designer/resource artifacts, malformed markup, unsupported UI project formats, unresolvable generated files, partial compilation failures, and extraction-scope limitations that affect UI extraction. |
| FR-146 | The extractor shall produce extraction errors only for failures that prevent the UI/client slice from completing for a project or solution. |
| FR-147 | The extractor shall not silently omit partially detectable UI facts when explicit unknown representation is possible. |

## 5. Non-Functional Requirements

### 5.1 Determinism and Evidence

| ID | Requirement |
| --- | --- |
| NFR-001 | Given the same repository content, solution paths, extraction settings, and dependency versions, WP011 shall produce deterministic UI/client facts. |
| NFR-002 | Stable keys and fingerprints for WP011 facts shall not depend on database IDs, absolute developer machine paths, enumeration order, generated temporary paths, live runtime state, UI rendering output, browser state, or external service availability. |
| NFR-003 | Every persisted UI/client architectural statement shall have evidence unless it is purely derived from persisted facts. |
| NFR-004 | Evidence shall preserve enough context for later API and MCP consumers to explain the fact without re-reading source files. |

### 5.2 Security and Safe Static Analysis

| ID | Requirement |
| --- | --- |
| NFR-005 | The extractor shall not execute analyzed applications, instantiate target UI apps, render screens, launch browsers, use device simulators, connect to remote services, submit forms, invoke event handlers, call APIs, or mutate files. |
| NFR-006 | Secret-like UI, API, configuration, authentication, or form values shall not be stored in metadata, evidence snippets, warnings, errors, logs, API-ready responses, or generated outputs. |
| NFR-007 | Evidence snippets from markup, code-behind, designer files, and configuration shall be redacted before storage when they contain values that look like passwords, tokens, API keys, connection strings, authorization headers, private keys, client secrets, or credentials. |
| NFR-008 | UI/client extraction shall be static and deterministic, based on source files, project artifacts, generated files, markup, resource files, package metadata, and Roslyn semantic information. |

### 5.3 Performance and Scalability

| ID | Requirement |
| --- | --- |
| NFR-009 | The extractor shall avoid repeated parsing of the same Razor, XAML, AXAML, designer, resource, or semantic artifact where prior context is available. |
| NFR-010 | The extractor shall use cancellation tokens from the extraction orchestration path. |
| NFR-011 | The extractor shall avoid unbounded recursion when following component references, resource dictionaries, navigation links, bindings, view locators, control hierarchies, or view-model conventions. |
| NFR-012 | The extractor shall define safeguards for large generated designer files, large XAML/AXAML resource dictionaries, large component trees, and repositories with many UI projects. |

### 5.4 Maintainability

| ID | Requirement |
| --- | --- |
| NFR-013 | C# code shall use block-scoped namespaces. |
| NFR-014 | C# code shall use Allman braces. |
| NFR-015 | C# files shall contain one public type per file. |
| NFR-016 | Private fields shall use underscore-prefixed naming. |
| NFR-017 | Executable entry points shall avoid top-level statements. |
| NFR-018 | `.csproj` files shall keep `PackageReference` entries in `ItemGroup` blocks that contain only package references. |
| NFR-019 | Internal and non-public types introduced for WP011 shall be treated as requiring the same developer-level documentation standard as public types when documentation is necessary to understand architecture or behavior. |

### 5.5 Testability

| ID | Requirement |
| --- | --- |
| NFR-020 | UI/client extraction logic shall be testable without starting the Aspire AppHost. |
| NFR-021 | UI/client extraction logic shall be testable using in-memory or fixture-based source repositories. |
| NFR-022 | Framework classification, graph fact emission, confidence assignment, stable-key behavior, evidence generation, redaction, deduplication, and unknown handling shall be directly testable. |
| NFR-023 | Tests shall not require running UI applications, browsers, device simulators, web servers, live APIs, network access, or platform-specific UI runtimes beyond static source analysis fixtures. |

## 6. Technical Requirements

### 6.1 Target Runtime and Project Placement

WP011 implementation shall use the repository-approved .NET target and the project layout created by WP001. The expected primary production projects are:

| Project | Responsibility |
| --- | --- |
| `Archon.Extractors.Ui` | Shared UI extraction abstractions, normalized UI graph fact construction, common markup helpers, metadata conventions, confidence rules, unknown handling, and cross-framework utilities. |
| `Archon.Extractors.Blazor` | Blazor `.razor` component, route, layout, parameter, injection, render-mode, authorization, component usage, API/client usage, and configuration usage extraction. |
| `Archon.Extractors.Razor` | Razor Pages and MVC Razor view, page model, handler, layout, partial, view component, tag helper, form, navigation, and controller/action linkage extraction. |
| `Archon.Extractors.WinForms` | Windows Forms project, form, user-control, designer, resource, control hierarchy, event wiring, binding, startup form, service usage, and data-access usage extraction. |
| `Archon.Extractors.Wpf` | WPF application, window, page, user-control, resource dictionary, style, template, binding, command, routed event, navigation, view-model, service usage, and data-access usage extraction. |
| `Archon.Extractors.WinUI` | WinUI application, window, page, user-control, resource, style, binding, command, navigation, app startup, packaging metadata, service usage, and data-access usage extraction. |
| `Archon.Extractors.Maui` | .NET MAUI application, page, view, Shell route, handler, resource, style, binding, command, view-model, platform-head, navigation, service usage, and data-access usage extraction. |
| `Archon.Extractors.Avalonia` | Avalonia AXAML application, window, user-control, resource, style, binding, command, view locator, ReactiveUI, view-model, navigation, service usage, and data-access usage extraction. |
| `Archon.Roslyn` and language-specific Roslyn projects | Shared semantic context, symbol resolution, invocation analysis, attribute analysis, source evidence projection, and generated-code handling. |
| `Archon.Application` | Shared extraction contracts, snapshot accumulation contracts, graph fact contracts, and orchestration interfaces. |
| `Archon.Api.Extraction` | Coordination of extractor execution through the established API-triggered extraction path. |

Expected corresponding test projects are:

| Test Project | Responsibility |
| --- | --- |
| `Archon.Extractors.Ui.Tests` | Shared UI graph fact construction, metadata, confidence, unknown, evidence, stable-key, and deduplication behavior. |
| `Archon.Extractors.Blazor.Tests` | Blazor component, route, layout, parameter, injection, authorization, render-mode, component usage, API/client usage, and configuration usage extraction. |
| `Archon.Extractors.Razor.Tests` | Razor Pages and MVC Razor view extraction, page model linkage, handlers, layouts, partials, tag helpers, forms, navigation, and controller/action linkage. |
| `Archon.Extractors.WinForms.Tests` | Windows Forms forms, user controls, designer files, resources, controls, event wiring, bindings, startup forms, service usage, and data-access usage. |
| `Archon.Extractors.Wpf.Tests` | WPF XAML windows, pages, resources, styles, templates, bindings, commands, routed events, navigation, view models, service usage, and data-access usage. |
| `Archon.Extractors.WinUI.Tests` | WinUI XAML, windows, pages, resources, styles, bindings, commands, navigation, packaging metadata, service usage, and data-access usage. |
| `Archon.Extractors.Maui.Tests` | .NET MAUI pages, views, Shell routes, handlers, resources, styles, bindings, commands, view models, platform heads, navigation, service usage, and data-access usage. |
| `Archon.Extractors.Avalonia.Tests` | Avalonia AXAML, windows, user controls, resources, styles, bindings, commands, view locators, ReactiveUI, view models, navigation, service usage, and data-access usage. |
| `Archon.Api.Extraction.Tests` | Pipeline participation, orchestration integration, warning/error propagation, and snapshot accumulation behavior. |

If WP001 uses different concrete project names, WP011 shall use the existing projects rather than creating duplicate responsibilities. The implementation shall preserve extractor-slice separation from host, infrastructure, and persistence projects.

### 6.2 Dependency Direction

WP011 must preserve Onion Architecture dependency direction:

- Domain must not depend on application, extractors, infrastructure, or hosts.
- Application may define contracts and ports but must not depend on infrastructure or hosts.
- Extractors may depend on application contracts and Roslyn abstractions according to existing solution direction.
- UI extraction logic belongs in extractor projects, not host, infrastructure, or API delivery projects.
- API extraction coordination may compose extractor execution but must not absorb framework-specific UI extraction logic.
- Infrastructure and hosts must not become a dumping ground for UI/client extraction behavior.

### 6.3 Markup and Artifact Analysis

The implementation shall analyze UI artifacts as source data. It shall not render UI, instantiate target application classes beyond safe static analysis models, invoke event handlers, execute user code, submit forms, or contact APIs.

Markup and artifact analysis shall preserve:

- Repository-relative source or artifact file path.
- UI framework and artifact kind.
- Project and target framework context.
- Component, page, view, window, control, route, resource, style, binding, command, event, navigation, and view-model identity where available.
- Source line span or artifact location where available.
- Confidence and detection mode.
- Unknown reason where a UI fact is partial.

### 6.4 Stable Key Requirements

WP011 stable keys shall be deterministic and shall not depend on database IDs, absolute developer machine paths, enumeration order, generated temporary paths, runtime state, UI rendering output, browser state, or external service availability.

Stable-key inputs shall use normalized repository-relative paths, project keys, target framework, UI framework, artifact kind, type names, route templates, component names, view names, control names, binding paths, command names, event names, navigation target hints, and source locations where appropriate. Unknown target keys shall use the source project key plus framework, artifact kind, normalized call-site or markup location, and unknown category.

### 6.5 Metadata Requirements

UI metadata fields shall use stable API-friendly lower camel case names, including `uiFramework`, `uiArtifactKind`, `projectKey`, `targetFramework`, `language`, `sourcePath`, `typeName`, `methodName`, `componentName`, `pageName`, `viewName`, `windowName`, `layoutName`, `routeTemplate`, `routeParameter`, `controlName`, `resourceKey`, `styleKey`, `bindingPath`, `commandName`, `eventName`, `navigationTarget`, `viewModelType`, `renderMode`, `hostingModel`, `platformHead`, `packageIdentity`, `detectionMode`, `confidenceReason`, and `unknownReason`.

Framework-specific distinctions shall be represented as metadata on the common graph node kinds unless the graph model already defines a more precise node kind. The implementation shall not create ad hoc graph node kinds outside the domain model established by WP002.

### 6.6 Documentation Pass

WP011 shall include a documentation pass covering:

- Supported Blazor extraction patterns.
- Supported Razor Pages and MVC Razor view extraction patterns.
- Supported Windows Forms extraction patterns.
- Supported WPF extraction patterns.
- Supported WinUI extraction patterns.
- Supported .NET MAUI extraction patterns.
- Supported Avalonia extraction patterns.
- Common UI graph node and relationship mapping.
- Confidence and unknown-state behavior.
- Safe static-analysis constraints.
- Redaction behavior for markup, configuration, event, and form-related evidence.
- Testing and fixture guidance for UI/client extraction.
- Limitations and known unsupported patterns, expressed as current implementation constraints rather than deferred mandatory requirements.

Internal and non-public implementation types introduced for WP011 shall be treated as requiring the same developer-level documentation standard as public types when documentation is necessary to understand the architecture or behavior.

## 7. Exclusions

WP011 shall not implement:

- Archon Discovery UI host, pages, components, assets, dashboard, explorer, graph view, prompt panel, evidence viewer, hotlist viewer, UI route explorer, or any human-facing product UI surface.
- JavaScript or TypeScript frontend framework extraction, including React, Angular, Vue, Svelte, or frontend build-pipeline analysis.
- API query endpoints for browsing UI/client facts; those belong to the query API work package.
- MCP tools, MCP resources, MCP prompts, or Copilot workflows.
- Rule catalog evaluation, hotlist generation, finding suppression, or rule management.
- Runtime endpoint extraction except where WP011 consumes existing runtime facts for UI-to-endpoint correlation.
- Data-access extraction except where WP011 consumes existing data-access facts for UI-to-data impact paths.
- External integration extraction except where WP011 consumes existing integration facts for UI-to-service impact paths.
- Markdown export.
- Snapshot diff.
- Direct Neo4j writes from extractor projects.
- Live application execution, UI rendering, browser automation, screenshot generation, device simulator execution, form submission, event-handler execution, API calls, or service availability checks.
- Automatic remediation, UI migration, code rewriting, view-model generation, component conversion, or replacement UI generation.

## 8. Data and Integration Requirements

### 8.1 Required Graph Facts

WP011 shall contribute graph facts that fit the existing Archon graph model:

| Fact Type | Required Treatment |
| --- | --- |
| UI application | Represent as `UiApplication` nodes with framework, project, target framework, hosting/startup metadata, evidence, confidence, and unknowns where applicable. |
| UI component | Represent as `UiComponent` nodes for Blazor components and component-like UI constructs with framework, artifact kind, component name, file path, metadata, evidence, confidence, and unknowns. |
| UI page | Represent as `UiPage` nodes for Razor Pages, WPF pages, WinUI pages, MAUI pages, and page-like UI constructs. |
| UI view | Represent as `UiView` nodes for MVC Razor views, XAML/AXAML views, and view-like artifacts. |
| UI layout | Represent as `UiLayout` nodes for Blazor layouts and Razor layouts. |
| UI route | Represent as `UiRoute` nodes with route templates, parameters, source artifact, framework, metadata, evidence, confidence, and unknowns. |
| UI control | Represent as `UiControl` nodes for controls where deterministic evidence exists. |
| UI resource | Represent as `UiResource` nodes for resource dictionaries, `.resx` resources, markup resources, and framework resources. |
| UI style | Represent as `UiStyle` nodes for styles and templates where represented as styles. |
| View model | Represent as `ViewModel` nodes where direct or convention-supported evidence exists. |
| Command | Represent as `Command` nodes where command definitions or usage are detected. |
| Binding | Represent as `Binding` nodes with binding path, source context, framework, evidence, confidence, and unknowns where applicable. |
| Relationships | Represent declarations, usage, layout usage, control usage, resource usage, style usage, binding, command usage, view-model usage, navigation, UI event handling, API calls, configuration usage, and dependencies through the standard edge kinds. |
| Evidence | Link file, line span, symbol, markup directive, designer code, resource entry, snippet hash, snippet preview, confidence, and redaction metadata. |
| Unknown | Represent unresolved components, routes, bindings, commands, navigation targets, view models, controls, resources, styles, and framework classification with explicit unknown reason. |

### 8.2 Evidence Requirements

Evidence shall include enough information for later API and MCP consumers to show why the fact exists:

- Repository-relative file path.
- Line and column span where available.
- Artifact path or generated file location where available.
- Symbol name where available.
- Containing symbol where available.
- UI framework and artifact kind.
- Directive, markup element, attribute, property, binding path, command name, event name, route template, control name, resource key, style key, or navigation target where relevant.
- Snippet hash.
- Snippet preview with secrets redacted.
- Detection mode.
- Confidence.

### 8.3 Integration with Earlier Work Packages

WP011 shall integrate with earlier outputs as follows:

- Use project and package facts from WP005 to identify candidate UI technologies, target frameworks, project formats, application types, package references, and generated file locations.
- Use semantic symbol facts from WP006 to identify types, methods, attributes, invocations, inheritance, interface implementation, event handlers, commands, view models, and code-behind dependencies.
- Use configuration facts from WP007 to link UI facts to configuration keys and configuration-dependent API/client usage.
- Use dependency-injection facts from WP007 to correlate injected services, view-model services, typed clients, and UI startup registration where available.
- Use runtime facts from WP008 to correlate UI routes, Razor pages, MVC views, forms, navigation, and controller/action linkages where evidence supports the link.
- Use data-access facts from WP009 to identify UI-to-data-access impact paths through code-behind, components, handlers, view models, and commands.
- Use external integration facts from WP010 to identify UI-to-service/API impact paths through components, handlers, view models, commands, and service clients.
- Reuse existing nodes and relationships when earlier work packages already emitted equivalent facts.

### 8.4 Integration with Later Work Packages

WP011 output shall be shaped so later work packages can:

- Query UI facts by project, framework, component, page, view, route, control, binding, command, view model, navigation target, service dependency, API dependency, data-access dependency, and snapshot.
- Assess change impact from backend services, APIs, data-access nodes, configuration keys, commands, and view models to user-facing screens or components.
- Feed rule evaluation and hotlist findings for legacy UI technologies, unsupported frameworks, UI-to-data coupling, hard-coded navigation/configuration, code-behind risk, and high-coupling UI patterns.
- Expose evidence-backed UI/client facts through MCP tools and resources.
- Include UI/client maps in generated markdown.
- Participate in metrics, hotspots, architecture rules, and snapshot diff.

## 9. Test Requirements

### 9.1 Required Test Coverage

| Test Area | Required Verification |
| --- | --- |
| Pipeline integration | UI/client extractors run through the existing extraction orchestration path and emit snapshot facts. |
| Blazor | `.razor` files, routes, layouts, injections, parameters, cascading parameters, event callbacks, render fragments, authorization, render modes, hosting, component references, forms, API/client usage, and configuration usage are detected. |
| Razor Pages and MVC views | `.cshtml`, Razor Pages, MVC views, view imports, view start, layouts, partials, view components, tag helpers, page models, handlers, forms, anchor tag helpers, routes, authorization, and controller/action linkage are detected. |
| Windows Forms | Project classification, startup forms, forms, user controls, designer files, resources, control hierarchy, event handlers, data bindings, service usage, and data-access usage are detected. |
| WPF | Application definitions, windows, pages, user controls, resources, styles, templates, bindings, commands, routed events, navigation, view models, service usage, and data-access usage are detected. |
| WinUI | Project classification, app startup, windows, pages, user controls, resources, styles, bindings, commands, navigation, packaging metadata, service usage, and data-access usage are detected. |
| .NET MAUI | `UseMaui`, `MauiProgram`, pages, views, Shell, Shell routes, handlers, resources, styles, bindings, commands, view models, platform heads, navigation, service usage, and data-access usage are detected. |
| Avalonia | Package references, AXAML, `App.axaml`, windows, user controls, resources, styles, bindings, commands, view locators, ReactiveUI, view models, navigation, service usage, and data-access usage are detected. |
| Graph facts | Required UI nodes and relationship kinds are emitted as applicable. |
| Evidence | Every non-derived fact has source evidence with file path, artifact location or line span where available, snippet hash, and redacted preview. |
| Confidence | High, medium, and low confidence cases are assigned consistently. |
| Unknowns | Dynamic routes, unresolved components, unresolved bindings, dynamic resources, runtime navigation, convention-only view models, and ambiguous framework classification produce explicit unknowns. |
| Redaction | Tokens, passwords, API keys, connection strings, authorization headers, credential-like values, and secret-like form/configuration values are not present in metadata, evidence previews, warnings, errors, logs, or test output. |
| Deduplication | Duplicate UI facts from project metadata, markup, code-behind, generated files, runtime facts, and semantic facts do not create duplicate graph facts. |
| C# support | C# UI extraction patterns are covered. |
| VB.NET support | VB.NET Windows Forms, WPF, classic Razor, and supported UI extraction patterns are covered where Roslyn and project artifacts support semantic detection. |

### 9.2 Test Fixtures

Tests shall include fixture repositories or in-memory source sets for:

- Blazor Server, WebAssembly, Web App, and Hybrid patterns where feasible.
- Blazor components with route directives, layouts, injections, parameters, cascading parameters, event callbacks, render fragments, authorization, render modes, forms, component references, API/client usage, and configuration usage.
- Razor Pages with page models, handlers, forms, tag helpers, layouts, partials, and route conventions.
- MVC Razor views with controllers, actions, layouts, partials, view components, tag helpers, and view-to-controller/action linkage.
- Windows Forms application with startup form, forms, user controls, designer partial classes, `.resx` resources, control hierarchy, event handlers, and data bindings.
- WPF application with application definition, windows, pages, user controls, resource dictionaries, styles, templates, bindings, commands, routed events, navigation, and view models.
- WinUI application with windows, pages, resources, styles, bindings, commands, navigation, startup, and packaging metadata.
- .NET MAUI application with pages, views, Shell routes, handlers, resources, styles, bindings, commands, view models, platform-specific heads, and navigation.
- Avalonia application with AXAML, windows, user controls, resources, styles, bindings, commands, view locators, ReactiveUI usage, view models, and navigation.
- UI code-behind or view-model examples that use services, configuration, APIs, external integrations, and data access.
- Dynamic or unresolved examples for routes, controls, resources, bindings, navigation, view models, and framework classification.
- Duplicate fact examples where project metadata, markup, code-behind, generated files, and semantic facts describe the same UI artifact.
- Mixed C# and VB.NET examples where feasible.

### 9.3 Test Constraints

Automated verification must not start the Aspire AppHost as a blocking process. Tests should use extractor-level fixtures, application-layer orchestration seams, and targeted integration tests. For this work package, the full test suite should not be run unless explicitly requested; run targeted WP011 tests and a solution build as final validation.

## 10. Acceptance Criteria

WP011 is accepted when all of the following are true:

1. .NET UI/client extractors are wired into the existing extraction orchestration path.
2. Blazor `.razor` components, routes, layouts, injections, parameters, cascading parameters, event callbacks, render fragments, authorization, render modes, hosting classification, component usage, API/client usage, and configuration usage are detected where evidence exists.
3. Razor Pages and MVC Razor views, view imports, view start, layouts, partials, view components, tag helpers, page models, handlers, forms, route metadata, authorization metadata, and controller/action linkage are detected where evidence exists.
4. Windows Forms applications, startup forms, forms, user controls, designer files, resources, controls, event wiring, data bindings, service usage, and data-access usage are detected where evidence exists.
5. WPF applications, windows, pages, user controls, resource dictionaries, styles, templates, bindings, commands, routed events, navigation, view models, service usage, and data-access usage are detected where evidence exists.
6. WinUI applications, windows, pages, user controls, resources, styles, bindings, commands, navigation, app startup, packaging metadata, service usage, and data-access usage are detected where evidence exists.
7. .NET MAUI applications, pages, views, Shell routes, handlers, resources, styles, bindings, commands, view models, platform heads, navigation targets, service usage, and data-access usage are detected where evidence exists.
8. Avalonia applications, AXAML views, windows, user controls, resources, styles, bindings, commands, view locators, ReactiveUI usage, view models, navigation targets, service usage, and data-access usage are detected where evidence exists.
9. Required UI-related node kinds are emitted through the snapshot contract where evidence exists.
10. Required UI-related relationship kinds are emitted through the snapshot contract where evidence exists.
11. UI facts can support later API and MCP queries by project, framework, route, component, page, view, control, binding, command, view model, navigation target, configuration key, API dependency, service dependency, and data-access dependency.
12. Unknowns and confidence are explicit for unresolved or inferred UI/client facts.
13. Secret-like markup, form, configuration, API, authentication, and credential values are redacted before being stored or exposed in evidence, metadata, warnings, errors, or logs.
14. Tests cover every .NET UI technology listed by `docs/foundation/archon_full_concept_brief.md` section 19.
15. Documentation is updated for supported .NET UI/client extraction behavior and validation.
16. No Archon Discovery UI implementation is introduced.
17. No dashboard, explorer, graph page, prompt panel, evidence viewer, hotlist viewer, human-facing UI component, or front-end asset is created.
18. The solution builds successfully.
19. Targeted WP011 tests pass.

## 11. Risks and Decisions

### 11.1 Risks

| Risk | Impact | Mitigation |
| --- | --- | --- |
| UI frameworks use dynamic routes, bindings, resources, and navigation. | Overconfident inference could produce misleading graph facts. | Preserve dynamic indicators, confidence metadata, and explicit unknowns rather than inventing targets. |
| XAML/AXAML and designer files can be large and generated. | Extraction could be slow or noisy. | Use bounded parsing, artifact caching, recursion safeguards, and stable-key deduplication. |
| View-model relationships are often convention-based. | False positive view-model links could reduce trust. | Prefer direct DataContext, locator, DI, or binding evidence; assign lower confidence to convention-only links. |
| UI-to-service and UI-to-data impact paths may cross multiple layers. | Missing prior facts could make UI dependencies appear incomplete. | Correlate only through existing semantic, integration, configuration, runtime, and data-access facts; emit unknowns when links cannot be proven. |
| Markup and configuration can contain secret-like values. | Persisted evidence could expose credentials. | Redact snippets, metadata, warnings, errors, and logs before storage. |
| Frameworks overlap in file extensions and concepts. | Classification may be ambiguous. | Use package/project metadata and symbols first, file patterns second, and unknown framework classification when evidence is insufficient. |
| VB.NET UI projects may differ from C# conventions. | Mixed-language estates may have uneven coverage. | Use Roslyn Visual Basic semantic support and artifact parsing where available; document/test supported parity. |

### 11.2 Decisions

| Decision | Rationale |
| --- | --- |
| Use a single WP011 specification document. | User requested a single markdown document spec for WP011. |
| Create the documentation under `docs/011-.NET-Client-and-UI-Technology-Extraction-for-API-MCP-Facts/`. | This is the next incremental documentation work-package folder after WP010. |
| Do not create separate overview and component spec documents. | The user explicitly requested a single markdown document, overriding the multi-document collaboration pattern for this output. |
| Treat .NET UI extraction as backend graph extraction only. | The work-package sequence explicitly excludes Archon Discovery UI while requiring UI technologies in analyzed repositories to become API/MCP facts. |
| Keep JavaScript and TypeScript frontend frameworks out of WP011. | The source brief states these frameworks are explicitly out of scope for the initial UI extraction model. |
| Represent framework-specific concepts through common UI graph nodes and metadata. | The source brief requires normalization into common graph concepts such as applications, components, pages, views, layouts, routes, controls, resources, styles, view models, commands, bindings, navigation edges, API calls, and evidence. |
| Preserve explicit unknowns rather than suppressing partial UI facts. | The source brief requires unknowns to be represented instead of omitted or invented. |
| Use lower camel case metadata field names. | Stable API-friendly metadata names support later API and MCP consumption. |
| Use deterministic UI stable-key inputs. | Stable keys must remain independent of database IDs, absolute developer paths, enumeration order, runtime state, UI rendering output, browser state, and external service availability. |

## 12. Manual Verification Requirements

The implementation documentation for WP011 shall instruct a developer to verify the work package by:

1. Restoring and building the solution.
2. Running targeted tests for shared UI graph fact construction, metadata, confidence, unknowns, evidence, stable keys, and deduplication.
3. Running targeted tests for Blazor extraction.
4. Running targeted tests for Razor Pages and MVC Razor view extraction.
5. Running targeted tests for Windows Forms extraction.
6. Running targeted tests for WPF extraction.
7. Running targeted tests for WinUI extraction.
8. Running targeted tests for .NET MAUI extraction.
9. Running targeted tests for Avalonia extraction.
10. Running targeted tests for UI-to-configuration, UI-to-service/API, and UI-to-data-access correlation.
11. Running targeted extraction integration tests through the API extraction module seam without launching the blocking Aspire AppHost process.
12. Inspecting representative snapshot output to confirm required UI nodes and relationship facts are emitted where applicable.
13. Confirming evidence includes redacted snippets and source locations.
14. Confirming secret-like markup, form, configuration, API, authentication, and credential values are not present in test output, logs, warnings, errors, metadata, or evidence previews.
15. Confirming no Archon Discovery UI resource, page, component, front-end asset, dashboard, explorer, graph page, or prompt panel was created.

Automated validation instructions shall explicitly state not to run the AppHost as a blocking process during agent-driven verification.

## 13. Traceability Matrix

| Source Requirement | Specification Coverage |
| --- | --- |
| Treat .NET UI technologies as first-class architecture facts | Sections 1, 3, 4, 8, 10 |
| Normalize UI findings into common graph concepts | Sections 3.8, 4.9, 8.1, 11.2 |
| Detect Blazor applications, components, routes, layouts, parameters, injected services, authorization, render modes, component usage, API/client usage, and configuration usage | Sections 3.1, 4.2, 8, 9, 10 |
| Detect Razor Pages and Razor views, layouts, partials, view components, tag helpers, page models, handlers, forms, route metadata, and controller/action linkage | Sections 3.2, 4.3, 8, 9, 10 |
| Detect Windows Forms applications, forms, user controls, designer files, resources, controls, events, bindings, startup forms, service usage, and data-access usage | Sections 3.3, 4.4, 8, 9, 10 |
| Detect WPF applications, windows, pages, user controls, resources, styles, templates, bindings, commands, events, navigation, view models, service usage, and data-access usage | Sections 3.4, 4.5, 8, 9, 10 |
| Detect WinUI applications, windows, pages, user controls, resources, styles, bindings, commands, navigation, startup, packaging metadata, service usage, and data-access usage | Sections 3.5, 4.6, 8, 9, 10 |
| Detect .NET MAUI applications, pages, views, Shell routes, handlers, resources, styles, bindings, commands, view models, platform heads, navigation, service usage, and data-access usage | Sections 3.6, 4.7, 8, 9, 10 |
| Detect Avalonia applications, AXAML views, windows, user controls, resources, styles, bindings, commands, view locators, ReactiveUI, view models, navigation, service usage, and data-access usage | Sections 3.7, 4.8, 8, 9, 10 |
| Populate UI-related node kinds | Sections 4.9, 8.1, 10 |
| Populate UI-related edge kinds | Sections 4.9, 8.1, 10 |
| Represent unknowns explicitly rather than inventing facts | Sections 4.10, 5.1, 8.1, 10, 11 |
| Provide tests for every .NET UI technology listed in section 19 | Sections 9, 10 |
| Repository documentation updated | Sections 6.6, 12, 10 |
| No Archon Discovery UI implementation | Sections 1.3, 7, 10, 12 |

## 14. Open Questions

No open questions remain for WP011. Stable-key inputs, graph relationship direction, metadata field names, backend-only scope, JavaScript/TypeScript frontend exclusion, safe static-analysis constraints, and redaction expectations are recorded as definitive decisions in section 11.2.

## 15. Change Log

| Date | Change |
| --- | --- |
| 2026-05-23 | Created initial single-document WP011 specification from `docs/foundation/work-packages.md` and the Archon source brief. |
