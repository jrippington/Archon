# Glossary

This glossary defines repository-specific terms used across the Archon wiki. Topic pages define terms in context when first introduced and link here when a contributor may need a central reference. Return to [home](home.md) for reader paths or [work-package documentation workflow](work-package-documentation-workflow.md) for maintenance rules.

## Accumulator

An accumulator is a stateful application-layer builder that accepts graph fact contributions and emits one assembled `ExtractedArchitectureSnapshot`. Archon's current accumulator is `ArchitectureSnapshotAccumulator`.

## AppHost

An AppHost is an Aspire project that describes which services, containers, and dependencies run together for local development. Archon's AppHost is `src/Archon`.

## ADO.NET

ADO.NET is the .NET data-access API family built around connections, commands, readers, adapters, and in-memory data containers. Archon extracts supported ADO.NET evidence statically from source code, including command text, execution methods, stored procedure command types, provider hints, and conservative table read/write hints. It does not open connections or execute command text.

## Architecture graph

The architecture graph is the durable representation of architecture facts, evidence, findings, metrics, and summaries. In the current persistence foundation, Neo4j stores this graph using stable labels, stable keys, fingerprints, and support relationships.

## Persistence diagnostic breakdown

A persistence diagnostic breakdown is the optional run-status section that explains the snapshot persistence handoff for one extraction run. It contains ordered scoped timings, persistence count values, and a completion flag. It is retained with the run lifecycle rather than stored as architecture graph content, and it can be present for completed runs or as partial evidence on failed persistence runs.

## Diagnostic collector

A diagnostic collector is the per-persistence-attempt component that records safe timing and count observations while a snapshot writer runs. The current Neo4j collector uses monotonic elapsed-time measurement, preserves sub-stage completion order, avoids raw Cypher, driver exceptions, credentials, connection strings, and duplicated payload materialization, and converts its observations into the application-owned persistence diagnostic breakdown.

## Sub-stage timing

A sub-stage timing is one scoped diagnostic duration inside a larger workflow stage. Persistence sub-stage timings use display-style names such as `Persistence.WriteRelationships` and `Persistence.Commit` so contributors can compare detailed persistence work without flattening those entries into the top-level extraction `timings` collection.

## Durable write finalization

Durable write finalization is the point at which the persistence adapter has finished the write transaction or equivalent store-specific commit boundary and can report a successful completed persistence result. In the current Neo4j adapter, this is represented by the `Persistence.Commit` diagnostic timing around the write transaction.

## Rule catalog

The rule catalog is the authored set of versioned JSON rules that describe modernization, lifecycle, security, dependency, configuration, data-access, and architecture concerns. In the current WP012 foundation, Archon loads the catalog from copied runtime output, validates it, upserts versioned catalog records into Neo4j, evaluates enabled rules against accumulated graph facts, constructs deterministic findings, persists finding history and suppression state, and exposes controlled rule and hotlist query output.

## Rule catalog upsert

A rule catalog upsert is the persistence operation that creates or updates a global `ArchonRule` node by rule code and rule version. It is idempotent for unchanged versions, preserves new versions beside old versions, and does not delete rules merely because they are disabled or omitted from a later disk load.

## Built-in rule

A built-in rule is repository-shipped JSON rule content with `builtIn` set to `true`. Built-in rules are still data-only, versioned by rule code plus version, loaded from copied runtime output, validated by the shared loader, and evaluated only against established graph facts.

## Rule coverage metadata

Rule coverage metadata is lower camel case traceability stored in a rule's metadata block to show which source-brief scenarios are represented by an authored rule. It is explanatory metadata for contributors and tests, not an executable condition language.

## Definition JSON

Definition JSON is the validated authored rule document stored with a persisted rule catalog record. It preserves the exact data payload that explains what the rule meant at a specific code/version pair.

## Historical fidelity

Historical fidelity is the repository principle that durable analysis records must remain explainable after authored rules, extracted facts, or classifications evolve. For WP012 rules, this means old rule versions and disabled or removed catalog records remain available so future findings can point to the exact rule code and version that classified them.

## Finding stable key

A finding stable key is the snapshot-scoped logical identity for one persisted finding record. It includes snapshot context and deterministic rule/target information so one extraction snapshot can store exactly one equivalent finding without depending on Neo4j internal IDs.

## Finding history key

A finding history key is the cross-snapshot logical identity for equivalent findings. It deliberately excludes the snapshot stable key so Archon can carry first-seen and latest-seen data forward when the same rule and affected target remain applicable in later snapshots.

## Suppression

Suppression is a lifecycle overlay that records why a finding is intentionally hidden, accepted, or deferred without deleting the underlying finding. WP012 suppressions target finding history key, rule identity, and primary node identity, and they preserve reason and suppressed-by audit fields.

## Architecture rule result

An architecture rule result is a controlled query DTO produced by built-in architecture checks over persisted graph facts, metrics, findings, runtime facts, and evidence. Results expose stable rule identity, target stable keys, status, severity-like review meaning, contributing metrics, edges, findings, evidence references, confidence, unknown state, metadata, and fingerprint without accepting custom rule expressions through the query API.

## Fingerprint

A fingerprint is a deterministic hash-like value that summarizes the public comparison-relevant content of a persisted graph record, metric, finding, cycle, hotspot, architecture-rule result, or diff item. Query APIs expose fingerprints so clients can detect content drift while continuing to use stable keys as durable logical identities.

## Hotlist

The hotlist is the controlled product-facing list of persisted findings. It returns bounded, deterministically ordered finding summaries with approved filters rather than exposing arbitrary graph queries or raw Neo4j records.

## Controlled query

A controlled query is an API read operation whose filters, page bounds, ordering, and response shape are defined by Archon code. Controlled queries protect the graph by accepting only approved values instead of caller-provided Cypher, SQL, JSONPath, or unrestricted predicate text.

## MCP registration catalog

The MCP registration catalog is the Archon MCP host's internal allow-list of stable capability names. A capability can represent a tool, resource, prompt, or operational registration. The current catalog registers the read-only `archon.health` operational capability, read-only tool capabilities such as `archon.search`, `archon.describe_project`, `archon.get_dependencies`, `archon.get_dependents`, `archon.find_dependency_paths`, `archon.describe_symbol`, `archon.find_symbol_usages`, `archon.get_data_access_usage`, `archon.assess_change_impact`, `archon.get_architecture_rules`, `archon.get_hotlist_findings`, and `archon.get_snapshot_diff`, the read-only resource-reader capability `archon.read_resource`, prompt operation capabilities `archon.list_prompts` and `archon.get_prompt`, and prompt template capabilities such as `impact-analysis`, `modernization-brief`, `refactoring-preflight`, `new-feature-placement`, `legacy-data-access-review`, `hotlist-summary`, and `architecture-rule-check`. Readiness uses the catalog to fail closed when mandatory registrations are missing, when a registration is not read-only, or when a capability name suggests unsafe behavior such as arbitrary shell execution, SQL, Cypher, unrestricted graph querying, direct Neo4j access, filesystem mutation, or code modification.

## MCP tool

An MCP tool is a named read-only operation exposed by the Archon MCP host for AI-assistant workflows. Current tools include `archon.search`, `archon.describe_project`, `archon.get_dependencies`, `archon.get_dependents`, `archon.find_dependency_paths`, `archon.describe_symbol`, `archon.find_symbol_usages`, `archon.get_data_access_usage`, `archon.assess_change_impact`, `archon.get_architecture_rules`, `archon.get_hotlist_findings`, and `archon.get_snapshot_diff`. Each tool validates request input, authorizes the caller, invokes an approved application/query abstraction, and returns the shared MCP envelope without allowing shell commands, arbitrary database queries, filesystem mutation, source-code modification, or direct Neo4j access.

## MCP resource

An MCP resource is a read-only addressable context document identified by a URI. Archon resources use the `archon://` scheme and are read through `archon.read_resource`. The current resource surface supports `archon://snapshot/current`, `archon://rules/current`, `archon://hotlist/current`, `archon://hotspots/current`, `archon://project/{projectKey}`, `archon://symbol/{symbolKey}`, and `archon://snapshot/{snapshotId}/diff/{previousSnapshotId}` with bounded result limits, stable keys, evidence references, unknowns, warnings, and no mutation behavior.

## MCP prompt

An MCP prompt is a versioned read-only workflow template exposed by the Archon MCP host for AI-assistant architecture investigation. Current prompts include `impact-analysis`, `modernization-brief`, `refactoring-preflight`, `new-feature-placement`, `legacy-data-access-review`, `hotlist-summary`, and `architecture-rule-check`. Prompts are embedded markdown resources loaded by the host, retrieved through audited prompt operations, and written to require evidence grounding, unknown reporting, confidence limits, prompt-injection resilience, and no mutation or direct remediation.

## Prompt template

A prompt template is the concrete markdown content returned for one MCP prompt name and version. It guides a client through safe read-only tool and resource sequences, but it is not itself architecture evidence and must not be treated as proof of project state. The supporting evidence remains in Archon tool and resource responses.

## MCP response envelope

An MCP response envelope is the shared outer shape returned by Archon MCP tools and resources. It carries the operation name, optional snapshot identity, summary, confidence, structured facts, evidence references, finding references, unknowns, warnings, limit metadata, and suggested follow-ups so AI clients can preserve evidence, uncertainty, and boundedness consistently across workflows.

## MCP verification endpoint

An MCP verification endpoint is a narrow HTTP route mapped by the Archon MCP host for local development and automated tests, such as `/mcp/tools/archon.search`, `/mcp/resources`, or `/mcp/prompts`. These routes exercise the same read-only handlers used by MCP registration, but they are not a general-purpose HTTP API and must not expose arbitrary shell, SQL, Cypher, filesystem, source-mutation, or database-mutation behavior.

## MCP capability inventory

The MCP capability inventory is the current list of registered operational capabilities, tools, resource URI patterns, prompt operations, and prompt templates that the Archon MCP host supports. The inventory is documented in the MCP reference so contributors can see the complete read-only product surface without reading implementation classes.

## Current snapshot selection

Current snapshot selection is the process of resolving a resource selector such as `archon://snapshot/current` to exactly one concrete `snapshot://` stable key inside an explicit repository and optional solution scope. If no snapshot matches, Archon returns `NotFound`; if multiple snapshots tie for current selection, Archon returns `Ambiguous` with candidate stable keys instead of choosing one arbitrarily.

## Resource URI

A resource URI is the stable address used to read an MCP resource. Archon resource URIs must use the `archon://` scheme, a supported family such as `snapshot`, `rules`, `hotlist`, `hotspots`, `project`, or `symbol`, and only the query parameters supported by that family. Current resources use repository and optional solution scope; parameterized project, symbol, and snapshot diff resources carry percent-encoded stable keys in the path. Duplicate, malformed, unsupported, or ambiguous resource URIs fail before query execution.

## Parameterized MCP resource

