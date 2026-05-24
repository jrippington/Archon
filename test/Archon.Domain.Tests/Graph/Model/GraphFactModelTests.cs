using System.Reflection;
using System.Text.Json;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;
using Xunit;

namespace Archon.Domain.Tests.Graph.Model
{
    /// <summary>
    /// Verifies WP002 graph fact models, confidence values, unknown invariants, and representative serialization.
    /// </summary>
    public sealed class GraphFactModelTests
    {
        /// <summary>
        /// Verifies confidence stores and compares deterministic fractional values.
        /// </summary>
        [Fact]
        public void GraphFactConfidenceSupportsDeterministicComparison()
        {
            // Confidence values are normalized decimals so later rules can compare certainty consistently.
            Confidence low = new(0.25m);
            Confidence high = new(0.90m);

            Assert.True(low.CompareTo(high) < 0);
            Assert.Equal("0.25", low.ToString());
            Assert.Equal(Confidence.Certain, new Confidence(1.0m));
        }

        /// <summary>
        /// Verifies confidence rejects values outside the inclusive zero-to-one range.
        /// </summary>
        /// <param name="value">The invalid confidence value.</param>
        [Theory]
        [InlineData("-0.01")]
        [InlineData("1.01")]
        public void GraphFactConfidenceRejectsOutOfRangeValues(string value)
        {
            // Confidence must be bounded so downstream sorting and threshold logic remain predictable.
            Assert.Throws<ArgumentOutOfRangeException>(() => new Confidence(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));
        }

        /// <summary>
        /// Verifies unknown state requires a reason when unknown data is present.
        /// </summary>
        [Fact]
        public void UnknownStateRequiresReasonWhenUnknownDataIsPresent()
        {
            // Unknowns are valuable only when the model explains why the value could not be determined.
            Assert.Throws<ArgumentException>(() => UnknownState.Unknown("   "));
        }

        /// <summary>
        /// Verifies unknown knowledge requires an explicit unknown reason on node facts.
        /// </summary>
        [Fact]
        public void GraphFactNodeRequiresUnknownReasonForUnknownKnowledge()
        {
            // KnowledgeKind.Unknown must never be represented as a silent omission.
            Assert.Throws<ArgumentException>(() => new ArchitectureNode(
                StableKeyGenerator.ForSummary("snapshot://current", "Graph", "test"),
                StableKeyGenerator.ForProject("src/Customer.Api/Customer.Api.csproj"),
                NodeKind.Project,
                "Customer.Api",
                "Customer.Api",
                "customer api",
                language: "C#",
                projectStableKey: null,
                parentNodeStableKey: null,
                KnowledgeKind.Unknown,
                ownership: null,
                externalCategory: null,
                Confidence.Low,
                UnknownState.Known,
                primaryEvidenceStableKey: null,
                GraphMetadata.Empty,
                new Fingerprint("sha256:test")));
        }

        /// <summary>
        /// Verifies representative repository, solution, snapshot, node, edge, evidence, rule, finding, metric, and summary models can be constructed.
        /// </summary>
        [Fact]
        public void GraphFactModelsCanConstructRepresentativeSnapshotFacts()
        {
            // The representative object graph exercises the full domain contract without requiring Neo4j IDs.
            RepositoryModel repository = CreateRepository();
            SolutionModel solution = CreateSolution(repository.StableKey);
            SnapshotHeader snapshot = CreateSnapshot(repository.StableKey);
            EvidenceRecord evidence = CreateEvidence(snapshot.StableKey);
            ArchitectureNode node = CreateNode(snapshot.StableKey, evidence.StableKey);
            ArchitectureEdge edge = CreateEdge(snapshot.StableKey, node.StableKey, evidence.StableKey);
            RuleDefinition rule = CreateRule();
            FindingRecord finding = CreateFinding(snapshot.StableKey, node.StableKey, evidence.StableKey);
            MetricRecord metric = CreateMetric(snapshot.StableKey, node.StableKey, evidence.StableKey);
            GeneratedSummary summary = CreateSummary(snapshot.StableKey, node.StableKey);

            Assert.Equal("Customer.Api", repository.Name);
            Assert.Equal(repository.StableKey, solution.RepositoryStableKey);
            Assert.Contains("No warnings", snapshot.Warnings);
            Assert.Equal(EvidenceKind.ProjectFile, evidence.EvidenceKind);
            Assert.Equal(NodeKind.Project, node.NodeKind);
            Assert.Equal(EdgeKind.DependsOn, edge.EdgeKind);
            Assert.Equal(RuleCategory.Lifecycle, rule.Category);
            Assert.Equal(FindingStatus.Open, finding.Status);
            Assert.Equal(MetricScopeKind.Node, metric.ScopeKind);
            Assert.Equal(SummaryKind.Node, summary.SummaryKind);
        }

