namespace Archon.Application.Projects
{
    /// <summary>
    /// Describes the resolved snapshot that produced a project query response.
    /// </summary>
    /// <param name="SnapshotStableKey">The stable key of the resolved snapshot.</param>
    /// <param name="Selector">The selector supplied by the caller or defaulted by the query service.</param>
    /// <param name="ResolvedAsLatest">A value indicating whether latest/current resolution was used.</param>
    /// <param name="CommitSha">The source-control commit SHA recorded for the snapshot when available.</param>
    /// <param name="StartedUtc">The UTC time at which extraction started.</param>
    /// <param name="CompletedUtc">The UTC time at which extraction completed when available.</param>
    /// <param name="Status">The persisted snapshot status.</param>
    public sealed record ProjectSnapshotMetadataDto(string SnapshotStableKey, string Selector, bool ResolvedAsLatest, string? CommitSha, DateTimeOffset StartedUtc, DateTimeOffset? CompletedUtc, string Status)
    {
        // Snapshot metadata is echoed through the API envelope so callers can audit exactly which graph state was queried.
    }
}
