using Archon.Application.Extraction.Contracts;
using Archon.Application.Hotspots;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;
using Archon.Domain.Graph.Metrics;
using Xunit;

namespace Archon.Application.Tests.Hotspots
{
    /// <summary>
    /// Verifies hotspot detection maps metrics, graph facts, cycles, and findings into deterministic architectural risk records.
    /// </summary>
    public sealed class HotspotDetectorTests
    {
        /// <summary>
        /// Confirms graph metrics produce the required coupling, depth, transitive dependency, and cycle hotspot categories.
        /// </summary>
        [Fact]
        public void DetectHotspots_WhenGraphMetricThresholdsAreMet_ShouldReturnCouplingHotspots()
        {
            // The fixture uses one project node with several graph metrics so every graph-derived category can be asserted together.
            StableKey snapshotStableKey = new("snapshot://hotspots-graph");
            StableKey projectStableKey = new("project://src/Shared/Shared.csproj");
            MetricRecord fanIn = CreateMetric(snapshotStableKey, MetricDefinitions.GraphFanIn.Kind, 9, MetricScopeKind.Node, projectStableKey, "edges");
            MetricRecord fanOut = CreateMetric(snapshotStableKey, MetricDefinitions.GraphFanOut.Kind, 7, MetricScopeKind.Node, projectStableKey, "edges");
            MetricRecord depth = CreateMetric(snapshotStableKey, MetricDefinitions.GraphDependencyDepth.Kind, 5, MetricScopeKind.Node, projectStableKey, "hops");
            MetricRecord transitive = CreateMetric(snapshotStableKey, MetricDefinitions.GraphTransitiveDependencyCount.Kind, 12, MetricScopeKind.Node, projectStableKey, "nodes");
            MetricRecord cycle = CreateMetric(snapshotStableKey, MetricDefinitions.GraphCycleParticipation.Kind, 2, MetricScopeKind.Node, projectStableKey, "cycles");
            ExtractedArchitectureSnapshot snapshot = CreateSnapshot(
                snapshotStableKey,
                [CreateProjectNode(snapshotStableKey, projectStableKey, "Shared")],
                [],
                [],
                [fanIn, fanOut, depth, transitive, cycle]);
            HotspotDetector detector = new();

            IReadOnlyList<HotspotRecord> hotspots = detector.DetectHotspots(snapshot, HotspotThresholds.Default);

            Assert.Contains(hotspots, hotspot => hotspot.Category == HotspotCategories.HighFanIn && hotspot.ContributingMetricStableKeys.Contains(fanIn.StableKey));
            Assert.Contains(hotspots, hotspot => hotspot.Category == HotspotCategories.HighFanOut && hotspot.ContributingMetricStableKeys.Contains(fanOut.StableKey));
            Assert.Contains(hotspots, hotspot => hotspot.Category == HotspotCategories.SharedLibrary && hotspot.ContributingMetricStableKeys.Contains(fanIn.StableKey));
            Assert.Contains(hotspots, hotspot => hotspot.Category == HotspotCategories.DependencyDepth && hotspot.ContributingMetricStableKeys.Contains(depth.StableKey));
            Assert.Contains(hotspots, hotspot => hotspot.Category == HotspotCategories.TransitiveDependencyCount && hotspot.ContributingMetricStableKeys.Contains(transitive.StableKey));
            HotspotRecord cycleHotspot = Assert.Single(hotspots, hotspot => hotspot.Category == HotspotCategories.CycleParticipation);
            Assert.Equal(projectStableKey, cycleHotspot.TargetStableKey);
            Assert.Equal("Project", cycleHotspot.TargetKind);
            Assert.Equal("Shared", cycleHotspot.DisplayName);
            Assert.Equal(2, cycleHotspot.Score);
            Assert.StartsWith("hotspot://snapshot://hotspots-graph/CycleParticipation/", cycleHotspot.StableKey.Value, StringComparison.Ordinal);
            Assert.StartsWith("sha256:", cycleHotspot.Fingerprint.Value, StringComparison.Ordinal);
        }

