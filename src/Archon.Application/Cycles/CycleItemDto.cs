using Archon.Domain.Graph.Metadata;

namespace Archon.Application.Cycles
{
    /// <summary>
    /// Represents a public application DTO for one detected dependency cycle.
    /// </summary>
    /// <param name="SnapshotStableKey">The stable key of the snapshot that owns the cycle.</param>
    /// <param name="StableKey">The deterministic stable key that identifies the canonical cycle.</param>
    /// <param name="NodeStableKeys">The cycle node path in order, including the repeated first node as the final item.</param>
    /// <param name="EdgeStableKeys">The stable edge keys in cycle path order.</param>
    /// <param name="EvidenceStableKeys">The stable evidence keys contributed by cycle edges.</param>
    /// <param name="Confidence">The confidence value assigned to the cycle.</param>
    /// <param name="HasUnknownData">A value indicating whether cycle detection has explicit unknown-state context.</param>
    /// <param name="UnknownReason">The optional reason explaining incomplete cycle detection data.</param>
    /// <param name="Truncated">A value indicating whether result limits truncated the cycle result set.</param>
    /// <param name="Metadata">Credential-safe cycle metadata.</param>
    /// <param name="Fingerprint">The deterministic cycle fingerprint.</param>
    public sealed record CycleItemDto(
        string SnapshotStableKey,
        string StableKey,
        IReadOnlyList<string> NodeStableKeys,
        IReadOnlyList<string> EdgeStableKeys,
        IReadOnlyList<string> EvidenceStableKeys,
        decimal Confidence,
        bool HasUnknownData,
        string? UnknownReason,
        bool Truncated,
        GraphMetadata Metadata,
        string Fingerprint);
}
