using Archon.Application.Extraction.Contracts;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;

namespace Archon.Infrastructure.Neo4j.Tests.Persistence
{
    /// <summary>
    /// Builds representative full mixed snapshots for WP003 end-to-end Neo4j persistence validation.
    /// </summary>
    /// <remarks>
    /// The builder deliberately lives in the Neo4j test project because it describes a validation scenario rather than production
    /// domain behavior. It uses WP002 stable-key and fingerprint contracts for every graph fact so tests exercise the same logical
    /// identities that later extractors, API modules, MCP resources, and diff workflows will depend on.
    /// </remarks>
    internal static class FullMixedSnapshotTestDataBuilder
    {
        /// <summary>
        /// Creates a full mixed architecture snapshot containing every graph section persisted by WP003.
        /// </summary>
        /// <param name="suffix">The unique suffix used to isolate stable keys, fingerprints, and descriptive fields for one test run.</param>
        /// <returns>A complete extracted architecture snapshot with repositories, solutions, nodes, relationships, evidence, rules, findings, metrics, and generated summaries.</returns>
        public static ExtractedArchitectureSnapshot Create(string suffix)
        {
            // The full mixed fixture models a small repository with one application project, one package dependency, one endpoint,
            // evidence for each fact category, one rule, one finding, two metrics, and three summaries. That breadth proves the writer
            // can coordinate all WP003 graph sections in one transaction rather than only in isolated per-slice snapshots.
            StableKey repositoryStableKey = new($"repository://{suffix}");
            StableKey solutionStableKey = new($"solution://{suffix}");
            StableKey snapshotStableKey = new($"snapshot://{suffix}");
            StableKey projectNodeStableKey = new($"project://{suffix}/app");
            StableKey packageNodeStableKey = new($"package://{suffix}/neo4j");
            StableKey endpointNodeStableKey = new($"endpoint://{suffix}/health");
            StableKey projectEvidenceStableKey = new($"evidence://{suffix}/project-file");
            StableKey packageEvidenceStableKey = new($"evidence://{suffix}/package-reference");
            StableKey endpointEvidenceStableKey = new($"evidence://{suffix}/endpoint");
            StableKey relationshipStableKey = new($"edge://{suffix}/project-uses-package");
            StableKey endpointRelationshipStableKey = new($"edge://{suffix}/project-exposes-endpoint");

            RepositoryModel repository = CreateRepository(repositoryStableKey, suffix);
            SolutionModel solution = CreateSolution(repositoryStableKey, solutionStableKey, suffix);
            SnapshotHeader header = CreateHeader(repositoryStableKey, snapshotStableKey, suffix);
            EvidenceRecord projectEvidence = CreateEvidence(snapshotStableKey, projectEvidenceStableKey, suffix, "src/App/App.csproj", "Project", "<Project />", "project-file");
            EvidenceRecord packageEvidence = CreateEvidence(snapshotStableKey, packageEvidenceStableKey, suffix, "src/App/App.csproj", "PackageReference", "<PackageReference Include=\"Neo4j.Driver\" />", "package-reference");
            EvidenceRecord endpointEvidence = CreateEvidence(snapshotStableKey, endpointEvidenceStableKey, suffix, "src/App/Program.cs", "MapHealthChecks", "app.MapHealthChecks(\"/health\");", "endpoint");
            ArchitectureNode projectNode = CreateNode(snapshotStableKey, projectNodeStableKey, NodeKind.Project, "Application Project", projectEvidenceStableKey, suffix, "project");
            ArchitectureNode packageNode = CreateNode(snapshotStableKey, packageNodeStableKey, NodeKind.Package, "Neo4j.Driver Package", packageEvidenceStableKey, suffix, "package");
            ArchitectureNode endpointNode = CreateNode(snapshotStableKey, endpointNodeStableKey, NodeKind.Endpoint, "Health Endpoint", endpointEvidenceStableKey, suffix, "endpoint");
            ArchitectureEdge packageRelationship = CreateEdge(snapshotStableKey, relationshipStableKey, EdgeKind.UsesPackage, projectNodeStableKey, packageNodeStableKey, packageEvidenceStableKey, suffix, "uses-package");
            ArchitectureEdge endpointRelationship = CreateEdge(snapshotStableKey, endpointRelationshipStableKey, EdgeKind.DeclaresEndpoint, projectNodeStableKey, endpointNodeStableKey, endpointEvidenceStableKey, suffix, "declares-endpoint");
            RuleDefinition rule = CreateRuleDefinition("ARCHON001", "1.0.0", suffix);
            FindingRecord finding = CreateFinding(snapshotStableKey, projectNodeStableKey, projectEvidenceStableKey, suffix);
            MetricRecord relationshipMetric = CreateMetric(snapshotStableKey, new StableKey($"metric://{suffix}/dependency-count"), projectNodeStableKey, relationshipStableKey, packageEvidenceStableKey, "DependencyCount", MetricScopeKind.Edge, 2m, "relationships", suffix, "dependency-count");
            MetricRecord projectMetric = CreateMetric(snapshotStableKey, new StableKey($"metric://{suffix}/project-health"), projectNodeStableKey, null, projectEvidenceStableKey, "ProjectHealth", MetricScopeKind.Node, 1m, "score", suffix, "project-health");
            GeneratedSummary snapshotSummary = CreateGeneratedSummary(snapshotStableKey, new StableKey($"summary://{suffix}/snapshot"), snapshotStableKey, SummaryKind.Snapshot, "Snapshot summary", "The snapshot contains project, package, endpoint, rule, finding, metric, and summary data.", suffix, "snapshot");
            GeneratedSummary nodeSummary = CreateGeneratedSummary(snapshotStableKey, new StableKey($"summary://{suffix}/project"), projectNodeStableKey, SummaryKind.Node, "Project summary", "The application project depends on Neo4j and exposes a health endpoint.", suffix, "project");
            GeneratedSummary relationshipSummary = CreateGeneratedSummary(snapshotStableKey, new StableKey($"summary://{suffix}/relationship"), relationshipStableKey, SummaryKind.Edge, "Relationship summary", "The relationship explains the Neo4j package dependency.", suffix, "relationship");

            return new ExtractedArchitectureSnapshot(
                header,
                new[] { repository },
                new[] { solution },
                new[] { projectNode, packageNode, endpointNode },
                new[] { packageRelationship, endpointRelationship },
                new[] { projectEvidence, packageEvidence, endpointEvidence },
                new[] { rule },
                new[] { finding },
                new[] { relationshipMetric, projectMetric },
                new[] { snapshotSummary, nodeSummary, relationshipSummary },
                new[] { "Full mixed snapshot warning retained for persistence validation." },
                Array.Empty<string>());
        }

