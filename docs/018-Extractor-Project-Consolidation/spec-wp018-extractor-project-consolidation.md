# WP018 Specification - Extractor Project Consolidation and Work-Package Naming Cleanup

## Document Control

| Field | Value |
| --- | --- |
| Work Package | WP018 - Extractor Project Consolidation and Work-Package Naming Cleanup |
| Output Path | `docs/018-Extractor-Project-Consolidation/spec-wp018-extractor-project-consolidation.md` |
| Source Brief | User request to consolidate extractor projects, remove work-package identifiers from type names, and tidy logging messages. |
| Specification Basis | `spec-template_v1.1.md` requested; template file was not present in the workspace at creation time, so this document follows the repository specification structure and documented work-package rules. |
| Status | Draft |
| Audience | Product owner, architect, implementation team, test engineer |

## 1. Overview

### 1.1 Purpose

This specification defines the requirements and migration strategy for consolidating the Archon extractor project estate into a smaller, clearer project structure. The work package removes delivery-history terminology from production type names and runtime log messages while preserving conceptual separation through namespaces, folders, tests, and existing Onion Architecture boundaries.

### 1.2 Background

The current extractor area contains multiple projects named in the pattern `Archon.Extractors.XXX`, with corresponding test projects. This creates project sprawl for closely related extractor capabilities and makes solution navigation more cumbersome than necessary. Some production types also include work-package identifiers such as `Wp009` in class names, and some runtime logging text refers to work packages as `WPxxx`.

Work-package identifiers are useful for planning, sequencing, and historical documentation, but they should not leak into production API shape, type names, namespaces, test naming, or operational logs. Runtime code should be named after the architecture concept or behavior it implements.

### 1.3 High-Level Scope

WP018 covers:

- Consolidating all production `Archon.Extractors.XXX` projects into a single `Archon.Extractors` project.
- Preserving extractor categories as folders and namespaces below `Archon.Extractors`, such as `Archon.Extractors.DataAccess`.
- Consolidating all extractor test projects into a single `Archon.Extractors.Tests` project.
- Preserving test categories as folders and namespaces below `Archon.Extractors.Tests`.
- Deleting extractor projects that become empty after consolidation.
- Removing work-package identifiers from production type names, test type names where appropriate, and related references.
- Rephrasing log messages that contain work-package identifiers such as `WP009` so that logs describe runtime behavior rather than delivery history.
- Updating solution, project references, documentation, and validation expectations needed for the new structure.

WP018 does not change extractor behavior, extraction facts, graph persistence behavior, API contracts, MCP behavior, or the work-package documentation history.

## 2. System Context

### 2.1 Product Context

Archon is a .NET architecture intelligence platform that extracts deterministic, evidence-backed facts from repositories and persists architecture knowledge for API and MCP consumption. Extractors are a core implementation area, but their project layout should support maintainability without exposing implementation chronology to callers or operators.

The consolidated extractor structure should make the solution easier to navigate while still keeping extractor responsibilities clear by folder, namespace, and test organization.

### 2.2 Source References

WP018 must align with these repository instructions and architectural expectations:

- `.github/copilot-instructions.md` for repository-wide work-package, coding, testing, and architecture rules.
- `.github/instructions/documentation.instructions.md` for work-package documentation location and specification file naming.
- `.github/instructions/coding-standards.instructions.md` for C# style expectations where implementation later occurs.
- Existing extractor projects and test projects under `./src` and `./test` respectively. `(Unverified)`
- Existing solution file `Archon.slnx`. `(Unverified)`

### 2.3 Users and Stakeholders

| Stakeholder | Interest |
| --- | --- |
| Product owner | Confirms the cleanup improves maintainability without altering intended extractor capability. |
| Architect | Confirms project consolidation preserves Onion Architecture and clear responsibility boundaries. |
| Developer | Gains a simpler extractor project structure and clearer type names. |
| Test engineer | Confirms tests move with their related extractor areas and continue validating the same behavior. |
| Operator or maintainer | Receives runtime log messages that describe meaningful behavior rather than work-package history. |

## 3. Component Summary

### 3.1 Consolidated Extractor Production Project

`Archon.Extractors` is the target production project for all extractor implementations currently split across `Archon.Extractors.XXX` projects. Category-specific code remains separated by folder and namespace.

Example target layout:

```plaintext
src/
  Archon.Extractors/
	DataAccess/
	Configuration/
	DependencyInjection/
	Projects/
	AspNet/
	Ui/
```

Example target namespace:

```plaintext
Archon.Extractors.DataAccess
```

### 3.2 Consolidated Extractor Test Project

