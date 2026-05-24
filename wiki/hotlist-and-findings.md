# Hotlist and Findings

The hotlist is the controlled product-facing view over persisted WP012 findings. A **finding** is a snapshot-owned analysis result produced by a versioned rule, and the hotlist turns those records into bounded API responses that API clients and future MCP tools can consume without reading Neo4j directly. This page assumes familiarity with [rule catalog and rule engine](rule-catalog-and-rule-engine.md), because hotlist records are meaningful only when the reader understands rule identity, finding stable keys, history keys, confidence, unknown state, and suppression.

Reader path: [Home](home.md) -> [Rule catalog and rule engine](rule-catalog-and-rule-engine.md) -> Hotlist and findings -> [Validation and test workflows](validation-and-test-workflows.md).

## Controlled query surface

The current WP012 query API exposes rule catalog, hotlist, finding detail, finding history, and suppression endpoints through the API host. These endpoints deliberately do not expose arbitrary Cypher, unrestricted graph-query execution, or raw graph property maps. A caller supplies only approved filters, page bounds, route identities, or suppression fields, and the application layer translates those inputs into stable DTOs. A **DTO**, or data transfer object, is the public response shape that protects clients from infrastructure-specific implementation details.

The rule catalog endpoints are `GET /rules` and `GET /rules/{ruleCode}/{version}`. The list endpoint supports exact filters for rule code, version, category, severity, enabled state, built-in state, and owner scope. The detail endpoint returns one exact rule code/version pair. Rule identity remains the pair of rule code and version, so an older finding can still point to the precise version that classified it even after a newer rule version exists.

The hotlist endpoint is `GET /hotlist`. It lists persisted findings with approved filters for snapshot stable key, category, severity, status, project stable key, and affected node stable key. Results include rule code and version, title, summary, severity, status, confidence, optional category, affected-node references, evidence references, and unknown-state information. Evidence is returned as references rather than as raw snippets, because evidence snippets can contain source text or configuration fragments that must not be exposed accidentally through summary lists.

Finding detail is available through `GET /findings/{snapshotStableKey}/{findingStableKey}` and the query-parameter form `GET /findings/detail?snapshotStableKey=...&findingStableKey=...`. The query-parameter form exists because stable keys commonly contain slash-like separators such as `snapshot://...` or `finding://...`, and HTTP path decoding can make those values awkward for some clients. Both forms resolve the same application query. Finding history is available through `GET /findings/history/{historyKey}` and the query-parameter form `GET /finding-history?historyKey=...` for the same route-safety reason.

## Paging, ordering, and response limits

Every list endpoint uses bounded paging. The query contracts normalize negative `skip` values to zero, apply a default page size of fifty records, and clamp oversized `take` values to the current maximum of two hundred records. This limit is part of the safety boundary: query APIs should be useful for dashboards, triage views, and future MCP consumers without becoming unbounded graph export mechanisms.

Ordering is deterministic. Rules are ordered by rule code and version. Findings are ordered by severity, rule code, and finding stable key in the current query implementation. Deterministic ordering makes paging repeatable for tests and consumers, and it avoids returning data in incidental database order. When adding a new filter or sort path, keep the ordering stable and update targeted tests before relying on the behavior in clients.

## Finding detail and evidence references

A finding detail response expands the hotlist item with the full finding description, knowledge kind, primary node and evidence stable keys, first-seen and latest-seen snapshot keys, suppression reason and actor when present, sanitized metadata, and fingerprint. The response still does not require direct graph access. A client that needs to explain why a finding exists can show the rule identity, affected node stable keys, evidence reference stable keys, confidence, unknown reason, and history without issuing Cypher.

The metadata exposed by the query service is defensive. Metadata keys must already be stable lower camel case, and obvious secret-bearing names such as password, secret, token, and connection-string style fields are omitted from public query DTOs. This does not replace extractor-level redaction; it is a final API-boundary safeguard. Contributors should continue to redact secret-like source and configuration values before they become graph facts, then treat the query API sanitizer as another defense in depth.

The integrated WP012 validation path proves this redaction boundary from the rule side through the query side. A representative security-sensitive rule matches a configuration location fact that says a secret-like value existed, but the graph facts, finding metadata, hotlist summary, finding detail metadata, extraction warnings, and suppression flow carry only safe location and unknown-state text. In practical terms, a finding can tell a reviewer that `app.config` contains a connection-string location and that the raw value was redacted; it must not echo the connection string itself in rule output, diagnostics, logs, metadata, or API responses. This is why security-sensitive rules should be written against location indicators such as `ConnectionStringLocation` rather than against credential literals.

## History and suppression

Finding history is based on the finding history key, not on Neo4j internal IDs. The history response returns first-seen and latest-seen snapshot stable keys plus compact historical finding records. Each historical record includes the snapshot stable key, finding stable key, status, severity, confidence, and fingerprint so a consumer can link back to the detail endpoint for the exact snapshot-owned record.

Suppression is exposed through `POST /findings/suppressions`. A suppression request must provide finding history key, rule code, rule version, primary node stable key, reason, and suppressed-by identity. The application validates these fields before persistence and returns validation problems for missing audit data. A successful suppression updates matching current findings to `Suppressed` and stores the suppression so later equivalent findings can inherit it. The underlying finding record is not deleted; suppression is an overlay that preserves auditability and historical fidelity.

A typical triage flow is therefore: list `/hotlist?snapshotStableKey=...&severity=High`, open one item through `/findings/detail`, review the rule through `/rules/{ruleCode}/{version}`, inspect history through `/finding-history?historyKey=...`, and, when policy allows, post a suppression with a clear reason and actor. At no point does the client need unrestricted graph access or raw evidence snippets.

The same triage flow is what the current end-to-end application test validates without launching the Aspire AppHost. It loads copied-output JSON rules, upserts versioned catalog entries, evaluates enabled rules over established graph facts, constructs deterministic findings, persists them in the application finding store, queries the hotlist and detail/history DTOs, posts a suppression command, and then re-reads the finding detail to confirm the status changed to `Suppressed`. That test is intentionally application-layer focused: it proves the behavior contract without starting a product UI, introducing MCP resources, exporting markdown, computing snapshot diffs, or running automatic remediation.

## Current boundaries

The query API is the minimum WP012 product surface for rule catalog and finding reads. It does not introduce MCP graph tools, markdown export, automatic remediation, a Discovery UI, arbitrary graph traversal, organization-specific rule-authoring UI, or a general-purpose Neo4j browser. Endpoint security remains limited to the host security model currently available in the repository; this slice adds controlled validation and response shaping, not a new authentication or authorization subsystem.
