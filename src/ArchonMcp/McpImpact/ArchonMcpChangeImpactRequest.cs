namespace ArchonMcp.McpImpact
{
    /// <summary>
    /// Represents caller-supplied inputs for the <c>archon.assess_change_impact</c> MCP tool.
    /// </summary>
    /// <param name="TargetStableKey">The supported stable key whose incoming direct and transitive impact should be assessed.</param>
    /// <param name="MaximumDepth">The optional maximum incoming traversal depth used for transitive impact assessment.</param>
    /// <param name="EdgeKindFilters">The optional controlled edge-kind filters used to narrow impact relationships.</param>
    /// <param name="Limit">The optional maximum number of impact relationships to return before MCP truncation metadata is emitted.</param>
    /// <param name="IncludeTransitive">A value indicating whether transitive impacts should be included beyond direct consumers.</param>
    /// <param name="SnapshotSelector">The optional snapshot selector, either <c>latest</c> or a stable snapshot key.</param>
    /// <param name="RepositoryStableKey">The repository stable key that bounds latest/current snapshot resolution.</param>
    /// <param name="SolutionStableKey">The optional solution stable key that narrows repository scope.</param>
    public sealed record ArchonMcpChangeImpactRequest(
        string? TargetStableKey,
        int? MaximumDepth,
        IReadOnlyList<string>? EdgeKindFilters,
        int? Limit,
        bool? IncludeTransitive,
        string? SnapshotSelector,
        string? RepositoryStableKey,
        string? SolutionStableKey);
}