A parameterized MCP resource is an `archon://` resource whose path contains an exact stable key rather than the `current` selector. Current parameterized resources include project context by `project://` key, symbol context by `symbol://` key, and explicit snapshot diff context by two `snapshot://` keys. They reuse approved read-only tool workflows and keep the resource operation name `archon.read_resource`.

## MCP snapshot diff resource

An MCP snapshot diff resource is the `archon://snapshot/{snapshotId}/diff/{previousSnapshotId}` resource form of explicit snapshot comparison. It returns summary counts and optional bounded details using stable keys, fingerprints, evidence references, unknowns, and truncation metadata without inferring previous snapshots or exposing raw graph records.

## MCP architecture-rule catalog review

MCP architecture-rule catalog review is the `archon.get_architecture_rules` tool behavior for listing bounded versioned rule catalog records. It returns rule identity, enabled state, category, severity, description, applicable scopes, and explicit unknowns for details not supplied by the current catalog query, without creating, editing, enabling, disabling, deleting, or re-evaluating rules.

## MCP hotlist finding review

MCP hotlist finding review is the `archon.get_hotlist_findings` tool behavior for listing bounded persisted findings with rule identity, severity, status, confidence, affected stable nodes, evidence references, metadata, unknowns, deterministic sorting, and truncation metadata. It is a read-only triage surface and does not expose suppression or finding mutation.

## MCP snapshot diff comparison

MCP snapshot diff comparison is the `archon.get_snapshot_diff` tool behavior for comparing explicit snapshots or latest comparable snapshots through stable keys and fingerprints. It returns summary counts, optional bounded details, fingerprints, evidence references, unknowns, no-change warnings, and truncation metadata without creating snapshots, deleting snapshots, mutating graph records, or accepting arbitrary graph queries.

## MCP project description

MCP project description is the `archon.describe_project` tool behavior that expands a project stable key or unambiguous project name into a bounded project-level response. It returns project identity, path, language, target framework, project format, application type, dependencies, packages, runtime facts, findings, evidence, warnings, and unknowns from the approved project query layer.

## MCP dependency traversal

MCP dependency traversal is the `archon.get_dependencies` and `archon.get_dependents` tool behavior for walking dependency-like graph relationships from one stable node or project identity. It supports direct and transitive modes, bounded depth, controlled edge-kind filters, stable node and relationship records, evidence references, and truncation metadata without accepting arbitrary graph queries.

## MCP dependency path search

MCP dependency path search is the `archon.find_dependency_paths` tool behavior for finding bounded ordered paths between two stable graph node identities. It distinguishes found paths, no-path results, and unavailable graph data while returning stable nodes, stable relationships, evidence references, explicit unknowns, and truncation metadata without accepting arbitrary Cypher or source inspection.

## MCP symbol description

MCP symbol description is the `archon.describe_symbol` tool behavior that expands a stable symbol key, or an exact unambiguous symbol search text, into symbol identity, containment, owning project, source context, semantic relationships, evidence, findings, warnings, and unknowns from the approved symbol query layer.

## MCP symbol usage investigation

MCP symbol usage investigation is the `archon.find_symbol_usages` tool behavior for listing bounded callers, references, calls, and other persisted usage relationships for one stable symbol. It returns stable relationship identities, source and target symbol keys, safe source context, evidence references, confidence, unknowns, and limit metadata without reading source files directly.

## MCP data-access usage review

MCP data-access usage review is the `archon.get_data_access_usage` tool behavior for listing bounded LINQ to SQL, Entity Framework, ADO.NET, typed DataSet, raw SQL, stored procedure, table, entity, and data-context usage facts. It returns stable data-access identities, broad operation kinds, dynamic SQL indicators, confidence, unknowns, evidence references, and truncation metadata without executing SQL, opening database connections, exposing connection strings, or reading source files directly.

## Dynamic SQL indicator

A dynamic SQL indicator is a persisted signal that data-access extraction saw SQL composition, command text, or target selection that could not be resolved deterministically to a complete table, column, entity, or stored-procedure identity. MCP and query responses report this as explicit unknown state so contributors do not treat partial raw SQL evidence as complete impact knowledge.

## MCP change-impact assessment

MCP change-impact assessment is the `archon.assess_change_impact` tool behavior for walking incoming graph relationships from one supported stable target to find direct and transitive consumers. It returns bounded impact records, evidence references, confidence, unknowns, warnings, and safe read-only follow-ups, and it frames output as investigation guidance rather than automatic remediation or code-change instruction.

## Direct impact

A direct impact is a one-hop incoming relationship from an impacted node to the changed target in a bounded impact assessment. Direct impact is useful for identifying immediate consumers, but it is not proof of the full blast radius when traversal limits, dynamic dispatch, or unsupported extraction families apply.

## Transitive impact

A transitive impact is a broader incoming relationship discovered through one or more intermediate graph nodes within a bounded depth. Transitive impact helps contributors reason about downstream consumers, but it must be read together with traversal depth, edge filters, truncation metadata, confidence, and unknowns.

## Result-type filter

A result-type filter is a controlled `archon.search` value that narrows broad search output to approved record families such as `Project`, `Symbol`, `RuntimeEndpoint`, `Fact`, `Evidence`, `Finding`, or `Metric`. It is not an arbitrary predicate language and cannot be used to submit Cypher, SQL, regular expressions, or filesystem search expressions.

## Suggested follow-up

A suggested follow-up is a safe next investigation path included in an MCP or query response. It may point to a supported Archon MCP operation, resource, controlled API route, or user question with stable-key parameters; it must not tell the caller to run arbitrary commands, edit code, inspect local files directly, or bypass Archon's query boundaries.

## Caller context

A caller context is the provider-neutral identity record used by the MCP host before executing an operation. It can include a stable caller identifier, display name, and role names, but it deliberately excludes access tokens, bearer credentials, passwords, raw claims payloads, and identity-provider-specific objects.

## Allow-list

An allow-list is a configuration-defined set of operation names or resource families that are permitted to run. In the MCP host, an omitted operation is treated as disabled once an explicit allow-list is configured, and disabled operations fail before application/query dependencies are invoked.

## Fail closed

Fail closed means Archon denies work when security state is missing, malformed, disabled, or uncertain. For MCP operations, missing authentication maps to an unauthorized error and disabled operations map to a forbidden error before the operation delegate can query architecture data.

## Audit trail

An audit trail is the safe sequence of operation-attempt records used to understand security-relevant activity. MCP audit events include caller identity when available, operation name, sanitized parameter metadata, result status, truncation state, error category, and duration while excluding secrets, credentials, raw evidence snippets, stack traces, and connection strings.

## Redaction

Redaction is the process of replacing sensitive values with a safe marker such as `[redacted]`. Archon applies redaction to MCP evidence snippets and audit parameter values that look like passwords, tokens, API keys, account keys, credentials, private keys, certificates, or connection strings, including nested secret-like fragments inside otherwise ordinary text.

## Prompt injection

Prompt injection is untrusted analyzed content that attempts to override an AI client's higher-priority instructions or trick it into revealing secrets, executing commands, mutating files, or treating source comments, markdown, string literals, configuration text, or rule metadata as authority. Archon labels extracted MCP evidence as untrusted repository data and keeps privileged instruction text separate from that evidence.

## Dashboard summary

A dashboard summary is a compact, non-visual query response over one repository, optional solution, and resolved snapshot. It reports high-level counts, bounded top-hotspot rows, latest-change summary rows, warnings, and explicit unknowns so API and future MCP consumers can start architecture review without arbitrary graph access.

## Project catalogue

A project catalogue is the bounded WP014 query response that lists project architecture nodes for one repository, optional solution, and resolved snapshot. It supports approved search, filters, deterministic sorting, pagination, stable project keys, aggregate dependency and package counts, endpoint counts, data-access indicators, hotlist counts, risk indicators, confidence, unknown state, and evidence references without exposing Neo4j internal IDs or unrestricted graph traversal.

## Project detail

A project detail response expands one exact project stable key or one unambiguous project display name into a controlled project-level view. It includes the project summary, responsibilities, evidence references, direct references, dependents, packages, application type, endpoints, workers, data-access indicators, configuration keys, integrations, hotlist findings, scoped graph summary, warnings, unknowns, and sanitized metadata.

## Stable project key

A stable project key is the durable `project://...` identity for a project architecture node. It is usually based on the repository-relative project path, such as `project://src/Customer.Api/Customer.Api.csproj`, and is the identity clients should use when following project catalogue rows to project detail responses.

## Scoped graph summary

A scoped graph summary is a compact aggregate over one selected project and its directly owned or directly related graph facts. In the project detail API it reports counts such as owned node count, outgoing dependency count, incoming dependency count, endpoint count, data-access count, and integration count; it is intentionally narrower than an arbitrary graph traversal.

## Bounded graph traversal

Bounded graph traversal is the WP014 query API pattern for walking approved architecture graph relationships from one stable node identity under explicit depth, direction, edge-kind, and result-size limits. It returns stable node and edge DTOs with evidence references and truncation metadata rather than accepting arbitrary Cypher or exporting raw graph records.

## Dependent

A dependent is a graph node that has an incoming dependency-like relationship to a selected node. If `project://src/Customer.Api/Customer.Api.csproj` has a `REFERENCES` edge to `project://src/Customer.Domain/Customer.Domain.csproj`, then the API project is a dependent of the domain project.

## Dependency path

A dependency path is an ordered sequence of stable graph nodes and stable graph edges showing how one source node reaches one target node by following outgoing dependency-like relationships within a bounded depth. WP014 path responses distinguish a found path, a no-path result, and unavailable graph data.

## Edge kind

An edge kind is the controlled graph vocabulary value that classifies a relationship, such as `REFERENCES`, `USES_PACKAGE`, `CALLS`, or `DEPENDS_ON`. Traversal endpoints accept only registered edge kinds so clients can narrow graph exploration without submitting arbitrary predicates.

## Graph neighbourhood

A graph neighbourhood is the bounded set of incoming, outgoing, or both-direction graph edges around one selected node, together with the stable nodes required to explain those edges. WP014 neighbourhood reads default to one hop and report truncation when the bounded result limit omits additional matches.

## Symbol query

A symbol query is the WP014 controlled API surface over persisted Roslyn semantic facts. It exposes bounded search, detail, and usage reads for namespace, type, method, property, and field nodes by stable public identities rather than by Neo4j internal IDs or arbitrary source-code search.

## Fully qualified name

A fully qualified name is the compiler-style symbol name that includes namespace and containing-type context when available. Symbol query responses include it so clients can distinguish symbols that share a short display name.

## Source context

Source context is the safe location information associated with a semantic fact or usage. In the symbol query API it can include repository-relative file path, line range, and a bounded snippet preview, but it is not a full source-code export.

## Symbol usage

Symbol usage is a persisted semantic relationship showing that one symbol references, calls, implements, inherits from, or handles another symbol. WP014 usage reads expose incoming and outgoing relationships with evidence, confidence, line-range, and unknown-state metadata.

## Unresolved symbol

An unresolved symbol is a semantic fact whose complete compiler target identity could not be proven from the available compilation references or source context. Archon reports unresolved symbol state as explicit unknown metadata instead of implying that the symbol or its usages are complete.

