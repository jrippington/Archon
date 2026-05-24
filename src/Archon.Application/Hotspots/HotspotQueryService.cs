using Archon.Application.Extraction.Contracts;
using Archon.Application.Graph.Persistence;
using Archon.Application.Rules;

namespace Archon.Application.Hotspots
{
    /// <summary>
    /// Implements controlled hotspot query behavior over persisted architecture snapshots.
    /// </summary>
    public sealed class HotspotQueryService : IHotspotQueryService
    {
        /// <summary>
        /// Reads snapshots from the registered architecture snapshot writer when in-memory diagnostics are available.
        /// </summary>
        private readonly IArchitectureSnapshotWriter _snapshotWriter;

        /// <summary>
        /// Detects and ranks hotspot records from snapshot facts.
        /// </summary>
        private readonly HotspotDetector _detector;

        /// <summary>
        /// Stores the threshold policy used by the current query service instance.
        /// </summary>
        private readonly HotspotThresholds _thresholds;

        /// <summary>
        /// Initializes a new instance of the <see cref="HotspotQueryService"/> class.
        /// </summary>
        /// <param name="snapshotWriter">The snapshot writer that may expose in-memory snapshots for local query behavior.</param>
        public HotspotQueryService(IArchitectureSnapshotWriter snapshotWriter)
            : this(snapshotWriter, HotspotThresholds.Default)
        {
            // The default constructor uses documented WP013 thresholds while keeping policy replacement possible for future composition.
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="HotspotQueryService"/> class with explicit thresholds.
        /// </summary>
        /// <param name="snapshotWriter">The snapshot writer that may expose in-memory snapshots for local query behavior.</param>
        /// <param name="thresholds">The threshold policy used for hotspot scoring.</param>
        public HotspotQueryService(IArchitectureSnapshotWriter snapshotWriter, HotspotThresholds thresholds)
        {
            // Query API default composition uses the in-memory writer, while future infrastructure adapters can replace this service or writer.
            _snapshotWriter = snapshotWriter ?? throw new ArgumentNullException(nameof(snapshotWriter));
            _thresholds = thresholds ?? throw new ArgumentNullException(nameof(thresholds));
            _detector = new HotspotDetector();
        }

        /// <summary>
        /// Lists detected hotspots using controlled snapshot, target, category, and paging filters.
        /// </summary>
        /// <param name="query">The controlled hotspot query contract.</param>
        /// <param name="cancellationToken">The cancellation token that can stop retrieval before graph work starts.</param>
        /// <returns>A bounded page of stable hotspot DTOs.</returns>
        public Task<PagedQueryResult<HotspotItemDto>> ListHotspotsAsync(HotspotQuery query, CancellationToken cancellationToken)
        {
            // The service computes hotspots from persisted snapshot facts and then applies fixed filters; callers never provide scoring logic.
            ArgumentNullException.ThrowIfNull(query);
            cancellationToken.ThrowIfCancellationRequested();
            ExtractedArchitectureSnapshot? snapshot = _snapshotWriter is InMemoryArchitectureSnapshotWriter writer
                ? writer.GetSnapshotsSnapshotForDiagnostics().FirstOrDefault(snapshot => StringComparer.Ordinal.Equals(snapshot.SnapshotHeader?.StableKey.Value, query.SnapshotStableKey))
                : null;
            if (snapshot is null)
            {
                PagedQueryResult<HotspotItemDto> empty = new([], totalCount: 0, query.Skip, query.Take);
                return Task.FromResult(empty);
            }

            HotspotRecord[] matches = _detector.DetectHotspots(snapshot, _thresholds)
                .Where(hotspot => query.Category is null || StringComparer.Ordinal.Equals(hotspot.Category, query.Category))
                .Where(hotspot => query.TargetStableKey is null || StringComparer.Ordinal.Equals(hotspot.TargetStableKey.Value, query.TargetStableKey))
                .OrderBy(static hotspot => hotspot.Category, StringComparer.Ordinal)
                .ThenBy(static hotspot => hotspot.Rank)
                .ThenBy(static hotspot => hotspot.TargetStableKey.Value, StringComparer.Ordinal)
                .ThenBy(static hotspot => hotspot.StableKey.Value, StringComparer.Ordinal)
                .ToArray();
            HotspotItemDto[] items = matches
                .Skip(query.Skip)
                .Take(query.Take)
                .Select(ToHotspotItem)
                .ToArray();
            PagedQueryResult<HotspotItemDto> result = new(items, matches.Length, query.Skip, query.Take);
            return Task.FromResult(result);
        }

        /// <summary>
        /// Maps a hotspot record to a public hotspot item DTO.
        /// </summary>
        /// <param name="hotspot">The detected hotspot record.</param>
        /// <returns>The stable public hotspot DTO.</returns>
        private static HotspotItemDto ToHotspotItem(HotspotRecord hotspot)
        {
            // Public hotspot responses expose contribution references and sanitized metadata but never database-local identifiers.
            return new HotspotItemDto(
                hotspot.SnapshotStableKey.Value,
                hotspot.StableKey.Value,
                hotspot.Category,
                hotspot.TargetStableKey.Value,
                hotspot.TargetKind,
                hotspot.DisplayName,
                hotspot.Score,
                hotspot.Rank,
                hotspot.ContributingMetricStableKeys.Select(static stableKey => stableKey.Value).ToArray(),
                hotspot.ContributingFindingStableKeys.Select(static stableKey => stableKey.Value).ToArray(),
                hotspot.EvidenceStableKeys.Select(static stableKey => stableKey.Value).ToArray(),
                hotspot.Confidence.Value,
                hotspot.UnknownState.HasUnknownData,
                hotspot.UnknownState.UnknownReason,
                PublicMetadataSanitizer.Sanitize(hotspot.Metadata),
                hotspot.Fingerprint.Value);
        }
    }
}
