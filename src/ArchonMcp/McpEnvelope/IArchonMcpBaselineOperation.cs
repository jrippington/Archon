namespace ArchonMcp.McpEnvelope
{
    /// <summary>
    /// Provides the baseline MCP operation through the common response envelope for host-level validation and tests.
    /// </summary>
    public interface IArchonMcpBaselineOperation
    {
        /// <summary>
        /// Builds the read-only baseline health envelope without invoking mutation, shell, database, filesystem, or code-editing behavior.
        /// </summary>
        /// <returns>A common MCP envelope describing the operational baseline capability.</returns>
        ArchonMcpEnvelope<IReadOnlyList<ArchonMcpFact>> GetHealthEnvelope();
    }
}
