# WP012 Specification - Rule Catalog, Rule Engine, Hotlist, and Findings

## Document Control

| Field | Value |
| --- | --- |
| Work Package | WP012 - Rule Catalog, Rule Engine, Hotlist, and Findings |
| Output Path | `docs/012-Rule-Catalog-Rule-Engine-Hotlist-and-Findings/spec-wp012-rule-catalog-rule-engine-hotlist-and-findings.md` |
| Source Work Package | `docs/foundation/work-packages.md` WP012 |
| Source Brief | `docs/foundation/archon_full_concept_brief.md` |
| Specification Basis | `spec-template_v1.1.md` requested; template file was not present in the workspace at creation time, so this document follows the repository specification structure and documented work-package rules. |
| Status | Draft |
| Audience | Product owner, architect, implementation team, test engineer |

## 1. Overview

### 1.1 Purpose

This specification defines the requirements for WP012, the Archon work package that introduces the disk-backed JSON rule catalog, schema-validated rule loading, boolean rule detection DSL, rule evaluation, suppressible findings, modernization hotlist output, and finding history across snapshots.

WP012 turns previously extracted architecture facts into evidence-backed modernization, risk, lifecycle, security, data-access, configuration, dependency, and architecture findings. It does not implement Archon's Discovery UI and does not use AI to invent findings.

### 1.2 Background

Archon is an API-first and MCP-first deterministic architecture intelligence platform for modern and legacy .NET estates. Earlier work packages establish the generalized graph model, snapshot orchestration, Neo4j persistence, project/package extraction, Roslyn semantic extraction, configuration and dependency-injection extraction, runtime extraction, data-access extraction, external integration extraction, and .NET UI-technology extraction.

WP012 builds on those persisted facts. It loads authored rules from repository content, validates those rules, persists a versioned runtime catalog, evaluates the rules against snapshot graph facts, and records findings with evidence, affected nodes, confidence, status, severity, suppression metadata, and history.

### 1.3 High-Level Scope

WP012 covers these capability areas:

- Repository-root `./rules` folder containing first-cut JSON rule definitions.
- JSON schema and semantic validation for rule files.
- Versioned rule authoring contract and disk-backed rule loading.
- Copy-to-output and copy-to-publish behavior for runtime rule loading.
- Boolean detection DSL with `all`, `any`, and `none` composition.
- Nested detection groups.
- Supported condition kinds and operators.
- Built-in rules for the source brief's currently identified detection scenarios.
- Runtime rule catalog persistence in Neo4j by rule code and version.
- Rule evaluation against extracted architecture graph facts.
- Findings with evidence links, node links, confidence, severity, status, suppression fields, first-seen data, latest-seen data, and metadata.
- Hotlist and rule catalog query APIs required by WP012.
- Tests and documentation for the rule catalog, rule engine, hotlist, and findings behavior.

WP012 excludes Archon Discovery UI, MCP tools/resources/prompts, markdown export, snapshot diff implementation, metrics calculation beyond consuming persisted metrics for `metric-threshold` rules, new extraction domains, automatic remediation, and organization-specific rule authoring UI.

## 2. System Context

### 2.1 Product Context

Archon accepts API-triggered extraction requests, extracts deterministic architecture facts into a snapshot, persists them in Neo4j, and exposes architecture knowledge through API and MCP surfaces. WP012 adds a post-extraction rule load and evaluation capability that transforms persisted or accumulated facts into findings and hotlist output.

The package must use the existing extraction orchestration and graph persistence seams. Rule evaluation must operate over deterministic graph facts, metrics, evidence, metadata, and unknown-state information. It must not scan source code independently when equivalent facts should already have been emitted by extractor slices.

### 2.2 Source References

WP012 must align with these source materials:

- `docs/foundation/work-packages.md` WP012 objective, required implementation, and completion criteria.
- `docs/foundation/archon_full_concept_brief.md` sections 25 through 27 for modernization hotlist, starter hotlist, and rule engine requirements.
- `docs/foundation/archon_full_concept_brief.md` section 34 for architecture rules and layering.
- `docs/foundation/archon_full_concept_brief.md` section 35 phase 6 for hotlist and findings, excluding Hotlist UI.
- `docs/foundation/archon_full_concept_brief.md` Appendix A for initial rule catalog format.
- `docs/foundation/archon_full_concept_brief.md` Appendix B for example rule pack.
- `docs/foundation/archon_full_concept_brief.md` Appendix E sections E.4.5, E.5.2.7, E.5.2.8, E.5.7, E.6.8, and E.7.5 for rule/finding model, disk-backed rules, loading, and acceptance criteria.
- `docs/foundation/work-packages.md` completion rules for evidence-backed facts, explicit unknowns, deterministic stable keys, Neo4j as the system of record, tests, documentation, and no Discovery UI.

### 2.3 Users and Stakeholders

| Stakeholder | Interest |
| --- | --- |
| Product owner | Confirms WP012 covers the mandatory rule, hotlist, and finding scope without UI delivery or deferred mandatory behavior. |
| Architect | Confirms the rule DSL, graph evaluation model, findings, and suppression model preserve deterministic evidence-backed architecture intelligence. |
| Developer | Uses findings and hotlist output to identify legacy, lifecycle, security, dependency, configuration, data-access, and architecture risks before making changes. |
| Test engineer | Verifies rule validation, DSL correctness, graph evaluation, finding persistence, suppression behavior, and historical tracking. |
| Future API consumer | Depends on persisted rules and findings for catalog, hotlist, and finding query APIs. |
| Future MCP consumer | Depends on evidence-backed findings for impact analysis and modernization prompts in later work packages. |
| Rule author | Reviews and evolves JSON rule definitions under `./rules` as versioned repository content. |

## 3. Component Summary

### 3.1 Authored Rule Catalog

The authored rule catalog is a repository-root `./rules` folder containing JSON rule-definition files. The folder is the source of truth for rule authoring and review. It contains first-cut built-in rule files for every currently identified detection scenario required by the source brief, including lifecycle, legacy technology, obsolete API, security-sensitive, configuration, dependency-risk, data-access, modernization blocker, and architecture-smell scenarios.

### 3.2 Rule Schema and Validation

The validation component parses rule JSON, validates it against the rule schema, validates required fields and enum values, verifies rule DSL shape, validates condition payloads, checks operator compatibility, and rejects invalid or ambiguous rules before loading. Validation failures are deterministic and actionable.

### 3.3 Disk Rule Loader

The disk rule loader resolves rules from copied runtime output content rather than hard-coded repository source paths. It loads rule files consistently for local development, tests, and published deployments. The same shared loader is used by API modules and any runtime component that needs the authored rule set.

### 3.4 Rule Catalog Persistence

The persistence integration upserts validated rules into Neo4j as global catalog nodes keyed by rule code and version. Persisted rules preserve definition JSON, enabled state, category, severity, default status, source URLs, built-in status, owner scope, metadata, and version identity. Historical rules remain available for findings created by older rule versions.

