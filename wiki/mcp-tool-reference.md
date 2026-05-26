# MCP Tool Reference

Archon's Model Context Protocol, usually shortened to **MCP**, is the AI-assistant-facing read-only surface over the same controlled application/query layer that backs the HTTP query APIs. An MCP **tool** is a named operation that a client can call to ask Archon a bounded architecture question. Tools are not shell commands, database query windows, source-code readers, or remediation agents. They are carefully shaped calls that validate their inputs, authorize the caller, invoke an approved query abstraction, and return the common MCP response envelope described in [runtime foundation](runtime-foundation.md).

This page is the MCP reference companion to the runtime foundation. The runtime foundation explains the host-level concepts: registration catalog, response envelope, caller context, fail-closed authorization, allow-listing, audit, redaction, and prompt-injection-aware evidence handling. This page explains the current user-facing tool, resource, and prompt behavior. As later WP015 slices add enough capabilities to make this page unwieldy, those contracts should be documented here or on a more specific child page if the reference becomes too large.

## Current capability inventory

The current MCP product surface is complete for WP015 and consists of one operational capability, twelve read-only tools, seven supported resource URI patterns, two prompt operations, and seven versioned prompt templates. The capability inventory below is the quick index; the detailed sections that follow explain input shape, output meaning, limits, safe follow-ups, and failure semantics.

| Capability | Kind | Purpose | Primary output | Typical safe follow-up |
| --- | --- | --- | --- | --- |
| `archon.health` | Operational | Proves the MCP host is composed, registered, and constrained to read-only behavior. | Common health/capability envelope. | Check `/health`, inspect readiness configuration, or review [runtime foundation](runtime-foundation.md). |
| `archon.search` | Tool | Searches persisted architecture facts by text and controlled result families. | Grouped stable-key search results with evidence references and unknowns. | Describe a project or symbol, read a resource, or narrow the search filters. |
| `archon.describe_project` | Tool | Expands a project stable key or unambiguous project name into project context. | Project identity, graph, runtime, responsibility, risk, evidence, findings, and unknowns. | Traverse dependencies/dependents, inspect data access, hotlist findings, or impact. |
| `archon.get_dependencies` | Tool | Traverses outgoing dependency-like relationships from a stable node. | Bounded node and relationship facts with evidence and truncation metadata. | Increase specificity with edge filters or read impacted project/symbol resources. |
| `archon.get_dependents` | Tool | Traverses incoming consumers of a stable node. | Bounded dependent nodes and relationships with evidence and truncation metadata. | Use impact analysis or describe the returned consumers. |
| `archon.find_dependency_paths` | Tool | Finds bounded paths between two stable nodes. | Ordered path records with stable node and edge identities. | Inspect intermediate nodes or reduce depth/edge filters. |
| `archon.describe_symbol` | Tool | Expands one stable symbol or exact unambiguous symbol search into symbol context. | Symbol identity, source context, relationships, evidence, findings, and unknowns. | Find symbol usages, inspect containing project, or assess change impact. |
| `archon.find_symbol_usages` | Tool | Lists bounded usages of one stable symbol. | Usage relationships, source context, evidence, confidence, and unknowns. | Describe callers/callees or narrow by usage kind/project. |
| `archon.get_data_access_usage` | Tool | Reviews persisted data-access facts without opening databases or executing SQL. | Data-access records, operation kinds, dynamic SQL indicators, evidence, and unknowns. | Describe owning projects, inspect hotlist findings, or assess impact. |
| `archon.assess_change_impact` | Tool | Walks incoming relationships from a supported stable target to identify direct and transitive consumers. | Direct/transitive impact records with evidence, confidence, and truncation warnings. | Call `archon.get_dependents`, describe returned projects/symbols, or search related terms. |
| `archon.get_architecture_rules` | Tool | Lists bounded architecture-rule catalog records. | Rule identity, category, severity, enabled state, description, scopes, and unknowns. | Inspect related hotlist findings or current rules resource. |
| `archon.get_hotlist_findings` | Tool | Lists bounded persisted findings for triage. | Finding records, affected nodes, evidence references, sorting, warnings, and unknowns. | Describe affected nodes, read hotlist resource, or assess impact. |
| `archon.get_snapshot_diff` | Tool | Compares explicit or latest comparable snapshots. | Summary counts and optional bounded details with stable keys and fingerprints. | Read project/symbol/diff resources or narrow by domain/change kind. |
| `archon.read_resource` | Resource operation | Reads supported `archon://` resources through one authorization, parsing, and audit path. | Resource envelope for snapshot, rules, hotlist, hotspots, project, symbol, or diff context. | Follow the delegated tool mapping or read a related stable-key resource. |
| `archon.list_prompts` | Prompt operation | Lists available versioned prompt templates. | Prompt names, versions, summaries, and safe workflow metadata. | Retrieve the selected prompt by name. |
| `archon.get_prompt` | Prompt operation | Retrieves one embedded prompt template. | Markdown prompt template with evidence-grounding and no-mutation instructions. | Use the prompt's suggested read-only tool/resource sequence. |

The supported resource URI patterns are `archon://snapshot/current`, `archon://rules/current`, `archon://hotlist/current`, `archon://hotspots/current`, `archon://project/{projectKey}`, `archon://symbol/{symbolKey}`, and `archon://snapshot/{snapshotId}/diff/{previousSnapshotId}`. The supported prompt names are `impact-analysis`, `modernization-brief`, `refactoring-preflight`, `new-feature-placement`, `legacy-data-access-review`, `hotlist-summary`, and `architecture-rule-check`.

## Common response envelope, limits, and evidence-backed examples

Every tool and resource response is shaped as a common MCP envelope so an AI client can reason consistently across different workflows. The `operation` field names the operation that ran. The optional `snapshot` section identifies the concrete snapshot when a response depends on snapshot state. The `summary` is an orientation sentence, not the source of truth. The `confidence` section explains how strongly the persisted data supports the answer. The `facts` section carries the operation-specific structured data. The `evidence`, `findings`, `unknowns`, `warnings`, `limits`, and `suggestedFollowUps` sections preserve support, risk, uncertainty, boundedness, and next safe investigation paths.

Default limits are intentionally conservative: 25 result records, traversal depth 3, 10 evidence references, 5 dependency paths, and an approximate 24,000-character serialized context budget. Request-level limits can be lower, and configured limits can be changed by the host, but the MCP surface must always return truncation metadata and suggested narrowing when a response is partial. Truncation is not a cosmetic warning. If a dependency traversal, impact assessment, hotlist review, or snapshot diff is truncated, the client must say that the answer is an orientation subset rather than a complete inventory.

An evidence-backed response should cite stable keys and evidence references rather than turning the summary into unsupported prose. A shortened change-impact response illustrates the pattern:

