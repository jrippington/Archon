# WP002 Implementation Notes - Architecture Graph Domain Model and Shared Contracts

## Work Item 1 - Domain Controlled Values and Developer-Facing Serialization Slice

### Implementation Summary

Work Item 1 introduced the first WP002 domain capability: controlled value sets for architecture graph classifications. A controlled value is a domain-owned string value that behaves like a smart enum without using numeric enum ordinals. This matters because Archon will later serialize the same values through JSON APIs, MCP responses, Neo4j properties, markdown export, and tests. If ordinary numeric enums were used, inserting or reordering enum members could change external meaning by accident. The implemented model instead treats the stable external string as the durable identity.

The implementation added a reusable `ControlledValue<TValue>` base type under `src/Archon.Domain/Graph/ControlledValues`. Each concrete value set declares static instances that register themselves with the base type. The base type provides deterministic declaration-order enumeration, strict parsing through `Parse`, non-throwing validation through `TryParse`, value-object equality, and string conversion through `ToString`. Parsing is ordinal and case-sensitive so external contracts remain exact and deterministic.

System.Text.Json support was included through `ControlledValueJsonConverterFactory` and `ControlledValueJsonConverter<TValue>`. Concrete controlled-value types are annotated with the converter factory so JSON serialization writes the stable string and deserialization resolves that string through the same parser used by application code. This preserves one validation path for external input and prevents controlled values from being serialized as objects or numeric ordinals.

### Controlled Value Sets Implemented

The following concrete value sets were added in `Archon.Domain`:

- `NodeKind` with all first-class node kinds from WP002 FR-011 through FR-050 and source brief sections 12.3 and E.4.2.
- `EdgeKind` with all first-class relationship kinds from WP002 FR-051 through FR-086 and source brief sections 12.4 and E.4.3.
- `EvidenceKind` with evidence kinds from source brief section 12.5.
- `RuleCategory` with rule categories from source brief section 12.6.
- `FindingSeverity` with critical, high, medium, low, and informational severity values.
- `FindingStatus` with open, acknowledged, suppressed, resolved, and unknown status values.
- `KnowledgeKind` with fact, inference, unknown, and human-confirmed classifications.
- `MetricScopeKind` with snapshot, node, edge, graph, project, and modernization scopes.
- `SummaryKind` with snapshot, node, edge, graph, project, and modernization summary categories.

### Tests Added

Tests were added in `test/Archon.Domain.Tests/Graph/ControlledValues/ControlledValueTests.cs`. The tests verify required value existence, stable external strings, parse behavior, try-parse behavior, equality across canonical instances, deterministic `All` ordering, and representative JSON serialization/deserialization.

### Validation Commands

The targeted validation command for this work item passed:

```powershell
dotnet test .\test\Archon.Domain.Tests\Archon.Domain.Tests.csproj --filter FullyQualifiedName~ControlledValue
```

Result: 136 tests passed, 0 failed, 0 skipped.

### Wiki Review Result

Reviewed `wiki/home.md` because Work Item 1 introduces developer-facing graph-domain terminology and the first WP002 domain contracts. Updated `wiki/home.md` with a narrative explanation of controlled values, smart-enum-style stable external string identities, numeric enum drift, the current controlled-value vocabulary, and the fact that this slice is pure domain behavior rather than extraction or Neo4j persistence.

## Work Item 2 - Stable-Key Generation Slice

### Implementation Summary

Work Item 2 introduced deterministic graph identity primitives for WP002. A stable key is the string identity that lets Archon recognize the same logical architecture fact across extraction snapshots without depending on a Neo4j internal ID, a relational database identity, or a process-local object reference. This is the identity layer later nodes, edges, evidence, findings, metrics, and generated summaries can share.

The implementation added `StableKey` as a small domain value object under `src/Archon.Domain/Graph/Identity`. The value object rejects null, empty, and whitespace-only strings so graph facts cannot be created with ambiguous identity. Equality is value-based because two independently generated stable keys with the same string represent the same logical graph identity.

The implementation also added `RepositoryRelativePath` to normalize repository artifact paths before they enter path-based stable keys. It converts Windows separators to forward slashes, removes leading `./` prefixes, collapses repeated separators, trims surrounding slash separators, and rejects absolute or rooted paths such as `D:\Dev\Archon\...`, `/home/user/...`, and UNC-style shares. This preserves the source-brief requirement that stable keys must not depend on developer-machine root paths.

