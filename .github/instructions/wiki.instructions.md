# Copilot Instructions: Wiki Maintenance

## Purpose
This instruction file defines the repository's mandatory wiki-maintenance workflow.

The goal is to keep the repository wiki aligned with the current codebase, contributor workflows, architecture, runtime behaviour, and repository operating model. The wiki is not optional background material. It is part of the developer experience and must be reviewed for every work package.

This file exists to make that responsibility explicit for both human contributors and GitHub Copilot. When a work package changes how the system works, how contributors reason about the system, or how contributors perform meaningful tasks, the wiki must either be updated or the absence of a needed update must be recorded explicitly.

## Ownership and Responsibility
Wiki maintenance is a shared repository responsibility.

- GitHub Copilot is responsible for performing the wiki review step during planning and execution whenever this workflow is used.
- Human contributors remain responsible for ensuring the repository's published guidance is accurate before work is considered complete.
- Reviewers should treat missing or stale wiki updates as a completeness issue, not as optional polish.

## Relationship to `documentation-pass.instructions.md`
This instruction file and `./.github/instructions/documentation-pass.instructions.md` serve related but different purposes.

- `./.github/instructions/wiki.instructions.md` is mandatory for every work package, including documentation-only work, prompt updates, instruction updates, and source-code work.
- `./.github/instructions/documentation-pass.instructions.md` remains the hard gate for any task that creates, updates, reviews, or plans source-code documentation work.
- Wiki maintenance is therefore broader in trigger scope, while documentation-pass is stricter in how source-code documentation must be delivered and validated.
- Neither instruction weakens the other. If both apply, both must be followed in full.

## Mandatory Triggers
A wiki review is mandatory for every work package.

A wiki update is required whenever the work package changes or materially clarifies any of the following:

- developer-facing behaviour
- architecture, layering, boundaries, or runtime composition
- setup steps, operational flows, commands, or troubleshooting paths
- workflow-heavy areas such as ingestion, orchestration, Workbench usage, or extension points
- technical concepts that new contributors must understand in order to work effectively
- terminology, naming, or conceptual boundaries used across the repository
- repository instructions or prompts whose behaviour affects how contributors plan, execute, document, or validate work

A wiki update may also be required when the implementation does not change runtime behaviour but does change how the repository should be understood. For example, a refactoring that preserves behaviour may still require refreshed architecture guidance if the explanation of responsibility boundaries has changed.

## Current-State Rules
The wiki must describe the repository as it exists now.

- Prefer present-tense, current-state guidance.
- Remove or rewrite phase-oriented, roadmap-oriented, or transitional wording unless historical context is explicitly useful and clearly labelled as such.
- Do not leave readers guessing whether a page describes current behaviour, an older phase, or a future aspiration.
- Preserve valid existing information where possible, but reorganize or rewrite it when the current structure obscures understanding.
- If a topic has changed meaning over time, explain the current meaning first and only then provide carefully scoped historical context if that context materially helps contributors.

## Narrative Structure Standards
The repository wiki must read like a technical book for contributors rather than a pile of disconnected checklists.

### Core expectation
For architecture, runtime foundations, workflow explanations, setup guidance, extension guidance, and other conceptually dense topics, the default standard is developed narrative prose with meaningful depth.

That means:

- explain what the thing is before explaining how to operate or change it
- explain why the design exists, not only what files or commands are involved
- define technical terms the first time they are introduced, or link directly to a glossary entry that does so
- connect each page to the wider repository story so readers understand how one concept leads into the next
- preserve important rationale and trade-offs instead of collapsing pages into short bullet lists
- include relevant examples, scenarios, or walkthrough fragments when they improve comprehension

### Explicit rejection of shallow treatment
Terse bullet-heavy treatment is not acceptable for foundational topics.

Pages about architecture, runtime foundations, ingestion flows, Workbench structure, setup journeys, extension models, or other workflow-heavy concepts must not be reduced to thin landing pages followed by sparse reference bullets. Those subjects require longer-form prose that explains intent, sequence, responsibilities, boundaries, and vocabulary.

