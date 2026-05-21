# Archon UI Brief
## Search, Lenses, Graph Slicing, and Architectural Health Visualisation

**Document purpose:**  
This brief describes the intended user interface for **Archon**, the Roslyn-powered, Neo4j-backed architecture intelligence platform. It extends the earlier architectural health visualisation notes into a product-facing UI brief.

**Primary UI goal:**  
Archon should let users find any architectural artefact, ask useful architectural questions about it, inspect a scoped slice of the architecture graph, and follow every claim back to evidence.

**Core interaction model:**

```text
Find anything.
Select a thing.
Choose a lens.
Inspect the graph slice.
Follow the evidence.
Export or hand off the result.
```

---

# 1. Product Positioning

Archon is not just a graph viewer. It is an **architectural investigation interface** over a deterministic Architecture Semantic Graph.

The UI should help users answer practical questions:

```text
What is this?
What depends on it?
What does it depend on?
Which code reaches this database table?
Which database tables does this project touch?
Which endpoints reach this class or table?
Which projects reference this class?
What would be affected if this changed?
Where is the evidence?
Why might this be risky?
Which legacy technologies are involved?
What should Copilot know before changing this?
```

The UI should avoid the common failure mode of graph tools: rendering a visually impressive but unusable hairball. Archon should instead provide **purpose-built projections** of the graph.

The guiding principle is:

```text
One architecture graph.
Many focused lenses.
Consistent health overlays.
Evidence everywhere.
```

---

# 2. Core UI Concept: Archon Lenses

A **Lens** is a reusable architectural question applied to a selected artefact.

A user does not start by choosing a graph algorithm. They start by selecting something they care about, such as a project, class, endpoint, configuration key, database table, package, or UI route. Archon then offers relevant lenses for that artefact.

```text
Selected thing
      ↓
Available lenses
      ↓
Graph slice / table / path / matrix / report
      ↓
Evidence and follow-up actions
```

A lens is not tied to one visual representation. The same lens may initially show a graph, table, matrix, path diagram, or grouped report depending on the question.

Example:

```text
Selected thing: project://src/Legacy.Customer.Web/Legacy.Customer.Web.csproj
Lens: Data Access
Question: Which database tables are referenced by code in this project?
```

Archon may render:

- a grouped table of tables read/written;
- a path graph from project to method to data context to table;
- a summary panel of unknowns and dynamic SQL;
- an evidence list of files and line spans;
- follow-up actions such as “who else writes this table?”

---

# 3. Search-First Selection Experience

The “select a thing” step should be a fast search experience.

The search box should be available globally and should behave like a command palette for the architecture graph.

## 3.1 Search goals

The search experience should let users find:

```text
Projects
Solutions
Repositories
Packages
Namespaces
Types/classes/interfaces
Methods
Endpoints
Controllers
Hosted services
UI applications
UI pages/routes/views/components
View models
Commands
Configuration keys
DbContexts
LINQ to SQL DataContexts
Entities
Database tables
Database columns
Stored procedures
External services
Queues/topics
Pipelines
Dockerfiles
OpenAPI documents
Findings/rules
Evidence
```

## 3.2 Search result grouping

Search results should be grouped by node kind.

Example search: `Customer`

```text
Projects
  Customer.Api
  Customer.Application
  Legacy.Customer.Web

Types
  CustomerService
  CustomerRepository
  CustomerDataContext

Methods
  CustomerService.GetCustomerAsync(int)

Endpoints
  GET /api/customers/{id}

Database tables
  dbo.Customer
  dbo.CustomerAddress

Configuration
  ConnectionStrings:CustomerDatabase
```

## 3.3 Search result actions

A search result should not only navigate to a detail page. It should expose contextual lens actions.

Example for a database table:

```text
dbo.Customer    DatabaseTable

Actions:
  Readers
  Writers
  All code paths
  Endpoints reaching this table
  UI paths reaching this table
  Impact of schema change
  Evidence
```

Example for a project:

```text
Legacy.Customer.Web    Project

Actions:
  Dependencies
  Dependents
  Data access
  Endpoints
  Configuration
  Legacy technology
  Rule violations
  Impact
  Evidence
```

## 3.4 Search implementation notes

Archon should use Neo4j full-text indexes for deterministic search over node properties.

Recommended indexed properties:

