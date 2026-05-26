namespace ArchonMcp.McpProjects
{
    /// <summary>
    /// Represents the external request contract accepted by the <c>archon.describe_project</c> MCP tool.
    /// </summary>
    /// <param name="ProjectStableKey">The optional exact project stable key to describe.</param>
    /// <param name="ProjectName">The optional project display name, which must resolve unambiguously when a stable key is not supplied.</param>
    /// <param name="SnapshotSelector">The optional snapshot selector, such as <c>latest</c> or a <c>snapshot://</c> stable key.</param>
    /// <param name="RepositoryStableKey">The repository stable key that bounds project lookup and latest snapshot resolution.</param>
    /// <param name="SolutionStableKey">The optional solution stable key that narrows repository scope when supported by the query layer.</param>
    public sealed record ArchonMcpDescribeProjectRequest(
        string? ProjectStableKey,
        string? ProjectName,
        string? SnapshotSelector,
        string? RepositoryStableKey,
        string? SolutionStableKey);
}