`StableKeyGenerator` is the single shared generation component for Work Item 2. It exposes explicit methods for every prefix required by the WP002 specification: repository, solution, project, package, namespace, type, method, property, field, endpoint, controller, hosted service, configuration, DbContext, LINQ to SQL data context, entity, database table, database column, stored procedure, external service, queue, topic, file, pipeline, rule, finding, metric, and summary. File-like inputs use repository-relative path normalization, endpoint keys normalize HTTP verbs to uppercase and route templates to one leading slash, and rule keys include version so historical findings can remain explainable when rule behavior changes.

### Tests Added

Tests were added in `test/Archon.Domain.Tests/Graph/Identity/StableKeyTests.cs`. The tests verify `StableKey` validation and value equality, repository-relative path normalization and absolute-path rejection, every required stable-key prefix, deterministic generation for equivalent inputs, and invalid generator input handling.

### Validation Commands

The targeted validation command for this work item passed:

```powershell
dotnet test .\test\Archon.Domain.Tests\Archon.Domain.Tests.csproj --filter FullyQualifiedName~StableKey
```

Result: 40 tests passed, 0 failed, 0 skipped.

### Wiki Review Result

Reviewed `wiki/home.md` because Work Item 2 introduces developer-facing stable-key terminology, repository-relative path rules, and a shared identity-generation component. Updated `wiki/home.md` with a narrative explanation of stable keys, why they are not database identifiers, why repository-relative paths are required for cross-machine determinism, and why extraction slices should use `StableKeyGenerator` rather than assembling key strings independently.

## Work Item 3 - Metadata and Fingerprint Slice

### Implementation Summary

Work Item 3 introduced deterministic metadata and fingerprint support for diff-ready graph facts. Metadata is the extension area for extraction-specific details that do not belong in normalized graph properties. A fingerprint is a deterministic content hash over normalized, diff-relevant graph fields plus canonical metadata. Later snapshot diff work can compare fingerprints to decide whether graph facts changed without relying on database IDs, process-local values, or dictionary insertion order.

The implementation added `GraphMetadata` under `src/Archon.Domain/Graph/Metadata`. It provides an explicit `GraphMetadata.Empty` instance that canonicalizes to `{}`. Non-empty metadata is created from JSON-compatible values and serializes to canonical JSON with ordinal property ordering. Nested object properties are also ordered recursively, while array item order is preserved because array order can be meaningful. Metadata rejects normalized graph properties such as stable keys, graph kinds, evidence kinds, knowledge kinds, confidence, unknown-state fields, and primary file or line fields so those core values remain first-class fields rather than hidden JSON payload.

The implementation added `Fingerprint`, `FingerprintInput`, and `FingerprintGenerator` under `src/Archon.Domain/Graph/Identity`. `Fingerprint` validates the external hash string. `FingerprintInput` builds deterministic line-oriented canonical input from a record category, sorted field names, explicit null markers, and canonical metadata. `FingerprintGenerator` hashes canonical UTF-8 input with SHA-256 and prefixes the lower-case hex digest with `sha256:`. It includes category-specific helpers for nodes, edges, evidence, findings, metrics, and generated summaries. These helpers accept normalized diff-relevant fields because full graph fact records are introduced in later work items.

### Metadata Fingerprint Example

Two metadata payloads with the same logical content but different insertion order produce the same canonical JSON and therefore the same fingerprint input:

```json
{"httpVerbs":["GET","POST"],"provider":"AspNetCore","routeTemplate":"/api/customers/{id}"}
```

If metadata changes from `{"httpVerbs":["GET"]}` to `{"httpVerbs":["GET","POST"]}`, the canonical metadata string changes and the resulting fingerprint changes. This is intentional because HTTP verb metadata is diff-relevant behavior for endpoint and metric facts. In contrast, values never supplied to `FingerprintInput`, such as database IDs or process IDs, cannot affect the fingerprint.

### Tests Added