### Technical-term handling
Whenever a term is not obvious to a new contributor, explain it.

Examples include terms such as "runtime foundation," "composition root," "contribution point," "dead-letter flow," or any repository-specific naming. The explanation can be written inline, linked to a glossary, or both, but the reader must not be expected to infer specialized meaning without help.

### Practical examples
Examples should be included when they materially improve comprehension.

Examples can include:

- a short narrative scenario explaining a typical contributor task
- a worked command sequence
- a guided walkthrough of how data or control moves through a subsystem
- a comparison between two similar concepts that are easy to confuse

Examples should clarify the main prose, not replace it.

## Walkthrough Standards
Where a topic is dense, pair conceptual overview pages with practical walkthrough content.

- Use overview pages to explain the mental model, architecture, vocabulary, and rationale.
- Use walkthrough pages to explain how a contributor performs the work in practice.
- Link overview and walkthrough pages in both directions so readers can move between concept-first and task-first reading paths.
- Ensure walkthroughs still explain what the reader is seeing. A walkthrough is not just a command dump.
- When a workflow contains decision points, explain why a contributor would choose one path over another.

## Mermaid Standards
Mermaid diagrams are optional, but when they are used they must earn their place.

- Only add or keep a diagram when it materially improves understanding.
- Diagrams must be GitHub-renderable and simple enough to read in the repository viewer.
- Keep labels clear and current-state.
- Ensure the surrounding prose explains how to read the diagram and what conclusions the reader should draw from it.
- Do not use a diagram as a substitute for narrative explanation.

## Maintenance Workflow
Follow this workflow for every work package.

1. Identify which developer-facing concepts, workflows, architectural boundaries, or repository behaviours the work package changes or clarifies.
2. Review the relevant wiki pages, appendix pages, glossary entries, and reader paths that might now be stale or incomplete.
3. Decide whether an update is required.
4. If an update is required, make the update before considering the work package complete.
5. If no update is required, record that decision explicitly with a short explanation grounded in the actual review you performed.
6. Ensure the final work-package record states what was updated, created, retired, or intentionally left unchanged.

This workflow is a completion gate. Do not skip it because the code change feels small, because the repository already has documentation, or because a prompt or instruction change appears "internal." Internal repository workflow changes can still alter how contributors must work and therefore can still require documentation attention.

## Reporting Requirements
The outcome of the mandatory wiki review must be recorded explicitly.

At minimum, the execution record or final summary must state one of the following:

- which wiki pages were updated, created, split, renamed, retired, or left unchanged as part of the work package, or
- that no wiki page update was necessary, together with a brief explanation of what was reviewed and why the existing wiki remained sufficient

Acceptable reporting examples:

- "Wiki review result: Updated `wiki/Project-Setup.md` and `wiki/Setup-Troubleshooting.md` to reflect the new services-mode startup flow and the revised container prerequisites."
- "Wiki review result: No wiki page update was required. Reviewed `wiki/Glossary.md` and `wiki/Workbench-Introduction.md`; the implemented prompt-only change affects repository workflow enforcement but does not change contributor-facing product behaviour beyond the instruction files updated in this work item."

Unacceptable reporting examples:

- "Wiki reviewed."
- "No wiki changes."
- "Documentation updated if needed."

## Practical Examples
### Example 1: Runtime architecture change
If a work package introduces a new background processing stage, queue, or module boundary, update the relevant architecture and workflow pages. Do not merely add the stage name to a list. Explain what the new stage does, why it exists, how it changes the flow, and any new terminology a contributor must understand.

### Example 2: Setup or tooling change
If the startup command sequence changes, preserve exact commands where syntax matters, but also explain when a contributor should use each command, what success looks like, and what common failure modes mean.

### Example 3: Instruction or prompt change
If repository prompts or instructions change how contributors are expected to plan, execute, validate, or document work, review whether the wiki or other repository-facing guidance needs a corresponding update. Even if no wiki page changes are needed, record the review outcome explicitly.

## Final Standard
A work package is not fully complete until the mandatory wiki review has been performed, the necessary wiki or repository guidance updates have been made, and the outcome has been recorded clearly.
