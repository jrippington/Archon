using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Archon.ServiceDefaults
{
    /// <summary>
    /// Provides shared host configuration used by Archon runtime hosts.
    /// </summary>
    /// <remarks>
    /// The extension methods follow the .NET Aspire service-defaults pattern so each independently runnable host receives
    /// the same health probe, telemetry, service discovery, and HTTP client resilience behavior without duplicating setup code.
    /// </remarks>
    public static class Extensions
    {
        /// <summary>
        /// Adds the shared runtime defaults required by WP001 host processes.
        /// </summary>
        /// <typeparam name="TBuilder">The concrete host-application builder type being configured.</typeparam>
        /// <param name="builder">The host builder that receives shared service registrations.</param>
        /// <returns>The same builder instance so callers can continue host-specific configuration fluently.</returns>
        public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
            where TBuilder : IHostApplicationBuilder
        {
            // Service defaults are deliberately ordered from observability to dependency-facing behavior so every host records
            // telemetry and health consistently before it starts adding feature-specific services in later work packages.
            builder.ConfigureOpenTelemetry();
            builder.AddDefaultHealthChecks();
            builder.Services.AddServiceDiscovery();

            // Default HttpClient configuration gives future outbound calls resilience and Aspire service discovery without
            // forcing individual hosts to repeat the same cross-cutting registration.
            builder.Services.ConfigureHttpClientDefaults(httpClientBuilder =>
            {
                httpClientBuilder.AddStandardResilienceHandler();
                httpClientBuilder.AddServiceDiscovery();
            });

            return builder;
        }

        /// <summary>
        /// Configures OpenTelemetry-compatible logging, metrics, and tracing for a host.
        /// </summary>
        /// <typeparam name="TBuilder">The concrete host-application builder type being configured.</typeparam>
        /// <param name="builder">The host builder that receives OpenTelemetry services and logging configuration.</param>
        /// <returns>The same builder instance so callers can continue host-specific configuration fluently.</returns>
        public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder)
            where TBuilder : IHostApplicationBuilder
        {
            // OpenTelemetry logging includes formatted messages and scopes so structured host startup events remain useful
            // in the Aspire dashboard and any future OTLP-compatible collector.
            builder.Logging.AddOpenTelemetry(logging =>
            {
                logging.IncludeFormattedMessage = true;
                logging.IncludeScopes = true;
            });

            // Metrics and tracing instrumentation are limited to ASP.NET Core, HttpClient, and runtime signals in WP001
            // because no extraction, graph, query, or MCP tool behavior exists yet.
            builder.Services.AddOpenTelemetry()
                .WithMetrics(metrics =>
                {
                    metrics.AddAspNetCoreInstrumentation()
                        .AddHttpClientInstrumentation()
                        .AddRuntimeInstrumentation();
                })
                .WithTracing(tracing =>
                {
                    tracing.AddAspNetCoreInstrumentation()
                        .AddHttpClientInstrumentation();
                });

            builder.AddOpenTelemetryExporters();

            return builder;
        }

        /// <summary>
        /// Adds the default WP001 health checks used by liveness and readiness probe endpoints.
        /// </summary>
        /// <typeparam name="TBuilder">The concrete host-application builder type being configured.</typeparam>
        /// <param name="builder">The host builder that receives health-check registrations.</param>
        /// <returns>The same builder instance so callers can continue host-specific configuration fluently.</returns>
        public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder)
            where TBuilder : IHostApplicationBuilder
        {
            // The self check is tagged as live so `/alive` can verify process responsiveness without implying dependency readiness.
            builder.Services.AddHealthChecks()
                .AddCheck("self", () => HealthCheckResult.Healthy(), tags: new[] { "live" });

            return builder;
        }

        /// <summary>
        /// Maps the standard readiness and liveness endpoints for Archon hosts.
        /// </summary>
        /// <param name="endpoints">The endpoint route builder owned by the current ASP.NET Core host.</param>
        /// <returns>The same endpoint route builder so callers can continue mapping host-specific endpoints when later work packages require them.</returns>
        public static IEndpointRouteBuilder MapDefaultEndpoints(this IEndpointRouteBuilder endpoints)
        {
            // `/health` executes all registered checks and therefore represents readiness for the current host.
            endpoints.MapHealthChecks(ServiceDefaultEndpointNames.Health);

            // `/alive` filters to the lightweight self check so orchestrators can distinguish a running process from a ready service.
            endpoints.MapHealthChecks(ServiceDefaultEndpointNames.Alive, new HealthCheckOptions
            {
                Predicate = registration => registration.Tags.Contains("live")
            });

            return endpoints;
        }

        /// <summary>
        /// Adds OTLP exporters when an OTLP endpoint is configured for the current process.
        /// </summary>
        /// <typeparam name="TBuilder">The concrete host-application builder type being configured.</typeparam>
        /// <param name="builder">The host builder whose configuration determines whether exporters are enabled.</param>
        /// <returns>The same builder instance so caller chaining remains consistent.</returns>
        private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder)
            where TBuilder : IHostApplicationBuilder
        {
            // The Aspire dashboard and other collectors advertise the endpoint with OTEL_EXPORTER_OTLP_ENDPOINT.
            // Exporters stay disabled when the variable is absent so tests and standalone host runs do not require a collector.
            string? otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];

            if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            {
                builder.Services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddOtlpExporter());
                builder.Services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddOtlpExporter());
            }

            return builder;
        }
    }
}