### 3.5 Rule Detection DSL

The rule detection DSL expresses graph predicates using `nodeKinds`, `match`, `conditions`, and nested `groups`. It supports required condition kinds and operators. The DSL must be deterministic, schema-validated, and stable for API, MCP, and future UI consumers.

### 3.6 Rule Evaluator

The evaluator applies enabled rules to snapshot graph facts, including nodes, edges, evidence, metadata, metrics, confidence, and unknown-state information. It emits findings only when the rule predicate is satisfied and evidence requirements can be met or explicitly represented as unknown context.

### 3.7 Finding Writer and History Tracker

The finding writer persists findings as snapshot-owned records linked to rules, affected nodes, and evidence. It tracks first-seen and latest-seen data across snapshots using deterministic stable keys and fingerprints. It preserves suppression fields and status without deleting historical findings.

### 3.8 Hotlist and Rule Query APIs

WP012 exposes the query APIs needed to retrieve the rule catalog, rule details, hotlist findings, finding details, suppression metadata, and finding history. These endpoints are the minimum product surface required by WP012 and must be shaped for later API and MCP consumption.

## 4. Functional Requirements

### 4.1 Rule Catalog Files

| ID | Requirement |
| --- | --- |
| FR-001 | WP012 shall create a repository-root `./rules` folder. |
| FR-002 | WP012 shall store first-cut JSON rule files under `./rules`. |
| FR-003 | Rule files shall be authored as JSON documents only. |
| FR-004 | Rule files shall be versioned repository content that can be reviewed and evolved independently of compiled code. |
| FR-005 | Each rule file shall contain one rule definition or a clearly defined rule-pack structure that is validated by the shared loader. |
| FR-006 | Rule files shall use stable rule codes that do not depend on file paths, database IDs, or load order. |
| FR-007 | Rule file names shall be deterministic and readable, using rule code and a short descriptor where practical. |
| FR-008 | Built-in rule files shall identify themselves as built-in rules. |
| FR-009 | Organization-specific rules shall be supported by the rule contract, but WP012 shall not require a UI for authoring them. |
| FR-010 | Rule removal from disk shall not destructively delete historical persisted rule or finding data. |

### 4.2 Rule Definition Contract

| ID | Requirement |
| --- | --- |
| FR-011 | Each rule shall define a stable rule code. |
| FR-012 | Each rule shall define a human-readable name. |
| FR-013 | Each rule shall define a category. |
| FR-014 | Each rule shall define a severity. |
| FR-015 | Each rule shall define a default status. |
| FR-016 | Each rule shall define an enabled flag. |
| FR-017 | Each rule shall define a version. |
| FR-018 | Each rule shall define a detection block. |
| FR-019 | Each rule shall define impact statements. |
| FR-020 | Each rule shall define evidence requirements. |
| FR-021 | Each rule may define source URLs. |
| FR-022 | Each rule may define recommended Archon actions. |
| FR-023 | Each rule may define owner scope, tags, and metadata. |
| FR-024 | A materially changed rule behavior shall require a new rule version. |
| FR-025 | Findings shall record the exact rule code and rule version used at evaluation time. |

### 4.3 Categories, Statuses, and Severities

| ID | Requirement |
| --- | --- |
| FR-026 | The rule catalog shall support `Lifecycle` category. |
| FR-027 | The rule catalog shall support `ObsoleteApi` category. |
| FR-028 | The rule catalog shall support `LegacyTechnology` category. |
| FR-029 | The rule catalog shall support `DataAccess` category. |
| FR-030 | The rule catalog shall support `SecuritySensitive` category. |
| FR-031 | The rule catalog shall support `Configuration` category. |
| FR-032 | The rule catalog shall support `ArchitectureLayering` category. |
| FR-033 | The rule catalog shall support `DependencyRisk` category. |
| FR-034 | The rule catalog shall support `ModernizationBlocker` category. |
| FR-035 | The rule catalog shall support `OrganisationSpecific` category. |
| FR-036 | The rule catalog shall support statuses `OutOfSupport`, `Obsolete`, `Legacy`, `FrameworkOnly`, `MigrationBlocker`, `SecuritySensitive`, `Discouraged`, and `Unknown`. |
| FR-037 | The rule catalog shall support severities `Critical`, `High`, `Medium`, `Low`, and `Info`. |
| FR-038 | Invalid category, status, or severity values shall fail validation before runtime evaluation. |

### 4.4 Rule Validation

| ID | Requirement |
| --- | --- |
| FR-039 | Rule loading shall parse JSON and report parse failures with file path and location where available. |
| FR-040 | Rule loading shall validate each rule against the JSON schema. |
| FR-041 | Rule loading shall validate required fields. |
| FR-042 | Rule loading shall validate enum fields. |
| FR-043 | Rule loading shall validate version format. |
| FR-044 | Rule loading shall validate unique rule code and version combinations within the loaded catalog. |
| FR-045 | Rule loading shall validate that detection groups are not empty. |
| FR-046 | Rule loading shall validate that condition kinds have the required payload properties. |
| FR-047 | Rule loading shall validate that metric-threshold conditions define metric, operator, and value. |
| FR-048 | Rule loading shall validate operator compatibility with condition payload types. |
| FR-049 | Rule loading shall validate node kind names against supported graph node kinds. |
| FR-050 | Rule loading shall reject rules that cannot be evaluated deterministically. |
| FR-051 | Rule loading shall return all validation errors for a file where practical instead of stopping at the first simple error. |
| FR-052 | Invalid rules shall prevent the affected rule from loading. |
| FR-053 | Invalid built-in rules shall fail startup or extraction initialization in a visible way rather than being silently ignored. |

### 4.5 Boolean Detection DSL

| ID | Requirement |
| --- | --- |
| FR-054 | The detection block shall support `nodeKinds`. |
| FR-055 | The detection block shall support `match: all`. |
| FR-056 | The detection block shall support `match: any`. |
| FR-057 | The detection block shall support `match: none`. |
| FR-058 | The detection block shall support `conditions`. |
| FR-059 | The detection block shall support nested `groups`. |
| FR-060 | When both `conditions` and `groups` are present, they shall be evaluated together as operands of the current `match` value. |
| FR-061 | `match: all` shall require every operand in the current group to evaluate true. |
| FR-062 | `match: any` shall require at least one operand in the current group to evaluate true. |
| FR-063 | `match: none` shall require no operand in the current group to evaluate true. |
| FR-064 | Nested groups shall use the same detection structure recursively. |
| FR-065 | Empty detection groups shall be invalid. |
| FR-066 | The DSL shall be stable and machine-readable for later API, UI, and MCP consumers. |
| FR-067 | The rule engine shall evaluate nested boolean groups exactly according to the source brief's section 27.5 semantics. |

