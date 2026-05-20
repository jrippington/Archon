# Archon
## A Roslyn-Powered Architecture Operating System for Legacy and Modern .NET Estates

**Document purpose:**  
This document is a detailed product and technical brief for **Archon**. It is intended to be handed to GitHub Copilot, developers, architects, and technical leads as the starting point for generating detailed implementation specifications, backlog items, architecture decision records, and prototype code.

**Document status:** First consolidated concept brief.  
**Primary goal:** Build a deterministic, evidence-backed architecture intelligence platform for large .NET estates, especially those containing a mixture of modern and legacy technologies.

---

# 1. Executive Summary

Archon is a **.NET-first Architecture Operating System**.

It is designed for organisations with large, long-lived .NET codebases that contain a mixture of modern and legacy technologies:

- modern .NET applications
- .NET Framework applications
- C#
- VB.NET
- ASP.NET Core
- classic ASP.NET
- Blazor
- Razor Pages and Razor views
- Windows Forms
- WPF
- WinUI
- .NET MAUI
- Avalonia
- Web APIs
- Web Forms
- console workers
- Windows-service-style workers
- LINQ to SQL
- ADO.NET
- Entity Framework Classic / EF6
- Entity Framework Core
- shared databases
- old project formats
- old configuration models
- high coupling
- unclear ownership
- undocumented runtime behaviour

The core idea is simple:

> **Archon extracts architectural facts deterministically from code through an API-driven analysis workflow, stores them in Neo4j as a native architecture graph, exposes them through a discovery UI, and makes them available to Copilot and other AI assistants through an MCP server.**

Archon does not ask AI to guess the architecture.

Instead:

> **Roslyn extracts facts.  
> Neo4j stores architectural memory as a native graph.  
> The UI makes it explorable.  
> MCP makes it available to AI.  
> Copilot explains, reasons, and assists using evidence-backed context.**

---

# 2. Why Archon Exists

Large enterprise .NET estates often contain systems that are business-critical but difficult to understand.

Typical symptoms include:

- no single person understands the whole system
- documentation is stale
- architecture diagrams are out of date
- project dependencies are unclear
- old applications still depend on shared libraries
- database coupling is hidden
- the same tables are used from many projects
- legacy data access exists alongside newer patterns
- AI assistants lack architectural context
- refactoring feels risky
- modernization planning is slow and speculative

The common meeting scenario is:

> “We need to change this part of the system.”  
> “What depends on it?”  
> “What database tables does it touch?”  
> “Which APIs or workers use it?”  
> “Will this break anything?”  
>  
> Silence. Then guesses.

This is not a failure of the team. It is a failure of visibility.

Archon exists to turn architectural visibility into an operational capability.

---

# 3. Core Positioning

Archon should be positioned as:

> **A Roslyn-powered Architecture Operating System for the .NET estate you actually have.**

Alternative positioning lines:

> **Compiler-grade architectural intelligence for legacy and modern .NET systems.**

> **Archon does not assume your system is clean. It helps you understand the system you actually have.**

> **Archon gives Copilot architectural memory.**

> **Archon is for the codebases organisations actually have, not the clean architectures they wish they had.**

---

# 4. Why “Architecture Operating System”?

Traditional architecture documentation tends to be:

- written once
- read occasionally
- slowly drifting out of date
- passive

A knowledge base stores information. Documentation describes a system. An architecture repository organises artefacts.

Archon should do more.

An operating system does not merely store information. It runs, coordinates, governs interaction, enforces rules, provides services to other processes, and mediates access to resources.

That maps well to what Archon should do for architecture.

Archon should:

- continuously extract facts from code
- store them in a queryable architecture model
- make change impact visible
- detect legacy and modernization signals
- provide discovery tools for humans
- expose evidence-backed architecture context to Copilot
- support “what if?” and “how do we?” reasoning
- guide safe refactoring and new development

The key idea:

> **We are not documenting the architecture.  
> We are creating a system that can reason about it, evolve with it, and guide its transformation.**

---

# 5. Product Principles

## 5.1 Deterministic Facts First

Archon should not rely on AI as the primary source of architectural truth.

The primary facts should be extracted deterministically from:

- solution files
- project files
- source code
- compiler symbols
- semantic references
- configuration files
- database mapping files
- generated code
- deployment artefacts
- package metadata
- pipelines

AI should explain, summarise, and reason over facts. It should not invent facts.

## 5.2 Evidence Everywhere

Every architectural statement should be traceable to evidence.

Examples of evidence:

- project file path
- source file path
- line span
- symbol name
- method name
- attribute
- package reference
- appsettings key
- DBML file
- generated designer file
- SQL script
- pipeline file

Example:

```text
Fact:
Customer.Api references Customer.Application.

Evidence:
src/Customer.Api/Customer.Api.csproj
<ProjectReference Include="..\Customer.Application\Customer.Application.csproj" />
```

## 5.3 Unknowns Are Valuable

Archon should explicitly capture unknowns.

Examples:

```text
Unknown:
Unable to determine the target database table because SQL is dynamically constructed.

Unknown:
Unable to determine runtime endpoint base URL because it is supplied by environment variable.

Unknown:
Reflection call detected; target method could not be resolved statically.
```

Unknowns are not failures. They are discovery items.

## 5.4 Legacy Is First-Class

Archon should not focus only on modern .NET.

It must understand legacy .NET because that is where modernization risk lives.

First-class legacy support should include:

- VB.NET
- .NET Framework
- old-style `.csproj` / `.vbproj`
- `packages.config`
- `web.config`
- `app.config`
- LINQ to SQL
- `.dbml`
- generated designer files
- ADO.NET
- typed DataSets
- Web Forms
- classic ASP.NET MVC
- old ASP.NET Web API
- WCF
- ASMX
- Windows services
- COM interop
- old DI containers

## 5.5 .NET-First Is a Strength

Archon is not trying to be a generic enterprise architecture tool.

It should be unapologetically .NET-first.

This allows deep understanding of:

- Roslyn semantic models
- C# and VB.NET syntax
- MSBuild
- NuGet
- project references
- ASP.NET Core conventions
- classic ASP.NET conventions
- Blazor and Razor component conventions
- Windows Forms designer and code-behind conventions
- XAML-based UI conventions for WPF, WinUI, .NET MAUI, and Avalonia
- `IServiceCollection`
- dependency injection
- `IConfiguration`
- `IOptions<T>`
- hosted services
- controllers
- minimal APIs
- Entity Framework Classic / EF6
- Entity Framework Core
- LINQ to SQL
- old configuration patterns

Archon may later support other artefacts, but its deepest semantic model should be .NET.

---

# 6. External Reference Points

## 6.1 Roslyn

Roslyn is the .NET Compiler Platform. It exposes APIs for analysing C# and Visual Basic codebases, including syntax and semantic analysis.

References:

- Roslyn SDK overview: https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/
- Roslyn workspace model: https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/work-with-workspace
- Roslyn semantic model: https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/work-with-semantics
- Roslyn compiler API model: https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/compiler-api-model

## 6.2 .NET Obsolete APIs and Lifecycle

Archon’s modernization hotlist should be seeded from Microsoft lifecycle and obsoletion guidance.

References:

- .NET obsolete features / SYSLIB diagnostics: https://learn.microsoft.com/en-us/dotnet/fundamentals/syslib-diagnostics/obsoletions-overview
- .NET Framework support policy: https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-framework
- Microsoft .NET Framework lifecycle: https://learn.microsoft.com/en-us/lifecycle/products/microsoft-net-framework

## 6.3 MCP

Archon should expose an MCP server so AI assistants can query the architecture model.

References:

- Model Context Protocol specification: https://modelcontextprotocol.io/specification
- MCP prompts concept: https://modelcontextprotocol.io/specification/2025-03-26/server/prompts

---

# 7. Roslyn and the Architecture Semantic Graph

OpenRewrite has the concept of a “Lossless Semantic Tree”.

For Archon, the equivalent concept should be:

> **Roslyn gives us full-fidelity syntax trees plus compiler-grade semantic models. Archon projects those into a persistent Architecture Semantic Graph.**

Roslyn does not expose one single object called a Lossless Semantic Tree. Instead, it provides:

- syntax trees
- syntax nodes
- tokens
- trivia
- semantic models
- symbols
- compilations
- workspaces
- solutions
- projects
- documents

For C# and VB.NET, Roslyn gives enough information to understand:

- syntax structure
- comments and trivia
- identifiers
- symbols
- types
- method calls
- inheritance
- interface implementations
- project references
- metadata references
- compiler diagnostics

Archon should not persist Roslyn syntax trees directly.

Instead:

```text
Roslyn Workspace / Compilation / SemanticModel
        ↓
API-triggered extraction pipeline
        ↓
Architecture Nodes + Edges + Evidence + Metrics
        ↓
Neo4j
```

Architecture is graph-shaped, not tree-shaped.

Therefore the core persisted model should be an **Architecture Semantic Graph**.

---

# 8. High-Level System Architecture

```text
.NET repositories / solutions
        ↓
POST /extractions request
        ↓
Archon API extraction module
        ↓
Neo4j Architecture Graph
        ↓
Archon API query and management modules
        ↓
Discovery UI
        ↓
MCP Server
        ↓
Copilot / IDE agents / AI assistants
```

## 8.1 Component Responsibilities

### Archon API Host

Hosts the HTTP surface for extraction, query, management, UI backend, and MCP-facing access.

Responsible for:

- accepting extraction requests that POST a repository root directory and one or more solution paths
- validating request shape, paths, and execution policy before analysis begins
- delegating extraction work to the extraction module
- delegating query work to the query module
- delegating administrative operations to the management module
- coordinating authentication, authorization, validation, telemetry, and host composition

### Archon API Extraction Module

Loads and analyses repositories and solutions when invoked through the API.

Responsible for:

- reading the submitted repository root directory
- reading the submitted solution path list
- loading projects with Roslyn
- analysing C# and VB.NET
- detecting project references
- detecting package references
- resolving symbols
- extracting architecture facts
- detecting legacy technologies
- producing findings
- writing a snapshot to Neo4j

### Neo4j Architecture Graph

Stores the architecture semantic graph natively as labelled nodes and typed relationships:

- snapshots
- repositories
- solutions
- project, package, runtime, and data-access nodes
- graph nodes
- graph relationships
- evidence
- findings
- metrics
- rules
- hotlist findings
- generated summaries

### Archon API Query Module

Provides query access over the architecture model.

Responsible for:

- project catalogue queries
- dependency queries
- graph neighbourhood queries
- snapshot diff queries
- evidence lookup
- hotlist reports
- MCP backend access
- markdown export access

### Archon API Management Module

Provides operational and administrative API capabilities.

Responsible for:

- repository registration and metadata management
- solution registration and metadata management
- snapshot lifecycle and retention controls
- rule catalog visibility and enablement controls
- extraction run history
- health and readiness views for graph-backed services
- controlled maintenance operations

### Archon Discovery UI

Human-facing exploration interface.

Responsible for:

- dashboard
- project explorer
- dependency explorer
- endpoint explorer
- database usage explorer
- configuration explorer
- modernization hotlist
- snapshot diff
- limited graph views
- evidence viewer
- markdown export
- AI prompt panel

### Archon MCP Server

AI-facing interface over the same architecture model.

Responsible for:

- exposing safe architecture tools
- exposing architecture resources
- exposing reusable prompts
- enforcing read-only behaviour by default
- returning evidence-backed responses
- supporting Copilot-assisted refactoring and development

---

# 9. Recommended Solution Structure

This section describes the **ideal target-state solution structure**, not merely the initial bootstrap shape of the repository.

All production source projects should live under `./src`.

All test projects should live under `./test`.

Every production project must have a corresponding test project named `xx.Tests.csproj`.

Executable projects should not use dotted names. Dotted naming is reserved for assembly/library projects.

```text
src/
  Archon/Archon.csproj
  Archon.ServiceDefaults/Archon.ServiceDefaults.csproj

  Archon.Domain/Archon.Domain.csproj
  Archon.Application/Archon.Application.csproj
  Archon.Api.Extraction/Archon.Api.Extraction.csproj
  Archon.Api.Query/Archon.Api.Query.csproj
  Archon.Api.Management/Archon.Api.Management.csproj

  Archon.Roslyn/Archon.Roslyn.csproj
  Archon.Roslyn.CSharp/Archon.Roslyn.CSharp.csproj
  Archon.Roslyn.VisualBasic/Archon.Roslyn.VisualBasic.csproj
  Archon.Roslyn.Legacy/Archon.Roslyn.Legacy.csproj

  Archon.Extractors.Projects/Archon.Extractors.Projects.csproj
  Archon.Extractors.AspNet/Archon.Extractors.AspNet.csproj
  Archon.Extractors.Ui/Archon.Extractors.Ui.csproj
  Archon.Extractors.Blazor/Archon.Extractors.Blazor.csproj
  Archon.Extractors.Razor/Archon.Extractors.Razor.csproj
  Archon.Extractors.WinForms/Archon.Extractors.WinForms.csproj
  Archon.Extractors.Wpf/Archon.Extractors.Wpf.csproj
  Archon.Extractors.WinUI/Archon.Extractors.WinUI.csproj
  Archon.Extractors.Maui/Archon.Extractors.Maui.csproj
  Archon.Extractors.Avalonia/Archon.Extractors.Avalonia.csproj
  Archon.Extractors.DependencyInjection/Archon.Extractors.DependencyInjection.csproj
  Archon.Extractors.Configuration/Archon.Extractors.Configuration.csproj
  Archon.Extractors.DataAccess/Archon.Extractors.DataAccess.csproj
  Archon.Extractors.LinqToSql/Archon.Extractors.LinqToSql.csproj
  Archon.Extractors.EntityFramework/Archon.Extractors.EntityFramework.csproj
  Archon.Extractors.AdoNet/Archon.Extractors.AdoNet.csproj
  Archon.Extractors.LegacyWeb/Archon.Extractors.LegacyWeb.csproj
  Archon.Extractors.Hotlist/Archon.Extractors.Hotlist.csproj

  Archon.Infrastructure.Roslyn/Archon.Infrastructure.Roslyn.csproj
  Archon.Infrastructure.Neo4j/Archon.Infrastructure.Neo4j.csproj
  Archon.Infrastructure.Markdown/Archon.Infrastructure.Markdown.csproj

  ArchonApi/ArchonApi.csproj
  ArchonUi/ArchonUi.csproj
  ArchonMcp/ArchonMcp.csproj

test/
  Archon.Tests/Archon.Tests.csproj
  Archon.ServiceDefaults.Tests/Archon.ServiceDefaults.Tests.csproj

  Archon.Domain.Tests/Archon.Domain.Tests.csproj
  Archon.Application.Tests/Archon.Application.Tests.csproj
  Archon.Api.Extraction.Tests/Archon.Api.Extraction.Tests.csproj
  Archon.Api.Query.Tests/Archon.Api.Query.Tests.csproj
  Archon.Api.Management.Tests/Archon.Api.Management.Tests.csproj

  Archon.Roslyn.Tests/Archon.Roslyn.Tests.csproj
  Archon.Roslyn.CSharp.Tests/Archon.Roslyn.CSharp.Tests.csproj
  Archon.Roslyn.VisualBasic.Tests/Archon.Roslyn.VisualBasic.Tests.csproj
  Archon.Roslyn.Legacy.Tests/Archon.Roslyn.Legacy.Tests.csproj

  Archon.Extractors.Projects.Tests/Archon.Extractors.Projects.Tests.csproj
  Archon.Extractors.AspNet.Tests/Archon.Extractors.AspNet.Tests.csproj
  Archon.Extractors.Ui.Tests/Archon.Extractors.Ui.Tests.csproj
  Archon.Extractors.Blazor.Tests/Archon.Extractors.Blazor.Tests.csproj
  Archon.Extractors.Razor.Tests/Archon.Extractors.Razor.Tests.csproj
  Archon.Extractors.WinForms.Tests/Archon.Extractors.WinForms.Tests.csproj
  Archon.Extractors.Wpf.Tests/Archon.Extractors.Wpf.Tests.csproj
  Archon.Extractors.WinUI.Tests/Archon.Extractors.WinUI.Tests.csproj
  Archon.Extractors.Maui.Tests/Archon.Extractors.Maui.Tests.csproj
  Archon.Extractors.Avalonia.Tests/Archon.Extractors.Avalonia.Tests.csproj
  Archon.Extractors.DependencyInjection.Tests/Archon.Extractors.DependencyInjection.Tests.csproj
  Archon.Extractors.Configuration.Tests/Archon.Extractors.Configuration.Tests.csproj
  Archon.Extractors.DataAccess.Tests/Archon.Extractors.DataAccess.Tests.csproj
  Archon.Extractors.LinqToSql.Tests/Archon.Extractors.LinqToSql.Tests.csproj
  Archon.Extractors.EntityFramework.Tests/Archon.Extractors.EntityFramework.Tests.csproj
  Archon.Extractors.AdoNet.Tests/Archon.Extractors.AdoNet.Tests.csproj
  Archon.Extractors.LegacyWeb.Tests/Archon.Extractors.LegacyWeb.Tests.csproj
  Archon.Extractors.Hotlist.Tests/Archon.Extractors.Hotlist.Tests.csproj

  Archon.Infrastructure.Roslyn.Tests/Archon.Infrastructure.Roslyn.Tests.csproj
  Archon.Infrastructure.Neo4j.Tests/Archon.Infrastructure.Neo4j.Tests.csproj
  Archon.Infrastructure.Markdown.Tests/Archon.Infrastructure.Markdown.Tests.csproj

  ArchonApi.Tests/ArchonApi.Tests.csproj
  ArchonUi.Tests/ArchonUi.Tests.csproj
  ArchonMcp.Tests/ArchonMcp.Tests.csproj
```