        /// <summary>
        /// Creates a repository graph fact for the full mixed snapshot.
        /// </summary>
        /// <param name="stableKey">The stable key that identifies the repository independent of database state.</param>
        /// <param name="suffix">The unique suffix used in descriptive fields and metadata.</param>
        /// <returns>A repository model with deterministic metadata.</returns>
        private static RepositoryModel CreateRepository(StableKey stableKey, string suffix)
        {
            // Repository metadata remains extension data while stable key and branch stay as first-class properties.
            return new RepositoryModel(
                stableKey,
                $"Full Mixed Repository {suffix}",
                $"D:/Dev/{suffix}",
                $"https://example.invalid/{suffix}.git",
                "main",
                GraphMetadata.From(new Dictionary<string, object?> { ["fixture"] = "full-mixed", ["suffix"] = suffix }));
        }

        /// <summary>
        /// Creates a solution graph fact owned by the supplied repository.
        /// </summary>
        /// <param name="repositoryStableKey">The stable key of the repository that owns the solution.</param>
        /// <param name="stableKey">The stable key that identifies the solution.</param>
        /// <param name="suffix">The unique suffix used in descriptive fields.</param>
        /// <returns>A solution model with a repository-relative path.</returns>
        private static SolutionModel CreateSolution(StableKey repositoryStableKey, StableKey stableKey, string suffix)
        {
            // The solution path is repository-relative so persisted identity remains deterministic across developer machines.
            return new SolutionModel(repositoryStableKey, stableKey, $"Full Mixed Solution {suffix}", RepositoryRelativePath.Parse($"src/{suffix}.slnx"), GraphMetadata.Empty);
        }

