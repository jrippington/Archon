namespace ArchonMcp.McpSnapshotDiff
{
    /// <summary>
    /// Defines the read-only MCP snapshot diff tool contract.
    /// </summary>
    public interface IArchonMcpSnapshotDiffTool
    {
        /// <summary>
        /// Compares snapshots through controlled application query abstractions.
        /// </summary>
        /// <param name="request">The read-only snapshot diff request.</param>
        /// <param name="cancellationToken">The token that can cancel query-layer execution before diff data is read.</param>
        /// <returns>A common MCP success envelope or a structured MCP error response.</returns>
        Task<object> GetSnapshotDiffAsync(ArchonMcpSnapshotDiffRequest request, CancellationToken cancellationToken);
    }
}
