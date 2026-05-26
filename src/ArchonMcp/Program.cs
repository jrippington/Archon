using Archon.ServiceDefaults;
using ArchonMcp.McpDataAccess;
using ArchonMcp.McpDependencies;
using ArchonMcp.McpEnvelope;
using ArchonMcp.McpHotlist;
using ArchonMcp.McpImpact;
using ArchonMcp.McpPrompts;
using ArchonMcp.McpProjects;
using ArchonMcp.McpResources;
using ArchonMcp.McpRules;
using ArchonMcp.McpRuntime;
using ArchonMcp.McpSearch;
using ArchonMcp.McpSecurity;
using ArchonMcp.McpSnapshotDiff;
using ArchonMcp.McpSymbols;

namespace ArchonMcp
{
    /// <summary>
    /// Provides the explicit executable entry point and bootstrap seam for the Archon MCP host.
    /// </summary>
    /// <remarks>
    /// WP015 keeps the externally mapped HTTP surface to health and readiness probes while adding internal read-only MCP
    /// registration, response-envelope, security, allow-list, audit, and redaction seams for later tools, resources, and prompts.
    /// </remarks>
    public static class Program
    {
        /// <summary>
        /// Starts the Archon MCP host with shared service defaults, health probe endpoints, and baseline MCP readiness wiring.
        /// </summary>
        /// <param name="args">Command-line arguments supplied by the .NET host and forwarded into ASP.NET Core configuration.</param>
        /// <returns>Zero to indicate the skeleton entry point completed successfully.</returns>
        public static int Main(string[] args)
        {
            // Build and run the web host through a separate method so tests can validate probes and catalog readiness without
            // launching a long-running process or Aspire AppHost.
            WebApplication app = BuildApplication(args);
            app.Logger.LogInformation("Archon MCP host starting with read-only baseline registration catalog, security seams, and probe endpoints.");
            app.Run();

            return 0;
        }

        /// <summary>
        /// Builds the Archon MCP web application without starting the HTTP listener.
        /// </summary>
        /// <param name="args">Command-line arguments used by ASP.NET Core configuration and hosting.</param>
        /// <returns>A configured <see cref="WebApplication"/> that maps probe endpoints and composes the read-only MCP baseline.</returns>
        public static WebApplication BuildApplication(string[] args)
        {
            // Production startup does not need to customize the builder before service registration or endpoint mapping.
            return BuildApplication(args, configureBuilder: null);
        }