```json
{
  "operation": "archon.assess_change_impact",
  "snapshot": {
	"stableKey": "snapshot://repo/main",
	"selectionMode": "latest"
  },
  "summary": "Returned 2 direct impacts and 1 transitive impact for dataaccess://orders/query.",
  "confidence": {
	"level": "Medium",
	"reason": "Impacts came from bounded incoming graph traversal with one dynamic SQL unknown."
  },
  "facts": {
	"targetStableKey": "dataaccess://orders/query",
	"directImpacts": [
	  {
		"impactedStableKey": "symbol://orders/service/create-order",
		"relationshipStableKey": "relationship://calls/orders-query",
		"depth": 1,
		"evidenceStableKeys": ["evidence://orders/service/create-order"]
	  }
	],
	"transitiveImpacts": [
	  {
		"impactedStableKey": "project://src/orders/api/orders.api.csproj",
		"relationshipStableKey": "relationship://depends/orders-api-service",
		"depth": 2,
		"evidenceStableKeys": ["evidence://orders/api/project"]
	  }
	],
	"recommendationFraming": "Read-only investigation guidance; not automatic remediation."
  },
  "evidence": [
	{
	  "stableKey": "evidence://orders/service/create-order",
	  "kind": "SourceSnippet",
	  "trustLabel": "untrusted-repository-evidence",
	  "snippetPreview": "return [redacted]"
	}
  ],
  "unknowns": [
	{
	  "code": "dynamicSql",
	  "message": "One data-access target was composed dynamically and may have additional consumers."
	}
  ],
  "warnings": [],
  "limits": {
	"truncated": false,
	"limitKind": "resultCount"
  },
  "suggestedFollowUps": [
	{
	  "label": "Inspect incoming dependents",
	  "operation": "archon.get_dependents",
	  "parameters": {
		"nodeStableKey": "dataaccess://orders/query"
	  }
	}
  ]
}
```

The example is intentionally stable-key based. It does not expose raw Neo4j IDs, absolute machine paths, connection strings, access tokens, unbounded source text, SQL execution authority, remediation commands, or repository mutation instructions. Snippet previews, when present, are treated as untrusted repository evidence and are redacted before output.

## Setup walkthrough for AI-assistant investigations

A safe MCP investigation normally starts with orientation, moves through exact stable keys, and ends with bounded conclusions. The recommended sequence is not a rigid script, but it helps prevent common AI-client mistakes.

First, verify the host and prompt catalog. Start `ArchonMcp`, check `/health`, list prompts, and retrieve the prompt that matches the user's workflow. Prompt retrieval does not prove architecture state; it gives the AI client the right safety and sequencing instructions before querying evidence.

Second, resolve exact identities. Use `archon.search` for broad terms or read `archon://snapshot/current` when the workflow needs current snapshot context. Do not invent stable keys from display names. If search returns multiple project or symbol candidates, ask the user or call a more specific read-only operation rather than selecting arbitrarily.

Third, call the tool or resource that matches the question. Use project and symbol descriptions for local context, dependency traversal for direction-aware relationships, dependency paths for reachability, data-access usage for persistence concerns, impact assessment for incoming consumers, hotlist and rule tools for governance, and snapshot diff for change review. Prefer resource URIs when the client already has an exact stable key and wants addressable context.

Finally, summarize with constraints. A good final answer names the snapshot, cites stable keys and evidence references, explains findings and unknowns, preserves truncation warnings, and distinguishes no-match or no-path results from unavailable data. It should propose only safe follow-ups such as another MCP read operation or a user clarification question. It must not propose running shell commands, executing SQL or Cypher, editing files, mutating findings or rules, suppressing results, or applying automatic remediation through MCP.

## Troubleshooting and security reference

Most MCP failures fall into a small set of categories. `Unauthorized` means the host requires a caller identity and the current caller provider did not supply one. `Forbidden` means the operation exists but is disabled by `Archon:Mcp:Security:AllowedOperations`. `Validation` means the request shape, stable key, resource URI, duplicate parameter, depth, limit, filter, or Boolean value was invalid before query execution. `Ambiguous` means Archon found multiple valid candidates and refused to choose one. `NotFound` means the selected data set was available but the exact key or explicit target was not present. `DependencyUnavailable` means the repository, solution, snapshot, query dependency, or comparable snapshot data needed to answer was unavailable. `QueryLayerFailure` means an approved query abstraction failed and the MCP host returned a safe error rather than leaking internals.

Security issues should be diagnosed by preserving the read-only model. If a user asks why MCP cannot execute a command, query SQL, query Cypher, browse Neo4j, mutate files, modify source, enable rules, suppress findings, or create snapshots, the correct answer is that those capabilities are intentionally forbidden by WP015. If a caller needs data that MCP does not expose, add or extend a controlled application/query abstraction in a future work package; do not add an escape hatch to the MCP host. If a response seems to omit source text, credentials, or connection details, that is usually the redaction and bounded-context contract doing its job. Only treat it as a bug when the response leaks a secret-like value, raw stack trace, raw graph-local ID, or privileged instruction confusion.

## Prompt template model

An MCP **prompt** is a curated workflow template that an AI client can retrieve before it begins a read-only architecture investigation. Prompt templates are not tools that query the graph and they are not instructions to edit code. They are versioned markdown resources embedded in the MCP host assembly, loaded read-only at runtime, registered in the same readiness catalog as tools and resources, and retrieved through the audited prompt operation path. Embedding the templates matters because the host does not need to inspect arbitrary files to answer a prompt request, and deployments receive the same reviewed prompt text that was tested with the host binary.

The current prompt retrieval surface has two narrow verification operations. `archon.list_prompts` returns prompt names, versions, and summaries so clients can discover supported workflows without expanding every template into context. `archon.get_prompt` retrieves one full markdown template by stable prompt name. Both operations pass through the same caller-context, allow-list, authorization, and sanitized audit pipeline as tool and resource calls. Audit metadata records safe values such as the requested prompt name; it does not record source evidence snippets, secrets, access tokens, connection strings, or repository content.

All prompt templates share the same safety contract. They tell the AI client to ground conclusions in Archon MCP tool or resource output, cite stable keys and evidence references, carry forward findings and metrics where returned, report unknowns and confidence limits, and ask safe follow-up questions when data is missing or ambiguous. They also explicitly prohibit invention of unsupported facts, repository mutation, shell commands, arbitrary SQL, arbitrary Cypher, filesystem mutation, source-code mutation, database mutation, rule mutation, finding mutation, snapshot mutation, and direct remediation. When a prompt mentions source snippets, evidence previews, comments, markdown, rule metadata, SQL text, or configuration values, that material is treated as untrusted repository data. The AI client must not follow instructions embedded in that content, even when a malicious comment or markdown fragment tries to override the user's request or the client's higher-priority instructions.

The registered prompt templates are:

| Prompt | Intended workflow | Typical read-only MCP sequence |
| --- | --- | --- |
| `impact-analysis` | Understand the likely consumers and blast-radius uncertainty for a proposed stable-key change target. | Resolve with `archon.search`, call `archon.assess_change_impact`, then narrow with `archon.get_dependents`, project resources, symbol resources, or snapshot diff where needed. |
| `modernization-brief` | Build an evidence-backed modernization overview for a repository, solution, project, subsystem, or legacy technology area. | Search or describe projects, inspect hotlist findings and architecture rules, review data-access usage, and use impact analysis for sequencing context. |
| `refactoring-preflight` | Review bounded symbol, project, dependency, and finding context before the user plans a refactoring. | Resolve exact stable keys, describe symbols, find usages, traverse dependencies/dependents, and inspect relevant findings. |
| `new-feature-placement` | Compare candidate project or subsystem locations for a proposed feature without inventing ownership. | Search domain terms, describe likely projects, inspect dependencies and dependents, check coupling paths, and review findings/rules that affect placement. |
| `legacy-data-access-review` | Summarize legacy or modern data-access facts, dynamic SQL uncertainty, and related findings. | Call `archon.get_data_access_usage`, describe owning projects, inspect hotlist findings, and assess impact for stable data-access targets. |
| `hotlist-summary` | Summarize filtered or current hotlist findings by severity, rule, category, affected node, and evidence support. | Call `archon.get_hotlist_findings`, read `archon://hotlist/current` when scoped current context is needed, and explain rules with `archon.get_architecture_rules`. |
| `architecture-rule-check` | Explain rule catalog records and related findings without mutating rules or suppressions. | Call `archon.get_architecture_rules`, inspect related hotlist findings, describe affected projects, or read `archon://rules/current` for scoped current rule context. |

