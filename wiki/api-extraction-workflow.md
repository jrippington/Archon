# API Extraction Workflow

This page describes the behavior that exists now. The current application layer accepts extraction requests asynchronously, runs an in-process orchestration path, executes the first project extraction stage, assembles a generalized snapshot, and hands that snapshot to the configured persistence writer.

Read this page with [solution architecture](solution-architecture.md) for layering rules, [runtime foundation](runtime-foundation.md) for host composition, [graph domain model](graph-domain-model.md) for snapshot vocabulary, [Neo4j persistence foundation](neo4j-persistence-foundation.md) for durable graph storage, and [validation and test workflows](validation-and-test-workflows.md) for focused verification commands. Terms such as asynchronous execution, run lifecycle, repository-relative path, snapshot assembly, and stable key are defined here in context and in the [glossary](glossary.md).

Reader path: [Home](home.md) -> [Solution architecture](solution-architecture.md) -> API extraction workflow -> [Validation and test workflows](validation-and-test-workflows.md).

## Current endpoint surface

The API host maps the extraction start, status, and history endpoints directly without a common `/api` prefix:

- `POST /extractions` starts a new extraction run request.
- `GET /extractions/{runId}` retrieves the current status for an accepted run.
- `GET /extractions` retrieves recent accepted runs as compact summaries.

The start endpoint accepts a JSON body with `repositoryRootDirectory`, `solutionPaths`, optional `branchName`, optional `commitSha`, optional `requestedBy`, and optional `metadata`. The `solutionPaths` list is explicit by design. Archon does not scan the repository to infer solution files when the caller omits them, because the caller's selected solution boundary is part of the extraction contract and later evidence model.

A successful start response returns HTTP 202 Accepted. The response body includes the `runId`, current `status`, accepted request summary, timestamps, current progress, warnings, errors, and optional `snapshotIdentity`. The accepted run is queued through a replaceable scheduler seam and the initial status is `Queued`. The HTTP start endpoint still returns after scheduling rather than running extraction stages inline, but the in-process scheduler can move the run to `Running`, `Completed`, or `Failed` almost immediately after the response because the current placeholder pipeline is intentionally lightweight.

The status endpoint returns HTTP 200 with the same run-status response shape when the run exists. It returns HTTP 404 when the run identifier is unknown or malformed. The history endpoint returns HTTP 200 with a `runs` collection ordered newest first. Each history item includes the run identifier, status, accepted and completed timestamps, repository root summary, solution count, warning count, error count, and optional snapshot identity. The endpoint accepts an optional `limit` query parameter for a bounded recent-history view. Status and history responses avoid raw exceptions, stack traces, connection strings, environment variables, and infrastructure implementation details. The API response layer also performs a final diagnostic-sanitization pass before warning, error, or validation messages leave the process. That pass is a defensive boundary for accidental unsafe diagnostic text returned by an inner adapter; it preserves safe user-actionable messages and replaces obvious stack-trace or connection-string fragments with a generic instruction to review server logs.

## Validation boundary

The validation boundary is the line between an untrusted HTTP request and an accepted operational run. Archon validates start requests before creating a run and before any future Roslyn workspace loading, solution loading, project loading, extractor stage execution, or persistence attempt can happen. This ordering matters because rejected requests should not appear as accepted work and should not accidentally analyze files outside the intended repository.

The current validator enforces these rules:

- `repositoryRootDirectory` must be present, non-blank, and point to an existing directory.
- `solutionPaths` must contain at least one non-blank entry.
- Relative solution paths resolve against the submitted repository root.
- Absolute and relative solution paths must normalize to locations inside the repository root.
- Solution files must exist.
- Solution paths must use the `.sln` extension.
- Solution paths must be unique after normalization, so the same solution cannot be submitted twice with different spelling.

Validation messages are intentionally user-actionable and credential-safe. Metadata values are not copied into validation responses, and run summaries expose metadata keys rather than metadata values. Repository and solution paths are still operational data and should be treated carefully in logs, examples, and screenshots.

## Run lifecycle and progress

A run lifecycle is the operational status history for an accepted extraction request. It is not the architecture graph and it is not the Neo4j system of record. The run lifecycle exists so API consumers and developers can understand what happened to asynchronous work: when it was accepted, whether it has been queued or started, what progress is visible, and which warnings or errors have been recorded.

