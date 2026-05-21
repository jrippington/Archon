# WP006 Specification - Roslyn Semantic Extraction for C# and VB.NET

## Document Control

| Field | Value |
| --- | --- |
| Work Package | WP006 - Roslyn Semantic Extraction for C# and VB.NET |
| Output Path | `docs/006-Roslyn-Semantic-Extraction/spec-wp006-roslyn-semantic-extraction.md` |
| Source Work Package | `docs/foundation/work-packages.md` WP006 |
| Source Brief | `docs/foundation/archon_full_concept_brief.md` |
| Specification Basis | `spec-template_v1.1.md` requested; template file was not present in the workspace at creation time, so this document follows the repository specification structure and documented work-package rules. |
| Status | Draft |
| Audience | Product owner, architect, implementation team, test engineer |

## 1. Overview

### 1.1 Purpose

This specification defines WP006, the work package that adds compiler-grade semantic extraction for C# and VB.NET source code to Archon. The package turns Roslyn syntax trees, semantic models, compilations, symbols, diagnostics, and source spans into deterministic, evidence-backed architecture facts for the Archon graph.

WP006 builds on the repository, solution, project, and package extraction foundation from earlier work packages. Its role is to move Archon from project-level inventory into symbol-level architecture intelligence across modern and legacy .NET codebases.

### 1.2 Background

Archon is a deterministic architecture intelligence platform for large .NET estates. Its central principle is that architecture facts must be extracted from code and supporting artifacts, persisted with evidence, and exposed through API and MCP surfaces without AI inventing missing knowledge.

WP006 is the first full semantic source-code extraction package. It must support both C# and VB.NET because the source brief treats legacy and modern .NET as first-class, including mixed-language solutions and older application styles that still contain valuable architecture information.

### 1.3 High-Level Scope

WP006 covers semantic extraction for source declarations, symbol relationships, dependencies, diagnostics, evidence spans, confidence classification, and explicit unknown handling. It does not implement configuration, dependency injection, ASP.NET runtime, data access, external integration, UI technology, hotlist, metrics, markdown export, or MCP tool behavior; those capabilities are assigned to later work packages in the sequence.

## 2. System Context

### 2.1 Product Context

Archon accepts repository and solution inputs, extracts deterministic architecture facts, persists those facts in Neo4j, and exposes them through API and MCP surfaces. WP006 provides the source-code semantic layer needed for later dependency, runtime, configuration, data-access, rule, impact-analysis, and evidence workflows.

The package must preserve Archon's evidence-first model. Every persisted semantic statement must be traceable to compiler symbols, source files, line spans, or diagnostics unless the statement is explicitly represented as unknown with confidence and unknown-reason data.

### 2.2 Source References

WP006 must align with these source materials:

- `docs/foundation/work-packages.md` WP006 objective, required implementation, and completion criteria.
- `docs/foundation/archon_full_concept_brief.md` section 6.1 for Roslyn references.
- `docs/foundation/archon_full_concept_brief.md` section 7 for Roslyn syntax trees, semantic models, symbols, compilations, and projection into the Architecture Semantic Graph.
- `docs/foundation/archon_full_concept_brief.md` section 12 for graph concepts, node kinds, edge kinds, evidence, confidence, classification, and unknowns.
- `docs/foundation/archon_full_concept_brief.md` section 15 for C# and VB.NET requirements.
- `docs/foundation/archon_full_concept_brief.md` sections 17.2 through 17.4 for type-level and method-level dependencies and confidence.
- `docs/foundation/archon_full_concept_brief.md` section 35 phase 2 and section 36 Roslyn Symbols epic.
- `docs/foundation/archon_full_concept_brief.md` Appendix E sections E.4.1, E.7.2, and E.9 for semantic extraction coverage and acceptance.

### 2.3 Users and Stakeholders

| Stakeholder | Interest |
| --- | --- |
| Product owner | Confirms that symbol-level extraction satisfies WP006 and supports later API/MCP capabilities. |
| Architect | Confirms Roslyn extraction preserves Onion Architecture and deterministic graph semantics. |
| Developer | Implements and maintains Roslyn extraction services across C# and VB.NET. |
| Test engineer | Verifies language parity, evidence, diagnostics, unknown handling, and confidence behavior. |
| API and MCP consumer | Depends on accurate symbol-level facts for later traversal, impact, evidence, and explanation workflows. |