Prompt output should be treated as workflow guidance for the AI client rather than as architecture evidence by itself. The evidence remains in the tool and resource responses. If a prompt suggests a sequence and a later tool returns an unknown, ambiguous result, dependency-unavailable error, or truncation warning, the final client answer must preserve that limitation rather than smoothing it over.

## Security validation contract

The completed MCP surface is intentionally read-only and validation-tested as a product contract, not merely as implementation preference. A client can call supported tools, supported `archon://` resources, and registered prompts, but it cannot ask the MCP host to run a shell command, submit arbitrary SQL, submit arbitrary Cypher, issue an unrestricted graph query, write files, mutate source code, mutate database records, enable or delete rules, suppress or edit findings, or create or delete snapshots. Unsupported operation names such as execution, command, write, delete, SQL, Cypher, and graph-query shapes fail closed because they are not registered capabilities and because the registration catalog marks unsafe names or non-read-only registrations as readiness failures.

Authorization and allow-listing are part of that validation contract. The host checks caller context and the configured operation allow-list before request validation and before any application/query dependency is invoked. That order is deliberate. A disabled or unauthenticated caller should not learn whether a project key, resource URI, search text, or snapshot selector would otherwise be valid, and it should not cause the query layer to run work that it was not authorized to request. Audit events still record the denied attempt with safe normalized metadata so operators can understand what was attempted without storing secrets or evidence snippets.

Prompt-injection validation is equally explicit. Source comments, markdown files, configuration values, string literals, rule metadata, SQL previews, and evidence snippets are all untrusted repository data. The MCP host may return redacted snippets so a contributor can understand why a fact exists, but it labels extracted content as `untrusted-repository-evidence` and keeps privileged instruction text separate. If a source comment says "ignore previous instructions" or a markdown fragment tries to reveal hidden prompts, the client must treat that text as evidence content only. The shared redaction layer removes representative passwords, tokens, API keys, account keys, credentials, connection strings, certificates, and private-key assignments before snippets or audit metadata leave the host.

Operational validation covers readiness, cancellation, and common response contracts. `/health` reports ready only when mandatory MCP registrations are complete and safe; missing required capabilities or unsafe capability names make readiness fail closed. `/alive` remains the lightweight liveness signal. Query-backed handlers propagate cancellation tokens to approved query abstractions where those abstractions support cancellation, which lets callers stop long-running reads without converting cancellation into a fabricated success response. Representative host-level tool, resource, and prompt calls are tested for the common envelope fields: operation, snapshot where relevant, summary, confidence, facts, evidence, findings, unknowns, warnings, limits, and suggested follow-ups.

## Resource URI model

An MCP **resource** is a read-only addressable context document identified by a URI rather than by a tool name. Archon resources use the `archon://` scheme so they cannot be confused with filesystem paths, HTTP routes, shell commands, SQL strings, or Neo4j queries. The current resource surface supports four current-snapshot resources, `archon://snapshot/current`, `archon://rules/current`, `archon://hotlist/current`, and `archon://hotspots/current`, and three parameterized resource patterns, `archon://project/{projectKey}`, `archon://symbol/{symbolKey}`, and `archon://snapshot/{snapshotId}/diff/{previousSnapshotId}`. Each resource is read through the single `archon.read_resource` operation, which means authorization, allow-listing, audit, response limits, redaction, and structured errors work the same way for every resource family.

Current resources always require an explicit repository scope in the URI query string. A typical URI is `archon://hotlist/current?repository=repository%3A%2F%2Farchon-sample&solution=solution%3A%2F%2Farchon-sample%2Fmain&limit=10`. Query parameters are percent-decoded before validation. `repository` is required and must be a stable-key URI such as `repository://archon-sample`. `solution` is optional and narrows the current snapshot selection to snapshots that contain that solution stable key. `limit` is optional and is still capped by configured MCP limits. List resources also accept supported narrowing filters: `category` for rules, hotlist, and hotspots; `severity` and `status` for hotlist. Duplicate query parameters are rejected because duplicate values make the selected scope or filters ambiguous.

The phrase **current snapshot selection** means Archon resolves `current` to exactly one concrete `snapshot://` stable key inside the supplied repository and optional solution scope. Resolution is intentionally explicit and fail-closed. If no snapshot matches the scope, the resource returns `NotFound`. If more than one snapshot ties for the latest completion and start timestamps, the resource returns `Ambiguous` with candidate snapshot stable keys instead of choosing one by alphabetical order or by persistence-local identity. This rule matters for AI clients because a resource summary over the wrong snapshot can lead to incorrect modernization, impact, or triage conclusions.

Resource responses use the same common MCP envelope as tools. The operation is `archon.read_resource`; the `snapshot` section contains the concrete selected snapshot key; the `facts` section is resource-specific; `evidence`, `findings`, `unknowns`, `warnings`, `limits`, and `suggestedFollowUps` retain their usual meanings. Resource outputs never expose secrets, repository root paths, remote URLs, raw evidence snippets, stack traces, connection strings, database-local IDs, shell commands, arbitrary Cypher, arbitrary SQL, filesystem mutation, source-code mutation, rule mutation, finding mutation, or snapshot mutation.

Parameterized resources are stable-key views over the same application/query abstractions as the corresponding tools. The project and symbol resource path segment is a percent-encoded stable key; for example, `project://src/orders/orders.csproj` appears in a URI path as `project%3A%2F%2Fsrc%2Forders%2Forders.csproj`. The snapshot diff resource uses two percent-encoded snapshot stable keys in the route path and an optional `limit` and `includeDetails=true|false` query string. These resources may include optional `repository` and `solution` query parameters to keep latest-style tool selectors bounded, but the path stable key remains the resource identity. Malformed keys, wrong key prefixes, duplicate query parameters, non-positive limits, and non-Boolean `includeDetails` values fail before query execution.

Parameterized resource success responses preserve the common resource envelope operation name `archon.read_resource`, while the facts, evidence, findings, unknowns, warnings, limits, and suggested follow-ups come from the approved tool-backed query workflow. The response includes a `resourceDelegatedToolMapping` warning to make that mapping explicit: the resource did not invent a second project, symbol, or diff contract; it reused `archon.describe_project`, `archon.describe_symbol`, or `archon.get_snapshot_diff` through the host's read-only abstractions. That design keeps tool and resource semantics aligned while letting MCP clients address frequently used context by URI.

### `archon://snapshot/current`

`archon://snapshot/current` returns selected snapshot identity and safe section counts. It is a concise orientation resource for clients that need to know which concrete snapshot a repository and optional solution currently resolve to before reading more detailed current resources.

Example URI:

```text
archon://snapshot/current?repository=repository%3A%2F%2Farchon-sample&solution=solution%3A%2F%2Farchon-sample%2Fmain
```

The facts include the canonical resource URI, selected snapshot stable key, repository stable key, solution stable keys present in the snapshot, optional branch and commit fields recorded by extraction, start and completion timestamps, status, and counts for nodes, edges, rules, findings, metrics, evidence records, warnings, and errors. Warning and error counts are reported as counts rather than expanded arbitrary diagnostic text so the resource remains bounded and safe.

