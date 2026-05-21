using Archon.Application.Extraction.Contracts;
using Archon.Application.Graph.Persistence;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;
using Archon.Infrastructure.Neo4j.DependencyInjection;
using Archon.Infrastructure.Neo4j.Schema;
using Archon.Infrastructure.Neo4j.Tests.Testcontainers;
using Microsoft.Extensions.DependencyInjection;
using Neo4j.Driver;
using Xunit;

namespace Archon.Infrastructure.Neo4j.Tests.Persistence
{
    /// <summary>
    /// Verifies minimal snapshot persistence against a real Neo4j Testcontainers database.
    /// </summary>
    public sealed class MinimalSnapshotNeo4jArchitectureSnapshotWriterTests : Neo4jIntegrationTestBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MinimalSnapshotNeo4jArchitectureSnapshotWriterTests"/> class.
        /// </summary>
        /// <param name="fixture">The Neo4j Testcontainers fixture that supplies a real database for persistence validation.</param>
        public MinimalSnapshotNeo4jArchitectureSnapshotWriterTests(Neo4jContainerFixture fixture)
            : base(fixture)
        {
            // Shared fixture construction keeps each test focused on persistence behavior rather than container lifecycle setup.
        }

        /// <summary>
        /// Confirms the writer persists a representative minimal snapshot and creates required supporting relationships.
        /// </summary>
        /// <returns>A task that completes after the snapshot has been written and queried back from Neo4j.</returns>
        [Fact]
        public async Task WriteSnapshotAsyncPersistsRepresentativeMinimalSnapshot()
        {
            // A fresh graph per test avoids stable-key collisions because the fixture may reuse the same container for the class.
            await using ServiceProvider serviceProvider = CreateServiceProvider();
            IArchitectureGraphRecreator recreator = serviceProvider.GetRequiredService<IArchitectureGraphRecreator>();
            IArchitectureSnapshotWriter writer = serviceProvider.GetRequiredService<IArchitectureSnapshotWriter>();
            IDriver driver = serviceProvider.GetRequiredService<IDriver>();
            await recreator.RecreateGraphAsync(GraphRecreationRequest.CreateAuthorized("minimal snapshot persistence test"));
            ExtractedArchitectureSnapshot snapshot = CreateMinimalSnapshot("minimal-one", duplicateEvidence: false);

            SnapshotPersistenceResult result = await writer.WriteSnapshotAsync(snapshot);
            GraphCounts counts = await ReadGraphCountsAsync(driver);
            string? nodeFingerprint = await ReadNodeFingerprintAsync(driver, "snapshot://minimal-one", "project://minimal-one");

            Assert.True(result.Succeeded);
            Assert.Equal(1, result.Counts.Repositories);
            Assert.Equal(1, result.Counts.Solutions);
            Assert.Equal(1, result.Counts.Snapshots);
            Assert.Equal(1, result.Counts.Nodes);
            Assert.Equal(1, result.Counts.Evidence);
            Assert.Equal(1, result.Counts.SnapshotSolutionRelationships);
            Assert.Equal(1, result.Counts.NodeEvidenceRelationships);
            Assert.Equal(1, counts.Repositories);
            Assert.Equal(1, counts.Solutions);
            Assert.Equal(1, counts.Snapshots);
            Assert.Equal(1, counts.Nodes);
            Assert.Equal(1, counts.Evidence);
            Assert.Equal(1, counts.SnapshotSolutionRelationships);
            Assert.Equal(1, counts.NodeEvidenceRelationships);
            Assert.Equal("sha256:node-minimal-one", nodeFingerprint);
        }

