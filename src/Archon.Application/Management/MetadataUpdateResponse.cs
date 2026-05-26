namespace Archon.Application.Management
{
    /// <summary>
    /// Represents the accepted metadata overlay after allowlist validation.
    /// </summary>
    /// <param name="TargetKind">The normalized target kind whose metadata was updated.</param>
    /// <param name="StableKey">The stable identity of the target record.</param>
    /// <param name="Metadata">The approved metadata fields retained for the target.</param>
    /// <param name="Audit">The audit-ready metadata for the accepted metadata action.</param>
    public sealed record MetadataUpdateResponse(
        string TargetKind,
        string StableKey,
        IReadOnlyDictionary<string, string> Metadata,
        AuditMetadataResponse Audit);
}