## Runtime query

A runtime query is the WP014 controlled API surface over persisted runtime facts. It exposes bounded endpoint, controller/handler, entry-point, and worker reads by stable public identities rather than by Neo4j internal IDs, runtime reflection, or target-application startup.

## Fact query

A fact query is the WP014 controlled API surface over persisted architecture facts that are not themselves runtime endpoint or worker records. It exposes bounded data-access, configuration usage, external integration, and backend UI-technology reads with stable public identities, safe metadata, confidence, unknown state, and evidence references rather than arbitrary graph access.

## Evidence drill-down

Evidence drill-down is the WP014 controlled API surface that resolves a stable evidence key, or evidence related to a stable node, edge, finding, metric, or rule identity, into source-location explanation data. It exposes repository-relative file path, line range, symbol context, bounded snippet preview, confidence, classification, unknown reason, and related stable records without reading additional source files or exposing Neo4j internals.

## Snippet preview

A snippet preview is the short source or configuration text persisted with an evidence record for explanation. Query APIs treat previews as untrusted display text, bound their length, report truncation metadata when they are shortened, and redact previews that look secret-like.

## Unknown reason

An unknown reason is the safe explanatory text attached to explicit unknown state. It tells contributors why a graph fact, evidence record, metric, finding, or query result is incomplete, inferred, or unavailable instead of leaving clients to interpret missing data as known absence.

## Classification

Classification is the controlled knowledge category that explains how Archon knows a graph fact or evidence record is valid, such as an established fact or an explicit unknown. In evidence query responses, classification comes from the evidence record's knowledge kind and helps clients distinguish proven source context from incomplete or inferred context.

## Data-access fact

A data-access fact is a persisted graph fact about a persistence technology or persistence usage site, such as a LINQ to SQL DataContext, Entity Framework DbContext, ADO.NET command, typed DataSet TableAdapter, raw SQL call, stored procedure, entity, database table, or table read/write relationship. WP014 data-access fact reads expose stable identities and safe operation indicators without returning connection strings or unbounded SQL text.

## Configuration provider

A configuration provider is the source family that supplies application settings, such as JSON files, environment variables, user secrets, or another extractor-recorded provider. WP014 configuration usage reads report provider names and source evidence paths when known, but they do not expose the configuration values supplied by those providers.

## External integration

An external integration is a dependency on a system, service, or transport outside the local code unit, such as an HTTP service, broker queue, publish-subscribe topic, storage endpoint, email service, payment provider, or generated service client. Archon exposes integration facts with safe host or service metadata and omits credentials, tokens, connection strings, paths, and query-string secrets.

## Protocol

A protocol is the communication family or transport hint associated with an integration, such as HTTP, gRPC, SOAP, WCF, Azure Service Bus, SMTP, or another extractor-provided value. Protocol values help contributors group integration behavior without requiring live external calls.

## UI-technology fact

A UI-technology fact is a persisted static fact about a UI application, component, page, view, route, layout, control, resource, style, binding, command, or view model. WP014 UI-technology reads expose these facts as backend API data for Blazor, Razor, Windows Forms, WPF, WinUI, .NET MAUI, and Avalonia without rendering UI, loading designers, starting dispatchers, or adding Discovery UI assets.

## Runtime endpoint

A runtime endpoint is an HTTP or service-facing runtime fact such as an ASP.NET Core route. Archon query responses can include method, route, owning project, controller or handler, action or implementation method, request and response DTO names, authorization attributes, service/data-access/configuration indicators, evidence stable keys, confidence, and unknown state.

## Controller or handler

A controller or handler is a runtime implementation node that owns or handles an endpoint, message, scheduled job, or related runtime action. Stable-key lookup is preferred because display names can repeat across projects, generated sources, or partial declarations.

## Runtime entry point

A runtime entry point is the project or runtime artifact that starts an application process or host loop, such as an API bootstrap, worker host, console `Program.Main`, or service-host configuration artifact. WP014 entry-point reads connect that bootstrap to hosted services, endpoint stable keys, safe configuration key names, and evidence references.

## Hosted service

A hosted service is a .NET background component registered with the application host lifetime. Archon records hosted-service facts statically and query responses expose them as worker-oriented runtime data without starting the target application.

## Background service

A background service is the common long-running .NET hosted-service pattern based on `BackgroundService`. In runtime query responses, background services appear as worker records with hosted-service identity, project context, data-access or integration indicators, evidence, and unknown-state metadata.

## Queue consumer

A queue consumer is a message-driven runtime fact that represents code consuming messages from a queue. WP014 worker reads expose safe queue names, transport hints, handler stable keys, evidence stable keys, confidence, and unknowns when the target was inferred or incomplete.

## Topic consumer

A topic consumer is a message-driven runtime fact that represents code consuming messages from a publish-subscribe topic, optionally through a subscription. Archon exposes safe topic and subscription metadata without secrets or live broker access.

## Scheduled job

A scheduled job is timer- or cron-like runtime behavior discovered statically from source or configuration. Runtime query responses can report a safe schedule description and handler identity, while unknown metadata remains explicit when schedule extraction was partial.

## Response envelope

A response envelope is the outer object that carries endpoint, tool, or resource data together with cross-cutting metadata. WP014 API responses use envelopes for scope metadata, snapshot metadata, pagination or non-paged metadata, truncation metadata, warnings, unknowns, and request trace metadata. WP015 MCP responses use a related shared envelope with operation identity, optional snapshot identity, summary, confidence, facts, evidence, findings, unknowns, warnings, limits, and suggested follow-ups so AI clients receive bounded and evidence-backed context.

## MCP response envelope

An MCP response envelope is the common Archon MCP success shape returned by future tools and resources. It keeps natural-language summary text grounded in structured facts, evidence references, findings, explicit unknowns, warnings, and limit metadata rather than allowing an AI client to infer unsupported architecture claims.

## Structured MCP error

A structured MCP error is the common failure shape for validation, unsupported operation, not-found, ambiguity, unauthorized, forbidden, dependency unavailable, query-layer failure, and server error cases. It exposes stable category, code, safe message, warnings, and suggested follow-ups while omitting stack traces, exception type names, credentials, connection strings, raw sensitive evidence, and Neo4j internal IDs.

## Suggested follow-up

A suggested follow-up is a safe next investigation step carried by an MCP response. For MCP, follow-ups must point to supported Archon operations, supported resources, controlled API routes, or safe user questions; they must not suggest arbitrary shell execution, SQL, Cypher, filesystem access, code modification, or direct Neo4j browsing.

## Scalar API reference

A Scalar API reference is the interactive browser documentation surface that reads Archon's generated OpenAPI document. The API host maps Scalar at `/scalar/v1` in the Development environment so contributors can inspect implemented route metadata, request shapes, response schemas, validation responses, and safe error contracts without introducing Swagger UI or a product Discovery UI.

## OpenAPI document

An OpenAPI document is the machine-readable HTTP API description generated from ASP.NET Core endpoint metadata. Archon serves the development document at `/openapi/v1.json`, and Scalar uses that document to render the API reference for query, extraction, management, health, and readiness endpoints.

## Snapshot selector

A snapshot selector is the request value that chooses which snapshot state powers a query. WP014 dashboard summary supports exact `snapshot://...` stable keys and a deterministic `latest` selector resolved within the required repository and optional solution scope.

## Dependency cycle

A dependency cycle is a directed path through dependency-like architecture edges that returns to the starting node. Archon reports cycles through stable node keys, stable edge keys, evidence references, confidence, unknown state, truncation metadata, and a `cycle://` stable key rather than Neo4j internal IDs.

## Snapshot diff

A snapshot diff is a deterministic comparison between two persisted architecture snapshots. Archon matches records by stable key, compares fingerprints, and classifies nodes, edges, findings, and metrics as added, removed, changed, or unchanged.

## Latest-to-previous diff

Latest-to-previous diff is the snapshot diff mode that resolves the two newest comparable snapshots inside one repository and optional solution scope before comparing them. It supports the same stable-key and fingerprint classification as explicit diff while saving callers from listing snapshots for the common “what changed since the last extraction?” question.

## Changed record

A changed record is a snapshot diff row whose stable key exists in both compared snapshots but whose normalized fingerprints differ. The stable key preserves logical continuity, while the fingerprint difference signals content drift.

## Cross-domain search

Cross-domain search is the WP014 controlled query surface that searches safe public fields across supported result families such as projects, symbols, runtime endpoints, facts, evidence, findings, and metrics. It returns stable keys, confidence, evidence references, unknown state, related nodes, and deterministic follow-up affordances instead of raw graph predicates or direct Neo4j access.

## Follow-up affordance

A follow-up affordance is a machine-readable next query suggestion returned with a search result. It contains a label, a controlled API route, and stable query parameters so API and future MCP clients can continue from a broad search hit into a more specific endpoint without inventing graph queries.

## Controlled management operation

A controlled management operation is an allowlisted operational read or write exposed by the WP014 management API. It validates a defined request shape, records audit-ready metadata when state changes, and rejects arbitrary graph mutation, database commands, shell execution, filesystem mutation, and code modification.

## Metadata allowlist

A metadata allowlist is the approved set of management metadata fields that callers may set on supported targets. Archon uses an allowlist so operational annotations can be useful without turning metadata updates into unrestricted graph property edits.

## Snapshot lifecycle row

A snapshot lifecycle row is the management API view of one snapshot header. It exposes stable snapshot and repository identities, optional solution identity, lifecycle status, branch or commit metadata, timestamps, and diagnostic counts without exposing database-local identifiers or infrastructure details.

## Retention boundary

A retention boundary is the explicit scope and rule set that limits snapshot cleanup. In the current management API it includes repository scope, optional solution scope, `keepLatest`, optional `deleteBeforeUtc`, and dry-run state so cleanup candidates cannot escape the intended lifecycle scope.

## Audit-ready metadata

Audit-ready metadata is the small operational record attached to accepted management actions. It contains a normalized actor, UTC timestamp, and correlation identity so a later review can connect API responses and logs without storing secrets or arbitrary request payloads.

## Controlled maintenance

Controlled maintenance is the management API pattern for operational commands chosen from an explicit allowlist, such as validating a rule cache or compacting local management state. It returns outcome, warnings, errors, and audit-ready metadata, and it rejects raw Cypher, SQL, shell commands, filesystem mutation commands, and code modification requests.

## Readiness

Readiness is the sanitized operational status that tells automation whether required dependencies are available for the management and query surface. Archon readiness reports public dependency names and coarse states, not connection strings, credentials, raw exception details, or infrastructure endpoint secrets.

## Health

Health is the sanitized operational status that tells local development and monitoring whether the management module can respond. It is intentionally less detailed than readiness and should not expose dependency internals or secrets.

## Truncation metadata

Truncation metadata describes when a bounded query omitted matching data because of a configured page, nested-section, traversal-depth, or result-size limit. For paged detail rows it records how many rows matched, how many were returned, and which skip/take bounds applied; for traversal responses it records whether edge results were cut down by the applied limit and gives a safe reason.

