# Rule Catalog and Rule Engine

The rule catalog is the authored vocabulary Archon uses to describe modernization, lifecycle, security, dependency, configuration, data-access, and architecture concerns before those concerns become persisted findings. In the current WP012 foundation, Archon can load JSON rule files from copied runtime output, validate their contract and detection-language shape, return deterministic diagnostics, fail visibly when invalid built-in rule content is required by startup or extraction initialization, upsert validated catalog entries as versioned Neo4j rule records, evaluate enabled rules after the extraction pipeline has accumulated graph facts, construct deterministic snapshot-owned findings from matched rule context, persist findings with history and suppression fields, apply suppressions without deleting underlying findings, and expose controlled rule catalog plus hotlist/finding query APIs. The WP015 MCP host now projects rule catalog and hotlist context through read-only tools and resources described in [MCP tool reference](mcp-tool-reference.md); this page remains focused on authored rule behavior and finding production rather than duplicating MCP contracts.

Read this page after [graph domain model](graph-domain-model.md), because rules use graph vocabulary such as node kinds, controlled values, evidence, stable keys, confidence, and unknown state. Use [hotlist and findings](hotlist-and-findings.md) for the product query surface that exposes persisted rules and findings without unrestricted graph access. Use [validation and test workflows](validation-and-test-workflows.md) for the focused build and test commands that prove loader, evaluator, persistence, and API behavior. Repository-specific terms are also collected in the [glossary](glossary.md).

Reader path: [Home](home.md) -> [Graph domain model](graph-domain-model.md) -> Rule catalog and rule engine -> [Hotlist and findings](hotlist-and-findings.md) -> [Validation and test workflows](validation-and-test-workflows.md).

## Authoring source and runtime source

The repository-root `rules/` folder is the source location for authored built-in rule files. A rule file is data-only JSON. It must not depend on executable code, shell commands, SQL, Cypher, network calls, filesystem mutations, or application startup side effects. This distinction matters because Archon analyzes other applications and must keep rule authoring deterministic and reviewable. A reviewer should be able to inspect a rule file, understand what graph facts it targets, and reason about version history without running the target repository or trusting arbitrary rule code.

Runtime loading does not read the repository source folder by walking relative paths from the current working directory. Instead, projects that need rule content copy `rules/**/*.json` into their build and publish output under a `rules/` directory. The default loader path is `AppContext.BaseDirectory/rules`. This copied-output boundary keeps local tests, published deployments, and hosted startup behavior aligned: the process reads the same kind of content regardless of where the repository was cloned or whether the original source tree is available beside the executable.

When adding a project that needs to load rules, configure its project file to copy the repository rule JSON files into output and publish output. Keep `PackageReference` entries in package-only item groups and put content-copy items in their own item group so project-file structure remains consistent with repository standards. The loader also accepts an explicit runtime rule directory through options, which is useful for tests and controlled initialization seams, but production code should prefer copied output rather than hard-coded repository-root paths.

## Rule identity and core fields

A rule identity is the pair of `ruleCode` and `version`. The rule code is a stable human-readable identifier such as `ARCHON-LIFECYCLE-NETFRAMEWORK-UNSUPPORTED`; the version is a semantic version such as `1.0.0`. This pair is used because findings must remain explainable after a rule changes. If a rule changes materially, the author should create a new version rather than silently changing the meaning of historical findings.

Each rule file currently contains one rule object. Required fields include `ruleCode`, `name`, `category`, `severity`, `defaultStatus`, `enabled`, `version`, `description`, and `detection`. The category must be one of the domain rule categories, such as `Lifecycle`, `ObsoleteApi`, `LegacyTechnology`, `DataAccess`, `SecuritySensitive`, `Configuration`, `ArchitectureLayering`, `DependencyRisk`, `ModernizationBlocker`, or `OrganisationSpecific`. Severity values are `Critical`, `High`, `Medium`, `Low`, and `Info`. Rule default statuses use the WP012 catalog vocabulary: `OutOfSupport`, `Obsolete`, `Legacy`, `FrameworkOnly`, `MigrationBlocker`, `SecuritySensitive`, `Discouraged`, or `Unknown`.

The `enabled` flag controls evaluator availability, not catalog visibility. A disabled rule still loads and validates so the catalog can explain authored content and historical rule versions. The current evaluator selects only catalog entries whose availability flag is true. A disabled rule therefore remains visible in catalog loading tests and future catalog APIs, but it does not inspect graph facts or produce matched rule context.

