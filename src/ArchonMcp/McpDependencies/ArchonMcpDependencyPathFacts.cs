namespace ArchonMcp.McpDependencies
{
    /// <summary>
    /// Represents the structured facts returned by <c>archon.find_dependency_paths</c>.
    /// </summary>
    /// <param name="SourceNodeStableKey">The stable source node key where path search started.</param>
    /// <param name="TargetNodeStableKey">The stable target node key where path search ended.</param>
    /// <param name="PathFound">A value indicating whether at least one dependency path was found.</param>
    /// <param name="DataAvailable">A value indicating whether persisted graph data was sufficient to answer the path question.</param>
    /// <param name="MaximumDepth">The maximum graph depth applied by path search.</param>
    /// <param name="EdgeKindFilters">The normalized edge-kind filters applied by path search.</param>
    /// <param name="Paths">The bounded deterministic path records returned to the MCP client.</param>
    public sealed record ArchonMcpDependencyPathFacts(
        string SourceNodeStableKey,
        string TargetNodeStableKey,
        bool PathFound,
        bool DataAvailable,
        int MaximumDepth,
        IReadOnlyList<string> EdgeKindFilters,
        IReadOnlyList<ArchonMcpDependencyPathRecord> Paths);
}
