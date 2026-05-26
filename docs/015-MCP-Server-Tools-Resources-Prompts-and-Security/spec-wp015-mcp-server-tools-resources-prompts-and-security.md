# WP015 Specification - MCP Server, Tools, Resources, Prompts, and Security

## Document Control

| Field | Value |
| --- | --- |
| Work Package | WP015 - MCP Server, Tools, Resources, Prompts, and Security |
| Output Path | `docs/015-MCP-Server-Tools-Resources-Prompts-and-Security/spec-wp015-mcp-server-tools-resources-prompts-and-security.md` |
| Source Work Package | `docs/foundation/work-packages.md` WP015 |
| Source Brief | `docs/foundation/archon_full_concept_brief.md` |
| Specification Basis | `spec-template_v1.1.md` requested; template file was not present in the workspace at creation time, so this document follows the repository specification structure and documented work-package rules. |
| Status | Draft |
| Audience | Product owner, architect, implementation team, test engineer, security reviewer, MCP consumer, Copilot workflow designer |

## 1. Overview

### 1.1 Purpose

This specification defines the requirements for WP015, the Archon work package that delivers the complete read-only Model Context Protocol server product surface for Copilot and other AI assistants.

WP015 turns the query and management capabilities delivered by earlier work packages into safe, evidence-backed MCP tools, resources, and prompts. It enables AI-assisted architecture investigation without granting AI clients arbitrary shell access, arbitrary database access, filesystem mutation, code modification, or direct Neo4j query capability.

### 1.2 Background

Archon is an API-first and MCP-first deterministic architecture intelligence platform for modern and legacy .NET estates. Earlier work packages establish the solution foundation, graph domain model, Neo4j persistence, extraction orchestration, source-code and technology extraction, rule catalog and findings, metrics, snapshot diff, and the complete API query product surface.

WP015 builds on those completed API and application/query capabilities. It does not introduce extraction behavior, graph persistence behavior, markdown export generation, Archon Discovery UI pages, or direct persistence access. Instead, it exposes a controlled read-only MCP server that helps AI clients answer architecture questions using persisted facts, confidence, evidence, findings, unknowns, and suggested follow-ups.

### 1.3 High-Level Scope

WP015 covers these capability areas:

- Read-only MCP server hosting in the existing Archon MCP host.
- MCP tool implementation for architecture search, project description, dependency traversal, symbol lookup, data-access usage, change impact, architecture rules, hotlist findings, and snapshot diff.
- MCP resource implementation for current snapshot, project, symbol, rules, hotlist, hotspots, and snapshot diff resources.
- MCP prompt implementation for impact analysis, modernization briefs, refactoring preflight, new-feature placement, legacy data-access review, hotlist summary, and architecture-rule checks.
- Evidence-backed MCP response envelopes with summaries, confidence, facts, evidence, findings, unknowns, warnings, truncation metadata, and suggested follow-ups.
- Authentication, authorization seams, audit logging, read-only enforcement, tool allow-listing, environment isolation, no-secrets exposure, response-size controls, and prompt-injection-aware output handling.
- Tests and documentation for all MCP tools, resources, prompts, security controls, and response behaviors.

WP015 excludes Archon Discovery UI implementation, arbitrary shell execution, arbitrary SQL execution, arbitrary Cypher execution, direct Neo4j exposure, filesystem mutation, database mutation, code modification, extraction triggering unless explicitly required by a controlled API from another work package, and automatic remediation of target repositories.

## 2. System Context

### 2.1 Product Context

Archon accepts API-triggered extraction requests, extracts deterministic architecture facts into snapshots, persists those facts in Neo4j, evaluates rules and metrics, exposes API query surfaces, and then provides MCP-compatible architecture intelligence to AI assistants. WP015 is the AI-assistant-facing layer over the existing application and API query capabilities.

The MCP server must use existing application/query abstractions and API-compatible DTO contracts wherever possible. It must not become a parallel source of truth, duplicate graph persistence behavior, or let MCP clients bypass product security boundaries. The server exists to help AI clients reason over persisted architecture knowledge, not to grant general automation authority over the developer machine, repository, database, or network.

### 2.2 Source References

WP015 must align with these source materials:

- `docs/foundation/work-packages.md` WP015 objective, required implementation, and completion criteria.
- `docs/foundation/archon_full_concept_brief.md` section 6.3 for MCP reference points.
- `docs/foundation/archon_full_concept_brief.md` section 8.1 for Archon MCP Server responsibilities.
- `docs/foundation/archon_full_concept_brief.md` section 29 for MCP design principles, tools, resources, prompts, and security requirements.
- `docs/foundation/archon_full_concept_brief.md` section 30 for Copilot workflows.
- `docs/foundation/archon_full_concept_brief.md` Appendix C for example MCP tool response shape.
- `docs/foundation/archon_full_concept_brief.md` section 35 phase 8 and section 36 MCP epic.
- `docs/foundation/archon_full_concept_brief.md` Appendix E sections E.5.7 and E.5.8 for resource refresh and query model implications.
- `docs/foundation/work-packages.md` completion rules for evidence-backed statements, explicit unknowns, deterministic stable keys, Neo4j as the system of record, tests, documentation, and no Discovery UI.
- `.github/instructions/documentation-pass.instructions.md` for mandatory source documentation expectations during implementation planning and execution, including internal and non-public types.

### 2.3 Users and Stakeholders

| Stakeholder | Interest |
| --- | --- |
| Product owner | Confirms WP015 exposes the mandatory MCP-first product capabilities without introducing unsafe automation or deferred MCP behavior. |
| Architect | Confirms MCP responses preserve deterministic evidence-backed architecture intelligence and do not expose raw persistence or mutate system state. |
| Developer | Uses AI-assistant workflows to understand projects, dependencies, symbols, data access, change impact, rules, findings, hotspots, and diffs. |
| MCP consumer | Calls tools, resources, and prompts through MCP-compatible clients and expects stable, bounded, explainable responses. |
| Copilot workflow designer | Uses prompt templates and tool outputs to support safe architecture investigation and modernization planning. |
| Test engineer | Verifies every MCP tool, resource, prompt, response envelope, edge case, security control, and truncation behavior. |
| Security reviewer | Confirms read-only enforcement, authorization, audit logging, prompt-injection handling, no secret exposure, and absence of arbitrary execution or mutation capabilities. |
| Operations owner | Reviews health, readiness, audit traces, configuration, authentication seams, and operational diagnostics for the MCP host. |

## 3. Component Summary

### 3.1 MCP Host Component

The MCP Host Component runs the Archon MCP server process and wires MCP protocol handling to the application/query layer. It owns server startup, health/readiness checks, dependency injection, configuration, telemetry, request limits, authentication and authorization seams, and audit logging integration.

