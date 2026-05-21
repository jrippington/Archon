using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;
using Archon.Infrastructure.Neo4j.Persistence;
using Xunit;

namespace Archon.Infrastructure.Neo4j.Tests.Persistence
{
    /// <summary>
    /// Verifies Neo4j snapshot persistence mapping for graph sections supported by the Neo4j writer.
    /// </summary>
    public sealed class MinimalSnapshotNeo4jSnapshotPersistenceMapperTests
    {
        /// <summary>
        /// Confirms repository and solution mappings expose required normalized properties and deterministic metadata JSON.
        /// </summary>
        [Fact]
        public void MapsRepositoryAndSolutionProperties()
        {
            // Repository and solution records are persisted before snapshot relationships, so their stable-key mappings must be exact.
            Neo4jSnapshotPersistenceMapper mapper = new();
            RepositoryModel repository = CreateRepository();
            SolutionModel solution = CreateSolution(repository.StableKey);

            IReadOnlyDictionary<string, object?> repositoryParameters = mapper.MapRepository(repository);
            IReadOnlyDictionary<string, object?> solutionParameters = mapper.MapSolution(solution);

            Assert.Equal("repository://archon", repositoryParameters["stableKey"]);
            Assert.Equal("Archon", repositoryParameters["name"]);
            Assert.Equal("{\"owner\":\"architecture\"}", repositoryParameters["metadataJson"]);
            Assert.Equal("repository://archon", solutionParameters["repositoryStableKey"]);
            Assert.Equal("solution://archon", solutionParameters["stableKey"]);
            Assert.Equal("src/Archon.slnx", solutionParameters["path"]);
        }

        /// <summary>
        /// Confirms snapshot mapping preserves normalized status, dates, diagnostics, and metadata.
        /// </summary>
        [Fact]
        public void MapsSnapshotHeaderProperties()
        {
            // Snapshot headers scope every minimal graph fact and carry extraction status details used by later query work.
            Neo4jSnapshotPersistenceMapper mapper = new();
            SnapshotHeader snapshot = CreateSnapshotHeader(new StableKey("repository://archon"), new StableKey("snapshot://one"));

            IReadOnlyDictionary<string, object?> parameters = mapper.MapSnapshot(snapshot);

            Assert.Equal("snapshot://one", parameters["stableKey"]);
            Assert.Equal("repository://archon", parameters["repositoryStableKey"]);
            Assert.Equal("main", parameters["branchName"]);
            Assert.Equal("Completed", parameters["status"]);
            Assert.Equal("[\"warning one\"]", parameters["warningsJson"]);
            Assert.Equal("[]", parameters["errorsJson"]);
        }

        /// <summary>
        /// Confirms architecture node mapping keeps query-critical fields as first-class parameters.
        /// </summary>
        [Fact]
        public void MapsArchitectureNodeProperties()
        {
            // Node mapping must not hide stable keys, kind, knowledge, confidence, or fingerprint inside metadata JSON.
            Neo4jSnapshotPersistenceMapper mapper = new();
            ArchitectureNode node = CreateNode(new StableKey("snapshot://one"), new StableKey("project://src/app"), new StableKey("evidence://project"));

            IReadOnlyDictionary<string, object?> parameters = mapper.MapNode(node);

            Assert.Equal("snapshot://one", parameters["snapshotStableKey"]);
            Assert.Equal("project://src/app", parameters["stableKey"]);
            Assert.Equal("Project", parameters["nodeKind"]);
            Assert.Equal("Fact", parameters["knowledgeKind"]);
            Assert.Equal(1.00m, parameters["confidence"]);
            Assert.Equal(false, parameters["hasUnknownData"]);
            Assert.Equal("evidence://project", parameters["primaryEvidenceStableKey"]);
            Assert.Equal("sha256:node", parameters["fingerprint"]);
        }