## Validation problem

A validation problem is the structured client-error response returned when a controlled API request is malformed or unsupported. WP013 query endpoints use validation problems for missing snapshot identities, invalid paging values, unsupported snapshot diff domains or change kinds, missing snapshots, and incompatible snapshot comparisons so callers can correct requests without inspecting server logs.

## Public metadata sanitizer

The public metadata sanitizer is the application-layer safeguard that removes metadata properties with unsafe or secret-like names before rule detail, finding detail, metric, cycle, hotspot, or architecture-rule DTOs cross the API boundary. It preserves safe lower camel case diagnostic metadata but omits fields whose names suggest passwords, secrets, tokens, or connection strings.

## Cycle path

A cycle path is the ordered node and edge sequence that explains a dependency cycle. The node path repeats its first node at the end so the closure is explicit, while the edge path contains one stable edge key for each hop.

## Cycle participation

Cycle participation is the graph metric value that counts how many returned canonical dependency cycles contain an architecture node. It helps contributors find nodes that are part of circular dependency structures before inspecting the path-level cycle records.

## Copied-output rule loading

Copied-output rule loading is the rule catalog runtime model where repository-root `rules/**/*.json` files are copied to build or publish output under `rules/`, and the loader reads `AppContext.BaseDirectory/rules` instead of walking back to the source repository. This keeps local tests, deployed binaries, and host initialization aligned.

## Rule identity

Rule identity is the stable pair of `ruleCode` and `version`. Findings must preserve both values so historical results remain explainable after an authored rule evolves.

## Detection DSL

The detection DSL is the JSON detection language used by rule files. The current loader validates `nodeKinds`, `match`, `conditions`, nested `groups`, condition kinds, operators, and operator/payload compatibility; the current evaluator applies the same structure to fixture graph facts with recursive `all`, `any`, and `none` semantics.

## Candidate node

A candidate node is a graph node that remains eligible for rule evaluation after the evaluator applies a rule's `nodeKinds` filter. Candidate filtering happens before condition evaluation so rules inspect only the graph node kinds they explicitly target.

## Rule evaluation warning

A rule evaluation warning is a deterministic diagnostic emitted when a rule can only be evaluated partially for a candidate node, such as when an expected graph fact collection or named metric is unavailable. Warnings do not invent facts and do not necessarily prevent an `any` group from matching through another branch.

## Deterministic rule diagnostic

A deterministic rule diagnostic is a stable validation result emitted by the rule catalog loader with a machine-readable code, developer-facing message, file context, and JSON path or parse location when available. Deterministic diagnostics let rule authors fix catalog content without depending on load order or incidental exception text.

## Blazor component

A Blazor component is a `.razor` artifact that can combine markup, Razor directives, and C# code. Archon's current UI extraction slice records source `.razor` components as `UiComponent` graph nodes without rendering them or starting the target application.

## Blazor route

A Blazor route is a literal `@page` directive in a Razor component. Archon records supported route templates as `UiRoute` nodes and `DECLARES_UI_ROUTE` relationships, while malformed or missing route templates become explicit unknown route facts with source evidence.

## Razor Page

A Razor Page is a server-rendered `.cshtml` artifact under a `Pages` folder. Archon extracts Razor Pages statically as `UiPage` facts, records literal or conventional route metadata, links page models and handler methods when deterministic source evidence exists, and treats runtime-computed page routes or navigation targets as explicit unknowns.

## MVC Razor view

An MVC Razor view is a server-rendered `.cshtml` artifact under a `Views` folder. Archon extracts MVC Razor views statically as `UiView` facts and links conventional `Views/{Controller}/{Action}.cshtml` artifacts to matching controller action source only when deterministic evidence exists.

## Tag helper

A tag helper is an ASP.NET Core Razor feature that augments HTML-like elements with server-side attributes such as `asp-page`, `asp-controller`, `asp-action`, and `asp-page-handler`. Archon records tag-helper usage from static markup and `_ViewImports.cshtml` context but does not execute tag helpers or render their output.

## Windows Forms application

A Windows Forms application is a desktop .NET application built around `System.Windows.Forms`, forms, controls, resources, and a message loop. Archon extracts Windows Forms projects statically from project files, C# or VB.NET source, designer partials, and `.resx` resources; it does not load designers, instantiate controls, or start the target application.

## Designer partial

A designer partial is a source file such as `MainForm.Designer.cs` or `MainForm.Designer.vb` that contains tool-maintained Windows Forms initialization code for a partial form or user-control class. Archon reads designer partials as static source evidence for controls, hierarchy, events, and bindings, but it does not execute `InitializeComponent`.

## Windows Forms data binding

Windows Forms data binding connects a control property to a data source path through APIs such as `DataBindings.Add("Text", source, "CustomerName", true)`. Archon records literal binding paths as `Binding` graph facts and treats runtime-computed binding sources as unsupported static-analysis gaps rather than evaluating application state.

## WPF application

A WPF application is a Windows Presentation Foundation desktop application whose UI is commonly described in XAML and connected to code-behind source. Archon extracts WPF artifacts statically from project metadata, application definitions, XAML files, C# or VB.NET source, resource dictionaries, and code-behind dependency hints; it does not load XAML, instantiate controls, start a dispatcher, or run the target application.

## WPF resource dictionary

A WPF resource dictionary is a XAML resource container that can define reusable brushes, styles, templates, and other keyed objects. Archon records visible resource keys, styles, and templates as UI resource or style facts and treats dynamic resource lookup as an explicit unknown because WPF resolves those targets at runtime.

## WPF binding

A WPF binding is a XAML expression such as `{Binding CustomerName}` or `{Binding Path=CustomerName}` that connects a UI property to a data-context path. Archon records static binding paths as `Binding` graph facts and emits explicit unknowns for bare or runtime-computed bindings.

## Routed event

A routed event is a WPF event that can travel through the element tree and be handled by source-visible handler methods such as `Click="SaveButton_Click"`. Archon records visible routed-event handlers as command-style graph facts, but it does not execute the handler or prove that the event fires at runtime.

## WinUI application

A WinUI application is a modern Windows desktop application built with the Windows App SDK and `Microsoft.UI.Xaml`. Archon extracts WinUI projects statically from project metadata, XAML artifacts, package manifests, C# or VB.NET source, and code-behind dependency hints; it does not load XAML, instantiate controls, start a dispatcher, validate MSIX packages, or run the target application.

## Windows App SDK

The Windows App SDK is the Microsoft platform SDK used by modern WinUI desktop applications. Archon treats package references such as `Microsoft.WindowsAppSDK` and source references such as `Microsoft.UI.Xaml` as static evidence that a project may contain WinUI UI facts.

## Package manifest

A package manifest is an XML artifact such as `Package.appxmanifest` that describes a Windows app package identity, display name, publisher, version, application entry point, and visual metadata. Archon records safe package identity metadata for WinUI extraction and represents missing or ambiguous package identity as an explicit unknown rather than validating or signing the package.

## Navigation frame

A navigation frame is a WinUI or XAML control that hosts navigable page content, commonly used through calls such as `Frame.Navigate(typeof(DetailsPage))`. Archon records static frame navigation targets as `NAVIGATES_TO` graph relationships and treats runtime-computed navigation targets as explicit unknowns.

## .NET MAUI application

A .NET MAUI application is a cross-platform .NET client application that can target multiple platform heads from one project. Archon extracts MAUI projects statically from project metadata, `UseMaui`, `MauiProgram` source, XAML artifacts, package references, platform folders, and code-behind dependency hints; it does not install MAUI workloads, load XAML, start platform applications, or run the target application.

## MAUI Shell

MAUI Shell is the application-level navigation and layout structure commonly declared in `AppShell.xaml`. Archon models Shell as a `UiLayout` fact and records static Shell route declarations or registrations as `UiRoute` facts without executing Shell navigation.

## Shell route

A Shell route is a MAUI route template declared in markup through attributes such as `Route="main"` or in source through calls such as `Routing.RegisterRoute("details", typeof(DetailsPage))`. Archon records static Shell routes as `DECLARES_UI_ROUTE` relationships and treats computed route names as explicit unknowns.

## Platform head

A platform head is the platform-specific entry point and assets that let a shared MAUI application run on a target such as Android, iOS, Mac Catalyst, Windows, or Tizen. Archon records platform heads as normalized metadata from target frameworks and `Platforms/{PlatformName}` folders rather than as separate graph nodes.

## MAUI handler

A MAUI handler maps a cross-platform control abstraction to a platform-specific implementation, commonly registered through `ConfigureMauiHandlers` and `AddHandler`. Archon records visible handler registrations as command-style facts with control and handler metadata because the current graph vocabulary does not define a dedicated handler node kind.

## Avalonia application

An Avalonia application is a cross-platform .NET client application whose UI is commonly declared in `.axaml` files and started through Avalonia desktop lifetime setup. Archon extracts Avalonia projects statically from package references, AXAML artifacts, startup source, view-locator source, ReactiveUI source, and code-behind dependency hints; it does not load AXAML, instantiate controls, start desktop lifetimes, render windows, or run the target application.

## AXAML

AXAML is Avalonia's XAML dialect and uses the `.axaml` file extension. Archon treats AXAML as static markup evidence for applications, windows, user controls, styles, resources, bindings, commands, events, and project-local component usage without evaluating the Avalonia runtime.

## Avalonia view locator

An Avalonia view locator is source code, commonly an `IDataTemplate`, that maps a view-model object to a view control. Archon records static view-model-to-view mappings when they are visible in source and emits explicit unknowns for convention-based or reflection-based locators that depend on runtime type creation.

## ReactiveUI relationship

A ReactiveUI relationship is the connection between an Avalonia reactive view, such as `ReactiveWindow<TViewModel>` or `ReactiveUserControl<TViewModel>`, and the view model type carried by its generic argument or navigation flow. Archon records generic relationships statically and treats non-generic or runtime-only ReactiveUI wiring as ambiguous unknown evidence.

## Unified UI/client stage

A unified UI/client stage is the WP011 API extraction pipeline stage with the stable identifier `wp011-ui-client`. It coordinates the framework-specific Blazor, Razor Pages, MVC Razor, Windows Forms, WPF, WinUI, .NET MAUI, and Avalonia adapters through one API-triggered extraction path, then relies on the shared accumulator to deduplicate nodes, relationships, and evidence by stable key.

## Classic ASP.NET application

A classic ASP.NET application is a legacy `System.Web` web application whose runtime surface can be declared across project references, `Global.asax`, `web.config`, Web Forms markup, MVC 5 controllers, Web API 2 controllers, handlers, modules, and route configuration. Archon extracts these artifacts statically and does not run the target application.

## ASP.NET Core controller

An ASP.NET Core controller is a class that participates in MVC-style HTTP request handling. Archon recognizes supported controller classes from controller naming, controller base types, and marker attributes, then records `Controller` nodes and controller-to-action endpoint relationships when deterministic source evidence exists.

## ASP.NET Core middleware registration

