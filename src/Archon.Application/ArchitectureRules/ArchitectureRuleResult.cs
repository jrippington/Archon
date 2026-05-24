using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;

namespace Archon.Application.ArchitectureRules
{
    /// <summary>
    /// Represents one deterministic architecture-rule result produced from snapshot graph, metric, finding, or semantic facts.
    /// </summary>
    public sealed class ArchitectureRuleResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ArchitectureRuleResult"/> class.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that scopes the result.</param>
        /// <param name="stableKey">The deterministic result stable key.</param>
        /// <param name="ruleCode">The stable rule/check identity.</param>
        /// <param name="ruleName">The developer-facing rule/check name.</param>
        /// <param name="category">The controlled rule category string.</param>
        /// <param name="status">The stable result status.</param>
        /// <param name="targetStableKey">The stable key of the primary result target.</param>
        /// <param name="targetKind">The public target kind such as Project, Controller, or Node.</param>
        /// <param name="displayName">The optional developer-facing target display name.</param>
        /// <param name="description">The developer-facing result description.</param>
        /// <param name="contributingMetricStableKeys">The metric stable keys that contributed to the result.</param>
        /// <param name="contributingEdgeStableKeys">The architecture edge stable keys that contributed to the result.</param>
        /// <param name="contributingFindingStableKeys">The finding stable keys that contributed to the result.</param>
        /// <param name="evidenceStableKeys">The evidence stable keys that explain contributing graph facts.</param>
        /// <param name="confidence">The normalized result confidence.</param>
        /// <param name="unknownState">The explicit unknown-state context for incomplete rule inputs.</param>
        /// <param name="metadata">The deterministic metadata explaining check behavior and inputs.</param>
        /// <param name="fingerprint">The deterministic fingerprint for diff-relevant result content.</param>
        public ArchitectureRuleResult(
            StableKey snapshotStableKey,
            StableKey stableKey,
            string? ruleCode,
            string? ruleName,
            string? category,
            string? status,
            StableKey targetStableKey,
            string? targetKind,
            string? displayName,
            string? description,
            IEnumerable<StableKey>? contributingMetricStableKeys,
            IEnumerable<StableKey>? contributingEdgeStableKeys,
            IEnumerable<StableKey>? contributingFindingStableKeys,
            IEnumerable<StableKey>? evidenceStableKeys,
            Confidence confidence,
            UnknownState unknownState,
            GraphMetadata metadata,
            Fingerprint fingerprint)
        {
            // Construction validates public identity and display fields because result DTOs are exposed directly by controlled APIs.
            SnapshotStableKey = snapshotStableKey;
            StableKey = stableKey;
            RuleCode = RequireText(ruleCode, nameof(ruleCode));
            RuleName = RequireText(ruleName, nameof(ruleName));
            Category = RequireText(category, nameof(category));
            Status = RequireText(status, nameof(status));
            TargetStableKey = targetStableKey;
            TargetKind = RequireText(targetKind, nameof(targetKind));
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
            Description = RequireText(description, nameof(description));
            ContributingMetricStableKeys = NormalizeStableKeys(contributingMetricStableKeys);
            ContributingEdgeStableKeys = NormalizeStableKeys(contributingEdgeStableKeys);
            ContributingFindingStableKeys = NormalizeStableKeys(contributingFindingStableKeys);
            EvidenceStableKeys = NormalizeStableKeys(evidenceStableKeys);
            Confidence = confidence;
            UnknownState = unknownState ?? throw new ArgumentNullException(nameof(unknownState));
            Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            Fingerprint = fingerprint;
        }

        /// <summary>
        /// Gets the stable key of the snapshot that scopes the result.
        /// </summary>
        public StableKey SnapshotStableKey { get; }

        /// <summary>
        /// Gets the deterministic result stable key.
        /// </summary>
        public StableKey StableKey { get; }

        /// <summary>
        /// Gets the stable rule/check identity.
        /// </summary>
        public string RuleCode { get; }

        /// <summary>
        /// Gets the developer-facing rule/check name.
        /// </summary>
        public string RuleName { get; }

        /// <summary>
        /// Gets the controlled rule category string.
        /// </summary>
        public string Category { get; }

        /// <summary>
        /// Gets the stable result status.
        /// </summary>
        public string Status { get; }

        /// <summary>
        /// Gets the stable key of the primary result target.
        /// </summary>
        public StableKey TargetStableKey { get; }

        /// <summary>
        /// Gets the public target kind such as Project, Controller, or Node.
        /// </summary>
        public string TargetKind { get; }

        /// <summary>
        /// Gets the optional developer-facing target display name.
        /// </summary>
        public string? DisplayName { get; }

        /// <summary>
        /// Gets the developer-facing result description.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Gets the metric stable keys that contributed to the result.
        /// </summary>
        public IReadOnlyList<StableKey> ContributingMetricStableKeys { get; }

        /// <summary>
        /// Gets the architecture edge stable keys that contributed to the result.
        /// </summary>
        public IReadOnlyList<StableKey> ContributingEdgeStableKeys { get; }

        /// <summary>
        /// Gets the finding stable keys that contributed to the result.
        /// </summary>
        public IReadOnlyList<StableKey> ContributingFindingStableKeys { get; }

        /// <summary>
        /// Gets the evidence stable keys that explain contributing graph facts.
        /// </summary>
        public IReadOnlyList<StableKey> EvidenceStableKeys { get; }

        /// <summary>
        /// Gets the normalized result confidence.
        /// </summary>
        public Confidence Confidence { get; }

        /// <summary>
        /// Gets the explicit unknown-state context for incomplete rule inputs.
        /// </summary>
        public UnknownState UnknownState { get; }

        /// <summary>
        /// Gets the deterministic metadata explaining check behavior and inputs.
        /// </summary>
        public GraphMetadata Metadata { get; }

        /// <summary>
        /// Gets the deterministic fingerprint for diff-relevant result content.
        /// </summary>
        public Fingerprint Fingerprint { get; }

        /// <summary>
        /// Requires a non-empty text value and returns the trimmed value.
        /// </summary>
        /// <param name="value">The candidate text value.</param>
        /// <param name="parameterName">The parameter name to use in validation exceptions.</param>
        /// <returns>The trimmed text value.</returns>
        private static string RequireText(string? value, string parameterName)
        {
            // Public result fields must be explicit because callers use them for filtering, display, and stable comparison.
            return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A non-empty value is required.", parameterName) : value.Trim();
        }

        /// <summary>
        /// Normalizes stable-key contribution sequences into deterministic distinct order.
        /// </summary>
        /// <param name="stableKeys">The nullable stable-key sequence.</param>
        /// <returns>A read-only list with duplicate stable keys removed.</returns>
        private static IReadOnlyList<StableKey> NormalizeStableKeys(IEnumerable<StableKey>? stableKeys)
        {
            // Stable contribution ordering keeps API responses, tests, and later snapshot diffs deterministic.
            return stableKeys is null
                ? []
                : stableKeys.Distinct().OrderBy(static stableKey => stableKey.Value, StringComparer.Ordinal).ToArray();
        }
    }
}