        /// <summary>
        /// Confirms semantic declaration node mapping preserves snapshot scope, symbol confidence, unknown state, evidence, and deterministic metadata.
        /// </summary>
        [Fact]
        public void MapsSemanticDeclarationNodeProperties()
        {
            // Semantic declaration facts flow through the same generic node contract, so Neo4j mapping must not need Roslyn-specific types.
            Neo4jSnapshotPersistenceMapper mapper = new();
            ArchitectureNode node = CreateSemanticDeclarationNode(
                new StableKey("snapshot://semantic"),
                new StableKey("semantic://declaration/type/customer-service"),
                new StableKey("project://Customer.Api.csproj"),
                new StableKey("evidence://semantic/type/customer-service"));

            IReadOnlyDictionary<string, object?> parameters = mapper.MapNode(node);

            Assert.Equal("snapshot://semantic", parameters["snapshotStableKey"]);
            Assert.Equal("semantic://declaration/type/customer-service", parameters["stableKey"]);
            Assert.Equal("Type", parameters["nodeKind"]);
            Assert.Equal("CustomerService", parameters["displayName"]);
            Assert.Equal("Customer.Api.CustomerService", parameters["qualifiedName"]);
            Assert.Equal("C#", parameters["language"]);
            Assert.Equal("project://Customer.Api.csproj", parameters["projectStableKey"]);
            Assert.Equal(0.90m, parameters["confidence"]);
            Assert.Equal(false, parameters["hasUnknownData"]);
            Assert.Equal("evidence://semantic/type/customer-service", parameters["primaryEvidenceStableKey"]);
            Assert.Equal("sha256:semantic-node", parameters["fingerprint"]);
            Assert.Equal("{\"semantic.confidenceCategory\":\"CompilerResolved\",\"semantic.declarationKind\":\"Type\",\"semantic.projectContext\":\"Customer.Api.csproj\",\"semantic.sourceLanguage\":\"CSharp\"}", parameters["metadataJson"]);
        }

        /// <summary>
        /// Confirms evidence mapping and deduplication identity are deterministic and snapshot-scoped.
        /// </summary>
        [Fact]
        public void MapsEvidenceAndBuildsSnapshotScopedDeduplicationKey()
        {
            // Identical evidence in one snapshot must share a key, while the same evidence in another snapshot must remain separate.
            Neo4jSnapshotPersistenceMapper mapper = new();
            EvidenceRecord first = CreateEvidence(new StableKey("snapshot://one"), new StableKey("evidence://one"));
            EvidenceRecord duplicatePayload = CreateEvidence(new StableKey("snapshot://one"), new StableKey("evidence://duplicate"));
            EvidenceRecord otherSnapshot = CreateEvidence(new StableKey("snapshot://two"), new StableKey("evidence://one"));

            IReadOnlyDictionary<string, object?> parameters = mapper.MapEvidence(first);
            string firstKey = mapper.GetEvidenceDeduplicationKey(first);
            string duplicateKey = mapper.GetEvidenceDeduplicationKey(duplicatePayload);
            string otherSnapshotKey = mapper.GetEvidenceDeduplicationKey(otherSnapshot);

            Assert.Equal("ProjectFile", parameters["evidenceKind"]);
            Assert.Equal("src/Archon/Archon.csproj", parameters["filePath"]);
            Assert.Equal("sha256:evidence", parameters["fingerprint"]);
            Assert.Equal(firstKey, duplicateKey);
            Assert.NotEqual(firstKey, otherSnapshotKey);
        }