An ASP.NET Core middleware registration is a startup pipeline call such as `UseRouting`, `UseAuthorization`, or `UseMiddleware<T>()`. Archon records supported calls as project-level runtime metadata in source order because the current graph contract does not yet define a dedicated middleware node kind.

## Application type classification

Application type classification is the WP005 project metadata value that places a supported project into a broad category such as ASP.NET Core Web App, ASP.NET Core Web API, Classic ASP.NET Web App, Web Forms App, MVC App, Web API 2 App, Console App, Worker Service, Class Library, Test Project, Tooling Project, or Unknown. Archon records confidence, evidence, and unknown reasons with the classification so consumers can distinguish direct project-file evidence from weaker artifact or naming indicators.

## Architecture relationship

An architecture relationship is the domain fact that one architecture node relates to another. Examples include a project referencing a package, a service calling an endpoint, or a component depending on configuration.

## Architecture-rule result

An architecture-rule result is a deterministic query-side result from a generic built-in architecture check over completed snapshot facts. It carries an `architecture-rule://` stable key, rule/check identity, category, status, target stable key, contribution references, confidence, unknown state, metadata, and fingerprint.

## Review required

Review required is an architecture-rule result status that means the graph indicates change risk or policy attention rather than a proven violation. High fan-in shared-library review results use this status.

## Target stable key

A target stable key is the stable key of the graph object that a metric, hotspot, architecture-rule result, or other analysis output is about. It remains a logical graph identity, not a database-local ID.

## External integration

An external integration is a dependency that leaves the analyzed application boundary, such as an outbound HTTP service, REST client, WCF/SOAP/gRPC client, queue, topic, storage target, SMTP/email channel, payment provider, or internal service API. Archon extracts integration facts statically and does not contact live external systems.

## External service node

An external service node is an `ExternalService` architecture node representing a service dependency observed from static source, configuration, package, generated-client, or runtime evidence. Known services use logical stable keys, while unknown services use evidence-scoped placeholder-safe stable keys and explicit unknown reasons.

## Internal service API

An internal service API is an HTTP endpoint exposed by one analyzed project and called by another analyzed project in the same extraction scope. Archon correlates internal service APIs only when prior endpoint facts and client-side route, base URL, or configuration-key evidence prove ownership deterministically.

## Internal service correlation

Internal service correlation is the WP010 process that links a client-side service call to endpoint, controller, method, project, and external-service graph facts from another analyzed project. The correlation is conservative: unresolved ownership, ambiguous routes, computed paths, or missing endpoint facts become explicit unknowns instead of guessed service matches.

## Storage account

A storage account is the Azure Storage identity that owns blob containers, file shares, queues, tables, and related endpoints. Archon records storage-account-related dependencies only from static source or configuration-key evidence; it does not validate account names, read connection-string values, list storage resources, or contact Azure Storage.

## Container

A container is an Azure Blob Storage namespace under a storage account. Archon records deterministic container names as storage target metadata when source-visible `GetBlobContainerClient` evidence proves the name. Runtime-computed container names become explicit unknown external integration facts.

## Share

A share is an Azure File Storage namespace under a storage account. Archon records deterministic share names from `ShareClient` evidence and may combine them with directory or file path hints when source evidence is static. It does not browse share contents.

## Blob path

A blob path is the object name or virtual path inside an Azure Blob Storage container. Archon records blob path hints only when source-visible constants prove them, and it treats computed blob paths as unknown evidence rather than evaluating runtime string construction.

## SMTP host

An SMTP host is the mail server endpoint used by an SMTP client to send email. Archon can record a literal host or a configuration-key dependency, but it does not open SMTP connections, validate credentials, send test messages, or persist recipient and body payload values.

## Payment provider

A payment provider is an external system that processes payment operations, such as a payment SDK service or a project-specific payment gateway. Archon records provider, endpoint-key, operation, and authentication-hint metadata from deterministic static evidence while redacting API keys, card data, payment tokens, and customer payment identifiers.

## Authentication hint

An authentication hint is non-secret metadata that indicates the mechanism a client appears to use, such as API key, bearer token, network credentials, or configuration-backed credentials. Archon records the mechanism when source evidence proves it but never stores the credential value.

## Redaction

Redaction is the process of replacing sensitive text with a safe marker before the value appears in graph metadata, evidence previews, diagnostics, logs, or tests. WP010 uses redaction for endpoints, connection strings, broker secrets, storage account keys, SMTP credentials, payment API keys, tokens, card data, customer payment identifiers, and secret-bearing payload snippets.

## Secret location evidence

Secret location evidence is a safe reference that identifies where a secret-like value was observed without storing the value itself. WP012 security-sensitive rules should match location indicators such as configuration keys, file paths, or sanitized symbol facts rather than passwords, tokens, or full connection strings.

## VB.NET parity limit

A VB.NET parity limit is a documented current implementation boundary where a feature uses Roslyn semantic infrastructure that supports Visual Basic generally, but a specific detector does not yet implement Visual Basic syntax traversal for that evidence shape. WP010 internal service correlation reports this limit explicitly rather than producing false-positive facts from unsupported syntax.

## Queue

A queue is a point-to-point messaging destination where producers send messages and consumers receive or process them. Archon represents deterministic queue targets as `Queue` graph nodes and records producer, sender, receiver, consumer, processor, handler, and abstraction roles as metadata and relationships without connecting to the broker.

## Topic

A topic is a publish-subscribe messaging destination where published messages can be distributed to one or more subscriptions. Archon represents deterministic topic targets as `Topic` graph nodes and records publisher, subscriber, processor, exchange, and routing evidence when source or configuration artifacts prove it.

## Subscription

A subscription is the named consumer view of a topic. In Azure Service Bus and similar brokers, a topic can have multiple subscriptions that each receive matching messages. Archon records subscription names as metadata on topic-related messaging facts when the name is deterministic.

## Message handler

A message handler is source code that processes messages from a queue, topic, subscription, or message bus. Examples include Azure Service Bus processor callbacks, NServiceBus `IHandleMessages<TMessage>` implementations, RabbitMQ consumer callbacks, and MSMQ receive flows. Archon links handlers to queue or topic targets with `HANDLES` relationships when static evidence identifies the target.

## Saga

A saga is a long-running message-driven workflow that coordinates state across multiple messages. In NServiceBus this is commonly a type deriving from `Saga<TData>` and implementing message handlers. Archon records saga relationships as handler-style messaging facts with saga role metadata; it does not execute saga state machines.

## Routing key

A routing key is broker-visible text used by RabbitMQ exchanges to decide which queues receive a published message. Archon records routing keys when they are source-visible constants and treats computed routing keys as unknown evidence rather than querying broker topology.

## Exchange

An exchange is a RabbitMQ routing entity that receives published messages and routes them to queues according to exchange type, bindings, and routing keys. Archon represents deterministic exchange names as topic-style messaging targets because they model publish-subscribe routing outside the analyzed application boundary.

## Recoverability

Recoverability is the messaging behavior that controls what happens after handler failures, such as retry policy and error-queue routing. Archon records deterministic recoverability hints such as NServiceBus error queue names as metadata; it does not inspect or execute broker retry behavior.

## Transport provider

A transport provider is the broker or messaging transport used by a framework or abstraction, such as Azure Service Bus for NServiceBus or RabbitMQ for direct AMQP-style calls. Archon records transport-provider names when source or configuration evidence proves them.

## Named HTTP client

A named HTTP client is an `HttpClient` identity registered or created through `IHttpClientFactory` with a string name, such as `CreateClient("InventoryClient")` or `AddHttpClient("InventoryClient", ...)`. Archon's HTTP/REST integration extractor records the name as deterministic integration evidence and does not instantiate the factory or contact the configured service.

## Typed HTTP client

A typed HTTP client is an application class registered through `AddHttpClient<TClient>(...)` and usually constructed with an `HttpClient` dependency. Archon records the typed-client class as static integration evidence and links configuration-backed or literal base addresses when they are visible in source.

## RestSharp

RestSharp is a .NET REST client library built around `RestClient`, `RestRequest`, method selections, resources, headers, and execute calls. Archon extracts supported RestSharp evidence statically, records resource and operation hints when deterministic, redacts authentication values, and represents dynamic resources as explicit unknowns.

## Generated proxy

A generated proxy is a client type produced from a service description, connected-service definition, service reference, web reference, or protocol schema rather than handwritten application logic. WCF, SOAP/ASMX, and gRPC clients commonly use generated proxies. Archon treats these files and symbols as static evidence, records deterministic endpoint and operation details when available, and never executes proxy constructors or service calls.

## Service contract

A service contract is the interface or schema-defined contract that describes operations exposed by a service client. In WCF this is commonly an interface used as `ClientBase<TContract>` or `ChannelFactory<TContract>`; in gRPC the generated client type reflects operations from a `.proto` service definition. Archon records service contract names as metadata when source or generated artifacts prove them.

## WCF binding

A WCF binding is the client-side transport and protocol configuration used to call a Windows Communication Foundation service, such as `basicHttpBinding`, `wsHttpBinding`, `netTcpBinding`, or a custom binding. Archon extracts binding names and transport hints from source and configuration artifacts without opening channels or resolving endpoints.

## gRPC channel

A gRPC channel is the client transport object used by generated gRPC clients to send calls to an endpoint address. In .NET source this is commonly created through `GrpcChannel.ForAddress(...)`. Archon records literal channel addresses, nearby configuration-key references, generated client types, and method calls when deterministic; runtime-computed channels become explicit unknown integration facts.

## Asynchronous extraction

Asynchronous extraction means the API start request validates and accepts work, records a run, queues the work through a scheduler seam, and returns before later extraction, snapshot assembly, or persistence finishes.

## Bolt-compatible URI

A Bolt-compatible URI is the address used by the Neo4j driver protocol, such as `bolt://localhost:7687`. It is separate from Neo4j's HTTP browser endpoint.

## Composition root

A composition root wires runtime resources and services together. Archon's Aspire AppHost is a composition root: it declares Neo4j, the API host, and the MCP host, but it does not implement domain rules or feature behavior.

## Confidence

Confidence is a deterministic value that describes how certain Archon is about a graph fact. The persisted domain model uses decimal confidence, while the current Roslyn intermediate model uses categories such as `CompilerResolved`, `MetadataOnly`, `Generated`, `PartiallyResolved`, `Inferred`, and `Unresolved` to preserve whether a fact came from compiler binding, external metadata, generated source, deterministic inference, or an explicit semantic gap.

## Configuration key

A configuration key is the logical path used by configuration systems to address a value, usually written with colon separators such as `Service:Endpoint`. Archon's modern configuration extractor normalizes JSON object nesting into this form, and the legacy configuration extractor prefixes XML concepts with categories such as `Legacy:AppSettings:`, `Legacy:ConnectionStrings:`, `Legacy:CustomSections:`, and `Legacy:BindingRedirects:`. Both forms are stored as `ConfigurationKey` nodes with centralized `config://` stable keys.

## ConfigurationManager

