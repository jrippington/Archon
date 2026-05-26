namespace ArchonMcp.McpResources
{
    /// <summary>
    /// Handles validated parameterized Archon MCP resource requests.
    /// </summary>
    public interface IArchonMcpParameterizedResourceHandler
    {
        /// <summary>
        /// Reads a project, symbol, or snapshot diff resource and maps it to a safe MCP response.
        /// </summary>
        /// <param name="request">The validated parameterized resource request.</param>
        /// <param name="cancellationToken">The token that can cancel delegated tool or query execution.</param>
        /// <returns>A typed MCP success envelope or structured MCP error response.</returns>
        Task<object> ReadParameterizedResourceAsync(ArchonMcpResourceRequest request, CancellationToken cancellationToken);
    }
}