## 9.1 Expected Responsibilities by Project

### Host and composition projects

- `Archon` is the Aspire AppHost and orchestration root. It should compose the distributed application, provision Neo4j and other runtime dependencies, and wire host-to-host configuration. It should not contain domain logic, extraction logic, or persistence implementations.
- `Archon.ServiceDefaults` should contain shared host-level defaults such as service discovery wiring, resilience defaults, health checks, telemetry, common authentication plumbing, and other cross-host runtime configuration helpers.
- `ArchonApi` should expose HTTP endpoints for extraction submission, snapshot queries, project queries, dependency queries, evidence lookup, findings, metrics, management operations, and other architecture model views. It should remain a thin delivery host over API module services.
- `ArchonUi` should contain the human-facing discovery UI, including dashboard, catalogue pages, explorer views, evidence views, scoped graphs, hotlist views, and snapshot diff experiences.
- `ArchonMcp` should host the MCP server and translate MCP tool/resource/prompt requests into application-layer queries over the persisted architecture model.

### Core projects

- `Archon.Domain` should contain the stable core concepts of the architecture model, core enums/value objects, domain rules that are independent of delivery and storage, and other inward-facing business semantics.
- `Archon.Application` should contain shared application contracts, snapshot assembly workflows, cross-module use cases, DTOs/models for API/UI/MCP consumption, and ports for Roslyn, graph persistence, markdown, and other outer concerns.

### API module projects

- `Archon.Api.Extraction` should contain the API entry point into extraction, extraction request contracts, validation, orchestration workflow, common extraction code, repository/solution loading coordination, extraction project invocation, snapshot assembly, and graph persistence use cases exposed by the API host. It should coordinate the `Archon.Extractors.*` projects rather than absorbing their implementation code.
- `Archon.Api.Query` should contain graph query use cases, traversal request/response contracts, projection models, evidence lookup workflows, snapshot comparison workflows, and query services consumed by the API host, UI, and MCP host.
- `Archon.Api.Management` should contain management use cases for repositories, solutions, snapshots, rule catalog administration, extraction run history, health views, retention policy operations, and other operational API capabilities.

### Roslyn projects

- `Archon.Roslyn` should contain Roslyn-oriented abstractions, shared semantic extraction contracts, common traversal helpers, symbol/evidence projection helpers, and language-agnostic Roslyn pipeline coordination.
- `Archon.Roslyn.CSharp` should contain C#-specific syntax and semantic extraction logic, including C# declaration, invocation, attribute, dependency, and evidence extraction.
- `Archon.Roslyn.VisualBasic` should contain VB.NET-specific syntax and semantic extraction logic with parity to the C# slice where feasible.
- `Archon.Roslyn.Legacy` should contain Roslyn-based handling for legacy patterns that need special treatment, such as older project conventions, generated code heuristics, legacy configuration/code idioms, and mixed modern/legacy interpretation helpers.

### Extractor slice projects

Extraction implementation should remain in separate `Archon.Extractors.*` projects because the extraction catalogue is expected to grow over time. `Archon.Api.Extraction` provides the API entry point, shared orchestration, common extraction code, and coordination layer over these projects.

- `Archon.Extractors.Projects` should extract repository, solution, project, package, and project-reference facts.
- `Archon.Extractors.AspNet` should extract ASP.NET Core runtime facts such as endpoints, controllers, route metadata, middleware clues, and API surface information.
- `Archon.Extractors.Ui` should contain shared UI extraction contracts, UI graph normalization helpers, XAML/Razor/component evidence models, route/navigation abstractions, and common UI metadata conventions used by the specific .NET UI extractors.
- `Archon.Extractors.Blazor` should extract Blazor applications, `.razor` components, `@page` routes, layouts, render modes, component parameters, injected services, event handlers, authorization metadata, and component usage relationships.
- `Archon.Extractors.Razor` should extract Razor Pages, MVC Razor views, `_ViewImports`, `_ViewStart`, layouts, partials, tag helpers, page models, route conventions, handlers, form posts, and links to controllers or page handlers.
- `Archon.Extractors.WinForms` should extract Windows Forms applications, forms, user controls, designer files, controls, event handler wiring, resources, data bindings, startup forms, and code-behind dependencies.
- `Archon.Extractors.Wpf` should extract WPF applications, windows, pages, user controls, XAML resources, resource dictionaries, bindings, commands, routed events, view models, styles, templates, and navigation relationships.
- `Archon.Extractors.WinUI` should extract WinUI applications, windows, pages, user controls, XAML resources, bindings, commands, view models, app startup, navigation, and packaged desktop application metadata.
- `Archon.Extractors.Maui` should extract .NET MAUI applications, pages, Shell routes, XAML views, handlers, resources, bindings, commands, view models, platform-specific heads, and cross-platform navigation relationships.
- `Archon.Extractors.Avalonia` should extract Avalonia applications, AXAML views, windows, user controls, resources, styles, bindings, commands, view models, routes/navigation where present, and cross-platform desktop application metadata.
- `Archon.Extractors.DependencyInjection` should extract DI registrations, registration lifetimes, service-to-implementation mappings, and constructor/service dependency relationships.
- `Archon.Extractors.Configuration` should extract configuration keys, binding relationships, configuration source evidence, connection string usage, and external endpoint configuration clues.
- `Archon.Extractors.DataAccess` should contain shared cross-technology data-access extraction contracts, helper models, and normalisation logic used by the more specific data-access extractors.
- `Archon.Extractors.LinqToSql` should extract LINQ to SQL contexts, entities, table mappings, stored procedure mappings, and usage sites.
- `Archon.Extractors.EntityFramework` should extract Entity Framework Classic / EF6 and Entity Framework Core contexts, entity mappings, migrations, relationships, and usage sites when those technologies are present in target repositories.
- `Archon.Extractors.AdoNet` should extract ADO.NET connections, commands, command text evidence, stored procedure usage, and read/write hints.
- `Archon.Extractors.LegacyWeb` should extract classic ASP.NET, Web Forms, MVC 5, Web API 2, `System.Web`, `Global.asax`, handlers, modules, and related legacy web evidence.
- `Archon.Extractors.Hotlist` should evaluate modernization, lifecycle, obsolete API, security, dependency-risk, and architecture-smell rules over extracted facts and emit findings.

### Infrastructure projects

- `Archon.Infrastructure.Roslyn` should provide the outer Roslyn adapter implementation, including MSBuild workspace loading, compilation creation, document access, metadata resolution, and any Roslyn host integration needed by the application layer.
- `Archon.Infrastructure.Neo4j` should provide graph persistence, Cypher query implementations, graph constraint/index management, snapshot storage, rule catalog storage, findings storage, metrics storage, and related Neo4j-specific infrastructure.
- `Archon.Infrastructure.Markdown` should provide markdown export generation, export formatting, document composition, and persistence/output adapters for generated architecture documentation.

### Test projects

- `Archon.Tests` should hold cross-cutting test utilities, repository-level smoke tests, and other broad tests that do not belong to one narrower project-specific test assembly.
- `Archon.ServiceDefaults.Tests` should verify shared host defaults and runtime configuration helpers.
- `Archon.Domain.Tests` should verify domain invariants, value objects, classification rules, and other pure domain behavior.
- `Archon.Application.Tests` should verify orchestration, use cases, mapping, snapshot assembly behavior, and application service contracts.
- `Archon.Api.Extraction.Tests` should verify extraction request contracts, validation, API-module orchestration, pipeline wiring, and end-to-end extraction execution behavior through the API module seam.
- `Archon.Api.Query.Tests` should verify graph query use cases, projection contracts, evidence lookup, traversal behavior, and snapshot comparison behavior.
- `Archon.Api.Management.Tests` should verify management use cases, repository and solution administration, rule catalog operations, extraction run history, and retention behavior.
- `Archon.Roslyn.Tests` should verify Roslyn-shared helpers, abstractions, and language-agnostic semantic projection behavior.
- `Archon.Roslyn.CSharp.Tests` should verify C#-specific extraction behavior.
- `Archon.Roslyn.VisualBasic.Tests` should verify VB.NET-specific extraction behavior.
- `Archon.Roslyn.Legacy.Tests` should verify legacy Roslyn interpretation and heuristics.
- `Archon.Extractors.Projects.Tests` should verify project/package/reference extraction behavior.
- `Archon.Extractors.AspNet.Tests` should verify ASP.NET extraction behavior.
- `Archon.Extractors.Ui.Tests` should verify shared UI extraction contracts and normalized UI graph behavior.
- `Archon.Extractors.Blazor.Tests` should verify Blazor component, route, layout, service usage, and component relationship extraction behavior.
- `Archon.Extractors.Razor.Tests` should verify Razor Pages, MVC view, layout, partial, tag helper, and page model extraction behavior.
- `Archon.Extractors.WinForms.Tests` should verify Windows Forms form, user control, designer, event handler, resource, and data binding extraction behavior.
- `Archon.Extractors.Wpf.Tests` should verify WPF XAML, window, page, user control, resource, binding, command, and view-model extraction behavior.
- `Archon.Extractors.WinUI.Tests` should verify WinUI XAML, window, page, resource, binding, command, and navigation extraction behavior.
- `Archon.Extractors.Maui.Tests` should verify .NET MAUI page, Shell route, resource, binding, command, view-model, and platform-head extraction behavior.
- `Archon.Extractors.Avalonia.Tests` should verify Avalonia AXAML, window, user control, resource, style, binding, command, and view-model extraction behavior.
- `Archon.Extractors.DependencyInjection.Tests` should verify DI extraction behavior.
- `Archon.Extractors.Configuration.Tests` should verify configuration extraction behavior.
- `Archon.Extractors.DataAccess.Tests` should verify shared data-access extraction contracts and normalization logic.
- `Archon.Extractors.LinqToSql.Tests` should verify LINQ to SQL extraction behavior.
- `Archon.Extractors.EntityFramework.Tests` should verify Entity Framework Classic / EF6 and Entity Framework Core extraction behavior.
- `Archon.Extractors.AdoNet.Tests` should verify ADO.NET extraction behavior.
- `Archon.Extractors.LegacyWeb.Tests` should verify classic ASP.NET and legacy web extraction behavior.
- `Archon.Extractors.Hotlist.Tests` should verify rule evaluation and finding generation behavior.
- `Archon.Infrastructure.Roslyn.Tests` should verify workspace-loading and Roslyn adapter integration behavior.
- `Archon.Infrastructure.Neo4j.Tests` should verify graph persistence, Cypher query, constraint/index, and schema-like graph contract behaviors against Neo4j-focused infrastructure seams.
- `Archon.Infrastructure.Markdown.Tests` should verify markdown export generation and formatting behavior.
- `ArchonApi.Tests` should verify API endpoint behavior, contracts, and host composition.
- `ArchonUi.Tests` should verify UI composition and user-facing discovery flows appropriate to the chosen UI test strategy.
- `ArchonMcp.Tests` should verify MCP tool contracts, resource exposure, prompt behavior, and host composition.

This target state is intentionally more granular than the likely initial implementation. Early work packages may temporarily consolidate some of these responsibilities, but the recommended end state should preserve this separation so API modules, Roslyn adapters, extraction slices, infrastructure concerns, hosts, and their corresponding tests can evolve independently.

---

# 10. Aspire Hosting Model

Archon should be built with .NET Aspire so that developers can run the entire tool quickly.

The ideal developer experience is:

```bash
dotnet run --project ./src/Archon/Archon.csproj
```

Aspire should start:

```text
Neo4j
Archon API
Archon UI
Archon MCP Server
optional graph administration tool
```

Developers should not need to manually provision Neo4j or wire graph connection details.

The AppHost should configure all services and pass connection details through service discovery/configuration.

---

# 11. Graph Database Choice

Use **Neo4j**.

Reasoning:

- the organisation is a .NET house
- Archon's core model is inherently graph-shaped
- architecture facts need deep traversal, neighborhood queries, impact analysis, and path discovery
- project, symbol, runtime, data-access, configuration, and integration relationships should be represented as first-class graph relationships
- Neo4j provides native labelled nodes, typed relationships, graph indexes, graph constraints, and Cypher traversal semantics
- native graph persistence keeps the architecture model aligned with the way users need to explore and reason about it
- graph queries should be a product strength rather than an impedance mismatch hidden behind another storage model

Archon should begin with a native graph database.

Neo4j is the system of record for the Architecture Semantic Graph.

---

# 12. Core Data Model

Archon uses one architecture-wide full-graph model.

This is the only persistence model described by this document.

The model is snapshot-scoped, evidence-first, and designed so all API extraction slices contribute facts into one durable architecture graph rather than into separate special-purpose models.

## 12.1 Core Persistence Concepts

The persisted architecture model consists of Neo4j graph elements and supporting graph-owned metadata for:

- `Repositories`
- `Solutions`
- `Snapshots`
- labelled architecture nodes
- typed architecture relationships
- `Evidence`
- `Rules`
- `Findings`
- `Metrics`
- `GeneratedSummaries`
- supporting relationship patterns where many-to-many relationships are required

Repositories and solutions are first-class graph nodes so they participate directly in traversal, evidence linking, and diff.

## 12.2 Snapshot-Centred Architecture Graph

Every API-triggered extraction run produces a snapshot.

Each snapshot owns:

- graph nodes
- graph edges
- evidence rows
- findings
- metrics
- generated summaries

This enables:

- history
- diffing
- architectural drift detection
- post-merge analysis
- trend reporting
- evidence-backed comparison of architecture over time

Snapshots are linked to repositories directly and to one or more solutions through explicit graph relationships rather than through a single owning-solution property.

## 12.3 Full Node Model

Labelled Neo4j nodes are the primary durable graph representation of extracted architecture concepts.

```text
Node
----
Id
SnapshotId
StableKey
NodeKind
DisplayName
QualifiedName
SearchName
Language
ProjectStableKey
ParentNodeStableKey
KnowledgeKind
Ownership
ExternalCategory
Confidence
HasUnknownData
UnknownReason
PrimaryEvidenceId
MetadataJson
Fingerprint
```

Required first-class `NodeKind` values include:

```text
Repository
Solution
Project
Package
Namespace
Type
Method
Property
Field
Endpoint
Controller
HostedService
UiApplication
UiComponent
UiPage
UiView
UiLayout
UiRoute
UiControl
UiResource
UiStyle
ViewModel
Command
Binding
ConfigurationKey
DbContext
LinqToSqlDataContext
Entity
DatabaseTable
DatabaseColumn
StoredProcedure
ExternalService
Queue
Topic
FilePath
Pipeline
OpenApiDocument
Dockerfile
SqlScript
GeneratedArtifact
```

The implementation may support additional node kinds, but it must not support fewer than these.

## 12.4 Full Edge Model

Typed Neo4j relationships are the primary durable graph representation of extracted architectural relationships.

