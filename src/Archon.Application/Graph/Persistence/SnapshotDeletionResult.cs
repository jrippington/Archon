namespace Archon.Application.Graph.Persistence
{
    /// <summary>
    /// Represents the storage-neutral result of deleting one persisted snapshot and its snapshot-scoped records.
    /// </summary>
    /// <param name="SnapshotStableKey">The public stable key targeted by the deletion request.</param>
    /// <param name="SnapshotDeleted">A value indicating whether the target snapshot node was deleted.</param>
    /// <param name="DeletedSnapshotCount">The number of snapshot header nodes deleted.</param>
    /// <param name="DeletedNodeCount">The number of snapshot-scoped data nodes deleted, excluding shared repository, solution, rule, and run records.</param>
    /// <param name="DeletedRelationshipCount">The number of relationships deleted where practical for the backing store.</param>
    /// <param name="AffectedRunCount">The number of preserved extraction runs that referenced the deleted snapshot.</param>
    /// <param name="Warnings">Credential-safe warnings about deletion completeness or count precision.</param>
    public sealed record SnapshotDeletionResult(
        string SnapshotStableKey,
        bool SnapshotDeleted,
        int DeletedSnapshotCount,
        int DeletedNodeCount,
        int DeletedRelationshipCount,
        int AffectedRunCount,
        IReadOnlyList<string> Warnings);
}