        /// <summary>
        /// Creates the snapshot header that scopes every snapshot-scoped graph fact in the fixture.
        /// </summary>
        /// <param name="repositoryStableKey">The stable key of the repository extracted by the snapshot.</param>
        /// <param name="snapshotStableKey">The stable key that identifies the snapshot.</param>
        /// <param name="suffix">The unique suffix used in metadata and extraction version fields.</param>
        /// <returns>A snapshot header with deterministic timestamps, warnings, and fingerprint-queryable metadata.</returns>
        private static SnapshotHeader CreateHeader(StableKey repositoryStableKey, StableKey snapshotStableKey, string suffix)
        {
            // Fixed timestamps and deterministic diagnostics make full mixed assertions repeatable under Testcontainers.
            return new SnapshotHeader(
                snapshotStableKey,
                repositoryStableKey,
                "main",
                "abc123-full-mixed",
                new DateTimeOffset(2025, 3, 4, 5, 6, 7, TimeSpan.Zero),
                new DateTimeOffset(2025, 3, 4, 5, 7, 7, TimeSpan.Zero),
                $"wp003-{suffix}",
                "Completed",
                new[] { "Full mixed snapshot warning retained for persistence validation." },
                Array.Empty<string>(),
                GraphMetadata.From(new Dictionary<string, object?> { ["fixture"] = "full-mixed" }));
        }

        /// <summary>
        /// Creates one evidence graph fact for the full mixed snapshot.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that scopes the evidence.</param>
        /// <param name="stableKey">The stable key that identifies the evidence within the snapshot.</param>
        /// <param name="suffix">The unique suffix used in deterministic fingerprints.</param>
        /// <param name="path">The repository-relative file path containing the evidence.</param>
        /// <param name="symbolName">The source symbol or artifact name associated with the evidence.</param>
        /// <param name="snippetPreview">The short source snippet persisted for developer explanation.</param>
        /// <param name="fingerprintSuffix">The unique suffix used to distinguish evidence fingerprints.</param>
        /// <returns>An evidence record with first-class source-location properties.</returns>
        private static EvidenceRecord CreateEvidence(StableKey snapshotStableKey, StableKey stableKey, string suffix, string path, string symbolName, string snippetPreview, string fingerprintSuffix)
        {
            // Evidence carries enough source-location detail for the full mixed query to prove every support link is explainable.
            return new EvidenceRecord(
                snapshotStableKey,
                stableKey,
                EvidenceKind.SourceCode,
                RepositoryRelativePath.Parse(path),
                1,
                8,
                symbolName,
                null,
                $"snippet-{suffix}-{fingerprintSuffix}",
                snippetPreview,
                KnowledgeKind.Fact,
                Confidence.Certain,
                UnknownState.Known,
                GraphMetadata.Empty,
                new Fingerprint($"sha256:evidence-{suffix}-{fingerprintSuffix}"));
        }

        /// <summary>
        /// Creates an architecture node graph fact for the full mixed snapshot.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that scopes the node.</param>
        /// <param name="stableKey">The stable key that identifies the node within the snapshot.</param>
        /// <param name="nodeKind">The controlled node kind stored as a first-class graph property.</param>
        /// <param name="displayName">The developer-facing node name.</param>
        /// <param name="evidenceStableKey">The primary evidence stable key that supports the node.</param>
        /// <param name="suffix">The unique suffix used in deterministic fingerprints.</param>
        /// <param name="fingerprintSuffix">The unique suffix used to distinguish node fingerprints.</param>
        /// <returns>An architecture node with stable identity, evidence, and fingerprint values populated.</returns>
        private static ArchitectureNode CreateNode(StableKey snapshotStableKey, StableKey stableKey, NodeKind nodeKind, string displayName, StableKey evidenceStableKey, string suffix, string fingerprintSuffix)
        {
            // Node facts use first-class stable keys and evidence references so the writer can create support links deterministically.
            return new ArchitectureNode(
                snapshotStableKey,
                stableKey,
                nodeKind,
                displayName,
                displayName,
                displayName.ToLowerInvariant(),
                "C#",
                null,
                null,
                KnowledgeKind.Fact,
                "Architecture",
                null,
                Confidence.Certain,
                UnknownState.Known,
                evidenceStableKey,
                GraphMetadata.Empty,
                new Fingerprint($"sha256:node-{suffix}-{fingerprintSuffix}"));
        }