`Archon.Extractors.Tests` is the target test project for all tests currently split across extractor-specific test projects. Category-specific tests remain separated by folder and namespace.

Example target layout:

```plaintext
test/
  Archon.Extractors.Tests/
	DataAccess/
	Configuration/
	DependencyInjection/
	Projects/
	AspNet/
	Ui/
```

### 3.3 Solution and Reference Updates

`Archon.slnx`, project references, package references, and test references must be updated so that the consolidated production and test projects are the active extractor projects. Old extractor projects must be removed only after their files, references, and solution entries have been migrated.

### 3.4 Naming Cleanup

Production and test types should use domain and architecture concept names, not work-package identifiers. For example, `Wp009DataAccessExtractionStage` should become `DataAccessExtractionStage`.

### 3.5 Logging Cleanup

Log messages must describe runtime behavior and operational context. Messages should not identify the work package that introduced the behavior.

Example direction:

| Avoid | Prefer |
| --- | --- |
| `WP009 data access extraction started.` | `Data access extraction started.` |
| `WP009 extraction completed for {ProjectPath}.` | `Data access extraction completed for {ProjectPath}.` |
| `WP009 found no data access patterns.` | `No data access patterns were found in {ProjectPath}.` |

## 4. Functional Requirements

### 4.1 Extractor Production Project Consolidation

| ID | Requirement |
| --- | --- |
| FR-001 | The solution shall contain a single active production extractor project named `Archon.Extractors`. |
| FR-002 | All source files from existing `Archon.Extractors.XXX` projects shall be moved into category folders under `src/Archon.Extractors`. |
| FR-003 | Category folders shall preserve the existing extractor concept names, such as `DataAccess`, `Configuration`, `DependencyInjection`, `Projects`, `AspNet`, and UI technology categories where present. `(Unverified)` |
| FR-004 | Namespaces shall preserve conceptual separation below `Archon.Extractors`, for example `Archon.Extractors.DataAccess`. |
| FR-005 | The consolidated project shall include the package references required by the moved extractor code. |
| FR-006 | Duplicate package references and duplicate analyzer or build settings from the old extractor projects shall be normalized into the consolidated project. |
| FR-007 | The consolidation shall not change extractor runtime behavior or extracted fact semantics. |

### 4.2 Extractor Test Project Consolidation

| ID | Requirement |
| --- | --- |
| FR-008 | The solution shall contain a single active extractor test project named `Archon.Extractors.Tests`. |
| FR-009 | All source files from existing `Archon.Extractors.XXX.Tests` projects shall be moved into category folders under `test/Archon.Extractors.Tests`. |
| FR-010 | Test namespaces shall preserve conceptual separation below `Archon.Extractors.Tests`, for example `Archon.Extractors.Tests.DataAccess`. |
| FR-011 | The consolidated test project shall reference `Archon.Extractors` and any other production or shared test projects required by the moved tests. |
| FR-012 | Duplicate test package references and duplicate test infrastructure settings shall be normalized into the consolidated test project. |
| FR-013 | Existing extractor test intent and coverage shall be preserved after consolidation. |

### 4.3 Deletion of Old Extractor Projects

| ID | Requirement |
| --- | --- |
| FR-014 | Old `Archon.Extractors.XXX` production project files shall be deleted only after their source files have been migrated. |
| FR-015 | Old `Archon.Extractors.XXX.Tests` project files shall be deleted only after their test files have been migrated. |
| FR-016 | Empty old extractor project directories shall be removed unless they contain documentation or non-project assets that must be preserved. |
| FR-017 | `Archon.slnx` shall no longer include deleted extractor production or test projects. |
| FR-018 | No remaining project shall reference a deleted extractor project. |
| FR-018A | Deletion of old extractor project directories shall use an explicit, reviewed allow-list of exact absolute paths and shall never use a wildcard, glob, prefix match, recursive pattern, or inferred directory enumeration that can match `src/Archon.Extractors` or `test/Archon.Extractors.Tests`. |
| FR-018B | The allow-list shall explicitly exclude and protect `src/Archon.Extractors` and `test/Archon.Extractors.Tests`; an implementation that cannot prove those paths are excluded shall stop before deleting anything. |
| FR-018C | Before any delete command runs, the executor shall perform and record a dry-run inventory showing every path that would be deleted and shall verify each listed path is an obsolete category project directory rather than a consolidated target directory. |
| FR-018D | Old directories shall be removed one exact path at a time after verification, not through a single broad recursive delete command over a wildcard result set. |
| FR-018E | If a proposed deletion list contains any path named exactly `Archon.Extractors` or `Archon.Extractors.Tests`, the deletion step is invalid and shall not proceed. |

