namespace ArchonMcp.McpRuntime
{
    /// <summary>
    /// Describes one capability that the Archon MCP host is allowed to advertise or use for readiness validation.
    /// </summary>
    /// <param name="Name">The stable capability name used in catalog validation and safe operational reporting.</param>
    /// <param name="Kind">The category of MCP capability represented by the registration.</param>
    /// <param name="Required">A value indicating whether readiness must fail when this capability is not registered.</param>
    /// <param name="ReadOnly">A value indicating whether the capability is constrained to read-only behavior.</param>
    /// <param name="Description">A short, secret-safe description of the capability purpose.</param>
    /// <remarks>
    /// This record is intentionally small because Work Item 1 only needs a baseline registration catalog. The fields capture the
    /// cross-cutting information that later tool, resource, and prompt slices must preserve: stable naming, required registration,
    /// read-only behavior, and safe diagnostics.
    /// </remarks>
    public sealed record ArchonMcpCapabilityRegistration(
        string Name,
        ArchonMcpCapabilityKind Kind,
        bool Required,
        bool ReadOnly,
        string Description);
}
