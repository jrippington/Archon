namespace Archon.Application.Rules
{
    /// <summary>
    /// Represents one controlled finding summary returned by the WP012 hotlist API.
    /// </summary>
    public sealed class HotlistItemDto
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="HotlistItemDto"/> class.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the finding.</param>
        /// <param name="stableKey">The snapshot-scoped finding stable key.</param>
        /// <param name="historyKey">The cross-snapshot finding history key.</param>
        /// <param name="ruleCode">The rule code that classified the finding.</param>
        /// <param name="ruleVersion">The rule version that classified the finding.</param>
        /// <param name="title">The finding title.</param>
        /// <param name="summary">The finding summary.</param>
        /// <param name="severity">The finding severity.</param>
        /// <param name="status">The finding lifecycle status.</param>
        /// <param name="confidence">The normalized finding confidence.</param>
        /// <param name="category">The optional rule category.</param>
        /// <param name="affectedNodes">The affected-node references returned with the finding.</param>
        /// <param name="evidenceReferences">The evidence references returned with the finding.</param>
        /// <param name="hasUnknownData">Indicates whether the finding was produced with partial unknown context.</param>
        /// <param name="unknownReason">The optional unknown-state reason.</param>
        public HotlistItemDto(
            string snapshotStableKey,
            string stableKey,
            string historyKey,
            string ruleCode,
            string ruleVersion,
            string title,
            string summary,
            string severity,
            string status,
            decimal confidence,
            string? category,
            IEnumerable<AffectedNodeReferenceDto> affectedNodes,
            IEnumerable<FindingEvidenceReferenceDto> evidenceReferences,
            bool hasUnknownData,
            string? unknownReason)
        {
            // The hotlist item is intentionally summary-shaped; detailed metadata and history are available through dedicated endpoints.
            SnapshotStableKey = RequireText(snapshotStableKey, nameof(snapshotStableKey));
            StableKey = RequireText(stableKey, nameof(stableKey));
            HistoryKey = RequireText(historyKey, nameof(historyKey));
            RuleCode = RequireText(ruleCode, nameof(ruleCode));
            RuleVersion = RequireText(ruleVersion, nameof(ruleVersion));
            Title = RequireText(title, nameof(title));
            Summary = RequireText(summary, nameof(summary));
            Severity = RequireText(severity, nameof(severity));
            Status = RequireText(status, nameof(status));
            Confidence = confidence;
            Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
            AffectedNodes = affectedNodes.OrderBy(static node => node.StableKey, StringComparer.Ordinal).ToArray();
            EvidenceReferences = evidenceReferences.OrderBy(static evidence => evidence.StableKey, StringComparer.Ordinal).ToArray();
            HasUnknownData = hasUnknownData;
            UnknownReason = string.IsNullOrWhiteSpace(unknownReason) ? null : unknownReason.Trim();
        }

        /// <summary>Gets the snapshot stable key that scopes the finding.</summary>
        public string SnapshotStableKey { get; }

        /// <summary>Gets the snapshot-scoped finding stable key.</summary>
        public string StableKey { get; }

        /// <summary>Gets the cross-snapshot finding history key.</summary>
        public string HistoryKey { get; }

        /// <summary>Gets the rule code that classified the finding.</summary>
        public string RuleCode { get; }

        /// <summary>Gets the rule version that classified the finding.</summary>
        public string RuleVersion { get; }

        /// <summary>Gets the finding title.</summary>
        public string Title { get; }

        /// <summary>Gets the finding summary.</summary>
        public string Summary { get; }

        /// <summary>Gets the finding severity.</summary>
        public string Severity { get; }

        /// <summary>Gets the finding lifecycle status.</summary>
        public string Status { get; }

        /// <summary>Gets the normalized finding confidence.</summary>
        public decimal Confidence { get; }

        /// <summary>Gets the optional rule category.</summary>
        public string? Category { get; }

        /// <summary>Gets the affected-node references returned with the finding.</summary>
        public IReadOnlyList<AffectedNodeReferenceDto> AffectedNodes { get; }

        /// <summary>Gets the evidence references returned with the finding.</summary>
        public IReadOnlyList<FindingEvidenceReferenceDto> EvidenceReferences { get; }

        /// <summary>Gets a value indicating whether the finding was produced with partial unknown context.</summary>
        public bool HasUnknownData { get; }

        /// <summary>Gets the optional unknown-state reason.</summary>
        public string? UnknownReason { get; }

        /// <summary>
        /// Requires non-empty text for stable hotlist fields.
        /// </summary>
        /// <param name="value">The candidate field value.</param>
        /// <param name="parameterName">The parameter name used for invalid input reporting.</param>
        /// <returns>The trimmed field value.</returns>
        private static string RequireText(string value, string parameterName)
        {
            // Hotlist records must be independently understandable without follow-up graph queries for basic identity and display fields.
            return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A non-empty value is required.", parameterName) : value.Trim();
        }
    }
}
