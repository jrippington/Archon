namespace Archon.Application.Rules
{
    /// <summary>
    /// Represents cross-snapshot history for one deterministic finding history key.
    /// </summary>
    public sealed class FindingHistoryDto
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FindingHistoryDto"/> class.
        /// </summary>
        /// <param name="historyKey">The deterministic cross-snapshot finding history key.</param>
        /// <param name="firstSeenSnapshotStableKey">The first snapshot stable key where the finding was observed.</param>
        /// <param name="latestSeenSnapshotStableKey">The latest snapshot stable key where the finding was observed.</param>
        /// <param name="records">The ordered historical finding records for the history key.</param>
        public FindingHistoryDto(string historyKey, string firstSeenSnapshotStableKey, string latestSeenSnapshotStableKey, IEnumerable<FindingHistoryRecordDto> records)
        {
            // The history envelope exposes first/latest values separately from the per-snapshot records for simple client display.
            HistoryKey = RequireText(historyKey, nameof(historyKey));
            FirstSeenSnapshotStableKey = RequireText(firstSeenSnapshotStableKey, nameof(firstSeenSnapshotStableKey));
            LatestSeenSnapshotStableKey = RequireText(latestSeenSnapshotStableKey, nameof(latestSeenSnapshotStableKey));
            Records = records.OrderBy(static record => record.SnapshotStableKey, StringComparer.Ordinal).ThenBy(static record => record.StableKey, StringComparer.Ordinal).ToArray();
        }

        /// <summary>Gets the deterministic cross-snapshot finding history key.</summary>
        public string HistoryKey { get; }

        /// <summary>Gets the first snapshot stable key where the finding was observed.</summary>
        public string FirstSeenSnapshotStableKey { get; }

        /// <summary>Gets the latest snapshot stable key where the finding was observed.</summary>
        public string LatestSeenSnapshotStableKey { get; }

        /// <summary>Gets the ordered historical finding records for the history key.</summary>
        public IReadOnlyList<FindingHistoryRecordDto> Records { get; }

        /// <summary>
        /// Requires non-empty history envelope text.
        /// </summary>
        /// <param name="value">The candidate field value.</param>
        /// <param name="parameterName">The parameter name used for invalid input reporting.</param>
        /// <returns>The trimmed field value.</returns>
        private static string RequireText(string value, string parameterName)
        {
            // History identity and first/latest snapshots are mandatory for explainable historical responses.
            return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A non-empty value is required.", parameterName) : value.Trim();
        }
    }
}
