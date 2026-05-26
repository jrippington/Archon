namespace ArchonMcp.McpDependencies
{
    /// <summary>
    /// Defines read-only MCP tool contracts for dependency and dependent traversal through approved query-layer abstractions.
    /// </summary>
    public interface IArchonMcpDependencyTool
    {
        /// <summary>
        /// Gets outgoing dependencies for the requested graph node or project.
        /// </summary>
        /// <param name="request">The traversal request containing node identity, scope, depth, filters, and limits.</param>
        /// <param name="cancellationToken">The token that can cancel query-layer traversal before response mapping completes.</param>
        /// <returns>A dependency traversal envelope or a structured MCP error response.</returns>
        Task<object> GetDependenciesAsync(ArchonMcpDependencyTraversalRequest request, CancellationToken cancellationToken);

        /// <summary>
        /// Gets incoming dependents for the requested graph node or project.
        /// </summary>
        /// <param name="request">The traversal request containing node identity, scope, depth, filters, and limits.</param>
        /// <param name="cancellationToken">The token that can cancel query-layer traversal before response mapping completes.</param>
        /// <returns>A dependent traversal envelope or a structured MCP error response.</returns>
        Task<object> GetDependentsAsync(ArchonMcpDependencyTraversalRequest request, CancellationToken cancellationToken);
    }
}
