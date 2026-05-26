namespace ArchonMcp.McpSnapshotDiff
{
    /// <summary>
    /// Carries the structured facts returned by the snapshot diff MCP tool.
    /// </summary>
    /// <param name="CurrentSnapshotStableKey">The current snapshot stable key used for comparison.</param>
    /// <param name="PreviousSnapshotStableKey">The previous snapshot stable key used for comparison.</param>
    /// <param name="ComparisonScope">The repository or compatibility scope used for comparison.</param>
    /// <param name="UsedImpliedPreviousSnapshot">A value indicating whether the query used latest-to-previous snapshot resolution.</param>
    /// <param name="Domains">The controlled domain filters applied when supplied.</param>
    /// <param name="ChangeKinds">The controlled change-kind filters applied when supplied.</param>
    /// <param name="TotalDetailRecords">The total number of matching detail records before MCP response limiting.</param>
    /// <param name="Summaries">The per-domain summary counts returned by the diff service.</param>
    /// <param name="Details">The bounded detail records returned when requested.</param>
    /// <param name="HasChanges">A value indicating whether added, removed, or changed summary counts are present.</param>
    public sealed record ArchonMcpSnapshotDiffFacts(
        string CurrentSnapshotStableKey,
        string PreviousSnapshotStableKey,
        string ComparisonScope,
        bool UsedImpliedPreviousSnapshot,
        IReadOnlyList<string> Domains,
        IReadOnlyList<string> ChangeKinds,
        int TotalDetailRecords,
        IReadOnlyList<ArchonMcpSnapshotDiffSummaryRecord> Summaries,
        IReadOnlyList<ArchonMcpSnapshotDiffDetailRecord> Details,
        bool HasChanges);
}