        /// <summary>
        /// Verifies architecture edges require both source and target node stable keys.
        /// </summary>
        [Fact]
        public void GraphFactEdgeRequiresSourceAndTargetStableKeys()
        {
            // Edge endpoints are mandatory because a relationship without endpoints is not a graph fact.
            StableKey snapshotKey = StableKeyGenerator.ForSummary("snapshot://current", "Graph", "test");
            StableKey nodeKey = StableKeyGenerator.ForProject("src/Customer.Api/Customer.Api.csproj");

            Assert.Throws<ArgumentException>(() => new ArchitectureEdge(
                snapshotKey,
                StableKeyGenerator.ForMetric("snapshot://current", "edge", "missing-source"),
                EdgeKind.DependsOn,
                default,
                nodeKey,
                isDirect: true,
                KnowledgeKind.Fact,
                Confidence.Certain,
                UnknownState.Known,
                primaryEvidenceStableKey: null,
                GraphMetadata.Empty,
                new Fingerprint("sha256:test")));
        }

        /// <summary>
        /// Verifies findings require non-empty rule code and rule version values.
        /// </summary>
        [Fact]
        public void GraphFactFindingRequiresRuleCodeAndVersion()
        {
            // Findings must preserve the exact rule identity that produced them for historical explainability.
            StableKey snapshotKey = StableKeyGenerator.ForSummary("snapshot://current", "Graph", "test");

            Assert.Throws<ArgumentException>(() => new FindingRecord(
                snapshotKey,
                StableKeyGenerator.ForFinding("snapshot://current", "ARCHON001", "target"),
                ruleCode: "   ",
                ruleVersion: "1.0.0",
                FindingSeverity.High,
                FindingStatus.Open,
                "Unsupported framework",
                "Target framework is unsupported.",
                KnowledgeKind.Fact,
                Confidence.High,
                primaryNodeStableKey: null,
                primaryEvidenceStableKey: null,
                firstSeenSnapshotStableKey: null,
                latestSeenSnapshotStableKey: null,
                suppressionReason: null,
                suppressedBy: null,
                affectedNodeStableKeys: [],
                evidenceStableKeys: [],
                historyKey: "history://finding/test",
                GraphMetadata.Empty,
                new Fingerprint("sha256:test")));
        }

        /// <summary>
        /// Verifies metrics require either a numeric or text value.
        /// </summary>
        [Fact]
        public void GraphFactMetricRequiresNumericOrTextValue()
        {
            // A metric without any value cannot support reporting or snapshot diff.
            StableKey snapshotKey = StableKeyGenerator.ForSummary("snapshot://current", "Graph", "test");

            Assert.Throws<ArgumentException>(() => new MetricRecord(
                snapshotKey,
                StableKeyGenerator.ForMetric("snapshot://current", "ProjectCount", "Graph"),
                metricKind: "Count",
                MetricScopeKind.Graph,
                nodeStableKey: null,
                edgeStableKey: null,
                primaryEvidenceStableKey: null,
                name: "ProjectCount",
                numericValue: null,
                textValue: null,
                unit: "count",
                GraphMetadata.Empty,
                new Fingerprint("sha256:test")));
        }