## 3. Component Summary

### 3.1 Roslyn Shared Abstractions

`Archon.Roslyn` contains language-agnostic contracts, extraction context models, semantic projection helpers, symbol identity helpers, evidence builders, confidence classifiers, and shared traversal utilities.

### 3.2 C# Semantic Extraction

`Archon.Roslyn.CSharp` contains C#-specific syntax and semantic extraction for namespaces, types, members, attributes, inheritance, interface implementation, constructor dependencies, object creation, member access, invocations, diagnostics, and source evidence.

### 3.3 VB.NET Semantic Extraction

`Archon.Roslyn.VisualBasic` contains VB.NET-specific syntax and semantic extraction with parity to the C# slice where Roslyn supports it. It must account for VB.NET syntax differences while projecting architecture facts into the same graph model.

### 3.4 Legacy Roslyn Interpretation

`Archon.Roslyn.Legacy` contains heuristics and shared handling for generated code, legacy language patterns, unresolved symbols, partial types, older project conventions, and confidence classification where semantic certainty is reduced.

### 3.5 Infrastructure Roslyn Adapter

`Archon.Infrastructure.Roslyn` provides the outer Roslyn adapter responsible for workspace loading, compilations, documents, metadata references, and compiler diagnostics. It must depend inward on application and Roslyn abstractions rather than placing extraction logic in hosts.

### 3.6 Extraction Orchestration

`Archon.Api.Extraction` coordinates semantic extraction as part of the shared extraction path. It receives repository and solution context from earlier work packages, invokes Roslyn extraction services, accumulates architecture facts, and hands facts to graph persistence seams.

### 3.7 Graph Persistence Seams

WP006 produces graph-ready semantic facts for Neo4j persistence. It must use existing application-layer ports or accumulation contracts rather than embedding Neo4j-specific implementation inside Roslyn projects.

## 4. Functional Requirements

### 4.1 Language-Agnostic Roslyn Abstractions

| ID | Requirement |
| --- | --- |
| FR-001 | The system shall provide language-agnostic Roslyn extraction contracts for C# and VB.NET source documents. |
| FR-002 | The system shall provide a shared extraction context containing repository, solution, project, document, compilation, semantic model, and snapshot context where applicable. |
| FR-003 | The system shall normalize Roslyn symbol identity into deterministic stable keys independent of database IDs and developer machine paths. |
| FR-004 | The system shall normalize source file paths relative to the analyzed repository root when generating evidence and stable identities. |
| FR-005 | The system shall provide shared helpers for evidence span creation, snippet preview creation, snippet hashing, symbol naming, and containing-symbol resolution. |
| FR-006 | The system shall provide shared confidence classification for resolved, partially resolved, generated, inferred, and unresolved semantic facts. |
| FR-007 | The system shall represent semantic unknowns explicitly instead of silently dropping unresolved or ambiguous information. |

### 4.2 C# Semantic Extraction

| ID | Requirement |
| --- | --- |
| FR-008 | The system shall extract C# namespace declarations, including block-scoped and file-scoped namespaces in analyzed target repositories. |
| FR-009 | The system shall extract C# type declarations, including classes, records, structs, interfaces, enums, delegates, nested types, generic types, partial types, and static types. |
| FR-010 | The system shall extract C# method-like members, including methods, constructors, static constructors, local functions where architecturally meaningful, operators, conversion operators, and property/event accessors where needed for dependency evidence. |
| FR-011 | The system shall extract C# properties, indexers, fields, events, constants, parameters, return types, generic type parameters, and constraints where graph model support exists. |
| FR-012 | The system shall extract C# attributes on assemblies, types, members, parameters, and return values where those attributes inform architecture facts. |
| FR-013 | The system shall extract C# inheritance, interface implementation, base type, implemented interface, and generic type relationship facts. |
| FR-014 | The system shall extract C# constructor injection dependencies from constructor parameters and service-like assignment patterns where confidence is sufficient. |
| FR-015 | The system shall extract C# method calls, member access, property access, object creation, extension-method calls, delegate invocations, and relevant static calls. |
| FR-016 | The system shall identify unresolved C# symbols and record unknowns with evidence and confidence instead of inventing targets. |