        /// <summary>
        /// Confirms semantic evidence mapping persists source spans, symbol context, snippet data, and diagnostic metadata as generic evidence properties.
        /// </summary>
        [Fact]
        public void MapsSemanticEvidenceProperties()
        {
            // Evidence produced by semantic extraction must remain snapshot-scoped and source-addressable after infrastructure mapping.
            Neo4jSnapshotPersistenceMapper mapper = new();
            EvidenceRecord evidence = CreateSemanticEvidence(new StableKey("snapshot://semantic"), new StableKey("evidence://semantic/type/customer-service"));

            IReadOnlyDictionary<string, object?> parameters = mapper.MapEvidence(evidence);

            Assert.Equal("snapshot://semantic", parameters["snapshotStableKey"]);
            Assert.Equal("evidence://semantic/type/customer-service", parameters["stableKey"]);
            Assert.Equal("CompilerSymbol", parameters["evidenceKind"]);
            Assert.Equal("src/Customer.Api/CustomerService.cs", parameters["filePath"]);
            Assert.Equal(3, parameters["startLine"]);
            Assert.Equal(18, parameters["endLine"]);
            Assert.Equal("CustomerService", parameters["symbolName"]);
            Assert.Equal("Customer.Api", parameters["containingSymbol"]);
            Assert.Equal("semantic-snippet-hash", parameters["snippetHash"]);
            Assert.Equal("public sealed class CustomerService", parameters["snippetPreview"]);
            Assert.Equal("sha256:semantic-evidence", parameters["fingerprint"]);
            Assert.Equal("{\"semantic.sourceLanguage\":\"CSharp\"}", parameters["metadataJson"]);
        }

        /// <summary>
        /// Confirms architecture relationship mapping keeps relationship-node properties queryable as first-class fields.
        /// </summary>
        [Fact]
        public void MapsArchitectureRelationshipProperties()
        {
            // Relationship mapping uses an ArchonRelationship node rather than a dynamic Neo4j relationship type so every WP002 edge
            // kind can preserve stable identity, metadata, fingerprint, endpoint keys, and evidence references with one schema shape.
            Neo4jSnapshotPersistenceMapper mapper = new();
            ArchitectureEdge edge = CreateEdge(new StableKey("snapshot://one"), new StableKey("edge://project-references-package"), new StableKey("project://src/app"), new StableKey("package://neo4j"), new StableKey("evidence://project"));

            IReadOnlyDictionary<string, object?> parameters = mapper.MapRelationship(edge);

            Assert.Equal("snapshot://one", parameters["snapshotStableKey"]);
            Assert.Equal("edge://project-references-package", parameters["stableKey"]);
            Assert.Equal("REFERENCES", parameters["edgeKind"]);
            Assert.Equal("project://src/app", parameters["sourceNodeStableKey"]);
            Assert.Equal("package://neo4j", parameters["targetNodeStableKey"]);
            Assert.Equal(true, parameters["isDirect"]);
            Assert.Equal("Fact", parameters["knowledgeKind"]);
            Assert.Equal(1.00m, parameters["confidence"]);
            Assert.Equal("evidence://project", parameters["primaryEvidenceStableKey"]);
            Assert.Equal("sha256:edge", parameters["fingerprint"]);
        }

        /// <summary>
        /// Confirms semantic relationship mapping preserves the graph vocabulary, endpoint keys, confidence, unknown reason, evidence, and metadata.
        /// </summary>
        [Fact]
        public void MapsSemanticRelationshipProperties()
        {
            // Semantic relationships are persisted through the same edge shape, including degraded unknown state when Roslyn could only partially resolve a target.
            Neo4jSnapshotPersistenceMapper mapper = new();
            ArchitectureEdge edge = CreateSemanticRelationship(
                new StableKey("snapshot://semantic"),
                new StableKey("semantic://relationship/calls/get-name/name"),
                new StableKey("semantic://declaration/method/get-name"),
                new StableKey("semantic://declaration/property/name"),
                new StableKey("evidence://semantic/relationship/get-name"));

            IReadOnlyDictionary<string, object?> parameters = mapper.MapRelationship(edge);

            Assert.Equal("snapshot://semantic", parameters["snapshotStableKey"]);
            Assert.Equal("semantic://relationship/calls/get-name/name", parameters["stableKey"]);
            Assert.Equal("CALLS", parameters["edgeKind"]);
            Assert.Equal("semantic://declaration/method/get-name", parameters["sourceNodeStableKey"]);
            Assert.Equal("semantic://declaration/property/name", parameters["targetNodeStableKey"]);
            Assert.Equal(true, parameters["isDirect"]);
            Assert.Equal("Fact", parameters["knowledgeKind"]);
            Assert.Equal(0.5m, parameters["confidence"]);
            Assert.Equal(true, parameters["hasUnknownData"]);
            Assert.Equal("PartiallyResolved", parameters["unknownReason"]);
            Assert.Equal("evidence://semantic/relationship/get-name", parameters["primaryEvidenceStableKey"]);
            Assert.Equal("sha256:semantic-edge", parameters["fingerprint"]);
            Assert.Equal("{\"semantic.confidenceCategory\":\"PartiallyResolved\",\"semantic.relationshipKind\":\"Calls\"}", parameters["metadataJson"]);
        }