### 4.6 Condition Kinds

| ID | Requirement |
| --- | --- |
| FR-068 | The DSL shall support `target-framework-membership` conditions. |
| FR-069 | The DSL shall support `namespace` conditions. |
| FR-070 | The DSL shall support `symbol` conditions. |
| FR-071 | The DSL shall support `package` conditions. |
| FR-072 | The DSL shall support `file-pattern` conditions. |
| FR-073 | The DSL shall support `method-call` conditions. |
| FR-074 | The DSL shall support `attribute` conditions. |
| FR-075 | The DSL shall support `metric-threshold` conditions. |
| FR-076 | Condition objects shall use explicit `kind` values rather than encoding operators in property names. |
| FR-077 | The rule engine may support additional condition kinds only when they are schema-validated and documented. |
| FR-078 | Unsupported condition kinds shall fail validation. |

### 4.7 Operators

| ID | Requirement |
| --- | --- |
| FR-079 | The DSL shall support `Equal`. |
| FR-080 | The DSL shall support `NotEqual`. |
| FR-081 | The DSL shall support `GreaterThan`. |
| FR-082 | The DSL shall support `GreaterThanOrEqual`. |
| FR-083 | The DSL shall support `LessThan`. |
| FR-084 | The DSL shall support `LessThanOrEqual`. |
| FR-085 | The DSL shall support `In`. |
| FR-086 | The DSL shall support `NotIn`. |
| FR-087 | The DSL shall support `Contains`. |
| FR-088 | The DSL shall support `StartsWith`. |
| FR-089 | The DSL shall support `EndsWith`. |
| FR-090 | The DSL shall support `MatchesPattern`. |
| FR-091 | Operators shall be explicit and shall not be encoded into metric names or property names. |
| FR-092 | Unsupported operators shall fail validation. |
| FR-093 | Operators shall evaluate deterministically using ordinal or documented normalized comparisons. |

### 4.8 Built-In First-Cut Rules

| ID | Requirement |
| --- | --- |
| FR-094 | WP012 shall include first-cut lifecycle rules for unsupported or retired target frameworks listed in the source brief. |
| FR-095 | WP012 shall include rules for `.NET Framework` versions earlier than 4.6.2 where required by the source brief. |
| FR-096 | WP012 shall include rules for `.NET Core` 1.x, 2.x, 3.0, and 3.1 lifecycle risks. |
| FR-097 | WP012 shall include rules for `.NET 5`, `.NET 6`, and `.NET 7` lifecycle risks as described by the source brief and current product policy. |
| FR-098 | WP012 shall include rules for .NET Standard-only libraries that can block migration. |
| FR-099 | WP012 shall include legacy application technology rules for ASP.NET Web Forms, ASP.NET Web Pages, classic ASP.NET MVC 3/4/5, ASP.NET Web API 2, and `System.Web`. |
| FR-100 | WP012 shall include legacy application technology rules for `Global.asax`, HTTP modules, HTTP handlers, WCF server applications, WCF clients, ASMX web services, Windows Workflow Foundation, .NET Remoting, Enterprise Services / COM+, ClickOnce deployment, classic Windows Services, Topshelf services, and OWIN/Katana startup. |
| FR-101 | WP012 shall include legacy data-access rules for LINQ to SQL, `.dbml` files, `System.Data.Linq.DataContext`, `Table<T>`, `SubmitChanges`, `ExecuteQuery`, and `ExecuteCommand`. |
| FR-102 | WP012 shall include data-access rules for typed DataSets, `DataSet`, `DataTable`, `TableAdapter`, ADO.NET `SqlCommand`, `SqlDataReader`, EF Classic / EF6, EF Core, `ObjectContext`, raw SQL construction, stored-procedure-heavy access, `OleDb`, and `Odbc`. |
| FR-103 | WP012 shall include obsolete API rules for the SYSLIB and EXTOBS scenarios identified by the source brief. |
| FR-104 | WP012 shall include security-sensitive rules for `BinaryFormatter`, `LosFormatter`, `NetDataContractSerializer`, `SoapFormatter`, `ObjectStateFormatter`, Code Access Security, `PrincipalPermissionAttribute`, Forms Authentication, `machineKey`, custom authentication, SHA1, MD5, DES, TripleDES, `RijndaelManaged`, hard-coded secrets, connection strings in config, and custom encryption. |
| FR-105 | WP012 shall include configuration and hosting rules for web.config-heavy applications, app.config-heavy applications, binding redirects, machine.config assumptions, `ConfigurationManager`, `packages.config`, non-SDK-style projects, `Global.asax`, OWIN Startup, IIS-only assumptions, Windows Registry configuration, hard-coded file paths, UNC paths, and environment-specific transforms. |
| FR-106 | WP012 shall include dependency and package pattern rules for the packages and namespaces listed by the source brief, including EntityFramework 6, Microsoft.AspNet.Mvc, Microsoft.AspNet.WebApi, System.Web packages, Unity, CommonServiceLocator, Enterprise Library, Castle Windsor, StructureMap, Ninject, log4net, old NLog, old RestSharp, old Newtonsoft.Json, and Topshelf. |
| FR-107 | WP012 shall include architecture-smell rules for high fan-in, high fan-out, shared libraries referenced by many apps, invalid layer references, data-access spread, shared table usage, circular project dependencies, large projects, god service classes, controller-heavy logic, static service locator, reflection-heavy call paths, and dynamic invocation where supporting facts or metrics exist. |
| FR-108 | Built-in rules shall use evidence requirements compatible with facts produced by prior work packages. |
| FR-109 | Built-in rules shall not require live application execution, external service calls, or direct source scanning outside the established graph facts. |

### 4.9 Runtime Rule Loading

| ID | Requirement |
| --- | --- |
| FR-110 | Runtime projects that need rules shall copy `./rules` content to build output. |
| FR-111 | Runtime projects that need rules shall copy `./rules` content to publish output. |
| FR-112 | The runtime loader shall load rules from copied output content. |
| FR-113 | The runtime loader shall not depend on hard-coded repository-relative source paths. |
| FR-114 | Local development, test execution, and published deployment shall use the same loading model. |
| FR-115 | API extraction, API management, and other runtime components shall share one rule-loading component. |
| FR-116 | Rule loading shall be part of the extraction pipeline before rule evaluation. |
| FR-117 | Rule loading shall be idempotent for unchanged rule code and version combinations. |
| FR-118 | Rule loading shall support disabled rules without evaluating them. |
| FR-119 | Rule loading shall expose diagnostics for missing rule folders, unreadable files, invalid files, duplicate rules, and unsupported DSL versions. |

### 4.10 Rule Catalog Persistence

