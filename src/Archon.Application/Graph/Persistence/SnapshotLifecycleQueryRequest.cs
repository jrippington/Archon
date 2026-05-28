namespace Archon.Application.Graph.Persistence
{
    /// <summary>
    /// Captures normalized, storage-neutral filters for a persisted snapshot lifecycle listing operation.
    /// </summary>
    /// <param name="RepositoryStableKey">The optional repository stable key that limits rows to one repository.</param>
    /// <param name="SolutionStableKey">The optional solution stable key that limits rows to snapshots associated with one solution.</param>
    /// <param name="Status">The optional lifecycle status filter, such as <c>Completed</c> or <c>Failed</c>.</param>
    /// <param name="FromUtc">The optional inclusive lower bound for snapshot start timestamps.</param>
    /// <param name="ToUtc">The optional inclusive upper bound for snapshot start timestamps.</param>
    /// <param name="CommitSha">The optional source-control commit SHA filter.</param>
    /// <param name="Take">The validated maximum number of rows the store may return.</param>
    public sealed record SnapshotLifecycleQueryRequest(
        string? RepositoryStableKey,
        string? SolutionStableKey,
        string? Status,
        DateTimeOffset? FromUtc,
        DateTimeOffset? ToUtc,
        string? CommitSha,
        int Take);
}