Tests were added in `test/Archon.Domain.Tests/Graph/Metadata/GraphMetadataTests.cs` and `test/Archon.Domain.Tests/Graph/Identity/FingerprintTests.cs`. The tests verify explicit empty metadata, canonical metadata ordering, recursive nested object ordering, reserved normalized property rejection, invalid metadata keys, fingerprint value validation, deterministic equivalent fingerprints, changed diff-relevant content, metadata-driven fingerprint changes, excluded non-input values, and required helper coverage for nodes, edges, evidence, findings, metrics, and generated summaries.

### Validation Commands

The targeted validation command for this work item passed:

```powershell
dotnet test .\test\Archon.Domain.Tests\Archon.Domain.Tests.csproj --filter "FullyQualifiedName~Metadata|FullyQualifiedName~Fingerprint"
```

Result: 22 tests passed, 0 failed, 0 skipped.

### Wiki Review Result

Reviewed `wiki/home.md` because Work Item 3 introduces developer-facing metadata and fingerprint terminology. Updated `wiki/home.md` with narrative guidance that explains metadata as an extraction-specific extension area, distinguishes normalized graph fields from metadata, defines fingerprint as a diff-relevant content hash, contrasts stable keys with fingerprints, and gives an example of metadata order stability versus behavior-changing metadata edits.

## Work Item 4 - Evidence-First Graph Fact Model Slice

### Implementation Summary

Work Item 4 introduced the snapshot-scoped graph fact model layer in `Archon.Domain`. This layer is the developer-facing contract for architecture facts before any Neo4j persistence, extractor behavior, API endpoints, MCP tools, markdown export, or UI discovery behavior exists. The models deliberately use stable keys and fingerprints from earlier WP002 slices rather than database-local IDs, so a fact can be assembled, serialized, tested, compared, and later persisted without changing its domain identity.

The implementation added explicit `Confidence` and `UnknownState` value objects under `src/Archon.Domain/Graph/Model`. Confidence is a decimal value from zero through one and implements deterministic comparison for future threshold and reporting logic. Unknown state is an explicit object with `HasUnknownData` and `UnknownReason`; it requires a non-empty reason whenever a fact declares unknown data. Shared graph fact validation also enforces the WP002 invariant that `KnowledgeKind.Unknown` on nodes, edges, evidence, or findings requires a non-empty unknown reason.

The model set now includes `RepositoryModel`, `SolutionModel`, `SnapshotHeader`, `ArchitectureNode`, `ArchitectureEdge`, `EvidenceRecord`, `RuleDefinition`, `FindingRecord`, `MetricRecord`, and `GeneratedSummary`. Repository, solution, and snapshot models describe the extraction scope. Nodes and edges describe architecture concepts and relationships. Evidence records explain where facts came from. Rules and findings preserve versioned rule identity, severity, status, confidence, suppression fields, and evidence linkage. Metrics require at least one numeric or text value, and generated summaries carry content without deciding how later work packages render it.

### Evidence-First Modeling Examples

An architecture node is not merely a label such as `Project`. In the WP002 contract, a node carries its snapshot stable key, its own stable key, `NodeKind`, display and search names, optional project or parent links, `KnowledgeKind`, confidence, unknown state, optional primary evidence, metadata, and fingerprint. For example, a `Project` node for `Customer.Api` can point to the project-file evidence that caused extraction to emit the node and can carry `Confidence.Certain` when the project file was directly observed.

An architecture edge is similarly evidence-first. A `DependsOn` edge must carry both source and target node stable keys, a directness flag, knowledge classification, confidence, unknown state, optional primary evidence, metadata, and fingerprint. An edge without endpoints is rejected because it cannot participate in graph traversal or explain a relationship.

An evidence record explains a claim. Project-file evidence can identify `src/Customer.Api/Customer.Api.csproj`, optional line range and symbol details, snippet hash or preview, knowledge classification, confidence, unknown state, metadata, and fingerprint. Evidence is snapshot-scoped so one canonical evidence record can later support multiple nodes, edges, findings, or metrics within the same extraction run.

A finding preserves rule provenance. The model requires both rule code and rule version so historical findings remain explainable when rule definitions evolve. It also records severity, status, title, description, knowledge kind, confidence, unknown state, optional primary node, optional primary evidence, optional first/latest seen snapshot keys, suppression details, metadata, and fingerprint.

