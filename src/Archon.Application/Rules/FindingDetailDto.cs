using Archon.Domain.Graph.Metadata;

namespace Archon.Application.Rules
{
    /// <summary>
    /// Represents the controlled public detail shape for one persisted WP012 finding.
    /// </summary>
    public sealed class FindingDetailDto
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FindingDetailDto"/> class.
        /// </summary>
        /// <param name="item">The summary hotlist shape for the finding.</param>
        /// <param name="description">The full finding description.</param>
        /// <param name="knowledgeKind">The knowledge classification for the finding.</param>
        /// <param name="primaryNodeStableKey">The optional primary node stable key.</param>
        /// <param name="primaryEvidenceStableKey">The optional primary evidence stable key.</param>
        /// <param name="firstSeenSnapshotStableKey">The optional first-seen snapshot stable key.</param>
        /// <param name="latestSeenSnapshotStableKey">The optional latest-seen snapshot stable key.</param>
        /// <param name="suppressionReason">The optional suppression reason.</param>
        /// <param name="suppressedBy">The optional actor or process that applied suppression.</param>
        /// <param name="metadata">The credential-safe lower camel case finding metadata.</param>
        /// <param name="fingerprint">The deterministic finding fingerprint.</param>
        public FindingDetailDto(
            HotlistItemDto item,
            string description,
            string knowledgeKind,
            string? primaryNodeStableKey,
            string? primaryEvidenceStableKey,
            string? firstSeenSnapshotStableKey,
            string? latestSeenSnapshotStableKey,
            string? suppressionReason,
            string? suppressedBy,
            GraphMetadata metadata,
            string fingerprint)
        {
            // Detail keeps potentially sensitive evidence snippets out of the response and exposes references plus safe metadata only.
            Item = item ?? throw new ArgumentNullException(nameof(item));
            Description = string.IsNullOrWhiteSpace(description) ? item.Summary : description.Trim();
            KnowledgeKind = RequireText(knowledgeKind, nameof(knowledgeKind));
            PrimaryNodeStableKey = NormalizeOptionalText(primaryNodeStableKey);
            PrimaryEvidenceStableKey = NormalizeOptionalText(primaryEvidenceStableKey);
            FirstSeenSnapshotStableKey = NormalizeOptionalText(firstSeenSnapshotStableKey);
            LatestSeenSnapshotStableKey = NormalizeOptionalText(latestSeenSnapshotStableKey);
            SuppressionReason = NormalizeOptionalText(suppressionReason);
            SuppressedBy = NormalizeOptionalText(suppressedBy);
            Metadata = metadata ?? GraphMetadata.Empty;
            Fingerprint = RequireText(fingerprint, nameof(fingerprint));
        }

        /// <summary>Gets the summary hotlist shape for the finding.</summary>
        public HotlistItemDto Item { get; }

        /// <summary>Gets the full finding description.</summary>
        public string Description { get; }

        /// <summary>Gets the knowledge classification for the finding.</summary>
        public string KnowledgeKind { get; }

        /// <summary>Gets the optional primary node stable key.</summary>
        public string? PrimaryNodeStableKey { get; }

        /// <summary>Gets the optional primary evidence stable key.</summary>
        public string? PrimaryEvidenceStableKey { get; }

        /// <summary>Gets the optional first-seen snapshot stable key.</summary>
        public string? FirstSeenSnapshotStableKey { get; }

        /// <summary>Gets the optional latest-seen snapshot stable key.</summary>
        public string? LatestSeenSnapshotStableKey { get; }

        /// <summary>Gets the optional suppression reason.</summary>
        public string? SuppressionReason { get; }

        /// <summary>Gets the optional actor or process that applied suppression.</summary>
        public string? SuppressedBy { get; }

        /// <summary>Gets the credential-safe lower camel case finding metadata.</summary>
        public GraphMetadata Metadata { get; }

        /// <summary>Gets the deterministic finding fingerprint.</summary>
        public string Fingerprint { get; }

        /// <summary>
        /// Normalizes optional query detail text.
        /// </summary>
        /// <param name="value">The optional text value.</param>
        /// <returns>The trimmed value, or <see langword="null"/> when the value is blank.</returns>
        private static string? NormalizeOptionalText(string? value)
        {
            // Optional fields use null instead of empty strings so JSON consumers can distinguish absent data from present data.
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>
        /// Requires non-empty text for mandatory detail fields.
        /// </summary>
        /// <param name="value">The candidate field value.</param>
        /// <param name="parameterName">The parameter name used for invalid input reporting.</param>
        /// <returns>The trimmed field value.</returns>
        private static string RequireText(string value, string parameterName)
        {
            // Detail records need stable identifiers and classifications so downstream tools can reason over them safely.
            return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A non-empty value is required.", parameterName) : value.Trim();
        }
    }
}
