# WP013 Specification - Metrics, Hotspots, Architecture Rules, and Snapshot Diff

## Document Control

| Field | Value |
| --- | --- |
| Work Package | WP013 - Metrics, Hotspots, Architecture Rules, and Snapshot Diff |
| Output Path | `docs/013-Metrics-Hotspots-Architecture-Rules-and-Snapshot-Diff/spec-wp013-metrics-hotspots-architecture-rules-and-snapshot-diff.md` |
| Source Work Package | `docs/foundation/work-packages.md` WP013 |
| Source Brief | `docs/foundation/archon_full_concept_brief.md` |
| Specification Basis | `spec-template_v1.1.md` requested; template file was not present in the workspace at creation time, so this document follows the repository specification structure and documented work-package rules. |
| Status | Draft |
| Audience | Product owner, architect, implementation team, test engineer |

## 1. Overview

### 1.1 Purpose

This specification defines the requirements for WP013, the Archon work package that introduces persisted architecture metrics, coupling and hotspot calculations, graph-structure-based architecture rule checks, and snapshot diff across nodes, edges, findings, and metrics.

WP013 converts the deterministic architecture graph and WP012 finding output into durable quantitative intelligence. The package ensures that metrics and diffs are persisted as evidence-backed snapshot outputs rather than transient query-only calculations.

### 1.2 Background

Archon is an API-first and MCP-first deterministic architecture intelligence platform for modern and legacy .NET estates. Earlier work packages establish the graph domain model, Neo4j persistence, API-triggered extraction orchestration, repository and project extraction, Roslyn semantic extraction, configuration and dependency-injection extraction, runtime extraction, data-access extraction, external integration extraction, .NET UI-technology extraction, and the rule catalog with findings.

WP013 builds on those persisted facts. It calculates project metrics, graph metrics, modernization metrics, hotspots, cycle information, architecture rule results, and stable-key/fingerprint-based snapshot diffs. These outputs become inputs for later query API, MCP, markdown export, and operational hardening work packages.

### 1.3 High-Level Scope

WP013 covers these capability areas:

- Snapshot-computed project metrics.
- Snapshot-computed graph metrics.
- Snapshot-computed modernization metrics.
- Coupling hotspot identification.
- Cycle detection and dependency-depth analysis.
- Metric persistence with stable keys and fingerprints.
- Architecture rule checks that depend on graph structure or persisted metrics.
- Snapshot diff across architecture nodes, architecture edges, findings, and metrics.
- API endpoints for retrieving metrics, hotspots, architecture-rule results, cycles, and snapshot diffs.
- Tests and documentation for all production behavior introduced by this work package.

WP013 excludes Archon Discovery UI, MCP tools/resources/prompts, markdown export generation, new extraction domains, new rule authoring UI, organization-specific hard-coded architecture policy beyond configured rules, automatic remediation, and graph visualization.

## 2. System Context

### 2.1 Product Context

Archon accepts API-triggered extraction requests, extracts deterministic architecture facts into a snapshot, persists them in Neo4j, and exposes architecture knowledge through API and MCP surfaces. WP013 adds durable metric, hotspot, architecture-rule-result, and diff outputs that are computed from the architecture-wide graph model.

The package must use the existing extraction orchestration, stable-key generation, fingerprint generation, snapshot accumulator, rule/finding model, and Neo4j persistence seams. It must not bypass the shared snapshot contract, recompute historical metrics from mutable current state, or treat expensive graph calculations as query-time-only behavior when the source brief requires persisted metric output.

### 2.2 Source References

WP013 must align with these source materials:

- `docs/foundation/work-packages.md` WP013 objective, required implementation, and completion criteria.
- `docs/foundation/archon_full_concept_brief.md` section 12.7 for metrics and generated summaries model context.
- `docs/foundation/archon_full_concept_brief.md` sections 25 through 27 for hotlist and findings inputs.
- `docs/foundation/archon_full_concept_brief.md` sections 32 through 34 for quality, classification, metrics, and architecture rules.
- `docs/foundation/archon_full_concept_brief.md` section 35 phase 7 for snapshot diff and architecture drift, excluding graph UI.
- `docs/foundation/archon_full_concept_brief.md` Appendix E sections E.4.6, E.4.8, E.5.2.9, E.5.9, E.6.9, and E.7.5 for metrics and diff strategy.
- `docs/foundation/work-packages.md` completion rules for evidence-backed facts, explicit unknowns, deterministic stable keys, Neo4j as the system of record, tests, documentation, and no Discovery UI.

### 2.3 Users and Stakeholders

| Stakeholder | Interest |
| --- | --- |
| Product owner | Confirms WP013 covers the mandatory metrics, hotspots, architecture-rule, and diff scope without UI delivery or deferred mandatory behavior. |
| Architect | Confirms metric definitions, graph calculations, architecture-rule checks, and diff semantics preserve deterministic evidence-backed architecture intelligence. |
| Developer | Uses metrics, hotspots, cycles, architecture-rule results, and diffs to assess change impact, modernization risk, and architecture drift. |
| Test engineer | Verifies metric calculation, persistence, fingerprint comparison, diff classification, hotspot detection, cycle detection, and architecture-rule integration. |
| Future API consumer | Depends on persisted metric and diff outputs for query endpoints in WP014. |
| Future MCP consumer | Depends on evidence-backed metrics, hotspots, architecture-rule results, and diffs for Copilot workflows in WP015. |
| Operations owner | Depends on deterministic metric and diff outputs that can be regenerated, audited, and explained across snapshots. |