```text
Edge
----
Id
SnapshotId
StableKey
EdgeKind
SourceNodeStableKey
TargetNodeStableKey
IsDirect
KnowledgeKind
Confidence
HasUnknownData
UnknownReason
PrimaryEvidenceId
MetadataJson
Fingerprint
```

Required first-class `EdgeKind` values include:

```text
CONTAINS
REFERENCES
CALLS
IMPLEMENTS
INHERITS
INJECTS
EXPOSES
HANDLES
USES_CONFIG
USES_DB_CONTEXT
USES_LINQ_TO_SQL_CONTEXT
MAPS_ENTITY
MAPS_TABLE
MAPS_COLUMN
READS_TABLE
WRITES_TABLE
CALLS_STORED_PROCEDURE
EXECUTES_RAW_SQL
CALLS_EXTERNAL_SERVICE
USES_PACKAGE
DECLARES_ENDPOINT
DECLARES_COMPONENT
DECLARES_UI_ROUTE
USES_COMPONENT
USES_LAYOUT
USES_CONTROL
USES_UI_RESOURCE
USES_STYLE
BINDS_TO
USES_COMMAND
USES_VIEW_MODEL
NAVIGATES_TO
HANDLES_UI_EVENT
CALLS_API
REGISTERED_AS_SERVICE
DEPENDS_ON
```

The system may support additional edge kinds, but these must be available without schema redesign.

## 12.5 Evidence Model

Every persisted architectural claim that is not purely derived from already persisted facts must be linkable to evidence.

```text
Evidence
--------
Id
SnapshotId
StableKey
EvidenceKind
FilePath
StartLine
EndLine
SymbolName
ContainingSymbol
SnippetHash
SnippetPreview
KnowledgeKind
Confidence
HasUnknownData
UnknownReason
MetadataJson
Fingerprint
```

Evidence is deduplicated per snapshot so one canonical evidence row can support multiple nodes, edges, and findings in the same snapshot.

Evidence kinds include:

```text
ProjectFile
SourceCode
Configuration
Dbml
DesignerGeneratedCode
SqlScript
PipelineFile
OpenApiDocument
Dockerfile
GeneratedArtifact
PackageReference
CompilerSymbol
CompilerDiagnostic
Inference
ManualAnnotation
```

## 12.6 Findings and Rules Model

Rules are global catalog entries authored from disk-backed rule files under `./rules` and loaded into the persisted rule catalog used by the running system.

```text
Rule
----
Id
RuleCode
Name
Category
Severity
DefaultStatus
Enabled
Version
Description
DefinitionJson
SourceUrlsJson
IsBuiltIn
OwnerScope
MetadataJson
```

In authored rule JSON, `status` is the default finding status for the rule and maps to persisted `Rule.DefaultStatus`. Authored rule `source` arrays are persisted in `Rule.SourceUrlsJson` so multiple source URLs can be retained without loss.

```text
Finding
-------
Id
SnapshotId
StableKey
RuleCode
RuleVersion
Severity
Status
Title
Description
KnowledgeKind
Confidence
PrimaryNodeStableKey
PrimaryEvidenceId
FirstSeenSnapshotId
LatestSeenSnapshotId
SuppressionReason
SuppressedBy
MetadataJson
Fingerprint
```

Rule categories include:

```text
Lifecycle
ObsoleteApi
LegacyTechnology
SecuritySensitive
DataAccess
Configuration
ArchitectureLayering
DependencyRisk
ModernizationBlocker
OrganisationSpecific
```

Finding severity values:

```text
Critical
High
Medium
Low
Info
```

Finding status values:

```text
Open
Acknowledged
Suppressed
Resolved
Unknown
```

## 12.7 Metrics and Summaries Model

Metrics are persisted as first-class snapshot outputs rather than being treated only as query-time calculations.

```text
Metric
------
Id
SnapshotId
StableKey
MetricKind
ScopeKind
NodeStableKey
EdgeStableKey
PrimaryEvidenceId
Name
NumericValue
TextValue
Unit
MetadataJson
Fingerprint
```

```text
GeneratedSummary
----------------
Id
SnapshotId
StableKey
SummaryKind
TargetStableKey
Format
Title
Content
MetadataJson
Fingerprint
```

## 12.8 Link Tables

The model includes explicit link tables where many-to-many relationships are required.

Required link tables:

- `SnapshotSolutionLinks`
- `NodeEvidenceLinks`
- `EdgeEvidenceLinks`
- `MetricEvidenceLinks`
- `FindingEvidenceLinks`
- `FindingNodeLinks`

## 12.9 Classification and Unknowns

Nodes, edges, evidence, and findings must support explicit knowledge classification:

- Fact
- Inference
- Unknown
- HumanConfirmed

Unknowns must be represented explicitly with confidence and unknown-reason fields rather than being treated as silent omissions.

---

# 13. Stable Keys

Archon must use stable keys for entities so results can be compared across snapshots.

Examples:

```text
repository://main
solution://src/Product.sln
project://src/Customer.Api/Customer.Api.csproj
package://Newtonsoft.Json
type://Customer.Application.CustomerService
method://Customer.Application.CustomerService.GetCustomerAsync(System.Int32)
endpoint://GET:/api/customers/{id}
dbtable://dbo.Customer
config://ConnectionStrings:CustomerDatabase
linqtosql://Legacy.Data.CustomerDataContext
openapi://src/Customer.Api/openapi.json
dockerfile://src/Customer.Api/Dockerfile
sqlscript://database/schema/customer.sql
generatedartifact://src/Legacy.Data/Customer.designer.cs
```

Stable keys should not depend on database IDs.

---

# 14. Extraction Pipeline

## 14.1 Pipeline Stages

```text
1. Repository discovery
2. Solution discovery
3. Project loading
4. Compilation creation
5. Project metadata extraction
6. Syntax and semantic extraction
7. Framework and application type detection
8. Data access extraction
9. Configuration extraction
10. Legacy technology detection
11. Hotlist rule evaluation
12. Metric calculation
13. Snapshot persistence
14. Markdown generation
15. MCP resource refresh
```

## 14.2 Extraction Scope

Archon should analyse:

- `.sln`
- `.csproj`
- `.vbproj`
- `.props`
- `.targets`
- `packages.config`
- C# source
- VB.NET source
- generated designer source
- `.dbml`
- `.edmx`
- `.xsd`
- `.config`
- `appsettings.json`
- SQL scripts
- OpenAPI files
- pipeline files
- Dockerfiles
- Bicep/ARM/Terraform later

---

# 15. Roslyn Language Support

## 15.1 C#

Archon must support C# through Roslyn:

- `Microsoft.CodeAnalysis.CSharp`
- syntax trees
- semantic models
- symbols
- method invocation analysis
- attribute analysis
- inheritance
- interface implementation
- constructor injection
- using directives
- diagnostics

## 15.2 VB.NET

Archon must support VB.NET through Roslyn:

- `Microsoft.CodeAnalysis.VisualBasic`
- syntax trees
- semantic models
- symbols
- method invocation analysis
- attribute analysis
- inheritance
- interface implementation
- constructor injection
- imports
- diagnostics

VB.NET support is important because many legacy enterprise systems contain business-critical VB.NET components.

Archon should support mixed-language solutions.

---

# 16. Project and Package Extraction

For each project, Archon should extract:

```text
Project name
Project path
Language
Target framework(s)
Output type
Assembly name
Root namespace
SDK style / old-style project
Project references
Package references
packages.config references
Analyzer references
Nullable setting
Implicit usings
Application type indicators
```

Application type detection should identify:

```text
ASP.NET Core Web App
ASP.NET Core Web API
Classic ASP.NET Web App
Web Forms App
MVC App
Web API 2 App
Console App
Worker Service
Class Library
Test Project
Tooling Project
Unknown
```

---

# 17. Dependency Extraction

Archon should extract dependencies at several levels.

## 17.1 Project-Level Dependencies

```text
Project A references Project B
Project A uses NuGet package P
Project A references assembly A
```

## 17.2 Type-Level Dependencies

```text
Type A inherits Type B
Type A implements Interface I
Type A has field of Type B
Type A has property of Type B
Type A constructor depends on Type B
Type A method parameter uses Type B
Type A method return type uses Type B
```

## 17.3 Method-Level Dependencies

```text
Method A calls Method B
Method A creates Type B
Method A accesses Property P
Method A uses configuration key K
Method A uses DbContext D
Method A calls external service S
```

## 17.4 Confidence

Some relationships are deterministic. Others are inferred.

Examples:

```text
High confidence:
ProjectReference in .csproj

High confidence:
Constructor parameter resolved to symbol

Medium confidence:
Configuration key string constant used in IConfiguration indexer

Low confidence:
Dynamically constructed SQL string
```

---

# 18. ASP.NET Extraction

## 18.1 ASP.NET Core

Detect:

- `Program.cs`
- `Startup.cs`
- `WebApplication.CreateBuilder`
- `MapGet`
- `MapPost`
- `MapPut`
- `MapDelete`
- controllers
- route attributes
- authorization attributes
- filters
- middleware registrations
- `IEndpointRouteBuilder`
- minimal APIs
- MVC setup
- OpenAPI/Swagger setup

## 18.2 Classic ASP.NET

Detect:

- `System.Web`
- `Global.asax`
- Web Forms pages
- code-behind files
- HttpHandlers
- HttpModules
- `web.config`
- MVC 5 controllers
- Web API 2 controllers
- route configuration

---

# 19. .NET UI Extraction

Archon should treat .NET UI technologies as first-class architecture facts. UI extraction is important because user-facing screens, components, routes, navigation, bindings, commands, and API usage often define the practical change-impact path from user interaction to backend service and data access.

Archon should focus on .NET UI technologies only for this capability area. JavaScript and TypeScript frontend frameworks such as React, Angular, Vue, and Svelte are explicitly out of scope for the initial UI extraction model.

The UI extraction model should normalize framework-specific findings into common graph concepts such as UI applications, components, pages, views, layouts, routes, controls, resources, styles, view models, commands, bindings, navigation edges, API calls, and evidence.

## 19.1 Blazor

Detect:

```text
.razor files
@page directives
@layout directives
@inject directives
[Parameter]
[CascadingParameter]
EventCallback
RenderFragment
AuthorizeView
[Authorize]
Interactive render modes
Blazor Server hosting
Blazor WebAssembly hosting
Blazor Web App hosting
Hybrid Blazor hosting
component references
route parameters
forms and validation components
```

Extract:

```text
Blazor application
component
route
layout
component parameters
cascading parameters
injected services
event handlers
child component usage
authorization metadata
API/client usage
configuration usage
evidence spans
```

## 19.2 Razor Pages and Razor Views

Detect:

```text
.cshtml files
Razor Pages
MVC Razor views
_ViewImports.cshtml
_ViewStart.cshtml
layouts
partials
view components
tag helpers
page models
handler methods
form posts
anchor tag helpers
route conventions
```

Extract:

```text
Razor page
MVC view
page model
layout
partial view
view component
tag helper usage
handler method
form action
controller/action linkage where detectable
route metadata
authorization metadata
```

## 19.3 Windows Forms

Detect:

```text
System.Windows.Forms references
Windows Forms project settings
Application.Run
forms
user controls
designer files
.resx resources
control fields
InitializeComponent
event handler subscriptions
data bindings
startup form
```

Extract:

```text
Windows Forms application
startup form
form
user control
control hierarchy
event handler wiring
resources
data bindings
code-behind dependencies
service and data-access usage
```

## 19.4 WPF

Detect:

```text
PresentationFramework references
ApplicationDefinition
.xaml files
windows
pages
user controls
resource dictionaries
styles
control templates
data templates
bindings
commands
routed events
navigation services
view model conventions
```

Extract:

```text
WPF application
window
page
user control
resource dictionary
style
template
binding
command
routed event
view model relationship
navigation target
service and data-access usage
```

## 19.5 WinUI

Detect:

```text
Microsoft.UI.Xaml references
WinUI project properties
.xaml files
windows
pages
user controls
resources
styles
bindings
commands
navigation frame usage
app startup
packaged desktop metadata
```

Extract:

```text
WinUI application
window
page
user control
resource
style
binding
command
view model relationship
navigation target
packaging metadata
service and data-access usage
```

## 19.6 .NET MAUI

Detect:

```text
UseMaui
MauiProgram
.xaml files
ContentPage
ContentView
Shell
Shell routes
handlers
resources
styles
bindings
commands
platform-specific heads
```

Extract:

```text
.NET MAUI application
page
view
Shell route
handler
resource
style
binding
command
view model relationship
platform-specific head
navigation target
service and data-access usage
```

## 19.7 Avalonia

Detect:

```text
Avalonia package references
AXAML files
App.axaml
windows
user controls
resources
styles
bindings
commands
view locator patterns
ReactiveUI usage where present
application startup
```

Extract:

```text
Avalonia application
window
user control
resource
style
binding
command
view model relationship
view locator relationship
navigation target where present
service and data-access usage
```

Key graph nodes:

```text
UiApplication
UiComponent
UiPage
UiView
UiLayout
UiRoute
UiControl
UiResource
UiStyle
ViewModel
Command
Binding
Method
Project
```

Key graph edges:

```text
DECLARES_COMPONENT
DECLARES_UI_ROUTE
USES_COMPONENT
USES_LAYOUT
USES_CONTROL
USES_UI_RESOURCE
USES_STYLE
BINDS_TO
USES_COMMAND
USES_VIEW_MODEL
NAVIGATES_TO
HANDLES_UI_EVENT
CALLS_API
USES_CONFIG
DEPENDS_ON
```

---

# 20. Worker and Console Extraction

Archon should detect:

- console entry points
- `static Main`
- top-level statements
- `IHostedService`
- `BackgroundService`
- worker services
- scheduled jobs
- queue consumers
- message handlers
- Windows-service-style hosting
- Topshelf-style services if present
- custom host loops

Extract:

```text
Worker project
Entry point
Hosted services
Background service classes
Queues/topics/config keys
External integrations
Database usage
```

---

# 21. Dependency Injection Extraction

Archon should detect dependency injection registrations.

For modern .NET:

```text
services.AddSingleton<TService, TImplementation>()
services.AddScoped<TService, TImplementation>()
services.AddTransient<TService, TImplementation>()
services.AddHostedService<T>()
services.AddHttpClient<TClient>()
```

Also detect extension methods that wrap registrations.

For legacy systems, detect:

```text
Unity
Autofac
Castle Windsor
StructureMap
Ninject
SimpleInjector
CommonServiceLocator
custom service locators
manual factories
```

DI extraction should produce edges:

```text
REGISTERED_AS_SERVICE
INJECTS
DEPENDS_ON
```

Registration lifetime, registration source, and other container-specific details should be captured in metadata and evidence rather than by introducing a second DI-specific edge vocabulary.

---

# 22. Configuration Extraction

## 22.1 Modern Configuration

Detect:

- `appsettings.json`
- `appsettings.*.json`
- `IConfiguration`
- `IOptions<T>`
- `IOptionsMonitor<T>`
- `IOptionsSnapshot<T>`
- `GetSection`
- configuration indexer access
- `Bind`
- `Configure<TOptions>`

## 22.2 Legacy Configuration

Detect:

- `app.config`
- `web.config`
- `ConfigurationManager.AppSettings`
- `ConfigurationManager.ConnectionStrings`
- custom XML configuration sections
- binding redirects
- machine-level config assumptions

Extract:

```text
Configuration key
Project using key
File evidence
Environment-specific values if available
Connection string names
Likely external endpoint keys
```

---

# 23. Data Access Extraction

Data access is central to Archon.

Archon should support multiple data access technologies.

## 23.1 LINQ to SQL

LINQ to SQL must be first-class because it exists in the target estate.

Detect:

```text
.dbml files
System.Data.Linq.DataContext
generated *.designer.cs files
Table<T>
GetTable<T>()
SubmitChanges()
InsertOnSubmit()
DeleteOnSubmit()
Attach()
ExecuteQuery<T>()
ExecuteCommand()
[Table]
[Column]
[Association]
[Function]
[Parameter]
stored procedure mappings
```

Extract from `.dbml`:

```text
DataContext name
database name
connection information
tables
columns
associations
functions
stored procedures
entity names
```

Extract from generated designer files:

