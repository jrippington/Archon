using Archon.Api.Extraction;
using Archon.Api.Management;
using Archon.Api.Query;
using Archon.Infrastructure.Neo4j.Configuration;
using Archon.Infrastructure.Neo4j.DependencyInjection;
using Archon.ServiceDefaults;
using Scalar.AspNetCore;
using System.Net;

namespace ArchonApi
{
    /// <summary>
    /// Provides the explicit executable entry point and bootstrap seam for the Archon API host.
    /// </summary>
    /// <remarks>
        /// The API host composes operational probes, implemented feature modules, and a development-time Scalar API reference.
    /// </remarks>
    public static class Program
    {
        /// <summary>
        /// Names the development-only CORS policy used by the Aspire-hosted ArchonExplorer Vite resource.
        /// </summary>
        private const string LocalDevelopmentCorsPolicyName = "LocalDevelopmentCors";

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
        /// <returns>A configured <see cref="WebApplication"/> that maps the implemented Archon API modules and probe endpoints.</returns>
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
        /// <returns>A configured <see cref="WebApplication"/> that maps the implemented Archon API modules and probe endpoints.</returns>
        public static WebApplication BuildApplication(string[] args, Action<WebApplicationBuilder>? configureBuilder)
        {
            // The API host is a thin delivery process; all cross-cutting runtime behavior comes from ServiceDefaults while feature modules own their services.
            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
            configureBuilder?.Invoke(builder);
            builder.AddServiceDefaults();
            builder.Services.AddOpenApi();
            builder.Services.AddCors(options =>
            {
                // ArchonExplorer is served by Vite on a separate localhost port in the Aspire dashboard.
                // The policy is intentionally development-scoped and limited to loopback origins so browser
                // preflight requests can reach JSON endpoints without opening the API to arbitrary sites.
                options.AddPolicy(LocalDevelopmentCorsPolicyName, policy =>
                {
                    policy.SetIsOriginAllowed(IsLocalDevelopmentOrigin)
                        .AllowAnyHeader()
                        .AllowAnyMethod();
                });
            });
            builder.Services.AddArchonExtractionApi();
            builder.Services.AddArchonQueryApi();
            builder.Services.AddArchonManagementApi();

            if (HasNeo4jConfiguration(builder.Configuration))
            {
                // Neo4j is the production/local-AppHost system of record. Register it after module fallbacks so infrastructure adapters win.
                builder.Services.AddArchonNeo4j(builder.Configuration);
            }

            WebApplication app = builder.Build();

            if (app.Environment.IsDevelopment())
            {
                // Scalar is deliberately development-only because the generated OpenAPI document can disclose operational contract details.
                app.MapOpenApi();
                app.MapScalarApiReference(options =>
                {
                    // The UI consumes the built-in ASP.NET Core OpenAPI document and avoids Swagger UI per repository standards.
                    options.Title = "Archon API";
                    options.OpenApiRoutePattern = "/openapi/v1.json";
                });

                // Vite serves ArchonExplorer from its own local origin, while ArchonApi listens on a separate
                // Aspire-assigned endpoint. Enabling this policy in development lets the browser complete
                // CORS preflight for POST /extractions and other JSON routes before endpoint handlers run.
                app.UseCors(LocalDevelopmentCorsPolicyName);
            }

            // Health probes remain mapped alongside feature modules so operational endpoints stay available for every host slice.
            app.MapDefaultEndpoints();
            app.MapArchonExtractionApi();
            app.MapArchonQueryApi();
            app.MapArchonManagementApi();

            return app;
        }

        /// <summary>
        /// Determines whether the host has enough Neo4j configuration to compose the infrastructure adapter.
        /// </summary>
        /// <param name="configuration">The host configuration built from appsettings, environment variables, and command-line arguments.</param>
        /// <returns><see langword="true" /> when Neo4j infrastructure should be registered; otherwise <see langword="false" />.</returns>
        private static bool HasNeo4jConfiguration(IConfiguration configuration)
        {
            // Tests and lightweight local hosts can continue using in-memory fallbacks, while AppHost-provided settings enable Neo4j persistence.
            IConfigurationSection section = configuration.GetSection(Neo4jOptions.SectionName);
            return !string.IsNullOrWhiteSpace(section[nameof(Neo4jOptions.Uri)])
                || !string.IsNullOrWhiteSpace(section[nameof(Neo4jOptions.Username)])
                || !string.IsNullOrWhiteSpace(section[nameof(Neo4jOptions.Password)]);
        }

        /// <summary>
        /// Determines whether a browser Origin value represents a trusted local development frontend.
        /// </summary>
        /// <param name="origin">The Origin header value supplied by the browser CORS request.</param>
        /// <returns><see langword="true" /> when the origin is HTTP or HTTPS loopback; otherwise <see langword="false" />.</returns>
        private static bool IsLocalDevelopmentOrigin(string origin)
        {
            // Aspire and Vite can allocate different localhost ports, so the policy validates the
            // scheme and loopback host instead of hardcoding a single port that would drift locally.
            if (!Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri))
            {
                return false;
            }

            if (uri.Scheme is not "http" and not "https")
            {
                return false;
            }

            if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return IPAddress.TryParse(uri.Host, out IPAddress? address) && IPAddress.IsLoopback(address);
        }
    }
}
