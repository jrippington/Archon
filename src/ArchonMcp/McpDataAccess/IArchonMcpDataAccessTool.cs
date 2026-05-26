namespace ArchonMcp.McpDataAccess
{
    /// <summary>
    /// Defines the read-only MCP data-access usage tool contract.
    /// </summary>
    public interface IArchonMcpDataAccessTool
    {
        /// <summary>
        /// Lists bounded persisted data-access usage facts through the approved application/query abstraction.
        /// </summary>
        /// <param name="request">The caller-supplied data-access filters, snapshot selector, scope, and limit.</param>
        /// <param name="cancellationToken">The token that cancels execution when the host request is aborted.</param>
        /// <returns>A data-access usage envelope or a structured MCP error response.</returns>
        Task<object> GetDataAccessUsageAsync(ArchonMcpDataAccessUsageRequest request, CancellationToken cancellationToken);
    }
}
