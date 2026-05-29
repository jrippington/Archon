# Copilot Instructions (High-Level)

You are an agent. Continue working until queries are fully resolved.  
Be concise but complete. Prefer current research (Microsoft Learn) for Microsoft technologies.

## Unalterable Work-Item Completion Requirement

This section is an unalterable execution requirement, not a guideline, preference, or quick principle. It overrides any weaker wording elsewhere.

- When asked to implement, execute, continue, or complete any implementation-plan or plan-driven work item, you MUST keep working autonomously until that entire active work item is fully complete. This is mandatory and has no optional interpretation. A step announcement, progress update, partial implementation, test run, build result, wiki edit, documentation edit, plan-record update, or summary of intermediate work is never a stopping point and must never be treated as a handoff to the user. After every status/progress message, immediately continue to the next required action in the same response flow.
- During an active work item, you MUST complete every required task and step in sequence, including investigation, implementation, test creation, defect fixes, build/test validation, mandatory documentation pass, mandatory wiki review, required wiki edits, plan-record updates, final validation, and the final completion report. Do not pause, wait, ask what to do next, ask for confirmation, or return control to the user between tasks or steps. If tests fail, fix them and rerun them. If the build fails, fix it and rerun it. If documentation or wiki review reveals required edits, make them and continue. If the plan record needs updating, update it and continue.
- The only permitted reasons to stop before full active-work-item completion are: (1) the user explicitly interrupts or changes direction, (2) the work item is fully complete and the required final completion message has been provided, or (3) a true blocker makes further autonomous progress impossible. A true blocker is narrowly limited to missing required information that cannot be inferred from the specification, plan, codebase, or existing repository guidance; an unrecoverable tool or environment failure after reasonable retry or fallback; or an external permission, secret, or resource requirement that cannot be satisfied inside the workspace. Routine uncertainty, ordinary defects, failing tests, build errors, unclear next steps, missing imports, documentation updates, wiki updates, plan updates, or validation reruns are not blockers and MUST be resolved without stopping.
- Do not ask for confirmation before continuing from one task or step to the next inside an active work item. Ask at most one concise clarification question only for a true blocker. Otherwise infer reasonable defaults from the current plan, specs, code, and repository instructions and continue until the active work item is done.

## Quick Principles
- Verify each command succeeds before proceeding; run commands sequentially.
- Prefer latest C#/.NET features; async/await; nullable reference types.
- Always use block-scoped namespaces (e.g., `namespace X.Y { ... }`) rather than file-scoped namespaces when creating or updating C# files in this workspace.
- Do not use top-level statements anywhere in this repository; always use explicit Program/AppHost classes and methods for executable entry points.
- Use Allman braces style for C# code.
- Add `//` comments on their own line for non-obvious logic.
- Do not interact with git (no branch creation, no git commands) unless explicitly requested.
- When adding or modifying code in this repo, always follow `.github/instructions/coding-standards.instructions.md`: Allman braces, block-scoped namespaces, one public type per file, and underscore-prefixed private fields. Double-check new files for these conventions before finishing.
- In this workspace/PowerShell environment, do not use the `rg` (ripgrep) command; assume it isn't available.
- Avoid clutter in the repository root by placing per-project config files alongside the relevant test or project directories when practical.
- Do not run Stryker again in this workspace, and remove all Stryker-related configuration/setup files when asked.
- Ask open clarification questions one at a time rather than batching multiple questions together.
- Never write log files or other temporary files to the repo root; always use suitable temporary storage.
- Do not run the Aspire AppHost for smoke testing; ask the user to perform the Aspire smoke test after completing the work item.
- For UI styling in this repository, prefer dense desktop workbench layouts with rails/toolbars/split panes/canvas or tables over card-heavy web-page-style visual treatment; avoid cards and avoid custom font-size tweaks unless explicitly requested.
- Do not introduce custom theme colors unless the user explicitly asks for them; prefer standard theme tokens when styling UI.
- ArchonExplorer will only be used through the Aspire dashboard; prioritize Aspire-hosted runtime behavior over direct standalone Vite usage when reasoning about local operation.
- ArchonExplorer UI work packages must not deviate from standard UI toolkit coloring, text sizing, or control styling unless the user explicitly asks for that deviation.

## Project Structure
- In this repository, production projects should be placed under `./src`, test projects under `./test`, and every production project should have a corresponding test project.
- Use `.slnx` solution folders with 'xx' numeric prefixes to reflect Onion structure.
- In the Archon repository, use the existing `Archon.slnx` solution file for work package implementation; the solution is an Aspire solution with an Aspire orchestration project named Archon.