        /// <summary>
        /// Confirms duplicate evidence payloads in one snapshot collapse to one canonical evidence node while preserving node support links.
        /// </summary>
        /// <returns>A task that completes after evidence deduplication has been verified in Neo4j.</returns>
        [Fact]
        public async Task WriteSnapshotAsyncDeduplicatesEvidenceWithinOneSnapshot()
        {
            // Two nodes reference distinct evidence stable keys with identical payloads; only the canonical evidence node should persist.
            await using ServiceProvider serviceProvider = CreateServiceProvider();
            IArchitectureGraphRecreator recreator = serviceProvider.GetRequiredService<IArchitectureGraphRecreator>();
            IArchitectureSnapshotWriter writer = serviceProvider.GetRequiredService<IArchitectureSnapshotWriter>();
            IDriver driver = serviceProvider.GetRequiredService<IDriver>();
            await recreator.RecreateGraphAsync(GraphRecreationRequest.CreateAuthorized("evidence deduplication test"));
            ExtractedArchitectureSnapshot snapshot = CreateMinimalSnapshot("dedupe-one", duplicateEvidence: true);

            SnapshotPersistenceResult result = await writer.WriteSnapshotAsync(snapshot);
            GraphCounts counts = await ReadGraphCountsAsync(driver);

            Assert.True(result.Succeeded);
            Assert.Equal(2, result.Counts.Nodes);
            Assert.Equal(1, result.Counts.Evidence);
            Assert.Equal(2, result.Counts.NodeEvidenceRelationships);
            Assert.Equal(1, counts.Evidence);
            Assert.Equal(2, counts.NodeEvidenceRelationships);
        }

        /// <summary>
        /// Confirms identical evidence payloads in different snapshots are not merged across snapshot scope.
        /// </summary>
        /// <returns>A task that completes after two snapshots have been persisted and evidence scope has been verified.</returns>
        [Fact]
        public async Task WriteSnapshotAsyncKeepsIdenticalEvidenceSeparateAcrossSnapshots()
        {
            // Evidence deduplication includes snapshot scope, so two snapshots can preserve equivalent source evidence independently.
            await using ServiceProvider serviceProvider = CreateServiceProvider();
            IArchitectureGraphRecreator recreator = serviceProvider.GetRequiredService<IArchitectureGraphRecreator>();
            IArchitectureSnapshotWriter writer = serviceProvider.GetRequiredService<IArchitectureSnapshotWriter>();
            IDriver driver = serviceProvider.GetRequiredService<IDriver>();
            await recreator.RecreateGraphAsync(GraphRecreationRequest.CreateAuthorized("cross snapshot evidence test"));

            SnapshotPersistenceResult first = await writer.WriteSnapshotAsync(CreateMinimalSnapshot("cross-one", duplicateEvidence: false));
            SnapshotPersistenceResult second = await writer.WriteSnapshotAsync(CreateMinimalSnapshot("cross-two", duplicateEvidence: false));
            GraphCounts counts = await ReadGraphCountsAsync(driver);

            Assert.True(first.Succeeded);
            Assert.True(second.Succeeded);
            Assert.Equal(2, counts.Snapshots);
            Assert.Equal(2, counts.Evidence);
        }

        /// <summary>
        /// Confirms missing primary evidence references are returned as explicit errors instead of silently dropping links.
        /// </summary>
        /// <returns>A task that completes after validation failure has been asserted.</returns>
        [Fact]
        public async Task WriteSnapshotAsyncReturnsErrorForMissingEvidenceReference()
        {
            // The invalid snapshot includes a node primary evidence key that is absent from the evidence section.
            await using ServiceProvider serviceProvider = CreateServiceProvider();
            IArchitectureGraphRecreator recreator = serviceProvider.GetRequiredService<IArchitectureGraphRecreator>();
            IArchitectureSnapshotWriter writer = serviceProvider.GetRequiredService<IArchitectureSnapshotWriter>();
            IDriver driver = serviceProvider.GetRequiredService<IDriver>();
            await recreator.RecreateGraphAsync(GraphRecreationRequest.CreateAuthorized("missing evidence test"));
            ExtractedArchitectureSnapshot snapshot = CreateSnapshotWithMissingEvidence("missing-evidence");

            SnapshotPersistenceResult result = await writer.WriteSnapshotAsync(snapshot);
            GraphCounts counts = await ReadGraphCountsAsync(driver);

            Assert.False(result.Succeeded);
            Assert.Equal("MissingNodeEvidenceReference", Assert.Single(result.Errors).Code);
            Assert.Equal(0, counts.Snapshots);
            Assert.Equal(0, counts.Nodes);
            Assert.Equal(0, counts.Evidence);
        }

