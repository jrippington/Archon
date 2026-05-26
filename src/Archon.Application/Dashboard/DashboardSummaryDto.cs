namespace Archon.Application.Dashboard
{
    /// <summary>
    /// Represents the application-owned dashboard summary data returned by the first WP014 query slice.
    /// </summary>
    public sealed class DashboardSummaryDto
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DashboardSummaryDto"/> class.
        /// </summary>
        /// <param name="scope">The repository and optional solution scope applied to the summary.</param>
        /// <param name="snapshot">The resolved snapshot metadata.</param>
        /// <param name="counts">The deterministic count summary.</param>
        /// <param name="topHotspots">The compact top-hotspot rows for the selected scope.</param>
        /// <param name="latestChanges">The compact latest-change rows for the selected scope.</param>
        /// <param name="warnings">The non-fatal warnings produced while building the summary.</param>
        /// <param name="unknowns">The explicit unknown fields produced while building the summary.</param>
        public DashboardSummaryDto(DashboardScopeDto scope, DashboardSnapshotMetadataDto snapshot, DashboardCountSummaryDto counts, IEnumerable<DashboardHotspotSummaryDto>? topHotspots, IEnumerable<DashboardLatestChangeSummaryDto>? latestChanges, IEnumerable<DashboardWarningDto>? warnings, IEnumerable<DashboardUnknownDto>? unknowns)
        {
            // The constructor copies collections so response data stays immutable after application service construction finishes.
            Scope = scope ?? throw new ArgumentNullException(nameof(scope));
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            Counts = counts ?? throw new ArgumentNullException(nameof(counts));
            TopHotspots = topHotspots?.ToArray() ?? [];
            LatestChanges = latestChanges?.ToArray() ?? [];
            Warnings = warnings?.ToArray() ?? [];
            Unknowns = unknowns?.ToArray() ?? [];
        }

        /// <summary>
        /// Gets the repository and optional solution scope applied to the summary.
        /// </summary>
        public DashboardScopeDto Scope { get; }

        /// <summary>
        /// Gets the resolved snapshot metadata.
        /// </summary>
        public DashboardSnapshotMetadataDto Snapshot { get; }

        /// <summary>
        /// Gets the deterministic count summary.
        /// </summary>
        public DashboardCountSummaryDto Counts { get; }

        /// <summary>
        /// Gets the compact top-hotspot rows for the selected scope.
        /// </summary>
        public IReadOnlyList<DashboardHotspotSummaryDto> TopHotspots { get; }

        /// <summary>
        /// Gets the compact latest-change rows for the selected scope.
        /// </summary>
        public IReadOnlyList<DashboardLatestChangeSummaryDto> LatestChanges { get; }

        /// <summary>
        /// Gets the non-fatal warnings produced while building the summary.
        /// </summary>
        public IReadOnlyList<DashboardWarningDto> Warnings { get; }

        /// <summary>
        /// Gets the explicit unknown fields produced while building the summary.
        /// </summary>
        public IReadOnlyList<DashboardUnknownDto> Unknowns { get; }
    }
}