### `archon://rules/current`

`archon://rules/current` returns a bounded rule-catalog view associated with the selected current snapshot context. The current rule catalog query is global catalog data rather than a per-snapshot rule-result query, so the response includes an explicit unknown explaining that per-snapshot rule source references and related finding counts require a different query path. The resource is still useful as a compact companion to current findings because it lists rule identity, category, severity, enabled state, built-in state, owner scope, summary, and tags without reading rule JSON files or exposing rule mutation.

Example URI:

```text
archon://rules/current?repository=repository%3A%2F%2Farchon-sample&category=Lifecycle&limit=10
```

### `archon://hotlist/current`

`archon://hotlist/current` returns bounded current hotlist findings for the resolved snapshot. The resource maps to the approved hotlist query abstraction with the concrete selected `snapshot://` key, not with the literal word `current`. It can filter by `category`, `severity`, and `status`, and it returns stable finding records, affected-node references, evidence stable keys, envelope-level finding references, explicit unknowns for history timestamps not supplied by the list query, truncation metadata, and safe follow-ups.

Example URI:

```text
archon://hotlist/current?repository=repository%3A%2F%2Farchon-sample&severity=High&status=Active&limit=10
```

### `archon://hotspots/current`

`archon://hotspots/current` returns bounded architecture hotspots for the resolved snapshot. A hotspot is a ranked concentration of metrics, findings, evidence, or graph context that deserves attention. The resource delegates hotspot detection and ranking to the approved application query service; MCP does not accept scoring logic or graph predicates from the client. Returned records include hotspot stable key, category, target stable key and kind, display name, score, rank, contributing metric keys, contributing finding keys, evidence keys, confidence, unknown-state fields, and fingerprint.

Example URI:

```text
archon://hotspots/current?repository=repository%3A%2F%2Farchon-sample&category=Modernization&limit=10
```

As with all current resources, truncated hotspot output is a partial orientation view rather than a complete inventory. Clients should carry the truncation warning forward when summarizing architecture risk or modernization priorities.

### `archon://project/{projectKey}`

`archon://project/{projectKey}` returns bounded project context for one exact `project://` stable key. It is the resource form of `archon.describe_project`, and it is useful when an MCP client already has a project stable key from search, hotlist, dependency traversal, impact assessment, or a previous resource response and wants to read the project context as addressable data rather than re-submit a tool payload.

Example URI:

```text
archon://project/project%3A%2F%2Fsrc%2Forders%2Forders.csproj?repository=repository%3A%2F%2Farchon-sample&solution=solution%3A%2F%2Farchon-sample%2Fmain
```

The facts section is the same project facts shape used by `archon.describe_project`: identity, graph, runtime, responsibility, risk, and metadata. The envelope can include project evidence references, hotlist finding references, unknowns for incomplete project or snapshot sections, warnings from the query layer, limit metadata, and safe follow-ups such as dependency or dependent traversal. The output is stable-key based and does not require the client to know HTTP API routes, Neo4j labels, project file system roots, or graph persistence details.

### `archon://symbol/{symbolKey}`

`archon://symbol/{symbolKey}` returns bounded symbol context for one exact `symbol://` stable key. It is the resource form of `archon.describe_symbol`. The resource is intentionally stable-key-only; it does not perform search-text lookup or ambiguity resolution. If a client has only a display name, it should first use `archon.search` or `archon.describe_symbol` with an exact search workflow, then read the resulting symbol resource once the stable key is known.

Example URI:

```text
archon://symbol/symbol%3A%2F%2Forders%2Fcreate?repository=repository%3A%2F%2Farchon-sample&solution=solution%3A%2F%2Farchon-sample%2Fmain
```

The facts section includes symbol identity, containing project stable key, source context, semantic relationships, evidence stable keys, and unknown-state markers. Source previews and evidence snippets remain untrusted repository evidence and are redacted before output when they resemble secrets. The resource can include finding-like references for rule-related symbol relationships, explicit unknowns for partial semantic coverage, warnings for bounded or incomplete context, and safe follow-ups to read usage or related architecture facts.

### `archon://snapshot/{snapshotId}/diff/{previousSnapshotId}`

`archon://snapshot/{snapshotId}/diff/{previousSnapshotId}` returns explicit snapshot diff context between two `snapshot://` stable keys. It is the resource form of the explicit comparison mode of `archon.get_snapshot_diff`; it does not infer the previous snapshot from repository history. When clients need latest-to-previous behavior, they should call the tool form with `useLatestComparableSnapshots` rather than relying on this explicit resource URI.

Example URI:

```text
archon://snapshot/snapshot%3A%2F%2Fcurrent/diff/snapshot%3A%2F%2Fprevious?limit=25&includeDetails=true
```

The facts section includes current and previous snapshot stable keys, comparison scope, whether implied previous-snapshot mode was used, applied domain and change-kind filters, total detail-record count, summary counts, bounded details when requested, and a `hasChanges` flag. Detail rows use stable keys, record kinds, project keys, target keys, fingerprints, changed fields, evidence stable keys, and explicit unknowns; they do not expose raw graph records. Truncation metadata and warnings mean the returned details are an orientation subset even when summary counts indicate more changes are present.

## `archon.search`

`archon.search` is the first evidence-backed architecture investigation tool exposed by the MCP host. It searches persisted architecture facts through the existing application search abstraction rather than querying Neo4j directly or reading repository files. That boundary matters: the tool can only return facts, evidence references, warnings, unknowns, and follow-up affordances that the query layer is allowed to expose. It cannot run arbitrary Cypher, SQL, shell commands, filesystem searches, or code modifications.

The tool is intended for broad first-pass discovery. A contributor or AI assistant can ask for records related to a term such as a project name, type name, endpoint route fragment, configuration key, data-access term, finding term, or evidence summary. The answer is grouped by result type and uses stable public identities. A **result-type filter** is a controlled value that narrows the families of records returned by the query layer, such as `Project`, `Symbol`, `RuntimeEndpoint`, `Fact`, `Evidence`, `Finding`, or `Metric`. A **stable key** is Archon's durable public identity for a graph record, such as a `project://`, `symbol://`, `snapshot://`, or `evidence://` value; MCP clients must use these keys instead of Neo4j internal IDs. An **evidence reference** is a safe pointer to persisted evidence that supports a fact, not a request for the MCP host to read arbitrary source files. A **suggested follow-up** is a safe next investigation path, such as a future MCP tool, controlled API route, or user question, not an instruction to execute a command.

### Inputs

The request accepts `searchText` as the required search term. Empty or whitespace-only text is rejected before the query layer is invoked. `snapshotSelector` is optional and may be `latest` or a `snapshot://` stable key. `resultTypeFilters` is optional and must use the controlled result-type vocabulary supported by the query layer. `repositoryStableKey`, `solutionStableKey`, and `projectStableKey` are optional scope filters where the query layer supports them. The repository key is especially important when `latest` snapshot selection is used, because latest resolution must be bounded to one repository rather than inferred globally. `limit` is optional and is bounded by the configured MCP result-count limit.

A typical request shape is:

```json
{
  "searchText": "orders",
  "snapshotSelector": "latest",
  "resultTypeFilters": ["Project", "Symbol"],
  "repositoryStableKey": "repository://archon-sample",
  "solutionStableKey": "solution://archon-sample/main",
  "projectStableKey": null,
  "limit": 10
}
```