        /// <summary>
        /// Verifies representative JSON serialization preserves controlled-value strings and explicit unknown-state fields.
        /// </summary>
        [Fact]
        public void GraphFactJsonSerializationPreservesStableStringsAndUnknownFields()
        {
            // Serialization should expose stable strings and explicit unknown fields without requiring persistence IDs.
            ArchitectureNode node = CreateNode(StableKeyGenerator.ForSummary("snapshot://current", "Graph", "test"), StableKeyGenerator.ForFile("src/Customer.Api/Customer.Api.csproj"));
            string json = JsonSerializer.Serialize(node);

            Assert.Contains("\"NodeKind\":\"Project\"", json);
            Assert.Contains("\"KnowledgeKind\":\"Fact\"", json);
            Assert.Contains("\"HasUnknownData\":false", json);
            Assert.DoesNotContain("\"Id\"", json);
        }

        /// <summary>
        /// Verifies graph fact models do not expose Neo4j or database-local identifier properties.
        /// </summary>
        /// <param name="modelType">The graph fact model type to inspect.</param>
        [Theory]
        [InlineData(typeof(RepositoryModel))]
        [InlineData(typeof(SolutionModel))]
        [InlineData(typeof(SnapshotHeader))]
        [InlineData(typeof(ArchitectureNode))]
        [InlineData(typeof(ArchitectureEdge))]
        [InlineData(typeof(EvidenceRecord))]
        [InlineData(typeof(RuleDefinition))]
        [InlineData(typeof(FindingRecord))]
        [InlineData(typeof(MetricRecord))]
        [InlineData(typeof(GeneratedSummary))]
        public void GraphFactModelsDoNotRequireNeo4jIds(Type modelType)
        {
            // WP002 domain contracts must be independent of persistence-store local identifiers.
            PropertyInfo? idProperty = modelType.GetProperty("Id", BindingFlags.Instance | BindingFlags.Public);

            Assert.Null(idProperty);
        }

        /// <summary>
        /// Creates a representative repository model for graph fact tests.
        /// </summary>
        /// <returns>A repository model.</returns>
        private static RepositoryModel CreateRepository()
        {
            // Repository facts are independent of one extraction snapshot.
            return new RepositoryModel(
                StableKeyGenerator.ForRepository("customer-suite"),
                "Customer.Api",
                "D:/Repositories/CustomerSuite",
                remoteUrl: "https://example.invalid/customer-suite.git",
                defaultBranch: "main",
                GraphMetadata.Empty);
        }

        /// <summary>
        /// Creates a representative solution model for graph fact tests.
        /// </summary>
        /// <param name="repositoryStableKey">The repository stable key associated with the solution.</param>
        /// <returns>A solution model.</returns>
        private static SolutionModel CreateSolution(StableKey repositoryStableKey)
        {
            // Solution identity uses a repository-relative path while retaining repository association.
            return new SolutionModel(
                repositoryStableKey,
                StableKeyGenerator.ForSolution("src/Customer.sln"),
                "Customer.sln",
                RepositoryRelativePath.Parse("src/Customer.sln"),
                GraphMetadata.Empty);
        }

        /// <summary>
        /// Creates a representative snapshot header for graph fact tests.
        /// </summary>
        /// <param name="repositoryStableKey">The repository stable key scoped by the snapshot.</param>
        /// <returns>A snapshot header model.</returns>
        private static SnapshotHeader CreateSnapshot(StableKey repositoryStableKey)
        {
            // Snapshot headers scope all graph facts emitted by one extraction run.
            return new SnapshotHeader(
                StableKeyGenerator.ForSummary("repository://customer-suite", "Snapshot", "2026-05-20"),
                repositoryStableKey,
                branchName: "main",
                commitSha: "abcdef",
                startedUtc: new DateTimeOffset(2026, 5, 20, 8, 0, 0, TimeSpan.Zero),
                completedUtc: new DateTimeOffset(2026, 5, 20, 8, 1, 0, TimeSpan.Zero),
                extractionVersion: "1.0.0",
                status: "Completed",
                warnings: ["No warnings"],
                errors: [],
                GraphMetadata.Empty);
        }

