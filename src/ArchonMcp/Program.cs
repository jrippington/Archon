using Archon.ServiceDefaults;

namespace ArchonMcp
{
    /// <summary>
    /// Provides the explicit executable entry point and bootstrap seam for the Archon MCP host.
    /// </summary>
    /// <remarks>
    /// WP001 limits the MCP host to health and readiness probes. MCP tools, resources, prompts, and architecture-query
    /// behavior are intentionally absent until later work packages add evidence-backed MCP capabilities.
    /// </remarks>
    public static class Program
    {
        /// <summary>
        /// Starts the Archon MCP host with shared service defaults and health probe endpoints.
        /// </summary>
        /// <param name="args">Command-line arguments supplied by the .NET host and forwarded into ASP.NET Core configuration.</param>
        /// <returns>Zero to indicate the skeleton entry point completed successfully.</returns>
        public static int Main(string[] args)
        {
            // Build and run the web host through a separate method so tests can validate probes without launching a process.
            WebApplication app = BuildApplication(args);
            app.Logger.LogInformation("Archon MCP host starting with health and readiness endpoints only.");
            app.Run();

            return 0;
        }

        /// <summary>
        /// Builds the Archon MCP web application without starting the HTTP listener.
        /// </summary>
        /// <param name="args">Command-line arguments used by ASP.NET Core configuration and hosting.</param>
        /// <returns>A configured <see cref="WebApplication"/> that maps only WP001 probe endpoints.</returns>
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
        /// <returns>A configured <see cref="WebApplication"/> that maps only WP001 probe endpoints.</returns>
        public static WebApplication BuildApplication(string[] args, Action<WebApplicationBuilder>? configureBuilder)
        {
            // The MCP host receives the same runtime defaults as the API host while keeping MCP feature surfaces absent in WP001.
            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
            configureBuilder?.Invoke(builder);
            builder.AddServiceDefaults();

            WebApplication app = builder.Build();

            // Probe endpoints are the only mapped HTTP surface until a later MCP work package adds tools, resources, or prompts.
            app.MapDefaultEndpoints();

            return app;
        }
    }
}