```text
DataContext classes
entity classes
Table<T> properties
table mappings
column mappings
associations
stored procedure methods
```

Extract from usage:

```text
Project uses DataContext
Method creates DataContext
Method queries Table<T>
Method calls SubmitChanges
Method calls ExecuteQuery
Method calls ExecuteCommand
Method calls stored procedure wrapper
```

Key graph nodes:

```text
LinqToSqlDataContext
Entity
DatabaseTable
DatabaseColumn
StoredProcedure
Method
Project
```

Key graph edges:

```text
USES_LINQ_TO_SQL_CONTEXT
MAPS_ENTITY
MAPS_TABLE
READS_TABLE
WRITES_TABLE
CALLS_STORED_PROCEDURE
EXECUTES_RAW_SQL
```

## 23.2 Entity Framework Classic / EF6 and Entity Framework Core

Entity Framework Classic / EF6 and Entity Framework Core are first-class extraction targets when they are present in analysed repositories. These references describe technologies Archon must analyse in customer code; they do not describe Archon's persistence implementation.

Detect:

```text
DbContext
ObjectContext
DbSet<T>
OnModelCreating
EntityTypeConfiguration
Migrations
provider configuration calls such as UseSqlServer, UseSqlite, and UseNpgsql
SaveChanges
SaveChangesAsync
FromSql
FromSqlRaw
FromSqlInterpolated
ExecuteSql
```

Extract:

```text
Entity Framework Classic / EF6 contexts
Entity Framework Core contexts
Entities
Tables where detectable
Relationships
Migrations
Projects using context
Methods using context
Read/write hints
```

## 23.3 ADO.NET

Detect:

```text
SqlConnection
SqlCommand
SqlDataReader
SqlDataAdapter
DataSet
DataTable
DbConnection
DbCommand
OleDbConnection
OdbcConnection
ExecuteReader
ExecuteNonQuery
ExecuteScalar
```

Extract:

```text
Connection string usage
SQL command text
Stored procedure calls
Read/write hints
Dynamic SQL indicators
Affected tables where detectable
```

## 23.4 Typed DataSets

Detect:

```text
.xsd files
TableAdapter
DataSet
DataTable
typed DataSet classes
```

Extract:

```text
Typed dataset name
Table adapters
Tables
Queries
Stored procedures
Usage sites
```

---

# 24. External Integration Extraction

Archon should detect integrations such as:

```text
HttpClient
IHttpClientFactory
typed HttpClient
named HttpClient
RestSharp
WCF clients
SOAP clients
gRPC clients
message queues
Azure Service Bus
RabbitMQ
MSMQ
storage clients
blob/file storage
SMTP/email
payment providers
internal service APIs
```

Extract:

```text
Integration name
Owning project
Client type
Base URL configuration key
Authentication mechanism if detectable
Usage sites
Evidence
```

---

# 25. Modernisation Hotlist

Archon should include a **Modernisation Hotlist**.

Do not call it only “deprecated technologies”, because many items are not formally deprecated. Some are:

- obsolete
- out of support
- legacy
- framework-only
- migration blockers
- security-sensitive
- organisation-discouraged
- high modernization risk

## 25.1 Hotlist Categories

```text
Lifecycle
ObsoleteApi
LegacyTechnology
DataAccess
SecuritySensitive
Configuration
ArchitectureLayering
DependencyRisk
ModernizationBlocker
OrganisationSpecific
```

The UI may render friendlier labels for these values, but the canonical category names should match the rule catalogue exactly.

## 25.2 Status Values

```text
OutOfSupport
Obsolete
Legacy
FrameworkOnly
MigrationBlocker
SecuritySensitive
Discouraged
Unknown
```

## 25.3 Severity Values

```text
Critical
High
Medium
Low
Info
```

---

# 26. Starter Modernisation Hotlist

The following list should seed Archon’s rule catalogue.

## 26.1 Target Framework Hotlist

Detect:

```text
.NET Framework < 4.6.2
.NET Framework 4.5.2
.NET Framework 4.6
.NET Framework 4.6.1
.NET Core 1.x
.NET Core 2.x
.NET Core 3.0
.NET Core 3.1
.NET 5
.NET 6
.NET 7
.NET Standard-only libraries that block migration
```

Notes:

- .NET Framework 4.5.2, 4.6, and 4.6.1 retired on 26 April 2022 according to Microsoft lifecycle guidance.
- .NET Framework 4.6.2+ support follows specific Microsoft lifecycle rules and parent OS considerations.
- Organisation policy may choose stricter rules.

Example rule:

```json
{
  "id": "ARCHON-LIFE-001",
  "name": "Unsupported .NET Framework version",
  "category": "Lifecycle",
  "severity": "Critical",
  "status": "OutOfSupport",
  "enabled": true,
  "version": "1.0.0",
  "detect": {
    "nodeKinds": ["Project"],
    "match": "all",
    "conditions": [
      {
        "kind": "target-framework-membership",
        "targetFrameworks": ["net45", "net451", "net452", "net46", "net461"]
      }
    ]
  },
  "impact": [
    "Project targets a retired or unsupported .NET Framework version.",
    "Security fixes and technical support may not be available.",
    "Modernization or retargeting should be planned."
  ],
  "evidenceRequired": ["projectFile"],
  "source": [
    "https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-framework"
  ]
}
```

## 26.2 Legacy Application Technologies

Detect:

```text
ASP.NET Web Forms
ASP.NET Web Pages
classic ASP.NET MVC 3/4/5
ASP.NET Web API 2
System.Web
Global.asax
HttpModules
HttpHandlers
WCF server applications
WCF clients
ASMX web services
Windows Workflow Foundation
.NET Remoting
Enterprise Services / COM+
ClickOnce deployment
classic Windows Services
Topshelf services
OWIN/Katana startup
```

Example rule:

```json
{
  "id": "ARCHON-WEB-001",
  "name": "System.Web dependency",
  "category": "LegacyTechnology",
  "severity": "High",
  "status": "FrameworkOnly",
  "enabled": true,
  "version": "1.0.0",
  "detect": {
    "nodeKinds": ["Project", "Type", "Method"],
    "match": "any",
    "conditions": [
      {
        "kind": "namespace",
        "namespaces": ["System.Web"]
      },
      {
        "kind": "package",
        "packages": ["Microsoft.AspNet.*"]
      }
    ]
  },
  "impact": [
    "Indicates dependency on classic ASP.NET / .NET Framework web stack.",
    "May constrain migration to modern .NET.",
    "Requires specific modernization strategy."
  ],
  "evidenceRequired": ["project", "sourceFile", "symbol"]
}
```

## 26.3 Legacy Data Access Technologies

Detect:

```text
LINQ to SQL
.dbml files
System.Data.Linq.DataContext
Table<T>
SubmitChanges()
ExecuteQuery<T>()
ExecuteCommand()
typed DataSets
DataSet
DataTable
TableAdapter
ADO.NET SqlCommand
ADO.NET SqlDataReader
Entity Framework Classic / EF6
Entity Framework Core
ObjectContext
raw SQL construction
stored procedure-heavy access
OleDb
Odbc
```

Example LINQ to SQL rule:

```json
{
  "id": "ARCHON-DATA-001",
  "name": "LINQ to SQL DataContext",
  "category": "DataAccess",
  "severity": "High",
  "status": "Legacy",
  "enabled": true,
  "version": "1.0.0",
  "detect": {
    "nodeKinds": ["Project", "Type", "Method", "FilePath"],
    "match": "any",
    "conditions": [
      {
        "kind": "symbol",
        "symbols": ["System.Data.Linq.DataContext", "System.Data.Linq.Table<T>"]
      },
      {
        "kind": "file-pattern",
        "filePatterns": ["*.dbml"]
      },
      {
        "kind": "method-call",
        "methodCalls": ["SubmitChanges", "ExecuteQuery", "ExecuteCommand"]
      }
    ]
  },
  "impact": [
    "Indicates legacy .NET Framework data access.",
    "Likely modernization consideration.",
    "May constrain migration to modern .NET.",
    "Must be mapped before service extraction or database separation."
  ],
  "evidenceRequired": ["project", "file", "symbol", "line"],
  "recommendedArchonAction": [
    "Add to Data Access Explorer.",
    "Include in modernization hotlist.",
    "Link affected projects, entities, and tables."
  ]
}
```

## 26.4 Obsolete APIs with SYSLIB Diagnostics

Seed from Microsoft’s SYSLIB and EXTOBS diagnostic documentation.

Detect examples:

```text
SYSLIB0001 - UTF-7 encoding
SYSLIB0002 - PrincipalPermissionAttribute
SYSLIB0003 - Code Access Security
SYSLIB0004 - Constrained Execution Regions
SYSLIB0011 - BinaryFormatter serialization
SYSLIB0014 - WebRequest, HttpWebRequest, WebClient, ServicePointManager
SYSLIB0021 - derived cryptographic types
SYSLIB0022 - RijndaelManaged
SYSLIB0023 - RNGCryptoServiceProvider
SYSLIB0026 - X509Certificate/X509Certificate2 constructors with password
SYSLIB0032 - Recovery from corrupted process state exceptions
SYSLIB0045 - cryptographic factory methods accepting algorithm names
```

Archon should not hard-code only the above. It should maintain a versioned rule catalogue that can be updated from Microsoft documentation.

Example rule:

```json
{
  "id": "ARCHON-OBSOLETE-SYSLIB0014",
  "name": "Legacy HTTP request APIs",
  "category": "ObsoleteApi",
  "severity": "Medium",
  "status": "Obsolete",
  "enabled": true,
  "version": "1.0.0",
  "detect": {
    "nodeKinds": ["Type", "Method"],
    "match": "all",
    "conditions": [
      {
        "kind": "symbol",
        "symbols": [
          "System.Net.WebRequest",
          "System.Net.HttpWebRequest",
          "System.Net.WebClient",
          "System.Net.ServicePointManager"
        ]
      }
    ]
  },
  "impact": [
    "Uses APIs marked obsolete with SYSLIB0014 in modern .NET.",
    "HttpClient is generally the expected migration path."
  ],
  "evidenceRequired": ["sourceFile", "symbol", "line"],
  "source": [
    "https://learn.microsoft.com/en-us/dotnet/fundamentals/syslib-diagnostics/syslib0014"
  ]
}
```

## 26.5 Security-Sensitive Technologies

Detect:

```text
BinaryFormatter
LosFormatter
NetDataContractSerializer
SoapFormatter
ObjectStateFormatter
Code Access Security
PrincipalPermissionAttribute
Forms Authentication
machineKey dependencies
custom authentication
SHA1
MD5
DES
TripleDES
RijndaelManaged
hard-coded secrets
connection strings in config
custom encryption
```

Example rule:

```json
{
  "id": "ARCHON-SEC-001",
  "name": "BinaryFormatter usage",
  "category": "SecuritySensitive",
  "severity": "Critical",
  "status": "SecuritySensitive",
  "enabled": true,
  "version": "1.0.0",
  "detect": {
    "nodeKinds": ["Type", "Method"],
    "match": "all",
    "conditions": [
      {
        "kind": "symbol",
        "symbols": ["System.Runtime.Serialization.Formatters.Binary.BinaryFormatter"]
      }
    ]
  },
  "impact": [
    "BinaryFormatter is unsafe for untrusted data.",
    "Requires urgent review before modernization."
  ],
  "evidenceRequired": ["sourceFile", "symbol", "line"]
}
```

## 26.6 Configuration and Hosting Constraints

Detect:

```text
web.config-heavy applications
app.config-heavy applications
binding redirects
machine.config assumptions
ConfigurationManager
packages.config
non-SDK-style csproj/vbproj
Global.asax
OWIN Startup
IIS-only assumptions
Windows Registry configuration
hard-coded file paths
UNC paths
environment-specific transforms
```

Example rule:

```json
{
  "id": "ARCHON-CONFIG-001",
  "name": "packages.config dependency management",
  "category": "Configuration",
  "severity": "Medium",
  "status": "Legacy",
  "enabled": true,
  "version": "1.0.0",
  "detect": {
    "nodeKinds": ["FilePath", "Project"],
    "match": "all",
    "conditions": [
      {
        "kind": "file-pattern",
        "filePatterns": ["packages.config"]
      }
    ]
  },
  "impact": [
    "Indicates legacy NuGet package management.",
    "May complicate migration to SDK-style projects."
  ],
  "evidenceRequired": ["file"]
}
```

## 26.7 Legacy Dependency and Package Patterns

Detect packages/namespaces such as:

```text
EntityFramework 6
Microsoft.AspNet.Mvc
Microsoft.AspNet.WebApi
System.Web.*
Microsoft.Practices.Unity
Unity older versions
CommonServiceLocator
Enterprise Library
Castle Windsor
StructureMap
Ninject
log4net
old NLog versions
RestSharp older versions
Newtonsoft.Json older versions
Topshelf
```

These are not all deprecated. They are modernization signals.

## 26.8 Architecture Smells

Detect:

```text
high fan-in project
high fan-out project
shared library referenced by many apps
domain project referencing infrastructure
domain project referencing web
web project referenced by non-web project
data access spread across many projects
same table used by many projects
circular project dependencies
large project with many public types
god service class
controller with heavy logic
static service locator
reflection-heavy call paths
dynamic invocation
```

Example rule:

```json
{
  "id": "ARCHON-ARCH-001",
  "name": "High fan-in shared library",
  "category": "DependencyRisk",
  "severity": "Medium",
  "status": "ModernizationBlocker",
  "enabled": true,
  "version": "1.0.0",
  "detect": {
    "nodeKinds": ["Project"],
    "match": "all",
    "conditions": [
      {
        "kind": "metric-threshold",
        "metric": "IncomingProjectReferences",
        "operator": "GreaterThan",
        "value": 20
      },
      {
        "kind": "metric-threshold",
        "metric": "ProjectFanInPercentile",
        "operator": "GreaterThanOrEqual",
        "value": 95
      }
    ]
  },
  "impact": [
    "Project is highly central.",
    "Changes may affect many applications.",
    "Requires impact analysis before refactoring."
  ],
  "evidenceRequired": ["metric", "projectReferenceEdges"]
}
```

---

# 27. Rule Engine Requirements

Archon should include a rule engine that runs after extraction.

Rules should be:

- versioned
- enabled/disabled
- configurable
- evidence-backed
- severity-scored
- category-scored
- suppressible
- organisation-extensible

Rules should produce findings.

Findings should link to:

- affected node
- evidence
- snapshot
- rule
- confidence
- suggested next investigation

Rules should not automatically prescribe a solution unless the rule is explicitly advisory.

## 27.1 Rule Definition Format

All Archon rules must be authored as **JSON documents**.

The rule authoring format is JSON only.

The JSON structure must be:

- explicit
- schema-validated
- machine-readable
- stable across API modules, UI, and MCP consumers
- expressive enough for nested boolean logic and heterogeneous condition types

Every rule file under `./rules` must conform to the same JSON rule contract.

## 27.2 Detection Logic and Boolean Composition

The `detect` block must support boolean composition directly.

At minimum, the detection model must support:

- `match: "all"`
- `match: "any"`
- `match: "none"`
- `conditions: []`
- `groups: []`

An empty detection group is invalid.

`conditions` contains leaf predicates.

`groups` contains nested boolean groups that use the same detection structure recursively.

When both `conditions` and `groups` are present, they are evaluated together as operands of the current `match` value.

`match: "all"` means every operand in the current group must evaluate to true.

`match: "any"` means at least one operand in the current group must evaluate to true.

`match: "none"` means no operand in the current group may evaluate to true.

This allows rules such as:

- all of several metric thresholds must be true
- any of several symbols or file patterns may trigger a rule
- no allowed exemption condition may be present
- nested combinations of `all` and `any` can be expressed without inventing special-case syntax

## 27.3 Supported Detection Condition Kinds

The rule DSL must support explicit condition objects rather than encoding operators inside property names.

At minimum, the JSON rule format must support condition kinds for:

- `target-framework-membership`
- `namespace`
- `symbol`
- `package`
- `file-pattern`
- `method-call`
- `attribute`
- `metric-threshold`

The implementation may support additional kinds, but these must exist in the first-class rule contract.

The `metric-threshold` condition kind must separate:

- `metric`
- `operator`
- `value`

