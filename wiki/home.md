# Archon

Archon is a .NET architecture-intelligence platform whose foundation is being built around small, independently runnable hosts. In the current WP001 state, the repository provides a buildable solution skeleton and the first runtime slices: the API host and MCP host both start as ASP.NET Core processes, expose only operational probe endpoints, and can now be composed together through the Aspire AppHost. They do not yet expose extraction, query, graph persistence, MCP tool, or user-interface behavior.

The term **service defaults** means shared host configuration that every runtime process uses instead of copying the same setup into each executable project. Archon keeps those defaults in `src/Archon.ServiceDefaults`. The defaults currently configure health checks, OpenTelemetry-compatible logging/metrics/tracing, Aspire-style service discovery, and resilient `HttpClient` behavior. Keeping this cross-cutting setup in one project helps later work packages add real API and MCP features without drifting away from a consistent runtime foundation.

The current operational probe model uses two endpoints. The `/health` endpoint is the **readiness** probe: it runs the registered health checks and answers whether the process is ready to accept work. The `/alive` endpoint is the **liveness** probe: it uses a lightweight self-check to answer whether the process itself is responsive. This distinction matters because a process can be alive before every future dependency is ready. Later work packages may add dependency-specific checks, but the probe names and shared mapping are already established.

Developers can run the hosts independently while working on the foundation:

```powershell
dotnet run --project .\src\ArchonApi\ArchonApi.csproj
dotnet run --project .\src\ArchonMcp\ArchonMcp.csproj
```

After a host starts, browse or request `/health` and `/alive` on the assigned local ASP.NET Core URL. A successful response confirms the WP001 runtime foundation is working for that host. Stop manually run host processes after verification. Automated validation should use the test projects instead of launching the Aspire AppHost as a blocking process.

The **AppHost** is the Aspire project in `src/Archon`. Aspire uses an AppHost to describe a local distributed application: the project declares which services, containers, and dependencies should run together for a developer. Archon's AppHost is also the **composition root**. A composition root wires resources together but does not implement business behavior. In practical terms, `src/Archon/Program.cs` may say that Neo4j, `ArchonApi`, and `ArchonMcp` run together, but it must not contain extraction rules, API endpoint handlers, graph persistence code, or MCP tool logic.

The current AppHost composes Neo4j as a container resource named `neo4j`. Neo4j is a graph database; Archon will later use it as the system of record for deterministic architecture facts. WP001 does not create graph schema, constraints, indexes, or data. It only establishes the local runtime seam so later graph-persistence work has a clear place to connect. Because no dedicated `Aspire.Hosting.Neo4j` package is available in NuGet for this implementation, the AppHost uses Aspire's generic container support with the official `neo4j:latest` image. Neo4j starts in secured mode: the AppHost reads the `neo4j-username` and `neo4j-password` Aspire parameters from `src/Archon/appsettings.json` and uses them to set the container's `NEO4J_AUTH` environment value. The password parameter is marked as secret in the Aspire resource graph so later work can move the same parameter flow to user secrets or another secure provider without changing the composition contract. The AppHost also binds the Neo4j HTTP browser endpoint to `localhost:7474`, binds Bolt to `localhost:7687`, and advertises those host-reachable addresses to Neo4j so Browser can connect back to the same local instance.

Archon uses **Onion Architecture**, which means dependencies should point inward toward the most stable business concepts and away from delivery or infrastructure details. The `Archon.Domain` project is the center. `Archon.Application` may depend on the domain layer, API modules and extractors may depend on application-facing contracts, infrastructure projects adapt external systems, and hosts compose the runtime at the outer edge. A project near the center must not reference a project farther out, because that would make core architecture knowledge depend on a delivery host, database adapter, or other replaceable implementation detail.

Project identity is also normalized at the foundation. A project identity is the repository-root-relative `.csproj` path, such as `src/Archon.Domain/Archon.Domain.csproj`, written with forward slashes. This is deliberately different from an absolute path such as `D:\Dev\Archon\src\Archon.Domain\Archon.Domain.csproj`. Relative normalized identities remain the same across machines, which matters because Archon will later reason about architecture evidence collected from repositories in different developer and CI environments.

The boundary tests in `test/Archon.Tests` enforce these rules without starting any host process. They read project files, classify projects into layers, and inspect `ProjectReference` edges. One important exception is intentional: the `Archon` AppHost references `ArchonApi` and `ArchonMcp` so Aspire can compose them as project resources. That edge is a host composition reference at the outer boundary, not an inward dependency from domain or application code.

For automated verification, start with restore and build from the repository root:

```powershell
dotnet restore .\Archon.slnx
dotnet build .\Archon.slnx --no-restore
```

Then run the targeted WP001 test slices rather than a blocking AppHost run. The service-default, API, and MCP test projects validate shared runtime defaults and probe-only host behavior. The `Archon.Tests` project validates AppHost composition metadata, project identity, and Onion Architecture boundaries:

```powershell
dotnet test .\test\Archon.ServiceDefaults.Tests\Archon.ServiceDefaults.Tests.csproj --no-build
dotnet test .\test\ArchonApi.Tests\ArchonApi.Tests.csproj --no-build
dotnet test .\test\ArchonMcp.Tests\ArchonMcp.Tests.csproj --no-build
dotnet test .\test\Archon.Tests\Archon.Tests.csproj --no-build --filter FullyQualifiedName~AppHostComposition
dotnet test .\test\Archon.Tests\Archon.Tests.csproj --no-build --filter FullyQualifiedName~Boundary
dotnet test .\test\Archon.Tests\Archon.Tests.csproj --no-build --filter FullyQualifiedName~ProjectIdentity
```

To verify the composed runtime manually, make sure an OCI-compatible container runtime such as Docker Desktop is running, then start the AppHost:

```powershell
dotnet run --project .\src\Archon\Archon.csproj
```

When the Aspire dashboard opens, the expected resources are `neo4j`, `ArchonApi`, and `ArchonMcp`. No `ArchonUi` or Discovery UI resource should appear. The API and MCP resources should expose their `/health` checks through the dashboard after they are ready. If you open the Neo4j browser endpoint, use `bolt://localhost:7687` as the connection URL and sign in with the configured `neo4j-username` and `neo4j-password` parameter values from `src/Archon/appsettings.json`. The direct `bolt://` URL avoids routing discovery errors that can appear when Browser is pointed at a routing-style `neo4j://` URI for this local single-container instance. Stop the AppHost manually after the check. Automated validation must not use this command as a blocking build or test step; use `Archon.Tests` metadata checks instead.

In the current WP001 runtime slice, absence is intentional. `ArchonApi` does not map extraction, query, management, Swagger, Scalar, or UI endpoints. `ArchonMcp` does not map MCP tools, MCP resources, MCP prompts, or architecture-query endpoints. Those capabilities remain assigned to later numbered work packages so the foundation can stay small, observable, and easy to validate.

The same is true for graph persistence, markdown export, findings, hotlist behavior, and Discovery UI. Their project homes exist where WP001 needs them, but behavior arrives in later numbered work packages. Contributors should therefore treat missing feature endpoints in WP001 as a correctness condition, not as a defect to fill opportunistically.
