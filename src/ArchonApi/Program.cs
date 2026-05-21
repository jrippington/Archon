using Archon.Api.Extraction;
using Archon.ServiceDefaults;

namespace ArchonApi
{
    /// <summary>
    /// Provides the explicit executable entry point and bootstrap seam for the Archon API host.
    /// </summary>
    /// <remarks>
    /// WP001 limits the API host to health and readiness probes. Extraction, query, management, Scalar, Swagger UI,
    /// and human-facing UI endpoints are intentionally absent until their later work packages introduce them.
    /// </remarks>
    public static class Program
    {
        /// <summary>
        /// Starts the Archon API host with shared service defaults and health probe endpoints.
        /// </summary>
        /// <param name="args">Command-line arguments supplied by the .NET host and forwarded into ASP.NET Core configuration.</param>
        /// <returns>Zero to indicate the skeleton entry point completed successfully.</returns>
        public static int Main(string[] args)
        {
            // Build and run the web host through a separate method so tests can validate the bootstrap without launching a process.
            WebApplication app = BuildApplication(args);
            app.Logger.LogInformation("Archon API host starting with health and readiness endpoints only.");
            app.Run();

            return 0;
        }

        /// <summary>
        /// Builds the Archon API web application without starting the HTTP listener.
        /// </summary>
        /// <param name="args">Command-line arguments used by ASP.NET Core configuration and hosting.</param>
        /// <returns>A configured <see cref="WebApplication"/> that maps only WP001 probe endpoints.</returns>
        public static WebApplication BuildApplication(string[] args)
        {
            // Production startup does not need to customize the builder before service registration or endpoint mapping.
            return BuildApplication(args, configureBuilder: null);
        }

        /// <summary>
        /// Builds the Archon API web application with an optional pre-build customization hook for tests.
        /// </summary>
        /// <param name="args">Command-line arguments used by ASP.NET Core configuration and hosting.</param>
        /// <param name="configureBuilder">An optional callback that can adjust the web builder before the application is built.</param>
        /// <returns>A configured <see cref="WebApplication"/> that maps only WP001 probe endpoints.</returns>
        public static WebApplication BuildApplication(string[] args, Action<WebApplicationBuilder>? configureBuilder)
        {
            // The API host is a thin delivery process in WP001; all cross-cutting runtime behavior comes from ServiceDefaults.
            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
            configureBuilder?.Invoke(builder);
            builder.AddServiceDefaults();
            builder.Services.AddArchonExtractionApi();

            WebApplication app = builder.Build();

            // Health probes remain mapped alongside feature modules so operational endpoints stay available for every host slice.
            app.MapDefaultEndpoints();
            app.MapArchonExtractionApi();

            return app;
        }
    }
}