The operator must be explicit and must not be encoded into the metric name.

At minimum, operators must support:

- `Equal`
- `NotEqual`
- `GreaterThan`
- `GreaterThanOrEqual`
- `LessThan`
- `LessThanOrEqual`
- `In`
- `NotIn`
- `Contains`
- `StartsWith`
- `EndsWith`
- `MatchesPattern`

## 27.4 Scope and Applicability

Rules must be able to declare the kinds of graph nodes they apply to.

The `detect` block must therefore support `nodeKinds`, for example:

- `Project`
- `Type`
- `Method`
- `FilePath`

This ensures that rule evaluation remains deterministic and that the rule engine can limit evaluation to the correct node scope.

## 27.5 Example of Required Final-State Rule Logic

The following style of JSON rule logic is required now and must be treated as part of the rule specification, not as a future enhancement:

```json
{
  "detect": {
    "nodeKinds": ["Project"],
    "match": "all",
    "conditions": [
      {
        "kind": "metric-threshold",
        "metric": "IncomingProjectReferences",
        "operator": "GreaterThan",
        "value": 20
      },
      {
        "kind": "metric-threshold",
        "metric": "ProjectFanInPercentile",
        "operator": "GreaterThanOrEqual",
        "value": 95
      }
    ]
  }
}
```

Nested boolean groups must also be supported now. Example:

```json
{
  "detect": {
    "nodeKinds": ["Project"],
    "match": "all",
    "conditions": [
      {
        "kind": "metric-threshold",
        "metric": "IncomingProjectReferences",
        "operator": "GreaterThan",
        "value": 20
      }
    ],
    "groups": [
      {
        "match": "any",
        "conditions": [
          {
            "kind": "metric-threshold",
            "metric": "ProjectFanInPercentile",
            "operator": "GreaterThanOrEqual",
            "value": 95
          },
          {
            "kind": "metric-threshold",
            "metric": "CentralityScore",
            "operator": "GreaterThanOrEqual",
            "value": 0.8
          }
        ]
      }
    ]
  }
}
```

## 27.6 Rule Catalog Governance

Because the rules are JSON-based structured contracts, Archon should validate rule files before loading them.

Validation should include at minimum:

- JSON parsing
- schema validation
- required field validation
- enum validation for category, severity, status, condition kind, and operator
- semantic validation of condition payloads
- version compatibility validation for the rule DSL

---

# 28. Discovery UI

The UI should be simple but highly useful.

Principle:

> **The UI should answer practical questions, not merely display entities.**

Key questions:

```text
What is this?
What depends on it?
What does it depend on?
Where is the evidence?
What changed?
Why might this be risky?
Which legacy technologies are involved?
What should Copilot know before changing this?
```

## 28.1 Dashboard

Show:

```text
Repository
Solution
Latest snapshot
Commit SHA
Analysis date/time
Number of projects
Number of C# projects
Number of VB.NET projects
Number of APIs
Number of workers
Number of endpoints
Number of data contexts
Number of hotlist findings
Top coupling hotspots
Latest architecture changes
```

## 28.2 Project Explorer

Searchable grid:

```text
Project name
Path
Language
Project type
Target framework
SDK-style / old-style
Incoming references
Outgoing references
Package count
Endpoint count
Data access indicators
Hotlist count
Risk indicators
```

Project detail page:

```text
Summary
Responsibilities
Evidence
Entry points
Project references
Referencing projects
Packages
Application type
Endpoints
Workers
Data access
Configuration keys
External integrations
Hotlist findings
Graph
Unknowns
```

## 28.3 Dependency Explorer

Capabilities:

```text
Direct dependencies
Direct dependents
Transitive dependencies
Transitive dependents
Dependency path between two nodes
Incoming/outgoing filters
Depth filters
Edge type filters
```

## 28.4 Limited Graph Views

Graphs should be scoped.

Do not render the entire estate by default.

Useful graph modes:

```text
Direct dependencies of Project X
Direct dependents of Project X
Neighbourhood graph depth 1/2/3
Dependency path from A to B
Endpoint-to-data flow
Table/entity usage graph
External integration graph
Layer violation graph
```

Default graph depth should be 1.

The UI should prevent hairball graphs:

```text
This graph contains too many nodes to be useful.
Please apply filters or select a narrower scope.
```

Suggested technologies:

- Cytoscape.js for interactive UI graphs
- Mermaid for markdown/export diagrams
- Graphviz/DOT optionally for static rendering

## 28.5 Endpoint Explorer

Show:

```text
HTTP method
Route
Project
Controller / handler
Action / method
Request DTO
Response DTO
Authorization attributes
Services used
DbContexts / DataContexts used
Configuration keys
Evidence
```

## 28.6 Worker Explorer

Show:

```text
Worker project
Entry point
Hosted services
Background services
Queue/topic consumers
Scheduled jobs
Data access
External integrations
Configuration keys
Evidence
```

## 28.7 Data Access Explorer

Sections:

```text
LINQ to SQL
Entity Framework Classic / EF6
Entity Framework Core
ADO.NET
Typed DataSets
Raw SQL
Stored Procedures
```

For LINQ to SQL:

```text
DataContexts
Entities
Tables
Stored procedures
Usage sites
SubmitChanges call sites
ExecuteQuery/ExecuteCommand call sites
```

Key questions:

```text
Which projects use this DataContext?
Which methods call SubmitChanges?
Which tables are touched by this project?
Where is raw SQL used?
Which entities are shared across many projects?
```

## 28.8 Modernisation Hotlist UI

Show:

```text
Finding
Severity
Category
Status
Affected project
Affected node
Evidence
Confidence
First seen snapshot
Latest seen snapshot
```

Provide filters:

```text
Critical only
Legacy data access
Out of support
Security-sensitive
Framework-only
By project
By technology
By snapshot
```

## 28.9 Snapshot Diff

Show architecture changes between snapshots:

```text
New projects
Removed projects
Target framework changes
New project references
Removed project references
New package references
Removed package references
New endpoints
Removed endpoints
Changed routes
New data contexts
Changed data access
New hotlist findings
Resolved hotlist findings
Coupling metric changes
```

## 28.10 Evidence Viewer

Every UI claim should be clickable to evidence.

Evidence view:

```text
File path
Line range
Symbol
Snippet preview
Finding/rule
Snapshot
Confidence
```

## 28.11 AI Prompt Panel

The UI may include an “Ask Archon” prompt.

Example prompts:

```text
What depends on this project?
Explain why this project is a coupling hotspot.
What changed in the latest snapshot?
Which APIs use CustomerDataContext?
What would be impacted if we changed this entity?
Generate a modernization brief for this subsystem.
```

Responses should include:

```text
Answer
Confidence
Evidence
Related nodes
Unknowns
Suggested follow-up questions
```

---

# 29. MCP Server

Archon should include an MCP server so Copilot and other AI assistants can query the architecture model.

## 29.1 MCP Design Principle

> **Copilot knows how to write code.  
> Archon knows how this system is put together.  
> MCP lets Copilot ask Archon before it acts.**

The MCP server should be read-only by default.

It should not expose:

- arbitrary SQL execution
- shell execution
- filesystem modification
- database mutation
- code modification

## 29.2 MCP Tools v1

Start with:

```text
archon.search
archon.describe_project
archon.get_dependencies
archon.get_dependents
archon.find_dependency_paths
archon.describe_symbol
archon.find_symbol_usages
archon.get_data_access_usage
archon.assess_change_impact
archon.get_architecture_rules
archon.get_hotlist_findings
archon.get_snapshot_diff
```

## 29.3 MCP Tool: archon.describe_project

Input:

```json
{
  "projectKeyOrName": "Customer.Api"
}
```

Output:

```text
Project summary
Project type
Language
Target framework
References
Dependents
Entry points
Endpoints
Data access
Configuration
Hotlist findings
Evidence
Unknowns
```

## 29.4 MCP Tool: archon.assess_change_impact

Input:

```json
{
  "target": "type://Customer.Application.CustomerService"
}
```

Output:

```text
Direct dependents
Transitive dependents
Affected endpoints
Affected workers
Affected data access
Affected configuration
Hotlist context
Confidence
Evidence
Unknowns
```

## 29.5 MCP Tool: archon.get_data_access_usage

Input:

```json
{
  "target": "linqtosql://Legacy.Data.CustomerDataContext"
}
```

Output:

```text
Projects using DataContext
Methods using DataContext
Entities/tables mapped
SubmitChanges usage
Raw SQL usage
Stored procedure usage
Evidence
Unknowns
```

## 29.6 MCP Resources

Expose resources:

```text
archon://snapshot/current
archon://project/{projectKey}
archon://symbol/{symbolKey}
archon://rules/current
archon://hotlist/current
archon://hotspots/current
archon://snapshot/{snapshotId}/diff/{previousSnapshotId}
```

## 29.7 MCP Prompts

Expose prompts:

```text
impact-analysis
modernization-brief
refactoring-preflight
new-feature-placement
legacy-data-access-review
hotlist-summary
architecture-rule-check
```

Example prompt: `refactoring-preflight`

```text
Before proposing code changes, query Archon for:
1. dependencies
2. dependents
3. data access usage
4. hotlist findings
5. architecture rules
6. evidence
7. unknowns

Then produce:
- safe refactoring plan
- risks
- tests likely affected
- files likely affected
- suggested sequence
```

## 29.8 MCP Security Requirements

MCP must be treated as a powerful internal API.

Requirements:

```text
Read-only by default
No arbitrary SQL tools
No shell tools
Authentication
Authorization
Audit logging
Tool allow-list
Environment isolation
No secrets exposure
Response size limits
Prompt-injection-aware output handling
```

---

# 30. Copilot Workflows

## 30.1 Refactoring Workflow

Developer asks Copilot:

```text
I want to refactor CustomerService. Use Archon to assess the impact first.
```

Copilot calls:

```text
archon.assess_change_impact("type://Customer.Application.CustomerService")
```

Archon returns:

```text
Used by 6 projects
Used by 14 endpoints
Depends on LegacyCustomerDataContext
Writes to Customer and Account tables
Has 3 transitive dependents
2 hotlist findings
Evidence links
Unknowns
```

Copilot then proposes:

```text
Refactoring plan
Safe extraction order
Tests to update
Interfaces to preserve
Risks
Unknowns
```

## 30.2 New Development Workflow

Developer asks:

```text
Add a new worker that processes customer export messages.
Use existing project conventions.
```

Copilot calls:

```text
archon.search("worker message processing")
archon.get_architecture_rules()
archon.get_dependencies(...)
```

Copilot then generates code that follows existing conventions.

## 30.3 Modernization Workflow

Developer asks:

```text
What are the biggest modernization blockers in the Customer subsystem?
```

Copilot calls:

```text
archon.get_hotlist_findings(projectOrSubsystem)
archon.get_data_access_usage(projectOrSubsystem)
archon.get_dependencies(projectOrSubsystem)
```

Copilot returns:

```text
Modernization brief
Critical blockers
Legacy data access
Framework-only dependencies
Recommended investigation order
Evidence
```

---

# 31. Markdown Export

Archon should export generated markdown.

Example structure:

```text
/docs/archon/generated/
  00-index.md
  01-system-overview.md
  02-solution-inventory.md
  03-project-catalogue/
  04-dependency-map.md
  05-runtime-map.md
  06-data-access.md
  07-integration-map.md
  08-modernisation-hotlist.md
  09-coupling-hotspots.md
  10-snapshot-diff.md
```

Markdown is an output, not the source of truth.

Source of truth:

```text
Code → API-triggered Roslyn extraction → Neo4j Architecture Semantic Graph
```

---

# 32. Quality, Confidence, and Classification

Archon should classify knowledge.

## 32.1 Fact

Directly evidenced by code, project files, configuration, metadata, or compiler symbols.

## 32.2 Inference

Reasonable conclusion based on patterns, naming, or partial evidence.

## 32.3 Unknown

Evidence is insufficient.

## 32.4 Human Confirmed

Later phase. Manually reviewed/confirmed by architects or engineers.

Phase 1 should be fully automated. Human review is next step.

---

# 33. Metrics

Archon should calculate useful architecture metrics.

## 33.1 Project Metrics

```text
Incoming project references
Outgoing project references
Package count
Public type count
Endpoint count
Data access count
Hotlist finding count
Target framework age/risk
```

## 33.2 Graph Metrics

```text
Fan-in
Fan-out
Centrality
Dependency depth
Transitive dependency count
Cycle detection
Neighbourhood size
```

## 33.3 Modernization Metrics

```text
Legacy technology count
Security-sensitive finding count
Out-of-support target count
Framework-only dependency count
Data access spread
Shared table usage count
```

---

# 34. Architecture Rules and Layering

Archon should eventually support organisation-defined rules.

Examples:

```text
Domain projects should not reference Infrastructure projects.
Domain projects should not reference Web projects.
Web projects should not be referenced by non-web projects.
Application projects should not directly use LINQ to SQL unless explicitly allowed.
Controllers should not directly use DataContext.
Worker projects should declare queue/topic dependencies.
Shared libraries with high fan-in require review before change.
```

These should be configurable.

Do not hard-code organisation-specific architecture too early.

---

# 35. Implementation Phases

## Phase 0: Concept and Spec

Deliverables:

```text
This document
Initial backlog
Initial architecture decisions
Prototype scope
```

## Phase 1: API Extraction MVP

Build:

```text
Aspire AppHost
Neo4j graph store
Extraction submission API
Solution loader
Project inventory
Project references
Package references
Snapshot persistence
Basic dashboard
```

Goal:

```text
Run Archon against a solution and see all projects/dependencies in UI.
```

## Phase 2: Roslyn Semantic Extraction

Add:

```text
C# semantic extraction
VB.NET semantic extraction
Type symbols
Method symbols
Interface implementations
Constructor dependencies
Method calls
Evidence spans
```

Goal:

```text
Understand symbol-level architecture relationships.
```

## Phase 3: Legacy Data Access

Add:

```text
LINQ to SQL extractor
DBML parser
DataContext detection
Table/entity mapping
SubmitChanges detection
ExecuteQuery/ExecuteCommand detection
ADO.NET detection
Typed DataSet detection
```

Goal:

```text
Expose database coupling and legacy data access.
```

## Phase 4: Application and Runtime Discovery

Add:

```text
ASP.NET Core endpoints
Classic ASP.NET detection
Web Forms detection
MVC/Web API detection
Worker detection
Hosted service detection
Configuration detection
External integration detection
```

Goal:

```text
Map runtime-facing applications and integrations.
```

## Phase 5: .NET UI Discovery

Add:

```text
Blazor component extraction
Razor Pages and Razor view extraction
Windows Forms form/control extraction
WPF XAML extraction
WinUI XAML extraction
.NET MAUI page/Shell extraction
Avalonia AXAML extraction
UI route, navigation, binding, command, resource, and view-model extraction
UI-to-service and UI-to-API relationship extraction
```

Goal:

```text
Expose user-facing screens, components, navigation, bindings, commands, and UI-driven backend dependencies.
```

## Phase 6: Hotlist and Findings

Add:

```text
Modernisation Hotlist rule engine
Lifecycle rules
Obsolete API rules
Legacy technology rules
Security-sensitive rules
Architecture smell rules
Hotlist UI
```

Goal:

```text
Prioritise modernization risk.
```

## Phase 7: Graph UI and Snapshot Diff

Add:

```text
Scoped graph views
Project neighbourhood graph
Dependency path graph
Endpoint-to-data graph
Snapshot diff
Architecture drift reports
```

Goal:

```text
Make change and coupling visible.
```

## Phase 8: MCP Server

Add:

```text
Read-only MCP server
Architecture tools
Architecture resources
Architecture prompts
Copilot integration instructions
Audit logging
```

Goal:

```text
Let Copilot query Archon before suggesting changes.
```

## Phase 9: Markdown Export and Architecture KB

Add:

```text
Generated markdown
Project catalogue export
Dependency map export
Data access export
Hotlist export
Snapshot diff export
```

Goal:

```text
Generate human-readable Architecture KB from deterministic model.
```

---

# 36. Initial Backlog Candidates

## Epic: Core Platform

```text
Create Archon AppHost with Neo4j
Create Archon API
Create Archon.Api.Extraction module
Create Archon.Api.Query module
Create Archon.Api.Management module
Create Archon UI shell
Create Neo4j graph constraints and indexes
Create snapshot model
Create repository/solution model
```

