# Architectural Health Visualisations for Large Software Systems

## Purpose

This document captures a set of visualisation ideas for an architectural knowledge-base / graph-analysis system. The exact underlying graph model can evolve, but the principles remain the same: use graph-derived data to give users a strong visual feel for whether a system is modular, risky, legacy-heavy, over-coupled, or drifting away from its intended architecture.

The intended scale is large systems: for example, solutions with 200+ projects, thousands of source files, multiple repositories, mixed technology stacks, legacy components, runtime dependencies, ownership metadata, change history, documentation links, and architectural decisions.

A conventional list-based interface is useful for detail work, but it is not enough for architectural comprehension. The product should provide visual maps that let users immediately see where the system is healthy, where it is fragile, and where intervention would have the highest payoff.

---

## CodeScene-inspired principle

A particularly strong reference point is the CodeScene style of analysis: combine structural or static facts with temporal development data so that the system highlights the parts of the codebase that matter most.

CodeScene’s hotspot analysis is useful inspiration because it does not merely ask “where is the code complex?” It asks where the team is spending development effort, and then combines that with code-health signals so users can prioritise the most valuable areas for improvement. CodeScene’s documentation describes hotspots as a recommended starting point for exploring a codebase and discusses combining hotspot analysis with code health to prioritise technical debt. It also supports architectural-level analysis, where hotspot and change-coupling ideas can be applied at component or subsystem level rather than only at file level.

For this project, the analogous principle should be:

> Do not only visualise structure. Visualise architectural importance, change pressure, and risk.

That means the most useful views should combine:

- dependency structure;
- coupling strength;
- change frequency;
- change coupling;
- code or component health;
- legacy technology detection;
- test coverage / confidence;
- ownership clarity;
- architectural rule violations;
- runtime criticality;
- migration difficulty.

The goal is not to produce a decorative graph. The goal is to create an architectural diagnostic surface.

References:

- CodeScene: Hotspots — <https://docs.enterprise.codescene.io/versions/6.4.30/guides/technical/hotspots.html>
- CodeScene: Architectural Analyses — <https://codescene.io/docs/guides/architectural/architectural-analyses.html>
- CodeScene: Change Coupling — <https://docs.enterprise.codescene.io/latest/guides/technical/change-coupling.html>
- CodeScene: Code Health — <https://codescene.io/docs/guides/technical/code-health.html>

---

## Core visual language

All views should share a common visual vocabulary so users learn the system once and can apply that intuition everywhere.

Suggested encodings:

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

A consistent visual language is especially important when moving between different projections: dependency graph, health map, change coupling, runtime flow, and architecture compliance.

---

## 1. System Health Map

### Purpose

Answer: **Where is the pain?**

This should be the main landing view. It should give the user a CodeScene-like overview of architectural hotspots across the whole system.

### Technique

Use a treemap, packed-circle map, nested rectangle map, or spatial “system landscape”. The map should group artefacts by a meaningful hierarchy such as:

```text
Capability
  → Repository
    → Solution
      → Project
        → Namespace / Folder
          → File / Type
```

The model can change, but the concept remains: show the system as a landscape of components.

### Encodings

- Size: lines of code, number of types, dependency count, change frequency, or aggregate importance.
- Colour: health score or risk score.
- Halo: churn / hotspot intensity.
- Border: ownership / team / bounded context.
- Icons: legacy tech, deprecated package, missing tests, policy violation.

### What it reveals

A healthy system may show balanced regions with limited hotspots. An unhealthy system may show huge red areas, large legacy regions, or small but intensely active risky components.

### Why it matters

This is the fastest way to give the user a visual feel for how good or bad the system is. It also helps avoid wasting effort on low-quality code that rarely changes; the highest-priority targets are usually the components that combine poor health with high change activity.

---

## 2. Project Dependency Graph

### Purpose

Answer: **What depends on what?**

This is the core interactive graph view. It should show project, package, service, namespace, or component dependencies depending on zoom level and selected projection.

### Technique

Use an interactive graph renderer such as Sigma.js + Graphology for large node-link views. Sigma.js is a WebGL-based graph renderer built on Graphology, and is designed for rendering thousands of nodes and edges in the browser.

### Encodings

- Node size: afferent/efferent coupling, importance, or fan-in/fan-out.
- Edge thickness: number of concrete references or coupling strength.
- Edge colour: dependency type or risk.
- Node colour: health, layer, domain, technology generation, or team.
- Red edges: forbidden or risky dependencies.
- Highlighted loops: cycles.