        /// <summary>
        /// Creates an architecture relationship graph fact for the full mixed snapshot.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that scopes the relationship.</param>
        /// <param name="stableKey">The stable key that identifies the relationship-node fact.</param>
        /// <param name="edgeKind">The controlled edge kind stored on the relationship node.</param>
        /// <param name="sourceNodeStableKey">The stable key of the source architecture node.</param>
        /// <param name="targetNodeStableKey">The stable key of the target architecture node.</param>
        /// <param name="evidenceStableKey">The primary evidence stable key supporting the relationship.</param>
        /// <param name="suffix">The unique suffix used in deterministic fingerprints.</param>
        /// <param name="fingerprintSuffix">The unique suffix used to distinguish relationship fingerprints.</param>
        /// <returns>An architecture edge with stable endpoints, evidence, and fingerprint values populated.</returns>
        private static ArchitectureEdge CreateEdge(StableKey snapshotStableKey, StableKey stableKey, EdgeKind edgeKind, StableKey sourceNodeStableKey, StableKey targetNodeStableKey, StableKey evidenceStableKey, string suffix, string fingerprintSuffix)
        {
            // Relationship facts are persisted as ArchonRelationship nodes so evidence and target links can be queried directly.
            return new ArchitectureEdge(
                snapshotStableKey,
                stableKey,
                edgeKind,
                sourceNodeStableKey,
                targetNodeStableKey,
                true,
                KnowledgeKind.Fact,
                Confidence.Certain,
                UnknownState.Known,
                evidenceStableKey,
                GraphMetadata.Empty,
                new Fingerprint($"sha256:edge-{suffix}-{fingerprintSuffix}"));
        }

        /// <summary>
        /// Creates a versioned rule catalog graph fact for the full mixed snapshot.
        /// </summary>
        /// <param name="ruleCode">The stable rule code that identifies the rule family.</param>
        /// <param name="version">The version that identifies this catalog entry.</param>
        /// <param name="suffix">The unique suffix used in descriptive fields and metadata.</param>
        /// <returns>A rule definition using rule code plus version as global upsert identity.</returns>
        private static RuleDefinition CreateRuleDefinition(string ruleCode, string version, string suffix)
        {
            // The rule is global catalog data, not a snapshot-scoped copy, so it intentionally has no snapshot stable key.
            return new RuleDefinition(
                ruleCode,
                $"Layering rule {suffix}",
                RuleCategory.ArchitectureLayering,
                FindingSeverity.High,
                FindingStatus.Open,
                true,
                version,
                "Detects invalid dependencies in the architecture graph.",
                "{\"type\":\"layering\",\"scope\":\"full-mixed\"}",
                new[] { "https://example.invalid/rules/ARCHON001" },
                true,
                "platform",
                GraphMetadata.From(new Dictionary<string, object?> { ["fixture"] = "full-mixed" }));
        }

