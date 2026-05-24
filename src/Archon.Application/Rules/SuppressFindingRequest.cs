using Archon.Domain.Graph.Metadata;

namespace Archon.Application.Rules
{
    /// <summary>
    /// Represents a validated-intent request to suppress an equivalent finding without deleting the underlying finding record.
    /// </summary>
    public sealed class SuppressFindingRequest
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SuppressFindingRequest"/> class.
        /// </summary>
        /// <param name="findingHistoryKey">The cross-snapshot finding history key to suppress.</param>
        /// <param name="ruleCode">The rule code associated with the suppression.</param>
        /// <param name="ruleVersion">The rule version associated with the suppression.</param>
        /// <param name="primaryNodeStableKey">The primary affected node stable key associated with the suppression.</param>
        /// <param name="reason">The human-readable reason the finding is suppressed.</param>
        /// <param name="suppressedBy">The actor or process that applied the suppression.</param>
        /// <param name="metadata">Additional deterministic suppression metadata, such as ticket references.</param>
        public SuppressFindingRequest(
            string findingHistoryKey,
            string ruleCode,
            string ruleVersion,
            string primaryNodeStableKey,
            string reason,
            string suppressedBy,
            GraphMetadata metadata)
        {
            // The request preserves rule and node identity so suppression can apply across later snapshots for the same logical finding.
            FindingHistoryKey = Normalize(findingHistoryKey);
            RuleCode = Normalize(ruleCode);
            RuleVersion = Normalize(ruleVersion);
            PrimaryNodeStableKey = Normalize(primaryNodeStableKey);
            Reason = Normalize(reason);
            SuppressedBy = Normalize(suppressedBy);
            Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        }

        /// <summary>
        /// Gets the cross-snapshot finding history key to suppress.
        /// </summary>
        public string FindingHistoryKey { get; }

        /// <summary>
        /// Gets the rule code associated with the suppression.
        /// </summary>
        public string RuleCode { get; }

        /// <summary>
        /// Gets the rule version associated with the suppression.
        /// </summary>
        public string RuleVersion { get; }

        /// <summary>
        /// Gets the primary affected node stable key associated with the suppression.
        /// </summary>
        public string PrimaryNodeStableKey { get; }

        /// <summary>
        /// Gets the human-readable reason the finding is suppressed.
        /// </summary>
        public string Reason { get; }

        /// <summary>
        /// Gets the actor or process that applied the suppression.
        /// </summary>
        public string SuppressedBy { get; }

        /// <summary>
        /// Gets additional deterministic suppression metadata, such as ticket references.
        /// </summary>
        public GraphMetadata Metadata { get; }

        /// <summary>
        /// Trims nullable text while allowing validation to report all missing fields later.
        /// </summary>
        /// <param name="value">The candidate text value.</param>
        /// <returns>The trimmed value or an empty string when no meaningful value was supplied.</returns>
        private static string Normalize(string value)
        {
            // Suppression validation reports missing fields as structured validation errors instead of constructor exceptions.
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
