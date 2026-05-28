namespace Archon.Application.Graph.Persistence
{
    /// <summary>
    /// Represents a storage-neutral result for a bounded snapshot lifecycle query.
    /// </summary>
    /// <param name="Items">The lifecycle rows returned after approved filtering, ordering, and take-limit application.</param>
    /// <param name="TotalCount">The total number of rows matching the filters before the take limit was applied.</param>
    /// <param name="Take">The effective take limit used by the storage query.</param>
    /// <param name="Warnings">Credential-safe warnings about truncation or incomplete lifecycle data.</param>
    public sealed record SnapshotLifecycleQueryResult(
        IReadOnlyList<SnapshotLifecycleQueryRow> Items,
        int TotalCount,
        int Take,
        IReadOnlyList<string> Warnings);
}