### 3.2 MCP Tool Component

The MCP Tool Component exposes bounded read-only tool calls that map to Archon query capabilities. Tools must return evidence-backed, response-size-limited results that are safe for AI clients to consume. Tool implementations must not issue arbitrary database queries, invoke shell commands, mutate files, mutate source code, or modify graph state.

### 3.3 MCP Resource Component

The MCP Resource Component exposes stable architecture resources using `archon://` URIs. Resources provide AI clients with current or selected snapshot context, project context, symbol context, rules, hotlist, hotspots, and diff information without requiring the client to know internal API routing or graph persistence details.

### 3.4 MCP Prompt Component

The MCP Prompt Component exposes curated prompts for common architecture intelligence workflows. Prompts must be grounded in tool and resource outputs, direct the AI client to cite evidence and unknowns, and discourage unsupported conclusions, invention of facts, or code-changing actions outside the scope of the read-only MCP server.

### 3.5 Response Envelope Component

The Response Envelope Component defines common MCP response shapes for summaries, confidence, facts, evidence, findings, unknowns, warnings, truncation, continuation metadata, and suggested follow-ups. It provides consistency across tools, resources, and prompt support.

### 3.6 Security and Isolation Component

The Security and Isolation Component enforces read-only behavior, authentication, authorization, tool allow-listing, prompt-injection-aware output handling, no-secrets exposure, environment isolation, audit logging, and controlled failure behavior.

### 3.7 Documentation and Test Component

The Documentation and Test Component ensures all MCP behavior is documented and verified. It covers tool contracts, resource URI semantics, prompt intent, response examples, security controls, test fixtures, and implementation guidance for public, internal, and other non-public types.

## 4. Functional Requirements

### 4.1 MCP Server Principles

| ID | Requirement |
| --- | --- |
| FR-001 | WP015 shall implement the Archon MCP server as a read-only product surface for AI assistants. |
| FR-002 | The MCP server shall use existing application/query/API-layer abstractions rather than directly querying Neo4j from tool implementations. |
| FR-003 | MCP responses shall be based on persisted architecture facts, persisted evidence, persisted findings, persisted metrics, persisted rules, deterministic diff output, or explicit unknowns. |
| FR-004 | MCP responses shall not invent facts, ownership, intent, risk, data flow, dependency direction, remediation, or confidence that is not supported by persisted facts, rules, findings, metrics, evidence, or explicit unknowns. |
| FR-005 | MCP public contracts shall use stable keys instead of Neo4j internal IDs. |
| FR-006 | MCP tools and resources shall expose snapshot identity when results depend on snapshot state. |
| FR-007 | MCP tools and resources shall support a current snapshot selector where the current snapshot is unambiguous. |
| FR-008 | MCP tools and resources shall return confidence and unknowns where extraction or graph facts are incomplete or uncertain. |
| FR-009 | MCP shall not implement Archon Discovery UI pages, dashboard views, client-side graph rendering, or front-end assets. |
| FR-010 | MCP shall not expose arbitrary shell execution, arbitrary SQL execution, arbitrary Cypher execution, arbitrary graph traversal syntax, filesystem mutation, database mutation, or code modification capabilities. |
| FR-011 | MCP shall not expose secrets, connection string values, environment secrets, raw credentials, API keys, access tokens, or sensitive local machine details. |
| FR-012 | MCP implementation shall keep deterministic architecture facts in Neo4j as the system of record and shall not create a competing MCP-only persistence model. |

### 4.2 Common Tool Request and Response Behavior

| ID | Requirement |
| --- | --- |
| FR-013 | Every MCP tool shall validate required parameters before calling the application/query layer. |
| FR-014 | Every MCP tool shall reject unsupported parameters with a structured validation error. |
| FR-015 | Every MCP tool shall enforce configured response-size limits. |
| FR-016 | Every MCP tool shall return truncation metadata when results are limited. |
| FR-017 | Every MCP tool shall return continuation or suggested-narrowing information where a result set exceeds configured limits. |
| FR-018 | Every MCP tool shall include a concise summary suitable for AI context. |
| FR-019 | Every MCP tool shall include structured facts where facts are available. |
| FR-020 | Every MCP tool shall include evidence references where facts are evidence-backed. |
| FR-021 | Every MCP tool shall include findings where the requested context intersects persisted findings. |
| FR-022 | Every MCP tool shall include unknowns where data is incomplete, uncertain, or unavailable. |
| FR-023 | Every MCP tool shall include warnings for partial results, inaccessible snapshots, unavailable optional data, or truncation. |
| FR-024 | Every MCP tool shall include suggested follow-ups that are limited to safe Archon MCP or API investigation paths. |
| FR-025 | Every MCP tool shall return consistent not-found, ambiguous, validation, authorization, and server-error shapes. |
| FR-026 | Error responses shall not expose raw stack traces, secrets, connection strings, or sensitive internal implementation details. |
| FR-027 | Tool implementations shall be deterministic for the same persisted snapshot and same request parameters, except for audit metadata and request timing fields. |

### 4.3 `archon.search` Tool

| ID | Requirement |
| --- | --- |
| FR-028 | The MCP server shall implement `archon.search`. |
| FR-029 | `archon.search` shall search persisted architecture facts across projects, symbols, endpoints, workers, data-access facts, configuration keys, integrations, rules, findings, and evidence summaries where supported by the query layer. |
| FR-030 | `archon.search` shall accept search text. |
| FR-031 | `archon.search` shall accept optional snapshot selection. |
| FR-032 | `archon.search` shall accept optional result-type filters. |
| FR-033 | `archon.search` shall accept optional project, solution, or repository scope filters where supported. |
| FR-034 | `archon.search` shall return ranked or grouped results using deterministic ordering. |
| FR-035 | `archon.search` shall include stable keys and entity kinds for each result. |
| FR-036 | `archon.search` shall include evidence references when the matched item is evidence-backed. |
| FR-037 | `archon.search` shall clearly distinguish no matches from unavailable search data. |

### 4.4 `archon.describe_project` Tool

| ID | Requirement |
| --- | --- |
| FR-038 | The MCP server shall implement `archon.describe_project`. |
| FR-039 | `archon.describe_project` shall accept a project stable key or an unambiguous project name. |
| FR-040 | `archon.describe_project` shall return project identity, path, language, target frameworks, project format, application type, and snapshot identity. |
| FR-041 | `archon.describe_project` shall return project responsibilities only when persisted or deterministically derived from persisted facts. |
| FR-042 | `archon.describe_project` shall return incoming and outgoing project dependencies. |
| FR-043 | `archon.describe_project` shall return packages and framework dependencies where available. |
| FR-044 | `archon.describe_project` shall return endpoints, workers, hosted services, queue/topic consumers, data-access usage, configuration keys, and integrations associated with the project. |
| FR-045 | `archon.describe_project` shall return related findings, hotlist indicators, metrics, and hotspots where available. |
| FR-046 | `archon.describe_project` shall return evidence and unknowns for the project-level summary. |
| FR-047 | Ambiguous project-name lookup shall return a disambiguation error rather than selecting an arbitrary project. |