### 4.4 Work-Package Identifier Removal from Code Symbols

| ID | Requirement |
| --- | --- |
| FR-019 | Production type names shall not include work-package prefixes or identifiers such as `Wp009`, `WP009`, or similar `WpXXX` forms. |
| FR-020 | `Wp009DataAccessExtractionStage` shall be renamed to `DataAccessExtractionStage`. |
| FR-021 | Other extractor types with work-package identifiers shall be renamed to behavior-focused names. |
| FR-022 | Constructor names, references, tests, dependency-injection registrations, and any reflection-sensitive usage shall be updated to match renamed types. |
| FR-023 | Test type names shall not include work-package identifiers unless the test explicitly validates a work-package documentation artifact. |
| FR-024 | File names shall match renamed C# type names where repository conventions require one public type per file. |

### 4.5 Logging Message Cleanup

| ID | Requirement |
| --- | --- |
| FR-025 | Runtime log messages shall not refer to implementation work packages as `WPxxx`, `WpXXX`, or equivalent delivery-history labels. |
| FR-026 | Log messages shall describe the runtime behavior, operation, extractor category, and relevant context. |
| FR-027 | Data access extractor logs shall refer to data access extraction behavior rather than `WP009`. |
| FR-028 | Rephrased logs shall preserve useful structured logging placeholders. |
| FR-029 | Rephrased logs shall not remove error context, project path context, snapshot context, or other operational diagnostics. |

### 4.6 Documentation Alignment

| ID | Requirement |
| --- | --- |
| FR-030 | Contributor-facing documentation that describes the current extractor project layout shall be updated to describe the consolidated structure. |
| FR-031 | Historical work-package documentation may continue to mention previous work-package identifiers when describing delivery history. |
| FR-032 | Current-state guidance shall not instruct contributors to add new extractor projects for each extractor category. |
| FR-033 | Current-state guidance shall explain that new extractor categories belong under `Archon.Extractors/<Category>` and `Archon.Extractors.Tests/<Category>`. |

## 5. Non-Functional Requirements

### 5.1 Maintainability

| ID | Requirement |
| --- | --- |
| NFR-001 | The extractor estate shall be easier to navigate after consolidation than before consolidation. |
| NFR-002 | Category-specific extractor responsibilities shall remain clear through folders, namespaces, and tests. |
| NFR-003 | Project names shall describe deployable or reusable assemblies rather than fine-grained implementation slices where those slices are better represented as namespaces. |
| NFR-004 | Work-package history shall not leak into production symbol names or operational messaging. |

### 5.2 Buildability

| ID | Requirement |
| --- | --- |
| NFR-005 | The solution shall build successfully after the consolidation. |
| NFR-006 | The consolidated extractor production project shall compile without missing references introduced by project deletion. |
| NFR-007 | The consolidated extractor test project shall compile without missing references introduced by project deletion. |
| NFR-008 | Project reference cycles shall not be introduced. |

### 5.3 Architecture

| ID | Requirement |
| --- | --- |
| NFR-009 | The consolidation shall preserve Onion Architecture dependency direction. |
| NFR-010 | Domain projects shall not gain references to extractor, infrastructure, API, or host projects. |
| NFR-011 | Service or application-layer projects shall not gain references that violate existing architecture rules. |
| NFR-012 | Host projects may reference the consolidated extractor project only where existing composition behavior already requires extractor access. `(Unverified)` |

### 5.4 Observability

| ID | Requirement |
| --- | --- |
| NFR-013 | Logging cleanup shall improve log meaning for operators and maintainers. |
| NFR-014 | Log message templates shall remain stable enough for existing tests or dashboards unless those tests or dashboards also refer to work-package terminology. `(Unverified)` |

## 6. Migration Strategy

### 6.1 Inventory

The implementation should begin by inventorying:

- All production projects named `Archon.Extractors.XXX`.
- All test projects named `Archon.Extractors.XXX.Tests`.
- All references to those projects in `Archon.slnx` and `.csproj` files.
- All package references, project references, analyzer settings, and build properties in the old extractor projects.
- All symbols and file names containing work-package identifiers such as `Wp009` or `WP009`.
- All log messages containing work-package identifiers.

### 6.2 Production Consolidation

Production extractor files should be moved into `src/Archon.Extractors/<Category>/`. Existing namespaces should be updated to the `Archon.Extractors.<Category>` pattern. The consolidated `Archon.Extractors.csproj` should receive required package references and project references from the old extractor projects, normalized to avoid duplication.

