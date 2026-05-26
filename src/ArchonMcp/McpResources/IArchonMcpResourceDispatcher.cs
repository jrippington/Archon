namespace ArchonMcp.McpResources
{
    /// <summary>
    /// Dispatches safe Archon MCP resource reads after authorization and URI validation.
    /// </summary>
    public interface IArchonMcpResourceDispatcher
    {
        /// <summary>
        /// Reads a supported Archon MCP resource URI.
        /// </summary>
        /// <param name="uri">The resource URI supplied by the MCP client.</param>
        /// <param name="cancellationToken">The token that can cancel authorization, parsing, current selection, or query execution.</param>
        /// <returns>A typed MCP success envelope or structured MCP error response.</returns>
        Task<object> ReadResourceAsync(string? uri, CancellationToken cancellationToken);
    }
}