## 3. Component Summary

### 3.1 Metric Calculation Pipeline Stage

The metric calculation stage runs as part of the API-driven extraction pipeline after required graph facts and findings are available. It calculates project metrics, graph metrics, and modernization metrics from the snapshot accumulator and persisted graph context where needed. It contributes metric records to the shared snapshot output and preserves warnings when a metric cannot be calculated with sufficient evidence.

### 3.2 Project Metrics Component

The project metrics component calculates project-scoped quantitative values including incoming project references, outgoing project references, package count, public type count, endpoint count, data-access count, hotlist finding count, and target-framework age or risk. Project metrics must be scoped to the correct project node and snapshot.

### 3.3 Graph Metrics Component

The graph metrics component calculates graph-structure values including fan-in, fan-out, centrality, dependency depth, transitive dependency count, cycle participation, and neighbourhood size. The component must work over stable architecture node and edge identities rather than database IDs.

### 3.4 Modernization Metrics Component

The modernization metrics component calculates modernization-oriented values including legacy technology count, security-sensitive finding count, out-of-support target count, framework-only dependency count, data-access spread, and shared table usage count. It consumes deterministic facts and persisted findings rather than inventing risk signals.

### 3.5 Hotspot Detector

The hotspot detector identifies coupling and modernization hotspots from persisted metrics, graph structure, and findings. Hotspot output must be explainable through contributing metric values, graph relationships, findings, and evidence links where available.

### 3.6 Cycle Detection Component

The cycle detection component identifies dependency cycles in project and architecture graph relationships. Cycle detection must return stable node identities, edge identities, cycle paths, confidence, and evidence references where available.

### 3.7 Architecture Rule Check Component

The architecture rule check component evaluates graph-structure and metric-dependent architecture checks, including layering and dependency patterns described by the source brief. It must use configured rules and generic graph semantics rather than hard-coded organization-specific rules.

### 3.8 Snapshot Diff Component

The snapshot diff component compares two snapshots using stable keys and normalized fingerprints. It reports added, removed, changed, and unchanged records across architecture nodes, architecture edges, findings, and metrics. It must not compare Neo4j internal IDs or rely on load order.

### 3.9 Metric and Diff Persistence Integration

The persistence integration stores metrics as first-class snapshot-owned records with stable keys, fingerprints, scope, values, units, metadata, and optional evidence links. Diff results may be calculated on demand from persisted snapshots, but the comparison rules must be deterministic and based on persisted stable keys and fingerprints.

### 3.10 Metrics, Hotspots, Rule Results, and Diff API Surface

WP013 exposes API endpoints sufficient for retrieving project metrics, graph metrics, modernization metrics, hotspots, cycles, architecture-rule results, and snapshot diffs. These endpoints provide the minimum surface introduced by WP013 and must be shaped for later full query API and MCP consumption.

## 4. Functional Requirements

### 4.1 Pipeline Participation

| ID | Requirement |
| --- | --- |
| FR-001 | WP013 shall add a metrics calculation stage to the existing API-driven extraction pipeline. |
| FR-002 | Metric calculation shall run after extraction facts required for the metric are available. |
| FR-003 | Metric calculation shall run after WP012 finding evaluation when a metric depends on hotlist or finding counts. |
| FR-004 | Metric calculation shall contribute metric records to the shared snapshot accumulator. |
| FR-005 | Metric calculation shall not persist directly to Neo4j outside the established snapshot persistence path. |
| FR-006 | Metric calculation shall not mutate extracted architecture nodes, edges, evidence, rules, or findings except through explicitly defined metric, warning, or error outputs. |
| FR-007 | Metric calculation shall emit warnings for incomplete metric inputs when the metric can be partially explained. |
| FR-008 | Metric calculation shall emit errors only for failures that prevent required metric output for the snapshot from being assembled. |
| FR-009 | Metric calculation shall preserve explicit unknown-state information when evidence is insufficient. |
| FR-010 | Metric calculation shall be deterministic for the same snapshot input. |

### 4.2 Metric Record Contract

| ID | Requirement |
| --- | --- |
| FR-011 | Each metric shall have a deterministic stable key. |
| FR-012 | Each metric shall have a normalized fingerprint. |
| FR-013 | Each metric shall identify the snapshot that produced it. |
| FR-014 | Each metric shall identify its metric kind. |
| FR-015 | Each metric shall identify its scope kind. |
| FR-016 | A node-scoped metric shall reference the target architecture node stable key. |
| FR-017 | An edge-scoped metric shall reference the target architecture edge stable key. |
| FR-018 | A snapshot-scoped metric shall not require a node or edge stable key. |
| FR-019 | A metric may reference primary evidence when evidence directly supports the value. |
| FR-020 | A metric shall have a human-readable name. |
| FR-021 | A metric shall support numeric values. |
| FR-022 | A metric shall support text values when a metric result is categorical. |
| FR-023 | A metric shall support a unit when a unit is meaningful. |
| FR-024 | A metric shall support metadata for metric-specific details. |
| FR-025 | Metrics shall be persisted as first-class snapshot outputs. |
| FR-026 | Metrics shall not be treated as transient query-time-only values. |

