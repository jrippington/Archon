namespace Archon.Application.Dashboard
{
    /// <summary>
    /// Represents a compact top-hotspot row in the dashboard summary.
    /// </summary>
    public sealed class DashboardHotspotSummaryDto
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DashboardHotspotSummaryDto"/> class.
        /// </summary>
        /// <param name="stableKey">The stable hotspot identity.</param>
        /// <param name="category">The stable hotspot category.</param>
        /// <param name="targetStableKey">The stable key of the hotspot target.</param>
        /// <param name="targetKind">The graph kind of the hotspot target.</param>
        /// <param name="displayName">The developer-facing target display name.</param>
        /// <param name="score">The calculated hotspot score.</param>
        /// <param name="rank">The deterministic category-local rank.</param>
        /// <param name="confidence">The confidence assigned to the hotspot.</param>
        /// <param name="hasUnknownData">Indicates whether the hotspot carries explicit unknown data.</param>
        /// <param name="unknownReason">The reason hotspot data is unknown when applicable.</param>
        public DashboardHotspotSummaryDto(string? stableKey, string? category, string? targetStableKey, string? targetKind, string? displayName, decimal score, int rank, decimal confidence, bool hasUnknownData, string? unknownReason)
        {
            // Top-hotspot rows preserve stable identities so clients can drill into the full hotspot endpoint later without database IDs.
            StableKey = stableKey ?? string.Empty;
            Category = category ?? string.Empty;
            TargetStableKey = targetStableKey ?? string.Empty;
            TargetKind = targetKind ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Score = score;
            Rank = Math.Max(0, rank);
            Confidence = confidence;
            HasUnknownData = hasUnknownData;
            UnknownReason = unknownReason;
        }

        /// <summary>
        /// Gets the stable hotspot identity.
        /// </summary>
        public string StableKey { get; }

        /// <summary>
        /// Gets the stable hotspot category.
        /// </summary>
        public string Category { get; }

        /// <summary>
        /// Gets the stable key of the hotspot target.
        /// </summary>
        public string TargetStableKey { get; }

        /// <summary>
        /// Gets the graph kind of the hotspot target.
        /// </summary>
        public string TargetKind { get; }

        /// <summary>
        /// Gets the developer-facing target display name.
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        /// Gets the calculated hotspot score.
        /// </summary>
        public decimal Score { get; }

        /// <summary>
        /// Gets the deterministic category-local rank.
        /// </summary>
        public int Rank { get; }

        /// <summary>
        /// Gets the confidence assigned to the hotspot.
        /// </summary>
        public decimal Confidence { get; }

        /// <summary>
        /// Gets a value indicating whether the hotspot carries explicit unknown data.
        /// </summary>
        public bool HasUnknownData { get; }

        /// <summary>
        /// Gets the reason hotspot data is unknown when applicable.
        /// </summary>
        public string? UnknownReason { get; }
    }
}