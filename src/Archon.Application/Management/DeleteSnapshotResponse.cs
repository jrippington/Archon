namespace Archon.Application.Management
{
    /// <summary>
    /// Represents the safe management response returned after deleting one persisted snapshot.
    /// </summary>
    /// <param name="SnapshotStableKey">The public stable key targeted by the deletion operation.</param>
    /// <param name="Deleted">A value indicating whether a matching snapshot was deleted.</param>
    /// <param name="DeletedSnapshotCount">The number of snapshot header records deleted.</param>
    /// <param name="DeletedNodeCount">The number of snapshot-scoped data nodes deleted, excluding preserved shared and run records.</param>
    /// <param name="DeletedRelationshipCount">The number of relationships deleted where practical for the backing store.</param>
    /// <param name="AffectedRunCount">The number of preserved extraction run records that referenced the deleted snapshot.</param>
    /// <param name="Warnings">Credential-safe warnings about deletion completeness or count precision.</param>
    /// <param name="Audit">The audit metadata created when the destructive operation was accepted.</param>
    public sealed record DeleteSnapshotResponse(
        string SnapshotStableKey,
        bool Deleted,
        int DeletedSnapshotCount,
        int DeletedNodeCount,
        int DeletedRelationshipCount,
        int AffectedRunCount,
        IReadOnlyList<string> Warnings,
        AuditMetadataResponse Audit);
}