## Epic: Project Extraction

```text
Load .sln using Roslyn workspace
Extract C# projects
Extract VB.NET projects
Extract target frameworks
Extract project references
Extract package references
Extract packages.config
Persist project nodes/edges
```

## Epic: Roslyn Symbols

```text
Extract namespaces
Extract type symbols
Extract method symbols
Extract inheritance
Extract interface implementations
Extract constructor dependencies
Extract method calls
Persist evidence spans
```

## Epic: LINQ to SQL

```text
Parse .dbml files
Detect DataContext classes
Detect Table<T> properties
Detect entity/table mappings
Detect SubmitChanges
Detect ExecuteQuery/ExecuteCommand
Map project/method to data usage
```

## Epic: .NET UI Extraction

```text
Extract Blazor applications, components, routes, layouts, parameters, injected services, and component usage
Extract Razor Pages, MVC Razor views, layouts, partials, tag helpers, page models, handlers, and form posts
Extract Windows Forms forms, user controls, designer files, controls, resources, event handlers, and data bindings
Extract WPF windows, pages, user controls, XAML resources, styles, templates, bindings, commands, and view models
Extract WinUI windows, pages, controls, resources, bindings, commands, view models, startup, and navigation
Extract .NET MAUI pages, Shell routes, handlers, resources, bindings, commands, view models, and platform heads
Extract Avalonia AXAML views, windows, user controls, resources, styles, bindings, commands, and view models
Map UI routes/navigation/components to services, API calls, configuration keys, and data-access usage where detectable
```

## Epic: UI

```text
Dashboard
Project explorer
Project details
Dependency explorer
Evidence viewer
Hotlist viewer
Snapshot diff viewer
Scoped graph view
```

## Epic: MCP

```text
Implement MCP server
Expose describe_project
Expose get_dependencies
Expose assess_change_impact
Expose get_data_access_usage
Expose get_hotlist_findings
Expose resources and prompts
```

---

# 37. Copilot Instructions for Building Specs

When using this document with Copilot, instruct it as follows:

```text
Use this document as the source brief for Archon.

Generate detailed implementation specifications in small, reviewable pieces.

Do not generate all code immediately.

Start by producing:
1. logical architecture
2. solution/project structure
3. database schema proposal
4. API extraction MVP specification
5. UI MVP specification
6. MCP v1 specification
7. backlog epics/stories

Preserve the core principle:
Roslyn extracts deterministic facts.
Neo4j stores architecture memory as a native graph.
The UI makes it explorable.
MCP makes it available to Copilot.
AI explains and reasons over facts.
Humans decide.
```

---

# 38. Non-Goals for Early Versions

Do not attempt early:

```text
Full automated refactoring
Automatic code rewriting
Full graph database
Full polyglot support
Perfect call graph resolution
Runtime tracing
Production security certification
Enterprise-wide deployment
Complex visual modelling suite
AI-only analysis
```

Early Archon should be narrow, reliable, useful, and evidence-backed.

---

# 39. Risks and Mitigations

## Risk: Scope Explosion

Mitigation:

```text
Start with project inventory and dependencies.
Add legacy data access early because it is high value.
Defer advanced graph and AI features.
```

## Risk: Roslyn Analysis Performance

Mitigation:

```text
Snapshot analysis
Incremental extraction later
Parallel project analysis
Cache compilations where safe
Store derived facts only
```

## Risk: Legacy Code Is Hard to Resolve

Mitigation:

```text
Use confidence levels.
Capture unknowns.
Do not pretend dynamic behaviour is statically knowable.
```

## Risk: Developers Do Not Trust AI

Mitigation:

```text
Make Roslyn facts primary.
Show evidence for every claim.
Use AI as explanation layer only.
```

## Risk: MCP Security

Mitigation:

```text
Read-only server
No shell execution
No arbitrary graph query execution
Tool allow-list
Audit logging
Authentication/authorization
```

---

# 40. Key Message

Archon should be built around this core message:

> **Archon is not another documentation tool.  
> It is a deterministic architecture intelligence platform for the .NET systems we actually have.**

And:

> **Copilot accelerates coding.  
> Archon supplies architectural memory, evidence, and guardrails.**

---

# 41. Final Summary

Archon is:

- .NET-first
- Roslyn-powered
- Neo4j-backed
- Aspire-hosted
- legacy-aware
- evidence-driven
- snapshot-based
- UI-explorable
- MCP-enabled
- AI-ready

Archon should help teams answer:

```text
What exists?
What depends on this?
What does this touch?
Where is the evidence?
What changed?
What is legacy?
What is risky?
What should Copilot know before changing this?
```

The desired end state:

```text
Code changes
   ↓
Archon extraction API runs
   ↓
Neo4j architecture graph updates
   ↓
UI shows what changed
   ↓
MCP exposes facts to Copilot
   ↓
Copilot assists with architectural awareness
   ↓
Humans validate, interpret, and decide
```

That is the Architecture Operating System.

---

# Appendix A: Initial Rule Catalogue Format

```json
{
  "id": "ARCHON-RULE-ID",
  "name": "Human readable rule name",
  "category": "Lifecycle | ObsoleteApi | LegacyTechnology | SecuritySensitive | DataAccess | Configuration | ArchitectureLayering | DependencyRisk | ModernizationBlocker | OrganisationSpecific",
  "severity": "Critical | High | Medium | Low | Info",
  "status": "OutOfSupport | Obsolete | Legacy | FrameworkOnly | MigrationBlocker | SecuritySensitive | Discouraged | Unknown",
  "enabled": true,
  "version": "1.0.0",
  "detect": {
    "nodeKinds": ["Project | Type | Method | FilePath | Endpoint | Controller | HostedService"],
    "match": "all | any | none",
    "conditions": [
      {
        "kind": "target-framework-membership | namespace | symbol | package | file-pattern | method-call | attribute | metric-threshold",
        "targetFrameworks": [],
        "namespaces": [],
        "symbols": [],
        "packages": [],
        "filePatterns": [],
        "methodCalls": [],
        "attributes": [],
        "metric": "MetricName",
        "operator": "Equal | NotEqual | GreaterThan | GreaterThanOrEqual | LessThan | LessThanOrEqual | In | NotIn | Contains | StartsWith | EndsWith | MatchesPattern",
        "value": 0
      }
    ],
    "groups": [
      {
        "match": "all | any | none",
        "conditions": [],
        "groups": []
      }
    ]
  },
  "impact": ["Impact statement"],
  "evidenceRequired": ["project", "file", "symbol", "line"],
  "source": ["url"],
  "recommendedArchonAction": ["UI/reporting/action"]
}
```

---

# Appendix B: Example Rule Pack

```json
{
  "rules": [
    {
      "id": "ARCHON-LIFE-001",
      "name": "Unsupported .NET Framework version",
      "category": "Lifecycle",
      "severity": "Critical",
      "status": "OutOfSupport",
      "enabled": true,
      "version": "1.0.0",
      "detect": {
        "nodeKinds": ["Project"],
        "match": "all",
        "conditions": [
          {
            "kind": "target-framework-membership",
            "targetFrameworks": ["net45", "net451", "net452", "net46", "net461"]
          }
        ]
      },
      "impact": [
        "Project targets a retired or unsupported .NET Framework version.",
        "Security fixes and technical support may not be available."
      ],
      "evidenceRequired": ["projectFile"],
      "source": [
        "https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-framework"
      ]
    },
    {
      "id": "ARCHON-DATA-001",
      "name": "LINQ to SQL DataContext",
      "category": "DataAccess",
      "severity": "High",
      "status": "Legacy",
      "enabled": true,
      "version": "1.0.0",
      "detect": {
        "nodeKinds": ["Project", "Type", "Method", "FilePath"],
        "match": "any",
        "conditions": [
          {
            "kind": "symbol",
            "symbols": ["System.Data.Linq.DataContext", "System.Data.Linq.Table<T>"]
          },
          {
            "kind": "file-pattern",
            "filePatterns": ["*.dbml"]
          },
          {
            "kind": "method-call",
            "methodCalls": ["SubmitChanges", "ExecuteQuery", "ExecuteCommand"]
          }
        ]
      },
      "impact": [
        "Indicates legacy .NET Framework data access.",
        "May constrain migration to modern .NET.",
        "Must be mapped before service extraction or database separation."
      ],
      "evidenceRequired": ["project", "file", "symbol", "line"]
    },
    {
      "id": "ARCHON-WEB-001",
      "name": "System.Web dependency",
      "category": "LegacyTechnology",
      "severity": "High",
      "status": "FrameworkOnly",
      "enabled": true,
      "version": "1.0.0",
      "detect": {
        "nodeKinds": ["Project", "Type", "Method"],
        "match": "any",
        "conditions": [
          {
            "kind": "namespace",
            "namespaces": ["System.Web"]
          }
        ]
      },
      "impact": [
        "Indicates dependency on classic ASP.NET / .NET Framework web stack.",
        "May constrain migration to modern .NET."
      ],
      "evidenceRequired": ["project", "sourceFile", "symbol"]
    },
    {
      "id": "ARCHON-OBSOLETE-SYSLIB0014",
      "name": "Legacy HTTP request APIs",
      "category": "ObsoleteApi",
      "severity": "Medium",
      "status": "Obsolete",
      "enabled": true,
      "version": "1.0.0",
      "detect": {
        "nodeKinds": ["Type", "Method"],
        "match": "all",
        "conditions": [
          {
            "kind": "symbol",
            "symbols": [
              "System.Net.WebRequest",
              "System.Net.HttpWebRequest",
              "System.Net.WebClient",
              "System.Net.ServicePointManager"
            ]
          }
        ]
      },
      "impact": [
        "Uses APIs marked obsolete with SYSLIB0014 in modern .NET.",
        "HttpClient is generally the expected migration path."
      ],
      "evidenceRequired": ["sourceFile", "symbol", "line"],
      "source": [
        "https://learn.microsoft.com/en-us/dotnet/fundamentals/syslib-diagnostics/syslib0014"
      ]
    },
    {
      "id": "ARCHON-SEC-001",
      "name": "BinaryFormatter usage",
      "category": "SecuritySensitive",
      "severity": "Critical",
      "status": "SecuritySensitive",
      "enabled": true,
      "version": "1.0.0",
      "detect": {
        "nodeKinds": ["Type", "Method"],
        "match": "all",
        "conditions": [
          {
            "kind": "symbol",
            "symbols": ["System.Runtime.Serialization.Formatters.Binary.BinaryFormatter"]
          }
        ]
      },
      "impact": [
        "BinaryFormatter is unsafe for untrusted data.",
        "Requires urgent review before modernization."
      ],
      "evidenceRequired": ["sourceFile", "symbol", "line"]
    },
    {
      "id": "ARCHON-CONFIG-001",
      "name": "packages.config dependency management",
      "category": "Configuration",
      "severity": "Medium",
      "status": "Legacy",
      "enabled": true,
      "version": "1.0.0",
      "detect": {
        "nodeKinds": ["FilePath", "Project"],
        "match": "all",
        "conditions": [
          {
            "kind": "file-pattern",
            "filePatterns": ["packages.config"]
          }
        ]
      },
      "impact": [
        "Indicates legacy NuGet package management.",
        "May complicate migration to SDK-style projects."
      ],
      "evidenceRequired": ["file"]
    },
    {
      "id": "ARCHON-ARCH-001",
      "name": "High fan-in shared library",
      "category": "DependencyRisk",
      "severity": "Medium",
      "status": "ModernizationBlocker",
      "enabled": true,
      "version": "1.0.0",
      "detect": {
        "nodeKinds": ["Project"],
        "match": "all",
        "conditions": [
          {
            "kind": "metric-threshold",
            "metric": "IncomingProjectReferences",
            "operator": "GreaterThan",
            "value": 20
          },
          {
            "kind": "metric-threshold",
            "metric": "ProjectFanInPercentile",
            "operator": "GreaterThanOrEqual",
            "value": 95
          }
        ]
      },
      "impact": [
        "Project is highly central.",
        "Changes may affect many applications.",
        "Requires impact analysis before refactoring."
      ],
      "evidenceRequired": ["metric", "projectReferenceEdges"]
    }
  ]
}
```

---

# Appendix C: Example MCP Tool Response Shape

```json
{
  "summary": "Customer.Application is a high fan-in project used by 18 projects.",
  "confidence": "High",
  "facts": [
    {
      "statement": "Customer.Application is referenced by Customer.Api.",
      "evidence": [
        {
          "filePath": "src/Customer.Api/Customer.Api.csproj",
          "line": 23,
          "kind": "ProjectReference"
        }
      ]
    }
  ],
  "findings": [
    {
      "ruleId": "ARCHON-ARCH-001",
      "severity": "Medium",
      "title": "High fan-in shared library"
    }
  ],
  "unknowns": [],
  "suggestedFollowUps": [
    "Show transitive dependents",
    "Show affected endpoints",
    "Show data access usage"
  ]
}
```

---

# Appendix D: Suggested First Copilot Prompt

```text
You are helping to build Archon.

Read the Archon concept brief.

Do not generate application code yet.

First produce a technical implementation specification for Phase 1:
- Aspire AppHost
- Neo4j graph storage
- extraction submission API accepting repository root and solution paths
- API module split for extraction, query, and management capabilities
- repository/solution/project snapshot model
- Roslyn solution loading
- project inventory extraction
- project reference extraction
- package reference extraction
- minimal API endpoints for querying projects
- minimal Blazor dashboard/project explorer
- initial tests

Use latest .NET and C#.
Do not omit implementation details for brevity.
```

---

# Appendix E: Full Graph Persistence and Extraction Specification

This appendix defines the detailed full-graph extraction and storage specification for Archon.

It is the authoritative definition of the architecture-wide persistence, extraction, evidence, findings, rules, and metrics model described by this brief.

## E.1 Overview

### E.1.1 Purpose

This specification defines Archon’s architecture-wide persistence and extraction model for full extraction, full evidence coverage, and full rule support across the complete capability set described by this brief.

This appendix defines the durable architecture model, persistence strategy, extraction contracts, and sequencing required so that all extraction domains fit into one stable architecture graph without repeated schema reshaping.

### E.1.2 Problem Statement

Archon must support all of the following extraction slices coherently within one model:

- project metadata
- project references
- package references
- semantic declarations
- structural relationships
- dependency relationships
- bounded evidence for those facts

The model must support all required extraction domains, especially:

- repository and solution modeling
- non-code artifacts such as configuration files, DBML, SQL scripts, pipeline files, Dockerfiles, OpenAPI files, and generated designer files
- runtime-facing concepts such as endpoints, controllers, hosted services, queues, topics, and external services
- data-access concepts such as DbContexts, LINQ to SQL contexts, entities, tables, columns, and stored procedures
- organization and modernization concepts such as rules, findings, metrics, and modernization or hotlist findings
- evidence parity across facts derived from code, metadata, configuration, diagnostics, and inference

The persistence model must therefore be broad enough that new extractors extend the architecture graph rather than forcing redesign.

### E.1.3 Scope

This appendix covers:

- the target architecture-wide model for nodes, edges, evidence, rules, findings, and metrics
- the Neo4j-native graph persistence design for labelled nodes, typed relationships, evidence, rules, findings, metrics, and generated summaries
- API-driven extraction contracts required to populate that model
- query and diff implications of the new model
- implementation sequencing and acceptance criteria

This appendix does not cover:

- migration from existing graph stores or older persistence designs
- backward compatibility with older persistence designs
- UI redesign beyond the data requirements imposed by the model
- MCP implementation details beyond the data requirements imposed by the model

### E.1.4 Non-Negotiable Constraints

The implementation defined by this specification must satisfy the following constraints:

1. The Neo4j graph may be dropped and recreated from scratch. No migration path from an older persistence design is required.
2. The model must be capable of supporting all missing or partial extraction capabilities required by the Archon vision.
3. The design must remain evidence-first. Every persisted architectural statement must be traceable to evidence.
4. Unknowns must be represented explicitly where evidence is insufficient.
5. The persistence model must support incremental delivery of new extraction slices without requiring repeated graph model redesign.
6. Neo4j remains the system of record for persisted extraction output.
7. Extraction must be initiated through the API by POSTing a repository root directory and a list of solution paths.