### 4.5 `archon.get_dependencies` Tool

| ID | Requirement |
| --- | --- |
| FR-048 | The MCP server shall implement `archon.get_dependencies`. |
| FR-049 | `archon.get_dependencies` shall accept a source node stable key or project identifier. |
| FR-050 | `archon.get_dependencies` shall support direct dependency mode. |
| FR-051 | `archon.get_dependencies` shall support transitive dependency mode. |
| FR-052 | `archon.get_dependencies` shall support maximum depth. |
| FR-053 | `archon.get_dependencies` shall support edge-kind filters where supported by the query layer. |
| FR-054 | `archon.get_dependencies` shall return dependency nodes and relationships using stable keys. |
| FR-055 | `archon.get_dependencies` shall return evidence for evidence-backed relationships. |
| FR-056 | `archon.get_dependencies` shall enforce depth and result-size limits. |
| FR-057 | `archon.get_dependencies` shall distinguish no dependencies from unavailable dependency data. |

### 4.6 `archon.get_dependents` Tool

| ID | Requirement |
| --- | --- |
| FR-058 | The MCP server shall implement `archon.get_dependents`. |
| FR-059 | `archon.get_dependents` shall accept a target node stable key or project identifier. |
| FR-060 | `archon.get_dependents` shall support direct dependent mode. |
| FR-061 | `archon.get_dependents` shall support transitive dependent mode. |
| FR-062 | `archon.get_dependents` shall support maximum depth. |
| FR-063 | `archon.get_dependents` shall support edge-kind filters where supported by the query layer. |
| FR-064 | `archon.get_dependents` shall return dependent nodes and relationships using stable keys. |
| FR-065 | `archon.get_dependents` shall return evidence for evidence-backed relationships. |
| FR-066 | `archon.get_dependents` shall enforce depth and result-size limits. |
| FR-067 | `archon.get_dependents` shall distinguish no dependents from unavailable dependent data. |

### 4.7 `archon.find_dependency_paths` Tool

| ID | Requirement |
| --- | --- |
| FR-068 | The MCP server shall implement `archon.find_dependency_paths`. |
| FR-069 | `archon.find_dependency_paths` shall accept source and target stable keys. |
| FR-070 | `archon.find_dependency_paths` shall support maximum depth. |
| FR-071 | `archon.find_dependency_paths` shall support edge-kind filters where supported by the query layer. |
| FR-072 | `archon.find_dependency_paths` shall return paths in deterministic order. |
| FR-073 | Each returned path shall include ordered node and relationship stable keys. |
| FR-074 | Each returned path shall include evidence references for evidence-backed relationships. |
| FR-075 | `archon.find_dependency_paths` shall return a no-path response that distinguishes no relationship from unavailable graph data. |
| FR-076 | `archon.find_dependency_paths` shall enforce maximum path count, maximum depth, and response-size limits. |

### 4.8 `archon.describe_symbol` Tool

| ID | Requirement |
| --- | --- |
| FR-077 | The MCP server shall implement `archon.describe_symbol`. |
| FR-078 | `archon.describe_symbol` shall accept a symbol stable key or unambiguous symbol search parameters. |
| FR-079 | `archon.describe_symbol` shall return symbol kind, name, containing type, containing namespace, project, source file, and snapshot identity where available. |
| FR-080 | `archon.describe_symbol` shall return relationships such as contains, calls, implements, inherits, injects, depends-on, and uses-config where available. |
| FR-081 | `archon.describe_symbol` shall return evidence spans, snippet previews, confidence, and unknowns. |
| FR-082 | `archon.describe_symbol` shall return related findings and rules where available. |
| FR-083 | Ambiguous symbol lookup shall return a disambiguation error rather than selecting an arbitrary symbol. |

### 4.9 `archon.find_symbol_usages` Tool

| ID | Requirement |
| --- | --- |
| FR-084 | The MCP server shall implement `archon.find_symbol_usages`. |
| FR-085 | `archon.find_symbol_usages` shall accept a symbol stable key or unambiguous symbol search parameters. |
| FR-086 | `archon.find_symbol_usages` shall return callers, references, injections, configuration usage, endpoint usage, data-access usage, or other persisted usage relationships where available. |
| FR-087 | `archon.find_symbol_usages` shall support filters for usage kind, project, and depth where supported by the query layer. |
| FR-088 | `archon.find_symbol_usages` shall return evidence and confidence for each usage relationship. |
| FR-089 | `archon.find_symbol_usages` shall enforce pagination or response-size limits. |
| FR-090 | `archon.find_symbol_usages` shall distinguish no usages from unavailable symbol usage data. |

### 4.10 `archon.get_data_access_usage` Tool

| ID | Requirement |
| --- | --- |
| FR-091 | The MCP server shall implement `archon.get_data_access_usage`. |
| FR-092 | `archon.get_data_access_usage` shall return LINQ to SQL, Entity Framework Classic/EF6, Entity Framework Core, ADO.NET, raw SQL, stored procedure, typed DataSet, table, column, and data-context facts where available. |
| FR-093 | `archon.get_data_access_usage` shall support project, data-context, entity, table, stored procedure, and snapshot filters where supported. |
| FR-094 | `archon.get_data_access_usage` shall identify read, write, execute, unknown, and dynamic SQL indicators where persisted. |
| FR-095 | `archon.get_data_access_usage` shall return confidence and unknown reasons for dynamic SQL, unresolved targets, or partial extraction. |
| FR-096 | `archon.get_data_access_usage` shall return evidence for data-access facts. |
| FR-097 | `archon.get_data_access_usage` shall enforce response-size limits and include truncation metadata. |

### 4.11 `archon.assess_change_impact` Tool

| ID | Requirement |
| --- | --- |
| FR-098 | The MCP server shall implement `archon.assess_change_impact`. |
| FR-099 | `archon.assess_change_impact` shall accept a project, symbol, endpoint, data-access entity, configuration key, integration, rule, finding, or other supported stable key as the change target. |
| FR-100 | `archon.assess_change_impact` shall use dependency, dependent, usage, finding, metric, and evidence data from the query layer to summarize possible impact. |
| FR-101 | `archon.assess_change_impact` shall distinguish direct impact from transitive impact. |
| FR-102 | `archon.assess_change_impact` shall include impacted projects, symbols, endpoints, workers, data-access facts, integrations, configuration keys, rules, and findings where available. |
| FR-103 | `archon.assess_change_impact` shall include confidence and unknowns for incomplete or uncertain impact data. |
| FR-104 | `archon.assess_change_impact` shall not recommend code changes unless the recommendation is framed as investigation guidance derived from evidence-backed facts. |
| FR-105 | `archon.assess_change_impact` shall include suggested follow-up MCP calls to narrow or verify the impact assessment. |

