using Archon.Domain.Graph.Model;

namespace Archon.Application.Rules
{
    /// <summary>
    /// Represents the persisted cross-snapshot history context used when an equivalent finding appears again.
    /// </summary>
    public sealed class FindingHistorySeed
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FindingHistorySeed"/> class.
        /// </summary>
        /// <param name="historyKey">The deterministic cross-snapshot history identity.</param>
        /// <param name="firstSeenSnapshotStableKey">The first snapshot where the finding was seen.</param>
        /// <param name="latestSeenSnapshotStableKey">The latest snapshot where the finding was seen before the current construction pass.</param>
        public FindingHistorySeed(string historyKey, string firstSeenSnapshotStableKey, string latestSeenSnapshotStableKey)
        {
            // History data is kept in application contracts so construction can resolve first/latest seen without knowing Neo4j details.
            HistoryKey = RequireText(historyKey, nameof(historyKey));
            FirstSeenSnapshotStableKey = RequireText(firstSeenSnapshotStableKey, nameof(firstSeenSnapshotStableKey));
            LatestSeenSnapshotStableKey = RequireText(latestSeenSnapshotStableKey, nameof(latestSeenSnapshotStableKey));
        }

        /// <summary>
        /// Gets the deterministic cross-snapshot history identity.
        /// </summary>
        public string HistoryKey { get; }

        /// <summary>
        /// Gets the first snapshot where the finding was seen.
        /// </summary>
        public string FirstSeenSnapshotStableKey { get; }

        /// <summary>
        /// Gets the latest snapshot where the finding was seen before the current construction pass.
        /// </summary>
        public string LatestSeenSnapshotStableKey { get; }

        /// <summary>
        /// Creates a history seed from an existing finding record.
        /// </summary>
        /// <param name="finding">The finding whose history should seed later construction.</param>
        /// <returns>A history seed carrying the finding's history identity and seen-snapshot range.</returns>
        public static FindingHistorySeed FromFinding(FindingRecord finding)
        {
            // Persisted findings already carry the history key and first/latest seen fields needed to classify equivalent later matches.
            ArgumentNullException.ThrowIfNull(finding);
            return new FindingHistorySeed(
                finding.HistoryKey,
                finding.FirstSeenSnapshotStableKey?.Value ?? finding.SnapshotStableKey.Value,
                finding.LatestSeenSnapshotStableKey?.Value ?? finding.SnapshotStableKey.Value);
        }

        /// <summary>
        /// Requires a non-empty text value and returns its trimmed form.
        /// </summary>
        /// <param name="value">The candidate text value.</param>
        /// <param name="parameterName">The parameter name used in validation failures.</param>
        /// <returns>The trimmed text value.</returns>
        private static string RequireText(string value, string parameterName)
        {
            // Blank history identities would prevent deterministic first/latest seen resolution.
            return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A non-empty value is required.", parameterName) : value.Trim();
        }
    }
}