The MCP host validates the request before running search. Validation rejects empty search text, malformed snapshot selectors, raw numeric or non-stable scope keys, empty filter values, and over-limit result counts. Authorization and allow-list checks still run before validation so a disabled or unauthenticated caller does not get request-shape details from the tool.

### Output shape

A successful response uses the common MCP envelope. The `facts` section contains the normalized query text, applied repository/solution/project scope, total match count, returned match count, a `dataAvailable` flag, and deterministic result groups. Each result item includes a stable key, entity kind, display text, safe summary, snapshot stable key, evidence stable keys, related stable keys, row confidence, unknown-state markers, and result-level suggested follow-ups.

A shortened example response is:

```json
{
  "operation": "archon.search",
  "snapshot": {
	"stableKey": "snapshot://repo/main",
	"selectionMode": "latest",
	"description": "Resolved from selector 'latest'."
  },
  "summary": "Returned 2 of 2 persisted architecture search matches for 'orders'.",
  "confidence": {
	"level": "High",
	"reason": "Search results were returned from the controlled application query layer."
  },
  "facts": {
	"queryText": "orders",
	"repositoryStableKey": "repository://archon-sample",
	"solutionStableKey": "solution://archon-sample/main",
	"projectStableKey": null,
	"totalMatches": 2,
	"returnedMatches": 2,
	"dataAvailable": true,
	"groups": [
	  {
		"resultKind": "Project",
		"results": [
		  {
			"stableKey": "project://src/orders/orders.csproj",
			"entityKind": "Project",
			"displayText": "Orders",
			"summary": "Owns order processing.",
			"snapshotStableKey": "snapshot://repo/main",
			"evidenceStableKeys": ["evidence://project/orders"],
			"relatedStableKeys": ["project://src/orders/orders.csproj"],
			"hasUnknownData": false,
			"unknownReason": null,
			"confidence": "High"
		  }
		]
	  }
	]
  },
  "evidence": [
	{
	  "stableKey": "evidence://project/orders",
	  "kind": "SearchEvidenceReference",
	  "snippetPreview": null,
	  "snapshot": {
		"stableKey": "snapshot://repo/main",
		"selectionMode": "latest",
		"description": "Resolved from selector 'latest'."
	  }
	}
  ],
  "unknowns": [],
  "warnings": [],
  "limits": {
	"truncated": false,
	"limitKind": "resultCount"
  },
  "suggestedFollowUps": [
	{
	  "label": "Inspect matched record",
	  "operation": "/query/search/follow-up",
	  "parameters": {
		"stableKey": "project://src/orders/orders.csproj"
	  }
	}
  ]
}
```

The example intentionally shows evidence by stable key rather than source text. The current search DTO exposes evidence identities for broad discovery; richer source spans belong to evidence drill-down behavior. Search snippets, when they appear in future evidence mappings, must still be treated as untrusted repository evidence and redacted before they enter MCP output.

### No matches, unavailable data, and truncation

`archon.search` distinguishes three states that are easy for AI clients to confuse. A successful response with zero result groups and `dataAvailable` set to `true` means the selected search data was available and no persisted record matched the supplied text and filters. The response includes an explicit unknown explaining that no supported persisted record matched, because absence of matches is not proof that the target concept does not exist outside the extracted or supported data.

Unavailable data is different. If the requested repository, solution, or snapshot scope cannot be resolved, the tool returns a structured `DependencyUnavailable` MCP error instead of an empty success envelope. That tells the caller that search could not evaluate the requested data set. Validation errors, such as malformed stable keys or unsupported filters, return the shared validation error shape and do not invoke the query layer.

When more matches exist than the configured or requested MCP limit allows, the response includes truncation metadata, a warning, and a narrowing follow-up. The summary and confidence must be read with that limit in mind. A truncated response is useful for orientation, but it is not a complete inventory.

### Security and audit behavior

`archon.search` uses the same security pipeline as the baseline `archon.health` operation. A caller must be authenticated when authentication is required, and the operation must be present in the configured allow-list. If the operation is disabled, authorization fails before validation or query execution. Audit records include the operation name, caller identity when available, safe normalized request metadata, result status, truncation state, error category, and duration. They do not include raw source evidence, stack traces, credentials, connection strings, access tokens, or prompt-injection payloads.

## `archon.describe_project`

`archon.describe_project` is the project-level investigation tool. It answers the question "what does Archon currently know about this project?" without reading project files directly and without asking the graph database for arbitrary traversal. The tool calls the existing project detail query abstraction and returns only the project facts that earlier extraction and query work persisted: identity, repository-relative path, language, target framework, project format, application type, responsibilities, dependencies, packages, endpoints, workers, data-access indicators, configuration keys, integrations, hotlist findings, scoped graph counts, evidence references, warnings, and explicit unknowns.

The tool accepts either `projectStableKey` or `projectName`. A stable key is preferred because display names can repeat across production and test projects, generated projects, or multi-solution repositories. If a project name matches more than one project, the tool returns a structured `Ambiguous` MCP error with stable-key candidates instead of selecting one arbitrarily. This behavior is important for AI-assisted workflows because choosing the wrong project can lead to incorrect modernization, impact-analysis, or dependency conclusions. A contributor should resolve the ambiguity by asking the user which candidate is intended or by calling the tool again with the exact `project://...` key.

### Inputs

The request accepts `projectStableKey` or `projectName`, but not both. `repositoryStableKey` bounds snapshot resolution and is required by the underlying query layer when `snapshotSelector` is `latest`. `solutionStableKey` narrows the repository scope when available. `snapshotSelector` may be `latest` or a `snapshot://` stable key.

```json
{
  "projectStableKey": "project://src/orders/orders.csproj",
  "projectName": null,
  "snapshotSelector": "latest",
  "repositoryStableKey": "repository://archon-sample",
  "solutionStableKey": "solution://archon-sample/main"
}
```

Authorization and allow-list checks run before validation. Once authorized, validation rejects missing identity, conflicting identity fields, malformed stable keys, and malformed snapshot selectors. The tool then calls the project query service; it does not query Neo4j directly, inspect the filesystem, run MSBuild, restore packages, or infer responsibilities that were not returned by the query layer.

### Output shape

The `facts` section groups project information into identity, graph, runtime, responsibility, risk, and metadata sections. The identity section carries stable project identity and project classification. The graph section reports direct outgoing dependencies, incoming dependents, package counts, endpoint counts, scoped node counts, and stable dependency/package keys. The runtime section reports entry points, endpoints, hosted services or workers, data-access indicators, configuration keys, and integrations. The risk section carries hotlist and unknown-state indicators. Evidence and finding references appear in the shared envelope sections so clients can cite support without treating the natural-language summary as the source of truth.

A shortened response looks like this:

```json
{
  "operation": "archon.describe_project",
  "snapshot": {
	"stableKey": "snapshot://repo/main",
	"selectionMode": "latest"
  },
  "summary": "Project 'Orders' has 2 outgoing dependencies, 1 incoming dependents, 1 packages, and 1 endpoints in the selected snapshot.",
  "facts": {
	"identity": {
	  "stableKey": "project://src/orders/orders.csproj",
	  "name": "Orders",
	  "path": "src/orders/orders.csproj",
	  "language": "C#",
	  "targetFramework": "net10.0",
	  "projectFormat": "SdkStyle",
	  "applicationType": "Worker"
	},
	"graph": {
	  "outgoingDependencyCount": 2,
	  "incomingDependentCount": 1,
	  "dependencies": ["project://src/domain/domain.csproj"],
	  "packages": ["package://nuget/newtonsoft.json"]
	},
	"runtime": {
	  "workers": ["OrdersHostedService"],
	  "configurationKeys": ["ConnectionStrings:Orders"],
	  "integrations": ["ServiceBus:orders"]
	}
  },
  "evidence": [
	{ "stableKey": "evidence://project/orders", "kind": "ProjectFile" }
  ],
  "findings": [
	{ "stableKey": "finding://hotlist/orders", "ruleCode": "HotlistIndicator" }
  ]
}
```

