# Neo4j Persistence Foundation

The Neo4j persistence foundation turns assembled graph contracts into durable graph records. It provides validated Neo4j configuration, dependency-injection-owned driver lifecycle, a credential-safe readiness probe, idempotent schema initialization, explicitly guarded graph recreation for local development and tests, and snapshot persistence for every currently supported graph section.

This page assumes familiarity with the [graph domain model](graph-domain-model.md) and [solution architecture](solution-architecture.md). Runtime composition is described in [runtime foundation](runtime-foundation.md), validation commands are collected in [validation and test workflows](validation-and-test-workflows.md), and specialized terms are defined in the [glossary](glossary.md).

Reader path: [Home](home.md) -> [Graph domain model](graph-domain-model.md) -> Neo4j persistence foundation -> [Validation and test workflows](validation-and-test-workflows.md).

## Configuration, driver lifecycle, and health

Neo4j host configuration is represented by the `Neo4j` configuration section. It contains the Bolt-compatible `Uri`, target `Database`, `Username`, `Password`, `ConnectionTimeout`, `MaxTransactionRetryTime`, and optional `EncryptionMode`. A **Bolt-compatible URI** is the address used by the Neo4j driver protocol, such as `bolt://localhost:7687`; it is separate from the HTTP browser endpoint on port `7474`.

The adapter validates these settings before creating the driver and reports only safe setting names and structural problems. Password values and other secret material must come from secure configuration providers in real environments and must not be copied into logs, validation messages, wiki examples, or test assertions.

The official Neo4j driver is created by infrastructure code and owned by dependency injection. Application and domain projects do not reference Neo4j driver types. Callers open short-lived sessions through the infrastructure session provider and dispose those sessions after each operation.

The Neo4j readiness check opens a read session and runs `RETURN 1 AS healthy`. This checks configuration binding, authentication, network connectivity, database selection, and Cypher execution without requiring schema or architecture data.

## Schema initialization

A **graph schema** is the set of constraints and indexes that make graph writes safe and queryable. A **constraint** is a rule enforced by Neo4j, such as “repository stable keys must be unique.” An **index** is a lookup structure that helps later queries find records efficiently by stable key, snapshot scope, kind, status, confidence, knowledge classification, or fingerprint.

Schema initialization is exposed through the application-layer `IArchitectureGraphInitializer` port and implemented by `Neo4jGraphInitializer` in `Archon.Infrastructure.Neo4j`. This preserves Onion Architecture: application and host code can ask for graph initialization without depending on Neo4j driver types, while the infrastructure adapter owns the Cypher statements.

Initialization is **idempotent**, meaning it can run repeatedly against the same database without failing merely because expected constraints and indexes already exist. That property is important for local development, CI, and future startup flows because contributors should be able to verify or repair schema state without manually deleting the database.

The initialized graph uses stable labels such as `ArchonRepository`, `ArchonSolution`, `ArchonSnapshot`, `ArchonNode`, `ArchonRelationship`, `ArchonEvidence`, `ArchonRule`, `ArchonFinding`, `ArchonMetric`, and `ArchonGeneratedSummary`. Constraint and index names use stable `archon_` prefixes, such as `archon_repository_stable_key_unique` and `archon_node_kind_index`. Stable names matter because Neo4j Browser and `SHOW CONSTRAINTS` / `SHOW INDEXES` output can be compared directly with the source catalog in `Neo4jSchemaStatementCatalog`.

## Guarded graph recreation

Graph recreation is a separate maintenance workflow exposed through the application-layer `IArchitectureGraphRecreator` port and implemented by `Neo4jGraphRecreator`. **Graph recreation** means deleting Archon-owned graph records and then recreating the required constraints and indexes.

Recreation is destructive by design and exists only for local development and automated integration tests where a clean Archon graph is needed quickly. It is not a schema migration, not a data repair operation, and not a production management endpoint. A migration changes stored data or schema from one version to another while preserving intended information. Recreation intentionally discards Archon graph records, so it must never substitute for future migration planning.

