namespace Archon.Application.Management
{
    /// <summary>
    /// Represents one snapshot lifecycle row safe for operational callers.
    /// </summary>
    /// <param name="SnapshotStableKey">The stable snapshot identity.</param>
    /// <param name="RepositoryStableKey">The stable repository identity associated with the snapshot.</param>
    /// <param name="SolutionStableKey">The optional stable solution identity inferred for the snapshot.</param>
    /// <param name="Status">The safe lifecycle status text.</param>
    /// <param name="BranchName">The optional branch name recorded for the snapshot.</param>
    /// <param name="CommitSha">The optional source-control commit SHA recorded for the snapshot.</param>
    /// <param name="StartedUtc">The UTC timestamp when extraction started.</param>
    /// <param name="CompletedUtc">The optional UTC timestamp when extraction completed.</param>
    /// <param name="WarningCount">The number of snapshot warnings without expanding message content.</param>
    /// <param name="ErrorCount">The number of snapshot errors without expanding message content.</param>
    public sealed record SnapshotLifecycleItemResponse(
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