Missing optional sections are not silently treated as proof that the project has no such behavior. When the query layer reports incomplete extraction or unknown project data, the tool adds `unknowns` explaining which field or project section is incomplete. When project data is unavailable because the repository, solution, or snapshot cannot be resolved, the tool returns a `DependencyUnavailable` error rather than an empty project envelope. When the project does not exist in an otherwise available snapshot, it returns `NotFound`.

## `archon.get_dependencies` and `archon.get_dependents`

`archon.get_dependencies` and `archon.get_dependents` expose bounded dependency traversal over stable graph identities. A **dependency** is an outgoing relationship from the selected node to something it uses or references. A **dependent** is an incoming relationship from something that uses or references the selected node. If `project://src/api/api.csproj` has a `REFERENCES` edge to `project://src/domain/domain.csproj`, then the API project has a dependency on the domain project, and the API project is a dependent of the domain project.

Both tools call the existing graph traversal query abstraction. They do not accept Cypher, SQL, regular expressions, filesystem paths, or arbitrary graph predicates. Their purpose is to give AI clients a safe way to ask "what does this project or graph node depend on?" and "what depends on this project or graph node?" while preserving stable keys, evidence references, unknowns, warnings, and truncation metadata.

### Direct versus transitive traversal

Direct traversal walks one hop from the selected node. `archon.get_dependencies` direct mode follows outgoing relationships to immediate dependencies. `archon.get_dependents` direct mode follows incoming relationships to immediate consumers. Transitive traversal walks beyond one hop up to `maximumDepth`, still bounded by the configured MCP traversal-depth and result-count limits.

For example, if `Api -> Application -> Domain`, a direct dependency request for `Api` returns `Application`. A transitive dependency request with depth two can return both `Application` and `Domain`. Conversely, a direct dependent request for `Domain` returns `Application`, while a transitive dependent request with depth two can return both `Application` and `Api`. The tools return the stable relationships they traversed, not a free-form architecture diagram.

### Inputs

The request accepts `nodeStableKey` or `projectStableKey` as the traversal start identity. The `projectStableKey` field is an alias for project-level traversal because project stable keys are graph node stable keys. Project-name traversal is intentionally rejected in this slice; use `archon.describe_project` or `archon.search` to resolve a project name to a stable key first. `transitive` selects direct or transitive mode. `maximumDepth` applies to transitive mode. `edgeKindFilters` narrows traversal to controlled edge kinds, such as `References`, `UsesPackage`, `DependsOn`, or other query-layer-supported relationship kinds. `limit` bounds the returned relationship count. `snapshotSelector`, `repositoryStableKey`, and `solutionStableKey` scope the graph snapshot.

```json
{
  "nodeStableKey": "project://src/orders/orders.csproj",
  "projectStableKey": null,
  "projectName": null,
  "transitive": true,
  "maximumDepth": 3,
  "edgeKindFilters": ["References", "UsesPackage"],
  "limit": 25,
  "snapshotSelector": "latest",
  "repositoryStableKey": "repository://archon-sample",
  "solutionStableKey": "solution://archon-sample/main"
}
```

### Output shape and empty results

Successful traversal responses use the common envelope with a `facts` section containing the start node, direction, mode, direct/transitive flag, applied depth, edge-kind filters, returned nodes, and returned relationships. Relationship facts include stable edge keys, edge kinds, stable source and target node keys, direct-observation indicators, evidence stable keys, confidence, and unknown-state markers.

A successful response with no returned relationships means graph data was available and no matching dependencies or dependents were found within the requested depth and edge-kind bounds. The tool records that known-empty state in `unknowns` with `noDependencies` or `noDependents`, because a bounded empty result is not the same as a global proof that no relationship exists outside the selected filters or unsupported extraction areas. If the repository, solution, or snapshot cannot be resolved, the tool returns `DependencyUnavailable` instead. If the start node is missing, it returns `NotFound`.

Truncation appears whenever more matching relationships exist than the requested or configured limit allows. The envelope includes limit metadata, a warning such as `mcp.archon.get_dependencies.truncated`, and a narrowing follow-up. AI clients should cite the truncation warning when summarizing dependency risk or impact because omitted relationships can materially change the conclusion.

## `archon.find_dependency_paths`

`archon.find_dependency_paths` answers a narrower question than dependency traversal: "is there a bounded dependency path from this source node to that target node?" A **dependency path** is an ordered sequence of stable graph nodes and stable graph edges discovered by following outgoing dependency-like relationships. The tool is useful when a contributor wants to understand why one symbol, project, runtime artifact, data-access fact, or other persisted graph node can reach another. It does not accept Cypher, arbitrary predicates, source file paths, regular expressions, or repository file reads. The MCP host validates stable keys and bounds first, then calls the approved graph traversal query abstraction.

The request supplies `sourceNodeStableKey` and `targetNodeStableKey`. Both values must be stable public graph identities, such as `symbol://...`, `project://...`, or another supported Archon graph key. `maximumDepth` limits how many graph hops the path search may traverse. `edgeKindFilters` narrows the search to controlled relationship kinds such as `Calls`, `References`, `DependsOn`, or other query-layer-supported values. `limit` bounds the returned path records, while `snapshotSelector`, `repositoryStableKey`, and `solutionStableKey` choose the graph snapshot.

```json
{
  "sourceNodeStableKey": "symbol://orders/api/create",
  "targetNodeStableKey": "symbol://orders/domain/order",
  "maximumDepth": 4,
  "edgeKindFilters": ["Calls", "References"],
  "limit": 1,
  "snapshotSelector": "latest",
  "repositoryStableKey": "repository://archon-sample",
  "solutionStableKey": "solution://archon-sample/main"
}
```

A successful response contains path facts with the source key, target key, whether a path was found, whether data was available, the applied depth, edge filters, and ordered path records. Each path record includes stable node facts and stable edge facts. Evidence is returned as evidence references in the shared envelope rather than as arbitrary source text. If the query layer finds no path in the requested bounds, the tool returns a success envelope with an explicit `noDependencyPath` unknown and a warning such as `mcp.archon.find_dependency_paths.no_path`. If repository, solution, or snapshot data cannot be resolved, the tool returns `DependencyUnavailable` instead of pretending that no path exists. This distinction is important: a no-path result means Archon searched the selected data and found no matching path within the bounds; unavailable data means Archon could not answer the question for that scope.

## `archon.describe_symbol`

`archon.describe_symbol` expands one stable symbol identity, or one exact unambiguous symbol search text, into a bounded symbol context. A **symbol** is a persisted semantic code element such as a namespace, type, method, property, or field. The tool returns identity, containment, owning project, source context, relationships, evidence, confidence, findings, warnings, and unknowns from the approved symbol query layer. It is not a source-code reader and does not inspect files directly at request time.

