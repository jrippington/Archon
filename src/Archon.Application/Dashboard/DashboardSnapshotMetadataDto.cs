namespace Archon.Application.Dashboard
{
    /// <summary>
    /// Describes the resolved snapshot used to calculate a dashboard summary.
    /// </summary>
    public sealed class DashboardSnapshotMetadataDto
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DashboardSnapshotMetadataDto"/> class.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the resolved snapshot.</param>
        /// <param name="selector">The selector value supplied by the caller or the implicit latest selector.</param>
        /// <param name="resolvedAsLatest">Indicates whether the service resolved the latest snapshot instead of an exact snapshot identity.</param>
        /// <param name="commitSha">The source-control commit SHA recorded on the snapshot when available.</param>
        /// <param name="startedUtc">The UTC time at which snapshot extraction started.</param>
        /// <param name="completedUtc">The UTC time at which snapshot extraction completed when available.</param>
        /// <param name="status">The persisted snapshot status.</param>
        public DashboardSnapshotMetadataDto(string? snapshotStableKey, string? selector, bool resolvedAsLatest, string? commitSha, DateTimeOffset startedUtc, DateTimeOffset? completedUtc, string? status)
        {
            // Snapshot metadata is the API-visible proof of which historical graph state produced the summary counts.
            SnapshotStableKey = snapshotStableKey ?? string.Empty;
            Selector = selector ?? DashboardSnapshotSelector.LatestSnapshotSelector;
            ResolvedAsLatest = resolvedAsLatest;
            CommitSha = commitSha;
            StartedUtc = startedUtc;
            CompletedUtc = completedUtc;
            Status = status ?? string.Empty;
        }

        /// <summary>
        /// Gets the stable key of the resolved snapshot.
        /// </summary>
        public string SnapshotStableKey { get; }

        /// <summary>
        /// Gets the selector value supplied by the caller or the implicit latest selector.
        /// </summary>
        public string Selector { get; }

        /// <summary>
        /// Gets a value indicating whether the service resolved the latest snapshot instead of an exact snapshot identity.
        /// </summary>
        public bool ResolvedAsLatest { get; }

        /// <summary>
        /// Gets the source-control commit SHA recorded on the snapshot when available.
        /// </summary>
        public string? CommitSha { get; }

        /// <summary>
        /// Gets the UTC time at which snapshot extraction started.
        /// </summary>
        public DateTimeOffset StartedUtc { get; }

        /// <summary>
        /// Gets the UTC time at which snapshot extraction completed when available.
        /// </summary>
        public DateTimeOffset? CompletedUtc { get; }

        /// <summary>
        /// Gets the persisted snapshot status.
        /// </summary>
        public string Status { get; }
    }
}