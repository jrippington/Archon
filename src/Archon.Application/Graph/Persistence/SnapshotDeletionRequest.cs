namespace Archon.Application.Graph.Persistence
{
    /// <summary>
    /// Represents a storage-neutral request to delete one persisted snapshot by public stable key.
    /// </summary>
    /// <param name="SnapshotStableKey">The validated public stable key that identifies the snapshot to delete.</param>
    public sealed record SnapshotDeletionRequest(string SnapshotStableKey);
}