### 4.3 VB.NET Semantic Extraction

| ID | Requirement |
| --- | --- |
| FR-017 | The system shall extract VB.NET namespace declarations and project-root namespace effects where determinable. |
| FR-018 | The system shall extract VB.NET type declarations, including classes, structures, interfaces, enums, delegates, modules, nested types, generic types, and partial types. |
| FR-019 | The system shall extract VB.NET method-like members, including methods, constructors, shared constructors, operators, conversion operators, property procedures, event handlers, and accessors where needed for dependency evidence. |
| FR-020 | The system shall extract VB.NET properties, default properties, fields, events, constants, parameters, return types, generic type parameters, and constraints where graph model support exists. |
| FR-021 | The system shall extract VB.NET attributes on assemblies, types, members, parameters, and return values where those attributes inform architecture facts. |
| FR-022 | The system shall extract VB.NET inheritance, interface implementation, base type, implemented interface, and generic type relationship facts. |
| FR-023 | The system shall extract VB.NET constructor injection dependencies from constructor parameters and service-like assignment patterns where confidence is sufficient. |
| FR-024 | The system shall extract VB.NET method calls, member access, property access, object creation, extension-method calls, delegate invocations, and relevant shared/static calls. |
| FR-025 | The system shall identify unresolved VB.NET symbols and record unknowns with evidence and confidence instead of inventing targets. |

### 4.4 Declaration Nodes

| ID | Requirement |
| --- | --- |
| FR-026 | The system shall persist or accumulate `Namespace` nodes for extracted namespace facts. |
| FR-027 | The system shall persist or accumulate `Type` nodes for extracted type facts. |
| FR-028 | The system shall persist or accumulate `Method` nodes for extracted method and constructor facts. |
| FR-029 | The system shall persist or accumulate `Property` nodes for extracted property and indexer facts. |
| FR-030 | The system shall persist or accumulate `Field` nodes for extracted field and constant facts. |
| FR-031 | Extracted nodes shall include stable keys, display names, fully qualified names, source language, project identity, containing symbol identity, and metadata needed by later graph queries. |
| FR-032 | Extracted nodes shall distinguish source-declared symbols, metadata symbols, generated-code symbols, unresolved symbols, and inferred placeholders where those distinctions are needed for confidence and evidence. |

### 4.5 Relationship Extraction

| ID | Requirement |
| --- | --- |
| FR-033 | The system shall persist or accumulate `CONTAINS` relationships between repositories, solutions, projects, namespaces, types, methods, properties, and fields where applicable. |
| FR-034 | The system shall persist or accumulate `CALLS` relationships for resolved method, constructor, accessor, delegate, and extension-method calls. |
| FR-035 | The system shall persist or accumulate `IMPLEMENTS` relationships for interface implementation at type and member level where determinable. |
| FR-036 | The system shall persist or accumulate `INHERITS` relationships for base class, derived type, and overridden member relationships where determinable. |
| FR-037 | The system shall persist or accumulate `INJECTS` relationships for constructor-injected dependencies where confidence is sufficient. |
| FR-038 | The system shall persist or accumulate `DEPENDS_ON` relationships for type-level and method-level symbol dependencies. |
| FR-039 | Relationships shall include confidence, evidence links, source symbol identity, target symbol identity where resolved, and unknown-reason data where unresolved. |
| FR-040 | Duplicate relationships discovered through partial declarations or multiple syntax forms shall be de-duplicated deterministically. |

### 4.6 Evidence Capture