```text
StableKey
NodeKind
DisplayName
QualifiedName
SearchName
FilePath
SymbolName
RouteTemplate
HttpMethod
TableSchema
TableName
PackageName
TargetFramework
ApplicationType
```

The API should expose search as a first-class query endpoint:

```http
GET /api/search?q=customer&snapshotId=current
```

The response should include enough information for the UI to show badges and available lenses:

```json
{
  "query": "customer",
  "results": [
    {
      "stableKey": "project://src/Customer.Api/Customer.Api.csproj",
      "nodeKind": "Project",
      "displayName": "Customer.Api",
      "qualifiedName": "src/Customer.Api/Customer.Api.csproj",
      "score": 4.91,
      "badges": ["ASP.NET Core Web API", ".NET 9"],
      "availableLenses": [
        "dependencies",
        "dependents",
        "data-access",
        "impact",
        "rule-violations"
      ]
    }
  ]
}
```

Later, Archon may add vector or semantic search over summaries, generated documentation, ADRs, and natural-language descriptions. The initial implementation should remain deterministic and evidence-backed.

---

# 4. Graph Slicing Model

A graph slice is a scoped projection of the Architecture Semantic Graph generated for a specific question.

Archon should not send the entire graph to the browser. The backend should return visual slices, projections, summaries, and precomputed metrics.

## 4.1 Graph Slice Definition

A slice definition describes:

- the selected starting node;
- the lens/question being applied;
- allowed node kinds;
- allowed edge kinds;
- traversal direction;
- maximum depth;
- collapse/expand rules;
- grouping;
- overlays;
- evidence mode.

Conceptual example:

```json
{
  "snapshotId": "current",
  "start": {
    "stableKey": "project://src/Legacy.Customer.Web/Legacy.Customer.Web.csproj"
  },
  "question": "project.databaseUsage",
  "direction": "outgoing",
  "maxDepth": 5,
  "includeNodeKinds": [
    "Project",
    "Type",
    "Method",
    "LinqToSqlDataContext",
    "DbContext",
    "Entity",
    "DatabaseTable",
    "StoredProcedure"
  ],
  "includeEdgeKinds": [
    "CONTAINS",
    "CALLS",
    "USES_LINQ_TO_SQL_CONTEXT",
    "USES_DB_CONTEXT",
    "MAPS_ENTITY",
    "MAPS_TABLE",
    "READS_TABLE",
    "WRITES_TABLE",
    "CALLS_STORED_PROCEDURE",
    "EXECUTES_RAW_SQL"
  ],
  "collapse": {
    "Method": true,
    "Type": false
  },
  "groupBy": "DatabaseTable",
  "riskOverlay": true,
  "evidenceMode": "summary"
}
```

The same graph slice definition should be usable by:

```text
Archon UI graph views
Archon UI table views
MCP tools
Markdown exports
Impact reports
AI prompt context generation
```

## 4.2 Why this matters

The graph slice is the core reusable product abstraction. It prevents the UI, API, MCP server, and export logic from implementing different versions of the same architectural question.

---

# 5. Primary Lens Catalogue

The following lenses should form the first coherent Archon UI experience.

---

## 5.1 Overview Lens

**Purpose:** Answer “what is this?”

Available for most node kinds.

Shows:

- display name;
- stable key;
- node kind;
- owning project/repository/solution;
- primary evidence;
- confidence;
- unknown flags;
- important metrics;
- findings;
- available follow-up lenses.

This is the default detail view after selecting an artefact.

---

## 5.2 Dependency Lens

**Purpose:** Answer “what depends on what?”

Useful for projects, packages, namespaces, types, services, endpoints, and UI components.

Questions:

```text
What does this depend on?
What depends on this?
What are the direct dependencies?
What are the transitive dependencies?
What dependency path connects A to B?
Which dependencies cross architectural boundaries?
Which dependencies are forbidden or risky?
```

Visual forms:

- scoped node-link graph;
- dependency table;
- path graph;
- adjacency matrix for dense project-level coupling.

Controls:

```text
Direction: inbound / outbound / both
Depth: 1..n
Node kind filters
Edge kind filters
Hide tests
Hide generated code
Hide external packages
Show only risky edges
Show only rule violations
```

---

## 5.3 Data Access Lens

**Purpose:** Answer “what data does this code touch?”

Useful for projects, types, methods, endpoints, UI routes, workers, DbContexts, LINQ to SQL DataContexts, entities, and database tables.

