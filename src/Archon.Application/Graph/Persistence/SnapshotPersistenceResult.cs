namespace Archon.Application.Graph.Persistence
{
    /// <summary>
    /// Reports the outcome of snapshot persistence in application-owned terms.
    /// </summary>
    /// <remarks>
    /// The result preserves safe diagnostics and aggregate counts while avoiding Neo4j transaction summaries, internal IDs, or driver
    /// exceptions. This keeps callers aligned with the application persistence port instead of the infrastructure adapter.
    /// </remarks>
    public sealed record SnapshotPersistenceResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SnapshotPersistenceResult"/> record.
        /// </summary>
        /// <param name="succeeded">A value indicating whether snapshot persistence completed without fatal errors.</param>
        /// <param name="snapshotStableKey">The stable key of the snapshot being persisted when known.</param>
        /// <param name="counts">The persisted record and relationship counts.</param>
        /// <param name="warnings">The non-fatal warnings produced during persistence.</param>
        /// <param name="errors">The fatal errors produced during persistence.</param>
        public SnapshotPersistenceResult(
            bool succeeded,
            string? snapshotStableKey,
            SnapshotPersistenceCounts counts,
            IReadOnlyList<PersistenceWarning> warnings,
            IReadOnlyList<PersistenceError> errors)
        {
            // The constructor copies diagnostics so a completed result cannot be mutated through caller-owned collections.
            ArgumentNullException.ThrowIfNull(counts);
            Succeeded = succeeded;
            SnapshotStableKey = string.IsNullOrWhiteSpace(snapshotStableKey) ? null : snapshotStableKey.Trim();
            Counts = counts;
            Warnings = warnings.ToArray();
            Errors = errors.ToArray();
        }

        /// <summary>
        /// Gets a value indicating whether snapshot persistence completed without fatal errors.
        /// </summary>
        public bool Succeeded { get; }

        /// <summary>
        /// Gets the stable key of the snapshot being persisted when known.
        /// </summary>
        public string? SnapshotStableKey { get; }

        /// <summary>
        /// Gets persisted record and relationship counts.
        /// </summary>
        public SnapshotPersistenceCounts Counts { get; }

        /// <summary>
        /// Gets the non-fatal warnings produced during persistence.
        /// </summary>
        public IReadOnlyList<PersistenceWarning> Warnings { get; }

        /// <summary>
        /// Gets the fatal errors produced during persistence.
        /// </summary>
        public IReadOnlyList<PersistenceError> Errors { get; }

        /// <summary>
        /// Creates a successful snapshot persistence result.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that was persisted.</param>
        /// <param name="counts">The persisted record and relationship counts.</param>
        /// <param name="warnings">The optional non-fatal warnings produced during persistence.</param>
        /// <returns>A successful snapshot persistence result.</returns>
        public static SnapshotPersistenceResult Success(string snapshotStableKey, SnapshotPersistenceCounts counts, IReadOnlyList<PersistenceWarning>? warnings = null)
        {
            // Successful persistence has no fatal errors, but may carry warnings such as intentionally ignored out-of-scope sections.
            return new SnapshotPersistenceResult(true, snapshotStableKey, counts, warnings ?? Array.Empty<PersistenceWarning>(), Array.Empty<PersistenceError>());
        }

        /// <summary>
        /// Creates a failed snapshot persistence result.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot being persisted when known.</param>
        /// <param name="error">The fatal error that caused persistence to fail.</param>
        /// <param name="warnings">The optional non-fatal warnings produced before the failure occurred.</param>
        /// <returns>A failed snapshot persistence result.</returns>
        public static SnapshotPersistenceResult Failure(string? snapshotStableKey, PersistenceError error, IReadOnlyList<PersistenceWarning>? warnings = null)
        {
            // Failed persistence uses empty counts because the caller must not treat partial transaction work as completed output.
            return new SnapshotPersistenceResult(false, snapshotStableKey, SnapshotPersistenceCounts.Empty, warnings ?? Array.Empty<PersistenceWarning>(), new[] { error });
        }
    }
}
