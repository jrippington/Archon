namespace ArchonMcp.McpDependencies
{
    /// <summary>
    /// Represents the external request contract accepted by dependency and dependent traversal MCP tools.
    /// </summary>
    /// <param name="NodeStableKey">The optional exact source or target graph node stable key to traverse from.</param>
    /// <param name="ProjectStableKey">The optional project stable key alias for callers using project terminology.</param>
    /// <param name="ProjectName">The optional project display name alias retained for client ergonomics; it must not be used when a stable key is supplied.</param>
    /// <param name="Transitive">A value indicating whether traversal should include transitive relationships instead of direct relationships only.</param>
    /// <param name="MaximumDepth">The optional maximum traversal depth for transitive traversal.</param>
    /// <param name="EdgeKindFilters">The optional controlled edge-kind filters supported by the query layer.</param>
    /// <param name="Limit">The optional maximum number of relationship records returned after query and MCP limits are applied.</param>
    /// <param name="SnapshotSelector">The optional snapshot selector, such as <c>latest</c> or a <c>snapshot://</c> stable key.</param>
    /// <param name="RepositoryStableKey">The repository stable key that bounds traversal and latest snapshot resolution.</param>
    /// <param name="SolutionStableKey">The optional solution stable key that narrows repository scope when supported by the query layer.</param>
    public sealed record ArchonMcpDependencyTraversalRequest(
        string? NodeStableKey,
        string? ProjectStableKey,
        string? ProjectName,
        bool? Transitive,
        int? MaximumDepth,
        IReadOnlyList<string>? EdgeKindFilters,
        int? Limit,
        string? SnapshotSelector,
        string? RepositoryStableKey,
        string? SolutionStableKey);
}
