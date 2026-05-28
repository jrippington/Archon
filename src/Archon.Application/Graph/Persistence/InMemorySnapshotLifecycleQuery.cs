using Archon.Application.Extraction.Contracts;

namespace Archon.Application.Graph.Persistence
{
    /// <summary>
    /// Provides the process-local snapshot lifecycle query fallback used by lightweight hosts and focused tests.
    /// </summary>
    /// <remarks>
    /// The fallback adapts snapshots already written through <see cref="InMemoryArchitectureSnapshotWriter"/> into the same lifecycle
    /// query port used by durable infrastructure adapters. Production hosts that compose Neo4j replace this implementation through
    /// dependency injection, so management logic does not inspect the writer implementation directly.
    /// </remarks>
    public sealed class InMemorySnapshotLifecycleQuery : ISnapshotLifecycleQuery
    {
        /// <summary>
        /// Reads the process-local snapshots captured by the fallback writer.
        /// </summary>
        private readonly InMemoryArchitectureSnapshotWriter _snapshotWriter;

        /// <summary>
        /// Initializes a new instance of the <see cref="InMemorySnapshotLifecycleQuery"/> class.
        /// </summary>
        /// <param name="snapshotWriter">The fallback writer whose diagnostic snapshot list supplies local lifecycle rows.</param>
        public InMemorySnapshotLifecycleQuery(InMemoryArchitectureSnapshotWriter snapshotWriter)
        {
            // The lifecycle query depends on the concrete fallback writer only inside the fallback adapter, not in management services.
            _snapshotWriter = snapshotWriter ?? throw new ArgumentNullException(nameof(snapshotWriter));
        }

        /// <summary>
        /// Lists lifecycle rows from process-local snapshots using the same bounded query semantics as durable adapters.
        /// </summary>
        /// <param name="query">The normalized lifecycle query filters and take limit approved by the application service.</param>
        /// <param name="cancellationToken">The token that cancels the lifecycle read before rows are materialized.</param>
        /// <returns>A lifecycle result containing filtered, newest-first rows and safe truncation warnings.</returns>
        public Task<SnapshotLifecycleQueryResult> ListSnapshotsAsync(SnapshotLifecycleQueryRequest query, CancellationToken cancellationToken)
        {
            // The fallback is intentionally synchronous after cancellation because it reads a defensive copy from process memory.
            ArgumentNullException.ThrowIfNull(query);
            cancellationToken.ThrowIfCancellationRequested();

            SnapshotLifecycleQueryRow[] matchingRows = _snapshotWriter.GetSnapshotsSnapshotForDiagnostics()
                .Where(static snapshot => snapshot.SnapshotHeader is not null)
                .Select(MapSnapshot)
                .Where(row => Matches(row, query))
                .OrderByDescending(row => row.StartedUtc)
                .ThenBy(row => row.SnapshotStableKey, StringComparer.Ordinal)
                .ToArray();

            string[] warnings = matchingRows.Length > query.Take
                ? ["Snapshot lifecycle response was truncated by the take limit."]
                : [];
            SnapshotLifecycleQueryResult result = new(matchingRows.Take(query.Take).ToArray(), matchingRows.Length, query.Take, warnings);
            return Task.FromResult(result);
        }

        /// <summary>
        /// Maps one extracted snapshot into the storage-neutral lifecycle row shape.
        /// </summary>
        /// <param name="snapshot">The process-local snapshot captured by the fallback writer.</param>
        /// <returns>The lifecycle row projected from the snapshot header and first associated solution.</returns>
        private static SnapshotLifecycleQueryRow MapSnapshot(ExtractedArchitectureSnapshot snapshot)
        {
            // The in-memory writer stores full snapshots, but the management lifecycle view exposes only safe header-level metadata.
            string? solutionStableKey = snapshot.Solutions.FirstOrDefault()?.StableKey.Value;
            return new SnapshotLifecycleQueryRow(
                snapshot.SnapshotHeader!.StableKey.Value,
                snapshot.SnapshotHeader.RepositoryStableKey.Value,
                solutionStableKey,
                snapshot.SnapshotHeader.Status,
                snapshot.SnapshotHeader.BranchName,
                snapshot.SnapshotHeader.CommitSha,
                snapshot.SnapshotHeader.StartedUtc,
                snapshot.SnapshotHeader.CompletedUtc,
                snapshot.SnapshotHeader.Warnings.Count,
                snapshot.SnapshotHeader.Errors.Count);
        }

        /// <summary>
        /// Determines whether a lifecycle row satisfies all normalized optional filters.
        /// </summary>
        /// <param name="row">The lifecycle row being tested.</param>
        /// <param name="query">The normalized query filters.</param>
        /// <returns><see langword="true"/> when the row matches; otherwise <see langword="false"/>.</returns>
        private static bool Matches(SnapshotLifecycleQueryRow row, SnapshotLifecycleQueryRequest query)
        {
            // Each optional filter is exact and ordinal so process-local behavior matches graph-backed stable-key semantics.
            return MatchesText(row.RepositoryStableKey, query.RepositoryStableKey)
                && MatchesNullableText(row.SolutionStableKey, query.SolutionStableKey)
                && MatchesText(row.Status, query.Status)
                && MatchesNullableText(row.CommitSha, query.CommitSha)
                && (!query.FromUtc.HasValue || row.StartedUtc >= query.FromUtc.Value)
                && (!query.ToUtc.HasValue || row.StartedUtc <= query.ToUtc.Value);
        }

        /// <summary>
        /// Applies an optional ordinal text comparison to a non-null row value.
        /// </summary>
        /// <param name="value">The row value being tested.</param>
        /// <param name="filter">The optional normalized filter value.</param>
        /// <returns><see langword="true"/> when no filter exists or the value matches exactly.</returns>
        private static bool MatchesText(string value, string? filter)
        {
            // Empty filters are normalized by the management service before the adapter receives the query.
            return string.IsNullOrWhiteSpace(filter) || StringComparer.Ordinal.Equals(value, filter);
        }

        /// <summary>
        /// Applies an optional ordinal text comparison to a nullable row value.
        /// </summary>
        /// <param name="value">The nullable row value being tested.</param>
        /// <param name="filter">The optional normalized filter value.</param>
        /// <returns><see langword="true"/> when no filter exists or the nullable value matches exactly.</returns>
        private static bool MatchesNullableText(string? value, string? filter)
        {
            // Nullable values only match explicit filters when the row actually carries the value.
            return string.IsNullOrWhiteSpace(filter) || StringComparer.Ordinal.Equals(value, filter);
        }
    }
}
