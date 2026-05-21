# Graph Domain Model

The graph domain model is the language Archon uses to describe architecture knowledge before that knowledge is persisted, queried, exported, or shown in a user interface. The current model lives primarily in `src/Archon.Domain/Graph` and `src/Archon.Application/Extraction`. It is intentionally pure domain and application behavior: it does not load Roslyn workspaces, run extractors, write Neo4j data, expose API endpoints, render markdown, or compose runtime hosts.

Read this page after [solution architecture](solution-architecture.md) and before [Neo4j persistence foundation](neo4j-persistence-foundation.md). Related terms are defined in the [glossary](glossary.md), and domain validation commands are collected in [validation and test workflows](validation-and-test-workflows.md).

Reader path: [Home](home.md) -> [Solution architecture](solution-architecture.md) -> Graph domain model -> [Neo4j persistence foundation](neo4j-persistence-foundation.md).

## Controlled values

A **controlled value** is a domain-owned string identity that behaves like a smart enum. Code uses named static instances such as `NodeKind.Project`, while external contracts see stable strings such as `Project`. This avoids **numeric enum drift**, which is the failure mode where adding, deleting, or reordering ordinary enum members changes serialized numeric meanings by accident.

Archon needs stable string values because the same graph vocabulary flows through JSON APIs, MCP responses, Neo4j properties, markdown output, tests, and documentation. The current controlled values define node kinds, edge kinds, evidence kinds, rule categories, finding severities, finding statuses, knowledge classifications, metric scopes, and generated-summary categories.

Controlled values do not perform extraction or persistence. They parse exact external strings, expose deterministic value lists for validation, and serialize through `System.Text.Json` as strings rather than objects or numbers. Later work packages can build graph facts, fingerprints, persistence, API responses, and MCP resources on the same vocabulary without inventing parallel constants.

## Stable keys

A **stable key** is the durable logical identity for an architecture fact. It is written as a prefixed string such as `project://src/Customer.Api/Customer.Api.csproj` or `type://Customer.Application.CustomerService`. Stable keys are not database identifiers. A Neo4j internal node ID, a future relational row ID, or a process-local object reference can change when data is recreated, imported, or analyzed on another machine.

Path-based stable keys use **repository-relative paths**, meaning paths are written from the repository root rather than from a developer machine root. For example, `src/Customer.Api/Customer.Api.csproj` is repository-relative, while `D:\Dev\Archon\src\Customer.Api\Customer.Api.csproj` is not. The domain helper normalizes separators to forward slashes and rejects absolute or rooted paths so keys remain deterministic across Windows workstations, Linux agents, and CI environments.

Extraction slices should call `StableKeyGenerator` instead of assembling strings by hand. This keeps prefixes such as `repository://`, `solution://`, `project://`, `package://`, `endpoint://`, `dbtable://`, `rule://`, `finding://`, `metric://`, and `summary://` consistent across the product.

## Metadata and fingerprints

**Metadata** is the extension area for extractor-specific details that are useful but not part of the normalized graph contract. Route tokens, HTTP verb sets, options binding details, SQL classification hints, queue transport details, and provider-specific mapping payloads are good metadata candidates.

Stable keys, graph kinds, evidence kinds, knowledge classifications, confidence, unknown-state indicators, and primary source-location fields are not metadata candidates. Those fields are normalized graph properties because later code needs to query, compare, and validate them directly. `GraphMetadata` has an explicit empty value, serializes metadata with deterministic property ordering, and rejects known normalized graph property names when they are accidentally placed in metadata.

A **fingerprint** is a deterministic hash of diff-relevant graph content. It answers a different question from a stable key. The stable key asks, “is this the same logical graph fact?” The fingerprint asks, “has the diff-relevant content for this graph fact changed?” Fingerprints are generated from normalized field values and canonical metadata, then represented as `sha256:` strings.

For example, two endpoint metadata dictionaries that contain the same route template and HTTP verbs in different insertion orders produce the same canonical metadata string and therefore the same fingerprint input. If the HTTP verbs change from `GET` to `GET` plus `POST`, the canonical metadata changes and so does the fingerprint because endpoint behavior changed.

## Graph facts

A **graph fact** is a domain object that states something Archon knows about an architecture snapshot. A repository was extracted, a solution belongs to that repository, a project node exists, one node depends on another, an evidence record explains a claim, a rule produced a finding, a metric was calculated, or a generated summary was emitted.

