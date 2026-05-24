using Archon.Application.ArchitectureRules;
using Archon.Application.Extraction.Contracts;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;
using Archon.Domain.Graph.Metrics;
using Xunit;

namespace Archon.Application.Tests.ArchitectureRules
{
    /// <summary>
    /// Verifies WP013 architecture-rule evaluation converts graph, metric, semantic, and catalog facts into deterministic rule results.
    /// </summary>
    public sealed class ArchitectureRuleEvaluatorTests
    {
        /// <summary>
        /// Confirms built-in layering checks detect domain-to-infrastructure, domain-to-web, and non-web-to-web dependency directions.
        /// </summary>
        [Fact]
        public void Evaluate_WhenLayeringDependenciesViolateGenericRules_ShouldReturnEvidenceBackedViolations()
        {
            // The fixture names projects with generic layer terms so the evaluator can apply source-brief layering checks without organization-specific policy.
            StableKey snapshotStableKey = new("snapshot://architecture-rules-layering");
            StableKey domainKey = ProjectKey("Ordering.Domain");
            StableKey infrastructureKey = ProjectKey("Ordering.Infrastructure");
            StableKey webKey = ProjectKey("Ordering.Web");
            StableKey servicesKey = ProjectKey("Ordering.Services");
            ArchitectureEdge domainToInfrastructure = CreateEdge(snapshotStableKey, "edge://domain-infra", domainKey, infrastructureKey, "evidence://domain-infra");
            ArchitectureEdge domainToWeb = CreateEdge(snapshotStableKey, "edge://domain-web", domainKey, webKey, "evidence://domain-web");
            ArchitectureEdge servicesToWeb = CreateEdge(snapshotStableKey, "edge://services-web", servicesKey, webKey, "evidence://services-web");
            ExtractedArchitectureSnapshot snapshot = CreateSnapshot(
                snapshotStableKey,
                [
                    CreateProjectNode(snapshotStableKey, domainKey, "Ordering.Domain", LayerMetadata("Domain")),
                    CreateProjectNode(snapshotStableKey, infrastructureKey, "Ordering.Infrastructure", LayerMetadata("Infrastructure")),
                    CreateProjectNode(snapshotStableKey, webKey, "Ordering.Web", LayerMetadata("Web")),
                    CreateProjectNode(snapshotStableKey, servicesKey, "Ordering.Services", LayerMetadata("Services"))
                ],
                [domainToInfrastructure, domainToWeb, servicesToWeb],
                [],
                [],
                []);
            ArchitectureRuleEvaluator evaluator = new();

            IReadOnlyList<ArchitectureRuleResult> results = evaluator.Evaluate(snapshot, ArchitectureRuleEvaluationOptions.Default);

            Assert.Contains(results, result => result.RuleCode == ArchitectureRuleChecks.DomainReferencesInfrastructure && result.TargetStableKey == domainKey && result.ContributingEdgeStableKeys.Contains(domainToInfrastructure.StableKey) && result.Status == ArchitectureRuleResultStatus.Violation);
            Assert.Contains(results, result => result.RuleCode == ArchitectureRuleChecks.DomainReferencesWeb && result.TargetStableKey == domainKey && result.ContributingEdgeStableKeys.Contains(domainToWeb.StableKey) && result.Status == ArchitectureRuleResultStatus.Violation);
            Assert.Contains(results, result => result.RuleCode == ArchitectureRuleChecks.WebReferencedByNonWeb && result.TargetStableKey == webKey && result.ContributingEdgeStableKeys.Contains(servicesToWeb.StableKey) && result.Status == ArchitectureRuleResultStatus.Violation);
            Assert.All(results, result => Assert.StartsWith("architecture-rule://snapshot://architecture-rules-layering/", result.StableKey.Value, StringComparison.Ordinal));
            Assert.All(results, result => Assert.StartsWith("sha256:", result.Fingerprint.Value, StringComparison.Ordinal));
        }