| ID | Requirement |
| --- | --- |
| FR-120 | Validated rules shall be upserted into Neo4j rule catalog nodes. |
| FR-121 | Rule upsert identity shall be rule code plus version. |
| FR-122 | Persisted rules shall include rule code, name, category, severity, default status, enabled flag, version, description, definition JSON, source URLs, built-in status, owner scope, and metadata. |
| FR-123 | Persisted rules shall preserve enough data to explain historical findings after rule files change. |
| FR-124 | Loading a new version of an existing rule code shall not overwrite historical rule versions. |
| FR-125 | Disabling a rule shall not delete the rule from the persisted catalog. |
| FR-126 | Removing a rule from disk shall not require destructive deletion of historical rules or findings. |
| FR-127 | Persisted rules shall be queryable by code, version, category, severity, enabled status, built-in status, and owner scope. |

### 4.11 Rule Evaluation

| ID | Requirement |
| --- | --- |
| FR-128 | Rule evaluation shall run after extraction facts needed by WP012 are available. |
| FR-129 | Rule evaluation shall evaluate only enabled rules. |
| FR-130 | Rule evaluation shall restrict candidate nodes by `nodeKinds` before evaluating conditions. |
| FR-131 | Rule evaluation shall evaluate target-framework conditions against project target-framework facts and metadata. |
| FR-132 | Rule evaluation shall evaluate namespace conditions against namespace, type, method, property, field, and evidence facts where applicable. |
| FR-133 | Rule evaluation shall evaluate symbol conditions against extracted semantic symbol facts. |
| FR-134 | Rule evaluation shall evaluate package conditions against extracted package and package-reference facts. |
| FR-135 | Rule evaluation shall evaluate file-pattern conditions against extracted file-path and artifact facts. |
| FR-136 | Rule evaluation shall evaluate method-call conditions against extracted method call facts. |
| FR-137 | Rule evaluation shall evaluate attribute conditions against extracted attribute facts. |
| FR-138 | Rule evaluation shall evaluate metric-threshold conditions against persisted metric facts. |
| FR-139 | Rule evaluation shall support nested boolean composition exactly as validated. |
| FR-140 | Rule evaluation shall preserve unknown context when a condition cannot be fully evaluated because required graph facts are explicitly unknown. |
| FR-141 | Rule evaluation shall not invent facts to satisfy a rule. |
| FR-142 | Rule evaluation shall produce deterministic results for the same snapshot and rule catalog. |
| FR-143 | Rule evaluation shall use stable ordering for deterministic finding output. |
| FR-144 | Rule evaluation shall produce warnings when rule evaluation is partial due to missing upstream facts, disabled dependencies, or unknown data. |
| FR-145 | Rule evaluation shall continue evaluating independent rules when one rule fails due to rule-specific data issues, while surfacing the failure. |

### 4.12 Finding Creation

| ID | Requirement |
| --- | --- |
| FR-146 | A satisfied rule shall produce a finding unless an existing equivalent snapshot finding already exists. |
| FR-147 | Each finding shall include snapshot identity. |
| FR-148 | Each finding shall include deterministic stable key. |
| FR-149 | Each finding shall include rule code. |
| FR-150 | Each finding shall include rule version. |
| FR-151 | Each finding shall include severity. |
| FR-152 | Each finding shall include status. |
| FR-153 | Each finding shall include title. |
| FR-154 | Each finding shall include description. |
| FR-155 | Each finding shall include knowledge kind. |
| FR-156 | Each finding shall include confidence. |
| FR-157 | Each finding shall include primary node stable key where applicable. |
| FR-158 | Each finding shall include primary evidence where applicable. |
| FR-159 | Each finding shall include first-seen snapshot identity where known. |
| FR-160 | Each finding shall include latest-seen snapshot identity. |
| FR-161 | Each finding shall include suppression fields. |
| FR-162 | Each finding shall include metadata. |
| FR-163 | Each finding shall include fingerprint. |
| FR-164 | Each finding shall link to one or more affected architecture nodes where applicable. |
| FR-165 | Each finding shall link to one or more evidence records where applicable. |
| FR-166 | Finding severity shall default from the evaluated rule unless a deterministic rule payload explicitly overrides it. |
| FR-167 | Finding status shall default from the evaluated rule unless suppression or deterministic rule payload explicitly overrides it. |
| FR-168 | Finding confidence shall be derived from rule confidence, matched evidence confidence, matched fact confidence, and unknown-state context. |
| FR-169 | Findings shall not automatically prescribe code changes unless the rule is explicitly advisory. |

### 4.13 Finding History

| ID | Requirement |
| --- | --- |
| FR-170 | Finding stable keys shall be deterministic across snapshots for the same rule, affected target, and finding identity. |
| FR-171 | The system shall identify findings that persist across snapshots. |
| FR-172 | The system shall identify first-seen snapshot data for a finding. |
| FR-173 | The system shall update latest-seen snapshot data for a finding. |
| FR-174 | The system shall preserve historical finding records for each evaluated snapshot. |
| FR-175 | Finding fingerprints shall change when normalized finding content changes for the same stable key. |
| FR-176 | Historical fidelity shall be preserved when a rule version changes. |
| FR-177 | Finding history shall not be based on Neo4j internal IDs. |

### 4.14 Suppression Model

| ID | Requirement |
| --- | --- |
| FR-178 | Findings shall be suppressible. |
| FR-179 | Suppression shall support suppressed status. |
| FR-180 | Suppression shall record suppression reason. |
| FR-181 | Suppression shall record suppressed-by identity where supplied. |
| FR-182 | Suppression shall preserve rule code and version. |
| FR-183 | Suppression shall preserve affected node identity. |
| FR-184 | Suppression shall not delete the underlying finding. |
| FR-185 | Suppression shall be queryable. |
| FR-186 | Suppression behavior shall be deterministic across later snapshots where the same finding stable key remains applicable. |
| FR-187 | Suppression APIs or seams shall validate required fields and reject invalid suppression requests. |

### 4.15 Hotlist Output

| ID | Requirement |
| --- | --- |
| FR-188 | WP012 shall provide hotlist output over persisted findings. |
| FR-189 | Hotlist output shall be filterable by snapshot. |
| FR-190 | Hotlist output shall be filterable by category. |
| FR-191 | Hotlist output shall be filterable by severity. |
| FR-192 | Hotlist output shall be filterable by status. |
| FR-193 | Hotlist output shall be filterable by project or affected node where available. |
| FR-194 | Hotlist output shall include rule code and version. |
| FR-195 | Hotlist output shall include finding title and summary. |
| FR-196 | Hotlist output shall include severity, status, confidence, and category. |
| FR-197 | Hotlist output shall include affected node identity and display information where available. |
| FR-198 | Hotlist output shall include evidence references. |
| FR-199 | Hotlist output shall include unknown-state information where applicable. |
| FR-200 | Hotlist output shall support paging and deterministic ordering. |
| FR-201 | Hotlist output shall not expose secrets through evidence snippets or metadata. |

### 4.16 Rule Catalog and Finding APIs