Built-in rules should set `builtIn` to `true`. The loader provides a fail-fast path that throws a visible `RuleCatalogValidationException` when catalog validation fails. Hosts or extraction initialization code that require built-in rules should use that path so broken built-in content cannot be silently ignored. The exception message includes deterministic diagnostic codes and file context so the fix starts with the authored JSON file, not an opaque startup failure.

## Detection language shape

The current detection language is a validated boolean structure. A detection group can declare `nodeKinds`, a `match` mode, `conditions`, and nested `groups`. The `nodeKinds` array narrows candidate graph node kinds, using the same controlled values described in [graph domain model](graph-domain-model.md). The `match` value is `all`, `any`, or `none`; when omitted, it defaults to `all`. A group is invalid when it contains no conditions and no nested groups because an empty predicate would make later evaluation ambiguous.

Conditions currently validate vocabulary and payload shape. Supported condition kinds are `target-framework-membership`, `namespace`, `symbol`, `package`, `file-pattern`, `method-call`, `attribute`, and `metric-threshold`. Supported operators are `Equal`, `NotEqual`, `GreaterThan`, `GreaterThanOrEqual`, `LessThan`, `LessThanOrEqual`, `In`, `NotIn`, `Contains`, `StartsWith`, `EndsWith`, and `MatchesPattern`.

Most condition kinds operate over string-like graph or metadata values and therefore require either a `value` property or a non-empty `values` array. The `In` and `NotIn` operators require `values`. Numeric comparison operators are currently valid only for `metric-threshold` conditions, which must declare `metric`, `operator`, and numeric `value`. The loader validates these rules before evaluation so invalid rules cannot drift into runtime behavior and fail later in less obvious ways.

The current validator is schema-equivalent rather than a separate JSON schema file. That means the implementation enforces the required JSON shape, controlled values, version format, duplicate identity rules, group emptiness, condition-kind vocabulary, operator vocabulary, and operator/payload compatibility in code. If a future work item adds a standalone JSON Schema document, it should remain aligned with these same runtime validation rules instead of becoming a second source of truth.

## Evaluation model

The current evaluator is an application-layer rule engine foundation rather than a persistence writer. It accepts validated `RuleCatalogEntry` objects and a `RuleEvaluationGraph`, which is a fixture-friendly graph-fact read model containing candidate nodes, fact collections, evidence stable keys, confidence values, metrics, and explicit unknown reasons. This read model exists so boolean DSL behavior can be proven without Neo4j, API hosts, extractor implementations, Roslyn workspace loading, or the Aspire AppHost. Later slices can adapt persisted or assembled snapshot facts into the same evaluator seam instead of re-implementing predicate semantics in persistence or API layers.

Evaluation starts by sorting enabled rules by rule code and version, then sorting candidate nodes by stable key. The evaluator applies the root `nodeKinds` restriction before reading any condition facts. This ordering is important: a rule that targets `Project` nodes should not inspect method, package, endpoint, or file-path nodes merely because those nodes contain text that happens to match a condition. Nested groups can also declare `nodeKinds`; those nested filters are checked against the same candidate node before the nested operands are evaluated.

The evaluator treats a detection group's direct `conditions` and nested `groups` as one operand list. `match: all` requires every operand in that list to be true. `match: any` requires at least one operand to be true. `match: none` requires every operand to be false. These semantics are recursive, so a nested group returns a boolean result to its parent in exactly the same way that a direct condition does. For example, a rule can require a project to target `net48`, have either a `System.Web` namespace or an MVC package, and have no explicitly excluded marker symbol:

```json
{
  "nodeKinds": ["Project"],
  "match": "all",
  "conditions": [
	{ "kind": "target-framework-membership", "operator": "Equal", "value": "net48" }
  ],
  "groups": [
	{
	  "match": "any",
	  "conditions": [
		{ "kind": "namespace", "operator": "StartsWith", "value": "System.Web" },
		{ "kind": "package", "operator": "Equal", "value": "Microsoft.AspNet.Mvc" }
	  ]
	},
	{
	  "match": "none",
	  "conditions": [
		{ "kind": "symbol", "operator": "Equal", "value": "AllowedOnlyMarker" }
	  ]
	}
  ]
}
```

