# Archon Wiki

Archon is a .NET architecture-intelligence platform. The current foundation provides independently runnable API and MCP hosts, Aspire-based local composition, a domain model for deterministic architecture graph facts, and a Neo4j persistence foundation for storing assembled snapshots. Extraction orchestration, query APIs, MCP tools, markdown export, and user-interface behavior remain assigned to later work packages.

This wiki is the contributor-facing source of truth for current behavior, terminology, validation workflows, and repository operating model. Work-package specs and plans under `docs/` explain how work is planned and recorded; the wiki explains how contributors should understand and work with the repository now.

## Recommended reading paths

### New contributor orientation

Start here if you need the mental model before working on code:

1. [Solution architecture](solution-architecture.md) explains project families, Onion Architecture, dependency boundaries, and project identity.
2. [Runtime foundation](runtime-foundation.md) explains service defaults, health probes, the Aspire AppHost, and local Neo4j composition.
3. [Graph domain model](graph-domain-model.md) explains controlled values, stable keys, graph facts, evidence, confidence, unknown state, and snapshot accumulation.
4. [Neo4j persistence foundation](neo4j-persistence-foundation.md) explains schema initialization, guarded recreation, snapshot persistence, relationship nodes, and persisted analysis outputs.

### Build and validation path

Use [validation and test workflows](validation-and-test-workflows.md) when you need commands for restore, build, targeted tests, Testcontainers-based Neo4j validation, boundary checks, or manual AppHost verification.

### Documentation and wiki maintenance path

Use [work-package documentation workflow](work-package-documentation-workflow.md) when planning, executing, or reviewing a work package. It explains how specs, plans, wiki updates, glossary entries, page-structure decisions, and final wiki review records fit together.

### Terminology lookup

Use the [glossary](glossary.md) for repository-specific terms such as AppHost, composition root, stable key, fingerprint, evidence-first, relationship-node pattern, readiness, liveness, and Testcontainers.

## Current capability summary

The API host and MCP host currently expose operational probes only. The Aspire AppHost composes Neo4j, `ArchonApi`, and `ArchonMcp` for local development and intentionally does not compose a Discovery UI resource. The graph domain model defines stable vocabulary and contracts for architecture facts, while the Neo4j infrastructure adapter persists supplied graph contracts after schema initialization. No host endpoint currently invokes snapshot persistence, and no query or MCP graph access behavior is complete yet.

## Wiki maintenance standard

Do not add detailed architecture, runtime, setup, domain, persistence, validation, or workflow guidance directly to this landing page. Add or update the relevant topic page instead, and link it from the appropriate reader path. If no existing topic page fits, create one.
