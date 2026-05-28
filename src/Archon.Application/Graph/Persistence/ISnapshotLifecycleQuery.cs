namespace Archon.Application.Graph.Persistence
{
    /// <summary>
    /// Defines the application-layer port for reading persisted snapshot lifecycle rows through controlled filters.
    /// </summary>
    /// <remarks>
    /// Implementations may read from Neo4j, process-local fallback state, or another infrastructure adapter, but they must return
    /// application-owned lifecycle contracts and must not expose database driver records, raw Cypher, internal node identifiers, or
    /// implementation-specific exception details to application or API callers.
    /// </remarks>
    public interface ISnapshotLifecycleQuery
    {
        /// <summary>
        /// Lists snapshot lifecycle rows using storage-owned filtering, deterministic ordering, and a bounded take limit.
        /// </summary>
        /// <param name="query">The normalized lifecycle query filters and take limit approved by the application service.</param>
        /// <param name="cancellationToken">The token that cancels the lifecycle read before or during asynchronous storage operations.</param>
        /// <returns>A lifecycle result containing the bounded rows, total matching count, effective take limit, and safe warnings.</returns>
        Task<SnapshotLifecycleQueryResult> ListSnapshotsAsync(SnapshotLifecycleQueryRequest query, CancellationToken cancellationToken);
    }
}