ConfigurationManager is the legacy `System.Configuration` API that .NET Framework applications commonly use to read `appSettings` and `connectionStrings` from XML `.config` files. Archon records compiler-bound `ConfigurationManager.AppSettings[...]` and `ConfigurationManager.ConnectionStrings[...]` source usage as `USES_CONFIG` graph facts.

## Global.asax

`Global.asax` is the classic ASP.NET application directive file that can point to an application class and lifecycle methods such as `Application_Start`. Archon records the directive and lifecycle source as classic runtime evidence.

## Constructor injection

Constructor injection is the dependency-injection pattern where a type declares required collaborators as constructor parameters. The current Roslyn C# relationship slice represents compiler-resolved constructor parameter types as `INJECTS` relationships because the constructor signature is deterministic source evidence for that collaboration boundary. The dependency-injection extractor also correlates registered implementation types with constructor parameters and emits deterministic `INJECTS` and `DEPENDS_ON` facts so registered service dependencies can be queried from the DI slice.

## Console entry point

A console entry point is the source method or top-level statement file where a command-line .NET application begins execution. Archon currently detects explicit C# `static Main` methods, explicit VB.NET `Sub Main` or `Function Main` methods, and C# top-level statements from submitted target repository projects, then represents them as `Method` facts with console runtime metadata.

## Background service

A background service is a hosted service that derives from `Microsoft.Extensions.Hosting.BackgroundService`. Archon marks registered implementations as background services when Roslyn proves that inheritance relationship during dependency-injection extraction, and the WP008 worker runtime slice emits source-proven background services as `HostedService` nodes with `runtimeKind` metadata of `BackgroundService`.

## Dependency injection

Dependency injection is the application-composition pattern where services receive collaborators from a container or composition root rather than constructing every dependency directly. Archon's current WP007 slice models Microsoft.Extensions.DependencyInjection registrations as graph facts when Roslyn can prove the registration method and participating types.

## Data access extraction

Data access extraction is Archon's static-analysis process for identifying repository evidence about database-facing technologies and mapping that evidence into the architecture graph. The current WP009 slice supports LINQ to SQL DBML model files, generated LINQ to SQL designer source, LINQ to SQL source usage, EF6 source artifacts, EF Core source artifacts, ADO.NET/raw SQL source usage, and typed DataSet XSD/generated-source/usage evidence. It does not connect to target databases, apply migrations, open ADO.NET connections, execute SQL command text, execute TableAdapter methods, or execute target application code.

## Data adapter

A data adapter is an ADO.NET component, such as `SqlDataAdapter` or `OleDbDataAdapter`, that fills `DataSet` or `DataTable` objects from a command. Archon treats supported adapter `Fill` calls as source evidence for raw SQL execution and conservative table reads when static command text is available.

## DBML

DBML is the XML model format used by LINQ to SQL designers to describe a database model, generated DataContext, mapped entity types, tables, columns, associations, and stored-procedure wrapper methods. Archon parses `.dbml` files as static XML artifacts and records DBML evidence with redacted snippets.

## DataContext

A DataContext is the LINQ to SQL unit-of-work type generated from a DBML model. In the current data-access graph, a DBML `Database` element with a class name becomes a `LinqToSqlDataContext` node.

## DataSet

A DataSet is an ADO.NET in-memory container for tabular data. Archon recognizes ordinary DataSet usage as part of ADO.NET extraction context, especially when a data adapter fills it from static SQL evidence. It also recognizes typed DataSet model roots from `.xsd` artifacts when the XML schema is marked as a generated DataSet model.

## DataTable

A DataTable is an ADO.NET in-memory representation of a table-like result set. In the current WP009 slice, ordinary DataTable variables are context evidence for ADO.NET usage, while typed DataSet XSD table declarations and generated DataTable classes become `Entity` nodes mapped to `DatabaseTable` and `DatabaseColumn` facts when the model metadata is deterministic.

## TableAdapter

A TableAdapter is a Visual Studio generated component in a typed DataSet model that wraps one or more database commands for a typed DataTable. Archon extracts TableAdapter definitions from typed DataSet `.xsd` files, represents generated TableAdapter artifacts as `GeneratedArtifact` nodes, records text queries as raw SQL facts, records stored-procedure commands as `StoredProcedure` facts, and links source methods to tables or procedures when generated TableAdapter methods are called.

## Typed DataSet

A typed DataSet is a legacy ADO.NET model generated from an `.xsd` schema. It provides strongly typed DataSet, DataTable, row, and TableAdapter classes for code that predates or avoids ORM patterns such as Entity Framework. Archon extracts typed DataSet facts statically from XSD artifacts and generated/consumer source; it does not run designer code, instantiate TableAdapters, or connect to the database described by the model.

## XSD model artifact

An XSD model artifact is an XML schema file that can describe a typed DataSet model. In WP009, Archon recognizes XSD files marked with typed DataSet metadata, reads DataSet names, DataTable declarations, column declarations, TableAdapters, query command text, stored procedure references, and connection-string property names, then emits graph facts with redacted XML evidence.

## DbCommand

DbCommand is the abstract ADO.NET command base type used by provider-independent code. Archon records executions through `DbCommand` parameters or variables even when the concrete provider is unknown, and it uses explicit unknown state when command text cannot be resolved statically.

## Database table node

A database table node is an architecture node representing a table identity extracted from data-access evidence. In the current DBML slice, a deterministic `Table Name="schema.Table"` value becomes a `DatabaseTable` node scoped by the DBML model path.

## Database column node

A database column node is an architecture node representing a column identity under a known database table. In the current DBML slice, deterministic `Column` metadata under a known table becomes a `DatabaseColumn` node.

## DbContext

A DbContext is the primary Entity Framework unit-of-work and mapping type. EF6 and EF Core context classes expose `DbSet<TEntity>` properties, provider configuration, model-building methods, and save operations. Archon represents EF6 and EF Core context types as `DbContext` graph nodes and records source-backed relationships from methods that use those contexts.

## DbSet

A DbSet is an Entity Framework property or object that represents a queryable and mutable collection of a specific entity type. Archon uses `DbSet<TEntity>` declarations to identify mapped entity types and conservative table dependencies, then links method usage to `DatabaseTable` nodes when the mapping can be determined.

## Dynamic SQL

Dynamic SQL is SQL text constructed from runtime values, concatenation, interpolation, formatting, or other computed expressions. Archon does not evaluate dynamic SQL. It records partial source evidence, a raw SQL fact, lower confidence, and an explicit unknown reason so consumers can see that a database command exists without trusting a guessed table or procedure target.

## Entity Framework 6

Entity Framework 6, often abbreviated EF6, is the legacy full-framework-era Entity Framework object-relational mapper that commonly uses `System.Data.Entity.DbContext`, `ObjectContext`, `DbSet<TEntity>`, Code First migrations, provider configuration, and raw SQL APIs such as `SqlQuery` or `ExecuteSqlCommand`. Archon extracts EF6 evidence statically from source and does not instantiate contexts or query databases.

## Entity Framework Core

Entity Framework Core, often abbreviated EF Core, is the modern cross-platform Entity Framework object-relational mapper that commonly uses `Microsoft.EntityFrameworkCore.DbContext`, `DbSet<TEntity>`, Fluent API model building, migrations, provider setup calls such as `UseSqlServer`, and raw SQL APIs such as `FromSqlRaw` or `ExecuteSqlRaw`. Archon extracts EF Core evidence statically from source and treats convention-only mappings and shadow properties as explicit uncertainty when exact database identity cannot be proven.

## EF migration

An EF migration is a source artifact that describes database schema changes for Entity Framework. Archon records migration classes and supported operations as generated artifact facts with source evidence. It never applies migrations during extraction.

## Fluent API mapping

Fluent API mapping is Entity Framework model configuration written through chained method calls, usually inside `OnModelCreating`. Examples include `.ToTable(...)`, `.Property(...).HasColumnName(...)`, and relationship configuration calls. Archon extracts deterministic mapping details from supported chains and records unknowns for source shapes that cannot be proven statically.

## ObjectContext

ObjectContext is a legacy Entity Framework context type from `System.Data.Entity.Core.Objects`. Archon represents ObjectContext-derived application types as `DbContext` graph nodes with metadata that identifies the legacy context kind.

## Shadow property

A shadow property is an EF Core model property that exists in the EF model but has no CLR property on the entity class. Archon records supported shadow-property column mappings with explicit unknown state because the database column can be observed from source mapping but there is no normal CLR property declaration to navigate to.

## Legacy container

A legacy container is a dependency-injection container other than Microsoft.Extensions.DependencyInjection, commonly found in older .NET systems. Archon's current dependency-injection extractor recognizes supported compiler-bound registration shapes for Unity, Autofac, Castle Windsor, StructureMap, Ninject, and SimpleInjector, and records unsupported scanning forms as explicit unknown registration facts instead of guessed service mappings.

## Manual factory

A manual factory is project code that creates an implementation behind an abstraction without a container registration call. Archon records narrow deterministic manual-factory patterns as medium-confidence heuristic composition facts when a source method returns an interface and directly constructs a concrete implementation of that interface.

## Custom host loop

A custom host loop is a source method that appears to run continuously or repeatedly, often through a loop combined with delay or cancellation checks. Archon records these as medium-confidence method-level runtime facts because the pattern suggests a service runner or polling loop but does not prove deployment behavior.

## Secret redaction

Secret redaction is the process of replacing sensitive configuration values with a placeholder before they can appear in evidence previews, metadata, diagnostics, logs, or test output. The configuration extractor applies redaction to appsettings values, legacy XML connection-string values, custom section payloads, source snippets, and diagnostics before graph records are created.

## Raw SQL

Raw SQL is command text written directly in source or model artifacts instead of expressed through a higher-level mapping abstraction. Archon stores only redacted previews and stable hashes for static raw SQL text, classifies simple statements with read/write hints, and records unknowns for dynamic or missing command text.

## Read/write hint

A read/write hint is metadata that classifies a data-access relationship as a likely `Read`, `Write`, `ReadWrite`, or `Unknown` operation. Archon derives hints conservatively from API shape and leading SQL verbs such as `SELECT`, `INSERT`, `UPDATE`, `DELETE`, `MERGE`, or `CREATE`; it does not claim full database impact analysis.

## Stored procedure command

A stored procedure command is an ADO.NET command whose `CommandType` is set to `StoredProcedure` and whose command text names a procedure. Archon represents the procedure as a `StoredProcedure` node and links the source method to it with `CALLS_STORED_PROCEDURE` when the name is static.

## Factory registration

A factory registration is a dependency-injection registration where a delegate creates or retrieves the implementation at runtime. Archon records the service type and lifetime for supported Microsoft DI factory overloads but uses explicit unknown state when the concrete implementation cannot be proven from the registration itself.

## Registration wrapper

A registration wrapper is a method that accepts `IServiceCollection` and delegates one or more Microsoft dependency-injection registrations to another method body. Archon's DI extractor follows compiler-bound wrappers when source bodies are available, records invocation-chain metadata, and emits warnings rather than guessed registrations when wrapper source, dynamic dispatch, recursion depth, or cycles prevent deterministic traversal.