### What it reveals

- God projects.
- Shared-kernel abuse.
- Circular dependencies.
- Legacy projects at the centre of the graph.
- Cross-boundary dependencies.
- Projects that are overly central or fragile.

### Interaction ideas

- Show inbound dependencies.
- Show outbound dependencies.
- Show both directions.
- Expand project to namespaces.
- Expand namespace to types.
- Collapse to repository or capability.
- Hide tests, generated code, external packages, or low-weight edges.
- Show only risky edges.

References:

- Sigma.js documentation — <https://www.sigmajs.org/docs/>
- Sigma.js rendering model — <https://v4.sigmajs.org/concepts/rendering/>

---

## 3. Layer Compliance Graph

### Purpose

Answer: **Does the implementation follow the intended architecture?**

This is not a force-directed graph. It should be a deliberate architectural layout.

### Technique

Use ELK / elkjs for layered layouts. ELK is well suited to directed, layered, compound graph layouts and supports ports as explicit edge anchor points. This is useful for modelling layered architecture, dataflow, pipeline stages, and nested architecture diagrams.

A typical layout might be:

```text
UI
  ↓
API
  ↓
Application
  ↓
Domain
  ↓
Infrastructure
  ↓
External Systems
```

### Encodings

- Swimlanes: intended architectural layers.
- Nodes: projects, namespaces, components, services, or types.
- Normal edges: allowed dependency direction.
- Red edges: upward or forbidden dependencies.
- Dashed edges: tolerated exceptions.
- Thick red edges: frequent or high-impact violations.

### What it reveals

- UI reaching into infrastructure.
- Domain depending on persistence details.
- Application layer bypasses.
- Cross-layer shortcuts.
- Accidental dependencies into legacy components.
- Architecture erosion over time.

### Why it matters

This view gives users a clear picture of architectural drift. A force graph may show a blob; a layered graph shows whether the system still respects its intended design.

References:

- Eclipse Layout Kernel paper — <https://arxiv.org/abs/2311.00533>
- ELK layout options — <https://eclipse.dev/elk/reference/options.html>
- elkjs package — <https://www.skypack.dev/view/elkjs>

---

## 4. Cycle Map

### Purpose

Answer: **Where are the dependency cycles?**

Cycles often get lost in a large dependency graph. They deserve their own view.

### Technique

Use strongly connected component analysis and display each cycle or cyclic component as a ring, cluster, or compressed component.

### Encodings

- Ring size: number of nodes in the cycle.
- Ring thickness: number of edges participating in the cycle.
- Colour: severity.
- Icons: legacy involvement, rule violation, high churn, missing tests.
- External arrows: incoming and outgoing dependencies from the cyclic group.

### What it reveals

- Project-level cycles.
- Namespace-level cycles.
- Type-level cycles.
- Cycles crossing bounded contexts.
- Cycles involving legacy or high-risk components.
- Cycles that block migration or modularisation.

### Interaction ideas

- “Show minimal cycle.”
- “Show all concrete edges behind this cycle.”
- “Suggest possible break points.”
- “Show cycle history over time.”

---

## 5. Change-Coupling Graph

### Purpose

Answer: **What changes together?**

This is one of the most valuable architectural-health views. Static dependencies show what the code says; change coupling shows how the system actually evolves.

### Technique

Build a graph from commit, pull request, or work-item history. Nodes are files, projects, components, services, or bounded contexts. Edges connect artefacts that frequently change together.

### Encodings

- Edge thickness: co-change frequency.
- Node size: change frequency.
- Node colour: health score.
- Node border: ownership/team.
- Edge colour: whether the co-change crosses a boundary.

### What it reveals

- Components that are supposedly independent but always change together.
- Shared projects that cause coordination overhead.
- Legacy modules that are low in static coupling but high in change coupling.
- Files or projects that act as hidden bottlenecks.
- Boundaries that do not match real development behaviour.

### Why it matters

CodeScene’s change-coupling analysis is a strong inspiration here. Its documentation describes change coupling as a way to reveal logical dependencies and unexpected change patterns, and notes that the analysis can apply to distributed architectures such as microservices.

References:

- CodeScene: Change Coupling — <https://docs.enterprise.codescene.io/latest/guides/technical/change-coupling.html>
- CodeScene: Architectural Analyses — <https://codescene.io/docs/guides/architectural/architectural-analyses.html>