The current model includes repository, solution, snapshot header, architecture node, architecture edge, evidence, rule, finding, metric, and generated summary contracts. These models are snapshot-scoped where appropriate and use stable keys and fingerprints rather than store-local IDs. Neo4j database IDs are intentionally absent from the domain model.

## Evidence-first modeling

The graph fact model is **evidence-first**. Evidence-first means a fact is designed to carry or link to the explanation that caused the system to believe it. An architecture node can point at the project file, source symbol, configuration file, generated artifact, compiler diagnostic, inference, or manual annotation that supports it. An architecture edge can point at evidence for a reference, call, dependency, navigation, package use, or data-access relationship.

This model helps contributors answer not only “what did Archon find?” but also “why did Archon believe it?” without inferring evidence from incidental logs or persistence details.

For example, a `Project` node for `Customer.Api` can point to the project-file evidence that caused extraction to emit the node. A `DependsOn` edge can point at package-reference evidence or a source call site. A finding can preserve both the rule version that classified it and the primary evidence that explains why the concern exists.

## Confidence and unknown state

Confidence and unknown state are first-class parts of the contract rather than optional notes. **Confidence** is a deterministic decimal from zero through one that lets later code compare certainty predictably. **Knowledge kind** explains whether a fact is a direct fact, an inference, an explicit unknown, or human-confirmed knowledge. **Unknown state** records whether a fact contains unknown data and, when it does, the reason the data is unknown.

If a fact uses `KnowledgeKind.Unknown`, or if it declares unknown data, it must carry a non-empty unknown reason. This rule prevents silent nulls from looking like meaningful absence. For example, a future extractor might know that an endpoint calls an external service but be unable to determine the service owner; the graph can retain that uncertainty with an explicit reason instead of dropping the relationship or hiding the limitation in metadata.

## Rules, findings, metrics, and generated summaries

Rules, findings, metrics, and generated summaries are part of the domain language. A rule is a versioned catalog entry. A finding must preserve both rule code and rule version so historical output remains explainable after rule definitions evolve. A metric is a snapshot output with either a numeric value or a text value because a metric without a value cannot support reporting or comparison. A generated summary is content associated with a snapshot or target stable key, while markdown export and summary generation behavior remain later work.

Across these contracts, Neo4j database IDs remain absent. Future persistence can assign store-local IDs internally, but domain models use stable keys and fingerprints so they remain deterministic before and after persistence.

## Snapshot assembly and accumulation

**Snapshot assembly** is the process of gathering many graph fact contributions into one in-memory `ExtractedArchitectureSnapshot`. That assembled snapshot contains the optional snapshot header, repositories, solutions, nodes, edges, evidence, rules, findings, metrics, generated summaries, warnings, and errors. It is an application contract rather than an extractor, database adapter, host endpoint, or UI feature.

The **extraction accumulator** is `ArchitectureSnapshotAccumulator`. An accumulator is a small stateful builder that accepts contributions from future extractor slices through explicit add and merge methods. A project extractor might add repository, solution, project-node, and project-file evidence facts. A dependency extractor might add edges between project nodes. A rule catalog contributor might add rule definitions, and a rule evaluator might add findings that reference those rule versions.

Duplicate stable keys have a deterministic policy: latest contribution wins for stable-keyed sections. Rule definitions use an equivalent policy based on rule code plus rule version because they are global catalog entries rather than stable-keyed snapshot facts. Final stable-keyed sections are ordered by ordinal stable-key string, and rule definitions are ordered by ordinal rule identity, so output remains predictable even when extractor contribution order varies.

Warnings and errors are diagnostic streams rather than graph facts. The accumulator trims blank diagnostics but preserves repeated non-empty warnings and errors in insertion order because repeated messages can explain repeated extractor observations.

Accumulation is deliberately free of hidden side effects. It does not read source files, load Roslyn workspaces, run analyzers, call Neo4j, map ASP.NET Core endpoints, expose MCP tools, or render markdown. Those behaviors belong to later work packages and outer layers.

The current WP004 placeholder pipeline uses this accumulator as the shared contribution model. Pipeline stages receive the accumulator, add warnings, errors, or graph facts, and return a stage result that tells the runner whether later stages can safely continue. Snapshot assembly then merges those accumulated contributions with deterministic repository and solution boundary facts from the accepted request. This means the assembled snapshot is complete as a contract shape even when real extractor sections are empty. Empty node, edge, evidence, finding, metric, and generated-summary collections are explicit current-state output, not missing serialization fields.