| ID | Requirement |
| --- | --- |
| FR-202 | WP012 shall expose an API to list rule catalog entries. |
| FR-203 | WP012 shall expose an API to retrieve one rule by code and version. |
| FR-204 | WP012 shall expose an API to list hotlist findings. |
| FR-205 | WP012 shall expose an API to retrieve one finding by stable key or identifier. |
| FR-206 | WP012 shall expose an API to retrieve finding history where supported by persisted data. |
| FR-207 | WP012 shall expose an API or application seam to suppress a finding when suppression is in scope for the host security model. |
| FR-208 | APIs shall use stable DTO contracts suitable for later MCP consumption. |
| FR-209 | APIs shall support pagination and response-size limits. |
| FR-210 | APIs shall return evidence references rather than requiring consumers to query Neo4j directly. |
| FR-211 | APIs shall not expose arbitrary Cypher or unrestricted graph-query execution. |

### 4.17 Documentation

| ID | Requirement |
| --- | --- |
| FR-212 | WP012 shall document the rule file structure. |
| FR-213 | WP012 shall document the rule schema and validation behavior. |
| FR-214 | WP012 shall document the detection DSL. |
| FR-215 | WP012 shall document condition kinds and operators. |
| FR-216 | WP012 shall document built-in rule coverage. |
| FR-217 | WP012 shall document runtime rule loading from copied output content. |
| FR-218 | WP012 shall document rule versioning and historical fidelity expectations. |
| FR-219 | WP012 shall document finding creation, suppression, confidence, unknowns, and evidence behavior. |
| FR-220 | WP012 shall document hotlist and rule catalog API behavior. |
| FR-221 | Internal and non-public types introduced for WP012 shall be treated as requiring the same developer-level documentation standard as public types when documentation is necessary to understand architecture or behavior. |

## 5. Non-Functional Requirements

### 5.1 Determinism and Evidence

| ID | Requirement |
| --- | --- |
| NFR-001 | Given the same snapshot facts and same loaded rule catalog, WP012 shall produce deterministic findings. |
| NFR-002 | Rule, finding, and evidence stable keys shall not depend on database IDs, absolute developer machine paths, rule file enumeration order, runtime output folder names, or load timing. |
| NFR-003 | Every persisted finding shall link to evidence unless it is explicitly represented as an unknown or derived finding with documented rationale. |
| NFR-004 | Rule evaluation shall preserve confidence and unknown-state context from matched graph facts. |
| NFR-005 | Rule evaluation shall never use AI-generated assumptions as rule predicates. |

### 5.2 Security and Safety

| ID | Requirement |
| --- | --- |
| NFR-006 | The rule engine shall not execute arbitrary code from rule files. |
| NFR-007 | The rule engine shall not execute shell commands, arbitrary SQL, arbitrary Cypher, filesystem mutations, network calls, or application code as part of evaluation. |
| NFR-008 | Rule files shall be data-only JSON documents. |
| NFR-009 | Evidence snippets, metadata, warnings, errors, API responses, and logs shall not expose secret-like values. |
| NFR-010 | Hard-coded secret findings shall report the existence and location of suspicious values without storing the secret value itself. |
| NFR-011 | Pattern matching shall be bounded to avoid catastrophic regular expression behavior. |
| NFR-012 | APIs shall not expose unrestricted graph query capabilities. |

### 5.3 Performance and Scalability

| ID | Requirement |
| --- | --- |
| NFR-013 | Rule evaluation shall avoid scanning the full graph repeatedly for each condition when candidate sets or indexes can be reused. |
| NFR-014 | Rule evaluation shall use snapshot, node kind, rule category, and metric indexes where available. |
| NFR-015 | Rule loading and validation shall cache immutable parsed definitions for a single evaluation run where safe. |
| NFR-016 | Rule evaluation shall honor cancellation tokens from the orchestration path. |
| NFR-017 | Rule evaluation shall define safeguards for large snapshots, large rule catalogs, nested groups, broad file-pattern rules, and expensive pattern matching. |
| NFR-018 | Hotlist APIs shall support pagination, filtering, and deterministic ordering. |

### 5.4 Maintainability

| ID | Requirement |
| --- | --- |
| NFR-019 | C# code shall use block-scoped namespaces. |
| NFR-020 | C# code shall use Allman braces. |
| NFR-021 | C# files shall contain one public type per file. |
| NFR-022 | Private fields shall use underscore-prefixed naming. |
| NFR-023 | Executable entry points shall avoid top-level statements. |
| NFR-024 | `.csproj` files shall keep `PackageReference` entries in `ItemGroup` blocks that contain only package references. |
| NFR-025 | Rule DSL models shall be documented as stable contracts. |
| NFR-026 | Rule evaluation should be decomposed so individual condition evaluators are independently testable. |

### 5.5 Testability

| ID | Requirement |
| --- | --- |
| NFR-027 | Rule validation shall be testable without starting the Aspire AppHost. |
| NFR-028 | Rule evaluation shall be testable using in-memory or fixture-based graph facts. |
| NFR-029 | Neo4j persistence integration shall be testable through existing persistence seams or targeted integration tests. |
| NFR-030 | API behavior shall be testable without launching a blocking AppHost process. |
| NFR-031 | Built-in rules shall have fixture coverage proving they can match expected graph facts. |

## 6. Technical Requirements

### 6.1 Target Runtime and Project Placement

WP012 implementation shall use the repository-approved .NET target and the project layout created by WP001. The expected primary production responsibilities are:

| Project or Area | Responsibility |
| --- | --- |
| `./rules` | Authored JSON built-in rules and future organization-authored rule files. |
| `Archon.Application` | Rule contracts, finding contracts, hotlist DTOs, rule-loading abstractions, rule-evaluation ports, and application service interfaces. |
| `Archon.Domain` | Shared value objects and enum-backed strings for rule category, severity, status, condition kind, operator, finding status, confidence, knowledge kind, and stable identity where appropriate. |
| Rule engine service project established by WP001 | Rule schema validation, disk loading, DSL parsing, condition evaluation, boolean group evaluation, and finding construction. |
| `Archon.Infrastructure.Neo4j` | Rule catalog upsert, finding persistence, finding history lookup, suppression persistence, and hotlist query implementation through persistence ports. |
| `Archon.Api.Extraction` | Extraction-pipeline integration for rule loading, catalog upsert, rule evaluation, and finding persistence. |
| `Archon.Api.Query` or equivalent API module | Rule catalog, hotlist, finding detail, finding history, and suppression endpoints required by WP012. |

Expected corresponding test responsibilities are:

| Test Area | Responsibility |
| --- | --- |
| Rule contract tests | Enum/string serialization, JSON contract stability, schema validation, invalid rule diagnostics, and version handling. |
| Rule loader tests | Disk loading, copied-output path resolution, duplicate detection, disabled rules, invalid files, and missing folder diagnostics. |
| DSL tests | `all`, `any`, `none`, nested groups, condition evaluation, operator behavior, invalid payloads, and deterministic ordering. |
| Built-in rule tests | First-cut rule files validate and match representative graph facts. |
| Finding tests | Stable keys, fingerprints, evidence links, node links, confidence, unknowns, suppression fields, and first/latest seen behavior. |
| Persistence tests | Rule upsert, finding persistence, finding history, suppression persistence, and hotlist query behavior. |
| API tests | Rule catalog, hotlist, finding detail, finding history, filtering, pagination, and error responses. |

If WP001 uses different concrete project names, WP012 shall use the existing projects rather than creating duplicate responsibilities.

### 6.2 Dependency Direction

WP012 must preserve Onion Architecture dependency direction:

- Domain must not depend on application, infrastructure, API modules, extractors, or hosts.
- Application may define rule, finding, and hotlist contracts and ports but must not depend on infrastructure or hosts.
- Rule engine implementation may depend on application contracts and domain value objects according to existing solution direction.
- Infrastructure implements persistence ports and must not be referenced by domain or application.
- API modules may compose application services but must not contain core rule-evaluation logic.
- Hosts perform composition only and must not absorb rule-engine implementation behavior.

### 6.3 Rule File Contract

The rule file contract shall include these logical fields:

| Field | Requirement |
| --- | --- |
| `id` or `ruleCode` | Stable rule code used for identity and finding references. |
| `name` | Human-readable rule name. |
| `category` | One supported hotlist category. |
| `severity` | One supported severity. |
| `status` or `defaultStatus` | Default status for produced findings. |
| `enabled` | Whether the rule is evaluated by default. |
| `version` | Version of the rule behavior. |
| `description` | Optional explanatory text. |
| `detect` | Required detection DSL block. |
| `impact` | User-facing impact statements. |
| `evidenceRequired` | Evidence kinds or evidence sources expected for findings. |
| `source` or `sourceUrls` | Optional authoritative URLs or source references. |
| `recommendedArchonAction` | Optional investigation or reporting hints. |
| `metadata` | Optional JSON object for future-compatible rule metadata. |

### 6.4 Detection Evaluation Model

Rule evaluation shall proceed in this order:

1. Load and validate rule files from runtime disk content.
2. Upsert loaded rules into the Neo4j rule catalog.
3. Select enabled rules for the current evaluation run.
4. Select candidate nodes for each rule by snapshot and node kind.
5. Evaluate leaf conditions against graph facts, metadata, evidence, metrics, and relationships.
6. Evaluate nested groups according to the `match` operator.
7. Construct finding candidates for satisfied rules.
8. Attach affected nodes, evidence, confidence, unknowns, and metadata.
9. Deduplicate equivalent findings within the snapshot.
10. Resolve first-seen and latest-seen history.
11. Persist findings and supporting relationships.
12. Make hotlist and finding query APIs reflect persisted results.

### 6.5 Stable Key Requirements

Stable keys shall be deterministic:

| Entity | Stable Key Basis |
| --- | --- |
| Rule | Rule code and version. |
| Finding | Snapshot-independent finding identity where possible, using rule code, rule version or version policy, affected node stable key, matched condition identity, and normalized target identity. |
| Finding evidence relationship | Finding stable key plus evidence stable key. |
| Finding node relationship | Finding stable key plus affected node stable key. |

Finding stable-key strategy shall support history across snapshots. If rule behavior changes materially, the new rule version may intentionally produce distinct finding identity where needed for historical fidelity.

### 6.6 Metadata Requirements

Rule metadata and finding metadata shall use stable API-friendly lower camel case names. Finding metadata may include:

- `category`
- `ruleCode`
- `ruleVersion`
- `matchedConditionKinds`
- `matchedNodeKind`
- `matchedNodeStableKey`
- `matchedMetricName`
- `matchedMetricValue`
- `matchedPackageName`
- `matchedSymbolName`
- `matchedNamespace`
- `matchedFilePattern`
- `matchedMethodCall`
- `matchedAttribute`
- `targetFramework`
- `confidenceReason`
- `unknownReason`
- `suggestedInvestigation`
- `sourceUrls`
- `suppressionScope`

Metadata shall not duplicate first-class properties unless duplication is necessary for response shaping and remains consistent.

### 6.7 Documentation Pass

WP012 shall include a documentation pass covering:

- Rule catalog folder structure.
- Rule JSON contract.
- Rule schema validation.
- DSL boolean semantics.
- Condition kinds and operators.
- Built-in rule categories and examples.
- Disk loading from copied runtime output content.
- Rule persistence and versioning behavior.
- Finding creation, confidence, unknowns, evidence, and suppression behavior.
- Hotlist and rule catalog API behavior.
- Test fixture guidance for rule authors and maintainers.

Internal and non-public implementation types introduced for WP012 shall be treated as requiring the same developer-level documentation standard as public types when documentation is necessary to understand architecture or behavior.

## 7. Exclusions

WP012 shall not implement:

- Archon Discovery UI, Hotlist UI, dashboards, explorer pages, graph pages, prompt panels, or front-end assets.
- MCP tools, resources, prompts, or Copilot workflows.
- Markdown export.
- Snapshot diff implementation.
- New extraction domains beyond consuming facts emitted by earlier extractors.
- Live application execution.
- Automatic remediation, code rewriting, migration execution, or refactoring.
- AI-authored findings without deterministic rule and evidence support.
- Arbitrary script execution from rule definitions.
- Arbitrary Cypher, SQL, shell, HTTP, or filesystem-mutation actions from rules.
- Organization-specific rule authoring UI.
- A complete enterprise governance workflow for rule approvals beyond versioned repository content.

## 8. Data and Integration Requirements

### 8.1 Required Rule Data

| Data Element | Required Treatment |
| --- | --- |
| Rule code | Stable identifier for catalog and finding references. |
| Rule version | Historical fidelity boundary for rule behavior. |
| Category | Canonical hotlist category. |
| Severity | Canonical severity used for prioritization. |
| Default status | Default finding status produced by the rule. |
| Enabled state | Determines whether the rule is evaluated. |
| Definition JSON | Persisted exact loaded rule definition. |
| Source URLs | Persisted authoritative references where supplied. |
| Built-in flag | Distinguishes repository-provided built-in rules from organization-defined rules. |
| Owner scope | Supports future ownership filtering. |
| Metadata | Extensible JSON payload for rule-specific details. |

### 8.2 Required Finding Data

| Data Element | Required Treatment |
| --- | --- |
| Snapshot identity | Findings are snapshot-owned. |
| Stable key | Deterministic identity for deduplication and history. |
| Rule code and version | Exact evaluated rule identity. |
| Severity and status | Queryable first-class values. |
| Title and description | Human-readable explanation. |
| Knowledge kind | Fact, inference, unknown, or later human-confirmed classification. |
| Confidence | Queryable confidence value. |
| Primary node | Main affected architecture node where applicable. |
| Primary evidence | Main evidence record where applicable. |
| First seen | Snapshot identity where the finding first appeared. |
| Latest seen | Latest snapshot identity where the finding appeared. |
| Suppression fields | Reason and actor data where suppressed. |
| Metadata | Rule-specific and matched-condition context. |
| Fingerprint | Normalized content identity for change detection. |

