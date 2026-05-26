namespace ArchonMcp.McpImpact
{
    /// <summary>
    /// Defines the read-only MCP change-impact assessment tool contract.
    /// </summary>
    public interface IArchonMcpImpactTool
    {
        /// <summary>
        /// Assesses bounded direct and transitive impacts for a supported stable target through approved query abstractions.
        /// </summary>
        /// <param name="request">The caller-supplied target, scope, depth, edge filters, and limit.</param>
        /// <param name="cancellationToken">The token that cancels execution when the host request is aborted.</param>
        /// <returns>A change-impact envelope or a structured MCP error response.</returns>
        Task<object> AssessChangeImpactAsync(ArchonMcpChangeImpactRequest request, CancellationToken cancellationToken);
    }
}