## Constraint

A constraint is a Neo4j-enforced rule, such as uniqueness of repository stable keys or snapshot-scoped architecture node identities.

## Controlled value

A controlled value is a domain-owned string identity that behaves like a smart enum. It avoids numeric enum drift by using stable external strings instead of serialized numeric ordinals.

## Evidence-first

Evidence-first means graph facts are designed to carry or link to the explanation that caused Archon to believe them. Evidence can be a project file, source symbol, configuration artifact, compiler diagnostic, inference, or manual annotation.

## Dynamic dispatch

Dynamic dispatch is a C# runtime-binding pattern where a `dynamic` receiver chooses the member target at runtime rather than through compile-time symbol binding. Archon records dynamic dispatch as a semantic unknown instead of inventing a resolved `CALLS` relationship.

## Evidence span

An evidence span is the source line range associated with an evidence record. When XML parsers expose line information, Archon records the exact line for project references, analyzer references, package references, and legacy package entries; otherwise it falls back to file-level evidence.

## Roslyn semantic model

A Roslyn semantic model is the compiler object that answers symbol and type questions for a specific syntax tree in a compilation. Archon uses semantic models so source declarations can be extracted from compiler-resolved symbols rather than from text matching alone.

## Semantic declaration fact

A semantic declaration fact is the Roslyn layer's graph-ready intermediate representation of a source declaration such as a namespace, type, constructor, method, property, or field. VB.NET modules, structures, interfaces, enums, delegates, default properties, events, and constants are projected into this shared declaration vocabulary. A semantic declaration fact carries source language, declaration kind, symbol identity, stable key, project context, parent declaration identity, and semantic evidence.

## Visual Basic default property

A Visual Basic default property is a property that can be accessed through an indexed object expression without writing the property name. Archon projects default properties as `Property` declaration facts and records property-access dependencies when Roslyn resolves the property symbol.

## Visual Basic module

A Visual Basic module is a compiler-backed type whose members are shared by default. Archon projects modules as `Type` declaration facts so module members participate in the same graph vocabulary as C# static classes and other source-declared types.

## Visual Basic root namespace

A Visual Basic root namespace is project-level namespace text that the Visual Basic compiler composes with namespaces declared in source files. When Roslyn exposes the composed namespace in semantic symbols, Archon uses that fully qualified namespace in declaration and relationship facts.

## Visual Basic shared member

A Visual Basic shared member is a member accessed on a type rather than on a particular instance. Archon records calls and dependencies for shared members through the same `CALLS` and `DEPENDS_ON` relationship vocabulary used for C# static members.

## Worker service

A worker service is a .NET application that uses the generic host to run background work, commonly through `IHostedService` or `BackgroundService`, rather than exposing HTTP endpoints as its primary runtime surface. Archon's WP008 worker slice represents source-proven worker components as hosted-service runtime facts and project-level generic-host setup metadata.

## Semantic evidence

Semantic evidence is source evidence captured for compiler-backed declarations and relationships. It includes repository-relative file path, one-based line and column span, symbol name, containing symbol, snippet preview, and snippet hash when source text is available.

## Semantic relationship fact

A semantic relationship fact is the Roslyn layer's graph-ready intermediate representation of a compiler-backed relationship such as `CONTAINS`, `CALLS`, `IMPLEMENTS`, `INHERITS`, `INJECTS`, or `DEPENDS_ON`. It carries deterministic endpoint keys, source and target symbol identity where available, confidence, evidence, relationship metadata, and optional unknown-reason data.

## Semantic stable key

A semantic stable key is the deterministic logical identity used by Roslyn extraction before domain persistence projection. Current C# declaration keys are scoped by source language, project context, fully qualified symbol name, and compiler-facing metadata name or signature; relationship keys are derived from relationship kind, endpoint keys, and a relationship-source qualifier; symbol-reference keys identify relationship endpoints that do not have a source declaration fact.

## Extraction pipeline

The extraction pipeline is the application-layer sequence of deterministic stages that contribute facts, warnings, or errors to the shared accumulator for one accepted run.

## Extraction stage

An extraction stage is one named unit of pipeline work with a stable stage identifier. A stage receives validated input, accepted run context, and the accumulator, then reports whether the pipeline can continue.

## Evidence deduplication

Evidence deduplication means equivalent evidence payloads submitted more than once in the same snapshot collapse to one canonical `ArchonEvidence` node. Equivalent evidence in different snapshots remains separate.

## Fingerprint

A fingerprint is a deterministic hash of diff-relevant graph content. A stable key asks whether two records represent the same logical fact; a fingerprint asks whether the content of that fact changed.

## Generated summary

A generated summary is durable narrative or report-ready content associated with a snapshot or graph target. Summary generation behavior remains later work, but persistence can store supplied generated summaries.

## Generated source

Generated source is code produced by tools, designers, source generators, or build steps rather than directly maintained by contributors. Archon detects deterministic generated-code signals such as `.g.cs`, `.g.vb`, `.Designer.cs`, `.Designer.vb`, generated folders, `obj` paths, and auto-generated headers, then marks semantic facts from that source instead of discarding them.

## Generic host

The generic host is the Microsoft.Extensions.Hosting runtime model that starts an application, manages dependency injection, configuration, logging, lifetime events, and hosted services. Archon's worker runtime slice records deterministic generic-host setup calls as project metadata and does not start or evaluate the target host.

## Handler identity

Handler identity is the deterministic source or symbol identity Archon uses to distinguish runtime handlers such as minimal API delegates, controller action methods, queue message handlers, scheduled-job methods, and entry points. It is used in stable keys and metadata so two runtime facts with the same route or target name can still be distinguished by the code that handles them.

## Legacy configuration artifact

A legacy configuration artifact is a repository-contained XML `.config` file such as `app.config` or `web.config`. Archon parses supported XML concepts from these files as static evidence and does not execute configuration section handlers, transforms, or machine-level configuration during extraction.

## Unknown-source provider

An unknown-source provider is an explicit unknown state used when source code references a configuration key but no matching provider definition was discovered in repository configuration artifacts. The fact preserves the source dependency without claiming that the repository contains the matching setting.

## Graph fact

A graph fact is a domain object that states something Archon knows about an architecture snapshot, such as a repository, solution, node, edge, evidence record, finding, metric, or generated summary.

## Graph recreation

Graph recreation is a deliberately destructive local/test workflow that deletes Archon-owned graph records and then recreates constraints and indexes. It is not a migration, repair mechanism, production endpoint, or startup hook.

## Graph schema

A graph schema is the set of Neo4j constraints and indexes that make graph writes safe and queryable.

## Hosted service

A hosted service is a service that participates in the .NET generic host lifecycle through `Microsoft.Extensions.Hosting.IHostedService`. Archon's DI extractor records `AddHostedService<T>()` and hosted-service-assignable registrations as `REGISTERED_AS_SERVICE` graph facts with hosted-service metadata. The WP008 worker runtime slice can also emit source-proven hosted services as `HostedService` nodes and correlate them with those prior registration facts.

## HttpClientFactory

HttpClientFactory is the Microsoft DI pattern for creating configured `HttpClient` instances through `AddHttpClient` registrations. Archon's current extractor records default, named, typed, and typed-implementation HttpClient registrations and marks unresolved external targets as explicit unknown data.

## HTTP handler

An HTTP handler is a classic ASP.NET type that implements `IHttpHandler` and can directly process requests, often through a `web.config` mapping. Archon represents handler types as `Type` nodes and configured handler paths as `Endpoint` nodes connected by `HANDLES` relationships.

## HTTP module

An HTTP module is a classic ASP.NET type that implements `IHttpModule` and participates in the request pipeline without declaring a single endpoint. Archon represents modules as `Type` nodes with runtime metadata.

## Idempotent

An operation is idempotent when it can run repeatedly without changing the result after the first successful run. Schema initialization is idempotent because it uses `CREATE ... IF NOT EXISTS` statements.

## Index

An index is a Neo4j lookup structure that helps queries find records by stable key, snapshot scope, kind, status, confidence, knowledge classification, fingerprint, or other indexed properties.

## Knowledge kind

Knowledge kind describes whether a fact is direct, inferred, explicitly unknown, or human-confirmed.

## Liveness

Liveness answers whether a process is responsive. Archon exposes liveness through `/alive`.

## Metadata

Metadata is deterministic extension data for extractor-specific details that do not belong in normalized graph properties. Metadata must not hide fields that the platform expects to query, compare, or validate directly.

## Metadata-only symbol

A metadata-only symbol is a Roslyn symbol resolved from a referenced assembly or package without a source declaration inside the analyzed repository. Archon represents dependencies on metadata-only symbols with deterministic symbol-reference stable keys and `MetadataOnly` confidence rather than inventing repository source nodes.

## Metric

A metric is a durable computed value for a graph, snapshot, project, architecture node, architecture relationship, or modernization scope.

## Hotspot

A hotspot is a deterministic architecture triage result derived from existing snapshot facts such as metrics, findings, graph nodes, and dependency-cycle participation. Hotspots use `hotspot://` stable keys and carry score, rank, category, target identity, contribution references, confidence, unknown state, metadata, and fingerprint.

## Hotspot score

A hotspot score is the numeric value used to decide whether a target crosses a category threshold and how it ranks within that category. For metric-derived hotspots the score is usually the contributing metric value; for finding concentration it is the count of open findings on the target.

## Hotspot rank

A hotspot rank is the one-based deterministic ordering position within a hotspot category. Higher scores rank first, and equal scores are tie-broken by stable target identity so paging and API responses remain repeatable.

## Neo4j

Neo4j is the graph database used by Archon as the system of record for deterministic architecture facts.

## Normalized property

A normalized property is a graph field stored directly because later code needs to query, compare, or validate it. Examples include stable keys, graph kinds, evidence kinds, confidence, unknown-state values, and fingerprints.

## Onion Architecture

Onion Architecture is a dependency model where stable core concepts sit at the center and replaceable delivery or infrastructure details sit at the outside. Dependencies should point inward.

## Orchestrator

An orchestrator is the application-layer component that coordinates one accepted asynchronous extraction run through pipeline execution, snapshot assembly, persistence handoff, and run lifecycle updates.

## Persistence handoff

Persistence handoff is the application-layer boundary where an assembled `ExtractedArchitectureSnapshot` is given to an `IArchitectureSnapshotWriter` implementation and the returned result controls completion or failure status.

## Readiness

Readiness answers whether a process is ready to accept work. Archon exposes readiness through `/health`.

## Scalar API reference

The Scalar API reference is the development-time browser UI for Archon's generated OpenAPI document. `ArchonApi` exposes it at `/scalar/v1` in the Development environment, backed by the OpenAPI document at `/openapi/v1.json`.

## Recent run history

Recent run history is the operational list of accepted extraction runs returned by `GET /extractions`. It is ordered deterministically newest first and summarizes run state without becoming the durable architecture graph.