---

## 6. Legacy Technology Island Map

### Purpose

Answer: **Where is the legacy technology, and how trapped are we by it?**

### Technique

Cluster projects or components by technology generation or detected platform characteristics. Represent each generation as an island or region.

Example technology islands:

- .NET Framework.
- .NET 6 / 8 / 9.
- WCF.
- ASMX.
- WebForms.
- Old Entity Framework.
- Obsolete Azure SDKs.
- SOAP clients.
- Binary serialization.
- Old logging frameworks.
- Abandoned NuGet packages.
- Deprecated APIs.

### Encodings

- Island colour: technology generation.
- Node size: project size or migration effort.
- Edge thickness: dependency strength.
- Bridge colour: migration risk.
- Halo: active churn in legacy component.

### What it reveals

- Whether legacy code is isolated or central.
- Whether modern projects depend directly on legacy projects.
- Which components block migration.
- Where adapter boundaries exist or are missing.
- Which legacy areas still receive frequent changes.

### Interaction ideas

- Click a legacy island to show inbound dependencies.
- Show migration blockers.
- Show suggested strangler boundaries.
- Show “modernisation path” sorted by blast radius and effort.

---

## 7. Blast-Radius View

### Purpose

Answer: **If I change this, what is likely to be affected?**

### Technique

Place the selected artefact at the centre and expand outward through selected edge types. Use concentric rings to show distance or confidence.

Ring examples:

```text
Ring 0: selected artefact
Ring 1: direct impact
Ring 2: likely indirect impact
Ring 3: possible wider impact
```

### Encodings

- Distance from centre: dependency distance.
- Node colour: health or risk.
- Node size: importance or fan-in.
- Edge thickness: coupling strength.
- Edge style: static dependency, runtime call, change coupling, test relation, ownership relation.

### What it reveals

- Whether a change is contained.
- Whether impact crosses bounded contexts.
- Whether tests are near the affected area.
- Whether ownership is clear.
- Whether legacy components sit in the impact path.

### Interaction ideas

- Toggle static dependencies.
- Toggle runtime dependencies.
- Toggle tests.
- Toggle configuration.
- Toggle ownership.
- Toggle documentation and ADRs.
- Change depth.
- Export impact report.

---

## 8. Bounded-Context Boundary View

### Purpose

Answer: **Are the system boundaries real?**

### Technique

Represent bounded contexts, capabilities, domains, teams, or modules as containers. Show the components inside them and the relationships crossing between them.

### Encodings

- Container colour: aggregate health.
- Container border thickness: boundary strength.
- Internal edges: cohesion.
- External edges: cross-boundary coupling.
- Edge style: call, event, package dependency, database sharing, message contract.

### What it reveals

- Dense internal cohesion.
- Excessive cross-context calls.
- Shared database tables across contexts.
- Shared domain models leaking across boundaries.
- Contexts that are boundaries in name only.

### Interaction ideas

- Show only cross-boundary edges.
- Show only database sharing.
- Show only synchronous calls.
- Compare intended boundaries with detected clusters.

---

## 9. Dependency Matrix / Adjacency Matrix

### Purpose

Answer: **Where is coupling dense?**

A matrix is still graph-based, but for 200+ projects it may be far more readable than a node-link hairball.

### Technique

Rows and columns represent projects, components, repositories, or bounded contexts. A cell shows the strength or type of dependency from row to column.

### Encodings

- Cell intensity: dependency strength.
- Cell colour: dependency type or risk.
- Diagonal blocks: internal modular cohesion.
- Off-diagonal cells: cross-module coupling.
- Symmetric cells: possible cycles or bidirectional dependencies.

### What it reveals

- Dense coupling.
- Block structure.
- Cross-boundary dependencies.
- Bidirectional relationships.
- Unwanted dependencies.
- Coupling between legacy and modern components.

### Interaction ideas

- Sort by architecture layer.
- Sort by repository.
- Sort by bounded context.
- Sort by detected cluster.
- Show only violations.
- Show only cycles.
- Show only legacy crossings.

---

## 10. Chord Diagram

### Purpose

Answer: **Which high-level areas are entangled?**

### Technique

Use a chord diagram at a high level only: bounded contexts, repositories, teams, or major subsystems. Avoid using it at file or type level.

### Encodings

- Arc size: component size or activity.
- Chord thickness: coupling strength.
- Chord colour: dependency type, risk, or source context.
- Red chords: forbidden or risky relationships.

