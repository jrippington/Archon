# Work-Package Documentation Workflow

Archon uses work-package specs and plans under `docs/` and current-state contributor guidance under `wiki/`. These two documentation areas serve different purposes. Specs and plans guide and record work. Wiki pages teach contributors how the repository currently works.

This page explains how to apply the repository wiki-maintenance rules after every work package. The detailed rule source is `.github/instructions/wiki.instructions.md`. Start from [home](home.md) for the overall wiki map, and use the [glossary](glossary.md) for shared terminology.

## Source-of-truth boundary

Work-package folders under `docs/` contain planning artifacts: specifications, implementation plans, concise completion history, validation outcomes, and final wiki review records. They should be useful for reconstructing what happened during a work package, but they must not become the main place contributors learn current architecture, runtime behavior, setup, validation, or terminology.

The wiki is the contributor-facing source of truth. If a work package changes how the system works, how contributors reason about the system, or how contributors perform meaningful tasks, the relevant wiki pages must be updated before the work package is complete.

## Required wiki impact matrix

Every final work-package record should include a concrete wiki impact matrix. It does not need to be large, but it must be specific enough for a reviewer to see that structure and content were considered.

Use this shape:

| Item | Result |
| --- | --- |
| Affected concepts | List architecture, runtime, setup, workflow, terminology, or contributor guidance affected by the work. |
| Pages reviewed | List exact wiki pages reviewed. |
| Pages updated | List exact wiki pages updated, or `None`. |
| Pages created | List exact wiki pages created, or `None`. |
| Pages retired or renamed | List exact wiki pages retired or renamed, or `None`. |
| Pages intentionally unchanged | List pages left unchanged and why they remained sufficient. |
| Structure decision | Explain why the content belongs on the selected page or why a new page was needed. |

A vague statement such as “wiki reviewed” or “no wiki changes” is not enough.

## Page selection rules

Do not use `wiki/home.md` as a catch-all page. `home.md` is a landing page and table of contents. It should orient readers and link to topic pages, not carry full architecture, runtime, domain, persistence, setup, or workflow explanations.

Use or create topic pages based on the reader's need:

- Use [solution architecture](solution-architecture.md) for Onion Architecture, project families, dependency boundaries, and project identity.
- Use [runtime foundation](runtime-foundation.md) for service defaults, probes, AppHost composition, local runtime resources, and manual runtime verification.
- Use [graph domain model](graph-domain-model.md) for controlled values, stable keys, metadata, fingerprints, evidence-first graph facts, confidence, unknown state, and accumulation.
- Use [Neo4j persistence foundation](neo4j-persistence-foundation.md) for Neo4j configuration, schema, recreation, snapshot writing, relationship-node persistence, and persisted analysis outputs.
- Use [validation and test workflows](validation-and-test-workflows.md) for build, test, Testcontainers, boundary validation, and manual-only AppHost workflow commands.
- Use [glossary](glossary.md) for shared terminology.

If none of the existing pages fits, create a new topic page. A new page is preferable to making an existing page mix unrelated concepts.

## Overview and walkthrough pairing

Dense topics often need both a conceptual explanation and a practical workflow. The overview page should teach the mental model, vocabulary, and rationale. The workflow page should show how a contributor performs the task in practice.

For example, [Neo4j persistence foundation](neo4j-persistence-foundation.md) explains graph schema, recreation, snapshot persistence, and relationship-node modeling. [Validation and test workflows](validation-and-test-workflows.md) provides the commands that prove those behaviors. Both pages link to each other so contributors can move between concept-first and task-first reading.

## What to do after each work package

Follow this sequence before marking a work package complete:

1. Identify affected contributor-facing concepts and workflows.
2. Review the relevant wiki topic pages, the glossary, and any workflow pages.
3. Decide whether each affected concept needs a new page, an existing page update, or no wiki change.
4. Update topic pages in current-state prose, not roadmap language.
5. Add or update glossary terms for specialized vocabulary.
6. Add or update walkthrough material when commands or decision points changed.
7. Ensure pages link to each other in useful reader paths.
8. Record the wiki impact matrix in the work-package plan or final execution record.

## Prohibited substitute artifacts

Do not create standalone implementation notes, implementation ledgers, architecture notes, or similar narrative records for contributor-facing detail. If the material teaches contributors how Archon works, how to validate it, how to troubleshoot it, or what terminology means, it belongs in the wiki.

Plans may retain concise historical status, files touched, validation commands, and explicit wiki review outcomes. They must link to wiki pages for contributor-facing explanations instead of duplicating those explanations.

## Good completion examples

A good final record is specific:

> Wiki review result: Updated `wiki/neo4j-persistence-foundation.md` and `wiki/validation-and-test-workflows.md` for snapshot relationship persistence. Added `relationship-node pattern` to `wiki/glossary.md`. Reviewed `wiki/solution-architecture.md`; no update was needed because dependency boundaries did not change.

Another good result for a prompt-only change is:

> Wiki review result: Updated `wiki/work-package-documentation-workflow.md` because the execution prompt now requires a wiki impact matrix and page-structure assessment after every work package. Reviewed `wiki/home.md`; no update was needed because the landing page already links to the documentation workflow page.

Both examples state what changed, what was reviewed, and why pages were left unchanged.