Questions from a project/type/method:

```text
Which database tables does this project read?
Which database tables does this project write?
Which stored procedures does this project call?
Which DataContexts or DbContexts are involved?
Where is raw SQL used?
Where is dynamic SQL detected?
Which code paths reach a particular table?
```

Questions from a table:

```text
Which projects read this table?
Which projects write this table?
Which methods access this table?
Which DataContexts or DbContexts map this table?
Which entities map to this table?
Which endpoints can reach this table?
Which UI screens can reach this table?
Which legacy components depend on this table?
What would be affected if this table changed?
```

Visual forms:

- grouped table of tables read/written;
- project-to-table path graph;
- endpoint-to-table path graph;
- table usage matrix;
- blast-radius view for schema change.

Important distinction:

```text
READS_TABLE and WRITES_TABLE should be shown separately.
Unknown/dynamic SQL should be visible, not hidden.
```

---

## 5.4 Impact Lens

**Purpose:** Answer “if I change this, what is likely to be affected?”

This is the investigative “what if” lens.

It does not initially simulate code changes. It shows evidence-backed likely impact based on known graph relationships.

Questions:

```text
What is directly affected?
What is indirectly affected?
Does impact cross bounded contexts?
Does impact cross team ownership?
Are legacy components in the impact path?
Are tests near the affected area?
Which endpoints, UI screens, workers, or database tables might be affected?
Which unknowns reduce confidence?
```

Visual form:

```text
Ring 0: selected artefact
Ring 1: direct impact
Ring 2: likely indirect impact
Ring 3: possible wider impact
```

Controls:

```text
Toggle static dependencies
Toggle runtime dependencies
Toggle data access
Toggle configuration
Toggle tests
Toggle ownership
Toggle documentation/ADRs
Change traversal depth
Export impact report
```

---

## 5.5 Path Lens

**Purpose:** Answer “how does A reach B?”

Questions:

```text
How does this endpoint reach this table?
How does this UI route reach this API?
How does this project depend on this package?
How does this class eventually call this method?
How does this legacy component affect this modern project?
```

Visual form:

- one or more ranked paths;
- path confidence score;
- edge-by-edge evidence;
- alternate path list;
- shortest path / most confident path / highest-risk path toggles.

Example:

```text
CustomerSearchPage
  → CustomerApiClient.SearchAsync
  → GET /api/customers/search
  → CustomerController.Search
  → CustomerService.SearchAsync
  → CustomerDataContext.Customers
  → dbo.Customer
```

---

## 5.6 Rule Violation Lens

**Purpose:** Answer “where does this slice break the intended architecture?”

Questions:

```text
Which rules are violated by this project?
Which dependencies violate layering?
Which table access is forbidden?
Which components bypass intended boundaries?
Which violations are new in this snapshot?
Which violations are tolerated exceptions?
```

Visual forms:

- filtered violation graph;
- rule finding table;
- grouped by severity/category;
- overlay on dependency, layer, or data-access graph.

Encodings:

```text
Red edge: violation
Warning badge: node with violation
Violation count badge: container/cluster
Ghosted valid edges: spotlight mode
Severity colour: critical/high/medium/low/info
```

---

## 5.7 Legacy Technology Lens

**Purpose:** Answer “where is the legacy technology, and how trapped are we by it?”

Questions:

```text
Which projects use .NET Framework?
Which projects use Web Forms, WCF, ASMX, LINQ to SQL, EF6, ADO.NET, or old project formats?
Which modern projects depend on legacy projects?
Which legacy components are central?
Which legacy areas still have active churn?
Which dependencies block migration?
Where are adapter or strangler boundaries possible?
```

Visual forms:

- legacy island map;
- dependency graph with legacy overlay;
- modernisation hotlist table;
- migration blocker graph;
- impact view from legacy component.

---

## 5.8 Layer Compliance Lens

**Purpose:** Answer “does the implementation follow the intended architecture?”

This should use deliberate layout rather than force-directed layout.

Typical swimlanes:

```text
UI
API
Application
Domain
Infrastructure
Database / External Systems
```

Questions:

```text
Does UI depend on Infrastructure?
Does Domain depend on persistence details?
Are controllers bypassing Application services?
Are database tables accessed from inappropriate layers?
Which dependencies go upward?
Which exceptions are tolerated?
```

