using Archon.Application.Extraction.Contracts;
using Archon.Application.Graph.Persistence;
using Archon.Application.Rules;
using Archon.Domain.Graph.Model;

namespace Archon.Application.Metrics
{
    /// <summary>
    /// Provides controlled in-memory query behavior for snapshot metric API tests and default composition.
    /// </summary>
    public sealed class InMemoryMetricQueryStore : IMetricQueryStore
    {
        /// <summary>
        /// Reads snapshots from the in-memory fallback writer when available.
        /// </summary>
        private readonly IArchitectureSnapshotWriter _snapshotWriter;

        /// <summary>
        /// Initializes a new instance of the <see cref="InMemoryMetricQueryStore"/> class.
        /// </summary>
        /// <param name="snapshotWriter">The snapshot writer that may expose in-memory snapshots for local query behavior.</param>
        public InMemoryMetricQueryStore(IArchitectureSnapshotWriter snapshotWriter)
        {
            // The store composes the existing fallback writer so tests can query metrics without Neo4j.
            _snapshotWriter = snapshotWriter ?? throw new ArgumentNullException(nameof(snapshotWriter));
        }

        /// <summary>
        /// Retrieves persisted metrics matching the supplied controlled snapshot metric query.
        /// </summary>
        /// <param name="query">The controlled filter and paging contract for the metric query.</param>
        /// <param name="cancellationToken">The cancellation token that can stop retrieval before store work starts.</param>
        /// <returns>A bounded page of persisted metric records.</returns>
        public Task<PagedQueryResult<MetricRecord>> QueryMetricsAsync(MetricQuery query, CancellationToken cancellationToken)
        {
            // In-memory querying is available only when the fallback writer exposes diagnostic snapshots in this process.
            ArgumentNullException.ThrowIfNull(query);
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<MetricRecord> source = _snapshotWriter is InMemoryArchitectureSnapshotWriter writer
                ? writer.GetSnapshotsSnapshotForDiagnostics().SelectMany(static snapshot => snapshot.Metrics).ToArray()
                : [];
            MetricRecord[] matches = source
                .Where(metric => StringComparer.Ordinal.Equals(metric.SnapshotStableKey.Value, query.SnapshotStableKey))
                .Where(metric => query.MetricKind is null || StringComparer.Ordinal.Equals(metric.MetricKind, query.MetricKind))
                .Where(metric => query.ScopeKind is null || StringComparer.Ordinal.Equals(metric.ScopeKind.Value, query.ScopeKind))
                .Where(metric => query.ProjectStableKey is null || StringComparer.Ordinal.Equals(metric.NodeStableKey?.Value, query.ProjectStableKey))
                .OrderBy(static metric => metric.MetricKind, StringComparer.Ordinal)
                .ThenBy(static metric => metric.ScopeKind.Value, StringComparer.Ordinal)
                .ThenBy(static metric => metric.NodeStableKey?.Value ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(static metric => metric.StableKey.Value, StringComparer.Ordinal)
                .ToArray();
            PagedQueryResult<MetricRecord> result = new(matches.Skip(query.Skip).Take(query.Take), matches.Length, query.Skip, query.Take);
            return Task.FromResult(result);
        }
    }
}