The evaluator maps each required condition kind to a specific fixture fact collection. `target-framework-membership` reads target framework monikers. `namespace`, `symbol`, `package`, `file-pattern`, `method-call`, and `attribute` read their corresponding string fact collections. `metric-threshold` reads a named decimal metric value from the candidate node. When a required fact collection is unavailable for a candidate, the evaluator records a warning instead of inventing a fact. If another branch of an `any` group still matches, the warning remains visible so contributors can see that evaluation was partial.

String comparisons use ordinal, case-sensitive behavior. `Equal`, `NotEqual`, `In`, `NotIn`, `Contains`, `StartsWith`, and `EndsWith` therefore do not use culture-sensitive comparison and do not silently lower-case values. `MatchesPattern` is intentionally a bounded wildcard comparison, not arbitrary regular-expression execution by rule authors. The wildcard pattern supports `*` and `?`, has a conservative maximum length, and runs through a short timeout after the evaluator escapes all other characters. Numeric metric comparisons use decimal comparison for `GreaterThan`, `GreaterThanOrEqual`, `LessThan`, and `LessThanOrEqual`.

Evaluation output is matched rule context rather than direct database writes. A match includes the exact rule code and version, the primary affected node stable key, affected node stable keys, matched condition evidence references, graph evidence stable keys already associated with the node, confidence input data, warnings, and unknown-state context. The finding construction service consumes that matched context and turns it into a persisted finding record without re-running predicate evaluation. This separation keeps predicate semantics, finding identity, and persistence responsibilities testable on their own.

Finding construction uses two related identities. The **finding stable key** is snapshot-scoped and includes the snapshot stable key, rule code, and a deterministic target discriminator derived from the matched rule version, primary node, affected nodes, and matched evidence references. The **finding history key** is cross-snapshot and deliberately excludes the snapshot stable key so the same logical rule/target concern can be recognized in later snapshots. This distinction lets Archon keep one durable finding record per snapshot while still answering history questions such as where a concern was first seen and most recently seen.

A persisted finding defaults its severity from the matched rule. Its current lifecycle status maps the WP012 rule-authored status into the existing graph status vocabulary: specific modernization statuses such as `OutOfSupport`, `Legacy`, `MigrationBlocker`, and `SecuritySensitive` are represented as open findings while the rule-authored status remains in deterministic metadata for later detail APIs. A rule-authored `Unknown` status maps to the graph `Unknown` status. Finding metadata also preserves matched evidence references, affected node stable keys, evidence stable keys, rule category, rule source URLs, rule tags, the finding history key, confidence inputs, and unknown reasons using stable lower camel case field names.

Finding confidence is derived from rule confidence, matched fact confidence, and unknown-state context. The current application formula multiplies rule confidence and fact confidence, then applies a small deterministic penalty for unknown contexts while keeping the value in the normalized zero-through-one confidence range. This is not a probabilistic guarantee; it is a stable explanation signal that lets contributors distinguish highly supported findings from findings whose supporting graph facts were partially degraded. Unknown-state context is explicit because Archon treats uncertainty as architecture information. A node can still match based on known facts while carrying an unknown reason that finding output preserves.

Suppression is a lifecycle overlay, not a delete operation. A suppression request targets the finding history key, rule code, rule version, and primary affected node stable key. It must include a reason and a suppressed-by identity so later readers can understand why the finding is intentionally hidden or accepted. When a suppression matches a finding, the finding status becomes `Suppressed`, the suppression reason and actor are persisted as first-class fields, and suppression metadata is retained in deterministic metadata. The original finding stable key, rule identity, affected node links, evidence links, fingerprint history, and first/latest seen behavior remain available for audit and future query APIs. If the same history key appears in a later snapshot, the stored suppression can be applied to the later finding as well.

## Extraction integration and catalog persistence

The extraction workflow now contains a WP012 rule stage with the stable identifier `wp012-rule-catalog-evaluation`. It runs after the project, semantic, configuration, runtime, data-access, external-integration, and UI/client stages have had an opportunity to contribute graph facts to the shared accumulator. This ordering matters because rule evaluation is intentionally a classifier over already-extracted graph facts. The rule stage does not load project files independently, does not run Roslyn independently, does not start the target application, and does not query Neo4j for arbitrary graph data before evaluating. It receives the same accumulated snapshot shape that later persistence receives and projects the fields needed by the evaluator into the smaller `RuleEvaluationGraph` read model.