Visual form:

- layered graph;
- swimlanes;
- red upward/forbidden edges;
- dashed tolerated exceptions;
- thick red high-impact violations.

Recommended renderer: ELK/elkjs or equivalent layered layout engine.

---

## 5.9 Endpoint-to-Data Lens

**Purpose:** Answer “what happens from runtime entry point to data?”

Questions:

```text
Which database tables can this endpoint reach?
Which stored procedures can this endpoint call?
Which services are involved in this request path?
Which configuration keys affect this path?
Which external services are called?
Which UI routes call this endpoint?
```

Visual forms:

- path graph;
- Sankey-style flow view;
- grouped endpoint/data table;
- evidence trail.

Example path:

```text
Endpoint
  → Controller / Handler
  → Application Service
  → Domain Service
  → Repository / DataContext
  → Table / Stored Procedure
```

---

## 5.10 Configuration Lens

**Purpose:** Answer “which configuration affects this artefact?”

Questions:

```text
Which configuration keys does this project use?
Which code uses this connection string?
Which endpoints depend on this external service URL?
Which projects read the same configuration key?
Which configuration values are unknown or environment-supplied?
```

Visual forms:

- grouped table;
- config-to-project reverse lookup;
- path graph from endpoint/project to configuration key;
- unknowns panel.

---

## 5.11 UI Flow Lens

**Purpose:** Answer “which user-facing screens lead to which backend behaviours?”

Relevant after .NET UI extraction exists.

Questions:

```text
Which UI pages call this API?
Which UI screens eventually reach this database table?
Which commands or event handlers trigger this backend method?
Which view models depend on this service?
Which bindings depend on this property or command?
```

Visual forms:

- UI route to API path;
- UI component dependency graph;
- UI-to-data path graph;
- grouped list of screens by backend dependency.

---

## 5.12 Timeline / Snapshot Diff Lens

**Purpose:** Answer “is the architecture getting better or worse?”

Questions:

```text
What changed since the previous snapshot?
Which dependencies are new?
Which rule violations are new or resolved?
Is coupling increasing?
Is legacy usage decreasing?
Are hotspots moving?
Did this project become more central?
```

Visual forms:

- snapshot diff table;
- before/after graph;
- timeline of metrics;
- trend cards;
- small multiples.

---

# 6. User Interaction Patterns

## 6.1 Search → Lens → Slice

Primary flow:

```text
1. User opens global search.
2. User searches for an artefact.
3. User selects a result.
4. Archon shows available lenses.
5. User chooses a lens.
6. Archon renders the scoped slice.
7. User clicks nodes/edges to inspect evidence.
8. User pivots to another lens or exports the result.
```

## 6.2 Pivoting

Every selected node inside a slice should become a new starting point.

Example:

```text
Project → Data Access Lens → dbo.Customer
```

User clicks `dbo.Customer`, then pivots to:

```text
Readers
Writers
Endpoint paths
UI paths
Impact of schema change
Evidence
```

This gives Archon the feel of an investigation workspace rather than a set of disconnected pages.

## 6.3 Explain this slice

Every slice should have an explanation panel:

```text
Showing database usage for Legacy.Customer.Web

Start:
  Project = Legacy.Customer.Web

Traversed:
  Project → Type → Method → DataContext → Entity → DatabaseTable

Included edge kinds:
  CALLS
  USES_LINQ_TO_SQL_CONTEXT
  USES_DB_CONTEXT
  MAPS_TABLE
  READS_TABLE
  WRITES_TABLE
  CALLS_STORED_PROCEDURE

Excluded:
  Test projects
  Generated-only evidence
  Low-confidence dynamic SQL

Confidence:
  81% high-confidence relationships
  12% inferred relationships
  7% unknown/dynamic relationships
```

The user must always understand why an artefact appeared in a slice.

## 6.4 Evidence-first inspection

Clicking any node or edge should show:

```text
Summary
Evidence
Metrics
Findings
Confidence
Unknowns
Related artefacts
Available lenses
```

Evidence should include:

```text
File path
Line span
Symbol name
Containing symbol
Snippet preview
Evidence kind
Confidence
Unknown reason, if any
```

Unknowns should be visible and valuable. For example:

```text
Unable to determine target table because SQL is dynamically constructed.
Unable to determine endpoint base URL because it is environment-supplied.
Reflection call detected; target method could not be resolved statically.
```

