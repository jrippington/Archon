namespace Archon.Application.Graph.Persistence
{
    /// <summary>
    /// Represents the storage-neutral result of deleting every persisted snapshot and all snapshot-scoped graph records.
    /// </summary>
    /// <param name="DeletedSnapshotCount">The number of snapshot header nodes deleted by the cleanup operation.</param>
    /// <param name="DeletedNodeCount">The number of snapshot-scoped data nodes deleted, excluding preserved shared repository, solution, rule, and run records.</param>
    /// <param name="DeletedRelationshipCount">The number of relationships deleted where practical for the backing store.</param>
    /// <param name="AffectedRunCount">The number of preserved extraction runs that referenced deleted snapshots.</param>
    /// <param name="Warnings">Credential-safe warnings about deletion completeness, count precision, or preserved run-history semantics.</param>
    public sealed record SnapshotDeleteAllResult(
        int DeletedSnapshotCount,
        int DeletedNodeCount,
        int DeletedRelationshipCount,
        int AffectedRunCount,
        IReadOnlyList<string> Warnings);
}