Runs are created as `Queued` after validation and scheduling. The scheduler dispatches accepted work to the application orchestrator, which moves the run to `Running` while it prepares the accepted context, executes the extraction pipeline, assembles the snapshot, and performs the persistence handoff. A run reports `Completed` only after snapshot persistence succeeds and returns or confirms a stable snapshot identity. A run reports `Failed` when a controlled pipeline error, assembly failure, persistence failure, cancellation, or unexpected exception prevents successful persistence.

Progress contains a stage name, a human-readable message, an optional percentage, and a UTC last-updated timestamp. The current orchestration path records stages such as validation/context preparation, pipeline execution, snapshot assembly, persistence, and completion or failure. Warnings and errors are appended to run state and become visible through `GET /extractions/{runId}`. The history endpoint intentionally stays compact: it reports warning and error counts rather than returning every diagnostic message for every run.

## Orchestration and persistence handoff

The **orchestrator** is the application-layer component that owns the asynchronous execution sequence after the API has accepted a run. It does not depend on ASP.NET Core endpoint types, Neo4j driver types, or host-specific objects. Instead, it reads the accepted run context from run history, reconstructs the validated execution input from the credential-safe request summary, calls the pipeline runner, calls the snapshot assembler, and finally calls the application-layer `IArchitectureSnapshotWriter` persistence port.

The term **persistence handoff** means the moment when the application layer gives the complete `ExtractedArchitectureSnapshot` to the persistence abstraction. This handoff is intentionally expressed in application-owned contracts. Neo4j infrastructure translates the snapshot into graph writes, but the orchestrator only sees `SnapshotPersistenceResult`, including success, safe warnings, safe errors, counts, and the snapshot stable key. The orchestrator records that snapshot stable key as the run's `snapshotIdentity` only when persistence succeeds. This rule prevents polling clients from seeing a completed run before durable graph storage has accepted the snapshot.

Failure handling is deliberately controlled. A stage can stop the pipeline by returning a blocking error, which becomes a run error without a raw exception or stack trace. A persistence adapter can return a failed `SnapshotPersistenceResult`, which becomes a failed run with the application-owned persistence stage and safe message. Unexpected exceptions are logged for operators, but the run status receives a generic safe error message that tells contributors where to investigate without exposing connection strings, secret values, stack frames, or driver internals.

## Scheduler and storage seams

The extraction API host remains an outer delivery component. It maps HTTP routes, translates JSON contracts, and registers services, but it does not own extraction workflow logic. The application layer owns validation, accepted run creation, status retrieval, and the scheduler abstraction. This preserves the Onion Architecture rule explained in [solution architecture](solution-architecture.md): delivery and infrastructure details point inward to application contracts instead of the application layer depending on ASP.NET Core or Neo4j driver types.

The initial run-history store is in-memory and replaceable. It is suitable for local execution and tests because it makes the first vertical slice observable without introducing durable queue or distributed worker infrastructure too early. Recent history is deterministic: runs are returned newest first by accepted timestamp with a stable tie-breaker, which keeps API behavior predictable for polling clients and automated tests. The run store should not be mistaken for the system of record for architecture facts. Neo4j remains the durable system of record for persisted extraction output, as described in [Neo4j persistence foundation](neo4j-persistence-foundation.md).

The current scheduler accepts the run identifier through an application interface, returns immediately, and dispatches orchestration on an in-process background task. That seam is important even while the implementation is deliberately simple. Later durable queues, hosted services, or distributed workers can replace the scheduler implementation without changing the public `POST /extractions`, `GET /extractions/{runId}`, or `GET /extractions` contracts.

## Project extraction stage and snapshot assembly

The **extraction pipeline** is the application-layer extension point that extractor slices use to contribute architecture facts. A pipeline stage is a deterministic unit of extraction work with a stable stage identifier. The runner executes stages sequentially in the order supplied by composition, gives each stage the validated input, accepted run, and shared accumulator, and stops when a stage returns a controlled blocking error. A blocking error is an expected, credential-safe failure result, not a raw exception or stack trace. Non-blocking warnings stay in the accumulator and do not prevent later stages from running.

The current `project-repository-solution` stage is the first real project extraction slice. It reads only the solution files that were explicitly submitted in the accepted request. It does not scan the repository for additional `.sln` files, and it does not load projects, restore packages, execute MSBuild targets, or require Visual Studio automation. This boundary is important because the caller's submitted solution list is part of the evidence model: a solution that exists in the repository but was not submitted is intentionally absent from the snapshot.

