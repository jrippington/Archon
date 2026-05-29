# Archon Wiki

Archon is a .NET architecture-intelligence platform. The current repository includes API and MCP hosts, an Aspire AppHost for local composition, extraction and analysis pipelines, Neo4j persistence, controlled query APIs, and the browser-facing ArchonExplorer workbench foundation. Detailed contributor guidance belongs on the topic pages linked below; this landing page is only the reader path and high-level orientation.

This wiki is the contributor-facing source of truth for current behavior, terminology, validation workflows, and repository operating model. Work-package specs and plans under `docs/` explain how work is planned and recorded; the wiki explains how contributors should understand and work with the repository now.

## Recommended reading paths

### New contributor orientation

Start here if you need the mental model before working on code:

1. [Solution architecture](solution-architecture.md) explains project families, Onion Architecture, dependency boundaries, and project identity.
2. [Runtime foundation](runtime-foundation.md) explains service defaults, health probes, the Aspire AppHost, local Neo4j composition, current MCP host foundations, and current runtime extraction concepts.
3. [Graph domain model](graph-domain-model.md) explains controlled values, stable keys, graph facts, evidence, confidence, unknown state, and snapshot accumulation.
4. [Roslyn semantic extraction](roslyn-semantic-extraction.md) explains the current compiler-backed C# and VB.NET declaration and relationship extraction slice, semantic stable keys, evidence, confidence, and dependency facts.
5. [Configuration and dependency-injection extraction](configuration-and-dependency-injection-extraction.md) explains the current Microsoft DI registration slice, legacy container/service-locator/manual-factory detection, modern appsettings/options extraction, service-registration and configuration-key graph facts, evidence, stable keys, redaction, and validation path.
6. [Data access extraction](data-access-extraction.md) explains the current LINQ to SQL, EF6, EF Core, ADO.NET, typed DataSet, and raw SQL extraction slice, graph facts, evidence, redaction, unknowns, and validation path.
7. [External integration extraction](external-integration-extraction.md) explains the current WP010 foundation seam, concrete integration detectors, external services, queues, topics, integration relationships, stable keys, evidence, unknowns, redaction, internal service correlation, and no-live-call safety.
8. [.NET UI and client extraction](dotnet-ui-client-extraction.md) explains the current Blazor `.razor`, Razor Pages, MVC Razor `.cshtml`, Windows Forms, WPF, WinUI, .NET MAUI, and Avalonia extraction behavior, evidence, unknown, redaction, and no-rendering/no-designer/no-XAML-runtime extraction boundary.
9. [Rule catalog and rule engine](rule-catalog-and-rule-engine.md) explains repository-authored JSON rules, copied-output runtime loading, validation diagnostics, disabled rules, built-in rule content, versioned catalog persistence, extraction-stage evaluation, finding construction, history, and suppression.
10. [Controlled analysis queries](hotlist-and-findings.md) explains controlled dashboard summary, project catalogue, project detail, bounded graph traversal, dependency path, graph neighbourhood, symbol search/detail/usage, runtime endpoint/controller/entry-point/worker, data-access/configuration/integration/UI-technology fact, evidence detail, related evidence, rule catalog, hotlist, finding detail, finding history, suppression, snapshot metric, cycle, hotspot, architecture-rule result, snapshot diff, latest-to-previous diff, cross-domain search, controlled management, health, and readiness API behavior.
11. [MCP tool reference](mcp-tool-reference.md) explains the current read-only MCP capability inventory, tool/resource/prompt contracts, evidence-backed response examples, URI inputs, outputs, limits, unknowns, truncation, prompt workflow safety, troubleshooting categories, ambiguity semantics, and safe follow-ups; pair it with [runtime foundation](runtime-foundation.md) for MCP host setup, local verification, authorization, audit, redaction, prompt-injection handling, and readiness behavior.
12. [Neo4j persistence foundation](neo4j-persistence-foundation.md) explains schema initialization, guarded recreation, snapshot persistence, relationship nodes, and persisted analysis outputs.
13. [API extraction workflow](api-extraction-workflow.md) explains `POST /extractions`, status polling, validation boundaries, run lifecycle state, and the initial scheduler/run-history seams.
14. [ArchonExplorer frontend foundation](archonexplorer-frontend-foundation.md) explains the current Vite, React, TypeScript, npm, TanStack Query provider, shadcn-compatible component foundation, visible workbench shell, local activity and tab state, layout preference persistence, bottom-panel placeholders, theme affordance, safe API configuration indicator, and Aspire-hosted local Vite resource.
15. [ArchonExplorer Snapshot Workspace](archonexplorer-extraction-center.md) explains the current API-backed Snapshot Workspace extraction start, update status, history, selected-run polling, duplicate-request, and produced-snapshot placeholder workflows.

### Build and validation path

Use [validation and test workflows](validation-and-test-workflows.md) when you need commands for restore, build, targeted tests, MCP validation, Testcontainers-based Neo4j validation, boundary checks, frontend validation, focused Playwright shell validation, or manual AppHost verification.

### Documentation and wiki maintenance path

Use [work-package documentation workflow](work-package-documentation-workflow.md) when planning, executing, or reviewing a work package. It explains how specs, plans, wiki updates, glossary entries, page-structure decisions, and final wiki review records fit together.

### Terminology lookup

Use the [glossary](glossary.md) for repository-specific terms such as AppHost, composition root, stable key, semantic stable key, semantic evidence, bounded graph traversal, dependency path, graph neighbourhood, controlled management operation, retention boundary, audit-ready metadata, fingerprint, evidence-first, relationship-node pattern, readiness, liveness, Workbench shell, command palette, notification host, Playwright, and Testcontainers.

## Current capability summary

Archon currently provides extraction, analysis, persistence, API, MCP, and browser-shell foundations that are documented in the topic pages above. The API and MCP hosts expose controlled, evidence-backed behavior; the extraction pipeline contributes static architecture facts and analysis outputs; Neo4j persistence stores approved graph shapes; and ArchonExplorer is currently a browser workbench shell that lands in the API-backed Snapshot Workspace for starting runs, reviewing update status and history, monitoring selected/background runs, duplicating safe request values, and recognizing produced snapshot identities.

## Wiki maintenance standard

Do not add detailed architecture, runtime, setup, domain, persistence, validation, or workflow guidance directly to this landing page. Add or update the relevant topic page instead, and link it from the appropriate reader path. If no existing topic page fits, create one.
