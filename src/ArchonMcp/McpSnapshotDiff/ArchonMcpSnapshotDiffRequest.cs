namespace ArchonMcp.McpSnapshotDiff
{
    /// <summary>
    /// Describes read-only filters for the <c>archon.get_snapshot_diff</c> MCP tool.
    /// </summary>
    /// <param name="CurrentSnapshotStableKey">The explicit current snapshot stable key, or <see langword="null" /> when using implied latest-to-previous behavior.</param>
    /// <param name="PreviousSnapshotStableKey">The explicit previous snapshot stable key, or <see langword="null" /> when using implied latest-to-previous behavior.</param>
    /// <param name="UseLatestComparableSnapshots">A value indicating whether the service should compare latest and previous comparable snapshots for the supplied repository scope.</param>
    /// <param name="RepositoryStableKey">The repository stable key required for implied latest-to-previous behavior.</param>
    /// <param name="SolutionStableKey">The optional solution stable key that narrows implied latest-to-previous behavior.</param>
    /// <param name="Domains">The optional controlled domain filters.</param>
    /// <param name="ChangeKinds">The optional controlled change-kind filters.</param>
    /// <param name="ProjectStableKey">The optional owning or related project stable-key filter.</param>
    /// <param name="TargetStableKey">The optional target node, edge endpoint, finding target, or metric target stable-key filter.</param>
    /// <param name="RecordKind">The optional domain-specific kind filter.</param>
    /// <param name="Severity">The optional finding severity filter.</param>
    /// <param name="IncludeDetails">A value indicating whether bounded detail records should be returned in addition to summary counts.</param>
    /// <param name="IncludeUnchangedDetails">A value indicating whether unchanged detail rows should be included when details are returned.</param>
    /// <param name="Limit">The optional maximum number of detail records returned by MCP.</param>
    public sealed record ArchonMcpSnapshotDiffRequest(
        string? CurrentSnapshotStableKey,
        string? PreviousSnapshotStableKey,
        bool? UseLatestComparableSnapshots,
        string? RepositoryStableKey,
        string? SolutionStableKey,
        IReadOnlyList<string>? Domains,
        IReadOnlyList<string>? ChangeKinds,
        string? ProjectStableKey,
        string? TargetStableKey,
        string? RecordKind,
        string? Severity,
        bool? IncludeDetails,
        bool? IncludeUnchangedDetails,
        int? Limit);
}