| ID | Requirement |
| --- | --- |
| FR-041 | Every extracted semantic node and relationship shall include evidence unless it is a purely derived fact from other persisted facts. |
| FR-042 | Evidence shall include repository-relative file path, line span, column span where available, symbol name, containing symbol, snippet hash, and snippet preview. |
| FR-043 | Evidence shall identify whether the fact came from syntax, semantic model resolution, compiler diagnostics, project metadata, generated code, or inference. |
| FR-044 | Snippet previews shall be small and deterministic and shall not become a substitute for storing full source files. |
| FR-045 | Snippet hashes shall be stable for identical source snippets and usable for evidence comparison across extraction runs. |
| FR-046 | Evidence creation shall be safe when documents have no source text, generated metadata symbols, unavailable line mappings, or compiler errors. |

### 4.7 Compiler Diagnostics

| ID | Requirement |
| --- | --- |
| FR-047 | The system shall collect compiler diagnostics associated with analyzed C# and VB.NET compilations. |
| FR-048 | Compiler diagnostics shall be representable as evidence or extraction metadata linked to the relevant project, document, line span, or symbol where available. |
| FR-049 | Diagnostics shall include diagnostic ID, severity, message, file path, line span, and compiler source where available. |
| FR-050 | The presence of compiler diagnostics shall not prevent partial extraction of resolvable symbols. |
| FR-051 | Diagnostics that reduce semantic certainty shall influence confidence or unknown-reason data for affected facts. |

### 4.8 Generated Code and Partial Types

| ID | Requirement |
| --- | --- |
| FR-052 | The system shall identify generated-code files using filename, auto-generated header, generator metadata, and project convention signals where available. |
| FR-053 | The system shall extract generated-code facts when they are architecturally relevant, while marking their generated origin in metadata. |
| FR-054 | The system shall merge partial type declarations into stable type identities while preserving evidence for each contributing declaration. |
| FR-055 | The system shall merge partial methods and partial members where Roslyn symbol identity supports it. |
| FR-056 | Generated-code and partial-type handling shall avoid double-counting nodes and relationships. |

### 4.9 Cross-Project and Metadata Symbols

| ID | Requirement |
| --- | --- |
| FR-057 | The system shall resolve cross-project symbols inside submitted solutions where Roslyn compilation references allow it. |
| FR-058 | The system shall identify dependencies on metadata symbols from referenced assemblies and packages where source declarations are unavailable. |
| FR-059 | Metadata-symbol dependencies shall be represented with appropriate confidence and evidence from usage sites. |
| FR-060 | The system shall not invent source nodes for metadata symbols that are not part of the analyzed repository. |
| FR-061 | The system shall preserve enough metadata identity for later package, external dependency, framework, and obsolete API rules to evaluate usage. |

### 4.10 Unknowns and Confidence

| ID | Requirement |
| --- | --- |
| FR-062 | The system shall record unknowns for unresolved symbols, ambiguous overloads, missing metadata references, unsupported syntax cases, dynamic dispatch targets, reflection targets, and expression forms that cannot be statically resolved. |
| FR-063 | Unknown records shall include unknown reason, source evidence, confidence, language, project identity, and source symbol context where available. |
| FR-064 | Confidence shall distinguish high-confidence compiler-resolved facts from lower-confidence inferred facts. |
| FR-065 | Dynamic, reflection, late-bound, or string-based invocation patterns shall not be forced into resolved relationships unless Roslyn or deterministic analysis proves the target. |
| FR-066 | Unknown extraction outcomes shall be queryable by later API and MCP consumers through the graph model. |

### 4.11 Extraction Accumulation and Persistence Integration

| ID | Requirement |
| --- | --- |
| FR-067 | WP006 extraction shall contribute facts to the shared extraction accumulation model established by earlier work packages. |
| FR-068 | WP006 shall not embed Neo4j implementation details inside Roslyn language projects. |
| FR-069 | WP006 shall hand graph-ready nodes, relationships, evidence, diagnostics, confidence, and unknowns to application-layer persistence ports. |
| FR-070 | Semantic extraction shall participate in snapshot-scoped persistence so facts are associated with the correct extraction run and snapshot. |
| FR-071 | Repeated extraction of unchanged source shall produce deterministic stable keys and stable relationship identities. |

## 5. Non-Functional Requirements

### 5.1 Determinism