Stable keys are preferred because symbol names often repeat across overloads, partial types, generated code, test projects, and different namespaces. If exact search text matches more than one symbol, the tool returns an `Ambiguous` structured MCP error and suggests resolving the intended symbol through `archon.search` or by asking the user. It never selects an arbitrary candidate.

```json
{
  "symbolStableKey": "symbol://orders/api/orders-controller/create",
  "searchText": null,
  "snapshotSelector": "latest",
  "repositoryStableKey": "repository://archon-sample",
  "solutionStableKey": "solution://archon-sample/main"
}
```

The `facts.identity` section reports the stable key, short name, fully qualified name when available, symbol kind, namespace, containing type, and language. The `facts.source` section reports repository-relative file path, line range, snippet hash when available, and a bounded snippet preview. Snippet previews are treated as untrusted repository evidence: the MCP mapper redacts secret-like values and labels the content as `untrusted-repository-evidence` so AI clients do not treat source comments, configuration fragments, or string literals as privileged instructions. Relationships are deterministic stable edge facts, not a free-form call graph.

Unknowns are first-class. Dynamic binding, incomplete compilation references, generated code gaps, unresolved overloads, or partial semantic extraction can reduce confidence. The tool records those gaps in the envelope so clients should say "unknown" rather than inventing complete call or ownership conclusions.

## `archon.find_symbol_usages`

`archon.find_symbol_usages` lists bounded usages of a stable symbol. A **symbol usage** is a persisted semantic relationship showing that one symbol or graph node calls, references, implements, inherits from, handles, injects, configures, or otherwise uses another symbol when the query layer has captured that relationship. The tool defaults to incoming usage investigation, which means it is optimized for questions such as "who calls this method?" or "where is this type referenced?"

The request requires `symbolStableKey` in the current MCP slice. `searchText` is intentionally rejected for usage lookup because usage queries should start from a resolved stable identity. `usageKindFilters` can narrow returned rows to controlled relationship kinds, and `projectStableKey` can narrow rows to a project scope when the relationship stable keys carry that project context. `maximumDepth` is retained in the MCP request shape for usage workflows that distinguish immediate and broader semantic context, while returned rows remain bounded by `limit` and the configured MCP result-count limit.

```json
{
  "symbolStableKey": "symbol://orders/domain/order-service/create",
  "searchText": null,
  "usageKindFilters": ["Calls", "References"],
  "projectStableKey": null,
  "maximumDepth": 1,
  "limit": 25,
  "snapshotSelector": "latest",
  "repositoryStableKey": "repository://archon-sample",
  "solutionStableKey": "solution://archon-sample/main"
}
```

Successful usage responses include the requested symbol key, applied filters, total matching count where known, bounded usage rows, evidence references, warnings, unknowns, and safe follow-ups back to `archon.describe_symbol` or `archon.search`. Usage rows include stable relationship identity, usage kind, source and target stable keys, optional display names, repository-relative source location, redacted untrusted snippet preview, evidence stable keys, confidence, and unknown-state metadata. Empty successful usage output is represented with `noSymbolUsages`; unavailable repository, solution, or snapshot data is a `DependencyUnavailable` error; a missing symbol is `NotFound`.

## `archon.get_data_access_usage`

`archon.get_data_access_usage` is the MCP data-access review tool. A **data-access usage fact** is persisted architecture knowledge about code that interacts with data storage, such as LINQ to SQL, Entity Framework 6, Entity Framework Core, ADO.NET, typed DataSet TableAdapters, raw SQL, stored procedures, database tables, columns, entities, and data-context types. The tool calls the approved fact-query abstraction and does not open database connections, run SQL, read files directly, inspect connection strings, execute stored procedures, or ask Neo4j for arbitrary graph records.

The tool is designed for investigation before modernization, migration, and impact-analysis work. It can narrow results by `projectStableKey`, `dataContextStableKey`, `entity`, `table`, `storedProcedure`, and `family` where the underlying query layer supports those filters. The `family` value should be a controlled data-access family such as `LinqToSql`, `EF6`, `EFCore`, `AdoNet`, `TypedDataSet`, `RawSql`, or `StoredProcedure`. `snapshotSelector`, `repositoryStableKey`, and `solutionStableKey` choose the persisted architecture snapshot, and `limit` is bounded by the configured MCP result-count limit.

```json
{
  "projectStableKey": "project://src/orders/orders.csproj",
  "dataContextStableKey": "datactx://orders/db",
  "entity": null,
  "table": "Orders",
  "storedProcedure": null,
  "family": "EFCore",
  "limit": 25,
  "snapshotSelector": "latest",
  "repositoryStableKey": "repository://archon-sample",
  "solutionStableKey": "solution://archon-sample/main"
}
```

Successful responses use the common envelope with a `facts` section containing the applied filters, total match count, and bounded usage records. Each usage record includes stable data-access identity, family, safe display name, owning project key, data-context key when available, entity/table/stored-procedure keys when available, usage-site identifiers, normalized operation kinds, dynamic SQL indicators, evidence stable keys, confidence, and unknown-state metadata. Operation kinds are intentionally broad: `Read`, `Write`, `Execute`, `Unknown`, or query-layer-provided labels that cannot be safely collapsed. This prevents the MCP response from pretending it knows exact database side effects when extraction only proved a method call or command pattern.

Dynamic SQL deserves special care. A **dynamic SQL indicator** means persisted extraction saw SQL composition or an unresolved target shape where table, column, command, or stored-procedure identity could not be proven deterministically. The tool reports those cases as explicit `dynamicSql` unknowns. AI clients should say that the target is uncertain rather than treating a raw SQL string, command name, or table hint as complete proof. Evidence references remain stable pointers and safe redacted previews; the tool does not return connection string values, credentials, tokens, unbounded SQL text, or source snippets that look secret-like.

If no records match the requested filters and the selected snapshot data is available, the response is a successful known-empty envelope with a no-match warning. If the repository, solution, or snapshot cannot be resolved, the tool returns `DependencyUnavailable`. Malformed stable keys, empty filters, and over-limit requests return validation errors before the fact query layer is invoked. Truncated responses include limit metadata, a warning, and safe follow-ups such as describing the owning project or assessing change impact for a selected data-access fact.

## `archon.assess_change_impact`

`archon.assess_change_impact` is the MCP impact-analysis tool. **Change impact** means the bounded set of persisted nodes that directly or transitively depend on a changed target. The target can be a supported stable graph identity such as a project, symbol, endpoint, worker, data-access fact, integration, configuration key, rule, finding, or metric. The tool uses incoming graph traversal through the approved traversal query abstraction, because incoming relationships represent consumers or dependents of the changed target. It does not run builds, execute tests, modify code, apply migrations, inspect files directly, or infer remediation steps from the filesystem.

```json
{
  "targetStableKey": "dataaccess://orders/query",
  "maximumDepth": 3,
  "edgeKindFilters": ["Calls", "References", "UsesData"],
  "limit": 25,
  "includeTransitive": true,
  "snapshotSelector": "latest",
  "repositoryStableKey": "repository://archon-sample",
  "solutionStableKey": "solution://archon-sample/main"
}
```

The response separates direct and transitive impacts. A direct impact is a one-hop incoming relationship to the changed target. A transitive impact is a broader relationship discovered through an intermediate node within the requested `maximumDepth`. Each impact record includes the stable relationship key, relationship kind, impacted stable key, impacted kind, safe display name, owning project key when known, inferred depth, evidence stable keys, confidence, and unknown-state metadata. The response also carries a `recommendationFraming` value that explicitly states the output is read-only investigation guidance, not automatic remediation or code-change instruction.