        /// <summary>
        /// Creates a service provider using production Neo4j infrastructure registrations and container-derived configuration.
        /// </summary>
        /// <returns>A service provider ready to resolve Neo4j infrastructure services for integration tests.</returns>
        private ServiceProvider CreateServiceProvider()
        {
            // The provider mirrors host composition while avoiding the Aspire AppHost, which must not run during automated validation.
            ServiceCollection services = new();
            services.AddLogging();
            services.AddArchonNeo4j(CreateNeo4jConfiguration());
            return services.BuildServiceProvider(validateScopes: true);
        }

        /// <summary>
        /// Creates a minimal extracted snapshot with optional duplicate evidence content.
        /// </summary>
        /// <param name="suffix">The unique suffix used to isolate stable keys for the test.</param>
        /// <param name="duplicateEvidence">A value indicating whether the snapshot should include two equivalent evidence records and two nodes.</param>
        /// <returns>An extracted architecture snapshot suitable for Work Item 4 persistence.</returns>
        private static ExtractedArchitectureSnapshot CreateMinimalSnapshot(string suffix, bool duplicateEvidence)
        {
            // The snapshot contains only Work Item 4 sections; later edge, finding, metric, and summary sections remain empty.
            StableKey repositoryStableKey = new($"repository://{suffix}");
            StableKey solutionStableKey = new($"solution://{suffix}");
            StableKey snapshotStableKey = new($"snapshot://{suffix}");
            StableKey firstEvidenceStableKey = new($"evidence://{suffix}/first");
            StableKey secondEvidenceStableKey = new($"evidence://{suffix}/second");

            RepositoryModel repository = CreateRepository(repositoryStableKey, suffix);
            SolutionModel solution = CreateSolution(repositoryStableKey, solutionStableKey, suffix);
            SnapshotHeader header = CreateHeader(repositoryStableKey, snapshotStableKey, suffix);
            EvidenceRecord firstEvidence = CreateEvidence(snapshotStableKey, firstEvidenceStableKey, suffix);
            ArchitectureNode firstNode = CreateNode(snapshotStableKey, new StableKey($"project://{suffix}"), firstEvidenceStableKey, suffix, "Project");

            List<ArchitectureNode> nodes = [firstNode];
            List<EvidenceRecord> evidence = [firstEvidence];
            if (duplicateEvidence)
            {
                evidence.Add(CreateEvidence(snapshotStableKey, secondEvidenceStableKey, suffix));
                nodes.Add(CreateNode(snapshotStableKey, new StableKey($"project://{suffix}/second"), secondEvidenceStableKey, suffix, "Second Project"));
            }

            return new ExtractedArchitectureSnapshot(header, new[] { repository }, new[] { solution }, nodes, Array.Empty<ArchitectureEdge>(), evidence, Array.Empty<RuleDefinition>(), Array.Empty<FindingRecord>(), Array.Empty<MetricRecord>(), Array.Empty<GeneratedSummary>(), Array.Empty<string>(), Array.Empty<string>());
        }

        /// <summary>
        /// Creates a snapshot whose node references evidence that is not supplied.
        /// </summary>
        /// <param name="suffix">The unique suffix used to isolate stable keys for the test.</param>
        /// <returns>An invalid extracted snapshot for validation testing.</returns>
        private static ExtractedArchitectureSnapshot CreateSnapshotWithMissingEvidence(string suffix)
        {
            // The missing reference test proves the writer returns explicit errors rather than silently dropping node evidence links.
            StableKey repositoryStableKey = new($"repository://{suffix}");
            StableKey solutionStableKey = new($"solution://{suffix}");
            StableKey snapshotStableKey = new($"snapshot://{suffix}");
            RepositoryModel repository = CreateRepository(repositoryStableKey, suffix);
            SolutionModel solution = CreateSolution(repositoryStableKey, solutionStableKey, suffix);
            SnapshotHeader header = CreateHeader(repositoryStableKey, snapshotStableKey, suffix);
            ArchitectureNode node = CreateNode(snapshotStableKey, new StableKey($"project://{suffix}"), new StableKey($"evidence://{suffix}/missing"), suffix, "Project");

            return new ExtractedArchitectureSnapshot(header, new[] { repository }, new[] { solution }, new[] { node }, Array.Empty<ArchitectureEdge>(), Array.Empty<EvidenceRecord>(), Array.Empty<RuleDefinition>(), Array.Empty<FindingRecord>(), Array.Empty<MetricRecord>(), Array.Empty<GeneratedSummary>(), Array.Empty<string>(), Array.Empty<string>());
        }