| ID | Requirement |
| --- | --- |
| NFR-001 | Stable keys shall not depend on Neo4j database IDs, absolute developer machine paths, temporary directories, or nondeterministic traversal order. |
| NFR-002 | Extraction ordering shall be deterministic for equivalent inputs. |
| NFR-003 | Duplicate facts from syntax aliases, partial types, generated files, or repeated references shall be de-duplicated deterministically. |

### 5.2 Accuracy and Evidence Quality

| ID | Requirement |
| --- | --- |
| NFR-004 | Compiler-resolved symbol relationships shall be preferred over text matching. |
| NFR-005 | Textual or heuristic inference shall be clearly marked with lower confidence. |
| NFR-006 | Evidence shall be precise enough for a developer to locate the source artifact that produced the fact. |
| NFR-007 | Missing or ambiguous evidence shall result in explicit unknown metadata rather than silent omission. |

### 5.3 Language Coverage

| ID | Requirement |
| --- | --- |
| NFR-008 | C# and VB.NET extraction shall project into the same graph vocabulary. |
| NFR-009 | VB.NET support shall be implemented as first-class behavior, not as a best-effort C#-only afterthought. |
| NFR-010 | Language-specific differences shall be represented in metadata without fragmenting the graph model. |

### 5.4 Performance and Scale

| ID | Requirement |
| --- | --- |
| NFR-011 | Extraction shall process large solutions without requiring all graph facts to be materialized in host-specific code. |
| NFR-012 | Semantic models and compilations shall be reused where practical and safe. |
| NFR-013 | Extraction shall avoid repeated expensive symbol lookups for identical documents or symbols in the same run. |
| NFR-014 | Performance optimizations shall not sacrifice deterministic output, evidence quality, or unknown handling. |

### 5.5 Maintainability

| ID | Requirement |
| --- | --- |
| NFR-015 | C# implementation code shall use block-scoped namespaces. |
| NFR-016 | C# implementation code shall use Allman braces. |
| NFR-017 | C# implementation files shall contain one public type per file. |
| NFR-018 | Private fields shall use underscore-prefixed naming. |
| NFR-019 | Roslyn extraction behavior shall be separated into shared abstractions and language-specific implementations. |
| NFR-020 | Internal and other non-public types introduced for this work package shall receive the same developer-level documentation consideration as public API surface when documentation is necessary to understand behavior, constraints, or architecture. |

### 5.6 Testability

| ID | Requirement |
| --- | --- |
| NFR-021 | Semantic extraction shall be testable using in-memory or fixture-based C# and VB.NET source projects. |
| NFR-022 | Tests shall be able to assert extracted nodes, relationships, evidence, diagnostics, confidence, and unknowns without depending on a running Aspire AppHost. |
| NFR-023 | Tests shall avoid relying on absolute developer paths. |
| NFR-024 | Tests shall cover both successful and degraded compilations. |

## 6. Technical Requirements

### 6.1 Target Runtime and SDK

The implementation shall align with the repository's current .NET target and package guidance. The workspace context indicates projects target `.NET 10`; implementation choices for WP006 shall remain consistent with the existing solution configuration at the time the work package is implemented.

### 6.2 Roslyn API Usage

WP006 shall use Roslyn compiler APIs for semantic extraction. Syntax-only approaches are acceptable only for facts that cannot require semantic resolution or as a precursor to semantic model analysis. Compiler symbols and semantic models are the preferred source for declarations, references, invocations, inheritance, implementations, diagnostics, and source spans.

### 6.3 Project Identity

Project identity shall use normalized project file paths relative to the repository root so identities are deterministic across developer machine locations. Symbol stable keys shall include repository-relative project context where needed to prevent collisions across multi-solution repositories.

### 6.4 Source Path Normalization

Source file paths in evidence shall be relative to the analyzed repository root whenever possible. Absolute paths may be used only as transient runtime data and shall not be persisted as stable identity.

### 6.5 Graph Vocabulary