### 4.3 Metric Stable Keys and Fingerprints

| ID | Requirement |
| --- | --- |
| FR-027 | Metric stable keys shall use the `metric://` prefix established by the shared stable-key strategy. |
| FR-028 | Metric stable keys shall be independent of Neo4j internal IDs. |
| FR-029 | Metric stable keys shall include enough normalized identity information to distinguish metric kind, scope, snapshot, and target. |
| FR-030 | Metric fingerprints shall include normalized value, unit, scope, target, and metadata that materially affects the metric meaning. |
| FR-031 | Metric fingerprints shall exclude volatile runtime data that does not materially affect the metric meaning. |
| FR-032 | The same metric calculated from equivalent input shall produce the same stable key and fingerprint. |
| FR-033 | A changed metric value for the same stable key shall produce a different fingerprint. |
| FR-034 | Metric stable-key and fingerprint generation shall use the shared components established by earlier work packages where applicable. |

### 4.4 Project Metrics

| ID | Requirement |
| --- | --- |
| FR-035 | WP013 shall calculate incoming project reference count for each project node. |
| FR-036 | WP013 shall calculate outgoing project reference count for each project node. |
| FR-037 | WP013 shall calculate package count for each project node. |
| FR-038 | WP013 shall calculate public type count for each project node. |
| FR-039 | WP013 shall calculate endpoint count for each project node. |
| FR-040 | WP013 shall calculate data-access count for each project node. |
| FR-041 | WP013 shall calculate hotlist finding count for each project node. |
| FR-042 | WP013 shall calculate target-framework age or risk for each project node where target-framework data exists. |
| FR-043 | Project metric calculations shall use extracted project, package, semantic, runtime, data-access, and finding facts rather than re-scanning source files independently. |
| FR-044 | Project metrics shall represent unavailable input as unknown or warning data rather than silently omitting the metric when the metric is required. |
| FR-045 | Project metrics shall support later filtering and sorting by project, solution, metric kind, numeric value, and risk category. |

### 4.5 Graph Metrics

| ID | Requirement |
| --- | --- |
| FR-046 | WP013 shall calculate fan-in for applicable architecture nodes. |
| FR-047 | WP013 shall calculate fan-out for applicable architecture nodes. |
| FR-048 | WP013 shall calculate centrality for applicable architecture nodes. |
| FR-049 | WP013 shall calculate dependency depth for applicable architecture nodes. |
| FR-050 | WP013 shall calculate transitive dependency count for applicable architecture nodes. |
| FR-051 | WP013 shall calculate cycle participation for applicable architecture nodes. |
| FR-052 | WP013 shall calculate neighbourhood size for applicable architecture nodes. |
| FR-053 | Graph metric calculations shall use stable node and edge identities. |
| FR-054 | Graph metric calculations shall support filtering by edge kind where needed. |
| FR-055 | Graph metric calculations shall avoid treating evidence, metric, rule, generated summary, or support relationships as ordinary dependency edges unless explicitly intended. |
| FR-056 | Graph metric calculations shall guard against unbounded traversal by using deterministic depth, edge-kind, and scope controls. |
| FR-057 | Graph metric output shall include metadata describing the traversal scope when scope affects the result. |

### 4.6 Modernization Metrics

| ID | Requirement |
| --- | --- |
| FR-058 | WP013 shall calculate legacy technology count. |
| FR-059 | WP013 shall calculate security-sensitive finding count. |
| FR-060 | WP013 shall calculate out-of-support target count. |
| FR-061 | WP013 shall calculate framework-only dependency count. |
| FR-062 | WP013 shall calculate data-access spread. |
| FR-063 | WP013 shall calculate shared table usage count. |
| FR-064 | Modernization metrics shall be derived from extracted facts, rules, findings, and graph relationships. |
| FR-065 | Modernization metrics shall not use AI-generated assumptions. |
| FR-066 | Modernization metrics shall retain links or metadata explaining the contributing facts where practical. |
| FR-067 | Modernization metrics shall support project, solution, repository, and snapshot-level rollups where the source graph supports those scopes. |

### 4.7 Hotspot Detection

| ID | Requirement |
| --- | --- |
| FR-068 | WP013 shall identify high fan-in hotspots. |
| FR-069 | WP013 shall identify high fan-out hotspots. |
| FR-070 | WP013 shall identify shared libraries referenced by many applications. |
| FR-071 | WP013 shall identify data-access spread hotspots. |
| FR-072 | WP013 shall identify shared table usage hotspots. |
| FR-073 | WP013 shall identify projects with high concentrations of hotlist findings. |
| FR-074 | WP013 shall identify projects with high dependency depth or large transitive dependency counts. |
| FR-075 | WP013 shall identify cycle-related hotspots. |
| FR-076 | Hotspot output shall include target stable key, hotspot category, score or rank where applicable, contributing metrics, relevant findings, and evidence references where available. |
| FR-077 | Hotspot ranking shall be deterministic for equal input. |
| FR-078 | Hotspot ranking ties shall be ordered by stable deterministic fields. |
| FR-079 | Hotspot detection shall not invent ownership, intent, or risk rationale beyond extracted facts, configured rules, metrics, and findings. |

