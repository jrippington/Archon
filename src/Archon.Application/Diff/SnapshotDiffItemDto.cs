namespace Archon.Application.Diff
{
    /// <summary>
    /// Represents one public snapshot diff detail row for a node, edge, finding, or metric record.
    /// </summary>
    /// <param name="Domain">The controlled domain that owns the compared record.</param>
    /// <param name="ChangeKind">The classified change kind for the record.</param>
    /// <param name="StableKey">The stable public identity used to match records across snapshots.</param>
    /// <param name="DisplayName">The optional human-readable display name for the compared record.</param>
    /// <param name="Kind">The domain-specific kind, such as node kind, edge kind, rule code, or metric kind.</param>
    /// <param name="PreviousFingerprint">The previous snapshot fingerprint when the record existed previously.</param>
    /// <param name="CurrentFingerprint">The current snapshot fingerprint when the record exists currently.</param>
    /// <param name="ChangedFields">A deterministic summary of fields known to differ for changed records.</param>
    /// <param name="EvidenceStableKeys">Stable evidence identities that explain the compared record where available.</param>
    /// <param name="HasUnknownData">Indicates whether the compared record carries explicit unknown-state context.</param>
    /// <param name="UnknownReason">The optional unknown-state reason carried from the compared record.</param>
    public sealed record SnapshotDiffItemDto(
        string Domain,
        string ChangeKind,
        string StableKey,
        string? DisplayName,
        string Kind,
        string? PreviousFingerprint,
        string? CurrentFingerprint,
        IReadOnlyList<string> ChangedFields,
        IReadOnlyList<string> EvidenceStableKeys,
        bool HasUnknownData,
        string? UnknownReason);
}
