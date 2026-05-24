using Archon.Application.Cycles;
using Archon.Application.Extraction.Pipeline;
using Archon.Application.Extraction.Resolution;
using Archon.Application.Extraction.Runs;
using Archon.Application.Extraction.Contracts;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Metrics;
using Archon.Domain.Graph.Model;
using Microsoft.Extensions.Logging;

namespace Archon.Application.Extraction.Metrics
{
    /// <summary>
    /// Calculates the first WP013 snapshot-owned metric from already accumulated architecture graph facts.
    /// </summary>
    public sealed class SnapshotMetricExtractionStage : IExtractionStage
    {
        /// <summary>
        /// Stores the metric definition used by this first end-to-end vertical slice.
        /// </summary>
        private static readonly MetricDefinition s_snapshotMetricDefinition = MetricDefinitions.SnapshotNodeCount;

        /// <summary>
        /// Stores the project metric definitions in deterministic calculation order.
        /// </summary>
        private static readonly MetricDefinition[] s_projectMetricDefinitions =
        [
            MetricDefinitions.ProjectIncomingReferenceCount,
            MetricDefinitions.ProjectOutgoingReferenceCount,
            MetricDefinitions.ProjectPackageCount,
            MetricDefinitions.ProjectPublicTypeCount,
            MetricDefinitions.ProjectEndpointCount,
            MetricDefinitions.ProjectDataAccessCount,
            MetricDefinitions.ProjectHotlistFindingCount,
            MetricDefinitions.ProjectTargetFrameworkRisk
        ];

        /// <summary>
        /// Stores graph metric definitions in deterministic calculation order for every architecture node.
        /// </summary>
        private static readonly MetricDefinition[] s_graphMetricDefinitions =
        [
            MetricDefinitions.GraphFanIn,
            MetricDefinitions.GraphFanOut,
            MetricDefinitions.GraphDegreeCentrality,
            MetricDefinitions.GraphDependencyDepth,
            MetricDefinitions.GraphTransitiveDependencyCount,
            MetricDefinitions.GraphNeighbourhoodSize,
            MetricDefinitions.GraphCycleParticipation
        ];

        /// <summary>
        /// Stores modernization metric definitions in deterministic calculation order for each supported rollup scope.
        /// </summary>
        private static readonly MetricDefinition[] s_modernizationMetricDefinitions =
        [
            MetricDefinitions.ModernizationLegacyTechnologyCount,
            MetricDefinitions.ModernizationSecuritySensitiveFindingCount,
            MetricDefinitions.ModernizationOutOfSupportTargetCount,
            MetricDefinitions.ModernizationFrameworkOnlyDependencyCount,
            MetricDefinitions.ModernizationDataAccessSpread,
            MetricDefinitions.ModernizationSharedTableUsageCount
        ];

        /// <summary>
        /// Stores the dependency edge kinds that participate in graph traversal metrics.
        /// </summary>
        private static readonly HashSet<EdgeKind> s_dependencyEdgeKinds =
        [
            EdgeKind.References,
            EdgeKind.Calls,
            EdgeKind.Implements,
            EdgeKind.Inherits,
            EdgeKind.Injects,
            EdgeKind.Exposes,
            EdgeKind.Handles,
            EdgeKind.UsesConfig,
            EdgeKind.UsesDbContext,
            EdgeKind.UsesLinqToSqlContext,
            EdgeKind.MapsEntity,
            EdgeKind.MapsTable,
            EdgeKind.MapsColumn,
            EdgeKind.ReadsTable,
            EdgeKind.WritesTable,
            EdgeKind.CallsStoredProcedure,
            EdgeKind.ExecutesRawSql,
            EdgeKind.CallsExternalService,
            EdgeKind.UsesPackage,
            EdgeKind.DeclaresEndpoint,
            EdgeKind.DeclaresComponent,
            EdgeKind.DeclaresUiRoute,
            EdgeKind.UsesComponent,
            EdgeKind.UsesLayout,
            EdgeKind.UsesControl,
            EdgeKind.UsesUiResource,
            EdgeKind.UsesStyle,
            EdgeKind.BindsTo,
            EdgeKind.UsesCommand,
            EdgeKind.UsesViewModel,
            EdgeKind.NavigatesTo,
            EdgeKind.HandlesUiEvent,
            EdgeKind.CallsApi,
            EdgeKind.RegisteredAsService,
            EdgeKind.DependsOn
        ];

        /// <summary>
        /// Stores the maximum outbound dependency traversal depth used by first-slice graph metrics.
        /// </summary>
        private const int GraphTraversalDepthLimit = 12;

        /// <summary>
        /// Stores node kinds that count as data-access footprint for project metrics.
        /// </summary>
        private static readonly HashSet<NodeKind> s_dataAccessNodeKinds =
        [
            NodeKind.DbContext,
            NodeKind.LinqToSqlDataContext,
            NodeKind.Entity,
            NodeKind.DatabaseTable,
            NodeKind.DatabaseColumn,
            NodeKind.StoredProcedure,
            NodeKind.SqlScript
        ];

        /// <summary>
        /// Logs credential-safe metric calculation diagnostics.
        /// </summary>
        private readonly ILogger<SnapshotMetricExtractionStage> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="SnapshotMetricExtractionStage"/> class.
        /// </summary>
        /// <param name="logger">The logger used for credential-safe metric calculation diagnostics.</param>
        public SnapshotMetricExtractionStage(ILogger<SnapshotMetricExtractionStage> logger)
        {
            // The stage has no persistence or source-loading dependency; it only reads the shared accumulation snapshot.
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Gets the stable stage identifier used for ordering, logging, progress reporting, and diagnostics.
        /// </summary>
        public string StageId => "WP013.SnapshotMetrics";

        /// <summary>
        /// Calculates and contributes the first snapshot metric to the shared extraction accumulation.
        /// </summary>
        /// <param name="context">The stage context containing validated input, accepted run state, and accumulation state.</param>
        /// <param name="cancellationToken">The cancellation token that stops metric calculation before work starts.</param>
        /// <returns>A successful stage result after the metric has been contributed, or a blocking result when no snapshot scope exists.</returns>
        public Task<ExtractionStageResult> ExecuteAsync(ExtractionStageContext context, CancellationToken cancellationToken)
        {
            // Metric calculation is intentionally based on accumulated facts, not direct source-file rescans or Neo4j reads.
            ArgumentNullException.ThrowIfNull(context);
            cancellationToken.ThrowIfCancellationRequested();

            ExtractedMetricInput input = ReadMetricInput(context);
            IReadOnlyList<MetricRecord> metrics = CreateMetrics(input);
            foreach (MetricRecord metric in metrics)
            {
                context.Accumulation.AddMetric(metric);
                if (metric.UnknownState.HasUnknownData)
                {
                    context.Accumulation.AddWarning(metric.UnknownState.UnknownReason);
                }
            }

            _logger.LogInformation(
                "Calculated {MetricCount} WP013 metric(s) for snapshot {SnapshotStableKey}.",
                metrics.Count,
                input.SnapshotStableKey.Value);
            return Task.FromResult(ExtractionStageResult.Success());
        }

        /// <summary>
        /// Reads the current accumulated snapshot and derives the minimal input required by the metric calculator.
        /// </summary>
        /// <param name="context">The stage context whose accumulation model contains prior extraction facts.</param>
        /// <returns>The extracted metric input containing snapshot identity and current node count.</returns>
        private static ExtractedMetricInput ReadMetricInput(ExtractionStageContext context)
        {
            // Taking a snapshot of accumulation gives the calculator deterministic, stable-key ordered facts without mutating them.
            ExtractedArchitectureSnapshot snapshot = context.Accumulation.ToSnapshot();
            StableKey snapshotStableKey = snapshot.SnapshotHeader?.StableKey ?? CreateSnapshotStableKey(context.ResolvedInput, context.Run);
            decimal nodeCount = snapshot.Nodes.Count;
            bool hasUnknownData = snapshot.SnapshotHeader is null;
            string? unknownReason = hasUnknownData ? "Snapshot metric calculation could not verify the snapshot header before counting nodes." : null;
            return new ExtractedMetricInput(snapshotStableKey, snapshot, nodeCount, hasUnknownData, unknownReason);
        }

        /// <summary>
        /// Creates the same deterministic snapshot stable key that final snapshot assembly will assign to this run.
        /// </summary>
        /// <param name="resolvedInput">The normalized extraction input that identifies the repository boundary.</param>
        /// <param name="run">The accepted run whose identifier scopes the snapshot.</param>
        /// <returns>The stable key that will own metric records for this extraction run.</returns>
        private static StableKey CreateSnapshotStableKey(ResolvedExtractionInput resolvedInput, ExtractionRun run)
        {
            // Metrics run before final assembly, so they recreate the assembler's stable snapshot identity without changing shared pipeline state.
            StableKey repositoryStableKey = StableKeyGenerator.ForRepository(NormalizeIdentitySegment(resolvedInput.RepositoryRootDirectory));
            return StableKeyGenerator.ForSummary(repositoryStableKey.Value, "ExtractionRun", run.RunId.ToString());
        }

        /// <summary>
        /// Normalizes a filesystem path into the repository identity segment used by final snapshot assembly.
        /// </summary>
        /// <param name="value">The absolute path value to normalize.</param>
        /// <returns>A deterministic lowercase segment suitable for stable-key generation.</returns>
        private static string NormalizeIdentitySegment(string value)
        {
            // This mirrors ExtractionSnapshotAssembler so metrics and the assembled header share one snapshot identity.
            string trimmed = Path.TrimEndingDirectorySeparator(value).Replace('\\', '/').Trim();
            return trimmed.ToLowerInvariant();
        }

        /// <summary>
        /// Creates the deterministic snapshot node count metric from normalized metric input.
        /// </summary>
        /// <param name="input">The normalized metric input derived from accumulated extraction facts.</param>
        /// <returns>A validated metric record ready for accumulation and persistence.</returns>
        private static MetricRecord CreateSnapshotNodeCountMetric(ExtractedMetricInput input)
        {
            // The snapshot metric uses a constant scope identity because the snapshot stable key already scopes the metric record.
            string scopeIdentity = s_snapshotMetricDefinition.DefaultScopeKind.Value;
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["calculationSource"] = "accumulatedSnapshotNodes",
                ["metricRegistry"] = "WP013"
            });
            UnknownState unknownState = input.HasUnknownData
                ? new UnknownState(true, input.UnknownReason)
                : UnknownState.Known;
            StableKey snapshotStableKey = input.SnapshotStableKey;
            StableKey stableKey = StableKeyGenerator.ForMetric(snapshotStableKey.Value, s_snapshotMetricDefinition.Kind, scopeIdentity);
            Fingerprint fingerprint = FingerprintGenerator.ForMetric(
                s_snapshotMetricDefinition.Kind,
                MetricScopeKind.Snapshot,
                scopeIdentity,
                input.NodeCount,
                textValue: null,
                s_snapshotMetricDefinition.Unit,
                unknownState.HasUnknownData,
                unknownState.UnknownReason,
                metadata);