### 4.8 Cycle Detection

| ID | Requirement |
| --- | --- |
| FR-080 | WP013 shall detect circular project dependencies. |
| FR-081 | WP013 shall detect circular dependency paths across configured architecture edge kinds where required. |
| FR-082 | Cycle detection shall report each cycle using stable node keys and stable edge keys. |
| FR-083 | Cycle detection shall include cycle path order. |
| FR-084 | Cycle detection shall include direct evidence where project or source references support cycle edges. |
| FR-085 | Cycle detection shall avoid duplicate reporting of the same cycle with different rotations. |
| FR-086 | Cycle detection shall apply deterministic ordering to reported cycles. |
| FR-087 | Cycle detection shall support limits or truncation metadata for very large cycle result sets. |
| FR-088 | Cycle participation shall contribute to relevant graph metrics and hotspot classification. |

### 4.9 Architecture Rule Checks

| ID | Requirement |
| --- | --- |
| FR-089 | WP013 shall implement architecture-rule checks for layering and dependency patterns described by the source brief. |
| FR-090 | Architecture-rule checks shall support domain projects referencing infrastructure projects. |
| FR-091 | Architecture-rule checks shall support domain projects referencing web projects. |
| FR-092 | Architecture-rule checks shall support web projects referenced by non-web projects. |
| FR-093 | Architecture-rule checks shall support application projects directly using LINQ to SQL when such usage is not explicitly allowed by configured rules. |
| FR-094 | Architecture-rule checks shall support controllers directly using DataContext when such usage is not explicitly allowed by configured rules. |
| FR-095 | Architecture-rule checks shall support worker projects missing queue or topic dependencies where worker evidence indicates such dependencies should exist. |
| FR-096 | Architecture-rule checks shall support shared libraries with high fan-in requiring review before change. |
| FR-097 | Architecture-rule checks shall use configured rule catalog semantics where applicable. |
| FR-098 | Architecture-rule checks shall not hard-code organization-specific rules beyond generic source-brief patterns and configured rule definitions. |
| FR-099 | Architecture-rule results shall be evidence-backed through graph edges, metrics, findings, or explicit unknowns. |
| FR-100 | Architecture-rule results shall be exposed through API endpoints introduced by WP013. |

### 4.10 Snapshot Diff Inputs

| ID | Requirement |
| --- | --- |
| FR-101 | Snapshot diff shall accept a current snapshot identity and previous snapshot identity. |
| FR-102 | Snapshot diff shall validate that both snapshots exist. |
| FR-103 | Snapshot diff shall validate that snapshots are comparable within the same repository or explicitly compatible comparison scope. |
| FR-104 | Snapshot diff shall compare architecture nodes. |
| FR-105 | Snapshot diff shall compare architecture edges. |
| FR-106 | Snapshot diff shall compare findings. |
| FR-107 | Snapshot diff shall compare metrics. |
| FR-108 | Snapshot diff shall not compare Neo4j internal IDs. |
| FR-109 | Snapshot diff shall use stable keys and fingerprints as the comparison basis. |
| FR-110 | Snapshot diff shall tolerate missing optional metadata while preserving deterministic output. |

### 4.11 Snapshot Diff Classification

| ID | Requirement |
| --- | --- |
| FR-111 | Snapshot diff shall classify records present only in the current snapshot as added. |
| FR-112 | Snapshot diff shall classify records present only in the previous snapshot as removed. |
| FR-113 | Snapshot diff shall classify records present in both snapshots with different fingerprints as changed. |
| FR-114 | Snapshot diff shall classify records present in both snapshots with equal fingerprints as unchanged. |
| FR-115 | Snapshot diff shall include changed fields or a deterministic change summary where practical. |
| FR-116 | Snapshot diff shall include counts by domain and change kind. |
| FR-117 | Snapshot diff shall include record-level stable keys, display names where available, kinds, and change kind. |
| FR-118 | Snapshot diff shall include evidence references where available for added, removed, or changed facts. |
| FR-119 | Snapshot diff shall include unknowns when a comparison cannot fully explain a change. |
| FR-120 | Snapshot diff shall support response-size limits or truncation metadata for large diffs. |

### 4.12 API Surface