WP006 shall use the graph node and relationship vocabulary established by earlier work packages and the source brief. At minimum, semantic extraction must support `Namespace`, `Type`, `Method`, `Property`, and `Field` nodes, plus `CONTAINS`, `CALLS`, `IMPLEMENTS`, `INHERITS`, `INJECTS`, and `DEPENDS_ON` relationships.

### 6.6 Error Handling

Compiler errors, missing references, unsupported syntax, generated-code edge cases, and unresolved symbols shall be treated as partial extraction conditions. The extractor shall continue processing resolvable facts and emit diagnostics or unknowns for degraded areas.

### 6.7 Documentation Pass

WP006 shall include a documentation pass covering:

- Roslyn extraction responsibilities by project.
- How C# and VB.NET facts map into the shared graph model.
- How evidence spans, snippet previews, and snippet hashes are produced.
- How confidence and unknowns are assigned.
- How generated code, partial types, metadata symbols, and compiler diagnostics are handled.
- How targeted WP006 tests should be run.

Internal and non-public implementation types shall be documented to the same developer-level standard as public types when they carry non-obvious Roslyn, evidence, confidence, unknown, or graph-projection behavior.

## 7. Exclusions

WP006 shall not implement:

- Dependency injection extraction beyond constructor dependency facts directly visible through semantic analysis.
- Configuration file extraction or configuration-key usage extraction.
- ASP.NET Core endpoint extraction, controller route extraction, middleware extraction, or classic ASP.NET runtime extraction.
- Worker, console, queue consumer, scheduled job, or hosted-service runtime extraction beyond ordinary type and method facts.
- Data-access technology extraction for LINQ to SQL, EF6, EF Core, ADO.NET, raw SQL, stored procedures, or typed DataSets.
- External integration extraction for HTTP clients, WCF, SOAP, gRPC, queues, storage, SMTP, or payment providers beyond ordinary symbol dependencies.
- .NET UI technology extraction for Blazor, Razor views/pages, Windows Forms, WPF, WinUI, .NET MAUI, or Avalonia beyond ordinary symbol declarations and dependencies.
- Rule catalog, rule engine, hotlist, findings, metrics, hotspot analysis, snapshot diff, markdown export, or MCP tool behavior.
- Archon Discovery UI implementation.

## 8. Data and Integration Requirements

### 8.1 Input Data

WP006 consumes repository, solution, project, document, compilation, and semantic model context produced by earlier extraction infrastructure. It assumes the API-triggered extraction path accepts a repository root directory and explicit solution path list.

### 8.2 Output Data

WP006 produces graph-ready semantic facts including nodes, relationships, evidence records, compiler diagnostics, confidence metadata, and unknown records. These outputs must be snapshot-scoped and suitable for Neo4j persistence through established application-layer ports.

### 8.3 Integration with Earlier Work Packages

WP006 depends on earlier work-package outputs for solution/project inventory, extraction request contracts, snapshot orchestration, graph-domain contracts, and persistence seams. It must not bypass those contracts by introducing host-specific or database-specific Roslyn extraction pathways.

### 8.4 Integration with Later Work Packages

Later packages use WP006 semantic facts to implement configuration and DI extraction, runtime extraction, data-access extraction, integration extraction, UI technology extraction, rules, findings, metrics, diff, markdown export, API traversal, and MCP workflows. WP006 must therefore preserve enough symbol metadata, evidence, and confidence context for those later packages to build on.

## 9. Test Requirements

### 9.1 Required Test Coverage

