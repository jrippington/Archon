using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ArchonMcp.McpRuntime
{
    /// <summary>
    /// Reports Archon MCP readiness from the configured capability registration catalog.
    /// </summary>
    /// <remarks>
    /// The health check fails closed when required capabilities are missing or forbidden capability names are present. Failure
    /// details are intentionally high-level so readiness cannot leak service-provider internals, secrets, stack traces, or graph
    /// persistence details.
    /// </remarks>
    internal sealed class ArchonMcpCatalogHealthCheck : IHealthCheck
    {
        /// <summary>
        /// Holds the catalog that supplies deterministic registration validation for each readiness probe.
        /// </summary>
        private readonly IArchonMcpRegistrationCatalog _catalog;

        /// <summary>
        /// Creates the readiness check from the MCP registration catalog.
        /// </summary>
        /// <param name="catalog">The catalog used to validate mandatory and forbidden MCP capabilities.</param>
        public ArchonMcpCatalogHealthCheck(IArchonMcpRegistrationCatalog catalog)
        {
            // The catalog is required because readiness must fail closed rather than silently succeeding without validation.
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        /// <summary>
        /// Checks whether the MCP catalog is complete and safe enough for the host to accept read-only MCP traffic.
        /// </summary>
        /// <param name="context">The health-check context supplied by the ASP.NET Core health-check pipeline.</param>
        /// <param name="cancellationToken">A cancellation token supplied by the readiness probe caller.</param>
        /// <returns>A completed health-check result describing ready or not-ready catalog state.</returns>
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            // Catalog validation is in-memory and deterministic, so cancellation does not require asynchronous cleanup work.
            ArchonMcpCatalogValidationResult validationResult = _catalog.Validate();

            if (validationResult.IsReady)
            {
                return Task.FromResult(HealthCheckResult.Healthy("Archon MCP mandatory registrations are present and read-only."));
            }

            Dictionary<string, object> data = new(StringComparer.OrdinalIgnoreCase)
            {
                ["missingRequiredCapabilityCount"] = validationResult.MissingRequiredCapabilityNames.Count,
                ["forbiddenCapabilityCount"] = validationResult.ForbiddenCapabilityNames.Count
            };

            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Archon MCP mandatory registration catalog is incomplete or unsafe.",
                data: data));
        }
    }
}