        /// <summary>
        /// Confirms data-access checks detect direct LINQ to SQL and DataContext usage while respecting configured allowances.
        /// </summary>
        [Fact]
        public void Evaluate_WhenDataAccessUsageIsConfigured_ShouldRespectPolicyLikeAllowances()
        {
            // Direct data-access policy is intentionally configurable because some repositories may temporarily allow legacy usage during migration.
            StableKey snapshotStableKey = new("snapshot://architecture-rules-data-access");
            StableKey applicationKey = ProjectKey("Billing.Application");
            StableKey controllerKey = new("type://Billing.Web/InvoiceController");
            ArchitectureNode applicationNode = CreateProjectNode(snapshotStableKey, applicationKey, "Billing.Application", GraphMetadata.From(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["architecture.layer"] = "Application",
                ["semantic.namespaces"] = new[] { "System.Data.Linq", "Billing.Application" },
                ["semantic.methodCalls"] = new[] { "System.Data.Linq.DataContext.SubmitChanges" }
            }));
            ArchitectureNode controllerNode = CreateNode(snapshotStableKey, controllerKey, NodeKind.Controller, "InvoiceController", GraphMetadata.From(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["architecture.layer"] = "Web",
                ["semantic.methodCalls"] = new[] { "BillingDataContext.SubmitChanges" },
                ["semantic.symbolName"] = "InvoiceController"
            }));
            ExtractedArchitectureSnapshot snapshot = CreateSnapshot(snapshotStableKey, [applicationNode, controllerNode], [], [], [], []);
            ArchitectureRuleEvaluator evaluator = new();

            IReadOnlyList<ArchitectureRuleResult> defaultResults = evaluator.Evaluate(snapshot, ArchitectureRuleEvaluationOptions.Default);
            ArchitectureRuleEvaluationOptions allowedOptions = ArchitectureRuleEvaluationOptions.Default with
            {
                AllowApplicationLinqToSqlDirectUse = true,
                AllowControllerDataContextDirectUse = true
            };
            IReadOnlyList<ArchitectureRuleResult> allowedResults = evaluator.Evaluate(snapshot, allowedOptions);

