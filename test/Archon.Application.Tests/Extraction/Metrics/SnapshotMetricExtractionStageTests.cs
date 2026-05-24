using Archon.Application.Extraction.Accumulation;
using Archon.Application.Extraction.Metrics;
using Archon.Application.Extraction.Pipeline;
using Archon.Application.Extraction.Requests;
using Archon.Application.Extraction.Resolution;
using Archon.Application.Extraction.Runs;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Metrics;
using Archon.Domain.Graph.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Archon.Application.Tests.Extraction.Metrics
{
    /// <summary>
    /// Verifies the WP013 snapshot metric extraction stage calculates deterministic metrics from accumulated graph facts.
    /// </summary>
    public sealed class SnapshotMetricExtractionStageTests
    {
        /// <summary>
        /// Verifies the stage contributes a snapshot-owned node-count metric with deterministic identity and fingerprint content.
        /// </summary>
        /// <returns>A task that completes after the stage result and accumulated metric are asserted.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenSnapshotContainsNodes_ShouldAddDeterministicSnapshotNodeCountMetric()
        {
            // The fixture contributes a snapshot header and two nodes before the metric stage runs, matching the intended pipeline ordering.
            ArchitectureSnapshotAccumulator accumulator = new();
            StableKey snapshotStableKey = new("snapshot://metrics-one");
            accumulator.SetSnapshotHeader(CreateSnapshotHeader(snapshotStableKey));
            accumulator.AddNode(CreateNode(snapshotStableKey, "project://metrics-one/api", "Metrics.Api"));
            accumulator.AddNode(CreateNode(snapshotStableKey, "project://metrics-one/application", "Metrics.Application"));
            SnapshotMetricExtractionStage stage = new(NullLogger<SnapshotMetricExtractionStage>.Instance);

            ExtractionStageResult result = await stage.ExecuteAsync(CreateContext(accumulator), CancellationToken.None);
            MetricRecord metric = Assert.Single(accumulator.ToSnapshot().Metrics, static metric => StringComparer.Ordinal.Equals(metric.MetricKind, "SnapshotNodeCount"));

            Assert.False(result.HasBlockingError);
            Assert.Equal("WP013.SnapshotMetrics", stage.StageId);
            Assert.Equal("metric://snapshot://metrics-one/SnapshotNodeCount/Snapshot", metric.StableKey.Value);
            Assert.Equal(MetricDefinitions.SnapshotNodeCount.Kind, metric.MetricKind);
            Assert.Equal(MetricScopeKind.Snapshot, metric.ScopeKind);
            Assert.Equal(2, metric.NumericValue);
            Assert.Null(metric.TextValue);
            Assert.Equal("nodes", metric.Unit);
            Assert.Equal(Confidence.Certain, metric.Confidence);
            Assert.False(metric.UnknownState.HasUnknownData);
            Assert.StartsWith("sha256:", metric.Fingerprint.Value, StringComparison.Ordinal);
        }

        /// <summary>
        /// Verifies equivalent accumulated metric input produces identical metric stable keys and fingerprints.
        /// </summary>
        /// <returns>A task that completes after both stage executions are compared.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenInputIsEquivalent_ShouldProduceDeterministicFingerprint()
        {
            // Determinism is tested by creating two independent accumulators with equivalent snapshot identities and node counts.
            SnapshotMetricExtractionStage stage = new(NullLogger<SnapshotMetricExtractionStage>.Instance);
            ArchitectureSnapshotAccumulator firstAccumulator = CreateAccumulatorWithNodeCount("snapshot://repeatable", 1);
            ArchitectureSnapshotAccumulator secondAccumulator = CreateAccumulatorWithNodeCount("snapshot://repeatable", 1);

            await stage.ExecuteAsync(CreateContext(firstAccumulator), CancellationToken.None);
            await stage.ExecuteAsync(CreateContext(secondAccumulator), CancellationToken.None);
            MetricRecord firstMetric = Assert.Single(firstAccumulator.ToSnapshot().Metrics, static metric => StringComparer.Ordinal.Equals(metric.MetricKind, "SnapshotNodeCount"));
            MetricRecord secondMetric = Assert.Single(secondAccumulator.ToSnapshot().Metrics, static metric => StringComparer.Ordinal.Equals(metric.MetricKind, "SnapshotNodeCount"));

            Assert.Equal(firstMetric.StableKey, secondMetric.StableKey);
            Assert.Equal(firstMetric.Fingerprint, secondMetric.Fingerprint);
        }

        /// <summary>
        /// Verifies missing snapshot identity derives the run snapshot identity and preserves explicit unknown-state context.
        /// </summary>
        /// <returns>A task that completes after the non-blocking unknown-state metric is asserted.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenSnapshotHeaderIsMissing_ShouldDeriveSnapshotIdentityAndReturnUnknownStateMetric()
        {
            // Pipeline execution calculates metrics before final assembly; the stage derives the final snapshot identity and records input uncertainty.
            ArchitectureSnapshotAccumulator accumulator = new();
            SnapshotMetricExtractionStage stage = new(NullLogger<SnapshotMetricExtractionStage>.Instance);

            ExtractionStageResult result = await stage.ExecuteAsync(CreateContext(accumulator), CancellationToken.None);
            MetricRecord metric = Assert.Single(accumulator.ToSnapshot().Metrics, static metric => StringComparer.Ordinal.Equals(metric.MetricKind, "SnapshotNodeCount"));

            Assert.False(result.HasBlockingError);
            Assert.StartsWith("metric://summary://repository://d:/repositories/metricssuite/ExtractionRun/", metric.StableKey.Value, StringComparison.Ordinal);
            Assert.True(metric.UnknownState.HasUnknownData);
            Assert.Equal("Snapshot metric calculation could not verify the snapshot header before counting nodes.", metric.UnknownState.UnknownReason);
        }

        /// <summary>
        /// Verifies project-scoped metrics are calculated from accumulated nodes, edges, metadata, and findings without source rescans.
        /// </summary>
        /// <returns>A task that completes after representative project metrics are asserted.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenProjectFactsExist_ShouldAddProjectScopedMetrics()
        {
            // The fixture models a small project graph so every required Work Item 2 metric has deterministic accumulated input.
            StableKey snapshotStableKey = new("snapshot://project-metrics");
            StableKey apiProjectKey = new("project://src/Metrics.Api/Metrics.Api.csproj");
            StableKey domainProjectKey = new("project://src/Metrics.Domain/Metrics.Domain.csproj");
            ArchitectureSnapshotAccumulator accumulator = new();
            accumulator.SetSnapshotHeader(CreateSnapshotHeader(snapshotStableKey));
            accumulator.AddNode(CreateProjectNode(snapshotStableKey, apiProjectKey, "Metrics.Api", "net8.0", packageReferenceCount: 3));
            accumulator.AddNode(CreateProjectNode(snapshotStableKey, domainProjectKey, "Metrics.Domain", "net10.0", packageReferenceCount: 1));
            accumulator.AddNode(CreateOwnedNode(snapshotStableKey, "type://Metrics.Api.PublicController", NodeKind.Type, apiProjectKey, GraphMetadata.From(new Dictionary<string, object?>(StringComparer.Ordinal) { ["semantic.accessibility"] = "Public" })));
            accumulator.AddNode(CreateOwnedNode(snapshotStableKey, "endpoint://Metrics.Api/GET/customers", NodeKind.Endpoint, apiProjectKey, GraphMetadata.Empty));
            accumulator.AddNode(CreateOwnedNode(snapshotStableKey, "dbcontext://Metrics.Api/AppDbContext", NodeKind.DbContext, apiProjectKey, GraphMetadata.Empty));
            accumulator.AddEdge(CreateEdge(snapshotStableKey, "edge://api-domain", EdgeKind.References, apiProjectKey, domainProjectKey));
            accumulator.AddFinding(CreateFinding(snapshotStableKey, apiProjectKey));
            SnapshotMetricExtractionStage stage = new(NullLogger<SnapshotMetricExtractionStage>.Instance);

            ExtractionStageResult result = await stage.ExecuteAsync(CreateContext(accumulator), CancellationToken.None);
            MetricRecord[] projectMetrics = accumulator.ToSnapshot().Metrics
                .Where(static metric => metric.ScopeKind == MetricScopeKind.Project)
                .OrderBy(static metric => metric.MetricKind, StringComparer.Ordinal)
                .ThenBy(static metric => metric.NodeStableKey?.Value ?? string.Empty, StringComparer.Ordinal)
                .ToArray();

            Assert.False(result.HasBlockingError);
            Assert.Equal(28, projectMetrics.Length);
            AssertProjectMetric(projectMetrics, apiProjectKey, MetricDefinitions.ProjectOutgoingReferenceCount.Kind, 1, null);
            AssertProjectMetric(projectMetrics, domainProjectKey, MetricDefinitions.ProjectIncomingReferenceCount.Kind, 1, null);
            AssertProjectMetric(projectMetrics, apiProjectKey, MetricDefinitions.ProjectPackageCount.Kind, 3, null);
            AssertProjectMetric(projectMetrics, apiProjectKey, MetricDefinitions.ProjectPublicTypeCount.Kind, 1, null);
            AssertProjectMetric(projectMetrics, apiProjectKey, MetricDefinitions.ProjectEndpointCount.Kind, 1, null);
            AssertProjectMetric(projectMetrics, apiProjectKey, MetricDefinitions.ProjectDataAccessCount.Kind, 1, null);
            AssertProjectMetric(projectMetrics, apiProjectKey, MetricDefinitions.ProjectHotlistFindingCount.Kind, 1, null);
            AssertProjectMetric(projectMetrics, apiProjectKey, MetricDefinitions.ProjectTargetFrameworkRisk.Kind, 1, "Supported");
            AssertProjectMetric(projectMetrics, domainProjectKey, MetricDefinitions.ProjectTargetFrameworkRisk.Kind, 0, "Current");
        }

        /// <summary>
        /// Verifies missing target-framework metadata creates an explicit unknown-state project metric instead of omitting required output.
        /// </summary>
        /// <returns>A task that completes after the target-framework unknown metric is asserted.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenProjectTargetFrameworkIsMissing_ShouldAddUnknownTargetFrameworkRiskMetric()
        {
            // Required project metrics remain present even when target-framework inputs are incomplete, making missing data queryable.
            StableKey snapshotStableKey = new("snapshot://project-metrics-unknown");
            StableKey projectKey = new("project://src/Unknown/Unknown.csproj");
            ArchitectureSnapshotAccumulator accumulator = new();
            accumulator.SetSnapshotHeader(CreateSnapshotHeader(snapshotStableKey));
            accumulator.AddNode(CreateProjectNode(snapshotStableKey, projectKey, "Unknown", targetFramework: null, packageReferenceCount: 0));
            SnapshotMetricExtractionStage stage = new(NullLogger<SnapshotMetricExtractionStage>.Instance);

            await stage.ExecuteAsync(CreateContext(accumulator), CancellationToken.None);
            MetricRecord metric = accumulator.ToSnapshot().Metrics.Single(metric =>
                metric.ScopeKind == MetricScopeKind.Project &&
                metric.NodeStableKey == projectKey &&
                StringComparer.Ordinal.Equals(metric.MetricKind, MetricDefinitions.ProjectTargetFrameworkRisk.Kind));

            Assert.Equal("Unknown", metric.TextValue);
            Assert.True(metric.UnknownState.HasUnknownData);
            Assert.Equal("Project target framework metadata was unavailable for metric calculation.", metric.UnknownState.UnknownReason);
        }

        /// <summary>
        /// Verifies graph-structure metrics are calculated from deterministic dependency adjacency rather than non-dependency support relationships.
        /// </summary>
        /// <returns>A task that completes after graph metric values and metadata are asserted.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenDependencyGraphExists_ShouldAddGraphScopedMetrics()
        {
            // The fixture creates a small dependency graph with one non-dependency containment edge to prove traversal filtering is explicit.
            StableKey snapshotStableKey = new("snapshot://graph-metrics");
            StableKey apiNodeKey = new("project://src/Graph.Api/Graph.Api.csproj");
            StableKey appNodeKey = new("project://src/Graph.Application/Graph.Application.csproj");
            StableKey domainNodeKey = new("project://src/Graph.Domain/Graph.Domain.csproj");
            StableKey packageNodeKey = new("package://Newtonsoft.Json/13.0.3");
            ArchitectureSnapshotAccumulator accumulator = new();
            accumulator.SetSnapshotHeader(CreateSnapshotHeader(snapshotStableKey));
            accumulator.AddNode(CreateProjectNode(snapshotStableKey, apiNodeKey, "Graph.Api", "net10.0", packageReferenceCount: 0));
            accumulator.AddNode(CreateProjectNode(snapshotStableKey, appNodeKey, "Graph.Application", "net10.0", packageReferenceCount: 0));
            accumulator.AddNode(CreateProjectNode(snapshotStableKey, domainNodeKey, "Graph.Domain", "net10.0", packageReferenceCount: 0));
            accumulator.AddNode(CreatePackageNode(snapshotStableKey, packageNodeKey, "Newtonsoft.Json"));
            accumulator.AddEdge(CreateEdge(snapshotStableKey, "edge://graph/api-app", EdgeKind.References, apiNodeKey, appNodeKey));
            accumulator.AddEdge(CreateEdge(snapshotStableKey, "edge://graph/api-package", EdgeKind.UsesPackage, apiNodeKey, packageNodeKey));
            accumulator.AddEdge(CreateEdge(snapshotStableKey, "edge://graph/app-domain", EdgeKind.References, appNodeKey, domainNodeKey));
            accumulator.AddEdge(CreateEdge(snapshotStableKey, "edge://graph/domain-api", EdgeKind.References, domainNodeKey, apiNodeKey));
            accumulator.AddEdge(CreateEdge(snapshotStableKey, "edge://graph/container", EdgeKind.Contains, apiNodeKey, domainNodeKey));
            SnapshotMetricExtractionStage stage = new(NullLogger<SnapshotMetricExtractionStage>.Instance);

            ExtractionStageResult result = await stage.ExecuteAsync(CreateContext(accumulator), CancellationToken.None);
            MetricRecord[] nodeMetrics = accumulator.ToSnapshot().Metrics
                .Where(static metric => metric.ScopeKind == MetricScopeKind.Node)
                .ToArray();

            Assert.False(result.HasBlockingError);
            Assert.Equal(28, nodeMetrics.Length);
            AssertGraphMetric(nodeMetrics, apiNodeKey, MetricDefinitions.GraphFanOut.Kind, 2, null);
            AssertGraphMetric(nodeMetrics, apiNodeKey, MetricDefinitions.GraphFanIn.Kind, 1, null);
            AssertGraphMetric(nodeMetrics, apiNodeKey, MetricDefinitions.GraphDegreeCentrality.Kind, 0.5m, null);
            AssertGraphMetric(nodeMetrics, apiNodeKey, MetricDefinitions.GraphDependencyDepth.Kind, 3, null);
            AssertGraphMetric(nodeMetrics, apiNodeKey, MetricDefinitions.GraphTransitiveDependencyCount.Kind, 4, null);
            AssertGraphMetric(nodeMetrics, apiNodeKey, MetricDefinitions.GraphNeighbourhoodSize.Kind, 3, null);
            AssertGraphMetric(nodeMetrics, apiNodeKey, MetricDefinitions.GraphCycleParticipation.Kind, 1, null);
            MetricRecord fanOutMetric = nodeMetrics.Single(metric => metric.NodeStableKey == apiNodeKey && StringComparer.Ordinal.Equals(metric.MetricKind, MetricDefinitions.GraphFanOut.Kind));
            Assert.Contains("dependencyEdgeKinds", fanOutMetric.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
            Assert.DoesNotContain(EdgeKind.Contains.Value, fanOutMetric.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
        }

        /// <summary>
        /// Verifies detected dependency cycles feed the graph cycle participation metric for every participating node.
        /// </summary>
        /// <returns>A task that completes after cycle participation metric records are asserted.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenDependencyCyclesExist_ShouldCalculateCycleParticipationMetrics()
        {
            // The graph has one three-project cycle and one acyclic package dependency so cycle participation is non-zero only for cycle members.
            StableKey snapshotStableKey = new("snapshot://graph-cycle-participation");
            StableKey apiNodeKey = new("project://src/Cycle.Api/Cycle.Api.csproj");
            StableKey appNodeKey = new("project://src/Cycle.Application/Cycle.Application.csproj");
            StableKey domainNodeKey = new("project://src/Cycle.Domain/Cycle.Domain.csproj");
            StableKey packageNodeKey = new("package://Serilog/4.0.0");
            ArchitectureSnapshotAccumulator accumulator = new();
            accumulator.SetSnapshotHeader(CreateSnapshotHeader(snapshotStableKey));
            accumulator.AddNode(CreateProjectNode(snapshotStableKey, apiNodeKey, "Cycle.Api", "net10.0", packageReferenceCount: 0));
            accumulator.AddNode(CreateProjectNode(snapshotStableKey, appNodeKey, "Cycle.Application", "net10.0", packageReferenceCount: 0));
            accumulator.AddNode(CreateProjectNode(snapshotStableKey, domainNodeKey, "Cycle.Domain", "net10.0", packageReferenceCount: 0));
            accumulator.AddNode(CreatePackageNode(snapshotStableKey, packageNodeKey, "Serilog"));
            accumulator.AddEdge(CreateEdge(snapshotStableKey, "edge://cycle/api-app", EdgeKind.References, apiNodeKey, appNodeKey));
            accumulator.AddEdge(CreateEdge(snapshotStableKey, "edge://cycle/app-domain", EdgeKind.References, appNodeKey, domainNodeKey));
            accumulator.AddEdge(CreateEdge(snapshotStableKey, "edge://cycle/domain-api", EdgeKind.References, domainNodeKey, apiNodeKey));
            accumulator.AddEdge(CreateEdge(snapshotStableKey, "edge://cycle/api-package", EdgeKind.UsesPackage, apiNodeKey, packageNodeKey));
            SnapshotMetricExtractionStage stage = new(NullLogger<SnapshotMetricExtractionStage>.Instance);

            await stage.ExecuteAsync(CreateContext(accumulator), CancellationToken.None);
            MetricRecord[] metrics = accumulator.ToSnapshot().Metrics.ToArray();

            AssertGraphMetric(metrics, apiNodeKey, MetricDefinitions.GraphCycleParticipation.Kind, 1, null);
            AssertGraphMetric(metrics, appNodeKey, MetricDefinitions.GraphCycleParticipation.Kind, 1, null);
            AssertGraphMetric(metrics, domainNodeKey, MetricDefinitions.GraphCycleParticipation.Kind, 1, null);
            AssertGraphMetric(metrics, packageNodeKey, MetricDefinitions.GraphCycleParticipation.Kind, 0, null);
            MetricRecord cycleMetric = metrics.Single(metric => metric.NodeStableKey == apiNodeKey && StringComparer.Ordinal.Equals(metric.MetricKind, MetricDefinitions.GraphCycleParticipation.Kind));
            Assert.False(cycleMetric.UnknownState.HasUnknownData);
            Assert.Contains("cycleStableKeys", cycleMetric.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
        }

        /// <summary>
        /// Verifies bounded graph traversal records explicit truncation metadata and unknown-state context when the depth limit is reached.
        /// </summary>
        /// <returns>A task that completes after traversal-limit metric metadata is asserted.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenDependencyTraversalExceedsLimit_ShouldMarkDepthAndTransitiveMetricsUnknown()
        {
            // The chain is longer than the stage traversal limit, so traversal remains deterministic while reporting that the full graph was not explored.
            StableKey snapshotStableKey = new("snapshot://graph-limit");
            ArchitectureSnapshotAccumulator accumulator = new();
            accumulator.SetSnapshotHeader(CreateSnapshotHeader(snapshotStableKey));
            StableKey[] nodeKeys = Enumerable.Range(0, 18)
                .Select(index => new StableKey($"project://src/Limit{index}/Limit{index}.csproj"))
                .ToArray();
            foreach (StableKey nodeKey in nodeKeys)
            {
                accumulator.AddNode(CreateProjectNode(snapshotStableKey, nodeKey, nodeKey.Value[(nodeKey.Value.LastIndexOf('/') + 1)..], "net10.0", packageReferenceCount: 0));
            }

            for (int index = 0; index < nodeKeys.Length - 1; index++)
            {
                accumulator.AddEdge(CreateEdge(snapshotStableKey, $"edge://limit/{index}", EdgeKind.References, nodeKeys[index], nodeKeys[index + 1]));
            }

            SnapshotMetricExtractionStage stage = new(NullLogger<SnapshotMetricExtractionStage>.Instance);

            await stage.ExecuteAsync(CreateContext(accumulator), CancellationToken.None);
            MetricRecord depthMetric = accumulator.ToSnapshot().Metrics.Single(metric =>
                metric.NodeStableKey == nodeKeys[0] &&
                StringComparer.Ordinal.Equals(metric.MetricKind, MetricDefinitions.GraphDependencyDepth.Kind));
            MetricRecord transitiveMetric = accumulator.ToSnapshot().Metrics.Single(metric =>
                metric.NodeStableKey == nodeKeys[0] &&
                StringComparer.Ordinal.Equals(metric.MetricKind, MetricDefinitions.GraphTransitiveDependencyCount.Kind));

            Assert.Equal(12, depthMetric.NumericValue);
            Assert.True(depthMetric.UnknownState.HasUnknownData);
            Assert.Equal("Graph dependency traversal reached the configured depth limit before all reachable dependencies were explored.", depthMetric.UnknownState.UnknownReason);
            Assert.Contains("\"truncated\":true", depthMetric.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
            Assert.Equal(12, transitiveMetric.NumericValue);
            Assert.True(transitiveMetric.UnknownState.HasUnknownData);
        }

        /// <summary>
        /// Verifies modernization metrics are calculated from deterministic accumulated facts without source rescans or AI inference.
        /// </summary>
        /// <returns>A task that completes after snapshot and project modernization metrics are asserted.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenModernizationFactsExist_ShouldAddModernizationMetrics()
        {
            // The fixture includes legacy runtime, target-framework, package, finding, and data-access facts so each Work Item 4 metric has evidence-backed input.
            StableKey snapshotStableKey = new("snapshot://modernization-metrics");
            StableKey repositoryKey = new("repository://modernization-suite");
            StableKey solutionKey = new("solution://modernization-suite/Modernization.sln");
            StableKey legacyProjectKey = new("project://src/LegacyWeb/LegacyWeb.csproj");
            StableKey dataProjectKey = new("project://src/DataAccess/DataAccess.csproj");
            ArchitectureSnapshotAccumulator accumulator = new();
            accumulator.SetSnapshotHeader(CreateSnapshotHeader(snapshotStableKey));
            accumulator.AddNode(CreateRollupNode(snapshotStableKey, repositoryKey, NodeKind.Repository, "ModernizationSuite", null));
            accumulator.AddNode(CreateRollupNode(snapshotStableKey, solutionKey, NodeKind.Solution, "Modernization.sln", repositoryKey));
            accumulator.AddNode(CreateModernizationProjectNode(snapshotStableKey, legacyProjectKey, "LegacyWeb", "net48", packageReferenceCount: 2, solutionKey, isOldStyle: true, runtimeKind: "ClassicAspNetApplication"));
            accumulator.AddNode(CreateModernizationProjectNode(snapshotStableKey, dataProjectKey, "DataAccess", "net10.0", packageReferenceCount: 1, solutionKey, isOldStyle: false, runtimeKind: null));
            accumulator.AddNode(CreateOwnedNode(snapshotStableKey, "dbcontext://modernization/LegacyContext", NodeKind.LinqToSqlDataContext, legacyProjectKey, GraphMetadata.From(new Dictionary<string, object?>(StringComparer.Ordinal) { ["dataAccessTechnology"] = "LinqToSql", ["contextType"] = "LegacyContext" })));
            accumulator.AddNode(CreateOwnedNode(snapshotStableKey, "table://modernization/Legacy/Orders", NodeKind.DatabaseTable, legacyProjectKey, GraphMetadata.From(new Dictionary<string, object?>(StringComparer.Ordinal) { ["schemaName"] = "dbo", ["tableName"] = "Orders" })));
            accumulator.AddNode(CreateOwnedNode(snapshotStableKey, "table://modernization/Data/Orders", NodeKind.DatabaseTable, dataProjectKey, GraphMetadata.From(new Dictionary<string, object?>(StringComparer.Ordinal) { ["schemaName"] = "dbo", ["tableName"] = "Orders" })));
            accumulator.AddFinding(CreateFinding(snapshotStableKey, legacyProjectKey, "ARCHON-SECURITY-001", FindingSeverity.High, GraphMetadata.From(new Dictionary<string, object?>(StringComparer.Ordinal) { ["ruleCategory"] = "SecuritySensitive" })));
            SnapshotMetricExtractionStage stage = new(NullLogger<SnapshotMetricExtractionStage>.Instance);

            ExtractionStageResult result = await stage.ExecuteAsync(CreateContext(accumulator), CancellationToken.None);
            MetricRecord[] metrics = accumulator.ToSnapshot().Metrics.ToArray();

            Assert.False(result.HasBlockingError);
            AssertModernizationMetric(metrics, MetricScopeKind.Snapshot, null, MetricDefinitions.ModernizationLegacyTechnologyCount.Kind, 2, false);
            AssertModernizationMetric(metrics, MetricScopeKind.Snapshot, null, MetricDefinitions.ModernizationSecuritySensitiveFindingCount.Kind, 1, false);
            AssertModernizationMetric(metrics, MetricScopeKind.Snapshot, null, MetricDefinitions.ModernizationOutOfSupportTargetCount.Kind, 1, false);
            AssertModernizationMetric(metrics, MetricScopeKind.Snapshot, null, MetricDefinitions.ModernizationFrameworkOnlyDependencyCount.Kind, 2, false);
            AssertModernizationMetric(metrics, MetricScopeKind.Snapshot, null, MetricDefinitions.ModernizationDataAccessSpread.Kind, 2, false);
            AssertModernizationMetric(metrics, MetricScopeKind.Snapshot, null, MetricDefinitions.ModernizationSharedTableUsageCount.Kind, 1, false);
            AssertModernizationMetric(metrics, MetricScopeKind.Project, legacyProjectKey, MetricDefinitions.ModernizationLegacyTechnologyCount.Kind, 2, false);
            AssertModernizationMetric(metrics, MetricScopeKind.Project, dataProjectKey, MetricDefinitions.ModernizationLegacyTechnologyCount.Kind, 0, false);
            MetricRecord sharedTableMetric = metrics.Single(metric => StringComparer.Ordinal.Equals(metric.MetricKind, MetricDefinitions.ModernizationSharedTableUsageCount.Kind) && metric.ScopeKind == MetricScopeKind.Snapshot);
            Assert.Contains("sharedTableIdentities", sharedTableMetric.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
            Assert.StartsWith("metric://snapshot://modernization-metrics/ModernizationSharedTableUsageCount/", sharedTableMetric.StableKey.Value, StringComparison.Ordinal);
            Assert.StartsWith("sha256:", sharedTableMetric.Fingerprint.Value, StringComparison.Ordinal);
        }

        /// <summary>
        /// Verifies incomplete modernization inputs remain visible through unknown-state metric records.
        /// </summary>
        /// <returns>A task that completes after unknown-state modernization metrics are asserted.</returns>
        [Fact]
        public async Task ExecuteAsync_WhenModernizationInputsAreIncomplete_ShouldPreserveUnknownState()
        {
            // Missing target-framework metadata and absent data-access facts both produce explicit unknown-state outputs instead of silently dropping metrics.
            StableKey snapshotStableKey = new("snapshot://modernization-unknown");
            StableKey projectKey = new("project://src/Unknown/Unknown.csproj");
            ArchitectureSnapshotAccumulator accumulator = new();
            accumulator.SetSnapshotHeader(CreateSnapshotHeader(snapshotStableKey));
            accumulator.AddNode(CreateProjectNode(snapshotStableKey, projectKey, "Unknown", targetFramework: null, packageReferenceCount: 0));
            SnapshotMetricExtractionStage stage = new(NullLogger<SnapshotMetricExtractionStage>.Instance);

            await stage.ExecuteAsync(CreateContext(accumulator), CancellationToken.None);
            MetricRecord outOfSupportMetric = accumulator.ToSnapshot().Metrics.Single(metric =>
                metric.ScopeKind == MetricScopeKind.Project &&
                metric.NodeStableKey == projectKey &&
                StringComparer.Ordinal.Equals(metric.MetricKind, MetricDefinitions.ModernizationOutOfSupportTargetCount.Kind));
            MetricRecord sharedTableMetric = accumulator.ToSnapshot().Metrics.Single(metric =>
                metric.ScopeKind == MetricScopeKind.Snapshot &&
                StringComparer.Ordinal.Equals(metric.MetricKind, MetricDefinitions.ModernizationSharedTableUsageCount.Kind));

            Assert.Equal("Unknown", outOfSupportMetric.TextValue);
            Assert.True(outOfSupportMetric.UnknownState.HasUnknownData);
            Assert.Equal("Project target framework metadata was unavailable for modernization target support calculation.", outOfSupportMetric.UnknownState.UnknownReason);
            Assert.True(sharedTableMetric.UnknownState.HasUnknownData);
            Assert.Equal("No database table facts were available for shared table usage calculation.", sharedTableMetric.UnknownState.UnknownReason);
        }

        /// <summary>
        /// Creates an accumulator with a snapshot header and deterministic node count.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot to create.</param>
        /// <param name="nodeCount">The number of project nodes to contribute before metric calculation.</param>
        /// <returns>An accumulator containing the requested node count.</returns>
        private static ArchitectureSnapshotAccumulator CreateAccumulatorWithNodeCount(string snapshotStableKey, int nodeCount)
        {
            // The helper keeps deterministic-fixture setup local to the metric stage tests.
            StableKey stableKey = new(snapshotStableKey);
            ArchitectureSnapshotAccumulator accumulator = new();
            accumulator.SetSnapshotHeader(CreateSnapshotHeader(stableKey));
            for (int index = 0; index < nodeCount; index++)
            {
                accumulator.AddNode(CreateNode(stableKey, $"project://repeatable/{index}", $"Project {index}"));
            }

            return accumulator;
        }

        /// <summary>
        /// Asserts a project metric has the expected node target, numeric value, text value, and stable public identity shape.
        /// </summary>
        /// <param name="metrics">The metric records to search.</param>
        /// <param name="projectStableKey">The project node stable key that should own the metric.</param>
        /// <param name="metricKind">The metric kind to assert.</param>
        /// <param name="numericValue">The expected numeric metric value.</param>
        /// <param name="textValue">The expected optional text metric value.</param>
        private static void AssertProjectMetric(IReadOnlyCollection<MetricRecord> metrics, StableKey projectStableKey, string metricKind, decimal numericValue, string? textValue)
        {
            // Project metrics are node-targeted records so tests assert both the filterable target and the stable-key prefix.
            MetricRecord metric = metrics.Single(metric => metric.NodeStableKey == projectStableKey && StringComparer.Ordinal.Equals(metric.MetricKind, metricKind));
            Assert.Equal(MetricScopeKind.Project, metric.ScopeKind);
            Assert.Equal(numericValue, metric.NumericValue);
            Assert.Equal(textValue, metric.TextValue);
            Assert.StartsWith($"metric://snapshot://project-metrics/{metricKind}/", metric.StableKey.Value, StringComparison.Ordinal);
            Assert.StartsWith("sha256:", metric.Fingerprint.Value, StringComparison.Ordinal);
        }

        /// <summary>
        /// Asserts a graph metric has the expected node target, numeric value, text value, and stable public identity shape.
        /// </summary>
        /// <param name="metrics">The graph metric records to search.</param>
        /// <param name="nodeStableKey">The node stable key that should own the graph metric.</param>
        /// <param name="metricKind">The metric kind to assert.</param>
        /// <param name="numericValue">The expected optional numeric value.</param>
        /// <param name="textValue">The expected optional text value.</param>
        private static void AssertGraphMetric(IReadOnlyCollection<MetricRecord> metrics, StableKey nodeStableKey, string metricKind, decimal? numericValue, string? textValue)
        {
            // Graph metrics are node-scoped records, so the node stable key is both the API filter target and scope discriminator.
            MetricRecord metric = metrics.Single(metric => metric.NodeStableKey == nodeStableKey && StringComparer.Ordinal.Equals(metric.MetricKind, metricKind));
            Assert.Equal(MetricScopeKind.Node, metric.ScopeKind);
            Assert.Equal(numericValue, metric.NumericValue);
            Assert.Equal(textValue, metric.TextValue);
            Assert.StartsWith($"metric://{metric.SnapshotStableKey.Value}/{metricKind}/", metric.StableKey.Value, StringComparison.Ordinal);
            Assert.StartsWith("sha256:", metric.Fingerprint.Value, StringComparison.Ordinal);
        }

        /// <summary>
        /// Asserts a modernization metric has the expected scope, target, value, unknown state, and public identity shape.
        /// </summary>
        /// <param name="metrics">The metric records to search.</param>
        /// <param name="scopeKind">The expected metric scope kind.</param>
        /// <param name="nodeStableKey">The optional node stable key that should own project, solution, or repository rollups.</param>
        /// <param name="metricKind">The metric kind to assert.</param>
        /// <param name="numericValue">The expected numeric metric value.</param>
        /// <param name="hasUnknownData">Whether the metric should report incomplete source input.</param>
        private static void AssertModernizationMetric(IReadOnlyCollection<MetricRecord> metrics, MetricScopeKind scopeKind, StableKey? nodeStableKey, string metricKind, decimal numericValue, bool hasUnknownData)
        {
            // Modernization metrics reuse the generic metric API contract, so tests assert scope, target, deterministic value, and sanitized metadata at once.
            MetricRecord metric = metrics.Single(metric =>
                metric.ScopeKind == scopeKind &&
                metric.NodeStableKey == nodeStableKey &&
                StringComparer.Ordinal.Equals(metric.MetricKind, metricKind));
            Assert.Equal(numericValue, metric.NumericValue);
            Assert.Equal(hasUnknownData, metric.UnknownState.HasUnknownData);
            Assert.Contains("calculationSource", metric.Metadata.ToCanonicalJson(), StringComparison.Ordinal);
            Assert.StartsWith("metric://snapshot://modernization-metrics/", metric.StableKey.Value, StringComparison.Ordinal);
            Assert.StartsWith("sha256:", metric.Fingerprint.Value, StringComparison.Ordinal);
        }

        /// <summary>
        /// Creates a stage context around the supplied accumulator.
        /// </summary>
        /// <param name="accumulator">The accumulation model that receives metric contributions.</param>
        /// <returns>A stage context suitable for direct stage execution.</returns>
        private static ExtractionStageContext CreateContext(ArchitectureSnapshotAccumulator accumulator)
        {
            // Direct stage tests do not require filesystem access because validation has already normalized the extraction input.
            ResolvedExtractionInput input = new(
                "D:/Repositories/MetricsSuite",
                ["D:/Repositories/MetricsSuite/MetricsSuite.sln"],
                BranchName: "main",
                CommitSha: "abcdef",
                RequestedBy: "developer@example.invalid",
                Metadata: new Dictionary<string, string>());
            ExtractionRun run = new(
                ExtractionRunId.New(),
                ExtractionRunStatus.Running,
                new ExtractionRunRequestSummary(input.RepositoryRootDirectory, input.SolutionPaths, input.BranchName, input.CommitSha, input.RequestedBy, input.Metadata.Keys.ToArray()),
                new DateTimeOffset(2026, 5, 20, 8, 0, 0, TimeSpan.Zero),
                completedUtc: null,
                new ExtractionRunProgress("Pipeline", "Executing metric stage.", 50, new DateTimeOffset(2026, 5, 20, 8, 0, 0, TimeSpan.Zero)),
                warnings: null,
                errors: null,
                snapshotIdentity: null);
            return new ExtractionStageContext(input, run, accumulator);
        }

        /// <summary>
        /// Creates a deterministic snapshot header for metric stage tests.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key that identifies the snapshot.</param>
        /// <returns>A snapshot header that scopes accumulated test facts.</returns>
        private static SnapshotHeader CreateSnapshotHeader(StableKey snapshotStableKey)
        {
            // The repository key only provides required snapshot context; metrics themselves are scoped by the snapshot key.
            return new SnapshotHeader(
                snapshotStableKey,
                new StableKey("repository://metrics-suite"),
                "main",
                "abcdef",
                new DateTimeOffset(2026, 5, 20, 8, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 5, 20, 8, 1, 0, TimeSpan.Zero),
                "wp013-tests",
                "Completed",
                warnings: [],
                errors: [],
                GraphMetadata.Empty);
        }

        /// <summary>
        /// Creates a deterministic project node for metric stage tests.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the node.</param>
        /// <param name="nodeStableKey">The stable key that identifies the node.</param>
        /// <param name="displayName">The node display name.</param>
        /// <returns>An architecture node suitable for accumulation before metric calculation.</returns>
        private static ArchitectureNode CreateNode(StableKey snapshotStableKey, string nodeStableKey, string displayName)
        {
            // The first metric counts nodes only, so node evidence and project parent relationships are intentionally absent here.
            return new ArchitectureNode(
                snapshotStableKey,
                new StableKey(nodeStableKey),
                NodeKind.Project,
                displayName,
                displayName,
                displayName.ToLowerInvariant(),
                "C#",
                projectStableKey: null,
                parentNodeStableKey: null,
                KnowledgeKind.Fact,
                ownership: null,
                externalCategory: null,
                Confidence.Certain,
                UnknownState.Known,
                primaryEvidenceStableKey: null,
                GraphMetadata.Empty,
                FingerprintGenerator.ForNode(NodeKind.Project, displayName, displayName, displayName.ToLowerInvariant(), KnowledgeKind.Fact, GraphMetadata.Empty));
        }

        /// <summary>
        /// Creates a deterministic project node with project metric source metadata.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the project.</param>
        /// <param name="projectStableKey">The stable key that identifies the project node.</param>
        /// <param name="displayName">The project display name.</param>
        /// <param name="targetFramework">The optional target framework metadata value.</param>
        /// <param name="packageReferenceCount">The package reference count metadata value.</param>
        /// <returns>An architecture project node suitable for project metric calculation.</returns>
        private static ArchitectureNode CreateProjectNode(StableKey snapshotStableKey, StableKey projectStableKey, string displayName, string? targetFramework, int packageReferenceCount)
        {
            // Project metric calculation reads package and target framework values from deterministic project metadata.
            Dictionary<string, object?> metadataValues = new(StringComparer.Ordinal)
            {
                ["project.packageReferenceCount"] = packageReferenceCount
            };
            if (!string.IsNullOrWhiteSpace(targetFramework))
            {
                metadataValues["project.targetFramework"] = targetFramework;
            }
            else
            {
                metadataValues["project.targetFrameworkUnknown"] = true;
            }

            GraphMetadata metadata = GraphMetadata.From(metadataValues);
            return new ArchitectureNode(
                snapshotStableKey,
                projectStableKey,
                NodeKind.Project,
                displayName,
                displayName,
                displayName.ToLowerInvariant(),
                "C#",
                projectStableKey: null,
                parentNodeStableKey: null,
                KnowledgeKind.Fact,
                ownership: null,
                externalCategory: null,
                Confidence.Certain,
                UnknownState.Known,
                primaryEvidenceStableKey: null,
                metadata,
                FingerprintGenerator.ForNode(NodeKind.Project, displayName, displayName, displayName.ToLowerInvariant(), KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates a deterministic non-project node owned by a project for project metric rollups.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the node.</param>
        /// <param name="nodeStableKey">The stable key that identifies the owned node.</param>
        /// <param name="nodeKind">The node kind to assign.</param>
        /// <param name="projectStableKey">The project stable key that owns the node.</param>
        /// <param name="metadata">The deterministic metadata to attach.</param>
        /// <returns>An architecture node suitable for accumulated project metric input.</returns>
        private static ArchitectureNode CreateOwnedNode(StableKey snapshotStableKey, string nodeStableKey, NodeKind nodeKind, StableKey projectStableKey, GraphMetadata metadata)
        {
            // Owned nodes expose their projectStableKey so project metrics can roll them up without reparsing source files.
            string displayName = nodeStableKey[(nodeStableKey.LastIndexOf('/') + 1)..];
            return new ArchitectureNode(
                snapshotStableKey,
                new StableKey(nodeStableKey),
                nodeKind,
                displayName,
                displayName,
                displayName.ToLowerInvariant(),
                "C#",
                projectStableKey,
                parentNodeStableKey: null,
                KnowledgeKind.Fact,
                ownership: null,
                externalCategory: null,
                Confidence.Certain,
                UnknownState.Known,
                primaryEvidenceStableKey: null,
                metadata,
                FingerprintGenerator.ForNode(nodeKind, displayName, displayName, displayName.ToLowerInvariant(), KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates a deterministic package node for dependency graph metric tests.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the package node.</param>
        /// <param name="nodeStableKey">The package node stable key.</param>
        /// <param name="displayName">The package display name.</param>
        /// <returns>An architecture package node suitable for dependency traversal fixtures.</returns>
        private static ArchitectureNode CreatePackageNode(StableKey snapshotStableKey, StableKey nodeStableKey, string displayName)
        {
            // Graph metric tests include package nodes to prove dependency traversal can cross non-project architecture nodes.
            return new ArchitectureNode(
                snapshotStableKey,
                nodeStableKey,
                NodeKind.Package,
                displayName,
                displayName,
                displayName.ToLowerInvariant(),
                language: null,
                projectStableKey: null,
                parentNodeStableKey: null,
                KnowledgeKind.Fact,
                ownership: null,
                externalCategory: null,
                Confidence.Certain,
                UnknownState.Known,
                primaryEvidenceStableKey: null,
                GraphMetadata.Empty,
                FingerprintGenerator.ForNode(NodeKind.Package, displayName, displayName, displayName.ToLowerInvariant(), KnowledgeKind.Fact, GraphMetadata.Empty));
        }

        /// <summary>
        /// Creates a deterministic repository or solution node for modernization rollup tests.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the node.</param>
        /// <param name="nodeStableKey">The stable key that identifies the rollup node.</param>
        /// <param name="nodeKind">The repository or solution node kind to assign.</param>
        /// <param name="displayName">The node display name.</param>
        /// <param name="parentNodeStableKey">The optional parent node stable key.</param>
        /// <returns>An architecture node suitable for modernization rollup fixtures.</returns>
        private static ArchitectureNode CreateRollupNode(StableKey snapshotStableKey, StableKey nodeStableKey, NodeKind nodeKind, string displayName, StableKey? parentNodeStableKey)
        {
            // Rollup nodes let modernization metrics prove repository and solution scopes without adding new persistence paths.
            return new ArchitectureNode(
                snapshotStableKey,
                nodeStableKey,
                nodeKind,
                displayName,
                displayName,
                displayName.ToLowerInvariant(),
                language: null,
                projectStableKey: null,
                parentNodeStableKey,
                KnowledgeKind.Fact,
                ownership: null,
                externalCategory: null,
                Confidence.Certain,
                UnknownState.Known,
                primaryEvidenceStableKey: null,
                GraphMetadata.Empty,
                FingerprintGenerator.ForNode(nodeKind, displayName, displayName, displayName.ToLowerInvariant(), KnowledgeKind.Fact, GraphMetadata.Empty));
        }

        /// <summary>
        /// Creates a project node with modernization-specific source metadata and optional solution parentage.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the project.</param>
        /// <param name="projectStableKey">The stable key that identifies the project node.</param>
        /// <param name="displayName">The project display name.</param>
        /// <param name="targetFramework">The optional target framework metadata value.</param>
        /// <param name="packageReferenceCount">The package reference count metadata value.</param>
        /// <param name="parentNodeStableKey">The optional solution node that owns the project.</param>
        /// <param name="isOldStyle">Whether project extraction identified the project as old-style MSBuild.</param>
        /// <param name="runtimeKind">The optional runtime-kind metadata value.</param>
        /// <returns>An architecture project node suitable for modernization metric calculation.</returns>
        private static ArchitectureNode CreateModernizationProjectNode(StableKey snapshotStableKey, StableKey projectStableKey, string displayName, string? targetFramework, int packageReferenceCount, StableKey? parentNodeStableKey, bool isOldStyle, string? runtimeKind)
        {
            // Modernization tests need project metadata aliases used by real extractors while keeping all facts deterministic and local.
            Dictionary<string, object?> metadataValues = new(StringComparer.Ordinal)
            {
                ["project.name"] = displayName,
                ["project.packageReferenceCount"] = packageReferenceCount,
                ["project.isOldStyle"] = isOldStyle,
                ["project.isSdkStyle"] = !isOldStyle
            };
            if (!string.IsNullOrWhiteSpace(targetFramework))
            {
                metadataValues["project.targetFramework"] = targetFramework;
            }
            else
            {
                metadataValues["project.targetFrameworkUnknown"] = true;
            }

            if (!string.IsNullOrWhiteSpace(runtimeKind))
            {
                metadataValues["runtimeKind"] = runtimeKind;
                metadataValues["framework"] = "Classic ASP.NET";
            }

            GraphMetadata metadata = GraphMetadata.From(metadataValues);
            return new ArchitectureNode(
                snapshotStableKey,
                projectStableKey,
                NodeKind.Project,
                displayName,
                displayName,
                displayName.ToLowerInvariant(),
                "C#",
                projectStableKey: null,
                parentNodeStableKey,
                KnowledgeKind.Fact,
                ownership: null,
                externalCategory: null,
                Confidence.Certain,
                UnknownState.Known,
                primaryEvidenceStableKey: null,
                metadata,
                FingerprintGenerator.ForNode(NodeKind.Project, displayName, displayName, displayName.ToLowerInvariant(), KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates a deterministic architecture edge for project metric graph counting.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the edge.</param>
        /// <param name="edgeStableKey">The stable key that identifies the edge.</param>
        /// <param name="edgeKind">The edge kind to assign.</param>
        /// <param name="sourceNodeStableKey">The source node stable key.</param>
        /// <param name="targetNodeStableKey">The target node stable key.</param>
        /// <returns>An architecture edge suitable for accumulation before metric calculation.</returns>
        private static ArchitectureEdge CreateEdge(StableKey snapshotStableKey, string edgeStableKey, EdgeKind edgeKind, StableKey sourceNodeStableKey, StableKey targetNodeStableKey)
        {
            // Project reference metrics count direct REFERENCES edges between project nodes.
            return new ArchitectureEdge(
                snapshotStableKey,
                new StableKey(edgeStableKey),
                edgeKind,
                sourceNodeStableKey,
                targetNodeStableKey,
                isDirect: true,
                KnowledgeKind.Fact,
                Confidence.Certain,
                UnknownState.Known,
                primaryEvidenceStableKey: null,
                GraphMetadata.Empty,
                FingerprintGenerator.ForEdge(edgeKind, sourceNodeStableKey, targetNodeStableKey, true, KnowledgeKind.Fact, GraphMetadata.Empty));
        }

        /// <summary>
        /// Creates a deterministic finding targeting a project node for hotlist finding count metrics.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the finding.</param>
        /// <param name="projectStableKey">The project node stable key affected by the finding.</param>
        /// <returns>A finding record suitable for accumulated project metric input.</returns>
        private static FindingRecord CreateFinding(StableKey snapshotStableKey, StableKey projectStableKey)
        {
            // The default helper creates a generic high-severity finding for project metric rollup tests.
            return CreateFinding(snapshotStableKey, projectStableKey, "ARCHON-TEST", FindingSeverity.High, GraphMetadata.Empty);
        }

        /// <summary>
        /// Creates a deterministic finding targeting a project node with caller-selected classification metadata.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the finding.</param>
        /// <param name="projectStableKey">The project node stable key affected by the finding.</param>
        /// <param name="ruleCode">The rule code that classifies the finding.</param>
        /// <param name="severity">The controlled finding severity.</param>
        /// <param name="metadata">The deterministic finding metadata.</param>
        /// <returns>A finding record suitable for modernization metric input.</returns>
        private static FindingRecord CreateFinding(StableKey snapshotStableKey, StableKey projectStableKey, string ruleCode, FindingSeverity severity, GraphMetadata metadata)
        {
            // The metric counts primary and affected node references so the fixture populates both fields consistently.
            StableKey findingStableKey = new($"finding://{projectStableKey.Value}/{ruleCode}");
            return new FindingRecord(
                snapshotStableKey,
                findingStableKey,
                ruleCode,
                "1.0.0",
                severity,
                FindingStatus.Open,
                "Test finding",
                "Test finding for project metric rollup.",
                KnowledgeKind.Fact,
                Confidence.Certain,
                primaryNodeStableKey: projectStableKey,
                primaryEvidenceStableKey: null,
                firstSeenSnapshotStableKey: snapshotStableKey,
                latestSeenSnapshotStableKey: snapshotStableKey,
                suppressionReason: null,
                suppressedBy: null,
                affectedNodeStableKeys: [projectStableKey],
                evidenceStableKeys: [],
                historyKey: $"history://{projectStableKey.Value}/{ruleCode}",
                metadata,
                FingerprintGenerator.ForFinding(ruleCode, "1.0.0", severity, FindingStatus.Open, "Test finding", KnowledgeKind.Fact, metadata));
        }
    }
}
