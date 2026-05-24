using Archon.Application.Extraction.Accumulation;
using Archon.Application.Extraction.Contracts;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;
using Xunit;

namespace Archon.Application.Tests.Extraction.Accumulation
{
    /// <summary>
    /// Verifies the WP002 application-layer snapshot accumulator can assemble extraction contributions without persistence or host dependencies.
    /// </summary>
    public sealed class ArchitectureSnapshotAccumulatorTests
    {
        /// <summary>
        /// Verifies a representative extraction snapshot preserves every required section from repository through diagnostics.
        /// </summary>
        [Fact]
        public void AccumulationBuildsRepresentativeSnapshotWithEverySection()
        {
            // The representative path models one future extractor slice contributing each graph fact type into an in-memory snapshot.
            ArchitectureSnapshotAccumulator accumulator = new();
            RepresentativeFacts facts = RepresentativeFacts.Create();

            accumulator.SetSnapshotHeader(facts.Snapshot);
            accumulator.AddRepository(facts.Repository);
            accumulator.AddSolution(facts.Solution);
            accumulator.AddNode(facts.Node);
            accumulator.AddEdge(facts.Edge);
            accumulator.AddEvidence(facts.Evidence);
            accumulator.AddFinding(facts.Finding);
            accumulator.AddMetric(facts.Metric);
            accumulator.AddGeneratedSummary(facts.Summary);
            accumulator.AddWarning("Package restore completed with warnings.");
            accumulator.AddError("Optional analyzer failed.");

            ExtractedArchitectureSnapshot snapshot = accumulator.ToSnapshot();

            Assert.Equal(facts.Snapshot, snapshot.SnapshotHeader);
            Assert.Single(snapshot.Repositories);
            Assert.Single(snapshot.Solutions);
            Assert.Single(snapshot.Nodes);
            Assert.Single(snapshot.Edges);
            Assert.Single(snapshot.Evidence);
            Assert.Single(snapshot.Findings);
            Assert.Single(snapshot.Metrics);
            Assert.Single(snapshot.GeneratedSummaries);
            Assert.Contains("Package restore completed with warnings.", snapshot.Warnings);
            Assert.Contains("Optional analyzer failed.", snapshot.Errors);
        }

        /// <summary>
        /// Verifies keyed sections replace duplicate stable keys deterministically while preserving stable-key ordering.
        /// </summary>
        [Fact]
        public void AccumulationReplacesDuplicateStableKeysAndOrdersOutputDeterministically()
        {
            // Latest contribution wins for stable-keyed facts so extractor slices can refine a fact without duplicating it.
            ArchitectureSnapshotAccumulator accumulator = new();
            RepositoryModel firstRepository = new(
                StableKeyGenerator.ForRepository("customer-suite"),
                "Customer.Api",
                "D:/Repositories/CustomerSuite",
                remoteUrl: null,
                defaultBranch: "main",
                GraphMetadata.Empty);
            RepositoryModel replacementRepository = new(
                StableKeyGenerator.ForRepository("customer-suite"),
                "Customer.Api Replacement",
                "D:/Repositories/CustomerSuite",
                remoteUrl: "https://example.invalid/customer-suite.git",
                defaultBranch: "main",
                GraphMetadata.Empty);
            RepositoryModel secondRepository = new(
                StableKeyGenerator.ForRepository("billing-suite"),
                "Billing.Api",
                "D:/Repositories/BillingSuite",
                remoteUrl: null,
                defaultBranch: "main",
                GraphMetadata.Empty);

            accumulator.AddRepository(firstRepository);
            accumulator.AddRepository(secondRepository);
            accumulator.AddRepository(replacementRepository);

            ExtractedArchitectureSnapshot snapshot = accumulator.ToSnapshot();

            Assert.Equal(2, snapshot.Repositories.Count);
            Assert.Equal("Customer.Api Replacement", snapshot.Repositories.Single(repository => repository.StableKey == firstRepository.StableKey).Name);
            Assert.Equal(["repository://billing-suite", "repository://customer-suite"], snapshot.Repositories.Select(repository => repository.StableKey.Value).ToArray());
        }

        /// <summary>
        /// Verifies warning and error diagnostics are preserved as streams rather than stable-keyed facts.
        /// </summary>
        [Fact]
        public void AccumulationPreservesWarningsAndErrorsInInsertionOrder()
        {
            // Diagnostics are intentionally not de-duplicated because repeated warnings can explain multiple extractor observations.
            ArchitectureSnapshotAccumulator accumulator = new();

            accumulator.AddWarning("First warning");
            accumulator.AddWarning("   ");
            accumulator.AddWarning("First warning");
            accumulator.AddError("First error");
            accumulator.AddError("Second error");

            ExtractedArchitectureSnapshot snapshot = accumulator.ToSnapshot();

            Assert.Equal(["First warning", "First warning"], snapshot.Warnings);
            Assert.Equal(["First error", "Second error"], snapshot.Errors);
        }

