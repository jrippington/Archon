namespace ArchonMcp.McpResources
{
    /// <summary>
    /// Handles validated current Archon MCP resource requests.
    /// </summary>
    public interface IArchonMcpCurrentResourceHandler
    {
        /// <summary>
        /// Reads a validated current resource and maps it to a safe MCP response.
        /// </summary>
        /// <param name="request">The validated current resource request.</param>
        /// <param name="cancellationToken">The token that can cancel current selection or query execution.</param>
        /// <returns>A typed MCP success envelope or structured MCP error response.</returns>
        Task<object> ReadCurrentResourceAsync(ArchonMcpResourceRequest request, CancellationToken cancellationToken);
    }
}