### 4.12 `archon.get_architecture_rules` Tool

| ID | Requirement |
| --- | --- |
| FR-106 | The MCP server shall implement `archon.get_architecture_rules`. |
| FR-107 | `archon.get_architecture_rules` shall return rule catalog records, enabled status, version, category, severity, description, and applicable scopes where available. |
| FR-108 | `archon.get_architecture_rules` shall support filters for rule code, category, severity, enabled status, and snapshot where supported. |
| FR-109 | `archon.get_architecture_rules` shall return related finding counts where available. |
| FR-110 | `archon.get_architecture_rules` shall not allow MCP clients to create, edit, enable, disable, delete, or persist rule changes. |
| FR-111 | `archon.get_architecture_rules` shall return evidence or source references for rule records where persisted. |

### 4.13 `archon.get_hotlist_findings` Tool

| ID | Requirement |
| --- | --- |
| FR-112 | The MCP server shall implement `archon.get_hotlist_findings`. |
| FR-113 | `archon.get_hotlist_findings` shall return findings with rule code, rule version, severity, status, confidence, first seen, latest seen, affected nodes, evidence, unknowns, and metadata where available. |
| FR-114 | `archon.get_hotlist_findings` shall support filters for project, rule, category, severity, status, snapshot, and text search where supported. |
| FR-115 | `archon.get_hotlist_findings` shall support deterministic sorting. |
| FR-116 | `archon.get_hotlist_findings` shall enforce response-size limits and truncation metadata. |
| FR-117 | `archon.get_hotlist_findings` shall not allow MCP clients to suppress, unsuppress, edit, delete, or otherwise mutate findings. |

### 4.14 `archon.get_snapshot_diff` Tool

| ID | Requirement |
| --- | --- |
| FR-118 | The MCP server shall implement `archon.get_snapshot_diff`. |
| FR-119 | `archon.get_snapshot_diff` shall accept current and previous snapshot identifiers or a current snapshot with an implied previous snapshot where supported by the query layer. |
| FR-120 | `archon.get_snapshot_diff` shall return added, removed, changed, and unchanged counts for nodes, edges, findings, and metrics. |
| FR-121 | `archon.get_snapshot_diff` shall return detailed diff records where requested and within response-size limits. |
| FR-122 | `archon.get_snapshot_diff` shall use stable keys and fingerprints rather than database IDs to determine diff results. |
| FR-123 | `archon.get_snapshot_diff` shall include evidence references and confidence where available. |
| FR-124 | `archon.get_snapshot_diff` shall distinguish unavailable diff data from no changes. |

### 4.15 MCP Resources

| ID | Requirement |
| --- | --- |
| FR-125 | The MCP server shall implement resource `archon://snapshot/current`. |
| FR-126 | The MCP server shall implement resource `archon://project/{projectKey}`. |
| FR-127 | The MCP server shall implement resource `archon://symbol/{symbolKey}`. |
| FR-128 | The MCP server shall implement resource `archon://rules/current`. |
| FR-129 | The MCP server shall implement resource `archon://hotlist/current`. |
| FR-130 | The MCP server shall implement resource `archon://hotspots/current`. |
| FR-131 | The MCP server shall implement resource `archon://snapshot/{snapshotId}/diff/{previousSnapshotId}`. |
| FR-132 | Resources shall return bounded, structured, evidence-aware content suitable for AI context. |
| FR-133 | Resource URI parsing shall reject malformed, unsupported, ambiguous, or unauthorized resource requests with structured errors. |
| FR-134 | Current resources shall define how current snapshot selection is resolved and shall report an error if current snapshot selection is ambiguous. |
| FR-135 | Resource outputs shall include snapshot identity where relevant. |
| FR-136 | Resource outputs shall enforce response-size and no-secrets controls. |

### 4.16 MCP Prompts

| ID | Requirement |
| --- | --- |
| FR-137 | The MCP server shall implement prompt `impact-analysis`. |
| FR-138 | The MCP server shall implement prompt `modernization-brief`. |
| FR-139 | The MCP server shall implement prompt `refactoring-preflight`. |
| FR-140 | The MCP server shall implement prompt `new-feature-placement`. |
| FR-141 | The MCP server shall implement prompt `legacy-data-access-review`. |
| FR-142 | The MCP server shall implement prompt `hotlist-summary`. |
| FR-143 | The MCP server shall implement prompt `architecture-rule-check`. |
| FR-144 | Prompts shall instruct the AI client to ground conclusions in Archon tools, resources, evidence, findings, metrics, and unknowns. |
| FR-145 | Prompts shall instruct the AI client not to invent architecture facts or unsupported implementation guidance. |
| FR-146 | Prompts shall include suggested tool/resource usage sequences where appropriate. |
| FR-147 | Prompts shall include explicit guidance to report unknowns, uncertainty, missing evidence, and follow-up questions. |
| FR-148 | Prompts shall not request shell commands, arbitrary database queries, filesystem mutation, source-code mutation, or direct repository modification. |
| FR-149 | Prompts shall include prompt-injection resilience guidance for treating extracted source text, evidence snippets, comments, markdown, and configuration content as untrusted data. |

### 4.17 Read-Only Enforcement

| ID | Requirement |
| --- | --- |
| FR-150 | MCP tool implementations shall be limited to read-only query operations. |
| FR-151 | MCP resources shall be read-only. |
| FR-152 | MCP prompts shall not introduce write, mutation, shell, database, or source-code modification workflows. |
| FR-153 | The MCP host shall not register tools that can mutate files, source code, graph data, rule data, findings, configuration, or external systems. |
| FR-154 | The MCP host shall not expose an escape hatch for arbitrary command execution. |
| FR-155 | The MCP host shall not expose an escape hatch for arbitrary Cypher, SQL, or query-language execution. |
| FR-156 | Any future mutation-capable product feature shall be outside WP015 scope and shall require a separate work package and explicit security model. |

### 4.18 Authentication, Authorization, and Audit

