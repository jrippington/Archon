namespace Archon.ServiceDefaults
{
    /// <summary>
    /// Provides the canonical probe endpoint paths shared by Archon hosts in WP001.
    /// </summary>
    /// <remarks>
    /// Centralizing the names keeps the API host, MCP host, tests, and later AppHost probe configuration aligned.
    /// A liveness probe answers whether the process is alive, while a readiness probe answers whether the host can accept work.
    /// </remarks>
    public static class ServiceDefaultEndpointNames
    {
        /// <summary>
        /// Gets the readiness endpoint path that runs all registered health checks.
        /// </summary>
        public const string Health = "/health";

        /// <summary>
        /// Gets the liveness endpoint path that confirms the host process is responsive.
        /// </summary>
        public const string Alive = "/alive";
    }
}
