# Copilot Instructions: Documentation

## Scope
Guidelines for authoring and maintaining specifications, plans, API docs, and component docs.

## Work Package documentation

### Work Package location
- Create a single Work Package folder under `./docs/` for all outputs.
- Folder naming: `xxx-<descriptor>` where `xxx` is the next incremental number (e.g. `001`, `002`, ...).
- Store the overview spec, component/service specs, plans, and architecture notes inside the same Work Package folder.

### Collaboration pattern (spec.research)
- Separate spec per service/component; overview references each.

## Specifications (Requirements)
- Filename pattern: `spec-<scope>-<descriptor>.md`.
- During collaborative specification work, update the same spec file in place rather than creating versioned copies.
- Keep a Change Log section in the document when useful to record notable decisions or direction changes.
- Preserve `(Unverified)` markers where evidence not yet collected.

## Modules
Within a Work Package folder:
- Module specs should be grouped logically (optional) under `modules/<module-name>/`.
- Required initial file: `spec-domain-<module-name>.md` capturing purpose, scope, gaps.
- Optional per-module specs: `spec-api-<module-name>.md`, `spec-frontend-<module-name>.md`.

## Plans (Implementation / Execution)
- Store plans under the Work Package folder (recommended: `<work-package>/plans/<area>/`).
- Filename pattern: `plan-<area>-<purpose>_vX.XX.md`.
- Each plan references source spec versions: `Based on: spec-api-functional_v1.2.md`.
- Include Baseline (current implemented), Delta (planned changes), Carry-over (incomplete / deferred items).
- Use Work Item / Task / Step hierarchy from plan prompt.
- Archive policy may be extended to plans (currently enforced for specs only).

Plans (extended note):
- Plans follow similar versioning and should live under the Work Package folder.
- If archiving of plans is adopted, mirror the Work Package `archive/` pattern.

## Workflow (Authoring Sequence)
1. Inspect codebase / gather evidence.
2. Create or update the current spec file in place.
3. Generate or update plan referencing current specs.
4. Implement code changes; update docs in same feature branch.
5. Merge with branch checks ensuring spec & plan consistency.

## Validation Checklist
- Correct Work Package folder placement (`docs/xxx-<descriptor>/...`).
- Filename matches the non-versioned spec naming pattern.
- Overview spec references only current component/module spec files.
- Plans reference current spec files and contain Baseline/Delta/Carry-over.

## Documentation Maintainability
- Avoid duplication; reference canonical spec rather than copying text.
- Keep API examples synchronized with implementation.
- Treat documentation updates as part of Definition of Done for each change set.

## Validation
- Preserve `(Unverified)` markers for unevidenced areas.

## File Naming Summary
- Spec: `spec-<scope>-<descriptor>.md`
- Module spec: `spec-<domain|api|frontend>-<module>.md`
- Plan: `plan-<area>-<purpose>_vM.NN.md`

End of File.
