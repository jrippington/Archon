using Archon.Application.Extraction.Contracts;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;
using Archon.Domain.Graph.Metrics;

namespace Archon.Application.Hotspots
{
    /// <summary>
    /// Detects deterministic architecture hotspots from extracted snapshot metrics, findings, graph facts, and cycle-derived metrics.
    /// </summary>
    public sealed class HotspotDetector
    {
        /// <summary>
        /// Detects hotspots for one extracted architecture snapshot using the supplied thresholds.
        /// </summary>
        /// <param name="snapshot">The snapshot containing metrics, findings, and display-name graph facts.</param>
        /// <param name="thresholds">The hotspot thresholds that convert numeric facts into hotspot records.</param>
        /// <returns>A deterministically ordered list of hotspot records with category-local ranks.</returns>
        public IReadOnlyList<HotspotRecord> DetectHotspots(ExtractedArchitectureSnapshot snapshot, HotspotThresholds thresholds)
        {
            // The detector is intentionally pure: it reads already-assembled snapshot facts and does not reach back into extractors or persistence.
            ArgumentNullException.ThrowIfNull(snapshot);
            ArgumentNullException.ThrowIfNull(thresholds);
            StableKey snapshotStableKey = snapshot.SnapshotHeader?.StableKey ?? throw new ArgumentException("Hotspot detection requires a snapshot header stable key.", nameof(snapshot));
            Dictionary<string, ArchitectureNode> nodeIndex = snapshot.Nodes.ToDictionary(static node => node.StableKey.Value, StringComparer.Ordinal);
            List<HotspotDraft> drafts = [];

            // Metric-derived hotspots cover graph coupling, modernization rollups, and cycle participation from the previous WP013 slices.
            AddMetricHotspots(snapshotStableKey, snapshot.Metrics, nodeIndex, thresholds, drafts);

            // Finding concentration is calculated from open findings grouped by affected node, keeping finding and evidence references intact.
            AddFindingConcentrationHotspots(snapshotStableKey, snapshot.Findings, nodeIndex, thresholds, drafts);

            return FinalizeRanking(drafts);
        }

        /// <summary>
        /// Adds hotspots whose score is derived directly from numeric metric records.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot being scored.</param>
        /// <param name="metrics">The metric records to inspect.</param>
        /// <param name="nodeIndex">The node display-name index keyed by stable key.</param>
        /// <param name="thresholds">The hotspot thresholds used for metric comparisons.</param>
        /// <param name="drafts">The mutable draft collection that receives matching hotspots.</param>
        private static void AddMetricHotspots(StableKey snapshotStableKey, IReadOnlyList<MetricRecord> metrics, IReadOnlyDictionary<string, ArchitectureNode> nodeIndex, HotspotThresholds thresholds, List<HotspotDraft> drafts)
        {
            // Each metric mapping is explicit so category semantics and threshold defaults remain easy to audit and document.
            foreach (MetricRecord metric in metrics.OrderBy(static metric => metric.StableKey.Value, StringComparer.Ordinal))
            {
                if (!metric.NumericValue.HasValue)
                {
                    continue;
                }

                AddMetricHotspotIfThresholdMet(snapshotStableKey, metric, nodeIndex, HotspotCategories.HighFanIn, thresholds.HighFanIn, drafts);
                AddMetricHotspotIfThresholdMet(snapshotStableKey, metric, nodeIndex, HotspotCategories.HighFanOut, thresholds.HighFanOut, drafts);
                AddMetricHotspotIfThresholdMet(snapshotStableKey, metric, nodeIndex, HotspotCategories.SharedLibrary, thresholds.SharedLibraryFanIn, drafts);
                AddMetricHotspotIfThresholdMet(snapshotStableKey, metric, nodeIndex, HotspotCategories.DependencyDepth, thresholds.DependencyDepth, drafts);
                AddMetricHotspotIfThresholdMet(snapshotStableKey, metric, nodeIndex, HotspotCategories.TransitiveDependencyCount, thresholds.TransitiveDependencyCount, drafts);
                AddMetricHotspotIfThresholdMet(snapshotStableKey, metric, nodeIndex, HotspotCategories.CycleParticipation, thresholds.CycleParticipation, drafts);
                AddMetricHotspotIfThresholdMet(snapshotStableKey, metric, nodeIndex, HotspotCategories.DataAccessSpread, thresholds.DataAccessSpread, drafts);
                AddMetricHotspotIfThresholdMet(snapshotStableKey, metric, nodeIndex, HotspotCategories.SharedTableUsage, thresholds.SharedTableUsage, drafts);
            }
        }