### Tests Added

Tests were added in `test/Archon.Domain.Tests/Graph/Model/GraphFactModelTests.cs`. The tests verify confidence comparison and range validation, unknown-state reason enforcement, unknown knowledge invariants, representative construction of every graph fact model, required edge endpoints, required finding rule code and version, required metric values, representative JSON serialization of stable controlled-value strings and explicit unknown-state fields, and the absence of Neo4j-style public `Id` properties.

### Validation Commands

The targeted validation command for this work item passed:

```powershell
dotnet test .\test\Archon.Domain.Tests\Archon.Domain.Tests.csproj --filter "FullyQualifiedName~GraphFact|FullyQualifiedName~Unknown|FullyQualifiedName~Evidence"
```

Result: 36 tests passed, 0 failed, 0 skipped.

The workspace build also passed through Visual Studio build validation.

### Wiki Review Result

Reviewed `wiki/home.md` because Work Item 4 introduces developer-facing graph fact, evidence-first modeling, confidence, knowledge classification, unknown-state, and no-Neo4j-ID terminology. Updated `wiki/home.md` with book-like current-state guidance that explains the snapshot-scoped graph fact model, how evidence links facts to source material, why confidence and unknown reasons are first-class fields, how rules and findings preserve versioned provenance, why metrics and generated summaries are snapshot outputs, and why domain contracts intentionally exclude Neo4j database IDs.

## Work Item 5 - Application Snapshot Accumulation Slice

### Implementation Summary

Work Item 5 introduced the application-layer snapshot assembly surface for WP002. The domain graph fact models from Work Item 4 describe individual facts, while the application accumulation slice describes how future extractor slices can contribute those facts into one authoritative in-memory snapshot before any persistence, API orchestration, MCP exposure, markdown export, or UI behavior exists.

The implementation added `ExtractedArchitectureSnapshot` under `src/Archon.Application/Extraction/Contracts`. This contract contains the optional snapshot header, repositories, solutions, nodes, edges, evidence, findings, metrics, generated summaries, warnings, and errors. Its constructor copies every supplied sequence into read-only lists so the assembled snapshot is not accidentally changed through caller-owned collections after creation. It also normalizes warning and error diagnostics by trimming text and omitting blank messages.

The implementation added `ArchitectureSnapshotAccumulator` under `src/Archon.Application/Extraction/Accumulation`. The accumulator exposes explicit add operations for every stable-keyed snapshot section and diagnostic operation. Stable-keyed facts use a deterministic latest-wins duplicate policy: when a repository, solution, node, edge, evidence record, finding, metric, or generated summary arrives with the same stable key as an earlier contribution, the later contribution replaces the earlier one. The final snapshot orders stable-keyed sections by ordinal stable-key string so output is predictable regardless of extractor contribution order. Warnings and errors are intentionally preserved as insertion-ordered diagnostic streams rather than de-duplicated facts.

### Future Extractor Walkthrough

A future extractor can contribute to the accumulator without owning persistence models. For example, a project extractor might begin by creating a `RepositoryModel`, a `SolutionModel`, and a `SnapshotHeader`, then call `SetSnapshotHeader`, `AddRepository`, and `AddSolution`. As it discovers project files, it can add an `EvidenceRecord` for `src/Customer.Api/Customer.Api.csproj`, then add an `ArchitectureNode` for the project that points back to that evidence. If the same extractor later discovers a more complete project model with the same stable key, it can add the node again; the accumulator keeps the latest version and still emits only one project node in deterministic stable-key order.

Another extractor can run independently and merge its own facts into the same accumulator. A dependency extractor might add a `DependsOn` edge between two project nodes and preserve a warning if package restore produced incomplete information. A rule evaluator might add a `FindingRecord` that references the project node and the evidence record. A metrics contributor might add a `MetricRecord` for project reference count, while a summarization contributor might add a `GeneratedSummary`. When orchestration calls `ToSnapshot`, the result is one `ExtractedArchitectureSnapshot` containing all contributed sections, with stable-keyed duplicates resolved and warnings/errors preserved for diagnostics.

The accumulator remains intentionally narrow. It does not read the filesystem, load Roslyn workspaces, run extractors, write Neo4j, publish endpoints, or interpret MCP requests. It is the application contract that future work packages can use when those behaviors are implemented elsewhere in the Onion Architecture.