        /// <summary>
        /// Verifies accumulation can merge an existing snapshot into another accumulator.
        /// </summary>
        [Fact]
        public void AccumulationMergesExistingSnapshotSections()
        {
            // Merge supports future extractor orchestration that combines partial snapshots from independent contributors.
            RepresentativeFacts facts = RepresentativeFacts.Create();
            ExtractedArchitectureSnapshot partialSnapshot = new(
                facts.Snapshot,
                [facts.Repository],
                [facts.Solution],
                [facts.Node],
                [facts.Edge],
                [facts.Evidence],
                [],
                [facts.Finding],
                [facts.Metric],
                [facts.Summary],
                ["Merged warning"],
                ["Merged error"]);
            ArchitectureSnapshotAccumulator accumulator = new();

            accumulator.Merge(partialSnapshot);
            ExtractedArchitectureSnapshot mergedSnapshot = accumulator.ToSnapshot();

            Assert.Equal(facts.Snapshot, mergedSnapshot.SnapshotHeader);
            Assert.Equal(facts.Repository.StableKey, mergedSnapshot.Repositories.Single().StableKey);
            Assert.Equal(facts.Summary.StableKey, mergedSnapshot.GeneratedSummaries.Single().StableKey);
            Assert.Equal(["Merged warning"], mergedSnapshot.Warnings);
            Assert.Equal(["Merged error"], mergedSnapshot.Errors);
        }

