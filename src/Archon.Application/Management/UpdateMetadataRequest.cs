namespace Archon.Application.Management
{
    /// <summary>
    /// Captures a constrained metadata update for a repository, solution, or snapshot record.
    /// </summary>
    /// <param name="TargetKind">The supported target kind, such as repository, solution, or snapshot.</param>
    /// <param name="StableKey">The stable identity of the target record.</param>
    /// <param name="Metadata">The requested metadata overlay, limited to approved fields.</param>
    /// <param name="RequestedBy">The optional actor identity used for audit metadata.</param>
    public sealed record UpdateMetadataRequest(
        string? TargetKind,
        string? StableKey,
        IReadOnlyDictionary<string, string>? Metadata,
        string? RequestedBy);
}
