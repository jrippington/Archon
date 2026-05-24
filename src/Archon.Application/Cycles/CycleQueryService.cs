using Archon.Application.Graph.Persistence;
using Archon.Application.Rules;

namespace Archon.Application.Cycles
{
    /// <summary>
    /// Implements controlled dependency cycle query behavior over persisted architecture snapshots.
    /// </summary>
    public sealed class CycleQueryService : ICycleQueryService
    {
        /// <summary>
        /// Reads snapshots from the registered architecture snapshot writer when in-memory diagnostics are available.
        /// </summary>
        private readonly IArchitectureSnapshotWriter _snapshotWriter;

        /// <summary>
        /// Detects and canonicalizes dependency cycles from extracted snapshot graph facts.
        /// </summary>
        private readonly DependencyCycleDetector _detector;

        /// <summary>
        /// Initializes a new instance of the <see cref="CycleQueryService"/> class.
        /// </summary>
        /// <param name="snapshotWriter">The snapshot writer that may expose in-memory snapshots for local query behavior.</param>
        public CycleQueryService(IArchitectureSnapshotWriter snapshotWriter)
        {
            // Query API default composition uses the in-memory writer, while future infrastructure adapters can replace this service or writer.
            _snapshotWriter = snapshotWriter ?? throw new ArgumentNullException(nameof(snapshotWriter));
            _detector = new DependencyCycleDetector();
        }

        /// <summary>
        /// Lists detected dependency cycles using controlled snapshot, node, and paging filters.
        /// </summary>
        /// <param name="query">The controlled cycle query contract.</param>
        /// <param name="cancellationToken">The cancellation token that can stop retrieval before graph work starts.</param>
        /// <returns>A bounded page of stable cycle DTOs.</returns>
        public Task<PagedQueryResult<CycleItemDto>> ListCyclesAsync(CycleQuery query, CancellationToken cancellationToken)
        {
            // The service computes cycles from persisted snapshot graph facts and then applies fixed filters; callers never provide traversal logic.
            ArgumentNullException.ThrowIfNull(query);
            cancellationToken.ThrowIfCancellationRequested();
            Archon.Application.Extraction.Contracts.ExtractedArchitectureSnapshot? snapshot = _snapshotWriter is InMemoryArchitectureSnapshotWriter writer
                ? writer.GetSnapshotsSnapshotForDiagnostics().FirstOrDefault(snapshot => StringComparer.Ordinal.Equals(snapshot.SnapshotHeader?.StableKey.Value, query.SnapshotStableKey))
                : null;
            if (snapshot is null)
            {
                PagedQueryResult<CycleItemDto> empty = new([], totalCount: 0, query.Skip, query.Take);
                return Task.FromResult(empty);
            }

            CycleDetectionResult detection = _detector.DetectCycles(snapshot, DependencyCycleDetector.DefaultMaxDepth, resultLimit: 500);
            CycleRecord[] matches = detection.Cycles
                .Where(cycle => query.NodeStableKey is null || cycle.NodeStableKeys.Take(cycle.NodeStableKeys.Count - 1).Any(node => StringComparer.Ordinal.Equals(node.Value, query.NodeStableKey)))
                .OrderBy(static cycle => cycle.NodeStableKeys[0].Value, StringComparer.Ordinal)
                .ThenBy(static cycle => string.Join("|", cycle.NodeStableKeys.Select(static stableKey => stableKey.Value)), StringComparer.Ordinal)
                .ThenBy(static cycle => cycle.StableKey.Value, StringComparer.Ordinal)
                .ToArray();
            CycleItemDto[] items = matches
                .Skip(query.Skip)
                .Take(query.Take)
                .Select(ToCycleItem)
                .ToArray();
            PagedQueryResult<CycleItemDto> result = new(items, matches.Length, query.Skip, query.Take);
            return Task.FromResult(result);
        }

        /// <summary>
        /// Maps a cycle record to a public cycle item DTO.
        /// </summary>
        /// <param name="cycle">The detected cycle record.</param>
        /// <returns>The stable public cycle DTO.</returns>
        private static CycleItemDto ToCycleItem(CycleRecord cycle)
        {
            // Public cycle responses expose stable identities and sanitized metadata but never database-local identifiers.
            return new CycleItemDto(
                cycle.SnapshotStableKey.Value,
                cycle.StableKey.Value,
                cycle.NodeStableKeys.Select(static stableKey => stableKey.Value).ToArray(),
                cycle.EdgeStableKeys.Select(static stableKey => stableKey.Value).ToArray(),
                cycle.EvidenceStableKeys.Select(static stableKey => stableKey.Value).ToArray(),
                cycle.Confidence.Value,
                cycle.UnknownState.HasUnknownData,
                cycle.UnknownState.UnknownReason,
                cycle.Truncated,
                PublicMetadataSanitizer.Sanitize(cycle.Metadata),
                cycle.Fingerprint.Value);
        }
    }
}
