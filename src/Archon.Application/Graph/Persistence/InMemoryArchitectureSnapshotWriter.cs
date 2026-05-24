using Archon.Application.Extraction.Contracts;

namespace Archon.Application.Graph.Persistence
{
    /// <summary>
    /// Provides a local in-memory snapshot writer used when no infrastructure persistence adapter has been registered.
    /// </summary>
    /// <remarks>
    /// The writer lets API and application tests exercise the asynchronous orchestration contract without requiring Neo4j credentials.
    /// Host composition that registers Neo4j replaces this fallback with the real infrastructure adapter.
    /// </remarks>
    public sealed class InMemoryArchitectureSnapshotWriter : IArchitectureSnapshotWriter
    {
        /// <summary>
        /// Serializes access to the in-memory snapshot list.
        /// </summary>
        private readonly object _syncRoot = new();

        /// <summary>
        /// Stores snapshots written during the current process lifetime.
        /// </summary>
        private readonly List<ExtractedArchitectureSnapshot> _snapshots = [];

        /// <summary>
        /// Gets a deterministic copy of snapshots written during the current process for in-memory query adapters and tests.
        /// </summary>
        /// <returns>A read-only snapshot of in-memory persisted architecture snapshots.</returns>
        public IReadOnlyList<ExtractedArchitectureSnapshot> GetSnapshotsSnapshotForDiagnostics()
        {
            // The copy prevents query callers from mutating the writer's internal process-local persistence list.
            lock (_syncRoot)
            {
                return _snapshots.ToArray();
            }
        }

        /// <summary>
        /// Persists one assembled snapshot into process memory and reports a successful application-owned result.
        /// </summary>
        /// <param name="snapshot">The assembled architecture snapshot to persist.</param>
        /// <param name="cancellationToken">A token that cancels the in-memory write before it is recorded.</param>
        /// <returns>A successful persistence result containing the snapshot stable key and section counts.</returns>
        public Task<SnapshotPersistenceResult> WriteSnapshotAsync(ExtractedArchitectureSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            // The fallback writer records the exact generalized contract while avoiding infrastructure dependencies in focused tests.
            ArgumentNullException.ThrowIfNull(snapshot);
            cancellationToken.ThrowIfCancellationRequested();

            lock (_syncRoot)
            {
                _snapshots.Add(snapshot);
            }

            string snapshotStableKey = snapshot.SnapshotHeader?.StableKey.Value ?? $"in-memory-snapshot:{Guid.NewGuid():N}";
            SnapshotPersistenceCounts counts = new(
                snapshot.Repositories.Count,
                snapshot.Solutions.Count,
                snapshot.SnapshotHeader is null ? 0 : 1,
                snapshot.Nodes.Count,
                snapshot.Evidence.Count,
                snapshot.Edges.Count,
                snapshot.Solutions.Count,
                0,
                snapshot.Edges.Count * 2,
                0,
                snapshot.Rules.Count,
                snapshot.Findings.Count,
                0,
                0,
                0,
                snapshot.Metrics.Count,
                0,
                0,
                snapshot.GeneratedSummaries.Count,
                snapshot.GeneratedSummaries.Count,
                0);
            return Task.FromResult(SnapshotPersistenceResult.Success(snapshotStableKey, counts));
        }
    }
}
