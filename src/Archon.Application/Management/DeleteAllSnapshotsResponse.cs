namespace Archon.Application.Management
{
    /// <summary>
    /// Represents the safe management response returned after deleting every persisted snapshot.
    /// </summary>
    /// <param name="DeletedSnapshotCount">The number of snapshot header records deleted.</param>
    /// <param name="DeletedNodeCount">The number of snapshot-scoped data nodes deleted, excluding preserved shared and run records.</param>
    /// <param name="DeletedRelationshipCount">The number of relationships deleted where practical for the backing store.</param>
    /// <param name="AffectedRunCount">The number of preserved extraction run records that referenced deleted snapshots.</param>
    /// <param name="Warnings">Credential-safe warnings about deletion completeness, count precision, or preserved run-history semantics.</param>
    /// <param name="Audit">The audit metadata created when the destructive operation was accepted.</param>
    public sealed record DeleteAllSnapshotsResponse(
        int DeletedSnapshotCount,
        int DeletedNodeCount,
        int DeletedRelationshipCount,
        int AffectedRunCount,
        IReadOnlyList<string> Warnings,
        AuditMetadataResponse Audit);
}
