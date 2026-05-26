namespace Archon.Application.Management
{
    /// <summary>
    /// Captures a validated retention request for snapshot lifecycle state.
    /// </summary>
    /// <param name="RepositoryStableKey">The repository scope whose snapshots may be considered.</param>
    /// <param name="SolutionStableKey">The optional solution scope whose snapshots may be considered.</param>
    /// <param name="KeepLatest">The minimum number of latest snapshots to keep in scope.</param>
    /// <param name="DeleteBeforeUtc">The optional cutoff timestamp for deletion candidates.</param>
    /// <param name="DryRun">A value indicating whether candidates should be reported without removal.</param>
    /// <param name="RequestedBy">The optional actor identity used for audit metadata.</param>
    public sealed record RetentionRequest(
        string? RepositoryStableKey,
        string? SolutionStableKey,
        int? KeepLatest,
        DateTimeOffset? DeleteBeforeUtc,
        bool DryRun,
        string? RequestedBy);
}
