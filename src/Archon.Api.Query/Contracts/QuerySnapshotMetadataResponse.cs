namespace Archon.Api.Query.Contracts
{
    /// <summary>
    /// Describes the snapshot that produced a query API response.
    /// </summary>
    public sealed record QuerySnapshotMetadataResponse
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="QuerySnapshotMetadataResponse"/> record.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the resolved snapshot.</param>
        /// <param name="selector">The selector value supplied by the caller or inferred by the endpoint.</param>
        /// <param name="resolvedAsLatest">Indicates whether latest/current resolution was used.</param>
        /// <param name="commitSha">The source-control commit SHA recorded for the snapshot when available.</param>
        /// <param name="startedUtc">The UTC time at which extraction started for the snapshot.</param>
        /// <param name="completedUtc">The UTC time at which extraction completed when available.</param>
        /// <param name="status">The persisted snapshot status.</param>
        public QuerySnapshotMetadataResponse(string snapshotStableKey, string selector, bool resolvedAsLatest, string? commitSha, DateTimeOffset startedUtc, DateTimeOffset? completedUtc, string status)
        {
            // Snapshot metadata prevents clients from confusing an implicit latest result with an exact historical snapshot selection.
            SnapshotStableKey = snapshotStableKey;
            Selector = selector;
            ResolvedAsLatest = resolvedAsLatest;
            CommitSha = commitSha;
            StartedUtc = startedUtc;
            CompletedUtc = completedUtc;
            Status = status;
        }

        /// <summary>
        /// Gets the stable key of the resolved snapshot.
        /// </summary>
        public string SnapshotStableKey { get; init; }

        /// <summary>
        /// Gets the selector value supplied by the caller or inferred by the endpoint.
        /// </summary>
        public string Selector { get; init; }

        /// <summary>
        /// Gets a value indicating whether latest/current resolution was used.
        /// </summary>
        public bool ResolvedAsLatest { get; init; }

        /// <summary>
        /// Gets the source-control commit SHA recorded for the snapshot when available.
        /// </summary>
        public string? CommitSha { get; init; }

        /// <summary>
        /// Gets the UTC time at which extraction started for the snapshot.
        /// </summary>
        public DateTimeOffset StartedUtc { get; init; }

        /// <summary>
        /// Gets the UTC time at which extraction completed when available.
        /// </summary>
        public DateTimeOffset? CompletedUtc { get; init; }

        /// <summary>
        /// Gets the persisted snapshot status.
        /// </summary>
        public string Status { get; init; }
    }
}