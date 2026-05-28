namespace Archon.Application.Graph.Persistence
{
    /// <summary>
    /// Provides the process-local snapshot deletion fallback used by lightweight hosts and focused management API tests.
    /// </summary>
    /// <remarks>
    /// The fallback removes snapshots captured by <see cref="InMemoryArchitectureSnapshotWriter"/> and reports conservative counts.
    /// Production hosts that compose Neo4j replace this adapter with durable graph-backed deletion storage.
    /// </remarks>
    public sealed class InMemorySnapshotDeletionStore : ISnapshotDeletionStore
    {
        /// <summary>
        /// Reads and mutates the process-local snapshots captured by the fallback writer.
        /// </summary>
        private readonly InMemoryArchitectureSnapshotWriter _snapshotWriter;

        /// <summary>
        /// Initializes a new instance of the <see cref="InMemorySnapshotDeletionStore"/> class.
        /// </summary>
        /// <param name="snapshotWriter">The fallback writer whose process-local snapshot list is modified by delete-one operations.</param>
        public InMemorySnapshotDeletionStore(InMemoryArchitectureSnapshotWriter snapshotWriter)
        {
            // The fallback stores only the concrete in-memory writer because no infrastructure adapter is available in local test composition.
            _snapshotWriter = snapshotWriter ?? throw new ArgumentNullException(nameof(snapshotWriter));
        }

        /// <summary>
        /// Deletes one process-local snapshot and reports safe approximate counts for management responses.
        /// </summary>
        /// <param name="request">The normalized delete-one request containing the snapshot stable key.</param>
        /// <param name="cancellationToken">The token that cancels deletion before the in-memory list is inspected.</param>
        /// <returns>A deletion result representing either one removed fallback snapshot or a not-found outcome.</returns>
        public Task<SnapshotDeletionResult> DeleteSnapshotAsync(SnapshotDeletionRequest request, CancellationToken cancellationToken)
        {
            // The fallback has no relationship graph, so relationship counts remain zero and a warning explains the local-only precision.
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            bool removed = _snapshotWriter.TryRemoveSnapshotForDiagnostics(request.SnapshotStableKey);
            IReadOnlyList<string> warnings = removed
                ? ["Local fallback deletion reports snapshot count only; durable graph node and relationship counts require configured graph storage."]
                : [];
            SnapshotDeletionResult result = new(
                request.SnapshotStableKey,
                removed,
                removed ? 1 : 0,
                DeletedNodeCount: 0,
                DeletedRelationshipCount: 0,
                AffectedRunCount: 0,
                warnings);
            return Task.FromResult(result);
        }

        /// <summary>
        /// Deletes every process-local snapshot and reports safe approximate counts for management responses.
        /// </summary>
        /// <param name="request">The normalized delete-all request containing the already validated confirmation phrase.</param>
        /// <param name="cancellationToken">The token that cancels deletion before the in-memory list is inspected.</param>
        /// <returns>An aggregate deletion result representing removed fallback snapshots.</returns>
        public Task<SnapshotDeleteAllResult> DeleteAllSnapshotsAsync(SnapshotDeleteAllRequest request, CancellationToken cancellationToken)
        {
            // The fallback cannot count graph relationships because it only owns process-local snapshot headers and snapshot payloads.
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();

            int removed = _snapshotWriter.ClearSnapshotsForDiagnostics();
            IReadOnlyList<string> warnings = removed > 0
                ? ["Local fallback delete-all reports snapshot count only; durable graph node and relationship counts require configured graph storage."]
                : [];
            SnapshotDeleteAllResult result = new(
                removed,
                DeletedNodeCount: 0,
                DeletedRelationshipCount: 0,
                AffectedRunCount: 0,
                warnings);
            return Task.FromResult(result);
        }
    }
}
