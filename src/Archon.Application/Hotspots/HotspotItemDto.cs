using Archon.Domain.Graph.Metadata;

namespace Archon.Application.Hotspots
{
    /// <summary>
    /// Describes one public hotspot item returned by controlled query APIs.
    /// </summary>
    /// <param name="SnapshotStableKey">The stable key of the snapshot that scopes the hotspot.</param>
    /// <param name="StableKey">The deterministic hotspot stable key.</param>
    /// <param name="Category">The stable hotspot category.</param>
    /// <param name="TargetStableKey">The stable key of the hotspot target.</param>
    /// <param name="TargetKind">The target kind such as Project, Node, or Snapshot.</param>
    /// <param name="DisplayName">The optional developer-facing target display name.</param>
    /// <param name="Score">The numeric score used for ranking.</param>
    /// <param name="Rank">The deterministic category-local rank.</param>
    /// <param name="ContributingMetricStableKeys">The metric stable keys that contributed to the hotspot.</param>
    /// <param name="ContributingFindingStableKeys">The finding stable keys that contributed to the hotspot.</param>
    /// <param name="EvidenceStableKeys">The evidence stable keys that explain contributing facts.</param>
    /// <param name="Confidence">The composed hotspot confidence.</param>
    /// <param name="HasUnknownData">A value indicating whether contributing data includes unknown-state context.</param>
    /// <param name="UnknownReason">The reason unknown data is present when available.</param>
    /// <param name="Metadata">The sanitized deterministic hotspot metadata.</param>
    /// <param name="Fingerprint">The deterministic hotspot fingerprint.</param>
    public sealed record HotspotItemDto(
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
        GraphMetadata Metadata,
        string Fingerprint);
}
