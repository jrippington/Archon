namespace Archon.Application.Management
{
    /// <summary>
    /// Represents a management request to delete one persisted snapshot by public stable key.
    /// </summary>
    /// <param name="SnapshotStableKey">The public stable key of the snapshot to delete.</param>
    /// <param name="RequestedBy">The optional actor identity recorded in audit metadata for the destructive operation.</param>
    public sealed record DeleteSnapshotRequest(string? SnapshotStableKey, string? RequestedBy);
}