The recreation guard is deliberately explicit. Callers must provide the exact confirmation phrase `DELETE ARCHON GRAPH DATA AND RECREATE SCHEMA` through `GraphRecreationRequest`. Near misses, different casing, missing words, and trailing spaces are rejected before a write session opens. Resolving `IArchitectureGraphRecreator` from dependency injection is not enough to erase data, and no API route, health check, snapshot writer, or startup hook invokes it in the current implementation.

When authorized, recreation deletes nodes carrying Archon-owned labels from the closed schema catalog and uses `DETACH DELETE` so relationships attached to deleted nodes are removed in the same clear operation. The label list comes from `Neo4jSchemaNames`; contributors must not extend this workflow with caller-provided labels or ad hoc dynamic Cypher. After clearing data, recreation runs the same idempotent initializer so the database remains ready for persistence tests.

## Snapshot persistence

Snapshot persistence is exposed through the application-layer `IArchitectureSnapshotWriter` port and implemented by `Neo4jArchitectureSnapshotWriter`. **Snapshot persistence** means taking an assembled `ExtractedArchitectureSnapshot` from the application layer and writing its graph facts to Neo4j using stable logical identities.

The current writer persists repositories, solutions, one snapshot header, architecture nodes, architecture relationship records, evidence nodes, versioned rule catalog entries, finding records, metric records, generated-summary records, snapshot-to-solution relationships, node-to-evidence support relationships, relationship endpoint relationships, relationship-to-evidence support relationships, finding-to-rule relationships, finding-to-primary-node relationships, finding-to-evidence support relationships, metric support relationships, and generated-summary target relationships.

The write order matters because graph records reference one another. Repositories and solutions are written first, then the snapshot header, architecture nodes, canonical evidence, architecture relationship nodes, global rule catalog entries, snapshot-scoped findings, metrics, generated summaries, and finally supporting relationships. The writer initializes schema before the write transaction and then uses a single Neo4j write transaction for supported snapshot data. If the transaction fails, the writer does not return a successful completed result for partial graph data.

Stable keys remain the only logical identities used by the writer. Repository, solution, and snapshot records are merged by global stable keys. Architecture nodes and evidence are merged by snapshot scope plus stable key. Neo4j internal IDs are never surfaced in persistence results, tests, API-facing values, or documentation examples.

## Normalized properties and metadata JSON

The writer preserves the distinction between normalized graph properties and metadata. **Normalized properties** are fields the platform expects to query, compare, or validate directly, such as `stableKey`, `snapshotStableKey`, `nodeKind`, `evidenceKind`, `knowledgeKind`, `confidence`, `hasUnknownData`, and `fingerprint`. **Metadata JSON** is reserved for deterministic extension data that does not belong in those first-class fields.

For example, a project node's `nodeKind` is stored as a Neo4j property because later queries may filter all `Project` nodes. Extractor-specific details that are not part of the shared graph contract remain in `metadataJson`.

## Evidence deduplication

Evidence persistence is snapshot-scoped and deduplicated. **Evidence deduplication** means equivalent evidence payloads submitted more than once in the same snapshot collapse to one canonical `ArchonEvidence` node. The canonical identity includes the snapshot stable key and evidence content fields but intentionally excludes the evidence stable key itself.

Node, relationship, finding, and metric support relationships are remapped to canonical evidence records. Because snapshot stable key is part of the deduplication identity, equivalent evidence in a different snapshot remains separate. This preserves historical snapshot isolation while avoiding duplicate evidence records within one snapshot.

## Architecture relationships and the relationship-node pattern

Architecture relationships are persisted through the `ArchonRelationship` label. An **architecture relationship** is the domain fact that one architecture node relates to another: a project references a package, a service calls an endpoint, a component uses a view model, or a repository artifact depends on configuration.

