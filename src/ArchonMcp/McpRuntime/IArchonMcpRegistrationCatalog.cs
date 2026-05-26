namespace ArchonMcp.McpRuntime
{
    /// <summary>
    /// Exposes the safe Archon MCP capability registrations known to the host runtime.
    /// </summary>
    /// <remarks>
    /// The catalog is the central allow-list seam for Work Item 1. Tool, resource, and prompt implementations added later should
    /// register through this concept instead of independently advertising capability names.
    /// </remarks>
    public interface IArchonMcpRegistrationCatalog
    {
        /// <summary>
        /// Gets the registered MCP capabilities in deterministic name order.
        /// </summary>
        /// <returns>A read-only list of safe capability registrations known to the host.</returns>
        IReadOnlyList<ArchonMcpCapabilityRegistration> GetRegistrations();

        /// <summary>
        /// Validates that the catalog contains all mandatory capabilities and no forbidden capability names.
        /// </summary>
        /// <returns>A validation result suitable for readiness checks and tests.</returns>
        ArchonMcpCatalogValidationResult Validate();
    }
}