        /// <summary>
        /// Creates a representative architecture node for graph fact tests.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the node.</param>
        /// <param name="evidenceStableKey">The primary evidence stable key for the node.</param>
        /// <returns>An architecture node model.</returns>
        private static ArchitectureNode CreateNode(StableKey snapshotStableKey, StableKey evidenceStableKey)
        {
            // Nodes represent extracted architecture concepts such as projects, types, endpoints, and UI routes.
            return new ArchitectureNode(
                snapshotStableKey,
                StableKeyGenerator.ForProject("src/Customer.Api/Customer.Api.csproj"),
                NodeKind.Project,
                "Customer.Api",
                "Customer.Api",
                "customer api",
                language: "C#",
                projectStableKey: null,
                parentNodeStableKey: null,
                KnowledgeKind.Fact,
                ownership: "Platform",
                externalCategory: null,
                Confidence.Certain,
                UnknownState.Known,
                evidenceStableKey,
                GraphMetadata.Empty,
                FingerprintGenerator.ForNode(NodeKind.Project, "Customer.Api", "Customer.Api", "customer api", KnowledgeKind.Fact, GraphMetadata.Empty));
        }

        /// <summary>
        /// Creates a representative architecture edge for graph fact tests.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the edge.</param>
        /// <param name="sourceNodeStableKey">The source node stable key.</param>
        /// <param name="evidenceStableKey">The primary evidence stable key for the edge.</param>
        /// <returns>An architecture edge model.</returns>
        private static ArchitectureEdge CreateEdge(StableKey snapshotStableKey, StableKey sourceNodeStableKey, StableKey evidenceStableKey)
        {
            // Edges represent relationships between architecture nodes.
            StableKey targetNodeStableKey = StableKeyGenerator.ForProject("src/Customer.Application/Customer.Application.csproj");
            return new ArchitectureEdge(
                snapshotStableKey,
                StableKeyGenerator.ForMetric("snapshot://current", "edge", "project-dependency"),
                EdgeKind.DependsOn,
                sourceNodeStableKey,
                targetNodeStableKey,
                isDirect: true,
                KnowledgeKind.Fact,
                Confidence.High,
                UnknownState.Known,
                evidenceStableKey,
                GraphMetadata.Empty,
                FingerprintGenerator.ForEdge(EdgeKind.DependsOn, sourceNodeStableKey, targetNodeStableKey, true, KnowledgeKind.Fact, GraphMetadata.Empty));
        }

        /// <summary>
        /// Creates a representative evidence record for graph fact tests.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the evidence.</param>
        /// <returns>An evidence model.</returns>
        private static EvidenceRecord CreateEvidence(StableKey snapshotStableKey)
        {
            // Evidence explains where an architecture claim came from.
            return new EvidenceRecord(
                snapshotStableKey,
                StableKeyGenerator.ForFile("src/Customer.Api/Customer.Api.csproj"),
                EvidenceKind.ProjectFile,
                RepositoryRelativePath.Parse("src/Customer.Api/Customer.Api.csproj"),
                startLine: 1,
                endLine: 1,
                symbolName: "ProjectReference",
                containingSymbol: null,
                snippetHash: "abc123",
                snippetPreview: "<ProjectReference />",
                KnowledgeKind.Fact,
                Confidence.Certain,
                UnknownState.Known,
                GraphMetadata.Empty,
                FingerprintGenerator.ForEvidence(EvidenceKind.ProjectFile, "src/Customer.Api/Customer.Api.csproj", 1, 1, "ProjectReference", KnowledgeKind.Fact, GraphMetadata.Empty));
        }

        /// <summary>
        /// Creates a representative rule definition for graph fact tests.
        /// </summary>
        /// <returns>A rule definition model.</returns>
        private static RuleDefinition CreateRule()
        {
            // Rule definitions model catalog metadata; rule loading and evaluation happen in later work packages.
            return new RuleDefinition(
                ruleCode: "ARCHON001",
                name: "Unsupported target framework",
                RuleCategory.Lifecycle,
                FindingSeverity.High,
                FindingStatus.Open,
                enabled: true,
                version: "1.0.0",
                description: "Detects unsupported target frameworks.",
                definitionJson: "{}",
                sourceUrls: ["https://example.invalid/rules/ARCHON001"],
                isBuiltIn: true,
                ownerScope: "Archon",
                GraphMetadata.Empty);
        }