| ID | Requirement |
| --- | --- |
| FR-121 | WP013 shall expose an API endpoint to retrieve metrics for a snapshot. |
| FR-122 | WP013 shall expose an API endpoint to retrieve metrics for a project. |
| FR-123 | WP013 shall expose an API endpoint to retrieve graph metrics. |
| FR-124 | WP013 shall expose an API endpoint to retrieve modernization metrics. |
| FR-125 | WP013 shall expose an API endpoint to retrieve hotspots. |
| FR-126 | WP013 shall expose an API endpoint to retrieve cycles. |
| FR-127 | WP013 shall expose an API endpoint to retrieve architecture-rule results. |
| FR-128 | WP013 shall expose an API endpoint to retrieve snapshot diff. |
| FR-129 | API responses shall include stable keys rather than database IDs as public identity. |
| FR-130 | API responses shall include confidence and unknown-state data where applicable. |
| FR-131 | API responses shall include evidence references where applicable. |
| FR-132 | API responses shall be suitable for later MCP consumption. |
| FR-133 | API responses shall support filtering by snapshot, project, metric kind, scope, hotspot category, rule category, and change kind where applicable. |
| FR-134 | API responses shall support pagination, limits, or truncation metadata for potentially large result sets. |

### 4.13 Evidence, Confidence, and Unknowns

| ID | Requirement |
| --- | --- |
| FR-135 | Metrics based directly on graph facts shall preserve links or metadata to contributing facts where practical. |
| FR-136 | Metrics based directly on evidence-backed facts shall expose evidence references where practical. |
| FR-137 | Metrics with incomplete inputs shall represent unknown data explicitly. |
| FR-138 | Hotspot output shall include confidence derived from contributing metric and finding confidence. |
| FR-139 | Architecture-rule results shall include evidence or explicit unknown reasons. |
| FR-140 | Diff output shall preserve confidence and unknown-state data from compared records. |
| FR-141 | WP013 shall not silently omit unknown or partially calculable metrics when the metric is required by WP013. |
| FR-142 | WP013 shall not use AI to infer facts, scores, or changes. |

### 4.14 Documentation Requirements

| ID | Requirement |
| --- | --- |
| FR-143 | WP013 shall document supported project metrics, graph metrics, and modernization metrics. |
| FR-144 | WP013 shall document metric scope, stable-key, fingerprint, and persistence behavior. |
| FR-145 | WP013 shall document hotspot calculation behavior and limitations. |
| FR-146 | WP013 shall document cycle detection behavior and limitations. |
| FR-147 | WP013 shall document architecture-rule check behavior and configuration boundaries. |
| FR-148 | WP013 shall document snapshot diff semantics for added, removed, changed, and unchanged records. |
| FR-149 | WP013 documentation shall explain evidence, confidence, and unknown handling. |
| FR-150 | Documentation shall treat internal and other non-public types as requiring the same developer-level documentation standard as public types when code-documentation expectations are specified. |

## 5. Non-Functional Requirements

### 5.1 Determinism

| ID | Requirement |
| --- | --- |
| NFR-001 | Metric output shall be deterministic for the same snapshot input. |
| NFR-002 | Hotspot ordering shall be deterministic for the same snapshot input. |
| NFR-003 | Cycle ordering shall be deterministic for the same snapshot input. |
| NFR-004 | Diff output shall be deterministic for the same pair of snapshots. |
| NFR-005 | Stable-key and fingerprint comparison shall be independent of persistence load order. |

### 5.2 Performance and Scalability

| ID | Requirement |
| --- | --- |
| NFR-006 | Graph calculations shall avoid unbounded traversal. |
| NFR-007 | Graph calculations shall support practical limits for large repositories. |
| NFR-008 | Expensive metric calculations shall run once during extraction or controlled metric generation rather than being repeated unnecessarily at query time. |
| NFR-009 | API result sets that may be large shall support filtering and pagination or truncation metadata. |
| NFR-010 | Diff calculation shall avoid loading unrelated snapshot data when a narrower comparison scope is requested. |

### 5.3 Reliability

| ID | Requirement |
| --- | --- |
| NFR-011 | Metric calculation failures shall be reported without corrupting snapshot persistence. |
| NFR-012 | Partial metric failures shall not prevent unrelated metric output when isolation is practical. |
| NFR-013 | Diff requests for invalid snapshots shall return deterministic validation errors. |
| NFR-014 | API endpoints shall return predictable errors for unsupported filters, invalid limits, and incompatible snapshot comparisons. |

### 5.4 Security and Safety

| ID | Requirement |
| --- | --- |
| NFR-015 | WP013 shall not expose secrets through metric metadata, hotspot output, architecture-rule results, or diff output. |
| NFR-016 | WP013 shall not execute arbitrary user-provided queries to calculate metrics or diffs. |
| NFR-017 | WP013 shall not expose Neo4j internal IDs as public API identity. |
| NFR-018 | WP013 shall respect existing API authorization seams where present. |
| NFR-019 | WP013 shall not mutate analyzed source repositories. |

### 5.5 Maintainability

| ID | Requirement |
| --- | --- |
| NFR-020 | Metric definitions shall be centralized enough to avoid inconsistent names, scopes, and units. |
| NFR-021 | Architecture-rule check definitions shall remain configurable where the source brief requires configurable behavior. |
| NFR-022 | Snapshot diff comparison logic shall be shared across node, edge, finding, and metric domains where practical. |
| NFR-023 | Internal and non-public implementation types shall follow the same developer-level documentation expectations as public types when documentation is required by implementation standards. |