---

# 7. Main UI Areas

## 7.1 Dashboard

The dashboard should answer “where should I look first?”

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
Number of UI applications
Number of endpoints
Number of data contexts
Number of database tables detected
Number of hotlist findings
Top coupling hotspots
Top data-access hotspots
Top legacy hotspots
Latest architecture changes
New rule violations
Resolved rule violations
```

Dashboard widgets should be clickable and should open the relevant lens or filtered view.

## 7.2 Global Search / Command Palette

This is the primary entry point into investigation.

Features:

```text
Keyboard shortcut
Grouped results
Badges
Recent selections
Saved searches
Available lenses as actions
Open in new investigation tab
```

## 7.3 Investigation Workspace

The main working area for lenses.

Recommended layout:

```text
┌─────────────────────┬──────────────────────────────┬─────────────────────┐
│  Start / Controls   │        Slice View             │  Inspector          │
│                     │                              │                     │
│  Selected artefact  │  Graph / table / path /      │  Selected node      │
│  Lens selection     │  matrix / report             │  Evidence           │
│  Filters            │                              │  Metrics            │
│  Depth              │                              │  Findings           │
│  Overlays           │                              │  Follow-up actions  │
│                     │                              │                     │
└─────────────────────┴──────────────────────────────┴─────────────────────┘
```

## 7.4 Project Explorer

A searchable and filterable project catalogue.

Columns:

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

Project detail should be a lens launcher rather than a static page.

## 7.5 Data Access Explorer

A focused explorer for database coupling.

Views:

```text
Tables by project
Projects by table
Read/write matrix
DataContexts and DbContexts
Stored procedures
Raw SQL usage
Dynamic SQL unknowns
Endpoint-to-table paths
UI-to-table paths
```

## 7.6 Hotlist / Findings Explorer

A prioritised list of modernisation and architecture findings.

Features:

```text
Filter by category
Filter by severity
Filter by status
Group by project
Group by technology
Group by rule
Show evidence
Open impact lens
Open rule violation lens
Track first seen / latest seen
```

## 7.7 Snapshot Diff Explorer

Shows architectural drift over time.

Features:

```text
Compare snapshots
New projects
Removed projects
New dependencies
Removed dependencies
New findings
Resolved findings
Changed centrality
Changed data access
Changed legacy footprint
Changed layer compliance
```

---

# 8. Visual Language

All views should use a consistent visual vocabulary.

| Visual property | Meaning |
|---|---|
| Node size | Importance, volume, fan-in/fan-out, change frequency, or risk |
| Node colour | Health score, technology generation, layer, domain, or ownership |
| Node border | Team, repository, bounded context, or ownership status |
| Node halo | Recent churn, active risk, hotspot intensity, or uncertainty |
| Edge thickness | Coupling strength, call volume, co-change frequency, or dependency count |
| Edge colour | Dependency type, risk, rule violation, legacy crossing, or runtime path |
| Edge direction | Dependency direction, data flow, call direction, or ownership relation |
| Badge/icon | Legacy tech, deprecated dependency, missing tests, rule violation, unknown owner |
| Container/background | Capability, bounded context, team, repository, layer, or technology island |
| Dashed edge | Inferred, low-confidence, tolerated, or indirect relationship |
| Red edge | Forbidden dependency, risky edge, or rule violation |
| Fog/opacity | Unknown, incomplete, or low-confidence data |

Consistency is important because users will move between graph, matrix, health-map, data-access, and diff views.

---

# 9. Visualisation Types

Archon should use the visual form that best answers the current question.

## 9.1 System Health Map

**Purpose:** Where is the pain?

Use a treemap, packed-circle map, nested rectangle map, or restrained landscape view.

Group by:

```text
Capability
  → Repository
    → Solution
      → Project
        → Namespace / Folder
          → File / Type