        /// <summary>
        /// Adds one metric-derived hotspot when the metric kind and threshold match the category mapping.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot being scored.</param>
        /// <param name="metric">The metric candidate.</param>
        /// <param name="nodeIndex">The node display-name index keyed by stable key.</param>
        /// <param name="category">The hotspot category being evaluated.</param>
        /// <param name="threshold">The minimum numeric value needed for a hotspot.</param>
        /// <param name="drafts">The mutable draft collection that receives matching hotspots.</param>
        private static void AddMetricHotspotIfThresholdMet(StableKey snapshotStableKey, MetricRecord metric, IReadOnlyDictionary<string, ArchitectureNode> nodeIndex, string category, decimal threshold, List<HotspotDraft> drafts)
        {
            // Mapping by metric kind keeps policy-like threshold behavior deterministic without requiring arbitrary rule expressions.
            if (!StringComparer.Ordinal.Equals(metric.MetricKind, GetMetricKindForCategory(category)) || !metric.NumericValue.HasValue || metric.NumericValue.Value < threshold)
            {
                return;
            }

            StableKey targetStableKey = metric.NodeStableKey ?? snapshotStableKey;
            ArchitectureNode? node = nodeIndex.TryGetValue(targetStableKey.Value, out ArchitectureNode? matchedNode) ? matchedNode : null;
            string targetKind = node?.NodeKind.Value ?? (metric.NodeStableKey is null ? "Snapshot" : "Node");
            string? displayName = node?.DisplayName ?? (metric.NodeStableKey is null ? snapshotStableKey.Value : targetStableKey.Value);
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["threshold"] = threshold,
                ["metricKind"] = metric.MetricKind,
                ["scoreSource"] = "metric",
                ["unit"] = metric.Unit
            });
            drafts.Add(new HotspotDraft(snapshotStableKey, category, targetStableKey, targetKind, displayName, metric.NumericValue.Value, [metric.StableKey], [], GetMetricEvidence(metric), metric.Confidence, metric.UnknownState, metadata));
        }

        /// <summary>
        /// Adds hotspots for architecture nodes that have many open findings.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot being scored.</param>
        /// <param name="findings">The finding records to group by affected node.</param>
        /// <param name="nodeIndex">The node display-name index keyed by stable key.</param>
        /// <param name="thresholds">The hotspot thresholds used for finding comparisons.</param>
        /// <param name="drafts">The mutable draft collection that receives matching hotspots.</param>
        private static void AddFindingConcentrationHotspots(StableKey snapshotStableKey, IReadOnlyList<FindingRecord> findings, IReadOnlyDictionary<string, ArchitectureNode> nodeIndex, HotspotThresholds thresholds, List<HotspotDraft> drafts)
        {
            // Only open findings contribute to active triage concentration so acknowledged or suppressed work does not inflate risk pressure.
            var groups = findings
                .Where(static finding => StringComparer.Ordinal.Equals(finding.Status.Value, "Open"))
                .SelectMany(static finding => finding.AffectedNodeStableKeys.Select(nodeStableKey => new { NodeStableKey = nodeStableKey, Finding = finding }))
                .GroupBy(static item => item.NodeStableKey.Value, StringComparer.Ordinal)
                .OrderBy(static group => group.Key, StringComparer.Ordinal);

            foreach (var group in groups)
            {
                FindingRecord[] contributingFindings = group.Select(static item => item.Finding).OrderBy(static finding => finding.StableKey.Value, StringComparer.Ordinal).ToArray();
                if (contributingFindings.Length < thresholds.HotlistFindingConcentration)
                {
                    continue;
                }

                StableKey targetStableKey = new(group.Key);
                ArchitectureNode? node = nodeIndex.TryGetValue(targetStableKey.Value, out ArchitectureNode? matchedNode) ? matchedNode : null;
                GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["threshold"] = thresholds.HotlistFindingConcentration,
                    ["findingCount"] = contributingFindings.Length,
                    ["scoreSource"] = "findings"
                });
                drafts.Add(new HotspotDraft(
                    snapshotStableKey,
                    HotspotCategories.HotlistFindingConcentration,
                    targetStableKey,
                    node?.NodeKind.Value ?? "Node",
                    node?.DisplayName ?? targetStableKey.Value,
                    contributingFindings.Length,
                    [],
                    contributingFindings.Select(static finding => finding.StableKey),
                    contributingFindings.SelectMany(static finding => finding.EvidenceStableKeys),
                    ComposeConfidence(contributingFindings.Select(static finding => finding.Confidence)),
                    ComposeUnknownState(contributingFindings.Select(static finding => finding.UnknownState)),
                    metadata));
            }
        }

        /// <summary>
        /// Assigns category-local ranks and constructs immutable hotspot records in deterministic order.
        /// </summary>
        /// <param name="drafts">The unranked hotspot drafts.</param>
        /// <returns>Ranked hotspot records.</returns>
        private static IReadOnlyList<HotspotRecord> FinalizeRanking(IReadOnlyList<HotspotDraft> drafts)
        {
            // Ranking is category-local: within a category higher scores appear first, with stable target and identity fields as tie-breakers.
            List<HotspotRecord> records = [];
            foreach (IGrouping<string, HotspotDraft> categoryGroup in drafts.GroupBy(static draft => draft.Category).OrderBy(static group => group.Key, StringComparer.Ordinal))
            {
                HotspotDraft[] orderedDrafts = categoryGroup
                    .OrderByDescending(static draft => draft.Score)
                    .ThenBy(static draft => draft.TargetStableKey.Value, StringComparer.Ordinal)
                    .ThenBy(static draft => draft.Category, StringComparer.Ordinal)
                    .ToArray();
                for (int index = 0; index < orderedDrafts.Length; index++)
                {
                    HotspotDraft draft = orderedDrafts[index];
                    int rank = index + 1;
                    StableKey stableKey = BuildStableKey(draft.SnapshotStableKey, draft.Category, draft.TargetStableKey);
                    records.Add(new HotspotRecord(
                        draft.SnapshotStableKey,
                        stableKey,
                        draft.Category,
                        draft.TargetStableKey,
                        draft.TargetKind,
                        draft.DisplayName,
                        draft.Score,
                        rank,
                        draft.ContributingMetricStableKeys,
                        draft.ContributingFindingStableKeys,
                        draft.EvidenceStableKeys,
                        draft.Confidence,
                        draft.UnknownState,
                        draft.Metadata,
                        CreateFingerprint(draft, rank)));
                }
            }

            return records
                .OrderBy(static hotspot => hotspot.Category, StringComparer.Ordinal)
                .ThenBy(static hotspot => hotspot.Rank)
                .ThenBy(static hotspot => hotspot.TargetStableKey.Value, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Gets the metric kind that can produce a specific hotspot category.
        /// </summary>
        /// <param name="category">The hotspot category.</param>
        /// <returns>The mapped metric kind, or an empty string when the category is not metric-derived.</returns>
        private static string GetMetricKindForCategory(string category)
        {
            // This switch is the central metric-to-hotspot category map for Work Item 6.
            return category switch
            {
                HotspotCategories.HighFanIn => MetricDefinitions.GraphFanIn.Kind,
                HotspotCategories.HighFanOut => MetricDefinitions.GraphFanOut.Kind,
                HotspotCategories.SharedLibrary => MetricDefinitions.GraphFanIn.Kind,
                HotspotCategories.DependencyDepth => MetricDefinitions.GraphDependencyDepth.Kind,
                HotspotCategories.TransitiveDependencyCount => MetricDefinitions.GraphTransitiveDependencyCount.Kind,
                HotspotCategories.CycleParticipation => MetricDefinitions.GraphCycleParticipation.Kind,
                HotspotCategories.DataAccessSpread => MetricDefinitions.ModernizationDataAccessSpread.Kind,
                HotspotCategories.SharedTableUsage => MetricDefinitions.ModernizationSharedTableUsageCount.Kind,
                _ => string.Empty
            };
        }

        /// <summary>
        /// Builds the deterministic stable key for one hotspot category and target.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key.</param>
        /// <param name="category">The hotspot category.</param>
        /// <param name="targetStableKey">The target stable key.</param>
        /// <returns>A deterministic hotspot stable key.</returns>
        private static StableKey BuildStableKey(StableKey snapshotStableKey, string category, StableKey targetStableKey)
        {
            // The stable key deliberately uses public logical identities rather than result rank so score changes do not change identity.
            return new StableKey($"hotspot://{snapshotStableKey.Value}/{category}/{targetStableKey.Value}");
        }

        /// <summary>
        /// Creates a deterministic fingerprint for one ranked hotspot.
        /// </summary>
        /// <param name="draft">The hotspot draft.</param>
        /// <param name="rank">The category-local rank assigned to the hotspot.</param>
        /// <returns>A deterministic fingerprint for diff-relevant hotspot content.</returns>
        private static Fingerprint CreateFingerprint(HotspotDraft draft, int rank)
        {
            // Fingerprints include the score, rank, contribution lists, unknown state, and metadata because each can change triage meaning.
            FingerprintInput input = FingerprintInput.Create("Hotspot")
                .AddField("snapshotStableKey", draft.SnapshotStableKey)
                .AddField("category", draft.Category)
                .AddField("targetStableKey", draft.TargetStableKey)
                .AddField("targetKind", draft.TargetKind)
                .AddField("score", draft.Score)
                .AddField("rank", rank)
                .AddField("confidence", draft.Confidence.Value)
                .AddField("hasUnknownData", draft.UnknownState.HasUnknownData)
                .AddField("unknownReason", draft.UnknownState.UnknownReason)
                .AddMetadata(draft.Metadata);
            input
                .AddField("metrics", string.Join("|", draft.ContributingMetricStableKeys.Select(static stableKey => stableKey.Value).OrderBy(static value => value, StringComparer.Ordinal)))
                .AddField("findings", string.Join("|", draft.ContributingFindingStableKeys.Select(static stableKey => stableKey.Value).OrderBy(static value => value, StringComparer.Ordinal)))
                .AddField("evidence", string.Join("|", draft.EvidenceStableKeys.Select(static stableKey => stableKey.Value).OrderBy(static value => value, StringComparer.Ordinal)));

            return FingerprintGenerator.FromInput(input);
        }

        /// <summary>
        /// Gets optional evidence references from a metric.
        /// </summary>
        /// <param name="metric">The metric whose evidence should be read.</param>
        /// <returns>A stable-key sequence containing the primary evidence when present.</returns>
        private static IEnumerable<StableKey> GetMetricEvidence(MetricRecord metric)
        {
            // Metric records currently carry one primary evidence stable key; the sequence form keeps the hotspot model extensible.
            return metric.PrimaryEvidenceStableKey.HasValue ? [metric.PrimaryEvidenceStableKey.Value] : [];
        }

        /// <summary>
        /// Composes hotspot confidence by taking the lowest confidence from all contributing facts.
        /// </summary>
        /// <param name="confidences">The contributing confidence values.</param>
        /// <returns>The conservative composed confidence value.</returns>
        private static Confidence ComposeConfidence(IEnumerable<Confidence> confidences)
        {
            // The minimum confidence is conservative and avoids overstating certainty when any contributor is weaker.
            decimal? minimum = confidences.Select(static confidence => confidence.Value).DefaultIfEmpty(1m).Min();
            return new Confidence(minimum ?? 1m);
        }

        /// <summary>
        /// Composes unknown-state details from contributing findings.
        /// </summary>
        /// <param name="unknownStates">The contributing unknown states.</param>
        /// <returns>A combined unknown state when any contributor has unknown data; otherwise known state.</returns>
        private static UnknownState ComposeUnknownState(IEnumerable<UnknownState> unknownStates)
        {
            // A hotspot is unknown-aware when any input carries unknown context; reasons are joined deterministically for explainability.
            string[] reasons = unknownStates
                .Where(static unknownState => unknownState.HasUnknownData)
                .Select(static unknownState => unknownState.UnknownReason)
                .Where(static reason => !string.IsNullOrWhiteSpace(reason))
                .Select(static reason => reason!)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static reason => reason, StringComparer.Ordinal)
                .ToArray();
            return reasons.Length == 0 ? UnknownState.Known : UnknownState.Unknown(string.Join("; ", reasons));
        }

        /// <summary>
        /// Holds one unranked hotspot candidate before deterministic category-local ranking is assigned.
        /// </summary>
        /// <param name="SnapshotStableKey">The snapshot stable key.</param>
        /// <param name="Category">The hotspot category.</param>
        /// <param name="TargetStableKey">The target stable key.</param>
        /// <param name="TargetKind">The target kind.</param>
        /// <param name="DisplayName">The optional target display name.</param>
        /// <param name="Score">The numeric hotspot score.</param>
        /// <param name="ContributingMetricStableKeys">The contributing metric stable keys.</param>
        /// <param name="ContributingFindingStableKeys">The contributing finding stable keys.</param>
        /// <param name="EvidenceStableKeys">The evidence stable keys.</param>
        /// <param name="Confidence">The composed confidence.</param>
        /// <param name="UnknownState">The composed unknown state.</param>
        /// <param name="Metadata">The deterministic hotspot metadata.</param>
        private sealed record HotspotDraft(
            StableKey SnapshotStableKey,
            string Category,
            StableKey TargetStableKey,
            string TargetKind,
            string? DisplayName,
            decimal Score,
            IEnumerable<StableKey> ContributingMetricStableKeys,
            IEnumerable<StableKey> ContributingFindingStableKeys,
            IEnumerable<StableKey> EvidenceStableKeys,
            Confidence Confidence,
            UnknownState UnknownState,
            GraphMetadata Metadata);
    }
}