## 6. Data and Contract Requirements

### 6.1 Metric Scope Kinds

WP013 shall support metric scopes that can represent at minimum:

- Snapshot-scoped metrics.
- Repository-scoped metrics where repository identity is available.
- Solution-scoped metrics where solution identity is available.
- Project-scoped metrics.
- Node-scoped metrics.
- Edge-scoped metrics where edge-specific values are required.

### 6.2 Metric Kinds

WP013 shall define stable metric kinds for at minimum:

- Incoming project references.
- Outgoing project references.
- Package count.
- Public type count.
- Endpoint count.
- Data-access count.
- Hotlist finding count.
- Target-framework age or risk.
- Fan-in.
- Fan-out.
- Centrality.
- Dependency depth.
- Transitive dependency count.
- Cycle detection or cycle participation.
- Neighbourhood size.
- Legacy technology count.
- Security-sensitive finding count.
- Out-of-support target count.
- Framework-only dependency count.
- Data-access spread.
- Shared table usage count.

### 6.3 Hotspot Output Contract

Hotspot responses shall include at minimum:

- Snapshot identity.
- Hotspot category.
- Target stable key.
- Target kind.
- Display name where available.
- Rank or score where applicable.
- Contributing metric stable keys.
- Contributing finding stable keys where applicable.
- Evidence references where applicable.
- Confidence.
- Unknown-state fields.
- Metadata.

### 6.4 Architecture Rule Result Contract

Architecture-rule result responses shall include at minimum:

- Snapshot identity.
- Rule or check identity.
- Rule category.
- Result status.
- Target stable key.
- Target kind.
- Description.
- Contributing metric stable keys where applicable.
- Contributing edge stable keys where applicable.
- Contributing finding stable keys where applicable.
- Evidence references where applicable.
- Confidence.
- Unknown-state fields.
- Metadata.

### 6.5 Snapshot Diff Contract

Snapshot diff responses shall include at minimum:

- Current snapshot identity.
- Previous snapshot identity.
- Comparison scope.
- Summary counts by domain and change kind.
- Node changes.
- Edge changes.
- Finding changes.
- Metric changes.
- Change kind for each record.
- Stable key for each record.
- Previous fingerprint where applicable.
- Current fingerprint where applicable.
- Display name where available.
- Evidence references where available.
- Unknown-state data where applicable.
- Truncation or continuation metadata where applicable.

## 7. Architecture and Integration Requirements

### 7.1 Onion Architecture

WP013 implementation must preserve the repository's Onion Architecture rules:

- Domain projects must not reference Services, Infrastructure, or Host projects.
- Services projects must not reference Infrastructure or Host projects.
- Infrastructure projects must not reference Host projects.
- Host projects contain API endpoint composition and startup/DI wiring only.
- Metric, hotspot, architecture-rule, and diff contracts that are shared across modules must live in the appropriate inward layer.
- Neo4j-specific query or persistence implementations must remain in infrastructure.

### 7.2 Extraction Pipeline Integration

The metric calculation stage must integrate with the established extraction orchestration. It must consume accumulated facts and findings, add metrics, warnings, and errors to the snapshot contract, and allow the existing Neo4j persistence stage to persist the complete snapshot output.

### 7.3 Rule Engine Integration

Architecture-rule checks that depend on metrics or graph structure must integrate with the configured rule catalog and findings model. Generic source-brief architecture checks may be implemented as built-in or configured rules, but organization-specific architecture policy must remain configurable and not hard-coded into the engine.

### 7.4 Neo4j Integration

Neo4j remains the system of record for persisted extraction output. WP013 must persist metrics through the existing graph model and query metrics, snapshots, nodes, edges, and findings through established infrastructure abstractions. Public contracts must use stable keys instead of Neo4j internal IDs.

### 7.5 API Integration

WP013 endpoints must align with the existing API host and module conventions. Endpoints introduced by WP013 may later be incorporated into the complete WP014 query API product surface, but WP013 must expose enough API capability to satisfy its own completion criteria.

## 8. Validation and Testing Requirements

### 8.1 Metric Calculation Tests

| ID | Requirement |
| --- | --- |
| TR-001 | Tests shall cover incoming project reference count. |
| TR-002 | Tests shall cover outgoing project reference count. |
| TR-003 | Tests shall cover package count. |
| TR-004 | Tests shall cover public type count. |
| TR-005 | Tests shall cover endpoint count. |
| TR-006 | Tests shall cover data-access count. |
| TR-007 | Tests shall cover hotlist finding count. |
| TR-008 | Tests shall cover target-framework age or risk. |
| TR-009 | Tests shall cover fan-in. |
| TR-010 | Tests shall cover fan-out. |
| TR-011 | Tests shall cover centrality. |
| TR-012 | Tests shall cover dependency depth. |
| TR-013 | Tests shall cover transitive dependency count. |
| TR-014 | Tests shall cover neighbourhood size. |
| TR-015 | Tests shall cover modernization metric calculations. |

### 8.2 Persistence and Fingerprint Tests