## E.2 Desired Outcome

### E.2.1 Target State

Archon persists extraction results using a general architecture graph model with first-class support for:

- repositories
- solutions
- projects
- packages
- namespaces
- types
- methods
- properties
- fields
- endpoints
- controllers
- hosted services
- configuration keys
- DbContexts
- LINQ to SQL contexts
- entities
- tables
- columns
- stored procedures
- external services
- queues
- topics
- files and artifact nodes
- rules
- findings
- metrics

The model must support both code-centric and non-code extraction domains without forcing them into semantic-symbol-only tables.

### E.2.2 Architectural Principle

The graph persistence model is based on:

- stable repository and solution models
- stable labelled architecture nodes
- stable typed architecture relationships
- a stable architecture evidence model
- dedicated findings, rules, and metrics models
- generated summary persistence
- extraction-specific metadata encoded in structured JSON where appropriate

The core principle is that extraction slices should add new node labels, relationship types, evidence kinds, findings, and metrics without requiring a new top-level persistence structure for each newly discovered concept.

### E.2.3 Success Criteria

This model is successful when:

1. The Neo4j graph model can represent every required extraction domain.
2. New extraction slices can be implemented by adding extraction logic and metadata mappings rather than redesigning persistence primitives.
3. Evidence parity is improved so that file path, line range, symbol identity, containing symbol, snippet preview, snippet fingerprint, and metadata can be recorded consistently.
4. Rules, findings, and metrics can be persisted and linked to extracted nodes, edges, and evidence.
5. Snapshot diff can operate across node, edge, finding, and metric domains rather than only a project-centric subset.

## E.3 High-Level Full-Graph Architecture

### E.3.1 Architecture Model Summary

The model uses the following foundational persistence concepts:

- repository nodes
- solution nodes
- snapshot nodes
- architecture concept nodes
- typed architecture relationships
- evidence nodes
- rule nodes
- finding nodes
- metric nodes
- generated summary nodes
- supporting relationship patterns and controlled value sets where required

These are the foundational persistence concepts of Archon.

### E.3.2 Logical Model

```text
Repositories / Solutions
        ↓
Snapshots
        ↓
Nodes
        ↓
Edges
        ↓
Evidence
        ↓
Findings / Metrics / Rules / GeneratedSummaries
        ↓
Query API / UI / MCP / Diff / Hotlist / Reports
```

### E.3.3 Design Decision

Archon uses:

- a labelled Neo4j node model for extracted architecture concepts
- typed Neo4j relationships for extracted architecture relationships
- evidence nodes and evidence relationships for all evidence instances
- dedicated labels and relationship patterns for rules, findings, metrics, and generated summaries
- JSON-compatible metadata properties for extraction-specific details that do not justify first-class graph properties

This is necessary because the required capability set spans too many heterogeneous concepts to encode safely in narrow semantic-only structures.

The persistence style is a **native graph model**:

- labelled nodes and typed relationships as the core architecture graph source of truth
- specialized labels and relationship patterns for behaviorally distinct or query-intensive domains such as rules, findings, and metrics

This preserves extensibility without forcing evaluative or computed concepts to masquerade as ordinary graph nodes.

## E.4 Functional Requirements

### E.4.1 Model Coverage Requirement

The model must support representation of all required domains, including but not limited to:

- repository and solution extraction
- broader artifact coverage
- full application-type classification
- full Roslyn language parity support
- additional semantic node kinds
- additional dependency and structural relationships
- ASP.NET runtime extraction
- .NET UI extraction for Blazor, Razor, Windows Forms, WPF, WinUI, .NET MAUI, and Avalonia
- worker and console extraction
- dependency injection extraction
- configuration extraction
- data-access extraction
- external integration extraction
- legacy technology detection
- rules, findings, and hotlist support
- evidence parity
- unknown and confidence classification
- metrics extraction
- snapshot diff support across all extracted domains

### E.4.2 First-Class Node Requirement

The model must support the following node kinds as first-class persisted concepts:

- Repository
- Solution
- Project
- Package
- Namespace
- Type
- Method
- Property
- Field
- Endpoint
- Controller
- HostedService
- UiApplication
- UiComponent
- UiPage
- UiView
- UiLayout
- UiRoute
- UiControl
- UiResource
- UiStyle
- ViewModel
- Command
- Binding
- ConfigurationKey
- DbContext
- LinqToSqlDataContext
- Entity
- DatabaseTable
- DatabaseColumn
- StoredProcedure
- ExternalService
- Queue
- Topic
- FilePath
- Pipeline
- OpenApiDocument
- Dockerfile
- SqlScript
- GeneratedArtifact

The implementation may support additional node kinds, but it must not support fewer than these.

### E.4.3 First-Class Edge Requirement

The model must support the following edge kinds as first-class persisted relationships:

- CONTAINS
- REFERENCES
- CALLS
- IMPLEMENTS
- INHERITS
- INJECTS
- EXPOSES
- HANDLES
- USES_CONFIG
- USES_DB_CONTEXT
- USES_LINQ_TO_SQL_CONTEXT
- MAPS_ENTITY
- MAPS_TABLE
- MAPS_COLUMN
- READS_TABLE
- WRITES_TABLE
- CALLS_STORED_PROCEDURE
- EXECUTES_RAW_SQL
- CALLS_EXTERNAL_SERVICE
- USES_PACKAGE
- DECLARES_ENDPOINT
- DECLARES_COMPONENT
- DECLARES_UI_ROUTE
- USES_COMPONENT
- USES_LAYOUT
- USES_CONTROL
- USES_UI_RESOURCE
- USES_STYLE
- BINDS_TO
- USES_COMMAND
- USES_VIEW_MODEL
- NAVIGATES_TO
- HANDLES_UI_EVENT
- CALLS_API
- REGISTERED_AS_SERVICE
- DEPENDS_ON

The system may support additional edge kinds, but these must be available without schema redesign.

### E.4.4 Evidence Requirement

Every persisted node, edge, finding, and metric that is not purely derived from other persisted facts must be linkable to evidence.

Evidence must support at minimum:

- evidence kind
- source file path
- start line
- end line
- symbol name
- containing symbol
- snippet fingerprint or hash
- snippet preview
- confidence
- explicit unknown state
- unknown reason
- metadata JSON

### E.4.5 Rule and Finding Requirement

The model must support:

- versioned rules
- enabled or disabled rules
- configurable rule payloads
- built-in and organisation-defined rules
- first-cut authored rules for all currently identified legacy detection scenarios
- suppressible findings
- findings linked to rules
- findings linked to one or more primary nodes
- findings linked to one or more evidence records
- first seen and latest seen tracking across snapshots
- status tracking
- severity tracking
- confidence tracking
- category tracking

The implementation must include first-cut rules for all currently identified legacy detection scenarios, including at minimum:

- unsupported or retired target frameworks
- legacy application technologies such as ASP.NET Web Forms, ASP.NET Web Pages, classic ASP.NET MVC 3/4/5, ASP.NET Web API 2, `System.Web`, `Global.asax`, `HttpModules`, `HttpHandlers`, WCF server applications, WCF clients, ASMX web services, Windows Workflow Foundation, .NET Remoting, Enterprise Services / COM+, ClickOnce deployment, classic Windows Services, Topshelf services, and OWIN/Katana startup
- legacy data-access technologies such as LINQ to SQL, `.dbml` files, `System.Data.Linq.DataContext`, `Table<T>`, `SubmitChanges()`, `ExecuteQuery<T>()`, `ExecuteCommand()`, Entity Framework Classic / EF6, Entity Framework Core, typed DataSets, `DataSet`, `DataTable`, `TableAdapter`, and ADO.NET `SqlCommand`
- obsolete API usage scenarios included in this concept brief
- security-sensitive usage scenarios included in this concept brief

These first-cut rules must be authored as rule-definition files under a repository-root `./rules` folder so they can be versioned, reviewed, and evolved as ordinary repository content.

### E.4.6 Metric Requirement

The model must support:

- project metrics
- graph metrics
- modernization metrics
- snapshot-scoped metric persistence
- node-scoped metric persistence
- optional edge-scoped metric persistence
- diffing of metric values across snapshots

### E.4.7 Classification Requirement

The model must support explicit knowledge classification for persisted facts.

At minimum, nodes, edges, evidence, and findings must support:

- Fact
- Inference
- Unknown
- HumanConfirmed

### E.4.8 Diff Requirement

The model must support snapshot diff across:

- nodes
- edges
- evidence where required for explanation
- findings
- metrics

This is necessary so later work can expose diffs for endpoints, routes, data contexts, configuration facts, data-access facts, and hotlist findings without additional schema redesign.

## E.5 Technical Specification

### E.5.1 Persistence Strategy

The Neo4j graph model is the architecture-wide persistence model defined by this document.

The implementation should:

1. use a Neo4j infrastructure adapter centered on architecture-wide graph primitives
2. preserve deterministic stable-key generation rules
3. ensure all persisted records remain snapshot-scoped
4. keep evidence, unknowns, rules, findings, and metrics as first-class persisted concepts

### E.5.2 Core Graph Elements

#### E.5.2.1 Repositories

Purpose:

- stores first-class repository identities independent of any one snapshot

Required properties:

- `Id`
- `StableKey`
- `Name`
- `RootPath`
- `RemoteUrl` nullable
- `DefaultBranch` nullable
- `MetadataJson`

#### E.5.2.2 Solutions

Purpose:

- stores first-class solution identities independent of any one snapshot

Required properties:

- `Id`
- `RepositoryId`
- `StableKey`
- `Name`
- `Path`
- `MetadataJson`

#### E.5.2.3 Snapshots

Purpose:

- records one extraction run
- provides snapshot identity for all nodes, edges, evidence, findings, and metrics

Required properties:

- `Id`
- `StableKey`
- `RepositoryId`
- `BranchName`
- `CommitSha`
- `StartedUtc`
- `CompletedUtc`
- `ExtractionVersion`
- `Status`
- `WarningsJson`
- `ErrorsJson`
- `MetadataJson`

#### E.5.2.4 Nodes

Purpose:

- stores all extracted architecture concepts uniformly

Required properties:

- `Id`
- `SnapshotId`
- `StableKey`
- `NodeKind`
- `DisplayName`
- `QualifiedName`
- `SearchName`
- `Language`
- `ProjectStableKey` nullable
- `ParentNodeStableKey` nullable
- `KnowledgeKind`
- `Ownership`
- `ExternalCategory`
- `Confidence`
- `HasUnknownData`
- `UnknownReason`
- `PrimaryEvidenceId` nullable
- `MetadataJson`
- `Fingerprint`

Key design notes:

- repositories and solutions are authoritative graph nodes
- repository and solution nodes must be physically materialized in Neo4j so they participate in traversal and diff
- `KnowledgeKind` is a first-class property so deterministic facts, inferences, explicit unknowns, and later human-confirmed facts can be filtered and reported consistently
- `Ownership`, `ExternalCategory`, `Confidence`, and explicit unknown-state fields are first-class properties rather than JSON metadata because they are expected to be primary query, filtering, reporting, and rule-evaluation dimensions across multiple extraction slices
- `NodeKind` must be a normalized string or enum-backed string to avoid future numeric drift issues
- `MetadataJson` must carry extraction-specific detail such as route templates, HTTP verbs, namespace imports, options type names, queue names, table schemas, and similar slice-specific payloads
- `ProjectStableKey` must remain nullable because repository, solution, and some cross-project concepts are not owned by a single project

#### E.5.2.5 Edges

Purpose:

- stores all extracted architecture relationships uniformly

Required properties:

- `Id`
- `SnapshotId`
- `StableKey`
- `EdgeKind`
- source node stable key
- target node stable key
- `IsDirect`
- `KnowledgeKind`
- `Confidence`
- `HasUnknownData`
- `UnknownReason`
- `PrimaryEvidenceId` nullable
- `MetadataJson`
- `Fingerprint`

Key design notes:

- `KnowledgeKind` is a first-class property so inferred relationships and explicit unknown relationships can be queried separately from directly evidenced facts
- `Confidence` and explicit unknown-state fields remain first-class properties on relationships because they are core traversal, filtering, reporting, and diff semantics rather than incidental extraction metadata
- edge metadata must support relationship-specific payloads such as occurrence count, access mode, registration lifetime, HTTP method, route source, read or write classification, or inference reason
- edges must not assume source-code semantics only

#### E.5.2.6 Evidence

Purpose:

- stores the evidence-first explanation layer

Evidence will be **fully deduplicated per snapshot**.

Consequences of this decision:

- one evidence row may be linked to multiple nodes, edges, and findings within the same snapshot
- identical evidence payloads within a snapshot must collapse to one canonical evidence row
- evidence stable keys and fingerprints must therefore be computed at snapshot scope
- deduplication is only required within a snapshot, not across snapshots

Required properties:

- `Id`
- `SnapshotId`
- `StableKey`
- `EvidenceKind`
- `FilePath`
- `StartLine` nullable
- `EndLine` nullable
- `SymbolName` nullable
- `ContainingSymbol` nullable
- `SnippetHash` nullable
- `SnippetPreview` nullable
- `KnowledgeKind`
- `Confidence`
- `HasUnknownData`
- `UnknownReason` nullable
- `MetadataJson`
- `Fingerprint`

The evidence table must support evidence from:

- project files
- source files
- configuration files
- DBML files
- designer-generated code
- SQL scripts
- pipeline files
- OpenAPI documents
- Dockerfiles
- generated artifacts
- package references
- compiler diagnostics
- inferred facts
- manual annotations

#### E.5.2.7 Rules

Purpose:

- stores the versioned rule catalog used for hotlist and findings generation

Rules will be modeled as **global catalog nodes**, not snapshot-scoped copies.

The authoritative authored form of the rule catalog will be **disk-backed rule-definition files under `./rules`**, not ad hoc in-database authoring.

Consequences of this decision:

- rules are authored once under `./rules` and reused across snapshots
- the `./rules` folder must be copied to the relevant build and publish outputs so runtime components can load rules from disk
- the API extraction module, API management module, and any other component that evaluates rules or exposes rule metadata must load rules from disk from the copied output content rather than relying on repository-relative source paths at runtime
- rule nodes persist the loaded catalog and version information used by the running system, but they are not the primary authoring source of truth
- findings must record the exact rule code and rule version used at evaluation time
- historical fidelity is preserved by versioning the catalog and stamping findings with the evaluated version
- repeated duplication of unchanged rule definitions across snapshots is avoided

Rule lifecycle requirements:

- rule definitions must be authored and reviewed as repository content under `./rules`
- the runtime rule engine must load rules from disk using the copied output content, validate them, and upsert them into `Rules` when rules are first referenced or during an equivalent explicit startup initialization path
- upsert identity must be based on rule code and version so the persisted catalog reflects the exact authored rule set that was available to the running system
- a rule definition that changes behaviour materially must publish a new version rather than mutating the meaning of an existing persisted version in place
- findings must continue to reference the exact rule code and rule version used during evaluation so historical results remain explainable even after newer rule versions are introduced
- removal of a rule from `./rules` must not require destructive deletion of historical catalog or finding data; the runtime may instead mark the persisted rule as disabled, inactive, superseded, or otherwise non-current according to the final implementation model
- enabled or disabled status in the persisted graph catalog must support temporarily disabling a rule without erasing its history

Runtime loading requirements:

- a repository-root `./rules` folder must exist
- first-cut rule files for all currently known legacy detection scenarios must be stored in that folder
- the projects that need rules at runtime, including at minimum the API host and API modules, must copy the `./rules` content to their bin and publish outputs
- rule loading must resolve from disk using the copied output content so local development, test execution, and published deployments use the same loading model
- rule-file parsing and runtime loading must use one shared rule-loading component so rule resolution semantics do not drift between hosts

Required properties:

- `Id`
- `RuleCode`
- `Name`
- `Category`
- `Severity`
- `DefaultStatus`
- `Enabled`
- `Version`
- `Description`
- `DefinitionJson`
- `SourceUrlsJson`
- `IsBuiltIn`
- `OwnerScope`
- `MetadataJson`

Key design notes:

