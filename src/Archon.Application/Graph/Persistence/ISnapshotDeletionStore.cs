namespace Archon.Application.Graph.Persistence
{
    /// <summary>
    /// Defines controlled snapshot deletion operations behind a storage-neutral application boundary.
    /// </summary>
    /// <remarks>
    /// Implementations delete only approved snapshot-scoped graph records and must not expose storage driver types, raw query text,
    /// internal graph identifiers, or arbitrary mutation controls to application or API callers.
    /// </remarks>
    public interface ISnapshotDeletionStore
    {
        /// <summary>
        /// Deletes one persisted snapshot and the records scoped to that snapshot stable key.
        /// </summary>
        /// <param name="request">The normalized delete-one request containing the public snapshot stable key.</param>
        /// <param name="cancellationToken">The token that cancels deletion before or during storage work.</param>
        /// <returns>A storage-neutral deletion result containing safe counts, warnings, and not-found state.</returns>
        Task<SnapshotDeletionResult> DeleteSnapshotAsync(SnapshotDeletionRequest request, CancellationToken cancellationToken);

        /// <summary>
        /// Deletes every persisted snapshot and all records whose lifecycle is scoped to any deleted snapshot.
        /// </summary>
        /// <param name="request">The normalized delete-all request containing the validated destructive confirmation phrase.</param>
        /// <param name="cancellationToken">The token that cancels deletion before or during storage work.</param>
        /// <returns>A storage-neutral aggregate deletion result containing safe counts and warnings.</returns>
        Task<SnapshotDeleteAllResult> DeleteAllSnapshotsAsync(SnapshotDeleteAllRequest request, CancellationToken cancellationToken);
    }
}
