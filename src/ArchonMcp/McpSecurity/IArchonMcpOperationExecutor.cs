namespace ArchonMcp.McpSecurity
{
    /// <summary>
    /// Executes MCP operations through authorization, allow-list, audit, and safe error handling seams.
    /// </summary>
    public interface IArchonMcpOperationExecutor
    {
        /// <summary>
        /// Executes an MCP operation after security checks have passed.
        /// </summary>
        /// <param name="operationName">The stable MCP operation name being requested.</param>
        /// <param name="parameters">The raw operation parameters that must be normalized before audit logging.</param>
        /// <param name="operation">The operation delegate that invokes the handler or query-layer path after authorization succeeds.</param>
        /// <param name="cancellationToken">The token that observes cancellation before and during operation execution.</param>
        /// <returns>The operation result containing a success envelope or a structured safe error.</returns>
        Task<ArchonMcpOperationResult> ExecuteAsync(
            string operationName,
            IReadOnlyDictionary<string, string>? parameters,
            Func<Task<object>> operation,
            CancellationToken cancellationToken);
    }
}