### Tests Added

Tests were added in `test/Archon.Application.Tests/Extraction/Accumulation/ArchitectureSnapshotAccumulatorTests.cs`. The tests build a representative snapshot with one repository, one solution, one snapshot header, one node, one edge, one evidence record, one finding, one metric, one generated summary, one warning, and one error. They also verify deterministic duplicate stable-key replacement, stable-key output ordering, diagnostic preservation, snapshot merge behavior, and absence of persistence, host, Roslyn, Neo4j, or infrastructure assembly dependencies.

### Validation Commands

The targeted validation command for this work item passed:

```powershell
dotnet test .\test\Archon.Application.Tests\Archon.Application.Tests.csproj --filter FullyQualifiedName~Accumulation
```

Result: 5 tests passed, 0 failed, 0 skipped.

The workspace build also passed through Visual Studio build validation.

### Wiki Review Result

Reviewed `wiki/home.md` because Work Item 5 introduces developer-facing extraction accumulator, snapshot assembly, duplicate stable-key policy, warning, and error terminology. Updated `wiki/home.md` with current-state narrative guidance that explains `ExtractedArchitectureSnapshot`, `ArchitectureSnapshotAccumulator`, latest-wins duplicate replacement, deterministic stable-key ordering, diagnostic preservation, and the boundary that accumulation performs no I/O, Roslyn loading, host behavior, MCP behavior, or Neo4j persistence.

## Work Item 6 - Cross-Slice Validation and Documentation Completion Slice

### Cross-Slice Validation Summary

Work Item 6 validated WP002 as one coherent developer-facing capability across `Archon.Domain` and `Archon.Application`. The validation covered controlled values, stable keys, metadata canonicalization, fingerprints, graph fact models, unknown-state behavior, evidence records, extracted architecture snapshots, and accumulation behavior.

The targeted domain validation command passed:

```powershell
dotnet test .\test\Archon.Domain.Tests\Archon.Domain.Tests.csproj --filter "FullyQualifiedName~ControlledValue|FullyQualifiedName~StableKey|FullyQualifiedName~Metadata|FullyQualifiedName~Fingerprint|FullyQualifiedName~GraphFact|FullyQualifiedName~Unknown|FullyQualifiedName~Evidence"
```

Result: 218 tests passed, 0 failed, 0 skipped. The first attempt to run the command was split by the terminal into `d` and `otnet`, so it failed before test execution with `CommandNotFoundException`. The command was retried through an explicit PowerShell command string and completed successfully.

The targeted application validation command passed:

```powershell
dotnet test .\test\Archon.Application.Tests\Archon.Application.Tests.csproj --filter FullyQualifiedName~Accumulation
```

Result: 5 tests passed, 0 failed, 0 skipped.

The full solution build command passed:

```powershell
dotnet build .\Archon.slnx
```

Result: build succeeded.

### Documentation-Pass Review Outcome

Reviewed representative touched production and test files across `src/Archon.Domain/Graph`, `src/Archon.Application/Extraction`, `test/Archon.Domain.Tests/Graph`, and `test/Archon.Application.Tests/Extraction`. The reviewed files include the application snapshot contract, accumulator, accumulator tests, graph fact model types, shared graph fact validation, confidence and unknown-state primitives, and fingerprint input support.

The review confirmed that touched public and internal types have local XML or developer-level documentation, public constructors and methods document their parameters, executable methods and constructors include explanatory comments, test methods explain scenario and behavioral significance, and non-obvious properties have comments. No documentation-only correction was required during Work Item 6.

### Completed Work Item Summary

WP002 now contains six validated slices:

1. Controlled-value graph vocabulary using smart-enum/value-object style stable external strings.
2. Stable-key value objects, repository-relative path normalization, and shared stable-key generation.
3. Deterministic metadata canonicalization and SHA-256 fingerprint generation.
4. Evidence-first graph fact models with confidence, knowledge classification, explicit unknown-state invariants, metadata, and fingerprints.
5. Application-layer extracted architecture snapshot and accumulation contracts with deterministic duplicate stable-key behavior.
6. Cross-slice validation, solution build validation, and documentation-pass review.