        /// <summary>
        /// Verifies the application assembly remains independent of infrastructure, hosts, Roslyn, and Neo4j assemblies.
        /// </summary>
        [Fact]
        public void AccumulationContractsHaveNoPersistenceOrHostDependencies()
        {
            // The assembly reference check guards the WP002 boundary that accumulation is application contract behavior only.
            string[] referencedAssemblyNames = typeof(ArchitectureSnapshotAccumulator)
                .Assembly
                .GetReferencedAssemblies()
                .Select(assemblyName => assemblyName.Name ?? string.Empty)
                .ToArray();

            Assert.DoesNotContain(referencedAssemblyNames, assemblyName => assemblyName.Contains("Neo4j", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(referencedAssemblyNames, assemblyName => assemblyName.Contains("AspNetCore", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(referencedAssemblyNames, assemblyName => assemblyName.Contains("Roslyn", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(referencedAssemblyNames, assemblyName => assemblyName.Contains("Infrastructure", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Creates representative domain graph facts shared by accumulation tests.
        /// </summary>
        private sealed class RepresentativeFacts
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="RepresentativeFacts"/> class.
            /// </summary>
            /// <param name="repository">The representative repository fact.</param>
            /// <param name="solution">The representative solution fact.</param>
            /// <param name="snapshot">The representative snapshot header.</param>
            /// <param name="evidence">The representative evidence fact.</param>
            /// <param name="node">The representative node fact.</param>
            /// <param name="edge">The representative edge fact.</param>
            /// <param name="finding">The representative finding fact.</param>
            /// <param name="metric">The representative metric fact.</param>
            /// <param name="summary">The representative generated summary fact.</param>
            private RepresentativeFacts(
                RepositoryModel repository,
                SolutionModel solution,
                SnapshotHeader snapshot,
                EvidenceRecord evidence,
                ArchitectureNode node,
                ArchitectureEdge edge,
                FindingRecord finding,
                MetricRecord metric,
                GeneratedSummary summary)
            {
                // The fixture groups related immutable facts so tests can focus on accumulation behavior rather than setup noise.
                Repository = repository;
                Solution = solution;
                Snapshot = snapshot;
                Evidence = evidence;
                Node = node;
                Edge = edge;
                Finding = finding;
                Metric = metric;
                Summary = summary;
            }

            /// <summary>
            /// Gets the representative repository fact.
            /// </summary>
            internal RepositoryModel Repository { get; }

            /// <summary>
            /// Gets the representative solution fact.
            /// </summary>
            internal SolutionModel Solution { get; }

            /// <summary>
            /// Gets the representative snapshot header.
            /// </summary>
            internal SnapshotHeader Snapshot { get; }

            /// <summary>
            /// Gets the representative evidence fact.
            /// </summary>
            internal EvidenceRecord Evidence { get; }

            /// <summary>
            /// Gets the representative node fact.
            /// </summary>
            internal ArchitectureNode Node { get; }

            /// <summary>
            /// Gets the representative edge fact.
            /// </summary>
            internal ArchitectureEdge Edge { get; }

            /// <summary>
            /// Gets the representative finding fact.
            /// </summary>
            internal FindingRecord Finding { get; }

            /// <summary>
            /// Gets the representative metric fact.
            /// </summary>
            internal MetricRecord Metric { get; }

            /// <summary>
            /// Gets the representative generated summary fact.
            /// </summary>
            internal GeneratedSummary Summary { get; }

            /// <summary>
            /// Creates a complete representative graph fact set for accumulation tests.
            /// </summary>
            /// <returns>A representative fact set.</returns>
            internal static RepresentativeFacts Create()
            {
                // This setup mirrors the domain graph fact test objects while remaining local to application accumulation tests.
                RepositoryModel repository = new(
                    StableKeyGenerator.ForRepository("customer-suite"),
                    "Customer.Api",
                    "D:/Repositories/CustomerSuite",
                    remoteUrl: "https://example.invalid/customer-suite.git",
                    defaultBranch: "main",
                    GraphMetadata.Empty);
                SolutionModel solution = new(
                    repository.StableKey,
                    StableKeyGenerator.ForSolution("src/Customer.sln"),
                    "Customer.sln",
                    RepositoryRelativePath.Parse("src/Customer.sln"),
                    GraphMetadata.Empty);
                SnapshotHeader snapshot = new(
                    StableKeyGenerator.ForSummary("repository://customer-suite", "Snapshot", "2026-05-20"),
                    repository.StableKey,
                    branchName: "main",
                    commitSha: "abcdef",
                    startedUtc: new DateTimeOffset(2026, 5, 20, 8, 0, 0, TimeSpan.Zero),
                    completedUtc: new DateTimeOffset(2026, 5, 20, 8, 1, 0, TimeSpan.Zero),
                    extractionVersion: "1.0.0",
                    status: "Completed",
                    warnings: [],
                    errors: [],
                    GraphMetadata.Empty);
                EvidenceRecord evidence = new(
                    snapshot.StableKey,
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
                ArchitectureNode node = new(
                    snapshot.StableKey,
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
                    evidence.StableKey,
                    GraphMetadata.Empty,
                    FingerprintGenerator.ForNode(NodeKind.Project, "Customer.Api", "Customer.Api", "customer api", KnowledgeKind.Fact, GraphMetadata.Empty));
                StableKey targetNodeStableKey = StableKeyGenerator.ForProject("src/Customer.Application/Customer.Application.csproj");
                ArchitectureEdge edge = new(
                    snapshot.StableKey,
                    StableKeyGenerator.ForMetric("snapshot://current", "edge", "project-dependency"),
                    EdgeKind.DependsOn,
                    node.StableKey,
                    targetNodeStableKey,
                    isDirect: true,
                    KnowledgeKind.Fact,
                    Confidence.High,
                    UnknownState.Known,
                    evidence.StableKey,
                    GraphMetadata.Empty,
                    FingerprintGenerator.ForEdge(EdgeKind.DependsOn, node.StableKey, targetNodeStableKey, true, KnowledgeKind.Fact, GraphMetadata.Empty));
                FindingRecord finding = new(
                    snapshot.StableKey,
                    StableKeyGenerator.ForFinding("snapshot://current", "ARCHON001", node.StableKey.Value),
                    "ARCHON001",
                    "1.0.0",
                    FindingSeverity.High,
                    FindingStatus.Open,
                    "Unsupported target framework",
                    "Project targets an unsupported framework.",
                    KnowledgeKind.Fact,
                    Confidence.High,
                    node.StableKey,
                    evidence.StableKey,
                    firstSeenSnapshotStableKey: null,
                    latestSeenSnapshotStableKey: null,
                    suppressionReason: null,
                    suppressedBy: null,
                    affectedNodeStableKeys: [node.StableKey],
                    evidenceStableKeys: [evidence.StableKey],
                    historyKey: "history://finding/ARCHON001/customer-api",
                    GraphMetadata.Empty,
                    FingerprintGenerator.ForFinding("ARCHON001", "1.0.0", FindingSeverity.High, FindingStatus.Open, "Unsupported target framework", KnowledgeKind.Fact, GraphMetadata.Empty));
                MetricRecord metric = new(
                    snapshot.StableKey,
                    StableKeyGenerator.ForMetric("snapshot://current", "ProjectReferenceCount", node.StableKey.Value),
                    metricKind: "Count",
                    MetricScopeKind.Node,
                    node.StableKey,
                    edgeStableKey: null,
                    evidence.StableKey,
                    name: "ProjectReferenceCount",
                    numericValue: 1,
                    textValue: null,
                    unit: "count",
                    GraphMetadata.Empty,
                    FingerprintGenerator.ForMetric("ProjectReferenceCount", MetricScopeKind.Node, node.StableKey.Value, GraphMetadata.Empty));
                GeneratedSummary summary = new(
                    snapshot.StableKey,
                    StableKeyGenerator.ForSummary("snapshot://current", "Node", node.StableKey.Value),
                    SummaryKind.Node,
                    node.StableKey,
                    format: "Markdown",
                    title: "Customer.Api summary",
                    content: "Customer.Api depends on Customer.Application.",
                    GraphMetadata.Empty,
                    FingerprintGenerator.ForGeneratedSummary(SummaryKind.Node, "Customer.Api summary", "Markdown", "Customer.Api depends on Customer.Application.", GraphMetadata.Empty));

                return new RepresentativeFacts(repository, solution, snapshot, evidence, node, edge, finding, metric, summary);
            }
        }
    }
}
