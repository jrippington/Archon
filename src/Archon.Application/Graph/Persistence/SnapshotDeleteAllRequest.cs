namespace Archon.Application.Graph.Persistence
{
    /// <summary>
    /// Represents a storage-neutral request to delete every persisted snapshot and every snapshot-scoped graph record.
    /// </summary>
    /// <param name="Confirmation">The validated confirmation phrase proving the caller intentionally requested global snapshot cleanup.</param>
    public sealed record SnapshotDeleteAllRequest(string Confirmation);
}