| Test Area | Required Verification |
| --- | --- |
| C# declarations | Namespaces, classes, records, structs, interfaces, enums, delegates, nested types, generic types, methods, constructors, properties, fields, events, parameters, and return types are extracted. |
| VB.NET declarations | Namespaces, classes, structures, interfaces, enums, delegates, modules, nested types, generic types, methods, constructors, properties, fields, events, parameters, and return types are extracted. |
| Relationships | `CONTAINS`, `CALLS`, `IMPLEMENTS`, `INHERITS`, `INJECTS`, and `DEPENDS_ON` relationships are produced with evidence and confidence. |
| Cross-project symbols | References between projects in the same analyzed solution resolve to stable source identities. |
| Metadata symbols | Dependencies on referenced assemblies or packages are represented without inventing unavailable source nodes. |
| Constructor dependencies | Constructor injection facts are extracted for C# and VB.NET where the dependency is visible through parameters. |
| Method calls | Direct calls, constructor calls, extension methods, delegate invocations, static/shared calls, property accessors, and overloads are handled where Roslyn can resolve them. |
| Attributes | Assembly, type, member, parameter, and return-value attributes are extracted where relevant. |
| Evidence spans | File path, line span, column span, symbol name, containing symbol, snippet preview, and snippet hash are generated deterministically. |
| Diagnostics | Compiler diagnostics are captured and linked to project, document, span, or symbol context where available. |
| Generated code | Generated files are detected, marked, and handled without double-counting architecture facts. |
| Partial types | Partial declarations merge into stable identities while preserving declaration evidence. |
| Unresolved symbols | Missing references, dynamic dispatch, reflection, ambiguous overloads, and unsupported syntax create unknowns rather than invented facts. |
| Confidence | Resolved, inferred, generated, metadata-only, and unresolved facts receive appropriate confidence classification. |
| Determinism | Repeated extraction of equivalent inputs produces stable keys and stable relationships. |

### 9.2 Test Constraints

Automated WP006 tests shall not run the Aspire AppHost as a blocking process. Tests should use targeted fixture projects, in-memory source text, temporary isolated test directories, or Roslyn workspace fixtures. Tests shall avoid depending on absolute machine paths or external services.

For this work package, do not run the full test suite unless explicitly requested. Run targeted tests for affected Roslyn, application, extraction, and persistence-seam behavior plus a solution build as final validation during implementation.

## 10. Acceptance Criteria

WP006 is accepted when all of the following are true:

1. Language-agnostic Roslyn abstractions and shared helpers exist for semantic extraction.
2. C# syntax and semantic extraction produces graph-ready declaration, relationship, evidence, diagnostic, confidence, and unknown facts.
3. VB.NET syntax and semantic extraction produces graph-ready declaration, relationship, evidence, diagnostic, confidence, and unknown facts with parity where Roslyn supports it.
4. Namespaces, types, methods, properties, fields, attributes, inheritance, interface implementation, constructor injection, method calls, property access, object creation, parameters, return types, and diagnostics are extracted.
5. `Namespace`, `Type`, `Method`, `Property`, and `Field` nodes are persisted or accumulated through the shared graph contract.
6. `CONTAINS`, `CALLS`, `IMPLEMENTS`, `INHERITS`, `INJECTS`, `DEPENDS_ON`, and related relationships are persisted or accumulated with confidence and evidence.
7. Compiler-symbol and source-code evidence includes file path, line span, symbol name, containing symbol, snippet hash, and snippet preview.
8. Mixed C# and VB.NET solutions produce symbol-level architecture facts.
9. Cross-project symbols, unresolved symbols, generated-code handling, partial types, confidence classification, and explicit unknowns are covered by tests.
10. Compiler diagnostics can be represented as evidence or extraction metadata.
11. Roslyn extraction logic remains separated from host and Neo4j implementation details.
12. Targeted WP006 tests pass.
13. The solution builds successfully.
14. Documentation is updated for Roslyn extraction responsibilities, evidence, confidence, unknowns, diagnostics, and validation workflow.
15. No Archon Discovery UI implementation is introduced.

## 11. Risks and Decisions

### 11.1 Risks

| Risk | Impact | Mitigation |
| --- | --- | --- |
| Roslyn semantic extraction may be expensive for large solutions. | Extraction could become slow or memory-intensive. | Reuse compilations and semantic models where practical; design fact accumulation to stream or batch safely. |
| VB.NET parity may expose language-specific syntax differences. | C# and VB.NET output may drift into incompatible graph shapes. | Keep language-specific extraction behind shared projection contracts and test equivalent graph output. |
| Missing references may reduce semantic resolution. | Some dependencies or calls may be unavailable. | Continue partial extraction and emit diagnostics, confidence reductions, and explicit unknowns. |
| Generated code and partial types may duplicate facts. | Graph output may contain duplicates or misleading relationships. | Use deterministic symbol identity and de-duplication rules. |
| Metadata symbols may be mistaken for source symbols. | The graph could imply unavailable source code exists in the analyzed repository. | Preserve metadata identity but do not invent repository source nodes. |
| Evidence snippets may expose too much source text. | Evidence records could become noisy or unnecessarily large. | Store concise snippet previews and hashes, not full source files. |

