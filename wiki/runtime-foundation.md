# Runtime Foundation

Archon's runtime foundation is deliberately small. The API host and MCP host can start as ASP.NET Core processes, expose operational probes, and run under an Aspire AppHost for local development. The foundation proves the runtime seams without implementing extraction orchestration, query APIs, MCP tools, markdown export, or user-interface behavior.

For architecture boundaries, read [solution architecture](solution-architecture.md). For persistence details, continue to [Neo4j persistence foundation](neo4j-persistence-foundation.md). For validation commands, read [validation and test workflows](validation-and-test-workflows.md). Terms used here are defined in the [glossary](glossary.md).

Reader path: [Home](home.md) -> [Solution architecture](solution-architecture.md) -> Runtime foundation -> [Validation and test workflows](validation-and-test-workflows.md).

## Service defaults

**Service defaults** are shared host configuration that every runtime process uses instead of copying the same setup into each executable project. Archon keeps these defaults in `src/Archon.ServiceDefaults`. They currently configure health checks, OpenTelemetry-compatible logging, metrics, tracing, Aspire-style service discovery, and resilient `HttpClient` behavior.

Keeping this cross-cutting setup in one project prevents the API and MCP hosts from drifting apart. Later work packages can add real API or MCP features while still receiving the same health, telemetry, service-discovery, and HTTP-resilience behavior.

## Readiness and liveness probes

The current operational probe model uses two endpoints:

- `/health` is the readiness probe. It runs registered health checks and answers whether the process is ready to accept work.
- `/alive` is the liveness probe. It answers whether the process itself is responsive.

The distinction matters because a process can be alive before every dependency is ready. For example, when a host opts into Neo4j infrastructure through `AddArchonNeo4j`, the infrastructure adapter contributes a dependency-specific readiness check named `neo4j`. That check opens a read session and runs `RETURN 1 AS healthy`, proving connection configuration, authentication, network reachability, database selection, and Cypher execution without creating graph schema or requiring architecture data.

## Running hosts independently

Developers can run the API and MCP hosts independently while working on the foundation:

```powershell
dotnet run --project .\src\ArchonApi\ArchonApi.csproj
dotnet run --project .\src\ArchonMcp\ArchonMcp.csproj
```

After a host starts, browse or request `/health` and `/alive` on the assigned local ASP.NET Core URL. A successful response confirms the host runtime foundation is working. Stop manually run host processes after verification. Automated validation should use the test projects instead of launching long-running hosts.

In the current runtime slice, absence is intentional. `ArchonApi` does not map extraction, query, management, Swagger, Scalar, or UI endpoints. `ArchonMcp` does not map MCP tools, MCP resources, MCP prompts, or architecture-query endpoints.

## Aspire AppHost

The **AppHost** is the Aspire project in `src/Archon`. Aspire uses an AppHost to describe a local distributed application: which services, containers, and dependencies should run together for a developer. Archon's AppHost is also the **composition root**, which means it wires resources together but does not implement business behavior.

The current AppHost composes three resources:

- `neo4j`, a Neo4j container resource.
- `ArchonApi`, the API host project resource.
- `ArchonMcp`, the MCP host project resource.

The AppHost establishes the local runtime seam. It must remain composition-only. It may configure resource relationships and health checks, but it must not contain extraction rules, API endpoint handlers, graph persistence code, domain logic, or MCP tool behavior.

## Local Neo4j runtime seam

Neo4j is a graph database. Archon uses it as the system of record for deterministic architecture facts. The AppHost uses Aspire's generic container support with the official `neo4j:latest` image.

Neo4j starts in secured mode. The AppHost reads the `neo4j-username` and `neo4j-password` Aspire parameters from `src/Archon/appsettings.json` and uses them to set the container's `NEO4J_AUTH` value. The password parameter is marked as secret in the Aspire resource graph so the same parameter flow can later move to user secrets or another secure provider without changing the composition contract.

The AppHost binds Neo4j's HTTP browser endpoint to `localhost:7474`, binds Bolt to `localhost:7687`, and advertises those host-reachable addresses to Neo4j. If you open the Neo4j Browser during manual verification, connect to `bolt://localhost:7687` with the configured `neo4j-username` and `neo4j-password` values. The direct Bolt URL avoids routing-discovery errors that can appear when Browser is pointed at a routing-style `neo4j://` URI for this local single-container instance.

## Manual AppHost verification

Manual AppHost verification is useful for local runtime exploration but must not be used as an automated build or test step because it starts a long-running distributed application and waits for manual shutdown.

Before running the AppHost, ensure Docker Desktop or another OCI-compatible container runtime is available. Then run:

```powershell
dotnet run --project .\src\Archon\Archon.csproj
```

When the Aspire dashboard opens, confirm that `neo4j`, `ArchonApi`, and `ArchonMcp` appear as resources. No `ArchonUi` or Discovery UI resource should appear. The API and MCP resources should expose `/health` checks after they are ready. Stop the AppHost manually after verification, usually with `Ctrl+C` in the terminal or by stopping the debug session in Visual Studio.
