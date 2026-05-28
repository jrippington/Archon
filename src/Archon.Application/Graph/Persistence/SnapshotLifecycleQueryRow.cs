namespace Archon.Application.Graph.Persistence
{
    /// <summary>
    /// Represents one persisted snapshot lifecycle row returned by a storage adapter.
    /// </summary>
    /// <param name="SnapshotStableKey">The public stable key of the snapshot.</param>
    /// <param name="RepositoryStableKey">The public stable key of the repository associated with the snapshot.</param>
    /// <param name="SolutionStableKey">The optional public stable key of a solution associated with the snapshot.</param>
    /// <param name="Status">The safe lifecycle status text recorded for the snapshot.</param>
    /// <param name="BranchName">The optional source-control branch recorded for the snapshot.</param>
    /// <param name="CommitSha">The optional source-control commit SHA recorded for the snapshot.</param>
    /// <param name="StartedUtc">The UTC timestamp when snapshot extraction started.</param>
    /// <param name="CompletedUtc">The optional UTC timestamp when snapshot extraction completed.</param>
    /// <param name="WarningCount">The number of warning diagnostics associated with the snapshot header.</param>
    /// <param name="ErrorCount">The number of error diagnostics associated with the snapshot header.</param>
    public sealed record SnapshotLifecycleQueryRow(
        string SnapshotStableKey,
        string RepositoryStableKey,
        string? SolutionStableKey,
        string Status,
        string? BranchName,
        string? CommitSha,
        DateTimeOffset StartedUtc,
        DateTimeOffset? CompletedUtc,
        int WarningCount,
        int ErrorCount);
}