## Documentation Workflow (Summary)
- For each new Work Package/piece of work: create a new numbered folder under `./docs/` named `xxx-<descriptor>` (e.g. `001-Initial-Shell`).
- Store work-package planning artifacts, such as specs and implementation plans, together inside that Work Package folder.
- For Archon work package specs, use the same format and location pattern as the existing numbered docs work packages; do not ask about spec format/location when the pattern is already present.
- Do not create standalone implementation notes, implementation ledgers, architecture notes, or similar narrative records for contributor-facing implementation detail. Current-state contributor guidance, design rationale, validation workflows, setup guidance, architecture explanation, and terminology belong in `./wiki` under `.github/instructions/wiki.instructions.md`.
- Work-package plan updates may record concise completion history and validation outcomes, but they must not become a parallel source of contributor guidance. If the content teaches contributors how the repository works, move that content into the wiki and link to the relevant wiki page from the plan.
- Do not overwrite prior work packages; create the next incremental folder (e.g. `002-...`).
- When asked to create specification documents for a work package, create only one document containing everything needed; do not split across multiple documents. If multiple were created, merge into one and delete the extras.
- For API routes in specs, inspect the existing ArchonApi implementation rather than relying only on roadmap examples.
- Use appropriate prompt family & phase from `.github/prompts/`.
- When asking open questions from a spec, record each answer directly in that same spec file and do not create a new version.
- When collaborating on specifications in this repository, do not repeat the draft spec in chat before clarification questions; ask the next question directly and keep the evolving draft in the spec file instead. If remaining questions are about look and feel only, use sensible defaults and revisit later instead of continuing to ask those presentation questions.
- When documentation references repository wiki pages, prefer proper markdown links rather than inline code-formatted URLs or plain page names.
- Repository documentation standards should be captured in `.github/instructions/documentation-pass.instructions.md` and referenced as a non-negotiable requirement from planning and execution prompts so they are enforced in every coding task.
- Documentation should prefer book-like narrative depth over terse, bullet-heavy wiki pages, especially for core architecture, runtime foundations, and other critical concepts. This preference should be reflected in repository instructions and prompts.
- When executing Archon work package plan/spec creation, continue autonomously through all planned steps until the requested document is finished; do not stop after step announcements or for status-only updates.

## Logging Standards
- Prefer using `ILogger` abstractions (Microsoft.Extensions.Logging.Abstractions) over `Action<string>` logging callbacks in this codebase, including Domain pipeline nodes.

## Coding Standards
- Never declare multiple classes/interfaces/enums in the same C# file; split each type into its own file. Enforce the standard of one public type per C# file.

## Architecture (Onion)
This repository uses **Onion Architecture**.

Dependency direction (must point inward):  
`Hosts (Web/Worker) -> Infrastructure -> Services -> Domain`

Rules:
- Domain projects must not reference Services, Infrastructure, or Host projects.
- Services projects must not reference Infrastructure or Host projects.
- Infrastructure projects must not reference Host projects.
- Only Host projects contain UI/endpoints and startup/DI wiring. Do not place domain logic or infrastructure implementations in hosts.
- For ingestion, keep queue/client wiring in `UKHO.Search.Infrastructure.Ingestion`, but place file-share-specific pipeline nodes (parsing/enrichment of file-share data) in `UKHO.Search.Ingestion.Providers.FileShare` (provider project).
- Prefer a single, obvious public entrypoint for queue-backed ingestion; avoid multiple builder APIs. Document this in the spec and keep code aligned (hosted service should start ingestion via the adapter/provider entrypoint path).
- For the ingestion rules DSL, support both `if` and `match` as predicate field aliases, but prefer writing examples using `if`.
- For the WorkbenchHost architecture discussions, include runtime menu contributions and status bar contributions from tools in the preferred lightweight slice.
- For Workbench architecture in this repository: put module/tool-accessible contracts and models in `UKHO.Workbench`; services in `UKHO.Workbench.Services`; infrastructure in `UKHO.Workbench.Infrastructure`; and overall composition/UI in `WorkbenchHost`.

## Archon Infrastructure Guidelines
- For Archon stable project identity, normalized project file paths must be made relative to the repository root directory so identities are deterministic across different developer machine locations.
- Use Aspire SDK 13.3.3 for Work Package 001 implementation in the Archon workspace, unless later repository guidance supersedes this.

## MCP Tool Selection
- Azure DevOps intent: use Azure DevOps tools.
- GitHub intent: use GitHub tools.
- Microsoft tech (Blazor, ASP.NET Core, Azure, .NET, Aspire): use Microsoft Learn tools.

## Testing Guidelines
- Prefer Playwright end-to-end tests over bUnit/component tests for Blazor UI verification in this repository.
- For test refactor work, it is acceptable for test projects to reference other test projects when that preserves Onion Architecture direction; broad shared test-wide helpers, such as fixture resolution helpers, should live in `UKHO.Search.Tests.Common` when they are reused throughout the test estate.
- For this work package, do not run the full test suite.

## .csproj File Editing Guidelines
- When editing `.csproj` files, keep `PackageReference` entries in `ItemGroup` blocks that contain only `PackageReference` entries (do not mix `ProjectReference` and `PackageReference` in the same `ItemGroup`).

## API Documentation Standards
- In this repository, do not use Swagger UI for API documentation; use Scalar instead.
- For Archon WP005 API specifications, prefer explicit snapshot selection via route paths rather than query-string snapshotId parameters.
- For Archon WP005 API specifications, do not use a common '/api' route prefix.

## Detailed Topic Guides
Refer to specialized instruction files for full detail:
- Architecture: `.github/instructions/architecture.instructions.md`
- Frontend (Blazor/UI): `.github/instructions/frontend.instructions.md`
- Backend (APIs/Services): `.github/instructions/backend.instructions.md`
- Testing: `.github/instructions/testing.instructions.md`
- Documentation Authoring: `.github/instructions/documentation.instructions.md`
- Coding Standards: `.github/instructions/coding-standards.instructions.md`