Impact analysis is deliberately conservative. A bounded traversal can miss dynamic dispatch, reflection, configuration-driven routing, unsupported extraction families, or relationships outside the selected edge filters and depth. Those uncertainty sources appear as unknowns and warnings. Truncation is especially important for impact work: if the result is truncated, clients should not claim that the listed nodes are the full blast radius. The safe follow-ups point back to read-only MCP calls such as `archon.get_dependents`, `archon.search`, or project/symbol description. They must not tell the user to edit code, run migration scripts, execute SQL, or suppress findings.

If the target is missing from an otherwise available snapshot, the tool returns `NotFound`. If the repository, solution, or snapshot is unavailable, it returns `DependencyUnavailable`. Unsupported target schemes, malformed stable keys, unsupported snapshot selectors, invalid depth, empty edge-kind filters, and over-limit counts are validation failures and stop before graph traversal. Query-layer exceptions return a safe `QueryLayerFailure` response without stack traces, exception type names, persistence internals, or evidence snippets.

## `archon.get_architecture_rules`

`archon.get_architecture_rules` is the MCP rule-catalog review tool. It lists versioned architecture-rule catalog records through the approved rule/hotlist query abstraction. A **rule catalog record** is authored rule metadata such as rule code, version, category, severity, enabled state, built-in state, description, and tags; it is not executable policy code. The tool is read-only. It cannot create, edit, enable, disable, delete, suppress, or rewrite rule definitions, and it does not read rule JSON files directly from disk.

The request supports exact filters for `ruleCode`, `category`, `severity`, and `enabled`, plus an optional `snapshotSelector` for consistency with other MCP workflows. The current catalog query is global catalog data rather than snapshot-scoped rule-result data, so snapshot selection does not mutate or re-evaluate rules. `limit` is bounded by the configured MCP result-count limit.

```json
{
  "ruleCode": "ARCHON-LIFECYCLE-NETFRAMEWORK-UNSUPPORTED",
  "category": "Lifecycle",
  "severity": "High",
  "enabled": true,
  "snapshotSelector": "latest",
  "limit": 25
}
```

Successful responses include bounded rule records with stable rule code/version identity, default status, enabled state, built-in state, owner scope, description, applicable scopes from catalog tags, and explicit unknowns for details that the current list query does not supply, such as related finding counts and safe rule source references. That explicit unknown state is intentional: an AI client should follow up with `archon.get_hotlist_findings` for finding volume instead of inventing counts from the catalog row. Truncation adds the shared limit metadata and narrowing follow-up. Validation failures, disabled operations, and query-layer failures use the same structured MCP error contract as other tools.

## `archon.get_hotlist_findings`

`archon.get_hotlist_findings` is the MCP findings triage tool. A **hotlist finding** is a persisted, bounded summary of a rule-produced concern in one snapshot. The tool returns stable finding keys, rule identity, severity, status, confidence, affected nodes, evidence references, metadata, unknowns, sorting, and limits through the approved hotlist query abstraction. It deliberately does not expose the suppression command supported by the HTTP API; MCP finding review is read-only and cannot suppress, resolve, delete, or edit findings.

The request supports filters for `projectStableKey`, `ruleCode`, `category`, `severity`, `status`, `snapshotSelector`, and safe `searchText` over returned display fields. `sortBy` currently supports `severity`, `latestSeen`, `ruleCode`, and `stableKey`; severity is the default because hotlist review usually begins with highest-risk findings. `repositoryStableKey` and `solutionStableKey` are retained for audit and future query-scope consistency, while the current list query maps the supported structured filters to the application hotlist query.

```json
{
  "projectStableKey": "project://src/legacy/legacy.csproj",
  "ruleCode": "ARCHON-DATAACCESS-LINQ2SQL",
  "category": "DataAccess",
  "severity": "High",
  "status": "Active",
  "snapshotSelector": "snapshot://repo/main",
  "searchText": "legacy",
  "sortBy": "severity",
  "limit": 10,
  "repositoryStableKey": "repository://archon-sample",
  "solutionStableKey": "solution://archon-sample/main"
}
```

Successful responses include finding records plus envelope-level finding and evidence references. Affected nodes are returned as stable identities with safe display names and optional project context. Evidence remains a stable reference; the tool does not expand source snippets, rule metadata payloads, or configuration values. The current hotlist list DTO does not carry first-seen and latest-seen timestamps, so the MCP tool records those values as explicit unknowns rather than pretending the history is absent. When more findings match than MCP can return, truncation metadata and warnings tell the client that the response is a partial triage view. Safe follow-ups point to read-only investigation such as `archon.assess_change_impact`, never to suppression or remediation.

## `archon.get_snapshot_diff`

`archon.get_snapshot_diff` is the MCP change-review tool for persisted architecture snapshots. A **snapshot diff** compares two snapshot states by stable key and fingerprint. Stable keys identify the same logical record across snapshots; fingerprints summarize comparison-relevant content. A record is `Added` when it appears only in the current snapshot, `Removed` when it appears only in the previous snapshot, `Changed` when the stable key exists in both snapshots but the fingerprint differs, and `Unchanged` when both stable key and fingerprint match.

The tool supports two modes. Explicit mode supplies `currentSnapshotStableKey` and `previousSnapshotStableKey`. Latest-comparable mode sets `useLatestComparableSnapshots` to `true` and supplies `repositoryStableKey`, with optional `solutionStableKey`; the application diff service then resolves the newest two comparable snapshots inside that scope. The two modes are mutually exclusive so the MCP host never has to guess which snapshots the caller meant. Optional filters include `domains`, `changeKinds`, `projectStableKey`, `targetStableKey`, `recordKind`, `severity`, `includeDetails`, `includeUnchangedDetails`, and `limit`.

```json
{
  "currentSnapshotStableKey": "snapshot://repo/current",
  "previousSnapshotStableKey": "snapshot://repo/previous",
  "useLatestComparableSnapshots": false,
  "repositoryStableKey": null,
  "solutionStableKey": null,
  "domains": ["Nodes", "Findings"],
  "changeKinds": ["Added", "Changed"],
  "projectStableKey": "project://src/orders/orders.csproj",
  "targetStableKey": null,
  "recordKind": null,
  "severity": "High",
  "includeDetails": true,
  "includeUnchangedDetails": false,
  "limit": 25
}
```

Successful responses always include summary counts by domain and include bounded detail records when requested. Detail rows carry stable keys, display names, domain-specific kind, project and target stable keys, severity, previous/current fingerprints, changed fields, evidence stable keys, and row-level unknown state. The response distinguishes a known no-change comparison from unavailable data: no changes is a successful envelope with a `noChanges` warning, while missing snapshots, missing repository scope, or unavailable comparable snapshots return structured `NotFound` or `DependencyUnavailable` errors. The tool cannot create snapshots, delete snapshots, mutate graph records, run diff commands, or browse Neo4j directly.

## Page-structure note

This dedicated reference page exists because tool-specific, resource-specific, and prompt-specific inputs, outputs, examples, limits, no-match, no-path, no-usage, data-access uncertainty, impact-analysis, rule catalog, hotlist, snapshot-diff, prompt workflow, and ambiguity semantics would make [runtime foundation](runtime-foundation.md) too broad. The runtime page remains the correct home for shared host concepts, while this page is the correct home for current MCP tool, resource, and prompt usage. `home.md` should continue to link to the reference but must not carry the detailed MCP contract itself.