```

Encodings:

```text
Size: lines of code, type count, dependency count, change frequency, or importance
Colour: health/risk score
Halo: churn/hotspot intensity
Border: ownership/team/context
Icons: legacy tech, missing tests, policy violation, unknowns
```

## 9.2 Interactive Graph View

**Purpose:** What depends on what?

Use for scoped graph slices only.

Good for:

```text
Project dependency graph
Scoped neighbourhood graph
Reverse usage graph
Change-coupling graph
Rule violation graph
Legacy dependency graph
```

Candidate renderer:

```text
Neo4j NVL for Neo4j-native graph rendering
Sigma.js + Graphology as an alternative for large graph rendering
```

The UI should hide the renderer behind an internal abstraction so Archon is not locked to one visualisation library.

## 9.3 Layered Graph View

**Purpose:** Does the implementation follow the intended architecture?

Use ELK/elkjs or equivalent layered layout.

Good for:

```text
Layer compliance
Endpoint-to-data flow
Pipeline/dataflow diagrams
Package/layer rules
```

## 9.4 Path Graph

**Purpose:** How does A reach B?

Good for:

```text
Endpoint to table
UI route to API
Project to package
Class to method
Legacy component to modern project
```

## 9.5 Matrix View

**Purpose:** Where is coupling dense?

Good for:

```text
Project dependencies
Context dependencies
Table usage by project
Package usage by project
Read/write table access
```

## 9.6 Sankey / Flow View

**Purpose:** How does runtime, data, or responsibility flow through the system?

Good for:

```text
Endpoint to service to database
UI to API to table
Message pipeline flows
Integration flows
```

## 9.7 Timeline / Diff View

**Purpose:** Is the architecture improving or degrading?

Good for:

```text
Coupling over time
Cycle count over time
Legacy usage over time
Rule violations over time
Hotspot movement over time
Migration progress over time
```

---

# 10. Renderer Strategy

Archon should separate **graph projection** from **graph rendering**.

```text
Archon API query result
        ↓
GraphProjectionDto
        ↓
Archon UI visual model
        ↓
Renderer adapter
          ├─ Neo4j NVL renderer
          ├─ Sigma/Graphology renderer
          ├─ ELK layered renderer
          ├─ Matrix renderer
          ├─ Sankey renderer
          └─ Health-map renderer