        /// <summary>
        /// Confirms rule catalog mapping preserves versioned rule identity and default finding behavior.
        /// </summary>
        [Fact]
        public void MapsRuleCatalogProperties()
        {
            // Rules are global catalog entries, so the mapper must expose rule code and version as first-class upsert keys.
            Neo4jSnapshotPersistenceMapper mapper = new();
            RuleDefinition rule = CreateRuleDefinition("ARCHON001", "1.0.0");

            IReadOnlyDictionary<string, object?> parameters = mapper.MapRule(rule);

            Assert.Equal("ARCHON001", parameters["ruleCode"]);
            Assert.Equal("1.0.0", parameters["ruleVersion"]);
            Assert.Equal("Layering rule", parameters["name"]);
            Assert.Equal("ArchitectureLayering", parameters["category"]);
            Assert.Equal("High", parameters["severity"]);
            Assert.Equal("Open", parameters["defaultStatus"]);
            Assert.Equal(true, parameters["enabled"]);
            Assert.Equal("[\"https://example.invalid/rules/ARCHON001\"]", parameters["sourceUrlsJson"]);
            Assert.Equal("platform", parameters["ownerScope"]);
            Assert.Equal("{\"ruleOwner\":\"architecture\"}", parameters["metadataJson"]);
        }

        /// <summary>
        /// Confirms finding mapping preserves snapshot scope, rule reference, suppression fields, and deterministic fingerprint data.
        /// </summary>
        [Fact]
        public void MapsFindingProperties()
        {
            // Findings are snapshot-scoped records that reference global rule versions and optionally point to node and evidence support.
            Neo4jSnapshotPersistenceMapper mapper = new();
            FindingRecord finding = CreateFinding(new StableKey("snapshot://one"), new StableKey("finding://one"), new StableKey("project://src/app"), new StableKey("evidence://project"));

            IReadOnlyDictionary<string, object?> parameters = mapper.MapFinding(finding);

            Assert.Equal("snapshot://one", parameters["snapshotStableKey"]);
            Assert.Equal("finding://one", parameters["stableKey"]);
            Assert.Equal("ARCHON001", parameters["ruleCode"]);
            Assert.Equal("1.0.0", parameters["ruleVersion"]);
            Assert.Equal("High", parameters["severity"]);
            Assert.Equal("Suppressed", parameters["status"]);
            Assert.Equal("Fact", parameters["knowledgeKind"]);
            Assert.Equal(1.00m, parameters["confidence"]);
            Assert.Equal("project://src/app", parameters["primaryNodeStableKey"]);
            Assert.Equal("evidence://project", parameters["primaryEvidenceStableKey"]);
            Assert.Equal("snapshot://one", parameters["firstSeenSnapshotStableKey"]);
            Assert.Equal("snapshot://one", parameters["latestSeenSnapshotStableKey"]);
            Assert.Equal("Accepted risk", parameters["suppressionReason"]);
            Assert.Equal("architecture-review", parameters["suppressedBy"]);
            Assert.Equal("sha256:finding", parameters["fingerprint"]);
        }