        /// <summary>
        /// Builds the Archon MCP web application with an optional pre-build customization hook for tests.
        /// </summary>
        /// <param name="args">Command-line arguments used by ASP.NET Core configuration and hosting.</param>
        /// <param name="configureBuilder">An optional callback that can adjust the web builder before the application is built.</param>
        /// <returns>A configured <see cref="WebApplication"/> that maps probe endpoints and composes the read-only MCP baseline.</returns>
        public static WebApplication BuildApplication(string[] args, Action<WebApplicationBuilder>? configureBuilder)
        {
            // The MCP host receives the same runtime defaults as the API host and then composes the WP015 baseline services that
            // prove mandatory MCP registrations can be checked before concrete tool/resource/prompt slices are added.
            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
            configureBuilder?.Invoke(builder);
            builder.AddServiceDefaults();
            builder.Services.AddArchonMcpRuntimeBaseline(builder.Configuration);

            WebApplication app = builder.Build();

            // Probe endpoints remain the primary externally mapped HTTP surface; the internal catalog and security executor prove
            // safe composition without exposing arbitrary shell, SQL, Cypher, filesystem, Neo4j, mutation, or code-editing access.
            app.MapDefaultEndpoints();
            app.MapGet("/mcp/operations/archon.health", async (
                IArchonMcpOperationExecutor executor,
                IArchonMcpBaselineOperation baselineOperation,
                CancellationToken cancellationToken) =>
            {
                // The endpoint is a narrow operational probe for end-to-end Work Item 3 verification, not a general MCP tool route.
                ArchonMcpOperationResult result = await executor.ExecuteAsync(
                    ArchonMcpBaselineCapabilities.Health.Name,
                    parameters: null,
                    () => Task.FromResult<object>(baselineOperation.GetHealthEnvelope()),
                    cancellationToken);

                int statusCode = result.Payload is ArchonMcpErrorResponse error && error.Error.Category == ArchonMcpErrorCategory.Unauthorized
                    ? StatusCodes.Status401Unauthorized
                    : StatusCodes.Status403Forbidden;

                return result.Succeeded ? Results.Ok(result.Payload) : Results.Json(result.Payload, statusCode: statusCode);
            });
            app.MapPost("/mcp/tools/archon.search", async (
                ArchonMcpSearchRequest request,
                IArchonMcpSearchTool searchTool,
                CancellationToken cancellationToken) =>
            {
                // This narrow HTTP path supports end-to-end verification until the protocol adapter maps the same handler as an MCP tool.
                object payload = await searchTool.SearchAsync(request, cancellationToken).ConfigureAwait(false);
                int statusCode = payload is ArchonMcpErrorResponse error
                    ? MapErrorStatusCode(error.Error.Category)
                    : StatusCodes.Status200OK;

                return payload is ArchonMcpErrorResponse
                    ? Results.Json(payload, statusCode: statusCode)
                    : Results.Ok(payload);
            });
            app.MapPost("/mcp/tools/archon.describe_project", async (
                ArchonMcpDescribeProjectRequest request,
                IArchonMcpProjectTool projectTool,
                CancellationToken cancellationToken) =>
            {
                // This verification path maps the same handler that the MCP protocol adapter will expose as a read-only tool.
                object payload = await projectTool.DescribeProjectAsync(request, cancellationToken).ConfigureAwait(false);
                int statusCode = payload is ArchonMcpErrorResponse error
                    ? MapErrorStatusCode(error.Error.Category)
                    : StatusCodes.Status200OK;

                return payload is ArchonMcpErrorResponse
                    ? Results.Json(payload, statusCode: statusCode)
                    : Results.Ok(payload);
            });
            app.MapPost("/mcp/tools/archon.get_dependencies", async (
                ArchonMcpDependencyTraversalRequest request,
                IArchonMcpDependencyTool dependencyTool,
                CancellationToken cancellationToken) =>
            {
                // Outgoing dependency verification is intentionally narrow and delegates all behavior to the MCP tool handler.
                object payload = await dependencyTool.GetDependenciesAsync(request, cancellationToken).ConfigureAwait(false);
                int statusCode = payload is ArchonMcpErrorResponse error
                    ? MapErrorStatusCode(error.Error.Category)
                    : StatusCodes.Status200OK;

                return payload is ArchonMcpErrorResponse
                    ? Results.Json(payload, statusCode: statusCode)
                    : Results.Ok(payload);
            });
            app.MapPost("/mcp/tools/archon.get_dependents", async (
                ArchonMcpDependencyTraversalRequest request,
                IArchonMcpDependencyTool dependencyTool,
                CancellationToken cancellationToken) =>
            {
                // Incoming dependent verification mirrors outgoing dependency verification while preserving a separate operation name.
                object payload = await dependencyTool.GetDependentsAsync(request, cancellationToken).ConfigureAwait(false);
                int statusCode = payload is ArchonMcpErrorResponse error
                    ? MapErrorStatusCode(error.Error.Category)
                    : StatusCodes.Status200OK;

                return payload is ArchonMcpErrorResponse
                    ? Results.Json(payload, statusCode: statusCode)
                    : Results.Ok(payload);
            });
            app.MapPost("/mcp/tools/archon.find_dependency_paths", async (
                ArchonMcpDependencyPathRequest request,
                IArchonMcpDependencyPathTool dependencyPathTool,
                CancellationToken cancellationToken) =>
            {
                // Dependency-path verification delegates all behavior to the MCP tool handler and exposes no arbitrary graph query surface.
                object payload = await dependencyPathTool.FindDependencyPathsAsync(request, cancellationToken).ConfigureAwait(false);
                int statusCode = payload is ArchonMcpErrorResponse error
                    ? MapErrorStatusCode(error.Error.Category)
                    : StatusCodes.Status200OK;

                return payload is ArchonMcpErrorResponse
                    ? Results.Json(payload, statusCode: statusCode)
                    : Results.Ok(payload);
            });
            app.MapPost("/mcp/tools/archon.describe_symbol", async (
                ArchonMcpDescribeSymbolRequest request,
                IArchonMcpSymbolTool symbolTool,
                CancellationToken cancellationToken) =>
            {
                // Symbol description verification uses the same handler that the MCP protocol adapter exposes as a read-only tool.
                object payload = await symbolTool.DescribeSymbolAsync(request, cancellationToken).ConfigureAwait(false);
                int statusCode = payload is ArchonMcpErrorResponse error
                    ? MapErrorStatusCode(error.Error.Category)
                    : StatusCodes.Status200OK;

                return payload is ArchonMcpErrorResponse
                    ? Results.Json(payload, statusCode: statusCode)
                    : Results.Ok(payload);
            });
            app.MapPost("/mcp/tools/archon.find_symbol_usages", async (
                ArchonMcpFindSymbolUsagesRequest request,
                IArchonMcpSymbolTool symbolTool,
                CancellationToken cancellationToken) =>
            {
                // Symbol usage verification remains bounded and delegates source, usage, and evidence facts to the query-layer seam.
                object payload = await symbolTool.FindSymbolUsagesAsync(request, cancellationToken).ConfigureAwait(false);
                int statusCode = payload is ArchonMcpErrorResponse error
                    ? MapErrorStatusCode(error.Error.Category)
                    : StatusCodes.Status200OK;

                return payload is ArchonMcpErrorResponse
                    ? Results.Json(payload, statusCode: statusCode)
                    : Results.Ok(payload);
            });
            app.MapPost("/mcp/tools/archon.get_data_access_usage", async (
                ArchonMcpDataAccessUsageRequest request,
                IArchonMcpDataAccessTool dataAccessTool,
                CancellationToken cancellationToken) =>
            {
                // Data-access verification delegates all behavior to the MCP tool handler and exposes no arbitrary SQL, Cypher, filesystem, or mutation surface.
                object payload = await dataAccessTool.GetDataAccessUsageAsync(request, cancellationToken).ConfigureAwait(false);
                int statusCode = payload is ArchonMcpErrorResponse error
                    ? MapErrorStatusCode(error.Error.Category)
                    : StatusCodes.Status200OK;

                return payload is ArchonMcpErrorResponse
                    ? Results.Json(payload, statusCode: statusCode)
                    : Results.Ok(payload);
            });
            app.MapPost("/mcp/tools/archon.assess_change_impact", async (
                ArchonMcpChangeImpactRequest request,
                IArchonMcpImpactTool impactTool,
                CancellationToken cancellationToken) =>
            {
                // Change-impact verification delegates to the read-only handler and frames output as investigation guidance, not remediation authority.
                object payload = await impactTool.AssessChangeImpactAsync(request, cancellationToken).ConfigureAwait(false);
                int statusCode = payload is ArchonMcpErrorResponse error
                    ? MapErrorStatusCode(error.Error.Category)
                    : StatusCodes.Status200OK;

                return payload is ArchonMcpErrorResponse
                    ? Results.Json(payload, statusCode: statusCode)
                    : Results.Ok(payload);
            });
            app.MapPost("/mcp/tools/archon.get_architecture_rules", async (
                ArchonMcpArchitectureRulesRequest request,
                IArchonMcpRulesTool rulesTool,
                CancellationToken cancellationToken) =>
            {
                // Architecture-rule verification delegates to the read-only handler and exposes no rule enable, disable, edit, delete, or suppression surface.
                object payload = await rulesTool.GetArchitectureRulesAsync(request, cancellationToken).ConfigureAwait(false);
                int statusCode = payload is ArchonMcpErrorResponse error
                    ? MapErrorStatusCode(error.Error.Category)
                    : StatusCodes.Status200OK;

                return payload is ArchonMcpErrorResponse
                    ? Results.Json(payload, statusCode: statusCode)
                    : Results.Ok(payload);
            });
            app.MapPost("/mcp/tools/archon.get_hotlist_findings", async (
                ArchonMcpHotlistFindingsRequest request,
                IArchonMcpHotlistTool hotlistTool,
                CancellationToken cancellationToken) =>
            {
                // Hotlist verification delegates to the read-only handler and intentionally omits suppression or finding mutation behavior.
                object payload = await hotlistTool.GetHotlistFindingsAsync(request, cancellationToken).ConfigureAwait(false);
                int statusCode = payload is ArchonMcpErrorResponse error
                    ? MapErrorStatusCode(error.Error.Category)
                    : StatusCodes.Status200OK;

                return payload is ArchonMcpErrorResponse
                    ? Results.Json(payload, statusCode: statusCode)
                    : Results.Ok(payload);
            });
            app.MapPost("/mcp/tools/archon.get_snapshot_diff", async (
                ArchonMcpSnapshotDiffRequest request,
                IArchonMcpSnapshotDiffTool snapshotDiffTool,
                CancellationToken cancellationToken) =>
            {
                // Snapshot-diff verification delegates to the read-only handler and exposes no snapshot creation, deletion, or graph mutation surface.
                object payload = await snapshotDiffTool.GetSnapshotDiffAsync(request, cancellationToken).ConfigureAwait(false);
                int statusCode = payload is ArchonMcpErrorResponse error
                    ? MapErrorStatusCode(error.Error.Category)
                    : StatusCodes.Status200OK;

                return payload is ArchonMcpErrorResponse
                    ? Results.Json(payload, statusCode: statusCode)
                    : Results.Ok(payload);
            });
            app.MapGet("/mcp/resources", async (
                string uri,
                IArchonMcpResourceDispatcher resourceDispatcher,
                CancellationToken cancellationToken) =>
            {
                // Resource verification delegates to the same dispatcher that the MCP protocol adapter will expose for read-only resource reads.
                object payload = await resourceDispatcher.ReadResourceAsync(uri, cancellationToken).ConfigureAwait(false);
                int statusCode = payload is ArchonMcpErrorResponse error
                    ? MapErrorStatusCode(error.Error.Category)
                    : StatusCodes.Status200OK;

                return payload is ArchonMcpErrorResponse
                    ? Results.Json(payload, statusCode: statusCode)
                    : Results.Ok(payload);
            });
            app.MapGet("/mcp/prompts", async (
                IArchonMcpPromptTool promptTool,
                CancellationToken cancellationToken) =>
            {
                // Prompt listing is a narrow verification path for the same read-only prompt registry used by MCP transport wiring.
                object payload = await promptTool.ListPromptsAsync(cancellationToken).ConfigureAwait(false);
                int statusCode = payload is ArchonMcpErrorResponse error
                    ? MapErrorStatusCode(error.Error.Category)
                    : StatusCodes.Status200OK;

                return payload is ArchonMcpErrorResponse
                    ? Results.Json(payload, statusCode: statusCode)
                    : Results.Ok(payload);
            });
            app.MapGet("/mcp/prompts/{name}", async (
                string name,
                IArchonMcpPromptTool promptTool,
                CancellationToken cancellationToken) =>
            {
                // Prompt retrieval returns versioned markdown assets and does not read arbitrary files or inspect repositories.
                object payload = await promptTool.GetPromptAsync(new ArchonMcpPromptRequest { Name = name }, cancellationToken).ConfigureAwait(false);
                int statusCode = payload is ArchonMcpErrorResponse error
                    ? MapErrorStatusCode(error.Error.Category)
                    : StatusCodes.Status200OK;

                return payload is ArchonMcpErrorResponse
                    ? Results.Json(payload, statusCode: statusCode)
                    : Results.Ok(payload);
            });

            return app;
        }

        /// <summary>
        /// Maps structured MCP error categories to HTTP status codes for verification-only host endpoints.
        /// </summary>
        /// <param name="category">The structured MCP error category returned by a handler.</param>
        /// <returns>The HTTP status code that best represents the MCP error category.</returns>
        private static int MapErrorStatusCode(ArchonMcpErrorCategory category)
        {
            // The mapping keeps probe-style HTTP verification aligned with the MCP error envelope without exposing implementation details.
            return category switch
            {
                ArchonMcpErrorCategory.Validation => StatusCodes.Status400BadRequest,
                ArchonMcpErrorCategory.Unauthorized => StatusCodes.Status401Unauthorized,
                ArchonMcpErrorCategory.Forbidden => StatusCodes.Status403Forbidden,
                ArchonMcpErrorCategory.NotFound => StatusCodes.Status404NotFound,
                ArchonMcpErrorCategory.DependencyUnavailable => StatusCodes.Status503ServiceUnavailable,
                ArchonMcpErrorCategory.QueryLayerFailure => StatusCodes.Status502BadGateway,
                _ => StatusCodes.Status500InternalServerError
            };
        }
    }
}
