namespace Archon
{
    /// <summary>
    /// Provides the explicit executable entry point and composition root for the Archon Aspire AppHost.
    /// </summary>
    /// <remarks>
    /// The AppHost is responsible only for local-development orchestration. It wires runtime resources together but must not
    /// contain domain rules, extraction logic, graph persistence behavior, API endpoint implementations, or MCP capabilities.
    /// </remarks>
    public static class Program
    {
        /// <summary>
        /// Starts the Archon distributed application through the Aspire AppHost.
        /// </summary>
        /// <param name="args">Command-line arguments supplied by the .NET host and forwarded into Aspire distributed-application configuration.</param>
        /// <returns>Zero when the AppHost run completes normally.</returns>
        public static int Main(string[] args)
        {
            // The AppHost run is intentionally reached only through manual execution because it starts long-running resources.
            DistributedApplication app = BuildApplication(args);
            app.Run();

            return 0;
        }

        /// <summary>
        /// Builds the Archon distributed application model without starting it.
        /// </summary>
        /// <param name="args">Command-line arguments used by Aspire configuration and resource orchestration.</param>
        /// <returns>The configured distributed application containing Neo4j, the API host, the MCP host, and the ArchonExplorer Vite resource.</returns>
        public static DistributedApplication BuildApplication(string[] args)
        {
            // This method is the single composition root for WP001: it declares resources and dependencies only.
            IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

            // Neo4j credentials are modeled as Aspire parameters so local configuration supplies the values without hardcoding them in the composition expression.
            IResourceBuilder<ParameterResource> neo4jUsername = builder.AddParameter("neo4j-username");
            IResourceBuilder<ParameterResource> neo4jPassword = builder.AddParameter("neo4j-password", secret: true);

            // Neo4j is composed as a container resource because no dedicated Aspire.Hosting.Neo4j package exists in NuGet.
            // The NEO4J_AUTH value is assembled from parameters so the container starts in secured mode while preserving Aspire's parameter flow.
            IResourceBuilder<ContainerResource> neo4j = builder.AddContainer("neo4j", "neo4j", "latest")
                .WithEnvironment("NEO4J_AUTH", ReferenceExpression.Create($"{neo4jUsername}/{neo4jPassword}"))
                .WithEnvironment("NEO4J_server_http_advertised__address", "localhost:7474")
                .WithEnvironment("NEO4J_server_bolt_advertised__address", "localhost:7687")
                .WithVolume("archon-neo4j-data", "/data")
                .WithVolume("archon-neo4j-logs", "/logs")
                .WithVolume("archon-neo4j-import", "/var/lib/neo4j/import")
                .WithVolume("archon-neo4j-plugins", "/plugins")
                .WithHttpEndpoint(port: 7474, targetPort: 7474, name: "browser")
                .WithEndpoint(port: 7687, targetPort: 7687, scheme: "tcp", name: "bolt");

            // The API host waits for Neo4j and exposes its Work Item 2 readiness probe to the Aspire dashboard.
            IResourceBuilder<ProjectResource> api = builder.AddProject<Projects.ArchonApi>("ArchonApi")
                .WithEnvironment("Neo4j__Uri", "bolt://localhost:7687")
                .WithEnvironment("Neo4j__Database", "neo4j")
                .WithEnvironment("Neo4j__Username", neo4jUsername)
                .WithEnvironment("Neo4j__Password", neo4jPassword)
                .WithEnvironment("Neo4j__EncryptionMode", "Unencrypted")
                .WaitFor(neo4j)
                .WithHttpHealthCheck("/health");

            // The MCP host is composed beside the API host and waits for both Neo4j and the API resource to be present.
            builder.AddProject<Projects.ArchonMcp>("ArchonMcp")
                .WaitFor(neo4j)
                .WaitFor(api)
                .WithHttpHealthCheck("/health");

            // ArchonExplorer is hosted as a Vite resource so local Aspire startup can serve the browser shell while keeping UI logic inside the frontend project.
            // The AppHost only supplies safe development-time configuration and dependency ordering; it does not implement API clients, workbench state, or UI behavior.
            builder.AddViteApp("ArchonExplorer", "../ArchonExplorer")
                .WithEnvironment("VITE_ARCHON_API_BASE_URL", api.GetEndpoint("http"))
                .WaitFor(api);

            // No Discovery UI resource is intentionally declared in WP001; ArchonExplorer is the current browser shell resource.
            return builder.Build();
        }
    }
}