The stage performs three operations in sequence. First, it loads the copied-output rule catalog through the same fail-fast validation path used by initialization scenarios. Missing folders, invalid JSON, duplicate rule identities, unsupported versions, and other catalog validation failures become controlled blocking extraction diagnostics rather than being ignored. Second, it upserts every validated rule through the application-layer `IRuleCatalogStore` port and contributes the corresponding graph `RuleDefinition` records to the accumulated snapshot. Third, it selects enabled rules and evaluates them against projected snapshot facts. Warnings from persistence, missing fact collections, unavailable metrics, or explicit unknown states are appended to extraction diagnostics so a completed run can still explain partial evaluation.

An **upsert** is a write that creates a record when it is absent and updates the existing record when the same logical identity already exists. For rules, that identity is the pair of rule code and rule version. Neo4j stores rule catalog entries as global `ArchonRule` nodes keyed by `ruleCode` and `ruleVersion`; they are not snapshot-scoped copies. Persisted rule properties include the rule code, rule version, name, category, severity, mapped default status, enabled flag, description, definition JSON, source URLs, built-in flag, owner scope, and deterministic metadata JSON. **Definition JSON** is the original validated rule document stored as JSON text so later catalog and finding readers can explain the exact authored payload that was evaluated.

Catalog persistence is non-destructive. Disabling a rule on disk updates or creates the versioned catalog record with `enabled = false`; it does not remove the rule record. Removing a rule file from the runtime folder during a later load does not delete the historical `ArchonRule` node. Loading a new version of an existing rule code creates a second versioned record instead of overwriting the earlier version. This historical fidelity is necessary because future findings will reference the exact rule code and version that classified them. A finding created by rule `ARCHON-X` version `1.0.0` must remain explainable after `ARCHON-X` version `1.1.0` changes detection behavior or after the file is removed from the active disk catalog.

The current extraction integration proves catalog persistence and match production, and the end-to-end validation path now composes that sequence with finding construction, finding persistence, controlled hotlist reads, finding detail/history reads, and suppression through application-layer seams. This matters because each part of WP012 has a different responsibility: extraction accumulates and classifies facts, finding construction turns matched context into durable identities, persistence stores the result, and the query layer returns DTOs rather than direct graph records. A contributor validating this flow should look for the same evidence in tests that a production run must preserve: rule code and version, severity, graph status, confidence, category metadata, affected-node references, evidence references, unknown-state reasons, first-seen/latest-seen snapshot keys, suppression state, and deterministic ordering. See [hotlist and findings](hotlist-and-findings.md) for the endpoint behavior, paging, filtering, redaction, and suppression request contract.

Rule files remain data only during evaluation. Condition values are treated as literal comparison data even when they look like shell commands, SQL, Cypher, URLs, file paths, or application code. The evaluator does not execute shell commands, does not run arbitrary SQL or Cypher, does not call the network, does not mutate the filesystem, and does not invoke target application code. This boundary is part of the rule authoring contract: authored JSON classifies extracted graph facts; it does not become an extension scripting environment.

## Deterministic diagnostics

A deterministic diagnostic is a stable, machine-readable report that gives authors the same code and path for the same invalid input. The loader emits diagnostics for missing folders, unreadable folders or files, JSON parse failures, missing required fields, invalid controlled values, invalid versions, missing detection blocks, empty groups, invalid match values, invalid node kinds, unsupported condition kinds, unsupported operators, incompatible operator/payload combinations, and duplicate rule identities.

Diagnostics include the file path when a file is involved, the JSON contract path when practical, and parse location details when `System.Text.Json` exposes line or byte-position context. Simple validation errors are aggregated for a file where practical. For example, a rule with an invalid category, invalid severity, invalid status, invalid version, invalid node kind, unsupported condition kind, and incompatible metric operator should return all of those author-fixable diagnostics in one load result rather than stopping after the first field.

The loader returns no valid catalog entries when any diagnostic is present in the loaded folder. This all-or-nothing behavior prevents a partially valid catalog from producing a misleading runtime where one broken built-in rule is ignored while other rules appear to work. Tests can still inspect diagnostic collections directly, and hosts can use the fail-fast helper when invalid catalog content should abort initialization.

## Built-in lifecycle rule example

The first built-in rule is `ARCHON-LIFECYCLE-NETFRAMEWORK-UNSUPPORTED` version `1.0.0`. It is a lifecycle rule with high severity and an `OutOfSupport` default status. Its detection group targets `Project` nodes and uses a `target-framework-membership` condition with the `In` operator against unsupported .NET Framework target framework monikers such as `net48`, `net472`, `net461`, and older values.

