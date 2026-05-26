namespace Archon.Application.Management
{
    /// <summary>
    /// Represents a bounded snapshot lifecycle query result.
    /// </summary>
    /// <param name="Items">The lifecycle rows returned after filtering and bounds were applied.</param>
    /// <param name="TotalCount">The total number of rows matching filters before the take bound.</param>
    /// <param name="Take">The effective result-size bound.</param>
    /// <param name="Warnings">The safe warnings explaining unavailable or truncated lifecycle data.</param>
    public sealed record SnapshotLifecycleResponse(
        IReadOnlyList<SnapshotLifecycleItemResponse> Items,
        int TotalCount,
        int Take,
        IReadOnlyList<string> Warnings);
}