        /// <summary>
        /// Confirms modernization metrics and concentrated findings produce explainable hotspot records with finding and evidence contributions.
        /// </summary>
        [Fact]
        public void DetectHotspots_WhenModernizationMetricsAndFindingsAreConcentrated_ShouldReturnModernizationAndFindingHotspots()
        {
            // Modernization metrics are snapshot-scoped, while finding concentration is grouped by affected architecture node.
            StableKey snapshotStableKey = new("snapshot://hotspots-modernization");
            StableKey projectStableKey = new("project://src/Legacy/Legacy.csproj");
            MetricRecord dataAccessSpread = CreateMetric(snapshotStableKey, MetricDefinitions.ModernizationDataAccessSpread.Kind, 4, MetricScopeKind.Snapshot, null, "projects");
            MetricRecord sharedTableUsage = CreateMetric(snapshotStableKey, MetricDefinitions.ModernizationSharedTableUsageCount.Kind, 3, MetricScopeKind.Snapshot, null, "tables");
            FindingRecord firstFinding = CreateFinding(snapshotStableKey, "finding://legacy/1", projectStableKey, "evidence://legacy/1", FindingSeverity.High, 0.8m);
            FindingRecord secondFinding = CreateFinding(snapshotStableKey, "finding://legacy/2", projectStableKey, "evidence://legacy/2", FindingSeverity.Critical, 0.9m);
            FindingRecord thirdFinding = CreateFinding(snapshotStableKey, "finding://legacy/3", projectStableKey, "evidence://legacy/3", FindingSeverity.Medium, 0.7m);
            ExtractedArchitectureSnapshot snapshot = CreateSnapshot(
                snapshotStableKey,
                [CreateProjectNode(snapshotStableKey, projectStableKey, "Legacy")],
                [],
                [firstFinding, secondFinding, thirdFinding],
                [dataAccessSpread, sharedTableUsage]);
            HotspotDetector detector = new();

            IReadOnlyList<HotspotRecord> hotspots = detector.DetectHotspots(snapshot, HotspotThresholds.Default);

            HotspotRecord dataHotspot = Assert.Single(hotspots, hotspot => hotspot.Category == HotspotCategories.DataAccessSpread);
            Assert.Equal(snapshotStableKey, dataHotspot.TargetStableKey);
            Assert.Contains(dataAccessSpread.StableKey, dataHotspot.ContributingMetricStableKeys);
            HotspotRecord tableHotspot = Assert.Single(hotspots, hotspot => hotspot.Category == HotspotCategories.SharedTableUsage);
            Assert.Equal(snapshotStableKey, tableHotspot.TargetStableKey);
            Assert.Contains(sharedTableUsage.StableKey, tableHotspot.ContributingMetricStableKeys);
            HotspotRecord findingHotspot = Assert.Single(hotspots, hotspot => hotspot.Category == HotspotCategories.HotlistFindingConcentration);
            Assert.Equal(projectStableKey, findingHotspot.TargetStableKey);
            Assert.Equal(3, findingHotspot.ContributingFindingStableKeys.Count);
            Assert.Equal(3, findingHotspot.EvidenceStableKeys.Count);
            Assert.Equal(0.7m, findingHotspot.Confidence.Value);
        }