        /// <summary>
        /// Creates a repository model for a persistence test snapshot.
        /// </summary>
        /// <param name="stableKey">The stable key that identifies the repository.</param>
        /// <param name="suffix">The unique suffix used in display fields.</param>
        /// <returns>A repository model.</returns>
        private static RepositoryModel CreateRepository(StableKey stableKey, string suffix)
        {
            // Repository root path is a persisted descriptive field and is not used as a stable identity by Neo4j.
            return new RepositoryModel(stableKey, $"Repository {suffix}", $"D:/Dev/{suffix}", null, "main", GraphMetadata.Empty);
        }

        /// <summary>
        /// Creates a solution model for a persistence test snapshot.
        /// </summary>
        /// <param name="repositoryStableKey">The stable key of the repository that owns the solution.</param>
        /// <param name="stableKey">The stable key that identifies the solution.</param>
        /// <param name="suffix">The unique suffix used in display fields.</param>
        /// <returns>A solution model.</returns>
        private static SolutionModel CreateSolution(StableKey repositoryStableKey, StableKey stableKey, string suffix)
        {
            // The path is repository-relative so persisted solution properties remain machine-independent.
            return new SolutionModel(repositoryStableKey, stableKey, $"Solution {suffix}", RepositoryRelativePath.Parse($"src/{suffix}.sln"), GraphMetadata.Empty);
        }

        /// <summary>
        /// Creates a snapshot header for a persistence test snapshot.
        /// </summary>
        /// <param name="repositoryStableKey">The stable key of the repository extracted by the snapshot.</param>
        /// <param name="stableKey">The stable key that identifies the snapshot.</param>
        /// <param name="suffix">The unique suffix used in metadata.</param>
        /// <returns>A snapshot header.</returns>
        private static SnapshotHeader CreateHeader(StableKey repositoryStableKey, StableKey stableKey, string suffix)
        {
            // Fixed timestamps make integration data deterministic while suffix metadata helps diagnose local test runs.
            return new SnapshotHeader(
                stableKey,
                repositoryStableKey,
                "main",
                "abc123",
                new DateTimeOffset(2025, 2, 3, 4, 5, 6, TimeSpan.Zero),
                new DateTimeOffset(2025, 2, 3, 4, 6, 6, TimeSpan.Zero),
                "wp004-tests",
                "Completed",
                Array.Empty<string>(),
                Array.Empty<string>(),
                GraphMetadata.From(new Dictionary<string, object?> { ["testSuffix"] = suffix }));
        }

        /// <summary>
        /// Creates an architecture node for a persistence test snapshot.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that scopes the node.</param>
        /// <param name="stableKey">The stable key that identifies the node.</param>
        /// <param name="evidenceStableKey">The primary evidence stable key referenced by the node.</param>
        /// <param name="suffix">The unique suffix used in fingerprints.</param>
        /// <param name="displayName">The display name for the node.</param>
        /// <returns>An architecture node.</returns>
        private static ArchitectureNode CreateNode(StableKey snapshotStableKey, StableKey stableKey, StableKey evidenceStableKey, string suffix, string displayName)
        {
            // The primary evidence reference is used by the writer to create SUPPORTED_BY_EVIDENCE relationships.
            return new ArchitectureNode(
                snapshotStableKey,
                stableKey,
                NodeKind.Project,
                displayName,
                displayName,
                displayName.ToLowerInvariant(),
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
                new Fingerprint($"sha256:node-{suffix}"));
        }

        /// <summary>
        /// Creates an evidence record for a persistence test snapshot.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that scopes the evidence.</param>
        /// <param name="stableKey">The stable key that identifies the evidence.</param>
        /// <param name="suffix">The unique suffix used in persisted path content.</param>
        /// <returns>An evidence record.</returns>
        private static EvidenceRecord CreateEvidence(StableKey snapshotStableKey, StableKey stableKey, string suffix)
        {
            // The stable key can differ while the payload remains equivalent, which exercises per-snapshot evidence deduplication.
            return new EvidenceRecord(
                snapshotStableKey,
                stableKey,
                EvidenceKind.ProjectFile,
                RepositoryRelativePath.Parse($"src/{suffix}.csproj"),
                1,
                3,
                "Project",
                null,
                "snippet-hash",
                "<Project />",
                KnowledgeKind.Fact,
                Confidence.Certain,
                UnknownState.Known,
                GraphMetadata.Empty,
                new Fingerprint("sha256:evidence-shared"));
        }