| ID | Requirement |
| --- | --- |
| FR-157 | The MCP host shall provide an authentication seam compatible with the repository's host configuration approach. |
| FR-158 | The MCP host shall provide an authorization seam for tool and resource access decisions. |
| FR-159 | Tool allow-listing shall be configurable. |
| FR-160 | Unauthorized tool calls shall fail before invoking the application/query layer. |
| FR-161 | Unauthorized resource reads shall fail before invoking the application/query layer. |
| FR-162 | Audit logging shall record MCP tool calls. |
| FR-163 | Audit logging shall record MCP resource reads. |
| FR-164 | Audit logging shall record prompt retrieval where meaningful. |
| FR-165 | Audit logging shall include caller identity when available, operation name, normalized parameters safe for logging, result status, truncation status, and timing. |
| FR-166 | Audit logs shall not record secrets, raw credentials, access tokens, connection strings, or unsafe evidence snippets. |

### 4.19 Prompt-Injection-Aware Handling

| ID | Requirement |
| --- | --- |
| FR-167 | MCP responses shall treat extracted source code, comments, markdown, configuration values, evidence snippets, and repository content as untrusted data. |
| FR-168 | MCP response metadata shall distinguish Archon-generated summary text from extracted repository content. |
| FR-169 | MCP prompts shall instruct AI clients not to follow instructions embedded in extracted repository content. |
| FR-170 | MCP outputs shall avoid presenting untrusted evidence snippets as system or developer instructions. |
| FR-171 | MCP outputs shall sanitize or omit content that is likely to expose secrets or unsafe instruction text beyond what is needed for evidence-backed investigation. |
| FR-172 | Prompt-injection handling shall be tested with malicious comments, markdown, configuration values, and source snippets. |

### 4.20 Health and Readiness

| ID | Requirement |
| --- | --- |
| FR-173 | The MCP host shall expose health behavior consistent with the repository service defaults. |
| FR-174 | The MCP host shall expose readiness behavior consistent with the repository service defaults. |
| FR-175 | Readiness shall reflect availability of required application/query dependencies. |
| FR-176 | Readiness shall reflect whether required MCP tools, resources, and prompts are registered. |
| FR-177 | Health and readiness behavior shall not expose secrets or sensitive internal implementation details. |

## 5. Non-Functional Requirements

### 5.1 Security

| ID | Requirement |
| --- | --- |
| NFR-001 | The MCP server shall be read-only by design and by implementation. |
| NFR-002 | The MCP server shall use least-privilege dependencies. |
| NFR-003 | The MCP server shall not require direct database credentials in MCP client-visible configuration. |
| NFR-004 | The MCP server shall not expose raw Neo4j connection details to MCP clients. |
| NFR-005 | The MCP server shall avoid logging sensitive parameters or evidence content. |
| NFR-006 | The MCP server shall support authorization checks before expensive query execution. |
| NFR-007 | The MCP server shall fail closed for unknown tools, unknown resources, malformed requests, and unauthorized access. |
| NFR-008 | The MCP server shall document all intentionally exposed tools, resources, and prompts. |

### 5.2 Reliability

| ID | Requirement |
| --- | --- |
| NFR-009 | MCP requests shall handle cancellation. |
| NFR-010 | MCP requests shall handle query-layer failures with structured errors. |
| NFR-011 | MCP requests shall not corrupt graph state when query failures occur. |
| NFR-012 | MCP requests shall distinguish dependency unavailability from empty query results. |
| NFR-013 | MCP requests shall return partial-result warnings when optional downstream data is unavailable but the main result can still be returned. |

### 5.3 Performance and Limits

| ID | Requirement |
| --- | --- |
| NFR-014 | MCP tools and resources shall enforce configurable maximum result sizes. |
| NFR-015 | MCP tools and resources shall enforce configurable maximum traversal depth where applicable. |
| NFR-016 | MCP tools and resources shall enforce configurable maximum evidence item count where applicable. |
| NFR-017 | MCP tools and resources shall avoid loading unbounded graph neighborhoods. |
| NFR-018 | MCP tools and resources shall prefer query-layer pagination and filtering rather than loading and trimming large results in memory. |
| NFR-019 | MCP responses shall be shaped for AI context efficiency by default. |

### 5.4 Maintainability

| ID | Requirement |
| --- | --- |
| NFR-020 | MCP tool handlers shall be organized so each tool has a clear request contract, validator, query mapping, response mapper, and test coverage. |
| NFR-021 | MCP resource handlers shall be organized so each resource URI has a clear parser, authorization check, query mapping, response mapper, and test coverage. |
| NFR-022 | MCP prompts shall be stored and tested in a maintainable form that avoids hard-coded duplicated workflow text where practical. |
| NFR-023 | Shared response-envelope logic shall be reused across tools and resources. |
| NFR-024 | Internal and other non-public MCP types shall receive the same developer-level documentation standard as public types when documentation-pass requirements apply. |

### 5.5 Observability

| ID | Requirement |
| --- | --- |
| NFR-025 | MCP tool calls shall produce structured telemetry. |
| NFR-026 | MCP resource reads shall produce structured telemetry. |
| NFR-027 | MCP prompt retrieval shall produce structured telemetry where meaningful. |
| NFR-028 | Telemetry shall include operation name, duration, result status, truncation status, and error category where applicable. |
| NFR-029 | Telemetry shall not include secrets, credentials, or unsafe evidence snippets. |

## 6. Data and Contract Requirements

### 6.1 Common MCP Response Envelope

| Field | Description |
| --- | --- |
| `operation` | MCP tool, resource, or prompt-related operation name. |
| `snapshot` | Snapshot identity and selection mode where relevant. |
| `summary` | Concise natural-language summary grounded in returned facts. |
| `confidence` | Overall confidence value or classification derived from persisted facts. |
| `facts` | Structured facts returned by the operation. |
| `evidence` | Evidence references with stable keys, file path, line range, symbol, snippet preview or hash, and confidence where available. |
| `findings` | Related findings with rule code, severity, status, confidence, and affected node references where available. |
| `unknowns` | Explicit unknowns with reason and affected scope. |
| `warnings` | Partial-result, truncation, unavailable dependency, or data-quality warnings. |
| `limits` | Applied limit, truncation, continuation, or suggested narrowing metadata. |
| `suggestedFollowUps` | Safe follow-up tool/resource calls or investigation questions. |

### 6.2 Evidence Reference Contract

Evidence references returned by MCP shall include stable evidence identity, evidence kind, source file path where applicable, line range where available, symbol name where available, containing symbol where available, snippet preview or snippet hash where allowed, confidence, and snapshot context.

Evidence references shall not expose raw secrets. If a snippet appears sensitive, the response shall prefer a redacted preview, a hash, or an omission warning.

### 6.3 Unknown Contract

Unknown records returned by MCP shall include unknown kind, affected entity stable key where applicable, reason, confidence impact, and suggested follow-up where useful. Unknown records shall distinguish unsupported extraction, unresolved symbol, unavailable snapshot, partial repository load, truncated result, dynamic target, and missing evidence where those distinctions are known.