| ID | Requirement |
| --- | --- |
| TR-016 | Tests shall verify metric stable-key determinism. |
| TR-017 | Tests shall verify metric fingerprint determinism. |
| TR-018 | Tests shall verify changed metric values produce changed fingerprints. |
| TR-019 | Tests shall verify metrics persist as snapshot-owned outputs. |
| TR-020 | Tests shall verify metric metadata persistence. |
| TR-021 | Tests shall verify metric evidence relationship persistence where evidence exists. |

### 8.3 Hotspot and Cycle Tests

| ID | Requirement |
| --- | --- |
| TR-022 | Tests shall cover high fan-in hotspot detection. |
| TR-023 | Tests shall cover high fan-out hotspot detection. |
| TR-024 | Tests shall cover shared-library hotspot detection. |
| TR-025 | Tests shall cover data-access spread hotspot detection. |
| TR-026 | Tests shall cover shared table usage hotspot detection. |
| TR-027 | Tests shall cover cycle detection. |
| TR-028 | Tests shall cover duplicate cycle normalization. |
| TR-029 | Tests shall cover deterministic hotspot ranking. |
| TR-030 | Tests shall cover deterministic cycle ordering. |

### 8.4 Architecture Rule Tests

| ID | Requirement |
| --- | --- |
| TR-031 | Tests shall cover domain-to-infrastructure dependency rule checks. |
| TR-032 | Tests shall cover domain-to-web dependency rule checks. |
| TR-033 | Tests shall cover web project referenced by non-web project checks. |
| TR-034 | Tests shall cover application project using LINQ to SQL checks where configured. |
| TR-035 | Tests shall cover controller using DataContext checks where configured. |
| TR-036 | Tests shall cover worker queue or topic dependency checks where configured. |
| TR-037 | Tests shall cover shared high fan-in review checks. |
| TR-038 | Tests shall verify architecture-rule checks use configured rules rather than hard-coded organization-specific policy. |

### 8.5 Snapshot Diff Tests

| ID | Requirement |
| --- | --- |
| TR-039 | Tests shall cover added node diff classification. |
| TR-040 | Tests shall cover removed node diff classification. |
| TR-041 | Tests shall cover changed node diff classification. |
| TR-042 | Tests shall cover unchanged node diff classification. |
| TR-043 | Tests shall cover added edge diff classification. |
| TR-044 | Tests shall cover removed edge diff classification. |
| TR-045 | Tests shall cover changed edge diff classification. |
| TR-046 | Tests shall cover unchanged edge diff classification. |
| TR-047 | Tests shall cover added finding diff classification. |
| TR-048 | Tests shall cover removed finding diff classification. |
| TR-049 | Tests shall cover changed finding diff classification. |
| TR-050 | Tests shall cover unchanged finding diff classification. |
| TR-051 | Tests shall cover added metric diff classification. |
| TR-052 | Tests shall cover removed metric diff classification. |
| TR-053 | Tests shall cover changed metric diff classification. |
| TR-054 | Tests shall cover unchanged metric diff classification. |
| TR-055 | Tests shall verify diff comparison uses stable keys and fingerprints only. |
| TR-056 | Tests shall verify diff validation for missing or incompatible snapshots. |

### 8.6 API Tests

| ID | Requirement |
| --- | --- |
| TR-057 | Tests shall cover snapshot metrics API responses. |
| TR-058 | Tests shall cover project metrics API responses. |
| TR-059 | Tests shall cover graph metrics API responses. |
| TR-060 | Tests shall cover modernization metrics API responses. |
| TR-061 | Tests shall cover hotspot API responses. |
| TR-062 | Tests shall cover cycle API responses. |
| TR-063 | Tests shall cover architecture-rule result API responses. |
| TR-064 | Tests shall cover snapshot diff API responses. |
| TR-065 | Tests shall cover filtering, pagination, limits, or truncation metadata where applicable. |
| TR-066 | Tests shall cover stable-key identity in public responses. |
| TR-067 | Tests shall cover evidence, confidence, and unknown-state response fields where applicable. |

## 9. Acceptance Criteria

WP013 is complete when all of the following are true:

1. Project metrics are calculated for incoming project references, outgoing project references, package count, public type count, endpoint count, data-access count, hotlist finding count, and target-framework age or risk.
2. Graph metrics are calculated for fan-in, fan-out, centrality, dependency depth, transitive dependency count, cycle detection, and neighbourhood size.
3. Modernization metrics are calculated for legacy technology count, security-sensitive finding count, out-of-support target count, framework-only dependency count, data-access spread, and shared table usage count.
4. Metrics are persisted during extraction as first-class snapshot outputs with stable keys and fingerprints.
5. Metrics are not recomputed as transient-only query values for historical snapshots.
6. Coupling and modernization hotspots are detected from persisted graph facts, metrics, and findings.
7. Cycle detection reports deterministic cycle paths using stable node and edge identities.
8. Architecture-rule checks for source-brief layering and dependency patterns are implemented without hard-coding organization-specific rules beyond configured rule catalog behavior.
9. Snapshot diff reports added, removed, changed, and unchanged records across nodes, edges, findings, and metrics.
10. Diff comparison uses stable keys and normalized fingerprints, not database IDs.
11. API consumers can retrieve project metrics, graph metrics, modernization metrics, hotspots, cycles, architecture-rule results, and snapshot diffs.
12. Evidence, confidence, and unknown-state data are preserved in metric, hotspot, rule-result, and diff outputs where applicable.
13. Tests cover metric calculation, persistence, fingerprint comparison, added/removed/changed/unchanged diff cases, cycle detection, hotspot detection, and architecture-rule integration.
14. Repository documentation explains metric definitions, hotspot semantics, architecture-rule behavior, snapshot diff semantics, evidence handling, confidence handling, unknown handling, and validation workflow.
15. No Archon Discovery UI, dashboard, explorer, graph page, prompt panel, or other human-facing UI surface is created.

