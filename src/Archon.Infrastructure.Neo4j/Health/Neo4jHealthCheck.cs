using Archon.Infrastructure.Neo4j.Driver;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Neo4j.Driver;

namespace Archon.Infrastructure.Neo4j.Health
{
    /// <summary>
    /// Verifies that the configured Neo4j database can execute a lightweight read query.
    /// </summary>
    /// <remarks>
    /// The check intentionally uses a constant query that returns one value and does not inspect graph schema or persisted data.
    /// Schema initialization and persistence validation belong to later WP003 slices.
    /// </remarks>
    public sealed class Neo4jHealthCheck : IHealthCheck
    {
        /// <summary>
        /// Defines the health-check registration name used by infrastructure composition and tests.
        /// </summary>
        public const string Name = "neo4j";

        private const string ProbeQuery = "RETURN 1 AS healthy";
        private readonly INeo4jSessionProvider _sessionProvider;
        private readonly ILogger<Neo4jHealthCheck> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="Neo4jHealthCheck"/> class.
        /// </summary>
        /// <param name="sessionProvider">The provider that opens configured Neo4j sessions without exposing driver lifecycle details.</param>
        /// <param name="logger">The logger used for credential-safe probe diagnostics.</param>
        public Neo4jHealthCheck(INeo4jSessionProvider sessionProvider, ILogger<Neo4jHealthCheck> logger)
        {
            // Health checks do not own driver disposal. They only borrow short-lived sessions from the provider for each probe.
            _sessionProvider = sessionProvider;
            _logger = logger;
        }

        /// <summary>
        /// Runs the Neo4j dependency probe and returns a credential-safe health result.
        /// </summary>
        /// <param name="context">The health-check context supplied by ASP.NET Core health infrastructure.</param>
        /// <param name="cancellationToken">A token that cancels the asynchronous probe when the caller stops waiting.</param>
        /// <returns>A health-check result that is healthy when Neo4j can execute the probe query and unhealthy otherwise.</returns>
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            // The probe creates and disposes one read session so it exercises authentication, network connectivity, database
            // selection, and Cypher execution without mutating graph data.
            try
            {
                await using IAsyncSession session = _sessionProvider.OpenSession(AccessMode.Read);
                IResultCursor cursor = await session.RunAsync(ProbeQuery).ConfigureAwait(false);
                IRecord record = await cursor.SingleAsync().ConfigureAwait(false);
                int healthyValue = record["healthy"].As<int>();

                if (healthyValue == 1)
                {
                    return HealthCheckResult.Healthy("Neo4j accepted the lightweight health query.");
                }

                _logger.LogWarning("Neo4j health query returned an unexpected value.");
                return HealthCheckResult.Unhealthy("Neo4j health query returned an unexpected value.", data: CreateFailureData("Query"));
            }
            catch (OptionsValidationException exception)
            {
                // Options validation failures are configuration problems. The exception message comes from our validator and is
                // intentionally credential-safe, but individual failure strings are still summarized as a category for health data.
                _logger.LogWarning(exception, "Neo4j health check failed because Neo4j configuration is invalid.");
                return HealthCheckResult.Unhealthy("Neo4j configuration is invalid.", exception, CreateFailureData("Configuration"));
            }
            catch (AuthenticationException exception)
            {
                // Authentication failures are separated because they usually mean a secret or username mismatch rather than a
                // stopped server or malformed query.
                _logger.LogWarning(exception, "Neo4j health check failed because authentication was rejected.");
                return HealthCheckResult.Unhealthy("Neo4j authentication failed.", exception, CreateFailureData("Authentication"));
            }
            catch (ServiceUnavailableException exception)
            {
                // Service unavailable generally covers network, routing, or server availability problems at the driver layer.
                _logger.LogWarning(exception, "Neo4j health check failed because the service is unavailable.");
                return HealthCheckResult.Unhealthy("Neo4j service is unavailable.", exception, CreateFailureData("Network"));
            }
            catch (Neo4jException exception)
            {
                // Other Neo4j exceptions are classified as query or server-side execution failures while preserving a safe category.
                _logger.LogWarning(exception, "Neo4j health check failed while executing the health query.");
                return HealthCheckResult.Unhealthy("Neo4j health query failed.", exception, CreateFailureData("Query"));
            }
        }

        /// <summary>
        /// Creates a small health-result data payload that identifies the safe failure category.
        /// </summary>
        /// <param name="failureKind">The credential-safe failure category to expose through health details.</param>
        /// <returns>A read-only dictionary suitable for <see cref="HealthCheckResult"/> detail data.</returns>
        private static IReadOnlyDictionary<string, object> CreateFailureData(string failureKind)
        {
            // Health data can flow to logs, dashboards, and HTTP responses, so only coarse non-secret categories are included.
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["failureKind"] = failureKind
            };
        }
    }
}