            return new MetricRecord(
                snapshotStableKey,
                stableKey,
                s_snapshotMetricDefinition.Kind,
                MetricScopeKind.Snapshot,
                nodeStableKey: null,
                edgeStableKey: null,
                primaryEvidenceStableKey: null,
                s_snapshotMetricDefinition.Name,
                input.NodeCount,
                textValue: null,
                s_snapshotMetricDefinition.Unit,
                Confidence.Certain,
                unknownState,
                metadata,
                fingerprint);
        }

        /// <summary>
        /// Creates all metrics produced by the current WP013 stage from normalized accumulated input.
        /// </summary>
        /// <param name="input">The normalized metric input derived from accumulated extraction facts.</param>
        /// <returns>The deterministic metric records ready for accumulation and persistence.</returns>
        private static IReadOnlyList<MetricRecord> CreateMetrics(ExtractedMetricInput input)
        {
            // The stage emits the original snapshot metric first, then project and graph metrics in stable-key order.
            List<MetricRecord> metrics = [CreateSnapshotNodeCountMetric(input)];
            ProjectMetricInput projectInput = ProjectMetricInput.FromSnapshot(input.Snapshot);
            foreach (ArchitectureNode project in projectInput.Projects)
            {
                foreach (MetricDefinition definition in s_projectMetricDefinitions)
                {
                    metrics.Add(CreateProjectMetric(input.SnapshotStableKey, projectInput, project, definition));
                }
            }

            GraphMetricInput graphInput = GraphMetricInput.FromSnapshot(input.Snapshot);
            CycleDetectionResult cycleDetection = new DependencyCycleDetector().DetectCycles(input.Snapshot);
            IReadOnlyDictionary<StableKey, int> cycleParticipationCounts = DependencyCycleDetector.CountParticipation(cycleDetection);
            foreach (ArchitectureNode node in graphInput.Nodes)
            {
                foreach (MetricDefinition definition in s_graphMetricDefinitions)
                {
                    metrics.Add(CreateGraphMetric(input.SnapshotStableKey, graphInput, cycleDetection, cycleParticipationCounts, node, definition));
                }
            }

            ModernizationMetricInput modernizationInput = ModernizationMetricInput.FromSnapshot(input.Snapshot);
            foreach (ModernizationMetricScope scope in modernizationInput.Scopes)
            {
                foreach (MetricDefinition definition in s_modernizationMetricDefinitions)
                {
                    metrics.Add(CreateModernizationMetric(input.SnapshotStableKey, modernizationInput, scope, definition));
                }
            }

            return metrics;
        }

        /// <summary>
        /// Creates one modernization metric from the accumulated fact read model and a supported rollup scope.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the metric.</param>
        /// <param name="modernizationInput">The deterministic modernization fact read model.</param>
        /// <param name="scope">The rollup scope that defines which project facts participate.</param>
        /// <param name="definition">The modernization metric definition to calculate.</param>
        /// <returns>A modernization metric record scoped to the snapshot, repository, solution, or project.</returns>
        private static MetricRecord CreateModernizationMetric(StableKey snapshotStableKey, ModernizationMetricInput modernizationInput, ModernizationMetricScope scope, MetricDefinition definition)
        {
            // Modernization metrics dispatch from central definitions so API filters can use the stable registry kind names without special endpoint behavior.
            ModernizationMetricValue value = StringComparer.Ordinal.Equals(definition.Kind, MetricDefinitions.ModernizationLegacyTechnologyCount.Kind)
                ? ModernizationMetricValue.Numeric(modernizationInput.CountLegacyTechnologyFacts(scope))
                : StringComparer.Ordinal.Equals(definition.Kind, MetricDefinitions.ModernizationSecuritySensitiveFindingCount.Kind)
                    ? ModernizationMetricValue.Numeric(modernizationInput.CountSecuritySensitiveFindings(scope))
                    : StringComparer.Ordinal.Equals(definition.Kind, MetricDefinitions.ModernizationOutOfSupportTargetCount.Kind)
                        ? modernizationInput.CountOutOfSupportTargets(scope)
                        : StringComparer.Ordinal.Equals(definition.Kind, MetricDefinitions.ModernizationFrameworkOnlyDependencyCount.Kind)
                            ? ModernizationMetricValue.Numeric(modernizationInput.CountFrameworkOnlyDependencies(scope))
                            : StringComparer.Ordinal.Equals(definition.Kind, MetricDefinitions.ModernizationDataAccessSpread.Kind)
                                ? ModernizationMetricValue.Numeric(modernizationInput.CountDataAccessSpread(scope))
                                : modernizationInput.CountSharedTableUsage(scope);
            return CreateModernizationMetricRecord(snapshotStableKey, modernizationInput, scope, definition, value);
        }

