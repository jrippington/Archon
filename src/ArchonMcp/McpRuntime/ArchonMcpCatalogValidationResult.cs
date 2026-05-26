namespace ArchonMcp.McpRuntime
{
    /// <summary>
    /// Reports whether the configured Archon MCP capability catalog is safe and complete enough for host readiness.
    /// </summary>
    /// <param name="IsReady">A value indicating whether all mandatory registrations are present and valid.</param>
    /// <param name="MissingRequiredCapabilityNames">Required capability names that were not registered.</param>
    /// <param name="ForbiddenCapabilityNames">Registered capability names that are disallowed by the read-only MCP baseline.</param>
    /// <remarks>
    /// The result deliberately exposes only capability names and high-level validation categories. It does not include stack traces,
    /// configuration internals, service-provider details, or any user data that could leak sensitive runtime state through readiness.
    /// </remarks>
    public sealed record ArchonMcpCatalogValidationResult(
        bool IsReady,
        IReadOnlyList<string> MissingRequiredCapabilityNames,
        IReadOnlyList<string> ForbiddenCapabilityNames);
}