        /// <summary>
        /// Confirms custom thresholds and stable tie-breaking control which hotspots are returned and how equal scores are ranked.
        /// </summary>
        [Fact]
        public void DetectHotspots_WhenThresholdsAndTiesApply_ShouldFilterAndRankDeterministically()
        {
            // The custom threshold suppresses fan-in score five while equal fan-out scores prove stable target-key tie ordering.
            StableKey snapshotStableKey = new("snapshot://hotspots-ranking");
            StableKey alphaStableKey = new("project://src/Alpha/Alpha.csproj");
            StableKey betaStableKey = new("project://src/Beta/Beta.csproj");
            MetricRecord suppressedFanIn = CreateMetric(snapshotStableKey, MetricDefinitions.GraphFanIn.Kind, 5, MetricScopeKind.Node, alphaStableKey, "edges");
            MetricRecord alphaFanOut = CreateMetric(snapshotStableKey, MetricDefinitions.GraphFanOut.Kind, 6, MetricScopeKind.Node, alphaStableKey, "edges");
            MetricRecord betaFanOut = CreateMetric(snapshotStableKey, MetricDefinitions.GraphFanOut.Kind, 6, MetricScopeKind.Node, betaStableKey, "edges");
            ExtractedArchitectureSnapshot snapshot = CreateSnapshot(
                snapshotStableKey,
                [CreateProjectNode(snapshotStableKey, alphaStableKey, "Alpha"), CreateProjectNode(snapshotStableKey, betaStableKey, "Beta")],
                [],
                [],
                [suppressedFanIn, alphaFanOut, betaFanOut]);
            HotspotThresholds thresholds = HotspotThresholds.Default with
            {
                HighFanIn = 6,
                HighFanOut = 6
            };
            HotspotDetector detector = new();

            IReadOnlyList<HotspotRecord> hotspots = detector.DetectHotspots(snapshot, thresholds);
            HotspotRecord[] fanOutHotspots = hotspots.Where(hotspot => hotspot.Category == HotspotCategories.HighFanOut).ToArray();

            Assert.DoesNotContain(hotspots, hotspot => hotspot.Category == HotspotCategories.HighFanIn);
            Assert.Equal([alphaStableKey, betaStableKey], fanOutHotspots.Select(hotspot => hotspot.TargetStableKey).ToArray());
            Assert.Equal([1, 2], fanOutHotspots.Select(hotspot => hotspot.Rank).ToArray());
        }

        /// <summary>
        /// Creates a deterministic extracted snapshot fixture for hotspot tests.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot to create.</param>
        /// <param name="nodes">The architecture nodes available for display-name lookup.</param>
        /// <param name="edges">The architecture edges available for future evidence aggregation scenarios.</param>
        /// <param name="findings">The findings that may contribute to hotspot scores.</param>
        /// <param name="metrics">The metrics that may contribute to hotspot scores.</param>
        /// <returns>An extracted architecture snapshot suitable for hotspot detector tests.</returns>
        private static ExtractedArchitectureSnapshot CreateSnapshot(StableKey snapshotStableKey, IReadOnlyList<ArchitectureNode> nodes, IReadOnlyList<ArchitectureEdge> edges, IReadOnlyList<FindingRecord> findings, IReadOnlyList<MetricRecord> metrics)
        {
            // Hotspot tests require a real header because hotspot stable keys are snapshot-scoped public identities.
            StableKey repositoryStableKey = new("repository://hotspots");
            SnapshotHeader header = new(snapshotStableKey, repositoryStableKey, "main", "abcdef", new DateTimeOffset(2026, 5, 20, 8, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 5, 20, 8, 1, 0, TimeSpan.Zero), "wp013-hotspot-tests", "Completed", warnings: [], errors: [], GraphMetadata.Empty);
            RepositoryModel repository = new(repositoryStableKey, "Hotspots", "D:/Repositories/Hotspots", null, "main", GraphMetadata.Empty);
            return new ExtractedArchitectureSnapshot(header, [repository], [], nodes, edges, [], [], findings, metrics, [], [], []);
        }