This rule does not scan project files by itself. It expects project target framework metadata to have been extracted into graph facts by earlier project inventory slices. That separation is intentional: extraction stages produce evidence-backed graph facts, and rules classify those facts. Keeping rules out of source scanning preserves the architecture boundary and allows later evaluation to work over persisted or assembled snapshots rather than re-reading arbitrary source files.

## Built-in first-cut catalog coverage

The built-in catalog now contains a first-cut set of data-only rules for the WP012 source-brief families. A **built-in rule** is repository-shipped JSON content with `builtIn` set to `true`; it is versioned like any other rule and is still loaded from copied runtime output rather than from a special compiled resource. The first-cut rules are intentionally grouped by detection family rather than split into hundreds of tiny files. For example, the classic ASP.NET rule covers Web Forms, Web Pages, MVC 3 through 5, Web API 2, `System.Web`, `Global.asax`, HTTP modules, and HTTP handlers because those signals all describe the same migration concern: a `System.Web` application surface that cannot be treated like modern ASP.NET Core hosting.

The current built-in families cover retired target frameworks, .NET Standard-only migration blockers, classic ASP.NET technology, .NET Framework-only runtime technologies, relational data-access patterns, obsolete API evidence, security-sensitive API or configuration locations, configuration and hosting blockers, legacy dependency packages, and first-cut architecture-smell indicators. Each rule includes impact statements, evidence requirements, recommended actions, tags, owner scope, source URLs where useful, and lower camel case metadata that records the coverage terms represented by the grouped rule. Those metadata coverage terms are not a separate rule language; they are traceability for contributors and tests so a reader can see why a scenario such as `wcfServer`, `binaryFormatter`, `packagesConfig`, or `highFanIn` is covered by a particular JSON file.

Representative authoring follows the same boundary as evaluation. Lifecycle rules inspect `target-framework-membership` facts. Legacy technology rules combine package, namespace, symbol, attribute, method-call, and file-pattern facts that earlier extraction slices can produce. Architecture-smell rules use `metric-threshold` conditions only where metric facts are expected to exist and also include symbol-based indicators for extracted smells such as `ServiceLocator.Current` or `CircularProjectDependency`. When a metric is missing, the evaluator records a partial-evaluation warning rather than fabricating a value. This warning behavior is part of the design: architecture smells often depend on computed graph metrics, and the absence of a metric is useful uncertainty rather than permission to guess.

Security-sensitive rules deserve special care. They should report the existence and location of sensitive evidence, not the secret value itself. The built-in security rule therefore targets safe indicators such as `ConnectionStringLocation`, `SecretCandidateLocation`, legacy serializer type names, weak cryptography type names, or configuration file paths. Extractors and tests should provide evidence stable keys that point to a safe location, such as `evidence://configuration/app-config/connection-string-location`, instead of putting raw passwords, tokens, connection strings, or key material in rule values, matched evidence references, diagnostics, logs, metadata, or API output. The hotlist query layer also applies metadata redaction as a final boundary, but rule authors should treat that as defense in depth rather than the first redaction step.

When adding a new built-in scenario, first decide which graph facts already exist. If the required evidence would require live application execution, direct source scanning by the rule engine, arbitrary SQL or Cypher, network calls, or filesystem mutation, the scenario is not ready for rule authoring in this catalog. Add or extend an extraction slice instead, then write the rule against the extracted fact. Keep the rule code stable, use a deterministic file name, update the metadata coverage list, add a representative fixture in `BuiltInRuleCatalogTests`, and run the built-in catalog validation workflow. This keeps contributor-facing coverage, runtime validation, and tests aligned without creating standalone implementation notes.

## Validation workflow

When rule catalog loading behavior changes, run the targeted application tests first:

```powershell
dotnet test .\test\Archon.Application.Tests\Archon.Application.Tests.csproj --filter FullyQualifiedName~RuleCatalogLoaderTests
```

When boolean DSL evaluation behavior changes, run the evaluator tests as well:

```powershell
dotnet test .\test\Archon.Application.Tests\Archon.Application.Tests.csproj --filter FullyQualifiedName~RuleEvaluatorTests
```

When built-in rule JSON, first-cut coverage metadata, representative fixtures, or security-sensitive redaction expectations change, run the built-in catalog tests:

```powershell
dotnet test .\test\Archon.Application.Tests\Archon.Application.Tests.csproj --filter FullyQualifiedName~BuiltInRuleCatalogTests
```

When extraction integration, rule catalog persistence, or Neo4j rule upsert behavior changes, run the targeted integration and persistence tests:

