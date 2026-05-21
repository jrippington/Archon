# API Extraction Workflow

This page describes the behavior that exists now. The current application layer accepts extraction requests asynchronously, runs an in-process orchestration path, executes the project extraction stage, assembles a generalized snapshot, and hands that snapshot to the configured persistence writer.

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

The current `project-repository-solution` stage is the active WP005 project extraction stage. It reads only the solution files that were explicitly submitted in the accepted request. It does not scan the repository for additional `.sln` files, restore packages, execute MSBuild targets, run repository scripts, contact external package feeds, or require Visual Studio automation. This boundary is important because the caller's submitted solution list is part of the evidence model: a solution that exists in the repository but was not submitted is intentionally absent from the snapshot.

For each accepted run, the stage contributes one repository architecture node, one solution architecture node per submitted solution, and a direct `CONTAINS` relationship from the repository node to each solution node. It also records solution-file evidence. File-level evidence proves that the submitted solution file existed and had a recognizable Visual Studio solution header. Line-level evidence is captured for visible `Project(...) = ...` declarations when they appear in the solution file.

Supported C# and VB.NET declarations now become project nodes in the same shared snapshot. A **project node** is an architecture node whose stable key is derived from the repository-relative project file path, such as `project://src/Customer.Api/Customer.Api.csproj`. The stage creates direct `CONTAINS` relationships from each submitted solution node to the project nodes it declares. If the same project is declared in more than one submitted solution, the path-based stable key collapses the project into one node while preserving a separate solution-to-project membership edge for each solution.

Project-to-project dependencies are visible through explicit `ProjectReference` items in supported project files. A `ProjectReference` is an MSBuild item that points from one project file to another project file, often with a relative `Include` path such as `..\Customer.Core\Customer.Core.csproj`. The stage normalizes that path relative to the declaring project file, keeps the raw declared include text in evidence metadata for troubleshooting, and contributes a direct `REFERENCES` relationship from the declaring project node to the referenced project node when the target can be resolved inside the submitted repository. Duplicate `ProjectReference` declarations collapse to one deterministic edge, while each declaration can still leave source evidence so contributors can find and clean up repeated project-file entries.

The submitted solution list still controls solution membership, but it does not hide repository-contained dependency targets. When a submitted project references another C# or VB.NET project file that lives inside the repository but is not declared by any submitted solution, the stage reads that target project file and contributes a project node so the `REFERENCES` edge has a graph target. The out-of-solution target does not receive a solution-to-project `CONTAINS` edge unless a submitted solution declared it. This distinction lets contributors ask two separate questions: “which projects did the caller submit as solution members?” and “which repository-contained projects participate in project-file dependencies?” References that point outside the repository or to missing files become evidence-backed warnings rather than raw exceptions or silent omissions.

SDK-style package dependencies are visible through `PackageReference` items. A `PackageReference` is an MSBuild item that names a NuGet package dependency and can optionally declare version and asset metadata. The stage reads direct `PackageReference` items from project XML and from explicit repository-contained `.props` or `.targets` files imported by the project. It deliberately ignores imports that require property expansion, wildcard traversal, external paths, missing files, or unsupported extensions, because those cases would require MSBuild evaluation or target execution. The package extraction path does not run restore, contact NuGet feeds, download packages, perform vulnerability checks, or infer transitive dependencies. It records only dependency facts that are deterministically visible in local XML artifacts.

Central Package Management is supported for local deterministic versions. In this context, central package management means the project omits a direct `Version` on `PackageReference` and a repository-contained `Directory.Packages.props` file supplies a matching `PackageVersion`. The stage walks from the repository root to the project directory so nearer central declarations override outer declarations deterministically. If a version is declared directly, the version source is `Direct`. If a local `PackageVersion` supplies it, the version source is `Central`. If a central package file exists but no local matching version can be resolved, the version source is `Inherited`; if there is no local source for the version, the version source is `Unknown`. Inherited and unknown dependencies remain graph facts rather than disappearing, because incomplete version information is still useful architecture evidence.

Package facts use package nodes and direct `USES_PACKAGE` relationships from project nodes to package nodes. The package node stable key uses a normalized package ID and the known version or explicit version-state segment, while metadata preserves the display package ID casing from source XML. Asset metadata such as `PrivateAssets`, `IncludeAssets`, `ExcludeAssets`, and `Aliases` stays on the `USES_PACKAGE` edge because those settings describe the project’s use of the package rather than the package identity itself. Duplicate package declarations collapse to one deterministic relationship while source evidence remains available for each declaration.

Analyzer references are visible as project metadata and project-file evidence. An **analyzer reference** is an MSBuild `Analyzer` item that points at a compiler analyzer assembly used during build or design-time analysis. The stage reads direct analyzer declarations from supported project XML, preserves the raw include path, resolves repository-contained analyzer paths where possible, and records source evidence for the `Analyzer` item. Analyzer references that point outside the submitted repository are warnings rather than graph targets, because Archon cannot prove an external analyzer artifact is part of the submitted source boundary. Repository-contained analyzer files are also represented as `FilePath` nodes so contributors can query the physical analyzer artifact without Archon running the analyzer.

The stage now contributes `FilePath` nodes for source artifacts that support extracted WP005 facts. A `FilePath` node represents a repository-relative file identity, such as a submitted solution file, a supported project file, a sibling `packages.config`, a local `Directory.Packages.props`, a local `Directory.Build.props` or `Directory.Build.targets`, an explicitly imported repository-contained `.props` or `.targets` file, or a repository-contained analyzer assembly. These nodes do not replace evidence records. Evidence remains the precise explanation for individual facts, while `FilePath` nodes make the artifact inventory queryable as architecture graph data. Imports stay conservative: local repository-contained build files are included only when they are visible from static XML declarations or known directory conventions, and imports that require property expansion, wildcards, missing files, or outside-repository paths remain excluded.

