namespace Archon.Application.Rules
{
    /// <summary>
    /// Represents one historical finding record in a cross-snapshot finding history response.
    /// </summary>
    public sealed class FindingHistoryRecordDto
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FindingHistoryRecordDto"/> class.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that owns the historical finding record.</param>
        /// <param name="stableKey">The snapshot-scoped finding stable key.</param>
        /// <param name="status">The finding lifecycle status in that snapshot.</param>
        /// <param name="severity">The finding severity in that snapshot.</param>
        /// <param name="confidence">The finding confidence in that snapshot.</param>
        /// <param name="fingerprint">The deterministic fingerprint in that snapshot.</param>
        public FindingHistoryRecordDto(string snapshotStableKey, string stableKey, string status, string severity, decimal confidence, string fingerprint)
        {
            // History records are intentionally compact because the finding detail endpoint exposes the full shape for a specific snapshot.
            SnapshotStableKey = RequireText(snapshotStableKey, nameof(snapshotStableKey));
            StableKey = RequireText(stableKey, nameof(stableKey));
            Status = RequireText(status, nameof(status));
            Severity = RequireText(severity, nameof(severity));
            Confidence = confidence;
            Fingerprint = RequireText(fingerprint, nameof(fingerprint));
        }

        /// <summary>Gets the snapshot stable key that owns the historical finding record.</summary>
        public string SnapshotStableKey { get; }

        /// <summary>Gets the snapshot-scoped finding stable key.</summary>
        public string StableKey { get; }

        /// <summary>Gets the finding lifecycle status in that snapshot.</summary>
        public string Status { get; }

        /// <summary>Gets the finding severity in that snapshot.</summary>
        public string Severity { get; }

        /// <summary>Gets the finding confidence in that snapshot.</summary>
        public decimal Confidence { get; }

        /// <summary>Gets the deterministic fingerprint in that snapshot.</summary>
        public string Fingerprint { get; }

        /// <summary>
        /// Requires non-empty history field text.
        /// </summary>
        /// <param name="value">The candidate field value.</param>
        /// <param name="parameterName">The parameter name used for invalid input reporting.</param>
        /// <returns>The trimmed field value.</returns>
        private static string RequireText(string value, string parameterName)
        {
            // History records must remain independently identifiable by snapshot and finding stable key.
            return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A non-empty value is required.", parameterName) : value.Trim();
        }
    }
}