### 8.3 Required Relationships

| Relationship | Required Treatment |
| --- | --- |
| Rule to finding | Findings must reference the exact rule code and version and should link to persisted rule catalog data where supported by the model. |
| Finding to architecture node | Findings must link to affected nodes where applicable. |
| Finding to evidence | Findings must link to evidence records where applicable. |
| Finding to snapshot | Findings must be snapshot-scoped. |
| Finding history | Findings must preserve first-seen and latest-seen relationships or fields sufficient for history queries. |

### 8.4 Integration with Earlier Work Packages

WP012 shall consume facts from earlier work packages as follows:

- Use project, package, target-framework, project-format, and file-path facts from WP005.
- Use namespace, symbol, method-call, attribute, compiler diagnostic, and source evidence facts from WP006.
- Use configuration and dependency-injection facts from WP007.
- Use runtime, endpoint, controller, worker, hosted-service, queue, and topic facts from WP008.
- Use data-access facts from WP009.
- Use external integration facts from WP010.
- Use .NET UI-technology facts from WP011 where relevant to legacy UI and architecture-smell rules.
- Use persisted metrics when evaluating `metric-threshold` rules, recognizing that broader metric calculation is completed in the later metrics work package.

### 8.5 Integration with Later Work Packages

WP012 output shall be shaped so later work packages can:

- Calculate and use additional metrics and hotspots for advanced architecture rules.
- Include findings in snapshot diff.
- Expose hotlist and rule findings through the complete query API product surface.
- Expose findings through MCP tools, resources, and prompts.
- Include modernization hotlist sections in generated markdown export.
- Support operational hardening, retention, audit logging, and response-size controls.

## 9. Test Requirements

### 9.1 Required Test Coverage

| Test Area | Required Verification |
| --- | --- |
| Rule file discovery | Rules are discovered from copied output content, not hard-coded repository paths. |
| JSON parsing | Valid and invalid JSON files produce deterministic results and diagnostics. |
| Schema validation | Required fields, enum values, DSL shape, condition payloads, operator compatibility, and version fields are validated. |
| Duplicate detection | Duplicate rule code and version combinations are rejected. |
| Boolean DSL | `all`, `any`, `none`, mixed conditions and groups, and nested groups evaluate correctly. |
| Condition kinds | Target framework, namespace, symbol, package, file pattern, method call, attribute, and metric threshold conditions evaluate correctly. |
| Operators | Equal, NotEqual, GreaterThan, GreaterThanOrEqual, LessThan, LessThanOrEqual, In, NotIn, Contains, StartsWith, EndsWith, and MatchesPattern evaluate correctly. |
| Built-in rules | First-cut rule files validate and match representative graph facts. |
| Disabled rules | Disabled rules load and persist but do not produce findings. |
| Rule persistence | Rule catalog upsert uses rule code and version and preserves definition JSON. |
| Rule versioning | New rule versions coexist with older persisted versions. |
| Finding creation | Satisfied rules produce findings with rule identity, severity, status, confidence, evidence, node links, metadata, stable keys, and fingerprints. |
| Finding deduplication | Equivalent findings within a snapshot are not duplicated. |
| Finding history | First-seen and latest-seen data are populated across snapshots. |
| Suppression | Suppression fields are persisted, queryable, and do not delete findings. |
| Unknown handling | Unknown graph facts produce explicit unknown context rather than invented findings. |
| Hotlist APIs | Filtering, paging, deterministic ordering, and evidence references work. |
| Rule APIs | Catalog listing, detail retrieval, filtering, and error responses work. |
| Security | Rule evaluation does not execute arbitrary code or expose secret-like values. |
| Documentation | Rule authoring, validation, evaluation, finding, suppression, and API documentation is present. |

### 9.2 Built-In Rule Fixtures

Tests shall include fixture graph facts for:

- Unsupported target frameworks.
- Legacy ASP.NET and `System.Web` applications.
- WCF, ASMX, Windows Workflow Foundation, .NET Remoting, Enterprise Services / COM+, ClickOnce, classic Windows Services, Topshelf, and OWIN/Katana startup.
- LINQ to SQL, DBML, typed DataSets, ADO.NET, EF6, EF Core, raw SQL, OleDb, and Odbc.
- Obsolete SYSLIB and EXTOBS API usage covered by first-cut rules.
- BinaryFormatter and other security-sensitive serializer or cryptography usage.
- Configuration-heavy applications, binding redirects, `packages.config`, non-SDK-style projects, registry configuration, hard-coded file paths, UNC paths, and transforms.
- Legacy dependency and package patterns from the source brief.
- Architecture-smell examples, including high fan-in, high fan-out, invalid layer references, circular dependencies, service locator, reflection-heavy call paths, and dynamic invocation where supporting facts exist.

### 9.3 Test Constraints

Automated verification must not start the Aspire AppHost as a blocking process. Tests should use rule-loader fixtures, in-memory rule definitions, fixture graph facts, application-layer seams, persistence seams, and targeted API tests. For this work package, the full test suite should not be run unless explicitly requested; run targeted WP012 tests and a solution build as final validation.

## 10. Acceptance Criteria

WP012 is accepted when all of the following are true:

1. A repository-root `./rules` folder exists.
2. First-cut JSON rule files exist for every currently identified legacy, lifecycle, obsolete API, security-sensitive, data-access, configuration, dependency-risk, modernization blocker, and architecture-smell scenario required by the source brief.
3. Rule files are schema-validated before loading.
4. Invalid rule files produce deterministic validation diagnostics and are not silently ignored.
5. The rule DSL supports `match: all`, `match: any`, `match: none`, `conditions`, and nested `groups`.
6. Nested boolean groups work exactly as specified by the source brief's rule engine requirements.
7. The required condition kinds are implemented and tested.
8. The required operators are implemented and tested.
9. Runtime rule loading resolves from copied output content rather than hard-coded repository source paths.
10. Loaded rules are upserted into Neo4j by rule code and version.
11. Rule evaluation runs after extraction and produces deterministic findings.
12. Findings link to rules, nodes, evidence, snapshots, confidence, and suggested investigation context.
13. Findings include suppression fields and support suppression behavior.
14. Finding first-seen and latest-seen history is tracked across snapshots.
15. Hotlist and rule catalog query APIs are exposed for WP012 needs.
16. Tests cover rule parsing, schema validation, invalid rules, DSL evaluation, nested groups, metric thresholds, built-in rules, disk loading, Neo4j upsert, finding persistence, finding history, hotlist output, query APIs, and suppression fields.
17. Documentation is updated for rule authoring, validation, loading, evaluation, findings, suppression, hotlist output, and APIs.
18. No Archon Discovery UI implementation is introduced.
19. The solution builds successfully.
20. Targeted WP012 tests pass.

