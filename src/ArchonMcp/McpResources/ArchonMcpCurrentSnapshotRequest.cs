namespace ArchonMcp.McpResources
{
    /// <summary>
    /// Represents an explicit current snapshot selection request for MCP resources.
    /// </summary>
    /// <param name="RepositoryStableKey">The repository stable key that bounds current snapshot selection.</param>
    /// <param name="SolutionStableKey">The optional solution stable key that narrows current snapshot selection.</param>
    public sealed record ArchonMcpCurrentSnapshotRequest(string RepositoryStableKey, string? SolutionStableKey);
}