            Assert.Contains(defaultResults, result => result.RuleCode == ArchitectureRuleChecks.ApplicationUsesLinqToSqlDirectly && result.TargetStableKey == applicationKey && result.Status == ArchitectureRuleResultStatus.Violation);
            Assert.Contains(defaultResults, result => result.RuleCode == ArchitectureRuleChecks.ControllerUsesDataContextDirectly && result.TargetStableKey == controllerKey && result.Status == ArchitectureRuleResultStatus.Violation);
            Assert.DoesNotContain(allowedResults, result => result.RuleCode == ArchitectureRuleChecks.ApplicationUsesLinqToSqlDirectly);
            Assert.DoesNotContain(allowedResults, result => result.RuleCode == ArchitectureRuleChecks.ControllerUsesDataContextDirectly);
        }

        /// <summary>
        /// Confirms worker messaging and shared-library review checks use unknown-state, metric, hotspot, finding, and catalog semantics.
        /// </summary>
        [Fact]
        public void Evaluate_WhenRuntimeAndMetricFactsApply_ShouldReturnWorkerUnknownAndSharedLibraryReviewResults()
        {
            // The worker declares runtime evidence that it should process messages but has no queue/topic edge, while the shared library crosses the fan-in review threshold.
            StableKey snapshotStableKey = new("snapshot://architecture-rules-runtime");
            StableKey workerKey = ProjectKey("Import.Worker");
            StableKey sharedKey = ProjectKey("Platform.Shared");
            MetricRecord fanInMetric = CreateMetric(snapshotStableKey, sharedKey, MetricDefinitions.GraphFanIn.Kind, 12);
            FindingRecord finding = CreateFinding(snapshotStableKey, sharedKey, "finding://shared-library/review");
            ArchitectureNode workerNode = CreateProjectNode(snapshotStableKey, workerKey, "Import.Worker", GraphMetadata.From(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["architecture.layer"] = "Worker",
                ["runtime.workerKind"] = "BackgroundWorker",
                ["runtime.messagingExpected"] = true
            }));
            ArchitectureNode sharedNode = CreateProjectNode(snapshotStableKey, sharedKey, "Platform.Shared", LayerMetadata("Shared"));
            ExtractedArchitectureSnapshot snapshot = CreateSnapshot(snapshotStableKey, [workerNode, sharedNode], [], [fanInMetric], [finding], []);
            ArchitectureRuleEvaluator evaluator = new();

            IReadOnlyList<ArchitectureRuleResult> results = evaluator.Evaluate(snapshot, ArchitectureRuleEvaluationOptions.Default);

            ArchitectureRuleResult workerResult = Assert.Single(results, result => result.RuleCode == ArchitectureRuleChecks.WorkerMissingQueueOrTopicDependency);
            Assert.Equal(ArchitectureRuleResultStatus.Unknown, workerResult.Status);
            Assert.True(workerResult.UnknownState.HasUnknownData);
            Assert.Contains("queue or topic", workerResult.UnknownState.UnknownReason, StringComparison.OrdinalIgnoreCase);
            ArchitectureRuleResult sharedResult = Assert.Single(results, result => result.RuleCode == ArchitectureRuleChecks.SharedLibraryHighFanInReview);
            Assert.Equal(ArchitectureRuleResultStatus.ReviewRequired, sharedResult.Status);
            Assert.Contains(fanInMetric.StableKey, sharedResult.ContributingMetricStableKeys);
            Assert.Contains(finding.StableKey, sharedResult.ContributingFindingStableKeys);
            Assert.Equal(sharedKey, sharedResult.TargetStableKey);
        }

        /// <summary>
        /// Confirms configured rule definitions can disable built-in checks without hard-coding organization-specific exceptions.
        /// </summary>
        [Fact]
        public void Evaluate_WhenCatalogDisablesBuiltInRule_ShouldNotReturnThatRuleResult()
        {
            // RuleDefinition.Enabled represents the persisted catalog state, so a disabled matching rule suppresses that built-in check while other checks remain available.
            StableKey snapshotStableKey = new("snapshot://architecture-rules-catalog");
            StableKey domainKey = ProjectKey("Catalog.Domain");
            StableKey infrastructureKey = ProjectKey("Catalog.Infrastructure");
            ArchitectureEdge domainToInfrastructure = CreateEdge(snapshotStableKey, "edge://catalog-domain-infra", domainKey, infrastructureKey, "evidence://catalog-domain-infra");
            RuleDefinition disabledRule = CreateRuleDefinition(ArchitectureRuleChecks.DomainReferencesInfrastructure, enabled: false);
            ExtractedArchitectureSnapshot snapshot = CreateSnapshot(
                snapshotStableKey,
                [CreateProjectNode(snapshotStableKey, domainKey, "Catalog.Domain", LayerMetadata("Domain")), CreateProjectNode(snapshotStableKey, infrastructureKey, "Catalog.Infrastructure", LayerMetadata("Infrastructure"))],
                [domainToInfrastructure],
                [],
                [],
                [disabledRule]);
            ArchitectureRuleEvaluator evaluator = new();

            IReadOnlyList<ArchitectureRuleResult> results = evaluator.Evaluate(snapshot, ArchitectureRuleEvaluationOptions.Default);

            Assert.DoesNotContain(results, result => result.RuleCode == ArchitectureRuleChecks.DomainReferencesInfrastructure);
        }

        /// <summary>
        /// Creates the standard project stable key used by architecture-rule tests.
        /// </summary>
        /// <param name="projectName">The project name without a file extension.</param>
        /// <returns>A deterministic project stable key.</returns>
        private static StableKey ProjectKey(string projectName)
        {
            // Stable project keys mirror the extraction convention sufficiently for rule tests without loading real project files.
            return new StableKey($"project://src/{projectName}/{projectName}.csproj");
        }

        /// <summary>
        /// Creates metadata containing one architecture layer classification.
        /// </summary>
        /// <param name="layer">The generic architecture layer name.</param>
        /// <returns>Metadata suitable for an architecture node fixture.</returns>
        private static GraphMetadata LayerMetadata(string layer)
        {
            // The evaluator reads generic layer metadata rather than organization-owned policy names.
            return GraphMetadata.From(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["architecture.layer"] = layer
            });
        }

        /// <summary>
        /// Creates an extracted snapshot fixture for architecture-rule evaluation.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key.</param>
        /// <param name="nodes">The architecture nodes to include.</param>
        /// <param name="edges">The architecture edges to include.</param>
        /// <param name="metrics">The metrics to include.</param>
        /// <param name="findings">The findings to include.</param>
        /// <param name="rules">The configured rule definitions to include.</param>
        /// <returns>A deterministic extracted architecture snapshot.</returns>
        private static ExtractedArchitectureSnapshot CreateSnapshot(StableKey snapshotStableKey, IReadOnlyList<ArchitectureNode> nodes, IReadOnlyList<ArchitectureEdge> edges, IReadOnlyList<MetricRecord> metrics, IReadOnlyList<FindingRecord> findings, IReadOnlyList<RuleDefinition> rules)
        {
            // Snapshot fixtures include a header because architecture-rule identities are snapshot-scoped public keys.
            StableKey repositoryStableKey = new("repository://architecture-rules");
            SnapshotHeader header = new(snapshotStableKey, repositoryStableKey, "main", "abcdef", new DateTimeOffset(2026, 5, 20, 8, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 5, 20, 8, 1, 0, TimeSpan.Zero), "wp013-architecture-rule-tests", "Completed", warnings: [], errors: [], GraphMetadata.Empty);
            RepositoryModel repository = new(repositoryStableKey, "ArchitectureRules", "D:/Repositories/ArchitectureRules", null, "main", GraphMetadata.Empty);
            return new ExtractedArchitectureSnapshot(header, [repository], [], nodes, edges, [], rules, findings, metrics, [], [], []);
        }

        /// <summary>
        /// Creates a project node fixture.
        /// </summary>
        /// <param name="snapshotStableKey">The owning snapshot stable key.</param>
        /// <param name="nodeStableKey">The node stable key.</param>
        /// <param name="displayName">The display name.</param>
        /// <param name="metadata">The node metadata.</param>
        /// <returns>A project architecture node.</returns>
        private static ArchitectureNode CreateProjectNode(StableKey snapshotStableKey, StableKey nodeStableKey, string displayName, GraphMetadata metadata)
        {
            // Project node fixtures carry layer and semantic metadata consumed by built-in architecture checks.
            return CreateNode(snapshotStableKey, nodeStableKey, NodeKind.Project, displayName, metadata);
        }

        /// <summary>
        /// Creates a graph node fixture with a supplied node kind.
        /// </summary>
        /// <param name="snapshotStableKey">The owning snapshot stable key.</param>
        /// <param name="nodeStableKey">The node stable key.</param>
        /// <param name="nodeKind">The controlled node kind.</param>
        /// <param name="displayName">The display name.</param>
        /// <param name="metadata">The node metadata.</param>
        /// <returns>An architecture node fixture.</returns>
        private static ArchitectureNode CreateNode(StableKey snapshotStableKey, StableKey nodeStableKey, NodeKind nodeKind, string displayName, GraphMetadata metadata)
        {
            // The fingerprint includes metadata so tests exercise the same deterministic identity support as production graph facts.
            return new ArchitectureNode(snapshotStableKey, nodeStableKey, nodeKind, displayName, displayName, displayName.ToLowerInvariant(), "C#", projectStableKey: nodeKind == NodeKind.Project ? null : nodeStableKey, parentNodeStableKey: null, KnowledgeKind.Fact, ownership: null, externalCategory: null, Confidence.Certain, UnknownState.Known, primaryEvidenceStableKey: null, metadata, FingerprintGenerator.ForNode(nodeKind, displayName, displayName, displayName.ToLowerInvariant(), KnowledgeKind.Fact, metadata));
        }

        /// <summary>
        /// Creates a dependency edge fixture with optional evidence.
        /// </summary>
        /// <param name="snapshotStableKey">The owning snapshot stable key.</param>
        /// <param name="edgeStableKey">The edge stable key.</param>
        /// <param name="sourceNodeStableKey">The source node stable key.</param>
        /// <param name="targetNodeStableKey">The target node stable key.</param>
        /// <param name="evidenceStableKey">The evidence stable key that explains the edge.</param>
        /// <returns>An architecture edge fixture.</returns>
        private static ArchitectureEdge CreateEdge(StableKey snapshotStableKey, string edgeStableKey, StableKey sourceNodeStableKey, StableKey targetNodeStableKey, string evidenceStableKey)
        {
            // References edges represent project dependency direction for generic layering rules.
            return new ArchitectureEdge(snapshotStableKey, new StableKey(edgeStableKey), EdgeKind.References, sourceNodeStableKey, targetNodeStableKey, isDirect: true, KnowledgeKind.Fact, Confidence.Certain, UnknownState.Known, new StableKey(evidenceStableKey), GraphMetadata.Empty, FingerprintGenerator.ForEdge(EdgeKind.References, sourceNodeStableKey, targetNodeStableKey, true, KnowledgeKind.Fact, GraphMetadata.Empty));
        }

        /// <summary>
        /// Creates a metric fixture used by metric-dependent checks.
        /// </summary>
        /// <param name="snapshotStableKey">The owning snapshot stable key.</param>
        /// <param name="nodeStableKey">The target node stable key.</param>
        /// <param name="metricKind">The metric kind.</param>
        /// <param name="numericValue">The metric numeric value.</param>
        /// <returns>A metric record fixture.</returns>
        private static MetricRecord CreateMetric(StableKey snapshotStableKey, StableKey nodeStableKey, string metricKind, decimal numericValue)
        {
            // Metric stable keys are included in architecture-rule result contribution fields.
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["testMetricKind"] = metricKind
            });
            return new MetricRecord(snapshotStableKey, new StableKey($"metric://{snapshotStableKey.Value}/{metricKind}/{nodeStableKey.Value}"), metricKind, MetricScopeKind.Node, nodeStableKey, edgeStableKey: null, primaryEvidenceStableKey: null, metricKind, numericValue, textValue: null, "edges", Confidence.Certain, UnknownState.Known, metadata, FingerprintGenerator.ForMetric(metricKind, MetricScopeKind.Node, nodeStableKey.Value, numericValue, null, "edges", false, null, metadata));
        }

        /// <summary>
        /// Creates a finding fixture used by architecture-rule contribution checks.
        /// </summary>
        /// <param name="snapshotStableKey">The owning snapshot stable key.</param>
        /// <param name="nodeStableKey">The affected node stable key.</param>
        /// <param name="findingStableKey">The finding stable key.</param>
        /// <returns>A finding record fixture.</returns>
        private static FindingRecord CreateFinding(StableKey snapshotStableKey, StableKey nodeStableKey, string findingStableKey)
        {
            // Shared-library review results preserve already-persisted finding references when a shared target is also on the hotlist.
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["architectureRuleTest"] = true
            });
            return new FindingRecord(snapshotStableKey, new StableKey(findingStableKey), "ARCHON-SHARED-REVIEW", "1.0.0", FindingSeverity.Medium, FindingStatus.Open, "Shared review finding", "A finding that contributes to shared-library review.", KnowledgeKind.Inference, Confidence.High, UnknownState.Known, nodeStableKey, primaryEvidenceStableKey: null, snapshotStableKey, snapshotStableKey, suppressionReason: null, suppressedBy: null, [nodeStableKey], [], findingStableKey, metadata, FingerprintGenerator.ForFinding("ARCHON-SHARED-REVIEW", "1.0.0", FindingSeverity.Medium, FindingStatus.Open, "Shared review finding", KnowledgeKind.Inference, metadata));
        }

        /// <summary>
        /// Creates a rule definition fixture that can enable or disable a built-in architecture check.
        /// </summary>
        /// <param name="ruleCode">The built-in rule code.</param>
        /// <param name="enabled">Indicates whether the rule is enabled.</param>
        /// <returns>A rule definition fixture.</returns>
        private static RuleDefinition CreateRuleDefinition(string ruleCode, bool enabled)
        {
            // Rule definitions model persisted catalog semantics without requiring JSON catalog loading in evaluator tests.
            return new RuleDefinition(ruleCode, "Architecture rule " + ruleCode, RuleCategory.ArchitectureLayering, FindingSeverity.High, FindingStatus.Open, enabled, "1.0.0", "Configured architecture rule.", "{\"ruleCode\":\"" + ruleCode + "\"}", [], isBuiltIn: true, ownerScope: null, GraphMetadata.Empty);
        }
    }
}