### Design Decisions Confirmed

- Controlled values remain string-backed smart-enum-style domain values rather than numeric enums, preventing numeric enum drift across JSON, persistence, API, MCP, markdown, and tests.
- Stable keys identify logical architecture facts and intentionally exclude Neo4j, relational, process-local, or store-local identifiers.
- Repository-relative paths are required for path-based identities so developer-machine roots do not affect deterministic output.
- Fingerprints are deterministic hashes of diff-relevant normalized content and canonical metadata; they answer whether content changed, not whether a fact has the same identity.
- Metadata is an extension area for extractor-specific details and must not hide normalized graph fields such as stable keys, graph kinds, evidence kinds, confidence, unknown state, or primary source-location fields.
- Unknown state and confidence are first-class graph fact fields. Unknown knowledge or unknown data must carry a non-empty reason.
- The extraction accumulator uses latest-wins duplicate replacement for stable-keyed sections and preserves warnings and errors as insertion-ordered diagnostic streams.

### Out-of-Scope Boundaries Confirmed

WP002 still does not implement Neo4j persistence, Cypher behavior, graph constraints or indexes, Roslyn workspace loading, semantic extraction, API extraction endpoints, API query endpoints, MCP tools/resources/prompts, markdown export generation, Discovery UI behavior, disk-backed rule loading, or rule evaluation. Existing documentation and wiki guidance describe these capabilities as later work-package responsibilities rather than completed WP002 behavior.

### Wiki Review Result

Reviewed `wiki/home.md` and the WP002 implementation notes for cross-slice completeness. No additional wiki page update was required during Work Item 6 because Work Items 1 through 5 had already updated `wiki/home.md` with current-state, book-like narrative guidance for controlled values, stable keys, metadata, fingerprints, evidence-first graph facts, confidence, unknown state, snapshot assembly, accumulation, duplicate behavior, diagnostics, and no-I/O boundaries. The review confirmed the wiki does not present Neo4j persistence, Roslyn extraction, API orchestration, MCP tools, markdown export, or UI behavior as complete in WP002.

## Work Item 7 - Final Mandatory Wiki Review and Documentation Outcome Slice

### Final Wiki Scope Reviewed

The final WP002 wiki review located the workspace wiki documentation area at `wiki/` and reviewed `wiki/home.md` as the available repository wiki page. No separate glossary, appendix, architecture page, setup page, or extraction-specific wiki page was present in the workspace. The review therefore treated `wiki/home.md` as the active contributor-facing wiki path for WP002 concepts, and reviewed this implementation notes document as the formal work-package execution record.

### WP002 Wiki Impact Reviewed

The review checked whether WP002 changed or materially clarified developer-facing architecture graph terminology, stable keys, fingerprints, controlled values, metadata, evidence, confidence, unknown state, extraction accumulation, warning and error diagnostics, and Onion Architecture boundaries. It also checked whether the wiki described current behavior rather than future aspiration, whether dense architecture concepts used developed narrative prose, whether important technical terms were defined on first use, and whether examples or walkthrough-style explanations existed where they materially improved comprehension.

`wiki/home.md` already contains the required WP002 guidance from Work Items 1 through 5. It explains controlled values and numeric enum drift, stable keys and repository-relative paths, metadata and fingerprints, evidence-first graph facts, confidence, knowledge kind, unknown state, rule and finding provenance, metrics, generated summaries, snapshot assembly, extraction accumulation, latest-wins duplicate stable-key behavior, diagnostic preservation, and the no-I/O application boundary. It also explicitly states that graph persistence, extraction, API/MCP behavior, markdown export, and UI behavior remain later work-package responsibilities.

### Final Wiki Review Result

Wiki review result: No additional wiki page update was required for Work Item 7. Reviewed `wiki/home.md` and `docs/002-Architecture-Graph-Domain-Model/implementation-notes-wp002.md`; `wiki/home.md` already reflects the completed WP002 domain and application contract behavior with current-state, book-like narrative guidance and defines the required technical terms in context. No wiki pages were created, split, renamed, retired, or left stale. The final documentation continues to avoid presenting Neo4j persistence, Roslyn extraction, API orchestration, MCP tools/resources/prompts, markdown export, or Discovery UI behavior as complete in WP002.