Archon uses a **relationship-node pattern**. Direct Neo4j relationships are useful for simple traversal, but Archon relationships are evidence-backed records with their own stable identity. Neo4j does not let a relationship have outgoing relationships to evidence nodes in the same way a node can. Archon therefore stores each edge as an `ArchonRelationship` node, connects it to the source architecture node with `RELATIONSHIP_SOURCE`, connects it to the target architecture node with `RELATIONSHIP_TARGET`, and connects it to primary evidence with `SUPPORTED_BY_EVIDENCE` when evidence is supplied.

For example, a project-to-package dependency is stored as an `ArchonRelationship` node whose `edgeKind` might be `REFERENCES` or `USES_PACKAGE`. The relationship node points to the project through `RELATIONSHIP_SOURCE`, points to the package through `RELATIONSHIP_TARGET`, and points to project-file evidence through `SUPPORTED_BY_EVIDENCE`. Multiple facts can connect the same source and target because merge identity is the relationship stable key within the snapshot, not the endpoint pair.

## Rules and findings

Rule and finding persistence adds a durable analysis-result layer to the graph. A **rule catalog entry** is a versioned definition identified by `ruleCode` plus `ruleVersion`, such as `ARCHON001` at version `1.0.0`. Rule catalog entries are global `ArchonRule` nodes rather than snapshot-scoped copies. When a rule changes from version `1.0.0` to `2.0.0`, Archon can keep both versions and findings from older snapshots can still point to the exact rule version that classified them.

A **finding** is a snapshot-scoped concern stored as an `ArchonFinding` node. It preserves the finding stable key, rule code, rule version, severity, status, title, description, knowledge classification, confidence, unknown-state fields, first-seen and latest-seen snapshot keys, suppression reason, suppressed-by actor, deterministic metadata JSON, and fingerprint.

Finding links make provenance and evidence explicit. Every persisted finding receives `CLASSIFIED_BY_RULE` to the matching global rule version. When a primary architecture node is supplied, the finding receives `PRIMARY_NODE`. When primary evidence is supplied, the finding receives `SUPPORTED_BY_EVIDENCE` to canonical evidence. Missing rule, node, or evidence references are rejected with explicit persistence errors before a write transaction opens.

## Metrics and generated summaries

A **metric** is a durable `ArchonMetric` node that stores a computed value for a graph, snapshot, project, architecture node, architecture relationship, or modernization scope. Metrics carry metric kind, scope kind, optional node and relationship target stable keys, optional primary evidence, a developer-facing name, numeric value, text value, unit, deterministic metadata JSON, and fingerprint. Persisting metrics lets later diff, API, MCP, report, and hotlist packages retrieve stable historical values instead of recomputing them during every read.

A **generated summary** is a durable `ArchonGeneratedSummary` node containing generated narrative or report-ready content. It stores summary kind, optional target stable key, format, title, content, deterministic metadata JSON, and fingerprint. Generated summaries always link to the owning snapshot through `SUMMARIZES_SNAPSHOT`; when a summary has a target stable key, it links to the target graph record through `PRIMARY_NODE` or `PRIMARY_RELATIONSHIP`.

## Integrated persistence mental model

WP003 provides persistence of already-assembled graph contracts. It does not decide how extraction finds facts, how rules are loaded from disk, how rules evaluate findings, how summaries are generated, how metrics are computed, or how API and MCP consumers query the graph. Those responsibilities belong to later work packages.

When reviewing or troubleshooting a WP003 graph, start with stable identities and support paths rather than Neo4j internal IDs. Locate repositories, solutions, snapshots, architecture nodes, relationship nodes, evidence records, findings, metrics, and summaries by stable key and, where relevant, snapshot stable key. Locate rules by rule code plus version. Compare fingerprints for diff-oriented investigations. Traverse support relationships for explainability: `CLASSIFIED_BY_RULE`, `SUPPORTED_BY_EVIDENCE`, `RELATIONSHIP_SOURCE`, `RELATIONSHIP_TARGET`, `PRIMARY_NODE`, `PRIMARY_RELATIONSHIP`, and `SUMMARIZES_SNAPSHOT`.