        /// <summary>
        /// Confirms metric mapping preserves scope, target references, values, evidence, and deterministic fingerprint data.
        /// </summary>
        [Fact]
        public void MapsMetricProperties()
        {
            // Metrics are snapshot-scoped computed facts, so target stable keys and value fields must remain first-class query properties.
            Neo4jSnapshotPersistenceMapper mapper = new();
            MetricRecord metric = CreateMetric(new StableKey("snapshot://one"), new StableKey("metric://one"), new StableKey("project://src/app"), new StableKey("edge://project-references-package"), new StableKey("evidence://project"));

            IReadOnlyDictionary<string, object?> parameters = mapper.MapMetric(metric);

            Assert.Equal("snapshot://one", parameters["snapshotStableKey"]);
            Assert.Equal("metric://one", parameters["stableKey"]);
            Assert.Equal("DependencyCount", parameters["metricKind"]);
            Assert.Equal("Edge", parameters["scopeKind"]);
            Assert.Equal("project://src/app", parameters["nodeStableKey"]);
            Assert.Equal("edge://project-references-package", parameters["edgeStableKey"]);
            Assert.Equal("evidence://project", parameters["primaryEvidenceStableKey"]);
            Assert.Equal("Outgoing dependency count", parameters["name"]);
            Assert.Equal(12.5m, parameters["numericValue"]);
            Assert.Equal("twelve point five", parameters["textValue"]);
            Assert.Equal("relationships", parameters["unit"]);
            Assert.Equal("sha256:metric", parameters["fingerprint"]);
        }

        /// <summary>
        /// Confirms generated-summary mapping preserves target identity, content fields, and deterministic fingerprint data.
        /// </summary>
        [Fact]
        public void MapsGeneratedSummaryProperties()
        {
            // Generated summaries are persisted as durable narrative outputs so later reporting does not need to regenerate content.
            Neo4jSnapshotPersistenceMapper mapper = new();
            GeneratedSummary summary = CreateGeneratedSummary(new StableKey("snapshot://one"), new StableKey("summary://one"), new StableKey("project://src/app"));

            IReadOnlyDictionary<string, object?> parameters = mapper.MapGeneratedSummary(summary);

            Assert.Equal("snapshot://one", parameters["snapshotStableKey"]);
            Assert.Equal("summary://one", parameters["stableKey"]);
            Assert.Equal("Node", parameters["summaryKind"]);
            Assert.Equal("project://src/app", parameters["targetStableKey"]);
            Assert.Equal("Markdown", parameters["format"]);
            Assert.Equal("Application project summary", parameters["title"]);
            Assert.Equal("The application project depends on Neo4j.", parameters["content"]);
            Assert.Equal("sha256:summary", parameters["fingerprint"]);
        }

        /// <summary>
        /// Creates a representative repository model for mapping tests.
        /// </summary>
        /// <returns>A repository model with deterministic metadata.</returns>
        private static RepositoryModel CreateRepository()
        {
            // The metadata key intentionally sorts deterministically so JSON assertions are stable.
            return new RepositoryModel(
                new StableKey("repository://archon"),
                "Archon",
                "D:/Dev/Archon",
                "https://example.invalid/archon.git",
                "main",
                GraphMetadata.From(new Dictionary<string, object?> { ["owner"] = "architecture" }));
        }

        /// <summary>
        /// Creates a representative solution model for mapping tests.
        /// </summary>
        /// <param name="repositoryStableKey">The stable key of the repository that owns the solution.</param>
        /// <returns>A solution model with deterministic metadata.</returns>
        private static SolutionModel CreateSolution(StableKey repositoryStableKey)
        {
            // The solution path is repository-relative to match the WP002 identity rules.
            return new SolutionModel(
                repositoryStableKey,
                new StableKey("solution://archon"),
                "Archon",
                RepositoryRelativePath.Parse("src/Archon.slnx"),
                GraphMetadata.Empty);
        }

        /// <summary>
        /// Creates a representative snapshot header for mapping tests.
        /// </summary>
        /// <param name="repositoryStableKey">The stable key of the repository extracted by the snapshot.</param>
        /// <param name="snapshotStableKey">The stable key of the snapshot to create.</param>
        /// <returns>A snapshot header with deterministic dates and diagnostics.</returns>
        private static SnapshotHeader CreateSnapshotHeader(StableKey repositoryStableKey, StableKey snapshotStableKey)
        {
            // Fixed timestamps and diagnostics make mapper assertions deterministic.
            return new SnapshotHeader(
                snapshotStableKey,
                repositoryStableKey,
                "main",
                "abc123",
                new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero),
                new DateTimeOffset(2025, 1, 2, 3, 5, 5, TimeSpan.Zero),
                "wp004-tests",
                "Completed",
                new[] { "warning one" },
                Array.Empty<string>(),
                GraphMetadata.Empty);
        }

