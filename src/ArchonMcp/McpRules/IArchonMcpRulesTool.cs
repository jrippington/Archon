namespace ArchonMcp.McpRules
{
    /// <summary>
    /// Defines the read-only MCP architecture-rule catalog tool contract.
    /// </summary>
    public interface IArchonMcpRulesTool
    {
        /// <summary>
        /// Lists architecture-rule catalog records through controlled query abstractions.
        /// </summary>
        /// <param name="request">The read-only rule catalog request.</param>
        /// <param name="cancellationToken">The token that can cancel query-layer execution before catalog data is read.</param>
        /// <returns>A common MCP success envelope or a structured MCP error response.</returns>
        Task<object> GetArchitectureRulesAsync(ArchonMcpArchitectureRulesRequest request, CancellationToken cancellationToken);
    }
}