        /// <summary>
        /// Creates a project architecture node fixture with deterministic stable identity and display text.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that owns the node.</param>
        /// <param name="nodeStableKey">The stable key that identifies the node.</param>
        /// <param name="displayName">The display name shown in hotspot output.</param>
        /// <returns>A validated project node fixture.</returns>
        private static ArchitectureNode CreateProjectNode(StableKey snapshotStableKey, StableKey nodeStableKey, string displayName)
        {
            // Project nodes provide display names for hotspot DTOs without depending on extraction stages in unit tests.
            return new ArchitectureNode(snapshotStableKey, nodeStableKey, NodeKind.Project, displayName, displayName, displayName.ToLowerInvariant(), "C#", projectStableKey: null, parentNodeStableKey: null, KnowledgeKind.Fact, ownership: null, externalCategory: null, Confidence.Certain, UnknownState.Known, primaryEvidenceStableKey: null, GraphMetadata.Empty, FingerprintGenerator.ForNode(NodeKind.Project, displayName, displayName, displayName.ToLowerInvariant(), KnowledgeKind.Fact, GraphMetadata.Empty));
        }

        /// <summary>
        /// Creates a metric fixture for hotspot scoring tests.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the metric.</param>
        /// <param name="metricKind">The metric kind used by hotspot category mapping.</param>
        /// <param name="numericValue">The numeric value compared with hotspot thresholds.</param>
        /// <param name="scopeKind">The metric scope kind.</param>
        /// <param name="nodeStableKey">The optional node stable key targeted by node or project metrics.</param>
        /// <param name="unit">The display unit for the metric value.</param>
        /// <returns>A validated metric record fixture.</returns>
        private static MetricRecord CreateMetric(StableKey snapshotStableKey, string metricKind, decimal numericValue, MetricScopeKind scopeKind, StableKey? nodeStableKey, string unit)
        {
            // Stable metric keys include the metric kind and target so hotspot contribution lists can be asserted exactly.
            string targetPart = nodeStableKey?.Value ?? scopeKind.Value;
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["testMetricKind"] = metricKind
            });
            return new MetricRecord(snapshotStableKey, new StableKey($"metric://{snapshotStableKey.Value}/{metricKind}/{targetPart}"), metricKind, scopeKind, nodeStableKey, edgeStableKey: null, primaryEvidenceStableKey: null, metricKind, numericValue, textValue: null, unit, Confidence.Certain, UnknownState.Known, metadata, FingerprintGenerator.ForMetric(metricKind, scopeKind, targetPart, numericValue, null, unit, false, null, metadata));
        }

        /// <summary>
        /// Creates a finding fixture that contributes to hotlist finding concentration hotspots.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the finding.</param>
        /// <param name="stableKey">The stable key of the finding.</param>
        /// <param name="nodeStableKey">The affected node stable key.</param>
        /// <param name="evidenceStableKey">The evidence stable key explaining the finding.</param>
        /// <param name="severity">The severity assigned to the finding.</param>
        /// <param name="confidence">The confidence assigned to the finding.</param>
        /// <returns>A validated finding record fixture.</returns>
        private static FindingRecord CreateFinding(StableKey snapshotStableKey, string stableKey, StableKey nodeStableKey, string evidenceStableKey, FindingSeverity severity, decimal confidence)
        {
            // Findings use affected-node and evidence lists because hotspot output must preserve contribution references for triage.
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["projectStableKey"] = nodeStableKey.Value
            });
            return new FindingRecord(snapshotStableKey, new StableKey(stableKey), "ARCHON-HOTSPOT-TEST", "1.0.0", severity, FindingStatus.Open, "Hotspot finding", "A finding that contributes to hotspot concentration.", KnowledgeKind.Inference, new Confidence(confidence), UnknownState.Known, nodeStableKey, new StableKey(evidenceStableKey), snapshotStableKey, snapshotStableKey, suppressionReason: null, suppressedBy: null, [nodeStableKey], [new StableKey(evidenceStableKey)], stableKey, metadata, FingerprintGenerator.ForFinding("ARCHON-HOTSPOT-TEST", "1.0.0", severity, FindingStatus.Open, "Hotspot finding", KnowledgeKind.Inference, metadata));
        }
    }
}
