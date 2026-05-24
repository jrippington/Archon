using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;

namespace Archon.Application.Hotspots
{
    /// <summary>
    /// Represents one deterministic architecture hotspot detected from snapshot metrics, findings, graph facts, or cycles.
    /// </summary>
    public sealed class HotspotRecord
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="HotspotRecord"/> class.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that scopes the hotspot.</param>
        /// <param name="stableKey">The deterministic hotspot stable key.</param>
        /// <param name="category">The stable hotspot category.</param>
        /// <param name="targetStableKey">The stable key of the node, snapshot, or graph target being scored.</param>
        /// <param name="targetKind">The public target kind such as Project, Node, or Snapshot.</param>
        /// <param name="displayName">The optional developer-facing target display name.</param>
        /// <param name="score">The numeric score used for deterministic ranking.</param>
        /// <param name="rank">The deterministic one-based rank after category, score, and stable-key ordering.</param>
        /// <param name="contributingMetricStableKeys">The metric stable keys that contributed to the hotspot.</param>
        /// <param name="contributingFindingStableKeys">The finding stable keys that contributed to the hotspot.</param>
        /// <param name="evidenceStableKeys">The evidence stable keys that explain contributing facts.</param>
        /// <param name="confidence">The confidence composed from contributing metrics and findings.</param>
        /// <param name="unknownState">The unknown-state value carried from contributing inputs.</param>
        /// <param name="metadata">The deterministic metadata explaining threshold and score composition.</param>
        /// <param name="fingerprint">The deterministic fingerprint for diff-relevant hotspot content.</param>
        public HotspotRecord(
            StableKey snapshotStableKey,
            StableKey stableKey,
            string? category,
            StableKey targetStableKey,
            string? targetKind,
            string? displayName,
            decimal score,
            int rank,
            IEnumerable<StableKey>? contributingMetricStableKeys,
            IEnumerable<StableKey>? contributingFindingStableKeys,
            IEnumerable<StableKey>? evidenceStableKeys,
            Confidence confidence,
            UnknownState unknownState,
            GraphMetadata metadata,
            Fingerprint fingerprint)
        {
            // Hotspot records are public query objects, so construction normalizes every list into deterministic stable-key order.
            SnapshotStableKey = snapshotStableKey;
            StableKey = stableKey;
            Category = string.IsNullOrWhiteSpace(category) ? throw new ArgumentException("Hotspot category is required.", nameof(category)) : category.Trim();
            TargetStableKey = targetStableKey;
            TargetKind = string.IsNullOrWhiteSpace(targetKind) ? throw new ArgumentException("Hotspot target kind is required.", nameof(targetKind)) : targetKind.Trim();
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
            Score = score;
            Rank = rank;
            ContributingMetricStableKeys = NormalizeStableKeys(contributingMetricStableKeys);
            ContributingFindingStableKeys = NormalizeStableKeys(contributingFindingStableKeys);
            EvidenceStableKeys = NormalizeStableKeys(evidenceStableKeys);
            Confidence = confidence;
            UnknownState = unknownState ?? throw new ArgumentNullException(nameof(unknownState));
            Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            Fingerprint = fingerprint;
        }

        /// <summary>
        /// Gets the stable key of the snapshot that scopes the hotspot.
        /// </summary>
        public StableKey SnapshotStableKey { get; }

        /// <summary>
        /// Gets the deterministic hotspot stable key.
        /// </summary>
        public StableKey StableKey { get; }

        /// <summary>
        /// Gets the stable hotspot category.
        /// </summary>
        public string Category { get; }

        /// <summary>
        /// Gets the stable key of the node, snapshot, or graph target being scored.
        /// </summary>
        public StableKey TargetStableKey { get; }

        /// <summary>
        /// Gets the public target kind such as Project, Node, or Snapshot.
        /// </summary>
        public string TargetKind { get; }

        /// <summary>
        /// Gets the optional developer-facing target display name.
        /// </summary>
        public string? DisplayName { get; }

        /// <summary>
        /// Gets the numeric score used for deterministic ranking.
        /// </summary>
        public decimal Score { get; }

        /// <summary>
        /// Gets the deterministic one-based rank after category, score, and stable-key ordering.
        /// </summary>
        public int Rank { get; }

        /// <summary>
        /// Gets the metric stable keys that contributed to the hotspot.
        /// </summary>
        public IReadOnlyList<StableKey> ContributingMetricStableKeys { get; }

        /// <summary>
        /// Gets the finding stable keys that contributed to the hotspot.
        /// </summary>
        public IReadOnlyList<StableKey> ContributingFindingStableKeys { get; }

        /// <summary>
        /// Gets the evidence stable keys that explain contributing facts.
        /// </summary>
        public IReadOnlyList<StableKey> EvidenceStableKeys { get; }

        /// <summary>
        /// Gets the confidence composed from contributing metrics and findings.
        /// </summary>
        public Confidence Confidence { get; }

        /// <summary>
        /// Gets the unknown-state value carried from contributing inputs.
        /// </summary>
        public UnknownState UnknownState { get; }

        /// <summary>
        /// Gets deterministic metadata explaining threshold and score composition.
        /// </summary>
        public GraphMetadata Metadata { get; }

        /// <summary>
        /// Gets the deterministic fingerprint for diff-relevant hotspot content.
        /// </summary>
        public Fingerprint Fingerprint { get; }

        /// <summary>
        /// Normalizes a nullable stable-key sequence into deterministic distinct order.
        /// </summary>
        /// <param name="stableKeys">The nullable stable-key sequence.</param>
        /// <returns>A read-only list with duplicate stable keys removed.</returns>
        private static IReadOnlyList<StableKey> NormalizeStableKeys(IEnumerable<StableKey>? stableKeys)
        {
            // Stable contribution ordering makes API responses, ranking, and tests repeatable regardless of input order.
            return stableKeys is null
                ? []
                : stableKeys.Distinct().OrderBy(static stableKey => stableKey.Value, StringComparer.Ordinal).ToArray();
        }
    }
}