        /// <summary>
        /// Creates a representative architecture node for mapping tests.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the node.</param>
        /// <param name="nodeStableKey">The stable key of the architecture node.</param>
        /// <param name="evidenceStableKey">The stable key of the node's primary evidence.</param>
        /// <returns>An architecture node with query-critical fields populated.</returns>
        private static ArchitectureNode CreateNode(StableKey snapshotStableKey, StableKey nodeStableKey, StableKey evidenceStableKey)
        {
            // The node uses known facts and high confidence so persistence can assert simple first-class properties.
            return new ArchitectureNode(
                snapshotStableKey,
                nodeStableKey,
                NodeKind.Project,
                "Archon.Project",
                "Archon.Project",
                "archon project",
                "C#",
                null,
                null,
                KnowledgeKind.Fact,
                null,
                null,
                Confidence.Certain,
                UnknownState.Known,
                evidenceStableKey,
                GraphMetadata.Empty,
                new Fingerprint("sha256:node"));
        }

        /// <summary>
        /// Creates a semantic declaration node using the generic architecture-node contract.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the node.</param>
        /// <param name="nodeStableKey">The stable key of the semantic declaration node.</param>
        /// <param name="projectStableKey">The stable key of the project that owns the declaration.</param>
        /// <param name="evidenceStableKey">The stable key of the declaration's primary semantic evidence.</param>
        /// <returns>An architecture node shaped like a projected Roslyn declaration.</returns>
        private static ArchitectureNode CreateSemanticDeclarationNode(StableKey snapshotStableKey, StableKey nodeStableKey, StableKey projectStableKey, StableKey evidenceStableKey)
        {
            // Semantic declarations remain generic graph nodes after projection; metadata carries Roslyn-specific classification details.
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["semantic.confidenceCategory"] = "CompilerResolved",
                ["semantic.declarationKind"] = "Type",
                ["semantic.projectContext"] = "Customer.Api.csproj",
                ["semantic.sourceLanguage"] = "CSharp"
            });
            return new ArchitectureNode(
                snapshotStableKey,
                nodeStableKey,
                NodeKind.Type,
                "CustomerService",
                "Customer.Api.CustomerService",
                "customer api customerservice",
                "C#",
                projectStableKey,
                null,
                KnowledgeKind.Fact,
                null,
                null,
                Confidence.High,
                UnknownState.Known,
                evidenceStableKey,
                metadata,
                new Fingerprint("sha256:semantic-node"));
        }

        /// <summary>
        /// Creates representative evidence for mapping and deduplication tests.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the evidence.</param>
        /// <param name="evidenceStableKey">The stable key of the evidence record.</param>
        /// <returns>An evidence record with deterministic content.</returns>
        private static EvidenceRecord CreateEvidence(StableKey snapshotStableKey, StableKey evidenceStableKey)
        {
            // StableKey is intentionally not part of the deduplication payload so equivalent evidence can collapse within a snapshot.
            return new EvidenceRecord(
                snapshotStableKey,
                evidenceStableKey,
                EvidenceKind.ProjectFile,
                RepositoryRelativePath.Parse("src/Archon/Archon.csproj"),
                1,
                5,
                "Archon",
                null,
                "snippet-hash",
                "<Project />",
                KnowledgeKind.Fact,
                Confidence.Certain,
                UnknownState.Known,
                GraphMetadata.Empty,
                new Fingerprint("sha256:evidence"));
        }

        /// <summary>
        /// Creates source evidence shaped like a Roslyn compiler-symbol evidence record.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the evidence.</param>
        /// <param name="evidenceStableKey">The stable key of the semantic evidence record.</param>
        /// <returns>An evidence record containing semantic source span and symbol data.</returns>
        private static EvidenceRecord CreateSemanticEvidence(StableKey snapshotStableKey, StableKey evidenceStableKey)
        {
            // The metadata is intentionally semantic-prefixed so infrastructure remains a generic mapper rather than a Roslyn-aware adapter.
            return new EvidenceRecord(
                snapshotStableKey,
                evidenceStableKey,
                EvidenceKind.CompilerSymbol,
                RepositoryRelativePath.Parse("src/Customer.Api/CustomerService.cs"),
                3,
                18,
                "CustomerService",
                "Customer.Api",
                "semantic-snippet-hash",
                "public sealed class CustomerService",
                KnowledgeKind.Fact,
                Confidence.High,
                UnknownState.Known,
                GraphMetadata.From(new Dictionary<string, object?> { ["semantic.sourceLanguage"] = "CSharp" }),
                new Fingerprint("sha256:semantic-evidence"));
        }

        /// <summary>
        /// Creates a representative architecture edge for mapping tests.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the edge.</param>
        /// <param name="edgeStableKey">The stable key of the architecture edge.</param>
        /// <param name="sourceNodeStableKey">The stable key of the source architecture node.</param>
        /// <param name="targetNodeStableKey">The stable key of the target architecture node.</param>
        /// <param name="evidenceStableKey">The stable key of the edge's primary evidence.</param>
        /// <returns>An architecture edge with query-critical fields populated.</returns>
        private static ArchitectureEdge CreateEdge(StableKey snapshotStableKey, StableKey edgeStableKey, StableKey sourceNodeStableKey, StableKey targetNodeStableKey, StableKey evidenceStableKey)
        {
            // The edge shape mirrors a common project-to-package reference while staying independent of Neo4j-specific details.
            return new ArchitectureEdge(
                snapshotStableKey,
                edgeStableKey,
                EdgeKind.References,
                sourceNodeStableKey,
                targetNodeStableKey,
                true,
                KnowledgeKind.Fact,
                Confidence.Certain,
                UnknownState.Known,
                evidenceStableKey,
                GraphMetadata.Empty,
                new Fingerprint("sha256:edge"));
        }

        /// <summary>
        /// Creates a semantic relationship using the generic architecture-edge contract.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the edge.</param>
        /// <param name="edgeStableKey">The stable key of the semantic edge.</param>
        /// <param name="sourceNodeStableKey">The stable key of the source semantic node.</param>
        /// <param name="targetNodeStableKey">The stable key of the target semantic node.</param>
        /// <param name="evidenceStableKey">The stable key of the edge's primary semantic evidence.</param>
        /// <returns>An architecture edge shaped like a projected Roslyn relationship.</returns>
        private static ArchitectureEdge CreateSemanticRelationship(StableKey snapshotStableKey, StableKey edgeStableKey, StableKey sourceNodeStableKey, StableKey targetNodeStableKey, StableKey evidenceStableKey)
        {
            // A partially resolved edge proves unknown-state fields are first-class even when the relationship still reaches persistence.
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["semantic.confidenceCategory"] = "PartiallyResolved",
                ["semantic.relationshipKind"] = "Calls"
            });
            return new ArchitectureEdge(
                snapshotStableKey,
                edgeStableKey,
                EdgeKind.Calls,
                sourceNodeStableKey,
                targetNodeStableKey,
                true,
                KnowledgeKind.Fact,
                Confidence.Medium,
                UnknownState.Unknown("PartiallyResolved"),
                evidenceStableKey,
                metadata,
                new Fingerprint("sha256:semantic-edge"));
        }

        /// <summary>
        /// Creates a representative versioned rule definition for mapping tests.
        /// </summary>
        /// <param name="ruleCode">The stable rule code for the definition.</param>
        /// <param name="version">The version string for the rule definition.</param>
        /// <returns>A rule definition with deterministic metadata and source URLs.</returns>
        private static RuleDefinition CreateRuleDefinition(string ruleCode, string version)
        {
            // The source URL and metadata values are deterministic so JSON assertions remain stable across test runs.
            return new RuleDefinition(
                ruleCode,
                "Layering rule",
                RuleCategory.ArchitectureLayering,
                FindingSeverity.High,
                FindingStatus.Open,
                true,
                version,
                "Detects invalid architecture layering.",
                "{\"type\":\"layering\"}",
                new[] { $"https://example.invalid/rules/{ruleCode}" },
                true,
                "platform",
                GraphMetadata.From(new Dictionary<string, object?> { ["ruleOwner"] = "architecture" }));
        }

        /// <summary>
        /// Creates a representative finding record for mapping tests.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the finding.</param>
        /// <param name="findingStableKey">The stable key that identifies the finding within the snapshot.</param>
        /// <param name="nodeStableKey">The primary node stable key associated with the finding.</param>
        /// <param name="evidenceStableKey">The primary evidence stable key associated with the finding.</param>
        /// <returns>A finding record with suppression and fingerprint fields populated.</returns>
        private static FindingRecord CreateFinding(StableKey snapshotStableKey, StableKey findingStableKey, StableKey nodeStableKey, StableKey evidenceStableKey)
        {
            // The finding includes optional suppression fields because Work Item 6 requires those details to persist as first-class data.
            return new FindingRecord(
                snapshotStableKey,
                findingStableKey,
                "ARCHON001",
                "1.0.0",
                FindingSeverity.High,
                FindingStatus.Suppressed,
                "Invalid dependency",
                "The project depends on a forbidden layer.",
                KnowledgeKind.Fact,
                Confidence.Certain,
                UnknownState.Known,
                nodeStableKey,
                evidenceStableKey,
                snapshotStableKey,
                snapshotStableKey,
                "Accepted risk",
                "architecture-review",
                GraphMetadata.Empty,
                new Fingerprint("sha256:finding"));
        }

        /// <summary>
        /// Creates a representative metric record for mapping tests.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the metric.</param>
        /// <param name="metricStableKey">The stable key that identifies the metric within the snapshot.</param>
        /// <param name="nodeStableKey">The optional node stable key targeted by the metric.</param>
        /// <param name="edgeStableKey">The optional edge stable key targeted by the metric.</param>
        /// <param name="evidenceStableKey">The optional primary evidence stable key associated with the metric.</param>
        /// <returns>A metric record with numeric and textual values populated.</returns>
        private static MetricRecord CreateMetric(StableKey snapshotStableKey, StableKey metricStableKey, StableKey nodeStableKey, StableKey edgeStableKey, StableKey evidenceStableKey)
        {
            // The metric uses both target shapes so the mapper test verifies node, edge, and evidence references together.
            return new MetricRecord(
                snapshotStableKey,
                metricStableKey,
                "DependencyCount",
                MetricScopeKind.Edge,
                nodeStableKey,
                edgeStableKey,
                evidenceStableKey,
                "Outgoing dependency count",
                12.5m,
                "twelve point five",
                "relationships",
                GraphMetadata.Empty,
                new Fingerprint("sha256:metric"));
        }

        /// <summary>
        /// Creates a representative generated summary for mapping tests.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the summary.</param>
        /// <param name="summaryStableKey">The stable key that identifies the summary within the snapshot.</param>
        /// <param name="targetStableKey">The stable key targeted by the generated summary.</param>
        /// <returns>A generated summary with deterministic content fields.</returns>
        private static GeneratedSummary CreateGeneratedSummary(StableKey snapshotStableKey, StableKey summaryStableKey, StableKey targetStableKey)
        {
            // The summary targets an architecture node because node summaries are common inputs to later markdown and report exports.
            return new GeneratedSummary(
                snapshotStableKey,
                summaryStableKey,
                SummaryKind.Node,
                targetStableKey,
                "Markdown",
                "Application project summary",
                "The application project depends on Neo4j.",
                GraphMetadata.Empty,
                new Fingerprint("sha256:summary"));
        }
    }
}