        /// <summary>
        /// Creates a validated metric record for one modernization metric value and rollup scope.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the metric.</param>
        /// <param name="modernizationInput">The modernization input model that supplies scope metadata.</param>
        /// <param name="scope">The rollup scope represented by the metric.</param>
        /// <param name="definition">The modernization metric definition.</param>
        /// <param name="value">The calculated metric value and unknown-state context.</param>
        /// <returns>A snapshot-owned modernization metric record.</returns>
        private static MetricRecord CreateModernizationMetricRecord(StableKey snapshotStableKey, ModernizationMetricInput modernizationInput, ModernizationMetricScope scope, MetricDefinition definition, ModernizationMetricValue value)
        {
            // The scope identity is either the snapshot scope literal or the public stable key for the repository, solution, or project node.
            string scopeIdentity = scope.ScopeStableKey?.Value ?? scope.ScopeKind.Value;
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["calculationSource"] = "accumulatedModernizationFacts",
                ["metricRegistry"] = "WP013",
                ["modernizationInference"] = "None",
                ["scopeKind"] = scope.ScopeKind.Value,
                ["scopeStableKey"] = scope.ScopeStableKey?.Value,
                ["projectCount"] = scope.ProjectStableKeys.Count,
                ["contributingProjectStableKeys"] = scope.ProjectStableKeys.Select(static stableKey => stableKey.Value).ToArray(),
                ["legacyTechnologyFactCount"] = modernizationInput.CountLegacyTechnologyFacts(scope),
                ["securitySensitiveFindingCount"] = modernizationInput.CountSecuritySensitiveFindings(scope),
                ["outOfSupportTargetCount"] = modernizationInput.CountKnownOutOfSupportTargets(scope),
                ["frameworkOnlyDependencyCount"] = modernizationInput.CountFrameworkOnlyDependencies(scope),
                ["dataAccessProjectCount"] = modernizationInput.CountDataAccessSpread(scope),
                ["sharedTableIdentities"] = modernizationInput.GetSharedTableIdentities(scope).ToArray()
            });
            StableKey stableKey = StableKeyGenerator.ForMetric(snapshotStableKey.Value, definition.Kind, scopeIdentity);
            Fingerprint fingerprint = FingerprintGenerator.ForMetric(
                definition.Kind,
                scope.ScopeKind,
                scopeIdentity,
                value.NumericValue,
                value.TextValue,
                definition.Unit,
                value.UnknownState.HasUnknownData,
                value.UnknownState.UnknownReason,
                metadata);
            return new MetricRecord(
                snapshotStableKey,
                stableKey,
                definition.Kind,
                scope.ScopeKind,
                scope.ScopeStableKey,
                edgeStableKey: null,
                primaryEvidenceStableKey: scope.PrimaryEvidenceStableKey,
                definition.Name,
                value.NumericValue,
                value.TextValue,
                definition.Unit,
                value.Confidence,
                value.UnknownState,
                metadata,
                fingerprint);
        }

        /// <summary>
        /// Creates one node-scoped graph metric from the deterministic graph metric read model.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the metric.</param>
        /// <param name="graphInput">The precomputed graph metric read model.</param>
        /// <param name="cycleDetection">The cycle detection result that supplies cycle-participation context and truncation state.</param>
        /// <param name="cycleParticipationCounts">The deterministic participation counts keyed by architecture node stable key.</param>
        /// <param name="node">The architecture node that scopes the metric.</param>
        /// <param name="definition">The graph metric definition to calculate.</param>
        /// <returns>A node-scoped graph metric record.</returns>
        private static MetricRecord CreateGraphMetric(StableKey snapshotStableKey, GraphMetricInput graphInput, CycleDetectionResult cycleDetection, IReadOnlyDictionary<StableKey, int> cycleParticipationCounts, ArchitectureNode node, MetricDefinition definition)
        {
            // Direct metrics read fan-in/fan-out from adjacency; traversal metrics reuse the bounded breadth-first traversal result.
            GraphTraversalResult traversal = graphInput.TraverseDependencies(node.StableKey, GraphTraversalDepthLimit);
            GraphMetricValue value = StringComparer.Ordinal.Equals(definition.Kind, MetricDefinitions.GraphFanIn.Kind)
                ? GraphMetricValue.Numeric(graphInput.CountIncoming(node.StableKey))
                : StringComparer.Ordinal.Equals(definition.Kind, MetricDefinitions.GraphFanOut.Kind)
                    ? GraphMetricValue.Numeric(graphInput.CountOutgoing(node.StableKey))
                    : StringComparer.Ordinal.Equals(definition.Kind, MetricDefinitions.GraphDegreeCentrality.Kind)
                        ? GraphMetricValue.Numeric(graphInput.CalculateDegreeCentrality(node.StableKey))
                        : StringComparer.Ordinal.Equals(definition.Kind, MetricDefinitions.GraphDependencyDepth.Kind)
                            ? GraphMetricValue.Numeric(traversal.Depth, traversal.Truncated)
                            : StringComparer.Ordinal.Equals(definition.Kind, MetricDefinitions.GraphTransitiveDependencyCount.Kind)
                                ? GraphMetricValue.Numeric(traversal.ReachableNodeCount, traversal.Truncated)
                                : StringComparer.Ordinal.Equals(definition.Kind, MetricDefinitions.GraphNeighbourhoodSize.Kind)
                                    ? GraphMetricValue.Numeric(graphInput.CountNeighbourhood(node.StableKey))
                                    : GraphMetricValue.Numeric(cycleParticipationCounts.TryGetValue(node.StableKey, out int count) ? count : 0, cycleDetection.HasTruncatedResults);
            return CreateGraphMetricRecord(snapshotStableKey, node, definition, value, graphInput, cycleDetection, traversal.Truncated);
        }

        /// <summary>
        /// Creates a validated metric record for one graph metric value.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the metric.</param>
        /// <param name="node">The architecture node that scopes the graph metric.</param>
        /// <param name="definition">The graph metric definition.</param>
        /// <param name="value">The calculated graph metric value and unknown-state context.</param>
        /// <param name="graphInput">The graph input model that describes traversal scope.</param>
        /// <param name="cycleDetection">The cycle detection result that contributes cycle references and truncation metadata.</param>
        /// <param name="truncated">A value indicating whether bounded traversal reached the configured limit for this node.</param>
        /// <returns>A node-scoped graph metric record.</returns>
        private static MetricRecord CreateGraphMetricRecord(StableKey snapshotStableKey, ArchitectureNode node, MetricDefinition definition, GraphMetricValue value, GraphMetricInput graphInput, CycleDetectionResult cycleDetection, bool truncated)
        {
            // Node-scoped graph metrics use the node stable key as the public target and metric scope discriminator.
            string scopeIdentity = node.StableKey.Value;
            string[] cycleStableKeys = cycleDetection.Cycles
                .Where(cycle => cycle.NodeStableKeys.Take(cycle.NodeStableKeys.Count - 1).Contains(node.StableKey))
                .Select(static cycle => cycle.StableKey.Value)
                .OrderBy(static stableKey => stableKey, StringComparer.Ordinal)
                .ToArray();
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["calculationSource"] = "accumulatedDependencyGraph",
                ["metricRegistry"] = "WP013",
                ["nodeStableKey"] = node.StableKey.Value,
                ["metricNodeKind"] = node.NodeKind.Value,
                ["dependencyEdgeKinds"] = graphInput.DependencyEdgeKindValues,
                ["traversalDirection"] = "outbound",
                ["traversalDepthLimit"] = GraphTraversalDepthLimit,
                ["cycleDetectionMaxDepth"] = DependencyCycleDetector.DefaultMaxDepth,
                ["cycleDetectionResultLimit"] = DependencyCycleDetector.DefaultResultLimit,
                ["cycleDetectionTruncated"] = cycleDetection.HasTruncatedResults,
                ["cycleStableKeys"] = cycleStableKeys,
                ["truncated"] = truncated
            });
            StableKey stableKey = StableKeyGenerator.ForMetric(snapshotStableKey.Value, definition.Kind, scopeIdentity);
            Fingerprint fingerprint = FingerprintGenerator.ForMetric(
                definition.Kind,
                MetricScopeKind.Node,
                scopeIdentity,
                value.NumericValue,
                value.TextValue,
                definition.Unit,
                value.UnknownState.HasUnknownData,
                value.UnknownState.UnknownReason,
                metadata);
            return new MetricRecord(
                snapshotStableKey,
                stableKey,
                definition.Kind,
                MetricScopeKind.Node,
                node.StableKey,
                edgeStableKey: null,
                primaryEvidenceStableKey: node.PrimaryEvidenceStableKey,
                definition.Name,
                value.NumericValue,
                value.TextValue,
                definition.Unit,
                value.Confidence,
                value.UnknownState,
                metadata,
                fingerprint);
        }

        /// <summary>
        /// Creates one project-scoped metric from the accumulated project metric read model.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the metric.</param>
        /// <param name="projectInput">The precomputed project metric read model.</param>
        /// <param name="project">The project node that scopes the metric.</param>
        /// <param name="definition">The metric definition to calculate.</param>
        /// <returns>A project-scoped metric record.</returns>
        private static MetricRecord CreateProjectMetric(StableKey snapshotStableKey, ProjectMetricInput projectInput, ArchitectureNode project, MetricDefinition definition)
        {
            // Dispatch keeps metric definitions centralized while each metric's value logic remains explicit and testable.
            ProjectMetricValue value = StringComparer.Ordinal.Equals(definition.Kind, MetricDefinitions.ProjectIncomingReferenceCount.Kind)
                ? ProjectMetricValue.Numeric(projectInput.CountIncomingProjectReferences(project.StableKey))
                : StringComparer.Ordinal.Equals(definition.Kind, MetricDefinitions.ProjectOutgoingReferenceCount.Kind)
                    ? ProjectMetricValue.Numeric(projectInput.CountOutgoingProjectReferences(project.StableKey))
                    : StringComparer.Ordinal.Equals(definition.Kind, MetricDefinitions.ProjectPackageCount.Kind)
                        ? ProjectMetricValue.Numeric(ReadIntMetadata(project.Metadata, "project.packageReferenceCount") ?? 0)
                        : StringComparer.Ordinal.Equals(definition.Kind, MetricDefinitions.ProjectPublicTypeCount.Kind)
                            ? ProjectMetricValue.Numeric(projectInput.CountPublicTypes(project.StableKey))
                            : StringComparer.Ordinal.Equals(definition.Kind, MetricDefinitions.ProjectEndpointCount.Kind)
                                ? ProjectMetricValue.Numeric(projectInput.CountOwnedNodes(project.StableKey, NodeKind.Endpoint))
                                : StringComparer.Ordinal.Equals(definition.Kind, MetricDefinitions.ProjectDataAccessCount.Kind)
                                    ? ProjectMetricValue.Numeric(projectInput.CountOwnedNodes(project.StableKey, s_dataAccessNodeKinds))
                                    : StringComparer.Ordinal.Equals(definition.Kind, MetricDefinitions.ProjectHotlistFindingCount.Kind)
                                        ? ProjectMetricValue.Numeric(projectInput.CountFindings(project.StableKey))
                                        : CreateTargetFrameworkRiskValue(project);
            return CreateProjectMetricRecord(snapshotStableKey, project, definition, value);
        }

        /// <summary>
        /// Creates a validated metric record for one project metric value.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that owns the metric.</param>
        /// <param name="project">The project node that scopes the metric.</param>
        /// <param name="definition">The project metric definition.</param>
        /// <param name="value">The calculated metric value and unknown-state context.</param>
        /// <returns>A project-scoped metric record.</returns>
        private static MetricRecord CreateProjectMetricRecord(StableKey snapshotStableKey, ArchitectureNode project, MetricDefinition definition, ProjectMetricValue value)
        {
            // The project stable key is both the public node target and the metric scope discriminator.
            string scopeIdentity = project.StableKey.Value;
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["calculationSource"] = "accumulatedProjectFacts",
                ["metricRegistry"] = "WP013",
                ["projectStableKey"] = project.StableKey.Value,
                ["projectDisplayName"] = project.DisplayName
            });
            StableKey stableKey = StableKeyGenerator.ForMetric(snapshotStableKey.Value, definition.Kind, scopeIdentity);
            Fingerprint fingerprint = FingerprintGenerator.ForMetric(
                definition.Kind,
                MetricScopeKind.Project,
                scopeIdentity,
                value.NumericValue,
                value.TextValue,
                definition.Unit,
                value.UnknownState.HasUnknownData,
                value.UnknownState.UnknownReason,
                metadata);
            return new MetricRecord(
                snapshotStableKey,
                stableKey,
                definition.Kind,
                MetricScopeKind.Project,
                project.StableKey,
                edgeStableKey: null,
                primaryEvidenceStableKey: project.PrimaryEvidenceStableKey,
                definition.Name,
                value.NumericValue,
                value.TextValue,
                definition.Unit,
                value.Confidence,
                value.UnknownState,
                metadata,
                fingerprint);
        }

        /// <summary>
        /// Calculates a target-framework risk metric value from project metadata.
        /// </summary>
        /// <param name="project">The project node whose target-framework metadata should be interpreted.</param>
        /// <returns>The numeric and categorical target-framework risk metric value.</returns>
        private static ProjectMetricValue CreateTargetFrameworkRiskValue(ArchitectureNode project)
        {
            // Risk is intentionally conservative: current .NET gets zero, older supported modern .NET gets one, legacy frameworks get higher values.
            string? targetFramework = ReadStringMetadata(project.Metadata, "project.targetFramework") ?? ReadFirstStringMetadata(project.Metadata, "project.targetFrameworks") ?? ReadStringMetadata(project.Metadata, "project.legacyTargetFramework");
            if (string.IsNullOrWhiteSpace(targetFramework))
            {
                return ProjectMetricValue.Unknown("Unknown", "Project target framework metadata was unavailable for metric calculation.");
            }

            string normalized = targetFramework.Trim().ToLowerInvariant();
            if (normalized.StartsWith("net10", StringComparison.Ordinal))
            {
                return ProjectMetricValue.TextNumeric(0, "Current");
            }

            if (normalized.StartsWith("net8", StringComparison.Ordinal) || normalized.StartsWith("net9", StringComparison.Ordinal))
            {
                return ProjectMetricValue.TextNumeric(1, "Supported");
            }

            if (normalized.StartsWith("netcoreapp", StringComparison.Ordinal) || normalized.StartsWith("net5", StringComparison.Ordinal) || normalized.StartsWith("net6", StringComparison.Ordinal) || normalized.StartsWith("net7", StringComparison.Ordinal))
            {
                return ProjectMetricValue.TextNumeric(2, "OutOfSupportModernDotNet");
            }

            if (normalized.StartsWith("net4", StringComparison.Ordinal) || normalized.StartsWith("v4", StringComparison.Ordinal))
            {
                return ProjectMetricValue.TextNumeric(3, "LegacyDotNetFramework");
            }

            return ProjectMetricValue.TextNumeric(2, "UnknownRisk");
        }

        /// <summary>
        /// Reads a string metadata property from a canonical graph metadata value.
        /// </summary>
        /// <param name="metadata">The metadata payload to inspect.</param>
        /// <param name="propertyName">The metadata property name to read.</param>
        /// <returns>The string value, or <see langword="null" /> when missing or non-string.</returns>
        private static string? ReadStringMetadata(GraphMetadata metadata, string propertyName)
        {
            // Parsing canonical JSON avoids adding read-only dictionary state to GraphMetadata for one metric calculator.
            using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(metadata.ToCanonicalJson());
            return document.RootElement.TryGetProperty(propertyName, out System.Text.Json.JsonElement element) && element.ValueKind == System.Text.Json.JsonValueKind.String
                ? element.GetString()
                : null;
        }

        /// <summary>
        /// Reads the first string from an array metadata property.
        /// </summary>
        /// <param name="metadata">The metadata payload to inspect.</param>
        /// <param name="propertyName">The metadata property name to read.</param>
        /// <returns>The first string array value, or <see langword="null" /> when absent.</returns>
        private static string? ReadFirstStringMetadata(GraphMetadata metadata, string propertyName)
        {
            // Multi-target projects use the first declared target framework as the deterministic risk representative for this slice.
            using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(metadata.ToCanonicalJson());
            if (!document.RootElement.TryGetProperty(propertyName, out System.Text.Json.JsonElement element) || element.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                return null;
            }

            foreach (System.Text.Json.JsonElement item in element.EnumerateArray())
            {
                if (item.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    return item.GetString();
                }
            }

            return null;
        }

        /// <summary>
        /// Reads an integer metadata property from a canonical graph metadata value.
        /// </summary>
        /// <param name="metadata">The metadata payload to inspect.</param>
        /// <param name="propertyName">The metadata property name to read.</param>
        /// <returns>The integer value, or <see langword="null" /> when absent or not numeric.</returns>
        private static int? ReadIntMetadata(GraphMetadata metadata, string propertyName)
        {
            // Project package counts are stored by project extraction as numeric metadata on the project node.
            using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(metadata.ToCanonicalJson());
            return document.RootElement.TryGetProperty(propertyName, out System.Text.Json.JsonElement element) && element.TryGetInt32(out int value)
                ? value
                : null;
        }

        /// <summary>
        /// Reads a boolean metadata property from a canonical graph metadata value.
        /// </summary>
        /// <param name="metadata">The metadata payload to inspect.</param>
        /// <param name="propertyName">The metadata property name to read.</param>
        /// <returns>The boolean value, or <see langword="null" /> when absent or not boolean.</returns>
        private static bool? ReadBoolMetadata(GraphMetadata metadata, string propertyName)
        {
            // Boolean source facts such as old-style project markers are stored in metadata by earlier extraction stages.
            using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(metadata.ToCanonicalJson());
            return document.RootElement.TryGetProperty(propertyName, out System.Text.Json.JsonElement element) && (element.ValueKind == System.Text.Json.JsonValueKind.True || element.ValueKind == System.Text.Json.JsonValueKind.False)
                ? element.GetBoolean()
                : null;
        }

        /// <summary>
        /// Carries normalized metric calculation input between stage methods.
        /// </summary>
        /// <param name="SnapshotStableKey">The stable key of the snapshot that owns the metric.</param>
        /// <param name="NodeCount">The number of architecture nodes accumulated before metric calculation.</param>
        /// <param name="HasUnknownData">A value indicating whether the input is incomplete.</param>
        /// <param name="UnknownReason">The optional reason explaining incomplete input.</param>
        private sealed record ExtractedMetricInput(StableKey SnapshotStableKey, ExtractedArchitectureSnapshot Snapshot, decimal NodeCount, bool HasUnknownData, string? UnknownReason);

        /// <summary>
        /// Carries a precomputed project metric read model derived from accumulated snapshot facts.
        /// </summary>
        /// <param name="Projects">The deterministic project nodes that should receive project metrics.</param>
        /// <param name="Nodes">All accumulated architecture nodes keyed by stable key.</param>
        /// <param name="Edges">All accumulated architecture edges.</param>
        /// <param name="Findings">All accumulated finding records.</param>
        private sealed record ProjectMetricInput(IReadOnlyList<ArchitectureNode> Projects, IReadOnlyDictionary<StableKey, ArchitectureNode> Nodes, IReadOnlyList<ArchitectureEdge> Edges, IReadOnlyList<FindingRecord> Findings)
        {
            /// <summary>
            /// Creates a project metric read model from one accumulated snapshot.
            /// </summary>
            /// <param name="snapshot">The accumulated snapshot to inspect.</param>
            /// <returns>A deterministic project metric input model.</returns>
            internal static ProjectMetricInput FromSnapshot(ExtractedArchitectureSnapshot snapshot)
            {
                // Project nodes are sorted by stable key so metric emission remains deterministic across accumulator insertion order.
                ArchitectureNode[] projects = snapshot.Nodes
                    .Where(static node => node.NodeKind == NodeKind.Project)
                    .OrderBy(static node => node.StableKey.Value, StringComparer.Ordinal)
                    .ToArray();
                Dictionary<StableKey, ArchitectureNode> nodes = snapshot.Nodes.ToDictionary(static node => node.StableKey);
                return new ProjectMetricInput(projects, nodes, snapshot.Edges, snapshot.Findings);
            }

            /// <summary>
            /// Counts direct incoming project-reference relationships for one project node.
            /// </summary>
            /// <param name="projectStableKey">The project stable key receiving incoming references.</param>
            /// <returns>The incoming reference count.</returns>
            internal int CountIncomingProjectReferences(StableKey projectStableKey)
            {
                // Only direct REFERENCES edges between project nodes count as project-to-project references.
                return Edges.Count(edge => edge.EdgeKind == EdgeKind.References && edge.TargetNodeStableKey == projectStableKey && IsProject(edge.SourceNodeStableKey));
            }

            /// <summary>
            /// Counts direct outgoing project-reference relationships for one project node.
            /// </summary>
            /// <param name="projectStableKey">The project stable key originating outgoing references.</param>
            /// <returns>The outgoing reference count.</returns>
            internal int CountOutgoingProjectReferences(StableKey projectStableKey)
            {
                // Only direct REFERENCES edges between project nodes count as project-to-project references.
                return Edges.Count(edge => edge.EdgeKind == EdgeKind.References && edge.SourceNodeStableKey == projectStableKey && IsProject(edge.TargetNodeStableKey));
            }

            /// <summary>
            /// Counts owned nodes for one project and one node kind.
            /// </summary>
            /// <param name="projectStableKey">The project stable key that owns candidate nodes.</param>
            /// <param name="nodeKind">The node kind to count.</param>
            /// <returns>The matching owned node count.</returns>
            internal int CountOwnedNodes(StableKey projectStableKey, NodeKind nodeKind)
            {
                // ProjectStableKey is the normalized ownership link emitted by prior extraction stages.
                return Nodes.Values.Count(node => node.ProjectStableKey == projectStableKey && node.NodeKind == nodeKind);
            }

            /// <summary>
            /// Counts owned nodes for one project and a set of node kinds.
            /// </summary>
            /// <param name="projectStableKey">The project stable key that owns candidate nodes.</param>
            /// <param name="nodeKinds">The accepted node kinds.</param>
            /// <returns>The matching owned node count.</returns>
            internal int CountOwnedNodes(StableKey projectStableKey, IReadOnlySet<NodeKind> nodeKinds)
            {
                // Data-access metrics roll up several graph node kinds into one project footprint value.
                return Nodes.Values.Count(node => node.ProjectStableKey == projectStableKey && nodeKinds.Contains(node.NodeKind));
            }

            /// <summary>
            /// Counts public type nodes owned by one project.
            /// </summary>
            /// <param name="projectStableKey">The project stable key that owns candidate type nodes.</param>
            /// <returns>The public type count.</returns>
            internal int CountPublicTypes(StableKey projectStableKey)
            {
                // Current semantic projection does not always normalize accessibility, so public type counting accepts explicit metadata when present.
                return Nodes.Values.Count(node => node.ProjectStableKey == projectStableKey && node.NodeKind == NodeKind.Type && IsPublicType(node));
            }

            /// <summary>
            /// Counts findings associated with one project node.
            /// </summary>
            /// <param name="projectStableKey">The project stable key to match against finding targets.</param>
            /// <returns>The finding count.</returns>
            internal int CountFindings(StableKey projectStableKey)
            {
                // Findings can identify project impact through primary node or affected node references.
                return Findings.Count(finding => finding.PrimaryNodeStableKey == projectStableKey || finding.AffectedNodeStableKeys.Contains(projectStableKey));
            }

            /// <summary>
            /// Determines whether a stable key identifies a known project node.
            /// </summary>
            /// <param name="stableKey">The candidate node stable key.</param>
            /// <returns><see langword="true" /> when the key belongs to a project node.</returns>
            private bool IsProject(StableKey stableKey)
            {
                // The node dictionary prevents package or semantic references from inflating project-reference metrics.
                return Nodes.TryGetValue(stableKey, out ArchitectureNode? node) && node.NodeKind == NodeKind.Project;
            }

            /// <summary>
            /// Determines whether a type node represents a public type.
            /// </summary>
            /// <param name="node">The candidate type node.</param>
            /// <returns><see langword="true" /> when metadata marks the type as public or no accessibility metadata is available.</returns>
            private static bool IsPublicType(ArchitectureNode node)
            {
                // Missing accessibility is treated as public for this first slice so historical semantic nodes remain countable until projection normalizes accessibility.
                string? accessibility = ReadStringMetadata(node.Metadata, "semantic.accessibility") ?? ReadStringMetadata(node.Metadata, "semantic.declaredAccessibility");
                return string.IsNullOrWhiteSpace(accessibility) || StringComparer.OrdinalIgnoreCase.Equals(accessibility, "Public");
            }
        }

        /// <summary>
        /// Carries a deterministic in-memory dependency graph used by graph metric calculations.
        /// </summary>
        /// <param name="Nodes">All accumulated architecture nodes in stable-key order.</param>
        /// <param name="OutgoingEdgesBySource">Dependency edges grouped by source node stable key.</param>
        /// <param name="IncomingEdgesByTarget">Dependency edges grouped by target node stable key.</param>
        /// <param name="DependencyEdgeKindValues">The dependency edge-kind values included by traversal metadata.</param>
        private sealed record GraphMetricInput(
            IReadOnlyList<ArchitectureNode> Nodes,
            IReadOnlyDictionary<StableKey, IReadOnlyList<ArchitectureEdge>> OutgoingEdgesBySource,
            IReadOnlyDictionary<StableKey, IReadOnlyList<ArchitectureEdge>> IncomingEdgesByTarget,
            IReadOnlyList<string> DependencyEdgeKindValues)
        {
            /// <summary>
            /// Creates a deterministic graph metric read model from one accumulated snapshot.
            /// </summary>
            /// <param name="snapshot">The accumulated snapshot whose nodes and dependency edges should be normalized.</param>
            /// <returns>A graph metric input model with stable ordering and filtered dependency edges.</returns>
            internal static GraphMetricInput FromSnapshot(ExtractedArchitectureSnapshot snapshot)
            {
                // Stable-key ordering makes metric emission and traversal expansion independent of extractor insertion order.
                ArchitectureNode[] nodes = snapshot.Nodes
                    .OrderBy(static node => node.StableKey.Value, StringComparer.Ordinal)
                    .ToArray();
                ArchitectureEdge[] dependencyEdges = snapshot.Edges
                    .Where(static edge => s_dependencyEdgeKinds.Contains(edge.EdgeKind))
                    .OrderBy(static edge => edge.SourceNodeStableKey.Value, StringComparer.Ordinal)
                    .ThenBy(static edge => edge.TargetNodeStableKey.Value, StringComparer.Ordinal)
                    .ThenBy(static edge => edge.EdgeKind.Value, StringComparer.Ordinal)
                    .ThenBy(static edge => edge.StableKey.Value, StringComparer.Ordinal)
                    .ToArray();
                Dictionary<StableKey, IReadOnlyList<ArchitectureEdge>> outgoing = dependencyEdges
                    .GroupBy(static edge => edge.SourceNodeStableKey)
                    .ToDictionary(static group => group.Key, static group => (IReadOnlyList<ArchitectureEdge>)group.ToArray());
                Dictionary<StableKey, IReadOnlyList<ArchitectureEdge>> incoming = dependencyEdges
                    .GroupBy(static edge => edge.TargetNodeStableKey)
                    .ToDictionary(static group => group.Key, static group => (IReadOnlyList<ArchitectureEdge>)group.ToArray());
                string[] dependencyKinds = s_dependencyEdgeKinds
                    .Select(static edgeKind => edgeKind.Value)
                    .OrderBy(static value => value, StringComparer.Ordinal)
                    .ToArray();
                return new GraphMetricInput(nodes, outgoing, incoming, dependencyKinds);
            }

            /// <summary>
            /// Counts dependency edges that target the supplied node.
            /// </summary>
            /// <param name="nodeStableKey">The node stable key receiving incoming dependency edges.</param>
            /// <returns>The direct fan-in count.</returns>
            internal int CountIncoming(StableKey nodeStableKey)
            {
                // Fan-in is a direct edge count after non-dependency relationships have already been filtered out.
                return IncomingEdgesByTarget.TryGetValue(nodeStableKey, out IReadOnlyList<ArchitectureEdge>? edges) ? edges.Count : 0;
            }

            /// <summary>
            /// Counts dependency edges that originate from the supplied node.
            /// </summary>
            /// <param name="nodeStableKey">The node stable key originating outgoing dependency edges.</param>
            /// <returns>The direct fan-out count.</returns>
            internal int CountOutgoing(StableKey nodeStableKey)
            {
                // Fan-out is a direct edge count after non-dependency relationships have already been filtered out.
                return OutgoingEdgesBySource.TryGetValue(nodeStableKey, out IReadOnlyList<ArchitectureEdge>? edges) ? edges.Count : 0;
            }

            /// <summary>
            /// Calculates normalized degree centrality for the supplied node.
            /// </summary>
            /// <param name="nodeStableKey">The node stable key whose direct degree should be normalized.</param>
            /// <returns>The fan-in plus fan-out divided by the largest possible directed degree for the snapshot.</returns>
            internal decimal CalculateDegreeCentrality(StableKey nodeStableKey)
            {
                // A directed graph with N nodes can have 2*(N-1) direct incident edges for one node when self-dependencies are ignored.
                int possibleDirectedDegree = Math.Max(0, (Nodes.Count - 1) * 2);
                if (possibleDirectedDegree == 0)
                {
                    return 0;
                }

                decimal directDegree = CountIncoming(nodeStableKey) + CountOutgoing(nodeStableKey);
                return Math.Round(directDegree / possibleDirectedDegree, 6, MidpointRounding.AwayFromZero);
            }

            /// <summary>
            /// Counts unique direct dependency neighbours around one node.
            /// </summary>
            /// <param name="nodeStableKey">The node stable key whose incoming and outgoing neighbours should be counted.</param>
            /// <returns>The unique one-hop neighbourhood size.</returns>
            internal int CountNeighbourhood(StableKey nodeStableKey)
            {
                // Neighbourhood combines direct predecessors and direct dependencies, de-duplicated by stable key.
                HashSet<StableKey> neighbours = [];
                if (IncomingEdgesByTarget.TryGetValue(nodeStableKey, out IReadOnlyList<ArchitectureEdge>? incomingEdges))
                {
                    foreach (ArchitectureEdge edge in incomingEdges)
                    {
                        neighbours.Add(edge.SourceNodeStableKey);
                    }
                }

                if (OutgoingEdgesBySource.TryGetValue(nodeStableKey, out IReadOnlyList<ArchitectureEdge>? outgoingEdges))
                {
                    foreach (ArchitectureEdge edge in outgoingEdges)
                    {
                        neighbours.Add(edge.TargetNodeStableKey);
                    }
                }

                return neighbours.Count;
            }

            /// <summary>
            /// Traverses outbound dependencies from one node using deterministic breadth-first expansion and a depth limit.
            /// </summary>
            /// <param name="nodeStableKey">The node stable key where traversal starts.</param>
            /// <param name="depthLimit">The maximum outbound hop depth to explore.</param>
            /// <returns>The bounded traversal result containing depth, unique reachable count, and truncation state.</returns>
            internal GraphTraversalResult TraverseDependencies(StableKey nodeStableKey, int depthLimit)
            {
                // Breadth-first traversal gives the shortest dependency depth while stable ordering makes ties reproducible.
                HashSet<StableKey> visited = [];
                Queue<GraphTraversalFrame> queue = new();
                int maxDepth = 0;
                bool truncated = false;
                EnqueueNextDepth(nodeStableKey, currentDepth: 0, queue, visited, depthLimit, ref truncated);
                while (queue.Count > 0)
                {
                    GraphTraversalFrame frame = queue.Dequeue();
                    maxDepth = Math.Max(maxDepth, frame.Depth);
                    EnqueueNextDepth(frame.NodeStableKey, frame.Depth, queue, visited, depthLimit, ref truncated);
                }

                return new GraphTraversalResult(maxDepth, visited.Count, truncated);
            }

            /// <summary>
            /// Enqueues the next outbound dependency layer while respecting visited nodes and configured depth limits.
            /// </summary>
            /// <param name="nodeStableKey">The node whose outbound dependencies should be expanded.</param>
            /// <param name="currentDepth">The traversal depth of the node being expanded.</param>
            /// <param name="queue">The breadth-first queue that receives next-layer frames.</param>
            /// <param name="visited">The set of unique dependency nodes already discovered.</param>
            /// <param name="depthLimit">The maximum permitted depth.</param>
            /// <param name="truncated">A mutable flag set when additional dependencies exist beyond the depth limit.</param>
            private void EnqueueNextDepth(StableKey nodeStableKey, int currentDepth, Queue<GraphTraversalFrame> queue, HashSet<StableKey> visited, int depthLimit, ref bool truncated)
            {
                // At the limit, the presence of any outbound edge proves the metric is a bounded lower-bound, so record truncation.
                if (!OutgoingEdgesBySource.TryGetValue(nodeStableKey, out IReadOnlyList<ArchitectureEdge>? outgoingEdges))
                {
                    return;
                }

                if (currentDepth >= depthLimit)
                {
                    truncated = outgoingEdges.Count > 0 || truncated;
                    return;
                }

                foreach (ArchitectureEdge edge in outgoingEdges)
                {
                    if (visited.Add(edge.TargetNodeStableKey))
                    {
                        queue.Enqueue(new GraphTraversalFrame(edge.TargetNodeStableKey, currentDepth + 1));
                    }
                }
            }
        }

        /// <summary>
        /// Carries one breadth-first traversal queue item for graph metric calculation.
        /// </summary>
        /// <param name="NodeStableKey">The node stable key to expand.</param>
        /// <param name="Depth">The outbound dependency depth represented by this frame.</param>
        private sealed record GraphTraversalFrame(StableKey NodeStableKey, int Depth);

        /// <summary>
        /// Carries the bounded outbound traversal result used by dependency-depth and transitive-dependency metrics.
        /// </summary>
        /// <param name="Depth">The deepest explored outbound dependency hop count.</param>
        /// <param name="ReachableNodeCount">The unique dependency node count discovered within the traversal limit.</param>
        /// <param name="Truncated">A value indicating whether traversal stopped before all reachable dependencies were explored.</param>
        private sealed record GraphTraversalResult(int Depth, int ReachableNodeCount, bool Truncated);

        /// <summary>
        /// Carries one calculated graph metric value together with confidence and unknown-state context.
        /// </summary>
        /// <param name="NumericValue">The optional numeric value.</param>
        /// <param name="TextValue">The optional categorical text value.</param>
        /// <param name="Confidence">The confidence assigned to the metric value.</param>
        /// <param name="UnknownState">The unknown-state details for incomplete metric input.</param>
        private sealed record GraphMetricValue(decimal? NumericValue, string? TextValue, Confidence Confidence, UnknownState UnknownState)
        {
            /// <summary>
            /// Creates a numeric graph metric value with optional truncation unknown-state context.
            /// </summary>
            /// <param name="value">The numeric graph metric value.</param>
            /// <param name="truncated">A value indicating whether bounded traversal made the value a lower-bound.</param>
            /// <returns>A graph metric value.</returns>
            internal static GraphMetricValue Numeric(decimal value, bool truncated = false)
            {
                // Truncated traversal remains numeric but marks the result as an incomplete lower-bound for API consumers.
                return truncated
                    ? new GraphMetricValue(value, null, Confidence.Medium, UnknownState.Unknown("Graph dependency traversal reached the configured depth limit before all reachable dependencies were explored."))
                    : new GraphMetricValue(value, null, Confidence.Certain, UnknownState.Known);
            }

            /// <summary>
            /// Creates the reserved cycle-participation value used only when a future caller explicitly lacks cycle input.
            /// </summary>
            /// <returns>An unknown graph metric value explaining that cycle inputs are not available.</returns>
            internal static GraphMetricValue CycleUnknown()
            {
                // The metric stage now provides cycle input, but keeping this factory preserves a clear fallback for incomplete future composition paths.
                return new GraphMetricValue(null, "Unknown", Confidence.Low, UnknownState.Unknown("Cycle participation could not be calculated because cycle detection input was unavailable."));
            }
        }

        /// <summary>
        /// Carries a deterministic in-memory modernization fact view used by modernization metric calculations.
        /// </summary>
        /// <param name="Projects">Project nodes in stable-key order.</param>
        /// <param name="RepositoryNodes">Repository nodes in stable-key order.</param>
        /// <param name="SolutionNodes">Solution nodes in stable-key order.</param>
        /// <param name="AllNodes">All architecture nodes keyed by stable key.</param>
        /// <param name="Findings">All accumulated finding records.</param>
        private sealed record ModernizationMetricInput(
            IReadOnlyList<ArchitectureNode> Projects,
            IReadOnlyList<ArchitectureNode> RepositoryNodes,
            IReadOnlyList<ArchitectureNode> SolutionNodes,
            IReadOnlyDictionary<StableKey, ArchitectureNode> AllNodes,
            IReadOnlyList<FindingRecord> Findings)
        {
            /// <summary>
            /// Creates the modernization metric read model from one accumulated snapshot.
            /// </summary>
            /// <param name="snapshot">The accumulated snapshot whose facts should be interpreted.</param>
            /// <returns>A modernization metric input model with deterministic node ordering.</returns>
            internal static ModernizationMetricInput FromSnapshot(ExtractedArchitectureSnapshot snapshot)
            {
                // Modernization metrics reuse existing graph facts and never inspect source files directly.
                ArchitectureNode[] projects = snapshot.Nodes
                    .Where(static node => node.NodeKind == NodeKind.Project)
                    .OrderBy(static node => node.StableKey.Value, StringComparer.Ordinal)
                    .ToArray();
                ArchitectureNode[] repositories = snapshot.Nodes
                    .Where(static node => node.NodeKind == NodeKind.Repository)
                    .OrderBy(static node => node.StableKey.Value, StringComparer.Ordinal)
                    .ToArray();
                ArchitectureNode[] solutions = snapshot.Nodes
                    .Where(static node => node.NodeKind == NodeKind.Solution)
                    .OrderBy(static node => node.StableKey.Value, StringComparer.Ordinal)
                    .ToArray();
                Dictionary<StableKey, ArchitectureNode> allNodes = snapshot.Nodes.ToDictionary(static node => node.StableKey);
                return new ModernizationMetricInput(projects, repositories, solutions, allNodes, snapshot.Findings);
            }

            /// <summary>
            /// Gets the snapshot, repository, solution, and project rollup scopes supported by current graph facts.
            /// </summary>
            internal IReadOnlyList<ModernizationMetricScope> Scopes
            {
                get
                {
                    // Snapshot always exists as the broadest rollup; repository and solution scopes are added only when graph nodes support them.
                    List<ModernizationMetricScope> scopes =
                    [
                        new ModernizationMetricScope(MetricScopeKind.Snapshot, null, Projects.Select(static project => project.StableKey).ToHashSet(), null)
                    ];
                    foreach (ArchitectureNode repository in RepositoryNodes)
                    {
                        scopes.Add(new ModernizationMetricScope(MetricScopeKind.Repository, repository.StableKey, FindProjectsUnder(repository.StableKey).ToHashSet(), repository.PrimaryEvidenceStableKey));
                    }

                    foreach (ArchitectureNode solution in SolutionNodes)
                    {
                        scopes.Add(new ModernizationMetricScope(MetricScopeKind.Solution, solution.StableKey, FindProjectsUnder(solution.StableKey).ToHashSet(), solution.PrimaryEvidenceStableKey));
                    }

                    foreach (ArchitectureNode project in Projects)
                    {
                        scopes.Add(new ModernizationMetricScope(MetricScopeKind.Project, project.StableKey, new HashSet<StableKey> { project.StableKey }, project.PrimaryEvidenceStableKey));
                    }

                    return scopes;
                }
            }

            /// <summary>
            /// Counts deterministic legacy technology facts within one rollup scope.
            /// </summary>
            /// <param name="scope">The modernization rollup scope to inspect.</param>
            /// <returns>The count of legacy technology facts.</returns>
            internal int CountLegacyTechnologyFacts(ModernizationMetricScope scope)
            {
                // Legacy technology means extracted facts such as old-style project, classic runtime, LINQ to SQL, or legacy framework metadata, not inferred business risk.
                return GetProjectNodes(scope).Count(IsLegacyProject)
                    + GetOwnedNodes(scope).Count(static node => IsLegacyTechnologyNode(node) && !IsProjectNode(node));
            }

            /// <summary>
            /// Counts security-sensitive findings within one rollup scope.
            /// </summary>
            /// <param name="scope">The modernization rollup scope to inspect.</param>
            /// <returns>The count of security-sensitive finding records.</returns>
            internal int CountSecuritySensitiveFindings(ModernizationMetricScope scope)
            {
                // Security-sensitive classification is read from rule/finding metadata, rule code, title, and severity, all deterministic finding facts.
                return Findings.Count(finding => IsFindingInScope(finding, scope) && IsSecuritySensitiveFinding(finding));
            }

            /// <summary>
            /// Counts out-of-support targets and preserves unknown-state when target metadata is missing.
            /// </summary>
            /// <param name="scope">The modernization rollup scope to inspect.</param>
            /// <returns>The out-of-support target metric value.</returns>
            internal ModernizationMetricValue CountOutOfSupportTargets(ModernizationMetricScope scope)
            {
                // Missing target framework metadata is surfaced as unknown because absence can mean extractor incompleteness rather than zero risk.
                ArchitectureNode[] projects = GetProjectNodes(scope).ToArray();
                int missingTargetCount = projects.Count(static project => ReadProjectTargetFramework(project) is null);
                int outOfSupportCount = projects.Count(static project => IsOutOfSupportTarget(ReadProjectTargetFramework(project)));
                if (missingTargetCount > 0)
                {
                    return ModernizationMetricValue.UnknownNumeric(outOfSupportCount, "Unknown", "Project target framework metadata was unavailable for modernization target support calculation.");
                }

                return ModernizationMetricValue.Numeric(outOfSupportCount);
            }

            /// <summary>
            /// Counts known out-of-support targets without adding unknown-state context.
            /// </summary>
            /// <param name="scope">The modernization rollup scope to inspect.</param>
            /// <returns>The known out-of-support target count.</returns>
            internal int CountKnownOutOfSupportTargets(ModernizationMetricScope scope)
            {
                // Metadata uses this known count even when the metric value marks missing target data as unknown.
                return GetProjectNodes(scope).Count(static project => IsOutOfSupportTarget(ReadProjectTargetFramework(project)));
            }

            /// <summary>
            /// Counts framework-only dependency signals in one rollup scope.
            /// </summary>
            /// <param name="scope">The modernization rollup scope to inspect.</param>
            /// <returns>The framework-only dependency count.</returns>
            internal int CountFrameworkOnlyDependencies(ModernizationMetricScope scope)
            {
                // Current graph facts expose package counts and package nodes; framework-only dependencies are conservatively represented by legacy project package references and packages marked framework-only.
                int legacyProjectPackageCount = GetProjectNodes(scope)
                    .Where(IsLegacyProject)
                    .Sum(static project => ReadIntMetadata(project.Metadata, "project.packageReferenceCount") ?? 0);
                int frameworkOnlyPackageCount = GetOwnedNodes(scope)
                    .Count(static node => node.NodeKind == NodeKind.Package && IsFrameworkOnlyPackage(node));
                return legacyProjectPackageCount + frameworkOnlyPackageCount;
            }

            /// <summary>
            /// Counts distinct projects with data-access facts in one rollup scope.
            /// </summary>
            /// <param name="scope">The modernization rollup scope to inspect.</param>
            /// <returns>The number of projects containing data-access facts.</returns>
            internal int CountDataAccessSpread(ModernizationMetricScope scope)
            {
                // Data-access spread is a project count, not a raw fact count, so a project with many tables still contributes one spread unit.
                return GetOwnedNodes(scope)
                    .Where(static node => s_dataAccessNodeKinds.Contains(node.NodeKind))
                    .Select(static node => node.ProjectStableKey)
                    .Where(static stableKey => stableKey.HasValue)
                    .Select(static stableKey => stableKey!.Value)
                    .Where(scope.ProjectStableKeys.Contains)
                    .Distinct()
                    .Count();
            }

            /// <summary>
            /// Counts database table identities shared by more than one project in one rollup scope.
            /// </summary>
            /// <param name="scope">The modernization rollup scope to inspect.</param>
            /// <returns>The shared table usage metric value.</returns>
            internal ModernizationMetricValue CountSharedTableUsage(ModernizationMetricScope scope)
            {
                // Shared table usage is based on tableName/schemaName metadata emitted by data-access extractors and never connects to a live database.
                string[] sharedTables = GetSharedTableIdentities(scope).ToArray();
                if (!GetOwnedNodes(scope).Any(static node => node.NodeKind == NodeKind.DatabaseTable))
                {
                    return ModernizationMetricValue.UnknownNumeric(0, null, "No database table facts were available for shared table usage calculation.");
                }

                return ModernizationMetricValue.Numeric(sharedTables.Length);
            }

            /// <summary>
            /// Gets deterministic table identities used by more than one project in one rollup scope.
            /// </summary>
            /// <param name="scope">The modernization rollup scope to inspect.</param>
            /// <returns>The shared table identity values.</returns>
            internal IReadOnlyList<string> GetSharedTableIdentities(ModernizationMetricScope scope)
            {
                // Table identity combines schema and table names so projects using dbo.Orders and audit.Orders are not conflated.
                return GetOwnedNodes(scope)
                    .Where(static node => node.NodeKind == NodeKind.DatabaseTable)
                    .Select(static node => new { TableIdentity = ReadTableIdentity(node), node.ProjectStableKey })
                    .Where(static item => item.TableIdentity is not null && item.ProjectStableKey.HasValue)
                    .GroupBy(static item => item.TableIdentity!, StringComparer.Ordinal)
                    .Where(static group => group.Select(static item => item.ProjectStableKey!.Value).Distinct().Count() > 1)
                    .Select(static group => group.Key)
                    .OrderBy(static value => value, StringComparer.Ordinal)
                    .ToArray();
            }

            /// <summary>
            /// Finds project nodes that belong under a repository or solution node.
            /// </summary>
            /// <param name="parentStableKey">The repository or solution stable key.</param>
            /// <returns>The project stable keys in deterministic order.</returns>
            private IReadOnlyList<StableKey> FindProjectsUnder(StableKey parentStableKey)
            {
                // Project parent links are optional across extractors, so repository rollups include all projects when no direct children are known.
                StableKey[] directChildren = Projects
                    .Where(project => project.ParentNodeStableKey == parentStableKey)
                    .Select(static project => project.StableKey)
                    .OrderBy(static stableKey => stableKey.Value, StringComparer.Ordinal)
                    .ToArray();
                return directChildren.Length > 0 ? directChildren : Projects.Select(static project => project.StableKey).ToArray();
            }

            /// <summary>
            /// Gets project nodes included by one modernization rollup scope.
            /// </summary>
            /// <param name="scope">The modernization rollup scope.</param>
            /// <returns>The scoped project nodes.</returns>
            private IEnumerable<ArchitectureNode> GetProjectNodes(ModernizationMetricScope scope)
            {
                // Scope project stable keys are precomputed once so each metric uses exactly the same rollup boundary.
                return Projects.Where(project => scope.ProjectStableKeys.Contains(project.StableKey));
            }

            /// <summary>
            /// Gets non-project nodes owned by projects inside one modernization rollup scope.
            /// </summary>
            /// <param name="scope">The modernization rollup scope.</param>
            /// <returns>The scoped owned nodes.</returns>
            private IEnumerable<ArchitectureNode> GetOwnedNodes(ModernizationMetricScope scope)
            {
                // Ownership through ProjectStableKey lets data-access and package facts roll up without traversing source artifacts.
                return AllNodes.Values.Where(node => node.ProjectStableKey.HasValue && scope.ProjectStableKeys.Contains(node.ProjectStableKey.Value));
            }

            /// <summary>
            /// Determines whether a finding belongs to one modernization rollup scope.
            /// </summary>
            /// <param name="finding">The finding to inspect.</param>
            /// <param name="scope">The modernization rollup scope.</param>
            /// <returns><see langword="true" /> when the finding targets a scoped project or scoped owned node.</returns>
            private bool IsFindingInScope(FindingRecord finding, ModernizationMetricScope scope)
            {
                // Findings may target project nodes directly or lower-level nodes that carry a project owner link.
                return finding.AffectedNodeStableKeys.Concat(finding.PrimaryNodeStableKey.HasValue ? [finding.PrimaryNodeStableKey.Value] : [])
                    .Any(stableKey => scope.ProjectStableKeys.Contains(stableKey) || (AllNodes.TryGetValue(stableKey, out ArchitectureNode? node) && node.ProjectStableKey.HasValue && scope.ProjectStableKeys.Contains(node.ProjectStableKey.Value)));
            }

            /// <summary>
            /// Determines whether a project node represents an extracted legacy technology fact.
            /// </summary>
            /// <param name="project">The project node to inspect.</param>
            /// <returns><see langword="true" /> when deterministic metadata marks legacy technology.</returns>
            private static bool IsLegacyProject(ArchitectureNode project)
            {
                // Legacy detection accepts old-style project metadata, classic runtime metadata, and legacy target frameworks recorded by extractors.
                string? targetFramework = ReadProjectTargetFramework(project);
                string? runtimeKind = ReadStringMetadata(project.Metadata, "runtimeKind");
                string? framework = ReadStringMetadata(project.Metadata, "framework");
                return ReadBoolMetadata(project.Metadata, "project.isOldStyle") == true
                    || IsOutOfSupportTarget(targetFramework)
                    || ContainsLegacyTechnology(runtimeKind)
                    || ContainsLegacyTechnology(framework);
            }

            /// <summary>
            /// Determines whether a non-project node represents legacy technology.
            /// </summary>
            /// <param name="node">The node to inspect.</param>
            /// <returns><see langword="true" /> when deterministic node kind or metadata marks legacy technology.</returns>
            private static bool IsLegacyTechnologyNode(ArchitectureNode node)
            {
                // LINQ to SQL and classic framework facts are explicit legacy technology indicators in current extractors.
                string? technology = ReadStringMetadata(node.Metadata, "dataAccessTechnology");
                string? framework = ReadStringMetadata(node.Metadata, "framework");
                string? runtimeKind = ReadStringMetadata(node.Metadata, "runtimeKind");
                return node.NodeKind == NodeKind.LinqToSqlDataContext
                    || ContainsLegacyTechnology(technology)
                    || ContainsLegacyTechnology(framework)
                    || ContainsLegacyTechnology(runtimeKind);
            }

            /// <summary>
            /// Determines whether a node is a project node.
            /// </summary>
            /// <param name="node">The node to inspect.</param>
            /// <returns><see langword="true" /> when the node is a project node.</returns>
            private static bool IsProjectNode(ArchitectureNode node)
            {
                // The helper keeps legacy project counting from double-counting project nodes as owned legacy nodes.
                return node.NodeKind == NodeKind.Project;
            }

            /// <summary>
            /// Determines whether a finding is security-sensitive.
            /// </summary>
            /// <param name="finding">The finding to inspect.</param>
            /// <returns><see langword="true" /> when metadata, rule code, title, or severity supports security-sensitive classification.</returns>
            private static bool IsSecuritySensitiveFinding(FindingRecord finding)
            {
                // Security-sensitive metrics use deterministic rule/finding classification, not severity alone unless severity is critical.
                string? category = ReadStringMetadata(finding.Metadata, "ruleCategory") ?? ReadStringMetadata(finding.Metadata, "category");
                return ContainsSecurity(category)
                    || ContainsSecurity(finding.RuleCode)
                    || ContainsSecurity(finding.Title)
                    || finding.Severity == FindingSeverity.Critical;
            }

            /// <summary>
            /// Determines whether a package node represents a framework-only dependency signal.
            /// </summary>
            /// <param name="node">The package node to inspect.</param>
            /// <returns><see langword="true" /> when metadata marks the dependency as framework-only.</returns>
            private static bool IsFrameworkOnlyPackage(ArchitectureNode node)
            {
                // Framework-only dependency metadata is accepted when extractors or future package stages provide explicit flags.
                return ReadBoolMetadata(node.Metadata, "frameworkOnly") == true
                    || ReadBoolMetadata(node.Metadata, "package.frameworkOnly") == true
                    || StringComparer.OrdinalIgnoreCase.Equals(ReadStringMetadata(node.Metadata, "dependencyKind"), "FrameworkOnly");
            }

            /// <summary>
            /// Reads the target-framework metadata value used by modernization target support rules.
            /// </summary>
            /// <param name="project">The project node to inspect.</param>
            /// <returns>The target-framework value, or <see langword="null" /> when missing.</returns>
            private static string? ReadProjectTargetFramework(ArchitectureNode project)
            {
                // Multi-target projects use the first declared target for this initial rollup, matching project metric behavior in this slice.
                return ReadStringMetadata(project.Metadata, "project.targetFramework")
                    ?? ReadFirstStringMetadata(project.Metadata, "project.targetFrameworks")
                    ?? ReadStringMetadata(project.Metadata, "project.legacyTargetFramework");
            }

            /// <summary>
            /// Determines whether a target framework is out of support for modernization metrics.
            /// </summary>
            /// <param name="targetFramework">The target-framework value to inspect.</param>
            /// <returns><see langword="true" /> when the target framework is known to be legacy or out of support.</returns>
            private static bool IsOutOfSupportTarget(string? targetFramework)
            {
                // The project targets .NET 10; older .NET Core and .NET Framework targets are treated as out-of-support modernization signals.
                if (string.IsNullOrWhiteSpace(targetFramework))
                {
                    return false;
                }

                string normalized = targetFramework.Trim().ToLowerInvariant();
                return normalized.StartsWith("net4", StringComparison.Ordinal)
                    || normalized.StartsWith("v4", StringComparison.Ordinal)
                    || normalized.StartsWith("netcoreapp", StringComparison.Ordinal)
                    || normalized.StartsWith("net5", StringComparison.Ordinal)
                    || normalized.StartsWith("net6", StringComparison.Ordinal)
                    || normalized.StartsWith("net7", StringComparison.Ordinal);
            }

            /// <summary>
            /// Reads a deterministic database table identity from a table node.
            /// </summary>
            /// <param name="node">The database table node to inspect.</param>
            /// <returns>The normalized table identity, or <see langword="null" /> when table metadata is incomplete.</returns>
            private static string? ReadTableIdentity(ArchitectureNode node)
            {
                // Schema is optional in some source artifacts; a default placeholder keeps table identities stable when only tableName is known.
                string? tableName = ReadStringMetadata(node.Metadata, "tableName");
                if (string.IsNullOrWhiteSpace(tableName))
                {
                    return null;
                }

                string schemaName = ReadStringMetadata(node.Metadata, "schemaName") ?? "default";
                return string.Concat(schemaName.Trim().ToLowerInvariant(), ".", tableName.Trim().ToLowerInvariant());
            }

            /// <summary>
            /// Determines whether a metadata value contains a known legacy technology marker.
            /// </summary>
            /// <param name="value">The metadata value to inspect.</param>
            /// <returns><see langword="true" /> when a legacy marker is present.</returns>
            private static bool ContainsLegacyTechnology(string? value)
            {
                // The accepted markers come from current extractor metadata and widely used framework names rather than AI-created labels.
                return !string.IsNullOrWhiteSpace(value)
                    && (value.Contains("Classic", StringComparison.OrdinalIgnoreCase)
                        || value.Contains("LinqToSql", StringComparison.OrdinalIgnoreCase)
                        || value.Contains("LINQ to SQL", StringComparison.OrdinalIgnoreCase)
                        || value.Contains("Web Forms", StringComparison.OrdinalIgnoreCase)
                        || value.Contains("WebForms", StringComparison.OrdinalIgnoreCase)
                        || value.Contains("MVC 5", StringComparison.OrdinalIgnoreCase)
                        || value.Contains("Web API 2", StringComparison.OrdinalIgnoreCase));
            }

            /// <summary>
            /// Determines whether a string contains security-sensitive classification text.
            /// </summary>
            /// <param name="value">The value to inspect.</param>
            /// <returns><see langword="true" /> when security-sensitive text is present.</returns>
            private static bool ContainsSecurity(string? value)
            {
                // Current rule categories and rule codes use security-oriented words, so ordinal ignore-case matching is deterministic enough for this metric.
                return !string.IsNullOrWhiteSpace(value)
                    && (value.Contains("SecuritySensitive", StringComparison.OrdinalIgnoreCase)
                        || value.Contains("Security", StringComparison.OrdinalIgnoreCase)
                        || value.Contains("Secret", StringComparison.OrdinalIgnoreCase)
                        || value.Contains("Credential", StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>
        /// Carries the normalized rollup scope for one modernization metric calculation pass.
        /// </summary>
        /// <param name="ScopeKind">The metric scope kind to write to the metric record.</param>
        /// <param name="ScopeStableKey">The optional repository, solution, or project stable key for node-targeted rollups.</param>
        /// <param name="ProjectStableKeys">The project stable keys included by this scope.</param>
        /// <param name="PrimaryEvidenceStableKey">The optional primary evidence key for the scoped node.</param>
        private sealed record ModernizationMetricScope(MetricScopeKind ScopeKind, StableKey? ScopeStableKey, IReadOnlySet<StableKey> ProjectStableKeys, StableKey? PrimaryEvidenceStableKey);

        /// <summary>
        /// Carries one calculated modernization metric value together with confidence and unknown-state context.
        /// </summary>
        /// <param name="NumericValue">The optional numeric value.</param>
        /// <param name="TextValue">The optional categorical text value.</param>
        /// <param name="Confidence">The confidence assigned to the metric value.</param>
        /// <param name="UnknownState">The unknown-state details for incomplete metric input.</param>
        private sealed record ModernizationMetricValue(decimal? NumericValue, string? TextValue, Confidence Confidence, UnknownState UnknownState)
        {
            /// <summary>
            /// Creates a known numeric modernization metric value.
            /// </summary>
            /// <param name="value">The numeric metric value.</param>
            /// <returns>A known modernization metric value.</returns>
            internal static ModernizationMetricValue Numeric(decimal value)
            {
                // Count metrics are certain when all participating source facts are present in the accumulated snapshot.
                return new ModernizationMetricValue(value, null, Confidence.Certain, UnknownState.Known);
            }

            /// <summary>
            /// Creates a numeric modernization metric value with explicit incomplete-input context.
            /// </summary>
            /// <param name="numericValue">The numeric value that could be calculated from known facts.</param>
            /// <param name="textValue">The optional categorical text value to expose.</param>
            /// <param name="unknownReason">The reason the metric input is incomplete.</param>
            /// <returns>An unknown-state modernization metric value.</returns>
            internal static ModernizationMetricValue UnknownNumeric(decimal numericValue, string? textValue, string unknownReason)
            {
                // Unknown metrics remain queryable as lower-bound or incomplete values instead of disappearing from API responses.
                return new ModernizationMetricValue(numericValue, textValue, Confidence.Low, UnknownState.Unknown(unknownReason));
            }
        }

        /// <summary>
        /// Carries one calculated metric value together with confidence and unknown-state context.
        /// </summary>
        /// <param name="NumericValue">The optional numeric value.</param>
        /// <param name="TextValue">The optional categorical text value.</param>
        /// <param name="Confidence">The confidence assigned to the metric value.</param>
        /// <param name="UnknownState">The unknown-state details for incomplete metric input.</param>
        private sealed record ProjectMetricValue(decimal? NumericValue, string? TextValue, Confidence Confidence, UnknownState UnknownState)
        {
            /// <summary>
            /// Creates a known numeric project metric value.
            /// </summary>
            /// <param name="value">The numeric metric value.</param>
            /// <returns>A known project metric value.</returns>
            internal static ProjectMetricValue Numeric(decimal value)
            {
                // Count metrics are certain when their source facts are already accumulated.
                return new ProjectMetricValue(value, null, Confidence.Certain, UnknownState.Known);
            }

            /// <summary>
            /// Creates a known numeric and categorical project metric value.
            /// </summary>
            /// <param name="numericValue">The numeric risk score.</param>
            /// <param name="textValue">The categorical risk label.</param>
            /// <returns>A known project metric value.</returns>
            internal static ProjectMetricValue TextNumeric(decimal numericValue, string textValue)
            {
                // Target-framework risk exposes both sortable score and human-readable category.
                return new ProjectMetricValue(numericValue, textValue, Confidence.Certain, UnknownState.Known);
            }

            /// <summary>
            /// Creates an unknown categorical project metric value.
            /// </summary>
            /// <param name="textValue">The categorical unknown value.</param>
            /// <param name="unknownReason">The reason the metric input is incomplete.</param>
            /// <returns>An unknown project metric value.</returns>
            internal static ProjectMetricValue Unknown(string textValue, string unknownReason)
            {
                // Unknown metrics remain queryable while making the missing prerequisite visible to callers.
                return new ProjectMetricValue(null, textValue, Confidence.Low, UnknownState.Unknown(unknownReason));
            }
        }
    }
}