### What it reveals

- High-level coupling knots.
- Major dependency flows.
- Contexts that are too entangled.
- Legacy areas coupled to modern areas.

### Caution

Chord diagrams are visually compelling but can become decorative spaghetti. Use them for executive-level or high-level architecture views, not detailed analysis.

---

## 11. Sankey / Flow View

### Purpose

Answer: **How does runtime, data, or responsibility flow through the system?**

### Technique

Use Sankey diagrams for directed flows such as request paths, data movement, message pipelines, or integration flows.

Example path:

```text
Endpoint
  → Handler
  → Application Service
  → Domain Service
  → Repository
  → Table
  → External System
```

### Encodings

- Flow thickness: call volume, dependency count, or data volume.
- Colour: domain/capability or technology generation.
- Red flow: legacy path, rule violation, or high-risk route.
- Node size: flow volume or criticality.

### What it reveals

- Request paths that bypass intended layers.
- Multiple contexts writing the same table.
- Old systems in the middle of modern flows.
- Over-centralised databases or services.
- Hidden integration bottlenecks.

Reference:

- D3 Sankey — <https://github.com/d3/d3-sankey>

---

## 12. Architecture Skyline

### Purpose

Answer: **What does the system feel like as a landscape?**

### Technique

Represent projects or components as buildings in a city-like view. Group buildings by capability, repository, bounded context, team, or layer.

### Encodings

- Building height: complexity, size, or change frequency.
- Building width: number of files or types.
- Colour: health score.
- Glow: recent churn.
- Cracks/icons: rule violations, legacy tech, missing tests.
- District: bounded context, repository, or team.

### What it reveals

- Giant risky modules.
- Abandoned areas.
- Legacy districts.
- Hotspot towers.
- Unbalanced architecture.

### Caution

This can become gimmicky if overdone. A restrained “city map” can be useful for overview and communication, but detail work should happen in more precise views.

---

## 13. Architecture Weather Map

### Purpose

Answer: **Where is architectural risk accumulating?**

### Technique

Overlay weather-like indicators on the system map.

### Encodings

- Storm clouds: high churn plus low health.
- Lightning: active rule violations.
- Fog: unknown ownership or missing metadata.
- Heat: high coupling.
- Cold zones: dead or unused code.
- Pressure fronts: migration boundaries.

### What it reveals

- Hot risk zones.
- Areas that need architectural attention.
- Areas where uncertainty is the main problem.
- Components where current change activity may cause instability.

### Caution

Use metaphor carefully. It should clarify the system, not turn the UI into a novelty dashboard.

---

## 14. Timeline Evolution View

### Purpose

Answer: **Is the architecture getting better or worse?**

### Technique

Show how structural and health metrics evolve over releases, commits, or analysis snapshots.

### Metrics

- Coupling over time.
- Cycle count over time.
- Legacy usage over time.
- Number of rule violations over time.
- Health score over time.
- Hotspot movement over time.
- Migration progress over time.
- Change coupling over time.

### Visual forms

- Timeline scrubber.
- Small multiples.
- Before/after graph comparison.
- Animated graph evolution.
- Release-to-release diff map.

### What it reveals

- Architecture drift.
- Migration progress.
- Technical debt burn-down.
- New coupling introduced by recent work.
- Whether architecture rules are improving behaviour.

---

## 15. Rule Violation Overlay

### Purpose

Answer: **Where does the implementation break the architectural rules?**

### Technique

Allow users to define architecture rules, then overlay violations on the relevant graph views.

Example rules:

```text
UI must not reference Infrastructure.
Domain must not reference Entity Framework.
Project X must not depend on Legacy.Core.
Only adapters may call SOAP services.
Only DataAccess may write database tables.
No circular project dependencies.
No direct dependency from bounded context A to B.
```

### Encodings

- Red edges: violating dependencies.
- Warning badges: nodes with violations.
- Violation count badges: containers or clusters.
- Ghosted valid edges: spotlight mode for invalid relationships.
- Severity colour: warning vs error.

### What it reveals

- Architecture erosion.
- Teams bypassing intended boundaries.
- Dependency rules that are not being enforced.
- Legacy dependencies that should have been isolated.

### Why it matters

This turns the visual system into an architecture fitness-function surface rather than a passive diagram.

---

## 16. Health Score Model

### Purpose

Answer: **How do we decide whether something is healthy or risky?**

