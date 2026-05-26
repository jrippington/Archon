namespace ArchonMcp.McpHotlist
{
    /// <summary>
    /// Represents one safe hotlist finding record returned by MCP.
    /// </summary>
    /// <param name="SnapshotStableKey">The snapshot stable key that scopes the finding.</param>
    /// <param name="StableKey">The snapshot-scoped finding stable key.</param>
    /// <param name="HistoryKey">The cross-snapshot finding history key.</param>
    /// <param name="RuleCode">The rule code that classified the finding.</param>
    /// <param name="RuleVersion">The rule version that classified the finding.</param>
    /// <param name="Title">The finding title.</param>
    /// <param name="Summary">The safe finding summary.</param>
    /// <param name="Severity">The finding severity.</param>
    /// <param name="Status">The finding lifecycle status.</param>
    /// <param name="Confidence">The normalized finding confidence value.</param>
    /// <param name="Category">The optional rule category.</param>
    /// <param name="FirstSeen">The first-seen timestamp when supplied by persisted history; otherwise <see langword="null" />.</param>
    /// <param name="LatestSeen">The latest-seen timestamp when supplied by persisted history; otherwise <see langword="null" />.</param>
    /// <param name="AffectedNodes">The safe affected-node references returned with the finding.</param>
    /// <param name="EvidenceStableKeys">The stable evidence identities that support the finding.</param>
    /// <param name="Metadata">The safe metadata values returned by MCP for this finding.</param>
    public sealed record ArchonMcpHotlistFindingRecord(
        string SnapshotStableKey,
        string StableKey,
        string HistoryKey,
        string RuleCode,
        string RuleVersion,
        string Title,
        string Summary,
        string Severity,
        string Status,
        decimal Confidence,
        string? Category,
        DateTimeOffset? FirstSeen,
        DateTimeOffset? LatestSeen,
        IReadOnlyList<ArchonMcpAffectedNodeFacts> AffectedNodes,
        IReadOnlyList<string> EvidenceStableKeys,
        IReadOnlyDictionary<string, string> Metadata);
}