### 6.4 Finding Reference Contract

Finding references returned by MCP shall include finding stable key, rule code, rule version, severity, status, confidence, first seen, latest seen, affected node keys, evidence references, and safe metadata where available.

### 6.5 Stable Key Usage

All MCP responses shall use stable keys for repositories, solutions, snapshots, projects, symbols, nodes, edges, evidence, rules, findings, metrics, generated summaries, and diff records where such identities are returned. MCP responses shall not expose Neo4j internal node IDs or relationship IDs.

## 7. Technical Requirements

### 7.1 Architecture Boundaries

| ID | Requirement |
| --- | --- |
| TR-001 | The MCP host shall remain a host-layer project. |
| TR-002 | MCP host implementation shall depend on application/query abstractions and shared contracts according to Onion Architecture direction. |
| TR-003 | Domain projects shall not reference MCP host code. |
| TR-004 | Application services shall not depend on MCP transport implementation details. |
| TR-005 | Infrastructure projects shall not depend on MCP host code. |
| TR-006 | MCP handlers shall not bypass application/query abstractions to directly access Neo4j. |
| TR-007 | MCP handlers shall not directly inspect arbitrary files in analyzed repositories to answer architecture questions. |

### 7.2 Hosting and Configuration

| ID | Requirement |
| --- | --- |
| TR-008 | The MCP host shall use existing service defaults where applicable. |
| TR-009 | The MCP host shall configure registered tools, resources, and prompts at startup. |
| TR-010 | The MCP host shall fail startup or readiness when mandatory tool, resource, or prompt registration is incomplete. |
| TR-011 | MCP limits shall be configurable through the repository's configuration approach. |
| TR-012 | MCP authentication and authorization seams shall be configurable. |
| TR-013 | Tool allow-list configuration shall support disabling individual tools. |
| TR-014 | Resource allow-list configuration shall support disabling individual resource families where required. |

### 7.3 Validation

| ID | Requirement |
| --- | --- |
| TR-015 | Tool request contracts shall validate required stable keys, search text, depth values, snapshot identifiers, filters, and pagination inputs. |
| TR-016 | Depth inputs shall reject negative values and values greater than configured maximums. |
| TR-017 | Result-size inputs shall reject negative values and values greater than configured maximums. |
| TR-018 | Snapshot identifiers shall be validated before query execution. |
| TR-019 | Resource URI parameters shall be decoded and validated before query execution. |
| TR-020 | Validation failures shall be reported as structured MCP errors. |

### 7.4 Response Mapping

| ID | Requirement |
| --- | --- |
| TR-021 | Tool and resource handlers shall map query-layer DTOs into MCP response envelopes without losing evidence, confidence, unknowns, warnings, or truncation metadata. |
| TR-022 | Response mappers shall redact secrets before returning evidence snippets or metadata. |
| TR-023 | Response mappers shall preserve stable keys from query-layer responses. |
| TR-024 | Response mappers shall avoid adding unsupported natural-language claims beyond the structured data. |
| TR-025 | Suggested follow-ups shall reference only supported Archon MCP tools, supported resources, or safe user questions. |

### 7.5 Error Handling

| ID | Requirement |
| --- | --- |
| TR-026 | Unknown tool requests shall fail with a structured unsupported-tool error. |
| TR-027 | Unknown resource requests shall fail with a structured unsupported-resource error. |
| TR-028 | Ambiguous project or symbol requests shall fail with a structured ambiguity response including safe disambiguation candidates where possible. |
| TR-029 | Missing projects, symbols, snapshots, rules, findings, or diff inputs shall fail with structured not-found responses. |
| TR-030 | Query-layer failures shall be mapped to structured MCP errors without leaking internal stack traces. |
| TR-031 | Authorization failures shall be mapped to structured unauthorized or forbidden responses. |

## 8. Security Requirements

### 8.1 Forbidden Capabilities

The MCP server shall not provide any of these capabilities:

- Shell command execution.
- Arbitrary process execution.
- Arbitrary SQL execution.
- Arbitrary Cypher execution.
- Arbitrary graph query execution.
- Arbitrary filesystem read as an MCP feature.
- Filesystem mutation.
- Source-code mutation.
- Rule mutation.
- Finding mutation.
- Snapshot mutation.
- Database mutation.
- Secret retrieval.
- Credential export.
- Direct Neo4j browsing.
- Direct package restore, build, test, or deployment execution.

### 8.2 Secret and Sensitive Data Handling

| ID | Requirement |
| --- | --- |
| SR-001 | MCP responses shall not reveal connection string values. |
| SR-002 | MCP responses shall not reveal access tokens, API keys, passwords, certificates, private keys, or secrets. |
| SR-003 | MCP responses shall redact sensitive configuration values in evidence snippets. |
| SR-004 | MCP responses shall prefer configuration key names over configuration values. |
| SR-005 | MCP logs and telemetry shall not store raw sensitive evidence. |
| SR-006 | Secret redaction shall be tested with representative configuration, code, and evidence snippets. |

### 8.3 Prompt Injection Controls

| ID | Requirement |
| --- | --- |
| SR-007 | MCP output shall label extracted repository content as untrusted evidence. |
| SR-008 | MCP prompt templates shall instruct AI clients to ignore instructions embedded in analyzed source, comments, markdown, configuration, and evidence snippets. |
| SR-009 | MCP prompt templates shall instruct AI clients to treat Archon tool results as data, not as executable instructions. |
| SR-010 | MCP responses shall avoid combining untrusted evidence snippets with privileged instruction text in a way that could confuse downstream AI clients. |
| SR-011 | Prompt-injection test cases shall cover malicious source comments, markdown files, string literals, configuration values, and rule metadata. |

## 9. Testing Requirements

### 9.1 Tool Tests

| ID | Requirement |
| --- | --- |
| TEST-001 | Tests shall cover `archon.search`. |
| TEST-002 | Tests shall cover `archon.describe_project`. |
| TEST-003 | Tests shall cover `archon.get_dependencies`. |
| TEST-004 | Tests shall cover `archon.get_dependents`. |
| TEST-005 | Tests shall cover `archon.find_dependency_paths`. |
| TEST-006 | Tests shall cover `archon.describe_symbol`. |
| TEST-007 | Tests shall cover `archon.find_symbol_usages`. |
| TEST-008 | Tests shall cover `archon.get_data_access_usage`. |
| TEST-009 | Tests shall cover `archon.assess_change_impact`. |
| TEST-010 | Tests shall cover `archon.get_architecture_rules`. |
| TEST-011 | Tests shall cover `archon.get_hotlist_findings`. |
| TEST-012 | Tests shall cover `archon.get_snapshot_diff`. |
| TEST-013 | Tool tests shall cover successful responses, validation failures, not-found responses, ambiguous responses, unauthorized responses, query-layer failures, truncation, and unknown handling. |