### 6.3 Test Consolidation

Extractor tests should be moved into `test/Archon.Extractors.Tests/<Category>/`. Test namespaces should follow `Archon.Extractors.Tests.<Category>`. The consolidated `Archon.Extractors.Tests.csproj` should reference `Archon.Extractors` and preserve required test packages and shared test dependencies.

### 6.4 Symbol Rename

Type renames should be handled as behavior-preserving refactors. The implementation should update filenames, constructors, references, using directives, dependency-injection registrations, tests, and any reflection-based references. Special attention should be paid to code that discovers extraction stages by type name or assembly name.

### 6.5 Logging Cleanup

Log messages should be rewritten in place, preserving structured logging placeholders and severity levels. The message text should communicate the operation being performed, such as data access extraction, configuration extraction, dependency injection extraction, or project extraction.

### 6.6 Project Deletion

Old extractor project files and directories should be deleted after successful migration and reference updates. Solution entries and project references should be removed as part of the same implementation pass.

Project deletion is a destructive operation and therefore requires a strict safety gate. The executor must build an explicit allow-list of exact obsolete category project directories from the verified inventory, review it against the consolidated target paths, and perform a dry run before deleting anything. The consolidated production directory `src/Archon.Extractors` and consolidated test directory `test/Archon.Extractors.Tests` are protected paths. They must never appear in a deletion list, and any deletion mechanism that could match them is prohibited.

The implementation must not use commands or code shaped like `Remove-Item src/Archon.Extractors*`, `Remove-Item src/Archon.Extractors.*`, `Get-ChildItem -Filter 'Archon.Extractors*' | Remove-Item`, or equivalent wildcard, glob, prefix, or broad recursive deletion patterns. The difference between `Archon.Extractors` and `Archon.Extractors.*` is too small to rely on pattern behavior during destructive cleanup. Deletion must instead be path-by-path against the reviewed allow-list, with the executor checking each path name before removal.

The required deletion sequence is:

1. List every old category project directory intended for removal by exact absolute path.
2. List the protected consolidated directories explicitly: `src/Archon.Extractors` and `test/Archon.Extractors.Tests`.
3. Confirm the intended removal list contains only obsolete directories with names such as `Archon.Extractors.DataAccess` or `Archon.Extractors.DataAccess.Tests` and does not contain the protected consolidated directories.
4. Confirm each intended removal directory contains no unmigrated source, tests, documentation, resources, or other assets that must be preserved.
5. Remove each verified obsolete directory by exact path.
6. Immediately confirm the protected consolidated directories still exist before making any further cleanup changes.

If any of these checks fail, the executor shall stop deletion and fix the inventory or plan before continuing. Recovery after deleting a consolidated target directory is not an acceptable normal workflow and shall not be treated as a substitute for safe deletion.

## 7. Implementation Considerations

### 7.1 Assembly Discovery Risk

The largest technical risk is assembly-based discovery. If extractor registration or execution discovers extractors by scanning assemblies named `Archon.Extractors.DataAccess`, `Archon.Extractors.Configuration`, or similar, consolidation will change assembly identity. The implementation must identify and update such discovery behavior.

Acceptable mitigations include:

- Updating discovery to scan the consolidated `Archon.Extractors` assembly.
- Making extractor registration explicit through a single public entry point.
- Preserving category-level selection through namespaces, marker types, or metadata rather than separate assemblies.

### 7.2 Internals Visibility Risk

If old extractor projects use `InternalsVisibleTo` for their corresponding test projects, those declarations must be updated for `Archon.Extractors.Tests` or removed if no longer needed.

### 7.3 Package Reference Normalization Risk

Old extractor projects may have different package versions or category-specific dependencies. The consolidated project must normalize package references without accidentally removing dependencies required by a subset of extractors.

### 7.4 Test Selection Risk

Visual Studio Test Explorer, test filters, and CI scripts may refer to old test project names. The implementation should update repository-owned scripts or documentation that reference old extractor test projects. External CI configuration is `(Unverified)`.

### 7.5 Documentation History

Historical work-package documents should not be rewritten merely to erase valid delivery history. Current-state documentation and contributor guidance should be updated where it describes active project layout.

### 7.6 Destructive Cleanup Safety Risk

The most severe operational risk in this work package is accidental deletion of the consolidated extractor project while removing old category projects. This can happen if a broad wildcard or prefix-based delete command treats the target directory and obsolete category directories as the same deletion set. The mitigation is mandatory: destructive cleanup must be explicit, reviewed, dry-run first, and path-by-path.

The protected paths are:

- `src/Archon.Extractors`
- `test/Archon.Extractors.Tests`

Any plan, command, script, or manual action that cannot demonstrate these paths are protected is non-compliant with this specification. A successful build after deletion is not enough to compensate for unsafe cleanup because the mistake can destroy uncommitted migrated code before validation begins.

## 8. Acceptance Criteria

| ID | Criterion |
| --- | --- |
| AC-001 | `src/Archon.Extractors` contains the consolidated production extractor code. |
| AC-002 | `test/Archon.Extractors.Tests` contains the consolidated extractor tests. |
| AC-003 | No active `Archon.Extractors.XXX` production project remains in the solution. |
| AC-004 | No active `Archon.Extractors.XXX.Tests` test project remains in the solution. |
| AC-005 | Deleted extractor projects are not referenced by any remaining project. |
| AC-006 | Production extractor type names do not contain work-package identifiers. |
| AC-007 | `Wp009DataAccessExtractionStage` is renamed to `DataAccessExtractionStage`. |
| AC-008 | Runtime log messages do not refer to work packages as `WPxxx` or equivalent delivery-history labels. |
| AC-009 | Extractor behavior remains functionally equivalent after consolidation. |
| AC-010 | Targeted extractor tests pass after consolidation. |
| AC-011 | The solution builds successfully after consolidation. |
| AC-012 | Current-state documentation reflects the consolidated extractor project structure where applicable. |
| AC-013 | The deletion record proves old extractor directories were removed from an explicit allow-list, that the protected consolidated directories were excluded before deletion, and that `src/Archon.Extractors` and `test/Archon.Extractors.Tests` still existed immediately after deletion. |

## 9. Validation Requirements

The implementation plan for WP018 should validate at least:

1. The solution builds successfully.
2. The consolidated extractor test project builds successfully.
3. Targeted extractor tests pass.
4. Project references no longer mention deleted extractor projects.
5. A search for production symbol names containing `Wp` plus a work-package number finds no remaining extractor production types.
6. A search for log message text containing `WP` plus a work-package number finds no remaining runtime log messages.
7. Architecture-boundary tests continue to pass if present.
8. The deletion safety record shows the exact obsolete paths deleted, the protected paths checked, and the post-deletion existence check for `src/Archon.Extractors` and `test/Archon.Extractors.Tests`.

The repository instruction says not to run the full test suite for this work package unless later guidance supersedes that instruction.

## 10. Decisions

| ID | Question | Decision |
| --- | --- | --- |
| D-001 | Are any extractor categories intentionally intended to remain separate assemblies for plugin loading or optional deployment? | No. Extractor categories are not intended to be independently deployed or plugin-loaded assemblies at this stage. They shall be consolidated into `Archon.Extractors`, with category separation preserved by folder, namespace, and registration metadata where needed. |
| D-002 | Should non-extractor projects with work-package identifiers in type names be cleaned in this work package? | Limit WP018 to extractor-related production code, extractor tests, and directly coupled API or composition references required by the consolidation. Non-extractor work-package naming cleanup shall be handled by a separate work package unless required to complete this migration safely. |
| D-003 | Should historical work-package specs be updated to mention the new consolidated structure? | No. Historical work-package specifications shall not be rewritten to erase valid delivery history. Only current-state contributor guidance, architecture documentation, and active implementation guidance shall be updated to describe the consolidated extractor structure. |
| D-004 | Are any dashboards, saved log queries, or tests dependent on the old `WPxxx` log message text? | Repository-owned tests or assertions that depend on `WPxxx` log message text shall be updated to the new operational wording. External dashboards, saved queries, or alerts are unverified and shall not block the cleanup. Structured logging placeholders and diagnostic context must be preserved to reduce downstream impact. |
| D-005 | How shall obsolete extractor directories be deleted safely? | Delete them only from an explicit reviewed allow-list of exact paths. Wildcard, glob, prefix, broad recursive, or inferred deletion sets are prohibited because they can delete `src/Archon.Extractors` or `test/Archon.Extractors.Tests`. The implementation must dry-run, record the deletion list, protect the consolidated paths, delete path-by-path, and immediately verify the protected directories remain. |

## 11. Change Log

| Date | Change |
| --- | --- |
| 2026-05-28 | Initial draft created from extractor consolidation strategy discussion. |
| 2026-05-28 | Recorded decisions for the initial open questions. |
| 2026-05-28 | Added mandatory destructive-cleanup safety requirements, protected-path rules, dry-run deletion sequence, validation criteria, and decision record to prevent accidental deletion of consolidated extractor projects. |