        /// <summary>
        /// Creates a finding graph fact linked to the rule, project node, and project evidence.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that scopes the finding.</param>
        /// <param name="nodeStableKey">The primary architecture node stable key referenced by the finding.</param>
        /// <param name="evidenceStableKey">The primary evidence stable key referenced by the finding.</param>
        /// <param name="suffix">The unique suffix used in stable keys and fingerprints.</param>
        /// <returns>A finding record with rule, node, evidence, suppression, and fingerprint data populated.</returns>
        private static FindingRecord CreateFinding(StableKey snapshotStableKey, StableKey nodeStableKey, StableKey evidenceStableKey, string suffix)
        {
            // The finding proves Work Item 8 can traverse from persisted concerns to rule provenance, primary node, and evidence.
            return new FindingRecord(
                snapshotStableKey,
                new StableKey($"finding://{suffix}/invalid-dependency"),
                "ARCHON001",
                "1.0.0",
                FindingSeverity.High,
                FindingStatus.Open,
                "Invalid dependency",
                "The application project depends on a forbidden layer in the representative mixed graph.",
                KnowledgeKind.Fact,
                Confidence.Certain,
                UnknownState.Known,
                nodeStableKey,
                evidenceStableKey,
                snapshotStableKey,
                snapshotStableKey,
                null,
                null,
                [nodeStableKey],
                [evidenceStableKey],
                $"history://finding/{suffix}/invalid-dependency",
                GraphMetadata.Empty,
                new Fingerprint($"sha256:finding-{suffix}"));
        }

        /// <summary>
        /// Creates a metric graph fact linked to optional node, relationship, and evidence targets.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that scopes the metric.</param>
        /// <param name="stableKey">The stable key that identifies the metric within the snapshot.</param>
        /// <param name="nodeStableKey">The optional architecture node target stable key.</param>
        /// <param name="relationshipStableKey">The optional architecture relationship target stable key.</param>
        /// <param name="evidenceStableKey">The primary evidence stable key supporting the metric.</param>
        /// <param name="metricKind">The stable metric-kind string stored as a first-class property.</param>
        /// <param name="scopeKind">The controlled metric scope kind stored as a first-class property.</param>
        /// <param name="numericValue">The numeric metric value to persist.</param>
        /// <param name="unit">The metric unit stored with the value.</param>
        /// <param name="suffix">The unique suffix used in deterministic fingerprints.</param>
        /// <param name="fingerprintSuffix">The unique suffix used to distinguish metric fingerprints.</param>
        /// <returns>A metric record with stable targets, evidence, value fields, and fingerprint populated.</returns>
        private static MetricRecord CreateMetric(StableKey snapshotStableKey, StableKey stableKey, StableKey? nodeStableKey, StableKey? relationshipStableKey, StableKey evidenceStableKey, string metricKind, MetricScopeKind scopeKind, decimal numericValue, string unit, string suffix, string fingerprintSuffix)
        {
            // Metrics exercise both target link paths: node-only metrics and relationship-targeted metrics with evidence.
            return new MetricRecord(
                snapshotStableKey,
                stableKey,
                metricKind,
                scopeKind,
                nodeStableKey,
                relationshipStableKey,
                evidenceStableKey,
                metricKind,
                numericValue,
                numericValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                unit,
                GraphMetadata.Empty,
                new Fingerprint($"sha256:metric-{suffix}-{fingerprintSuffix}"));
        }

        /// <summary>
        /// Creates a generated-summary graph fact linked to a snapshot, node, or relationship target.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that scopes the generated summary.</param>
        /// <param name="stableKey">The stable key that identifies the generated summary within the snapshot.</param>
        /// <param name="targetStableKey">The snapshot, node, or relationship stable key described by the summary.</param>
        /// <param name="summaryKind">The controlled summary kind stored as a first-class property.</param>
        /// <param name="title">The persisted title of the generated content.</param>
        /// <param name="content">The persisted generated content.</param>
        /// <param name="suffix">The unique suffix used in deterministic fingerprints.</param>
        /// <param name="fingerprintSuffix">The unique suffix used to distinguish summary fingerprints.</param>
        /// <returns>A generated summary with target identity, content, and fingerprint values populated.</returns>
        private static GeneratedSummary CreateGeneratedSummary(StableKey snapshotStableKey, StableKey stableKey, StableKey targetStableKey, SummaryKind summaryKind, string title, string content, string suffix, string fingerprintSuffix)
        {
            // Summaries prove the writer can link durable narrative outputs to snapshots, nodes, and relationship-node targets.
            return new GeneratedSummary(
                snapshotStableKey,
                stableKey,
                summaryKind,
                targetStableKey,
                "Markdown",
                title,
                content,
                GraphMetadata.Empty,
                new Fingerprint($"sha256:summary-{suffix}-{fingerprintSuffix}"));
        }
    }
}