### 11.2 Decisions

| Decision | Rationale |
| --- | --- |
| Use a single WP006 specification document. | The user requested one markdown document spec for WP006. |
| Create the documentation under `docs/006-Roslyn-Semantic-Extraction/`. | Existing work-package folders cover 001 through 005; this is the next incremental documentation folder and matches WP006. |
| Treat C# and VB.NET as first-class extraction targets. | The source brief requires legacy and modern .NET support, including mixed-language estates. |
| Prefer compiler-resolved symbols over syntax/text matching. | Archon's architectural facts must be deterministic and evidence-backed. |
| Represent unresolved semantic cases as unknowns. | Unknowns are valuable product facts and must not be silently omitted or invented. |
| Keep Neo4j-specific implementation out of Roslyn projects. | Onion Architecture requires infrastructure details to remain outside inward semantic extraction logic. |

## 12. Manual Verification Requirements

The implementation documentation for WP006 shall instruct a developer to verify the package by:

1. Restoring and building the solution.
2. Running targeted Roslyn extraction tests for C# and VB.NET declaration extraction.
3. Running targeted relationship extraction tests for calls, inheritance, implementations, constructor dependencies, and type dependencies.
4. Running targeted evidence tests for line spans, snippet previews, snippet hashes, and repository-relative paths.
5. Running targeted degraded-compilation tests for diagnostics, unresolved symbols, unknowns, and confidence classification.
6. Confirming no Aspire AppHost blocking process is required for automated validation.
7. Confirming no Archon Discovery UI resource, page, component, or asset has been introduced.

## 13. Traceability Matrix

| Source Requirement | Specification Coverage |
| --- | --- |
| Implement language-agnostic Roslyn abstractions and shared helpers | Sections 3.1, 4.1, 6.2, 10 |
| Implement C# syntax and semantic extraction | Sections 3.2, 4.2, 9, 10 |
| Implement VB.NET syntax and semantic extraction | Sections 3.3, 4.3, 9, 10 |
| Extract namespaces, types, methods, properties, fields, attributes, inheritance, interface implementation, constructor injection, method calls, property access, object creation, parameters, return types, and diagnostics | Sections 4.2 through 4.7, 9, 10 |
| Persist `Namespace`, `Type`, `Method`, `Property`, and `Field` nodes | Section 4.4 |
| Persist `CONTAINS`, `CALLS`, `IMPLEMENTS`, `INHERITS`, `INJECTS`, `DEPENDS_ON`, and related relationships | Section 4.5 |
| Capture compiler-symbol and source-code evidence | Section 4.6 |
| Mixed C# and VB.NET solutions produce symbol-level architecture facts | Sections 4.2, 4.3, 9, 10 |
| Method calls, constructor dependencies, inheritance, and interface implementations are represented with evidence | Sections 4.5, 4.6, 9, 10 |
| Compiler diagnostics can be represented as evidence | Sections 4.7, 9, 10 |
| Tests cover C# extraction, VB.NET extraction, cross-project symbols, unresolved symbols, generated-code handling, confidence classification, and explicit unknowns | Section 9 |
| Documentation pass includes internal and non-public developer-level documentation expectations | Sections 5.5 and 6.7 |

## 14. Open Questions

No blocking open questions are known for producing the WP006 Roslyn semantic extraction specification. Implementation may still need to confirm the exact Roslyn package versions already used by the solution and whether any existing extraction accumulation contracts need minor extension to carry all WP006 evidence, confidence, diagnostic, and unknown metadata.

## 15. Change Log

| Date | Change |
| --- | --- |
| 2026-05-21 | Created initial single-document WP006 specification from `docs/foundation/work-packages.md` and the Archon source brief. |