        /// <summary>
        /// Reads persisted graph counts needed by integration assertions.
        /// </summary>
        /// <param name="driver">The configured Neo4j driver used to query the database.</param>
        /// <returns>Counts for minimal snapshot nodes and relationships.</returns>
        private static async Task<GraphCounts> ReadGraphCountsAsync(IDriver driver)
        {
            // Count queries validate persisted shape without relying on Neo4j internal IDs.
            await using IAsyncSession session = driver.AsyncSession(sessionBuilder => sessionBuilder.WithDatabase("neo4j"));
            IResultCursor cursor = await session.RunAsync(@"
CALL { MATCH (repository:ArchonRepository) RETURN count(repository) AS repositories }
CALL { MATCH (solution:ArchonSolution) RETURN count(solution) AS solutions }
CALL { MATCH (snapshot:ArchonSnapshot) RETURN count(snapshot) AS snapshots }
CALL { MATCH (node:ArchonNode) RETURN count(node) AS nodes }
CALL { MATCH (evidence:ArchonEvidence) RETURN count(evidence) AS evidence }
CALL { MATCH (:ArchonSnapshot)-[includes:INCLUDES_SOLUTION]->(:ArchonSolution) RETURN count(includes) AS snapshotSolutionRelationships }
CALL { MATCH (:ArchonNode)-[supported:SUPPORTED_BY_EVIDENCE]->(:ArchonEvidence) RETURN count(supported) AS nodeEvidenceRelationships }
RETURN repositories, solutions, snapshots, nodes, evidence, snapshotSolutionRelationships, nodeEvidenceRelationships");
            IRecord record = await cursor.SingleAsync();
            return new GraphCounts(
                record["repositories"].As<long>(),
                record["solutions"].As<long>(),
                record["snapshots"].As<long>(),
                record["nodes"].As<long>(),
                record["evidence"].As<long>(),
                record["snapshotSolutionRelationships"].As<long>(),
                record["nodeEvidenceRelationships"].As<long>());
        }

        /// <summary>
        /// Reads the fingerprint for a persisted architecture node by stable key.
        /// </summary>
        /// <param name="driver">The configured Neo4j driver used to query the database.</param>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the node.</param>
        /// <param name="nodeStableKey">The stable key of the node to look up.</param>
        /// <returns>The node fingerprint when found; otherwise, <see langword="null"/>.</returns>
        private static async Task<string?> ReadNodeFingerprintAsync(IDriver driver, string snapshotStableKey, string nodeStableKey)
        {
            // The lookup uses indexed stable-key and fingerprint properties required by the work item.
            await using IAsyncSession session = driver.AsyncSession(sessionBuilder => sessionBuilder.WithDatabase("neo4j"));
            IResultCursor cursor = await session.RunAsync(
                "MATCH (node:ArchonNode { snapshotStableKey: $snapshotStableKey, stableKey: $nodeStableKey }) RETURN node.fingerprint AS fingerprint",
                new { snapshotStableKey, nodeStableKey });
            IRecord? record = await cursor.SingleOrDefaultAsync();
            return record?["fingerprint"].As<string>();
        }

        /// <summary>
        /// Captures minimal graph counts read from Neo4j.
        /// </summary>
        /// <param name="Repositories">The repository node count.</param>
        /// <param name="Solutions">The solution node count.</param>
        /// <param name="Snapshots">The snapshot node count.</param>
        /// <param name="Nodes">The architecture node count.</param>
        /// <param name="Evidence">The evidence node count.</param>
        /// <param name="SnapshotSolutionRelationships">The snapshot-to-solution relationship count.</param>
        /// <param name="NodeEvidenceRelationships">The node-to-evidence relationship count.</param>
        private sealed record GraphCounts(long Repositories, long Solutions, long Snapshots, long Nodes, long Evidence, long SnapshotSolutionRelationships, long NodeEvidenceRelationships);
    }
}