## Placeholder stage

The placeholder stage was the early extraction stage that proved the pipeline boundary by contributing a warning without inventing real repository, Roslyn, runtime, UI, data-access, markdown, MCP, rule, or architecture facts. The current extraction composition has moved beyond that placeholder for repository and submitted-solution facts.

## Project extraction stage

The project extraction stage is the WP005 pipeline stage family that reads submitted solution and project artifacts and contributes graph facts to the shared extraction accumulator. The current `project-repository-solution` stage reads explicitly submitted solution files, extracts supported C# and VB.NET project files declared by those solutions, follows repository-contained project-reference targets, reads deterministic local package, analyzer, and build-artifact declarations, classifies application types, and contributes repository nodes, solution nodes, project nodes, package nodes, FilePath nodes, `CONTAINS`, `REFERENCES`, and `USES_PACKAGE` relationships, evidence records, controlled warnings, and unsupported-project diagnostics.

## Project node

A project node is an architecture node representing a supported C# or VB.NET project file. Its stable key is based on the repository-relative project path so the same project declared by multiple submitted solutions remains one graph identity. Current project node metadata includes application type classification when WP005 can determine or explicitly preserve that category as Unknown.

## Package node

A package node is an architecture node representing a NuGet package dependency extracted from a supported project file or safe imported build file. Its stable key uses the normalized package ID and known version or explicit version-source state.

## FilePath node

A FilePath node is an architecture node representing a repository-contained source artifact path. In WP005, FilePath nodes are used for submitted solutions, supported project files, package manifests, local central package files, repository-contained imported build files, and repository-contained analyzer assemblies that support extracted facts.

## Analyzer reference

An analyzer reference is an MSBuild `Analyzer` item that points at a compiler analyzer assembly. Archon reads analyzer declarations from project XML as metadata and evidence without loading or running the analyzer.

## Imported build artifact

An imported build artifact is a repository-contained `.props` or `.targets` file that a project imports and that can be inspected safely as static XML. Archon excludes imports that require property expansion, wildcard traversal, missing files, or outside-repository paths because those cases would require build evaluation or external state.

## PackageReference

A `PackageReference` is an MSBuild item that declares a NuGet package dependency for an SDK-style project. Archon reads direct package IDs, versions, asset metadata, aliases, and safe repository-contained imported declarations without running restore or contacting package feeds.

## packages.config

A `packages.config` file is the legacy NuGet package manifest commonly found beside old-style .NET Framework project files. Archon reads valid package entries from a sibling repository-contained file as static XML, preserves package ID, version, target framework, and evidence line information, and represents those dependencies through the same package node and `USES_PACKAGE` relationship model used by SDK-style package references.

## Central Package Management

Central Package Management is the NuGet/MSBuild pattern where project files omit package versions and a `Directory.Packages.props` file supplies `PackageVersion` declarations. Archon resolves local deterministic central versions from repository-contained files only.

## ProjectReference

A `ProjectReference` is an MSBuild item in a C# or VB.NET project file that declares a direct dependency on another project file. Archon records the raw include path as evidence, resolves repository-contained targets to project nodes when possible, and represents resolved dependencies as `REFERENCES` edges.

## Partial declaration

A partial declaration is one source declaration that contributes to a compiler symbol assembled from multiple source spans, such as a C# or VB.NET partial class. Archon merges partial declarations by Roslyn symbol identity and records additional evidence contributions so the graph does not double-count the type while still preserving each contributing source span.

## REFERENCES edge

A `REFERENCES` edge is an architecture relationship showing that one graph node directly references another. In the current WP005 project extraction stage, a project-to-project `REFERENCES` edge means a source project file declared a `ProjectReference` to the target project file.

## REGISTERED_AS_SERVICE edge

A `REGISTERED_AS_SERVICE` edge is an architecture relationship showing that an implementation type has been registered to satisfy a service abstraction in a dependency-injection container. In the current WP007 Microsoft DI slice, the edge points from the implementation type node to the service abstraction type node and carries lifetime metadata such as `Singleton`, `Scoped`, or `Transient`.

## Service registration

A service registration is a source statement that tells a dependency-injection container which implementation should satisfy a service request and what lifetime should govern the produced instance. Examples in the current WP007 slice include Microsoft DI calls such as `AddSingleton<TService, TImplementation>()`, service-only overloads, `typeof(...)` overloads, factory overloads, `TryAdd`, `TryAddEnumerable`, `Replace`, `AddHostedService<T>()`, and `AddHttpClient`, plus supported legacy container calls such as Unity `RegisterType`, Autofac `RegisterType().As`, Castle Windsor `Register(Component.For)`, StructureMap `For().Use`, Ninject `Bind().To`, and SimpleInjector `Register`.

## Service locator

A service locator is a global or ambient resolver that application code asks for services directly. Archon detects CommonServiceLocator `GetInstance<TService>()` as a medium-confidence heuristic fact because the requested service type is compiler-bound but the concrete implementation is not deterministically visible at the call site.

## Typed HttpClient

A typed HttpClient is a class or abstraction registered through `AddHttpClient<TClient>()` or `AddHttpClient<TClient, TImplementation>()` so callers consume a domain-specific client rather than requesting `HttpClient` or `IHttpClientFactory` directly.

## USES_PACKAGE edge

A `USES_PACKAGE` edge is an architecture relationship showing that a project directly uses a NuGet package. In the current WP005 project extraction stage, this edge can come from an SDK-style `PackageReference` declaration or a legacy `packages.config` entry and carries source-type, version-source, target framework, and asset metadata when those values are available.

## SDK-style project

An SDK-style project is an MSBuild project whose root `<Project>` element declares an `Sdk` attribute such as `Microsoft.NET.Sdk`. Archon reads that XML as metadata and does not execute build targets to identify the project style.

## Old-style project

An old-style project is a non-SDK-style MSBuild project, often using the legacy MSBuild XML namespace and properties such as `TargetFrameworkVersion`. Archon records it as old-style when no root `Sdk` attribute is present.

## Run lifecycle

A run lifecycle is the operational status model for an accepted extraction request. It records states such as queued, running, completed, failed, or cancelled together with progress, warnings, errors, timestamps, and snapshot identity when available.

## Runtime extraction slice

A runtime extraction slice is a static-analysis contribution that identifies runtime-facing application behavior, such as an HTTP endpoint, console entry point, hosted service, scheduled job, or message consumer, without executing the analyzed application. The current WP008 slices recognize direct ASP.NET Core minimal API mappings, endpoint groups, attributed controllers and actions, MVC setup calls, middleware registration order, OpenAPI setup in C# source, console entry points in C# and VB.NET source, worker hosted-service source, scheduled-job source, queue/topic consumer source, service-host setup source, custom host-loop source, and extractor-level classic ASP.NET artifacts such as `Global.asax`, Web Forms, handlers, modules, MVC 5, Web API 2, and route configuration.

## Scheduled job

A scheduled job is runtime work registered with a scheduler so it can run at a time, interval, or recurrence expression. Archon represents supported scheduled jobs as method-level runtime facts and records a schedule expression only when source evidence provides a compile-time literal.

## Topic consumer

A topic consumer is a message consumer that reads from a publish/subscribe target, often using both a topic name and a subscription name. Archon represents supported topic consumers as `Topic` nodes with transport, topic, and subscription metadata when those values are deterministic.

## Web Forms page

A Web Forms page is a classic ASP.NET `.aspx` markup artifact that is addressable by a virtual path. Archon represents supported pages as `Endpoint` nodes with Web Forms runtime metadata and markup evidence.

## Web Forms user control

A Web Forms user control is a classic ASP.NET `.ascx` markup artifact that can participate in page rendering but is not directly addressable as an endpoint by itself. Archon represents supported user controls as `FilePath` nodes with user-control runtime metadata.

## Endpoint group

An endpoint group is an ASP.NET Core minimal API route prefix created with `MapGroup`. Archon combines a literal group prefix with literal endpoint route templates and records explicit unknown state when the group prefix is computed.

## OpenAPI setup

OpenAPI setup is source-level configuration that enables OpenAPI or Swagger descriptions for an ASP.NET Core application, such as `AddSwaggerGen`, `AddOpenApi`, `UseSwagger`, `UseSwaggerUI`, or `MapOpenApi`. Archon records supported setup calls as project-level runtime metadata rather than invoking the application to generate a document.

## Relationship-node pattern

The relationship-node pattern stores an architecture edge as an `ArchonRelationship` node instead of only as a native Neo4j relationship. This lets the relationship fact carry its own stable key, fingerprint, metadata, confidence, unknown state, and evidence links.

## Repository-relative path

A repository-relative path is written from the repository root rather than from a developer's machine root. Repository-relative paths keep stable keys deterministic across workstations and CI agents.

## Service defaults

Service defaults are shared host configuration used by runtime processes. In Archon, `src/Archon.ServiceDefaults` configures health checks, OpenTelemetry-compatible telemetry, service discovery, and HTTP client resilience.

## Snapshot assembly

Snapshot assembly is the process of gathering many graph fact contributions into one in-memory `ExtractedArchitectureSnapshot` before persistence or presentation.

## Snapshot persistence

Snapshot persistence writes an assembled `ExtractedArchitectureSnapshot` into Neo4j using stable logical identities and support relationships.

## Stable key

A stable key is the durable logical identity for an architecture fact. It is not a database ID, process-local object reference, or machine-specific path.

## Stored procedure node

A stored procedure node is an architecture node representing a database stored procedure or DBML function wrapper target that static evidence can identify. In the current DBML slice, deterministic `Function Name="schema.Procedure"` metadata becomes a `StoredProcedure` node connected to the DataContext by `CALLS_STORED_PROCEDURE`.

## Testcontainers

Testcontainers is a test library that starts short-lived Docker containers under test control and removes them after tests. Archon uses it for real Neo4j integration tests without starting the Aspire AppHost.

## Target framework

A target framework is the .NET platform moniker or legacy framework version a project builds for, such as `net10.0`, `net8.0`, or `v4.7.2`. Archon records single-target, multi-target, and legacy target framework values from project-file metadata when available.

## Target repository context

Target repository context is the accepted repository root, submitted solution path list, and repository-relative project/source paths that scope extraction to the application being analyzed. Runtime extractors use this context to avoid confusing target application entry points with Archon host code, test harness code, package-cache files, or arbitrary files outside the submitted extraction input.

## Top-level statements

Top-level statements are C# statements written directly in a source file, usually `Program.cs`, instead of inside an explicit `Main` method. The C# compiler turns them into an implicit entry point; Archon represents that implicit entry point with method metadata keyed by the project and normalized repository-relative file path.

## Unknown state

Unknown state records whether a fact contains unknown data and, when it does, the reason the data is unknown. Facts that use unknown knowledge or declare unknown data must carry a non-empty reason.

## Work-package implementation record

The work-package implementation record is the concise historical status retained in a plan after work completes: what changed, what validation ran, and what wiki review outcome was recorded. It must not become a parallel source of contributor-facing guidance; current-state guidance belongs in the wiki.
