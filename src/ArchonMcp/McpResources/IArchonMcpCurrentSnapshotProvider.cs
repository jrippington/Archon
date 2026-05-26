namespace ArchonMcp.McpResources
{
    /// <summary>
    /// Resolves explicit current snapshot context for MCP resources through approved application-layer snapshot state.
    /// </summary>
    public interface IArchonMcpCurrentSnapshotProvider
    {
        /// <summary>
        /// Resolves the current snapshot for a repository and optional solution scope.
        /// </summary>
        /// <param name="request">The repository and optional solution scope used for current selection.</param>
        /// <param name="cancellationToken">The token that can cancel snapshot resolution before data is read.</param>
        /// <returns>A current snapshot resolution result.</returns>
        Task<ArchonMcpCurrentSnapshotResolution> ResolveCurrentSnapshotAsync(ArchonMcpCurrentSnapshotRequest request, CancellationToken cancellationToken);
    }
}