        /// <summary>
        /// Creates a representative finding record for graph fact tests.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the finding.</param>
        /// <param name="nodeStableKey">The primary node stable key for the finding.</param>
        /// <param name="evidenceStableKey">The primary evidence stable key for the finding.</param>
        /// <returns>A finding model.</returns>
        private static FindingRecord CreateFinding(StableKey snapshotStableKey, StableKey nodeStableKey, StableKey evidenceStableKey)
        {
            // Findings preserve rule identity, confidence, status, and evidence linkage.
            return new FindingRecord(
                snapshotStableKey,
                StableKeyGenerator.ForFinding("snapshot://current", "ARCHON001", nodeStableKey.Value),
                "ARCHON001",
                "1.0.0",
                FindingSeverity.High,
                FindingStatus.Open,
                "Unsupported target framework",
                "Project targets an unsupported framework.",
                KnowledgeKind.Fact,
                Confidence.High,
                nodeStableKey,
                evidenceStableKey,
                firstSeenSnapshotStableKey: null,
                latestSeenSnapshotStableKey: null,
                suppressionReason: null,
                suppressedBy: null,
                affectedNodeStableKeys: [nodeStableKey],
                evidenceStableKeys: [evidenceStableKey],
                historyKey: "history://finding/ARCHON001/unsupported-target-framework",
                GraphMetadata.Empty,
                FingerprintGenerator.ForFinding("ARCHON001", "1.0.0", FindingSeverity.High, FindingStatus.Open, "Unsupported target framework", KnowledgeKind.Fact, GraphMetadata.Empty));
        }

        /// <summary>
        /// Creates a representative metric record for graph fact tests.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the metric.</param>
        /// <param name="nodeStableKey">The node stable key scoped by the metric.</param>
        /// <param name="evidenceStableKey">The primary evidence stable key for the metric.</param>
        /// <returns>A metric model.</returns>
        private static MetricRecord CreateMetric(StableKey snapshotStableKey, StableKey nodeStableKey, StableKey evidenceStableKey)
        {
            // Metrics are snapshot outputs with numeric or textual values.
            return new MetricRecord(
                snapshotStableKey,
                StableKeyGenerator.ForMetric("snapshot://current", "ProjectReferenceCount", nodeStableKey.Value),
                metricKind: "Count",
                MetricScopeKind.Node,
                nodeStableKey,
                edgeStableKey: null,
                evidenceStableKey,
                name: "ProjectReferenceCount",
                numericValue: 1,
                textValue: null,
                unit: "count",
                GraphMetadata.Empty,
                FingerprintGenerator.ForMetric("ProjectReferenceCount", MetricScopeKind.Node, nodeStableKey.Value, GraphMetadata.Empty));
        }

        /// <summary>
        /// Creates a representative generated summary for graph fact tests.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the summary.</param>
        /// <param name="targetStableKey">The target stable key described by the summary.</param>
        /// <returns>A generated summary model.</returns>
        private static GeneratedSummary CreateSummary(StableKey snapshotStableKey, StableKey targetStableKey)
        {
            // Generated summaries are content contracts only; markdown export occurs in later packages.
            return new GeneratedSummary(
                snapshotStableKey,
                StableKeyGenerator.ForSummary("snapshot://current", "Node", targetStableKey.Value),
                SummaryKind.Node,
                targetStableKey,
                format: "Markdown",
                title: "Customer.Api summary",
                content: "Customer.Api depends on Customer.Application.",
                GraphMetadata.Empty,
                FingerprintGenerator.ForGeneratedSummary(SummaryKind.Node, "Customer.Api summary", "Markdown", "Customer.Api depends on Customer.Application.", GraphMetadata.Empty));
        }
    }
}
