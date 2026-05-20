# Prompt: SPEC Family / RESEARCH Phase (Baseline Extraction from Existing Solution)

## Objective
Create initial (draft) specification documents that accurately describe the current state of the existing solution WITHOUT proposing changes.

## Scope Coverage
Include:
- Solution/project structure (.NET9, Blazor, Aspire usage)
- Architecture layers (UI, API, shared, infra, mocks, workers/functions)
- Technology stack components
- Domain entities & relationships
- Public API surface (routes, verbs, purpose, auth, models)
- Blazor UI components (pages, shared components, navigation, state patterns)
- Cross-cutting concerns (DI, logging, error handling, caching, configuration, serialization, security, accessibility)
- Infrastructure & deployment descriptors (AZD, Bicep, hosting models)
- Testing status (existing test project layout & patterns)

## Required Inputs (gather via inspection)
1. List of projects (names, paths, target frameworks).
2. Namespaces & folders reflecting domains/features.
3. Minimal API or controller endpoints summary.
4. Reusable services & interfaces.
5. Blazor component catalog (Pages, Shared, Layouts, Forms, Results).
6. Shared models / DTOs / contracts.
7. Configuration & environment management approach.
8. Serialization strategy (source-generated contexts?).
9. Error handling patterns (middleware, boundaries).
10. Authentication/authorization mechanisms.
11. Theming/styling approach (Bootstrap + CSS variables).
12. Testing coverage indicators (presence of tests per area).

## Output Documents (initial draft versions)
Create the following spec files as applicable:
- `spec-system-overview_v0.01.md`
- `spec-architecture-components_v0.01.md` (if system overview would become too large)
- `spec-domain-[context]_v0.01.md` (one per domain context if needed)
- `spec-api-functional_v0.01.md`
- `spec-frontend-functional_v0.01.md`
- `spec-infra-deployment_v0.01.md` (if infra files exist)

If a section lacks implementation evidence, include the heading with "No current implementation" or "Unverified".

## Document Structure Template (apply to each)
1. Title
2. Version: v0.01 (Draft)
3. Status: Draft / Baseline Extraction
4. Supersedes: None
5. Change Log: Initial draft creation
6. Scope / Purpose
7. Context & Overview
8. Components / Modules
9. Detailed Elements (domain entities, endpoints, components, infra items)
10. Cross-Cutting Concerns
11. Non-Functional Characteristics (performance, scalability, security, accessibility, reliability)
12. Gaps & Unknowns
13. Future Indicators (only existing TODOs/placeholders; no invention)
14. Traceability (source paths, related docs)

## Rules & Constraints
- Do NOT invent future features.
- Use concise bullet lists; avoid redundancy.
- Reference relative paths (e.g., `src/Shell/UKHO.ADDS.Management/`).
- Mark uncertain areas as `(Unverified)`.
- Maintain consistent terminology across docs.
- Ensure every item from high-level instruction files is either confirmed or marked as gap.

## Gap & Risk Enumeration
List:
- Missing tests for critical services/components.
- Lack of documentation for APIs/components.
- Security/auth unclear or absent.
- Inconsistent naming/style patterns.

## Completion Checklist
- All planned spec files created under `docs/specs/`.
- Naming & versioning rules followed.
- Each spec includes required sections.
- Cross-references added between specs where appropriate.
- Gaps clearly identified.

## Follow-On Planning Suggestions
After specs: propose plan document stubs, e.g.:
- `plan-backend-refactor-auth_v0.01.md`
- `plan-frontend-accessibility_v0.01.md`
- `plan-tests-coverage-improvement_v0.01.md`

---
Responder Instructions: Use workspace exploration tools to collect real data before writing specs. Produce files iteratively starting with system overview.
