namespace ArchonMcp.McpResources
{
    /// <summary>
    /// Represents one safe architecture hotspot record returned by MCP resources.
    /// </summary>
    /// <param name="SnapshotStableKey">The snapshot stable key that scopes the hotspot.</param>
    /// <param name="StableKey">The deterministic hotspot stable key.</param>
    /// <param name="Category">The hotspot category.</param>
    /// <param name="TargetStableKey">The stable key of the hotspot target.</param>
    /// <param name="TargetKind">The kind of target represented by the hotspot.</param>
    /// <param name="DisplayName">The optional developer-facing target display name.</param>
    /// <param name="Score">The score used for hotspot ranking.</param>
    /// <param name="Rank">The category-local deterministic rank.</param>
    /// <param name="ContributingMetricStableKeys">Metric stable keys that contributed to the hotspot.</param>
    /// <param name="ContributingFindingStableKeys">Finding stable keys that contributed to the hotspot.</param>
    /// <param name="EvidenceStableKeys">Evidence stable keys that explain hotspot contributors.</param>
    /// <param name="Confidence">The normalized confidence for the hotspot.</param>
    /// <param name="HasUnknownData">A value indicating whether the hotspot includes unknown-state context.</param>
    /// <param name="UnknownReason">The reason unknown data is present when available.</param>
    /// <param name="Fingerprint">The deterministic hotspot fingerprint.</param>
    public sealed record ArchonMcpHotspotRecord(
        string SnapshotStableKey,
        string StableKey,
        string Category,
        string TargetStableKey,
        string TargetKind,
        string? DisplayName,
        decimal Score,
        int Rank,
        IReadOnlyList<string> ContributingMetricStableKeys,
        IReadOnlyList<string> ContributingFindingStableKeys,
        IReadOnlyList<string> EvidenceStableKeys,
        decimal Confidence,
        bool HasUnknownData,
        string? UnknownReason,
        string Fingerprint);
}