For each accepted run, the stage contributes one repository architecture node, one solution architecture node per submitted solution, and a direct `CONTAINS` relationship from the repository node to each solution node. It also records solution-file evidence. File-level evidence proves that the submitted solution file existed and had a recognizable Visual Studio solution header. Line-level evidence is captured for visible `Project(...) = ...` declarations when they appear in the solution file. Those project declarations are evidence only in this slice; later project metadata slices turn supported C# and VB.NET declarations into project nodes and solution-to-project relationships.

The stage treats malformed or unreadable submitted solution content as a controlled blocking pipeline error. The public run status receives a credential-safe message telling contributors that a submitted solution could not be read as a valid Visual Studio solution. Raw exception types, stack traces, absolute local paths, connection strings, environment variables, and request metadata values must not appear in the public diagnostic. If every submitted solution parses successfully, the stage normally completes without the old placeholder warning because real repository and solution facts were produced.

Snapshot assembly turns the accepted run, resolved input, and accumulated contributions into an `ExtractedArchitectureSnapshot`. Assembly still creates deterministic repository and solution boundary models from the validated request and preserves every accumulated warning and error. The project extraction stage adds the queryable architecture-node, architecture-edge, and evidence sections that explain those boundaries in graph form. Unsupported sections such as findings, metrics, and generated summaries are represented as explicit empty collections. This is important because consumers can distinguish “the section was considered and has no facts yet” from “the section was omitted by accident.” The assembler and stage use stable logical keys and metadata-key-only request context; they do not create Neo4j database identifiers and do not copy potentially sensitive request metadata values into graph metadata.

## Example request and polling flow

The following example uses non-sensitive sample paths. Replace them with a local repository path when manually testing.

```json
{
  "repositoryRootDirectory": "C:\\Repos\\CustomerSuite",
  "solutionPaths": [
	"CustomerSuite.sln"
  ],
  "branchName": "main",
  "commitSha": "abcdef1234567890",
  "requestedBy": "developer@example.invalid",
  "metadata": {
	"source": "manual-verification"
  }
}
```

Because orchestration now runs asynchronously after scheduling, an immediate poll can legitimately observe `Queued`, `Running`, `Completed`, or `Failed` depending on timing and the configured persistence writer. In the default test/module composition, the project repository/solution stage normally completes quickly and records a stable snapshot identity from the in-memory persistence writer. In a host that composes Neo4j infrastructure, the same handoff flows through the Neo4j writer.

For a manual HTTP walkthrough, start the API host only when you intentionally want local runtime exploration. Automated validation should use the focused tests described below instead of launching the Aspire AppHost. When the API host is already running locally, a contributor can submit a request with PowerShell from the repository root or any working directory. The path values below are deliberately illustrative and should be replaced with non-sensitive local paths:

```powershell
$body = @{
	repositoryRootDirectory = 'C:\Repos\CustomerSuite'
	solutionPaths = @('CustomerSuite.sln')
	branchName = 'main'
	commitSha = 'abcdef1234567890'
	requestedBy = 'developer@example.invalid'
	metadata = @{
		source = 'manual-verification'
	}
} | ConvertTo-Json -Depth 5

$start = Invoke-RestMethod -Method Post -Uri 'https://localhost:7001/extractions' -ContentType 'application/json' -Body $body
$start.runId
Invoke-RestMethod -Method Get -Uri "https://localhost:7001/extractions/$($start.runId)"
Invoke-RestMethod -Method Get -Uri 'https://localhost:7001/extractions?limit=10'
```

The exact HTTPS port depends on the local launch profile or hosting setup, so adjust the URI to match the running API host. Successful manual verification should show the direct `/extractions` routes without a common `/api` prefix, a 202 start response, status polling through the returned run identifier, and a history response ordered newest first. Validation failures should return problem-details-style client errors before any run appears in history. Runtime failures that occur after acceptance should be visible through the run status as `Failed` with a stable error code, a workflow stage, and a controlled message; they should not include stack frames, connection strings, environment variables, raw exception names, or metadata values.

## Validation and automation expectations

Automated validation for this workflow should use targeted builds and tests. It must not start the Aspire AppHost as an automated step because the AppHost is a long-running local composition process. The focused test commands for the current start, status, history, progress-reporting, project repository/solution extraction stage, snapshot assembly, orchestration, and persistence-handoff slices are recorded in [validation and test workflows](validation-and-test-workflows.md) and in the relevant work-package implementation plan completion records.

Manual AppHost verification remains useful when a contributor wants to inspect composed local resources, but it is separate from automated acceptance. The API extraction workflow can be validated through in-memory API tests and application/project-extractor tests without launching the Aspire dashboard or requiring real Neo4j credentials for the current orchestration and repository/solution extraction slices.