A visualisation layer needs a scoring model. The model should be explainable and decomposable: users should be able to click a score and see why it exists.

### Node health inputs

Possible node-level factors:

- coupling;
- complexity;
- churn;
- test coverage;
- age;
- legacy technology;
- ownership clarity;
- rule violations;
- cycle participation;
- dependency centrality;
- runtime criticality;
- documentation freshness.

### Edge risk inputs

Possible edge-level factors:

- forbidden dependency;
- cross-boundary dependency;
- dependency into legacy code;
- high change coupling;
- bidirectional relationship;
- runtime criticality;
- hidden database sharing;
- unstable target;
- missing contract boundary.

### Cluster health inputs

Possible cluster-level factors:

- internal cohesion;
- external coupling;
- cycle count;
- dependency direction compliance;
- change concentration;
- legacy concentration;
- ownership spread;
- documentation coverage;
- test coverage.

### Important principle

Do not hide everything behind a single opaque score. A single score is useful for colour and sorting, but the inspector should show the contributing factors.

---

## Recommended first five views

The full list above is broad. The first implementation should focus on the views with the highest diagnostic value.

### 1. System Health Map

The landing page. Inspired by the CodeScene hotspot/code-health idea.

Shows:

- where the development effort is going;
- which areas are unhealthy;
- which components combine size, churn, and risk;
- where to focus refactoring or architecture work.

### 2. Project Dependency Graph

The main graph exploration surface.

Shows:

- dependencies;
- central components;
- cycles;
- high coupling;
- legacy crossings;
- rule violations.

### 3. Layer Compliance Graph

The architecture rule view.

Shows:

- intended layers;
- allowed dependency direction;
- upward dependencies;
- cross-layer shortcuts;
- violations.

### 4. Change-Coupling Graph

The evolution-driven architecture view.

Shows:

- components that change together;
- hidden logical dependencies;
- fake boundaries;
- coordination hotspots;
- change-impact risk.

### 5. Legacy Technology Island Map

The modernisation view.

Shows:

- legacy technology clusters;
- migration blockers;
- central legacy dependencies;
- bridge/adaptor opportunities;
- modernisation risk.

---

## Suggested open-source visualisation stack

For a React-based open-source UI, consider:

| Purpose | Candidate |
|---|---|
| Large interactive graph rendering | Sigma.js |
| In-memory graph model and algorithms | Graphology |
| Layered / compound architecture layouts | ELKJS |
| Treemaps, matrices, Sankey, chord diagrams | D3 |
| API state | TanStack Query |
| UI state | Zustand or Jotai |
| Component library | shadcn/ui |
| Command/search palette | cmdk |
| Panels/layout | react-resizable-panels |

The graph database should remain the source of truth. The frontend should receive visual slices, projections, and precomputed metrics from a backend API.

---

## Product principle

The most important architectural decision is this:

```text
One graph database.
Many visual projections.
Consistent health overlays.
```

The product should not try to render the entire knowledge graph as one enormous hairball. Instead, it should let users move between projections:

- health map;
- dependency graph;
- layer compliance graph;
- cycle map;
- change-coupling graph;
- legacy island map;
- blast-radius view;
- matrix view;
- runtime flow view;
- timeline evolution view.

Each view should answer a specific architectural question. Together, they should give the user a graphical feel for whether the system is clean, fragile, legacy-heavy, over-coupled, drifting, or genuinely well-structured.

---

## Reference list

1. CodeScene Hotspots documentation: <https://docs.enterprise.codescene.io/versions/6.4.30/guides/technical/hotspots.html>
2. CodeScene Architectural Analyses documentation: <https://codescene.io/docs/guides/architectural/architectural-analyses.html>
3. CodeScene Change Coupling documentation: <https://docs.enterprise.codescene.io/latest/guides/technical/change-coupling.html>
4. CodeScene Code Health documentation: <https://codescene.io/docs/guides/technical/code-health.html>
5. Sigma.js documentation: <https://www.sigmajs.org/docs/>
6. Sigma.js rendering documentation: <https://v4.sigmajs.org/concepts/rendering/>
7. Eclipse Layout Kernel paper: <https://arxiv.org/abs/2311.00533>
8. ELK layout options: <https://eclipse.dev/elk/reference/options.html>
9. elkjs package reference: <https://www.skypack.dev/view/elkjs>
10. D3 Sankey: <https://github.com/d3/d3-sankey>