## 11. Risks and Decisions

### 11.1 Risks

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Rule DSL becomes too permissive. | Rules could become nondeterministic or unsafe. | Keep rules as data-only JSON, schema-validate condition kinds and operators, and forbid arbitrary code execution. |
| Built-in rule scope is broad. | First implementation may miss required source-brief scenarios. | Maintain traceability from each source-brief scenario to a rule file and test fixture. |
| Rule versions are mutated in place. | Historical findings become misleading. | Require materially changed rule behavior to use a new version and stamp findings with rule code/version. |
| Dynamic architecture facts are incomplete. | Rules might overstate risk or produce false positives. | Preserve confidence, unknown reason, and evidence; avoid findings when the predicate cannot be deterministically satisfied. |
| Regex or pattern rules are expensive. | Large snapshots could evaluate slowly. | Bound pattern matching, use candidate filtering, and test broad rules against large fixtures. |
| Secret-detection rules could expose secrets. | Findings could leak sensitive values. | Report existence and location only; redact evidence and metadata. |
| Runtime path differences cause rule loading drift. | Tests and deployments could load different rule sets. | Load only from copied runtime output content through one shared loader. |
| Suppression behavior may be ambiguous across rule versions. | A suppression might hide a materially changed finding. | Scope suppression to deterministic finding stable keys and preserve rule version policy in metadata. |

### 11.2 Decisions

| Decision | Rationale |
| --- | --- |
| Use a single WP012 specification document. | User requested a single markdown document spec for WP012. |
| Create the documentation under `docs/012-Rule-Catalog-Rule-Engine-Hotlist-and-Findings/`. | This is the next incremental documentation work-package folder after WP011. |
| Do not create separate overview and component spec documents. | The user explicitly requested a single markdown document, overriding the multi-document collaboration pattern for this output. |
| Use repository-root `./rules` as the authored rule source. | The source brief identifies disk-backed rule-definition files under `./rules` as the authoritative authored form. |
| Load rules from copied runtime output content. | The source brief requires runtime loading to avoid repository-relative path assumptions and maintain parity across local, test, and published execution. |
| Key persisted rule identity by rule code and version. | This preserves historical fidelity and supports multiple versions of a rule. |
| Keep rule files data-only JSON. | This keeps evaluation deterministic and prevents arbitrary code execution. |
| Represent dynamic or incomplete graph knowledge through confidence and unknowns. | The source brief requires unknowns to be explicit rather than silently omitted or invented. |
| Expose only controlled rule and finding APIs in WP012. | Full query API product surface is a later work package, but WP012 requires hotlist and rule catalog query APIs. |

## 12. Manual Verification Requirements

The implementation documentation for WP012 shall instruct a developer to verify the work package by:

1. Restoring and building the solution.
2. Confirming the repository-root `./rules` folder exists.
3. Confirming first-cut built-in rule files exist for every required source-brief scenario.
4. Running targeted rule schema validation tests.
5. Running targeted invalid-rule diagnostics tests.
6. Running targeted rule-loader tests that load from copied output content.
7. Running targeted boolean DSL tests for `all`, `any`, `none`, and nested groups.
8. Running targeted condition-kind tests for target framework, namespace, symbol, package, file pattern, method call, attribute, and metric threshold conditions.
9. Running targeted operator tests for every required operator.
10. Running targeted built-in rule tests against representative fixture facts.
11. Running targeted rule catalog persistence tests.
12. Running targeted finding creation and persistence tests.
13. Running targeted finding history tests across snapshots.
14. Running targeted suppression tests.
15. Running targeted hotlist and rule catalog API tests.
16. Inspecting representative finding output to confirm rule code, rule version, severity, status, confidence, affected nodes, evidence, unknowns, and suppression fields are present.
17. Confirming secret-like values are redacted from evidence, metadata, warnings, errors, logs, and API output.
18. Confirming no Archon Discovery UI resource, page, component, front-end asset, dashboard, explorer, graph page, or prompt panel was created.

Automated validation instructions shall explicitly state not to run the AppHost as a blocking process during agent-driven verification.

## 13. Traceability Matrix

| Source Requirement | Specification Coverage |
| --- | --- |
| Modernization hotlist categories, statuses, and severities | Sections 3, 4.3, 4.8, 4.15, 8, 9, 10 |
| Starter target framework hotlist | Sections 4.8, 9.2, 10 |
| Legacy application technology rules | Sections 4.8, 9.2, 10 |
| Legacy data-access rules | Sections 4.8, 9.2, 10 |
| Obsolete API rules | Sections 4.8, 9.2, 10 |
| Security-sensitive rules | Sections 4.8, 5.2, 9.2, 10 |
| Configuration and hosting constraints | Sections 4.8, 9.2, 10 |
| Dependency and package risk patterns | Sections 4.8, 9.2, 10 |
| Architecture smells and layering rules | Sections 4.8, 8.4, 9.2, 10 |
| JSON rule authoring format | Sections 3.1, 4.1, 4.2, 6.3 |
| Schema validation | Sections 3.2, 4.4, 9, 10 |
| Boolean DSL with nested groups | Sections 3.5, 4.5, 6.4, 9, 10 |
| Supported condition kinds | Sections 4.6, 9, 10 |
| Supported operators | Sections 4.7, 9, 10 |
| Disk-backed rule loading from copied output content | Sections 3.3, 4.9, 6.4, 9, 10 |
| Neo4j rule catalog persistence by code and version | Sections 3.4, 4.10, 8.1, 9, 10 |
| Findings linked to rules, nodes, evidence, snapshots, confidence, and suggested investigation context | Sections 3.7, 4.12, 8.2, 8.3, 10 |
| Finding history across snapshots | Sections 4.13, 8.2, 9, 10 |
| Suppression fields | Sections 4.14, 8.2, 9, 10 |
| Hotlist and rule catalog query APIs | Sections 3.8, 4.15, 4.16, 9, 10 |
| Explicit unknowns and evidence-first behavior | Sections 4.11, 4.12, 5.1, 8, 10 |
| No Archon Discovery UI implementation | Sections 1.3, 7, 10, 12 |

## 14. Open Questions

No open questions remain for WP012. Rule folder location, single-document output, disk-backed rule source, runtime loading model, DSL shape, required condition kinds, required operators, built-in rule coverage, finding persistence model, suppression expectations, and no-Discovery-UI scope are recorded as definitive decisions in section 11.2.

## 15. Change Log

| Date | Change |
| --- | --- |
| 2026-05-23 | Created initial single-document WP012 specification from `docs/foundation/work-packages.md` and the Archon source brief. |
