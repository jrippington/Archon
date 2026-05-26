namespace Archon.Application.Management
{
    /// <summary>
    /// Represents the safe result of a retention validation or execution operation.
    /// </summary>
    /// <param name="RepositoryStableKey">The repository scope evaluated by the retention request.</param>
    /// <param name="SolutionStableKey">The optional solution scope evaluated by the retention request.</param>
    /// <param name="KeepLatest">The effective latest-snapshot keep count.</param>
    /// <param name="DeleteBeforeUtc">The optional cutoff timestamp applied to deletion candidates.</param>
    /// <param name="DryRun">A value indicating whether candidates were reported without removal.</param>
    /// <param name="CandidateSnapshotStableKeys">The candidate snapshot identities within the intended lifecycle scope.</param>
    /// <param name="DeletedSnapshotStableKeys">The snapshot identities removed from management lifecycle state.</param>
    /// <param name="Warnings">The safe warnings explaining retention boundaries and no-op conditions.</param>
    /// <param name="Audit">The audit-ready metadata for the retention request.</param>
    public sealed record RetentionResponse(
        string RepositoryStableKey,
        string? SolutionStableKey,
        int KeepLatest,
        DateTimeOffset? DeleteBeforeUtc,
        bool DryRun,
        IReadOnlyList<string> CandidateSnapshotStableKeys,
        IReadOnlyList<string> DeletedSnapshotStableKeys,
        IReadOnlyList<string> Warnings,
        AuditMetadataResponse Audit);
}