```powershell
dotnet test .\test\Archon.Application.Tests\Archon.Application.Tests.csproj --filter FullyQualifiedName~RuleExtractionIntegrationServiceTests
dotnet test .\test\Archon.Infrastructure.Neo4j.Tests\Archon.Infrastructure.Neo4j.Tests.csproj --filter RuleCatalog
dotnet test .\test\Archon.Api.Extraction.Tests\Archon.Api.Extraction.Tests.csproj --filter "FullyQualifiedName~AddArchonExtractionApi|FullyQualifiedName~RuleEvaluation"
```

When finding construction, history, suppression, or finding persistence behavior changes, run the targeted finding tests:

```powershell
dotnet test .\test\Archon.Application.Tests\Archon.Application.Tests.csproj --filter FullyQualifiedName~FindingConstructionServiceTests
dotnet test .\test\Archon.Infrastructure.Neo4j.Tests\Archon.Infrastructure.Neo4j.Tests.csproj --filter "FullyQualifiedName~Neo4jRuleCatalogStoreTests|FullyQualifiedName~Neo4jServiceCollectionExtensionsTests"
```

When rule catalog query, hotlist, finding detail, finding history, suppression endpoint, pagination, ordering, response-size limit, or redaction behavior changes, run the targeted query API tests:

```powershell
dotnet test .\test\Archon.Api.Query.Tests\Archon.Api.Query.Tests.csproj --filter "FullyQualifiedName~QueryEndpointTests|FullyQualifiedName~ArchonApiQueryProjectReferenceTests"
dotnet test .\test\Archon.Infrastructure.Neo4j.Tests\Archon.Infrastructure.Neo4j.Tests.csproj --filter "FullyQualifiedName~Neo4jRuleCatalogStoreTests|FullyQualifiedName~Neo4jServiceCollectionExtensionsTests"
```

Then run the integrated solution build gate:

```powershell
dotnet build .\Archon.slnx --no-restore
```

These commands do not start the Aspire AppHost and do not call query APIs. The Neo4j rule catalog tests use the repository's existing Testcontainers path when integration coverage is selected, so Docker or another OCI-compatible runtime must be available for that targeted persistence test. The loader tests prove copied-output loading, missing-folder diagnostics, parse diagnostics, required-field and controlled-value validation, version validation, empty-group validation, unsupported condition and operator diagnostics, operator compatibility checks, duplicate identity detection, disabled-rule availability, and visible invalid-catalog failure. The evaluator tests prove enabled-rule selection, disabled-rule skipping, candidate node-kind filtering, recursive `all`, `any`, and `none` behavior, required condition-kind mapping, supported operator behavior, deterministic output ordering, explicit unknown context, partial-evaluation warnings, and data-only safety boundaries. The extraction and persistence tests prove the load-upsert-evaluate sequence, code/version upsert identity, idempotency, new-version coexistence, disabled-rule persistence, removed-on-disk non-deletion, and API pipeline stage composition. The finding tests prove deterministic finding stable keys, cross-snapshot history keys, fingerprints, affected-node links, evidence links, metadata, confidence derivation, unknown preservation, first-seen/latest-seen behavior, in-memory persistence seams, suppression validation, suppression carry-forward, Neo4j finding Cypher identity, stable relationship links, and dependency-injection registration.

## Current boundaries and future slices

The rule catalog loader, persistence seam, Neo4j catalog store, extraction integration stage, evaluator, finding construction service, finding store port, in-memory finding store, Neo4j finding store, query service, query store, HTTP query endpoints, and read-only MCP projections are now enough to prove rule authoring, validation, versioned catalog durability, deterministic predicate behavior, deterministic finding identity, finding history, suppression behavior, and controlled product query output against persisted data. The product query surface is intentionally narrow: it returns stable DTOs for catalog, hotlist, finding detail, finding history, suppression responses, and MCP rule or hotlist investigation views without arbitrary Cypher or unrestricted graph-query execution.

Contributors should treat the current behavior as a validated, persisted catalog, evaluator, finding persistence foundation, and first controlled query surface. It makes rule content safe to author, copy, load, reject deterministically, upsert into Neo4j as versioned catalog history, execute as data-only predicates over extraction graph facts, create deterministic finding records, carry first-seen/latest-seen history forward, suppress findings without deleting them, and query persisted outputs through bounded API endpoints. It still does not introduce MCP graph access, markdown export, automatic remediation, a Discovery UI, or a general-purpose graph browser.