```

This keeps the product focused on architectural questions rather than on a particular visualisation library.

## 10.1 Neo4j NVL

Neo4j NVL is a strong candidate for the first graph prototype because Archon stores the Architecture Semantic Graph in Neo4j. It should be evaluated for:

```text
Project dependency graph
Scoped neighbourhood graph
Dependency path graph
Rule violation graph
Data access graph
Impact graph
```

NVL should not be expected to solve every visualisation problem. Treemaps, matrices, Sankey views, layered diagrams, and timeline views should use specialist renderers.

## 10.2 Suggested React UI stack

| Purpose | Candidate |
|---|---|
| App framework | React / TypeScript |
| Graph rendering | Neo4j NVL and/or Sigma.js |
| In-memory graph algorithms | Graphology |
| Layered layouts | ELKJS |
| Treemap/matrix/Sankey/timeline | D3 or focused chart libraries |
| Server state | TanStack Query |
| UI state | Zustand or Jotai |
| Component library | shadcn/ui or equivalent |
| Command/search palette | cmdk or equivalent |
| Resizable panels | react-resizable-panels |

---

# 11. Lens Availability Matrix

| Selected artefact | Primary lenses |
|---|---|
| Repository | Overview, Health, Projects, Hotlist, Snapshot Diff |
| Solution | Overview, Project Dependency, Health, Snapshot Diff |
| Project | Overview, Dependencies, Dependents, Data Access, Configuration, Endpoints, Legacy, Impact, Rule Violations |
| Package | Overview, Projects Using Package, Impact, Hotlist |
| Namespace | Overview, Dependencies, Dependents, Impact |
| Type/Class | Overview, References, Callers, Callees, DI Usage, Data Access, Impact, Evidence |
| Method | Overview, Callers, Callees, Data Access, Configuration, External Calls, Evidence |
| Endpoint | Overview, Request Path, Data Access, Configuration, External Calls, UI Callers, Impact |
| UI Route/Page | Overview, UI Flow, API Calls, Data Access Path, Impact |
| DbContext | Overview, Entities, Tables, Usages, Impact |
| LINQ to SQL DataContext | Overview, Entities, Tables, Stored Procedures, Usages, Impact |
| Entity | Overview, Table Mapping, Usages, Impact |
| Database Table | Overview, Readers, Writers, Endpoint Paths, UI Paths, Impact, Evidence |
| Stored Procedure | Overview, Callers, Projects, Endpoints, Impact |
| Configuration Key | Overview, Usages, Projects, Endpoints, Impact |
| External Service | Overview, Callers, Projects, Configuration, Impact |
| Finding | Overview, Evidence, Affected Artefacts, Related Rules, Impact |
| Rule | Overview, Findings, Violating Slices, Trend |

---

# 12. Investigative What-If vs Simulated What-If

Archon should initially support **investigative what-if**:

```text
What would be affected if this changed?
What paths lead to this table?
What depends on this class?
What projects would need review before changing this package?
What endpoints might be affected by this schema change?
```

This is evidence-backed and can be implemented with graph traversal.

Later, Archon may support **simulated what-if**:

```text
What if we removed this dependency?
What if this namespace became a separate project?
What if this table moved behind an API?
What if this project was migrated to .NET 9?
What if this shared library was split into three components?
```

Simulated what-if requires temporary graph mutation or scenario overlays and should come after the core lens model is reliable.

---

# 13. API Implications

The UI requires query APIs that return purpose-built projections.

Recommended API areas:

```text
Search
Node overview
Available lenses
Graph slice execution
Dependency queries
Path queries
Impact queries
Data access queries
Rule violation queries
Evidence lookup
Metric lookup
Snapshot diff queries
Export generation
```

Example endpoints:

```http
GET  /api/search?q={query}&snapshotId={snapshotId}
GET  /api/nodes/{stableKey}?snapshotId={snapshotId}
GET  /api/nodes/{stableKey}/lenses?snapshotId={snapshotId}
POST /api/graph-slices
POST /api/paths
POST /api/impact
GET  /api/evidence/{evidenceId}
POST /api/exports/impact-report
```

The UI should not accept arbitrary Cypher from the user. Lens queries should be predefined, parameterised, testable, and safe.

---

# 14. MVP UI Scope

The first useful Archon UI should include:

```text
Dashboard
Global search
Project explorer
Project detail / overview lens
Dependency lens
Data access lens
Impact lens
Path lens
Rule violation lens
Evidence inspector
Scoped graph renderer
Hotlist viewer
```

MVP questions:

```text
What projects are in this solution?
What does this project depend on?
What depends on this project?
Which database tables does this project use?
Which projects use this database table?
How does this endpoint reach this table?
What would be affected if this class/table/project changed?
Which rules are violated in this slice?
Where is the evidence?
```

A first vertical slice could be:

```text
Search for project
  → Open project overview
  → Open Dependency Lens
  → Open Data Access Lens
  → Click database table
  → Pivot to Readers/Writers
  → Open Impact Lens
  → Inspect evidence
```

---

# 15. Design Principles

## 15.1 Questions before entities

The UI should be organised around architectural questions, not just entity catalogues.

## 15.2 Scoped views by default

Do not render the entire estate by default. Large systems need slices, filters, summaries, and progressive disclosure.

## 15.3 Evidence everywhere

Every claim must be traceable back to evidence. Evidence should be one click away from every node, edge, finding, and metric.

## 15.4 Unknowns are first-class

Unknowns should be visible and useful. They show where further investigation is needed.

## 15.5 Renderer-agnostic UI architecture

The graph database and API projections are the product foundation. Rendering libraries are replaceable implementation details.

## 15.6 Consistent visual language

Users should learn the meaning of colour, size, border, edge thickness, badges, halos, and overlays once and apply that knowledge across views.

## 15.7 Human decision, AI assistance

The UI should prepare evidence-backed context for humans and Copilot. AI can explain and summarise; the deterministic graph remains the source of truth.

---

# 16. Open Design Questions

The following decisions should be refined during prototyping:

```text
Should lenses open as tabs, panels, or a breadcrumb investigation trail?
How much graph traversal should happen synchronously versus as background query jobs?
How should large slices be summarised before rendering?
What is the maximum node/edge count for interactive graph rendering?
Which graph renderer performs best for Archon’s expected slices?
How should confidence be visualised without overwhelming users?
Should search support saved filters and named investigations?
How should exported impact reports be structured?
How should MCP tools reuse the same lens definitions?
```

---

# 17. Key Message

Archon UI should be built around this experience:

```text
Find anything.
Ask architectural questions about it.
See only the relevant slice of the graph.
Understand why it appears.
Follow the evidence.
Decide what to do next.
```

This makes Archon an architectural investigation system, not just a documentation tool and not just a graph visualiser.
