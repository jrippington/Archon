namespace ArchonMcp.McpSnapshotDiff
{
    /// <summary>
    /// Represents one safe snapshot diff detail row returned by MCP.
    /// </summary>
    /// <param name="Domain">The controlled diff domain that owns the compared record.</param>
    /// <param name="ChangeKind">The classified change kind for the record.</param>
    /// <param name="StableKey">The stable public identity used to match records across snapshots.</param>
    /// <param name="DisplayName">The optional human-readable display name for the compared record.</param>
    /// <param name="Kind">The domain-specific kind such as node kind, edge kind, rule code, or metric kind.</param>
    /// <param name="ProjectStableKey">The owning or related project stable key when known.</param>
    /// <param name="TargetStableKeys">Stable target identities associated with the row.</param>
    /// <param name="Severity">The finding severity when the compared row is a finding.</param>
    /// <param name="PreviousFingerprint">The previous snapshot fingerprint when the record existed previously.</param>
    /// <param name="CurrentFingerprint">The current snapshot fingerprint when the record exists currently.</param>
    /// <param name="ChangedFields">A deterministic summary of fields known to differ for changed records.</param>
    /// <param name="EvidenceStableKeys">Stable evidence identities that explain the compared record where available.</param>
    /// <param name="HasUnknownData">A value indicating whether the compared row carries explicit unknown-state context.</param>
    /// <param name="UnknownReason">The optional unknown-state reason carried from the compared record.</param>
    public sealed record ArchonMcpSnapshotDiffDetailRecord(
        string Domain,
        string ChangeKind,
        string StableKey,
        string? DisplayName,
        string Kind,
        string? ProjectStableKey,
        IReadOnlyList<string> TargetStableKeys,
        string? Severity,
        string? PreviousFingerprint,
        string? CurrentFingerprint,
        IReadOnlyList<string> ChangedFields,
        IReadOnlyList<string> EvidenceStableKeys,
        bool HasUnknownData,
        string? UnknownReason);
}