### 9.2 Resource Tests

| ID | Requirement |
| --- | --- |
| TEST-014 | Tests shall cover `archon://snapshot/current`. |
| TEST-015 | Tests shall cover `archon://project/{projectKey}`. |
| TEST-016 | Tests shall cover `archon://symbol/{symbolKey}`. |
| TEST-017 | Tests shall cover `archon://rules/current`. |
| TEST-018 | Tests shall cover `archon://hotlist/current`. |
| TEST-019 | Tests shall cover `archon://hotspots/current`. |
| TEST-020 | Tests shall cover `archon://snapshot/{snapshotId}/diff/{previousSnapshotId}`. |
| TEST-021 | Resource tests shall cover malformed URIs, unknown resources, authorization failures, current snapshot ambiguity, not-found records, and response-size controls. |

### 9.3 Prompt Tests

| ID | Requirement |
| --- | --- |
| TEST-022 | Tests shall verify prompt `impact-analysis` exists and contains evidence-grounding and unknown-reporting instructions. |
| TEST-023 | Tests shall verify prompt `modernization-brief` exists and contains evidence-grounding and unknown-reporting instructions. |
| TEST-024 | Tests shall verify prompt `refactoring-preflight` exists and contains evidence-grounding and unknown-reporting instructions. |
| TEST-025 | Tests shall verify prompt `new-feature-placement` exists and contains evidence-grounding and unknown-reporting instructions. |
| TEST-026 | Tests shall verify prompt `legacy-data-access-review` exists and contains evidence-grounding and unknown-reporting instructions. |
| TEST-027 | Tests shall verify prompt `hotlist-summary` exists and contains evidence-grounding and unknown-reporting instructions. |
| TEST-028 | Tests shall verify prompt `architecture-rule-check` exists and contains evidence-grounding and unknown-reporting instructions. |
| TEST-029 | Prompt tests shall verify prompts do not instruct AI clients to mutate repositories, run shell commands, execute arbitrary database queries, or invent unsupported facts. |
| TEST-030 | Prompt tests shall verify prompt-injection resilience guidance is present. |

### 9.4 Security Tests

| ID | Requirement |
| --- | --- |
| TEST-031 | Tests shall verify MCP cannot execute shell commands. |
| TEST-032 | Tests shall verify MCP cannot execute arbitrary SQL. |
| TEST-033 | Tests shall verify MCP cannot execute arbitrary Cypher. |
| TEST-034 | Tests shall verify MCP cannot mutate files. |
| TEST-035 | Tests shall verify MCP cannot mutate source code. |
| TEST-036 | Tests shall verify MCP cannot mutate database state through MCP tools or resources. |
| TEST-037 | Tests shall verify MCP cannot mutate rules or findings. |
| TEST-038 | Tests shall verify tool allow-listing blocks disabled tools. |
| TEST-039 | Tests shall verify authorization is checked before query execution. |
| TEST-040 | Tests shall verify audit logging for tool calls and resource reads. |
| TEST-041 | Tests shall verify audit logs do not include secrets. |
| TEST-042 | Tests shall verify sensitive evidence redaction. |
| TEST-043 | Tests shall verify prompt-injection handling for malicious repository content. |

### 9.5 Contract and Integration Tests

| ID | Requirement |
| --- | --- |
| TEST-044 | Tests shall verify common response-envelope shape across tools. |
| TEST-045 | Tests shall verify common response-envelope shape across resources. |
| TEST-046 | Tests shall verify stable key usage and absence of Neo4j internal IDs. |
| TEST-047 | Tests shall verify evidence, confidence, findings, unknowns, warnings, and suggested follow-ups are preserved where available. |
| TEST-048 | Tests shall verify cancellation handling. |
| TEST-049 | Tests shall verify health and readiness behavior. |
| TEST-050 | Tests shall verify startup or readiness detects missing mandatory tool, resource, or prompt registration. |

## 10. Documentation Requirements

| ID | Requirement |
| --- | --- |
| DOC-001 | WP015 implementation shall update repository documentation to describe MCP server setup and usage. |
| DOC-002 | Documentation shall list every supported MCP tool, its purpose, inputs, outputs, limits, and safe follow-ups. |
| DOC-003 | Documentation shall list every supported MCP resource URI and its semantics. |
| DOC-004 | Documentation shall list every supported MCP prompt and intended workflow. |
| DOC-005 | Documentation shall describe read-only constraints and forbidden capabilities. |
| DOC-006 | Documentation shall describe authentication and authorization configuration seams. |
| DOC-007 | Documentation shall describe audit logging behavior. |
| DOC-008 | Documentation shall describe response-size controls, truncation, and suggested narrowing. |
| DOC-009 | Documentation shall describe prompt-injection-aware handling and untrusted evidence treatment. |
| DOC-010 | Documentation shall describe secret redaction expectations. |
| DOC-011 | Documentation shall include examples of evidence-backed tool responses. |
| DOC-012 | Documentation shall avoid creating a contributor guidance source that conflicts with the wiki; durable contributor guidance shall be placed in the appropriate wiki location when required by repository documentation workflow. |
| DOC-013 | Documentation and source documentation expectations shall apply to internal and other non-public types as well as public API surface where documentation-pass requirements apply. |

## 11. Acceptance Criteria

| ID | Acceptance Criterion |
| --- | --- |
| AC-001 | Every MCP tool listed in WP015 is implemented and tested. |
| AC-002 | Every MCP resource listed in WP015 is implemented and tested. |
| AC-003 | Every MCP prompt listed in WP015 is implemented and tested. |
| AC-004 | MCP responses include evidence, confidence, unknowns, findings where relevant, warnings, truncation metadata, and safe suggested follow-ups. |
| AC-005 | MCP cannot execute shell commands. |
| AC-006 | MCP cannot execute arbitrary SQL or arbitrary Cypher. |
| AC-007 | MCP cannot perform arbitrary graph queries outside supported tool/resource contracts. |
| AC-008 | MCP cannot mutate files, source code, graph data, rules, findings, snapshots, or external systems. |
| AC-009 | MCP uses application/query/API-layer abstractions and does not bypass them to query Neo4j directly. |
| AC-010 | MCP public responses use stable keys and do not expose Neo4j internal IDs. |
| AC-011 | Authentication and authorization seams are implemented and tested. |
| AC-012 | Tool allow-listing is implemented and tested. |
| AC-013 | Audit logging is implemented and tested without logging secrets. |
| AC-014 | Prompt-injection handling is implemented and tested. |
| AC-015 | Secret redaction is implemented and tested. |
| AC-016 | Health and readiness behavior is implemented and tested. |
| AC-017 | Documentation explains setup, usage, tools, resources, prompts, limits, security, and troubleshooting. |
| AC-018 | No Archon Discovery UI implementation, dashboard page, graph view, prompt panel, or front-end asset is introduced. |
| AC-019 | The solution builds after implementation. |
| AC-020 | Relevant tests for WP015 pass after implementation. |