## 10. Risks and Open Questions

### 10.1 Risks

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Large repositories may produce expensive graph calculations. | Extraction or metric generation may become slow. | Use scoped traversal, deterministic limits, cached intermediate structures, and persisted metrics. |
| Centrality definitions can vary. | Consumers may misinterpret values. | Document the selected centrality calculation and include metadata describing calculation scope. |
| Hotspot ranking can appear subjective. | Users may distrust rankings. | Base rankings only on documented metrics, findings, and deterministic tie-breaking. |
| Architecture-rule checks may drift into organization-specific policy. | Product behavior becomes too opinionated. | Keep organization-specific policy in configured rules and document generic built-in behavior. |
| Snapshot diff can become very large. | API responses may be impractical. | Provide filtering, limits, pagination, and truncation metadata. |
| Missing evidence can obscure why a metric changed. | Change explanation may be incomplete. | Preserve evidence links where available and explicit unknown reasons where not available. |

### 10.2 Definitive Answers

| ID | Question | Definitive Answer |
| --- | --- | --- |
| OQ-001 | Which centrality algorithm should be used first? | WP013 shall initially use normalized degree-based centrality derived from fan-in and fan-out. More advanced centrality algorithms are out of scope unless later evidence shows they are needed. |
| OQ-002 | Should diff results themselves be persisted? | WP013 shall compute diff results deterministically from persisted snapshots and shall not persist diff reports as first-class records. |
| OQ-003 | Should hotspot thresholds be fixed or configurable? | WP013 shall provide documented default hotspot thresholds, but policy-like thresholds shall be configurable through the rule catalog and metric-threshold conditions. |
| OQ-004 | Should unchanged records be returned by default in diff responses? | WP013 shall return unchanged counts by default and shall only return unchanged record details when explicitly requested. |

## 11. Traceability Matrix

| Source Requirement | Specification Coverage |
| --- | --- |
| `docs/foundation/work-packages.md` WP013 objective | Sections 1, 3, 4, 9 |
| WP013 project metrics | FR-035 through FR-045, TR-001 through TR-008 |
| WP013 graph metrics | FR-046 through FR-057, TR-009 through TR-014 |
| WP013 modernization metrics | FR-058 through FR-067, TR-015 |
| WP013 metric persistence | FR-011 through FR-034, TR-016 through TR-021 |
| WP013 architecture-rule checks | FR-089 through FR-100, TR-031 through TR-038 |
| WP013 snapshot diff | FR-101 through FR-120, TR-039 through TR-056 |
| WP013 API endpoints | FR-121 through FR-134, TR-057 through TR-067 |
| Source brief section 32 quality, confidence, and classification | FR-135 through FR-142, NFR-001 through NFR-005 |
| Source brief section 33 metrics | FR-035 through FR-067 |
| Source brief section 34 architecture rules and layering | FR-089 through FR-100 |
| Source brief Appendix E.5.2.9 metrics | FR-011 through FR-034 |
| Source brief Appendix E.5.9 diff strategy | FR-101 through FR-120 |
| Completion rule: no Discovery UI | Scope exclusions, Acceptance Criterion 15 |

## 12. Out of Scope

The following are explicitly out of scope for WP013:

- Archon Discovery UI implementation.
- Human-facing dashboard, explorer, graph view, evidence viewer, hotlist viewer, or prompt panel.
- MCP tools, MCP resources, MCP prompts, and MCP security behavior.
- Markdown export generation.
- New extraction domains beyond the metrics, hotspots, architecture-rule, and diff behavior specified here.
- Automatic remediation or code modification recommendations.
- Arbitrary graph query execution from API callers.
- Organization-specific hard-coded architecture rules beyond configured rule catalog behavior.
- Replacing Neo4j as the system of record.

## 13. Implementation Readiness Checklist

Before WP013 implementation begins, confirm that:

- WP012 findings are available for metric calculations that depend on hotlist or finding counts.
- Stable-key and fingerprint helpers support metric identity and comparison.
- Neo4j persistence supports metric records and metric-to-evidence relationships.
- The extraction orchestration path can run metric calculation after required inputs are available.
- Public API response conventions for stable keys, evidence references, confidence, unknowns, pagination, and validation errors are available or can be extended consistently.
- Test fixtures can create comparable snapshots with controlled nodes, edges, findings, and metrics.