- rule nodes form a persisted runtime catalog, query surface, and historical reference, but not the primary authoring store
- the canonical authored form of each rule lives on disk under `./rules`
- the system must be able to determine whether a disk-authored rule already exists in the persisted catalog for the same rule code and version
- the system must support loading a newly introduced rule version alongside older persisted versions of the same rule code so findings can preserve evaluation history
- the final implementation may add columns such as content hash, loaded-at timestamp, source path, inactive status, superseded-by version, or similar catalog-management fields if they materially improve synchronization and operational clarity

#### E.5.2.8 Findings

Purpose:

- stores rule evaluation output and other persisted findings

Required properties:

- `Id`
- `SnapshotId`
- `StableKey`
- `RuleCode`
- `RuleVersion`
- `Severity`
- `Status`
- `Title`
- `Description`
- `KnowledgeKind`
- `Confidence`
- `PrimaryNodeStableKey` nullable
- `PrimaryEvidenceId` nullable
- `FirstSeenSnapshotId` nullable
- `LatestSeenSnapshotId` nullable
- `SuppressionReason` nullable
- `SuppressedBy` nullable
- `MetadataJson`
- `Fingerprint`

#### E.5.2.9 Metrics

Purpose:

- stores computed quantitative architecture data

Metrics will be **snapshot-computed and persisted as first-class rows**.

Consequences of this decision:

- metrics are part of the durable extraction output, not a transient query-time convenience
- snapshot diff can compare metric fingerprints and values directly without recomputing historical states
- UI, API, MCP, and reporting consumers can rely on stable persisted metric values for the same snapshot
- expensive graph or modernization calculations can be performed once during extraction rather than repeatedly at query time

Required properties:

- `Id`
- `SnapshotId`
- `StableKey`
- `MetricKind`
- `ScopeKind`
- `NodeStableKey` nullable
- `EdgeStableKey` nullable
- `PrimaryEvidenceId` nullable
- `Name`
- `NumericValue` nullable
- `TextValue` nullable
- `Unit` nullable
- `MetadataJson`
- `Fingerprint`

#### E.5.2.10 GeneratedSummaries

Purpose:

- stores generated architecture summaries and exported narrative artifacts derived from the persisted model

Required properties:

- `Id`
- `SnapshotId`
- `StableKey`
- `SummaryKind`
- `TargetStableKey` nullable
- `Format`
- `Title`
- `Content`
- `MetadataJson`
- `Fingerprint`

### E.5.3 Supporting Relationships

The model must include explicit relationship patterns where many-to-many relationships are expected.

Required supporting relationship patterns:

- `Snapshot` to `Solution`
- architecture node to evidence
- architecture relationship to evidence
- metric to evidence
- finding to evidence
- finding to architecture node

These relationships are required because one snapshot may cover multiple solutions and because one node, relationship, metric, or finding may have multiple supporting evidence records.

### E.5.4 Stable Key Strategy

Stable keys must remain deterministic and independent of Neo4j internal IDs.

Required prefixes include:

- `repository://`
- `solution://`
- `project://`
- `package://`
- `namespace://`
- `type://`
- `method://`
- `property://`
- `field://`
- `endpoint://`
- `controller://`
- `hostedservice://`
- `config://`
- `dbcontext://`
- `linqtosql://`
- `entity://`
- `dbtable://`
- `dbcolumn://`
- `storedprocedure://`
- `externalservice://`
- `queue://`
- `topic://`
- `file://`
- `pipeline://`
- `rule://`
- `finding://`
- `metric://`
- `summary://`

Stable-key generation must be documented and implemented in one shared component so all extraction slices use the same rules.

### E.5.5 Metadata Strategy

The model must balance normalization with extensibility.

The following must be normalized graph properties:

- snapshot identity
- stable keys
- node kind
- relationship kind
- evidence kind
- knowledge kind
- rule code
- severity
- status
- confidence
- unknown-state indicators
- primary file and line information

The following may be stored in JSON metadata:

- route templates and route tokens
- HTTP verb sets
- options binding details
- configuration provider names
- connection string names
- SQL classification hints
- queue or topic transport metadata
- DI registration lifetime and registration path
- table schema details beyond the stable key
- provider-specific database mapping payloads
- extraction-specific classification annotations

### E.5.6 API Extraction Contract

Extraction is initiated through the API by submitting a repository root directory and an explicit list of solution paths.

Request contract:

- `StartExtractionRequest`
  - `RepositoryRootDirectory`
  - `SolutionPaths`
  - `BranchName` optional
  - `CommitSha` optional
  - `RequestedBy` optional
  - `Metadata`

The API extraction module validates that the request identifies one repository root and at least one solution path before any Roslyn workspace loading begins.

The API extraction module exposes a single durable architecture extraction result contract with the following shape:

- repositories
- solutions
- snapshot header data
- nodes
- edges
- evidence
- findings
- metrics
- generated summaries
- warnings
- errors

Contract:

- `ExtractedArchitectureSnapshot`
  - `Repositories`
  - `Solutions`
  - `Snapshot`
  - `Nodes`
  - `Edges`
  - `Evidence`
  - `Findings`
  - `Metrics`
  - `GeneratedSummaries`
  - `Warnings`
  - `Errors`

The contract allows multiple API extraction slices to contribute facts into a single snapshot assembly pipeline.

Generated summaries may be empty during initial extraction and populated by a later post-persistence summarization/export step, but they remain part of the authoritative snapshot contract because they are persisted as snapshot-owned outputs.

Rule definitions themselves do not need to be embedded directly into `ExtractedArchitectureSnapshot`, because they are loaded from disk through the shared rule loader and then persisted into the `Rules` catalog and referenced by findings using rule code and version.

### E.5.7 Extraction Pipeline

The API-driven extraction pipeline consists of the following composable stages:

1. extraction request validation stage
2. repository and submitted solution resolution stage
3. project inventory stage
4. code semantic extraction stage
5. runtime or application extraction stage
6. .NET UI extraction stage
7. configuration extraction stage
8. data-access extraction stage
9. integration extraction stage
10. rule-definition load stage from disk-backed `./rules` content
11. rules evaluation stage
12. metrics calculation stage
13. snapshot assembly stage
14. Neo4j persistence stage
15. markdown generation stage
16. MCP resource refresh stage

Each stage must emit nodes, edges, evidence, findings, and metrics into a shared accumulation model.

Markdown generation and MCP resource refresh run after persistence and may consume the persisted snapshot rather than contributing new extraction facts. When generated markdown summaries are persisted, they are written as generated summary nodes linked to the snapshot that produced them.

The rule-definition load stage must use the same shared rule-loading component across API modules and any other runtime that needs access to the authored rule set.

The synchronization model must be explicit:

- authored rules are changed on disk under `./rules`
- build and publish copy those rules to runtime output locations
- runtime components load and validate those rules from disk
- runtime components upsert the loaded rules into Neo4j rule nodes
- findings reference the persisted rule code and version that were active for that evaluation

### E.5.8 Query Model

The query layer is based on the architecture-wide model.

At minimum, the new model must support efficient query patterns for:

- project catalogue
- project details
- symbol lookup
- dependency traversal
- endpoint lookup
- worker lookup
- UI application, component, view, route, binding, command, and navigation lookup
- data-access lookup
- configuration usage lookup
- finding and hotlist lookup
- evidence drill-down
- snapshot diff for nodes, edges, findings, and metrics

### E.5.9 Diff Strategy

Diffing must compare fingerprints and stable keys at the node, edge, finding, and metric levels.

The design must support the following change kinds:

- added
- removed
- changed
- unchanged

A record is considered changed when:

- the stable key is the same
- but the normalized fingerprint differs

This allows endpoint route changes, metric changes, finding severity changes, and metadata changes to be surfaced without inventing new diff mechanisms per feature area.

## E.6 Capability Coverage

### E.6.1 Repository and Solution Modeling

Supported by:

- repository and solution graph nodes
- architecture concept nodes with `Repository` and `Solution` node kinds
- typed relationships with `CONTAINS` and `REFERENCES`
- `Evidence` for discovery evidence and path evidence

### E.6.2 Configuration Extraction

Supported by:

- `ConfigurationKey` node kind
- `USES_CONFIG` edges
- `FilePath` nodes for configuration artifacts where useful
- evidence kinds for configuration files
- JSON metadata for provider names, environment, binding source, and inferred endpoint classification

### E.6.3 Runtime Extraction

Supported by:

- `Endpoint`, `Controller`, and `HostedService` node kinds
- `DECLARES_ENDPOINT`, `EXPOSES`, `HANDLES`, and `DEPENDS_ON` edges
- metadata for route templates, authorization, transport, and scheduling details

### E.6.4 .NET UI Extraction

Supported by:

- `UiApplication`, `UiComponent`, `UiPage`, `UiView`, `UiLayout`, `UiRoute`, `UiControl`, `UiResource`, `UiStyle`, `ViewModel`, `Command`, and `Binding` node kinds
- `DECLARES_COMPONENT`, `DECLARES_UI_ROUTE`, `USES_COMPONENT`, `USES_LAYOUT`, `USES_CONTROL`, `USES_UI_RESOURCE`, `USES_STYLE`, `BINDS_TO`, `USES_COMMAND`, `USES_VIEW_MODEL`, `NAVIGATES_TO`, `HANDLES_UI_EVENT`, `CALLS_API`, `USES_CONFIG`, and `DEPENDS_ON` edges
- evidence kinds for Razor, XAML, AXAML, designer files, source code, project files, resources, and generated UI artifacts
- metadata for UI framework, route templates, layout relationships, binding paths, command names, control names, resource keys, startup forms, Shell routes, render modes, platform heads, and view-model conventions

### E.6.5 Dependency Injection Extraction

Supported by:

- `REGISTERED_AS_SERVICE`, `INJECTS`, and `DEPENDS_ON` edges
- metadata for service lifetime and registration source
- evidence for registration call sites and wrapper extension methods

### E.6.6 Data-Access Extraction

Supported by:

- `DbContext`, `LinqToSqlDataContext`, `Entity`, `DatabaseTable`, `DatabaseColumn`, and `StoredProcedure` node kinds
- `USES_DB_CONTEXT`, `USES_LINQ_TO_SQL_CONTEXT`, `MAPS_ENTITY`, `MAPS_TABLE`, `MAPS_COLUMN`, `READS_TABLE`, `WRITES_TABLE`, `CALLS_STORED_PROCEDURE`, and `EXECUTES_RAW_SQL` edges
- evidence kinds for DBML, designer code, source code, SQL scripts, and inference

### E.6.7 External Integration Extraction

Supported by:

- `ExternalService`, `Queue`, and `Topic` node kinds
- `CALLS_EXTERNAL_SERVICE` and `HANDLES` edges
- metadata for transport, client type, base URL key, and authentication hints

### E.6.8 Hotlist, Rules, and Findings

Supported by:

- `Rules`
- `Findings`
- graph relationships connecting findings to architecture nodes and evidence
- metric support for hotspot counts and modernization indicators
- disk-backed rule-definition files under `./rules` as the authored source of rule content

### E.6.9 Metrics

Supported by:

- `Metrics`
- scope-aware metric rows for snapshot, node, and edge scope
- fingerprint-based diffing

### E.6.10 Generated Summary Persistence

Supported by:

- `GeneratedSummaries`
- stable summary kinds for exported markdown and narrative outputs
- snapshot and target linkage so generated summaries can be traced back to the model state that produced them

## E.7 Implementation Plan

### E.7.1 Phase A - Persistence Foundation

Deliver:

- Neo4j graph model
- graph constraints and indexes for repositories, solutions, snapshots, architecture nodes, relationships, evidence, rules, findings, metrics, and generated summaries
- stable-key helpers
- fingerprint helpers
- snapshot writer for the graph model

Acceptance criteria:

- the Neo4j graph can be created from scratch
- one snapshot can persist mixed node and edge kinds
- one node or edge can link to multiple evidence records
- findings and metrics can be persisted in the same snapshot

### E.7.2 Phase B - Project and Code Slice Implementation

Deliver:

- repository and solution nodes
- project and package nodes
- semantic extraction into the node, edge, and evidence model
- field and property support
- compiler diagnostics support
- attribute analysis support

Acceptance criteria:

- project and semantic extraction capabilities are represented in the architecture-wide model without loss
- project and semantic facts are served from the architecture-wide model

### E.7.3 Phase C - Runtime and Configuration Slice Enablement

Deliver:

- configuration nodes and edges
- endpoint, controller, and hosted-service nodes and edges
- DI registration edges
- evidence support for configuration and runtime artifacts

Acceptance criteria:

- the model can persist configuration, runtime, and DI facts without schema changes

### E.7.4 Phase D - Data Access and Integration Slice Enablement

Deliver:

- data-access nodes and edges
- external integration nodes and edges
- support for DBML, designer, SQL, and transport metadata

Acceptance criteria:

- LINQ to SQL, Entity Framework Classic / EF6, Entity Framework Core, ADO.NET, and integration facts can all be stored in the same generalized model

### E.7.5 Phase E - Findings and Metrics Enablement

Deliver:

- first-cut rule-definition files under `./rules` for all currently identified legacy detection scenarios
- shared disk-based rule-loading component
- copy-to-output and copy-to-publish wiring for the relevant runtime projects so rule files are available from disk at runtime
- graph-backed rule catalog persistence
- finding persistence
- metric persistence
- node, edge, finding, and metric diff support

Acceptance criteria:

- first-cut legacy detection rules exist for all scenarios currently identified by the Archon source briefs and parity requirements
- the authored rules are loaded from disk from copied output content rather than from hard-coded repository-relative paths
- hotlist and modernization slices can be persisted and diffed without schema changes

## E.8 Risks and Mitigations

### E.8.1 Risk: Over-normalization slows delivery

Mitigation:

- promote only identity, scope, and query-critical values to first-class graph properties
- place slice-specific details in JSON metadata
- provide clear metadata contracts per extraction slice

### E.8.2 Risk: Generic model becomes vague and hard to query

Mitigation:

- keep node kind, edge kind, severity, status, confidence, and ownership strongly typed
- define approved metadata keys per extraction slice
- enforce stable-key prefixes and fingerprint rules centrally

### E.8.3 Risk: Query performance degrades as model broadens

Mitigation:

- create graph indexes and constraints for snapshot plus stable-key combinations
- index node kind and relationship kind by snapshot where query patterns require it
- index project ownership where relevant
- index source and target stable keys for traversal hand-offs
- index rule code and severity for hotlist lookup
- index metric kind and scope for reporting

### E.8.4 Risk: Extractor implementation becomes difficult to evolve

Mitigation:

- introduce a shared extraction assembly model first
- implement one slice at a time against the architecture-wide model
- keep projections and query surfaces aligned to the architecture-wide source of truth

## E.9 Acceptance Criteria

This specification is complete when all of the following are true:

1. A Neo4j graph model exists that can be created from scratch and used as the sole persistence model for extraction output.
2. The graph model includes architecture-wide node, relationship, evidence, rule, finding, and metric support.
3. The graph model can represent every feature area in the Archon vision without requiring another foundational graph redesign.
4. The API extraction module exposes a POST-driven extraction contract accepting a repository root directory and solution path list, then produces a generalized snapshot contract rather than a project-only aggregate contract.
5. Evidence can be attached consistently to nodes, edges, and findings.
6. Fact, inference, unknown, and later human-confirmed classifications can be represented consistently across persisted facts.
7. Unknowns and confidence can be represented consistently across extraction slices.
8. Snapshot diff can operate over nodes, edges, findings, and metrics.
9. The design explicitly assumes graph recreation and does not include migration requirements.
10. First-cut legacy detection rules exist under `./rules`, are copied to the relevant runtime outputs, and can be loaded from disk by the API modules.

## E.10 Out of Scope

The following are intentionally out of scope for this specification:

- migration of existing graph stores or older persistence designs
- backward compatibility with existing query projections
- delivery of every remaining extractor in the same work package
- UI redesign beyond what is necessary to consume the new model later
- MCP implementation details beyond persistence support

## E.11 Final-State Architectural Decisions

### E.11.1 Repository and Solution Materialization

Repository and solution concepts are persisted physically as first-class graph nodes.

This ensures:

- repository and solution concepts participate in graph traversal and diff as ordinary graph nodes
- the architecture graph is materially complete in storage rather than partly projected at query time
- graph nodes remain authoritative for repository and solution identity and metadata
- repository and solution nodes are kept consistent during snapshot assembly
