using Archon.Domain.Graph.Metadata;

namespace Archon.Application.Metrics
{
    /// <summary>
    /// Represents a public application DTO for one persisted snapshot metric.
    /// </summary>
    /// <param name="SnapshotStableKey">The stable key of the snapshot that owns the metric.</param>
    /// <param name="StableKey">The deterministic stable key that identifies the metric.</param>
    /// <param name="MetricKind">The stable metric kind.</param>
    /// <param name="ScopeKind">The metric scope kind.</param>
    /// <param name="NodeStableKey">The optional node stable key targeted by the metric.</param>
    /// <param name="EdgeStableKey">The optional edge stable key targeted by the metric.</param>
    /// <param name="PrimaryEvidenceStableKey">The optional primary evidence stable key explaining the metric.</param>
    /// <param name="Name">The human-readable metric name.</param>
    /// <param name="NumericValue">The optional numeric metric value.</param>
    /// <param name="TextValue">The optional textual metric value.</param>
    /// <param name="Unit">The optional unit associated with the metric value.</param>
    /// <param name="Confidence">The metric confidence value.</param>
    /// <param name="HasUnknownData">A value indicating whether the metric has explicit unknown-state context.</param>
    /// <param name="UnknownReason">The optional reason explaining unknown or incomplete metric input.</param>
    /// <param name="Metadata">Credential-safe metric metadata.</param>
    /// <param name="Fingerprint">The deterministic metric fingerprint.</param>
    public sealed record MetricItemDto(
        string SnapshotStableKey,
        string StableKey,
        string MetricKind,
        string ScopeKind,
        string? NodeStableKey,
        string? EdgeStableKey,
        string? PrimaryEvidenceStableKey,
        string Name,
        decimal? NumericValue,
        string? TextValue,
        string? Unit,
        decimal Confidence,
        bool HasUnknownData,
        string? UnknownReason,
        GraphMetadata Metadata,
        string Fingerprint);
}
