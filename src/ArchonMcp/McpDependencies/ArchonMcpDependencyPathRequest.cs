namespace ArchonMcp.McpDependencies
{
    /// <summary>
    /// Represents an MCP request to find bounded dependency paths between two stable graph nodes.
    /// </summary>
    /// <param name="SourceNodeStableKey">The stable source node key where path search starts.</param>
    /// <param name="TargetNodeStableKey">The stable target node key where path search ends.</param>
    /// <param name="MaximumDepth">The optional maximum number of graph hops to search.</param>
    /// <param name="EdgeKindFilters">The optional controlled edge-kind filters to apply to path search.</param>
    /// <param name="Limit">The optional maximum number of path records to return after MCP limiting.</param>
    /// <param name="SnapshotSelector">The optional snapshot selector, either <c>latest</c> or a stable snapshot key.</param>
    /// <param name="RepositoryStableKey">The repository stable key that bounds snapshot resolution.</param>
    /// <param name="SolutionStableKey">The optional solution stable key that narrows repository scope.</param>
    public sealed record ArchonMcpDependencyPathRequest(
        string? SourceNodeStableKey,
        string? TargetNodeStableKey,
        int? MaximumDepth,
        IReadOnlyList<string>? EdgeKindFilters,
        int? Limit,
        string? SnapshotSelector,
        string? RepositoryStableKey,
        string? SolutionStableKey);
}
