namespace ArchonMcp.McpDependencies
{
    /// <summary>
    /// Defines the read-only MCP dependency-path investigation tool contract.
    /// </summary>
    public interface IArchonMcpDependencyPathTool
    {
        /// <summary>
        /// Finds bounded dependency paths between two stable graph nodes.
        /// </summary>
        /// <param name="request">The path request containing source, target, scope, depth, edge-kind filters, and path limits.</param>
        /// <param name="cancellationToken">The token that cancels path search before or during query-layer execution.</param>
        /// <returns>A dependency-path envelope or structured MCP error response.</returns>
        Task<object> FindDependencyPathsAsync(ArchonMcpDependencyPathRequest request, CancellationToken cancellationToken);
    }
}