Old-style projects can also contribute package facts through a sibling `packages.config` file. A `packages.config` file is the legacy NuGet dependency manifest used by many .NET Framework projects before SDK-style `PackageReference` became common. When the project extraction stage identifies a non-SDK-style C# or VB.NET project, it checks the project directory for a repository-contained `packages.config` file and reads package entries as static XML. Each valid `<package>` entry contributes the same package node and direct `USES_PACKAGE` relationship shape used for SDK-style dependencies, but the relationship metadata marks the source type as `packages.config` and preserves the entry's `targetFramework` value when it is declared. This lets contributors distinguish a legacy dependency from a modern `PackageReference` while still querying package usage through one graph relationship kind.

The legacy package path is intentionally conservative. Archon does not scan arbitrary directories for package manifests, does not restore packages, does not inspect the repository-level `packages` folder, and does not contact external NuGet feeds. If an old-style project has no sibling `packages.config`, extraction simply records the project metadata and moves on because old-style project format alone does not prove that a legacy package manifest was expected. If the sibling file exists but is malformed or inaccessible, the stage records a controlled warning and file evidence for the problematic manifest, then continues extracting the project and other graph facts. The warning is credential-safe and avoids raw XML parser exception types, absolute local paths, stack traces, and file contents.

Project metadata is extracted by deterministic XML inspection of the project file. An **SDK-style project** is recognized when the root `<Project>` element declares an `Sdk` attribute such as `Microsoft.NET.Sdk` or `Microsoft.NET.Sdk.Web`; an **old-style project** is a non-SDK-style MSBuild project, often using the legacy MSBuild XML namespace and properties such as `TargetFrameworkVersion`. A **target framework** is the .NET platform moniker a project builds for, for example `net10.0` or a legacy value such as `v4.7.2`. The stage records target framework data from `TargetFramework`, ordered multi-target values from `TargetFrameworks`, legacy target framework values from `TargetFrameworkVersion`, output type, assembly name, root namespace, SDK value, nullable setting, and implicit-usings setting when those values are present. When `AssemblyName` is absent, the deterministic project-system default is the project file name without extension, and that default is recorded as metadata rather than inferred from a build.

Unsupported declarations are evidence-backed warnings when at least one supported C# or VB.NET project can be extracted. This means a solution containing a setup project, WiX project, database project, or other unsupported project kind can still contribute supported project facts. If a submitted solution set exposes only unsupported project declarations and no supported C# or VB.NET project can be extracted, the stage returns a controlled blocking error. That error is intentionally phrased as a user-actionable extraction limitation and must not expose raw exception types, stack traces, absolute local paths, secrets, or metadata values.

The stage treats malformed or unreadable submitted solution content as a controlled blocking pipeline error. The public run status receives a credential-safe message telling contributors that a submitted solution could not be read as a valid Visual Studio solution. Raw exception types, stack traces, absolute local paths, connection strings, environment variables, and request metadata values must not appear in the public diagnostic. If every submitted solution parses successfully, the stage normally completes without the old placeholder warning because real repository and solution facts were produced.

Snapshot assembly turns the accepted run, resolved input, and accumulated contributions into an `ExtractedArchitectureSnapshot`. Assembly still creates deterministic repository and solution boundary models from the validated request and preserves every accumulated warning and error. The project extraction stage adds the queryable architecture-node, architecture-edge, and evidence sections that explain those boundaries in graph form, including repository nodes, solution nodes, project nodes, package nodes, `FilePath` nodes, repository-to-solution containment, solution-to-project containment, project-to-project `REFERENCES` relationships, project-to-package `USES_PACKAGE` relationships, solution-file evidence, project-declaration evidence, project-file evidence, project-reference evidence, analyzer-reference evidence, package-reference evidence, and artifact evidence for malformed package files. XML-backed evidence uses source line spans where the parser provides them and records bounded snippet hashes and previews for supported XML elements. The snippet hash supports deterministic comparison without storing large full-file contents, and the preview is intentionally short enough to help contributors locate a fact without copying entire source artifacts into graph metadata. Unsupported sections such as findings, metrics, and generated summaries are represented as explicit empty collections. This is important because consumers can distinguish “the section was considered and has no facts yet” from “the section was omitted by accident.” The assembler and stage use stable logical keys and metadata-key-only request context; they do not create Neo4j database identifiers and do not copy potentially sensitive request metadata values into graph metadata.

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

Because orchestration now runs asynchronously after scheduling, an immediate poll can legitimately observe `Queued`, `Running`, `Completed`, or `Failed` depending on timing and the configured persistence writer. In the default test/module composition, the project extraction stage normally completes quickly for small submitted solutions and records a stable snapshot identity from the in-memory persistence writer. In a host that composes Neo4j infrastructure, the same handoff flows through the Neo4j writer.

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

Automated validation for this workflow should use targeted builds and tests. It must not start the Aspire AppHost as an automated step because the AppHost is a long-running local composition process. The focused test commands for the current start, status, history, progress-reporting, project extraction stage, snapshot assembly, orchestration, and persistence-handoff slices are recorded in [validation and test workflows](validation-and-test-workflows.md) and in the relevant work-package implementation plan completion records.

Manual AppHost verification remains useful when a contributor wants to inspect composed local resources, but it is separate from automated acceptance. The API extraction workflow can be validated through in-memory API tests and application/project-extractor tests without launching the Aspire dashboard or requiring real Neo4j credentials for the current orchestration and project extraction slices.