## 12. Out of Scope

The following are out of scope for WP015:

- Archon Discovery UI implementation.
- Human-facing dashboard, explorer, graph view, prompt panel, evidence viewer, or hotlist viewer.
- New extraction domains.
- New Neo4j graph schema beyond what is required by earlier packages.
- Markdown export generation.
- Direct Neo4j query access by MCP clients.
- Arbitrary Cypher, SQL, shell, or filesystem access.
- Source-code modification.
- Automatic remediation.
- Rule editing through MCP.
- Finding suppression through MCP.
- Snapshot deletion or retention mutation through MCP.
- Repository registration, solution registration, extraction triggering, or management mutation through MCP unless a later explicit work package adds a separate secured write model.

## 13. Dependencies and Assumptions

### 13.1 Dependencies

- WP001 through WP014 are complete or provide the contracts needed by WP015.
- The Archon MCP host shell exists from WP001.
- Query and management capabilities from WP014 are available through application/query abstractions.
- The persisted graph includes stable keys, evidence, confidence, unknowns, findings, metrics, hotspots, and snapshot diff data from earlier work packages.
- Service defaults, health/readiness conventions, telemetry conventions, and host configuration patterns are available from earlier work packages.

### 13.2 Assumptions

- MCP clients consume Archon as a read-only architecture intelligence source.
- MCP tool names, resource URIs, and prompt names listed in WP015 are mandatory.
- Current snapshot selection is defined by existing query-layer behavior or must be explicitly added without creating ambiguous behavior.
- Authentication and authorization implementation can use seams if a production identity provider is not yet fixed by repository guidance.
- Response-size limits should be conservative by default because MCP responses are intended for AI context windows.

## 14. Risks and Mitigations

| Risk | Impact | Mitigation |
| --- | --- | --- |
| MCP server accidentally becomes a general automation surface. | Security boundary violation and unsafe AI capabilities. | Enforce strict read-only allow-listing, reject arbitrary command/query/mutation requests, and test forbidden capabilities. |
| Tool responses become too large for AI context windows. | Poor client behavior and expensive interactions. | Enforce result-size limits, truncation metadata, and suggested narrowing. |
| MCP responses omit evidence or unknowns. | AI clients may overstate confidence or invent details. | Use common response envelopes and tests that assert evidence, confidence, and unknown preservation. |
| Direct graph access leaks implementation details. | Coupling to Neo4j internals and possible data leakage. | Require application/query abstraction usage and stable-key-only public contracts. |
| Prompt injection in extracted repository content influences AI behavior. | AI client may follow malicious source comments or configuration text. | Label evidence as untrusted, include prompt-injection guidance, sanitize output, and test malicious content. |
| Secrets appear in evidence snippets. | Credential exposure. | Redact sensitive values, prefer key names and hashes, and test representative secret patterns. |
| Current snapshot selection is ambiguous. | Incorrect or misleading answers. | Return structured ambiguity errors and require explicit snapshot selection. |
| Tool and API behavior diverge. | MCP becomes a competing product contract. | Reuse query-layer DTOs and mapper tests, document MCP as a bounded projection of API/query capabilities. |

## 15. Open Questions

| ID | Question | Answer |
| --- | --- | --- |
| OQ-001 | Which MCP transport modes must be supported initially by the Archon MCP host? | Support the transport already established by `ArchonMcp` first, and design the host so additional transports can be added later without changing tool, resource, or prompt contracts. Do not expand WP015 to multiple transports unless the current host already supports them. |
| OQ-002 | What production authentication provider should be used for MCP clients? | Implement provider-neutral authentication and authorization seams with configuration-driven policy enforcement and test doubles. Do not hard-code a production provider in WP015; align with the API host's eventual production authentication model when that is selected. |
| OQ-003 | What default response-size limits should each tool use? | Use conservative configurable defaults per tool: search/result lists default to 25 items; dependency and dependent traversal default to depth 2 and a maximum of 100 nodes/edges; dependency paths default to 10 paths and maximum depth 5; evidence defaults to 3 items per fact or finding; findings and hotlist responses default to 50 findings; snapshot diff details default to 100 records while always returning summary counts; total serialized responses should stay within a configurable MCP context budget. |
| OQ-004 | Should MCP expose extraction-run status resources in addition to the listed WP015 resources? | No. Keep WP015 limited to the explicitly listed tools, resources, and prompts. Extraction-run history remains available through API management/query surfaces and should not be added as an MCP resource unless a later work package explicitly extends MCP operations. |
| OQ-005 | Should prompt text be stored as embedded resources, files, or code-defined templates? | Store prompts as versioned markdown or text resources included with the MCP project, loaded read-only at runtime, and covered by tests. This keeps prompts reviewable, packageable, and testable without allowing runtime mutation. Avoid database-backed or user-editable prompts for WP015. |

## 16. Traceability to WP015 Required Implementation

| WP015 Required Implementation Item | Specification Coverage |
| --- | --- |
| Implement read-only MCP server hosting. | Sections 3.1, 4.1, 4.17, 5, 7, 8, 9, and 11. |
| Implement all required MCP tools. | Sections 4.3 through 4.14 and 9.1. |
| Implement MCP resources. | Sections 4.15 and 9.2. |
| Implement MCP prompts. | Sections 4.16 and 9.3. |
| Enforce read-only behavior, authentication, authorization, audit logging, allow-listing, environment isolation, no secrets exposure, response-size limits, and prompt-injection-aware output handling. | Sections 4.17 through 4.20, 5, 8, 9.4, and 11. |
| Return evidence-backed responses with summary, confidence, facts, evidence, findings, unknowns, and suggested follow-ups. | Sections 4.2, 6, 7.4, 9.5, and 11. |
| Test every tool, resource, prompt, read-only constraint, authentication/authorization seam, audit logging, prompt-injection handling, and large-response truncation. | Section 9. |

## 17. Final Specification Statement

WP015 is complete when the Archon MCP server provides the full read-only tools, resources, prompts, response contracts, security controls, tests, and documentation described in this specification. The completed package must allow Copilot and other AI assistants to investigate Archon architecture knowledge using deterministic, evidence-backed, bounded responses while preventing arbitrary execution, mutation, secret exposure, prompt-injection escalation, and unsupported fact invention.
