using Archon.Application.Graph.Persistence;
using Archon.Infrastructure.Neo4j.DependencyInjection;
using Archon.Infrastructure.Neo4j.Schema;
using Archon.Infrastructure.Neo4j.Tests.Testcontainers;
using Microsoft.Extensions.DependencyInjection;
using Neo4j.Driver;
using Xunit;

namespace Archon.Infrastructure.Neo4j.Tests.Recreation
{
    /// <summary>
    /// Verifies guarded Neo4j graph recreation against a real Testcontainers database.
    /// </summary>
    public sealed class GraphRecreationNeo4jGraphRecreatorTests : Neo4jIntegrationTestBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="GraphRecreationNeo4jGraphRecreatorTests"/> class.
        /// </summary>
        /// <param name="fixture">The Neo4j Testcontainers fixture that supplies a real database for recreation validation.</param>
        public GraphRecreationNeo4jGraphRecreatorTests(Neo4jContainerFixture fixture)
            : base(fixture)
        {
            // Shared fixture construction keeps each test focused on guarded recreation behavior rather than container startup.
        }

        /// <summary>
        /// Confirms an unauthorized request returns a guard failure and leaves existing Archon data untouched.
        /// </summary>
        /// <returns>A task that completes after the real database has been seeded and verified.</returns>
        [Fact]
        public async Task RecreateGraphAsyncRejectsUnguardedRequestAndKeepsData()
        {
            // The unguarded path proves ordinary callers cannot accidentally clear a graph by resolving the recreator from DI.
            await using ServiceProvider serviceProvider = CreateServiceProvider();
            IArchitectureGraphRecreator recreator = serviceProvider.GetRequiredService<IArchitectureGraphRecreator>();
            IDriver driver = serviceProvider.GetRequiredService<IDriver>();
            string stableKey = $"repository://unguarded-{Guid.NewGuid():N}";

            await SeedRepositoryAsync(driver, stableKey);

            GraphRecreationResult result = await recreator.RecreateGraphAsync(new GraphRecreationRequest("not authorized"));
            long repositoryCount = await CountLabelAsync(driver, Neo4jSchemaNames.Labels.Repository, stableKey);

            Assert.False(result.Succeeded);
            Assert.False(result.Authorized);
            Assert.Equal(0, result.RecordsDeleted);
            Assert.Equal("GraphRecreationNotAuthorized", Assert.Single(result.Errors).Code);
            Assert.Equal(1, repositoryCount);
        }

        /// <summary>
        /// Confirms an explicitly authorized recreation clears representative Archon records and leaves schema initialized.
        /// </summary>
        /// <returns>A task that completes after data clearing and schema metadata verification against real Neo4j.</returns>
        [Fact]
        public async Task RecreateGraphAsyncClearsArchonDataAndRecreatesSchema()
        {
            // The authorized path seeds multiple Archon-owned labels so DETACH DELETE and the closed label catalog are exercised together.
            await using ServiceProvider serviceProvider = CreateServiceProvider();
            IArchitectureGraphRecreator recreator = serviceProvider.GetRequiredService<IArchitectureGraphRecreator>();
            Neo4jSchemaStatementCatalog catalog = serviceProvider.GetRequiredService<Neo4jSchemaStatementCatalog>();
            IDriver driver = serviceProvider.GetRequiredService<IDriver>();
            string suffix = Guid.NewGuid().ToString("N");

            await SeedRepresentativeArchonGraphAsync(driver, suffix);

            GraphRecreationResult result = await recreator.RecreateGraphAsync(GraphRecreationRequest.CreateAuthorized("integration test reset"));
            long archonNodeCount = await CountAllArchonNodesAsync(driver);
            SchemaMetadata metadata = await ReadSchemaMetadataAsync(driver);

            Assert.True(result.Succeeded);
            Assert.True(result.Authorized);
            Assert.True(result.RecordsDeleted >= 4);
            Assert.Equal(catalog.GetStatements().Count, result.SchemaStatementsExecuted);
            Assert.Empty(result.Errors);
            Assert.Equal(0, archonNodeCount);
            Assert.Contains(Neo4jSchemaNames.Constraints.RepositoryStableKeyUnique, metadata.ConstraintNames);
            Assert.Contains(Neo4jSchemaNames.Constraints.NodeSnapshotStableKeyUnique, metadata.ConstraintNames);
            Assert.Contains(Neo4jSchemaNames.Indexes.NodeKind, metadata.IndexNames);
            Assert.Contains(Neo4jSchemaNames.Indexes.EvidenceFingerprint, metadata.IndexNames);
        }

        /// <summary>
        /// Creates a service provider using production Neo4j infrastructure registrations and container-derived configuration.
        /// </summary>
        /// <returns>A service provider ready to resolve Neo4j infrastructure services for an integration test.</returns>
        private ServiceProvider CreateServiceProvider()
        {
            // The provider mirrors host registration while avoiding the Aspire AppHost, which must not be started during validation.
            ServiceCollection services = new();
            services.AddLogging();
            services.AddArchonNeo4j(CreateNeo4jConfiguration());
            return services.BuildServiceProvider(validateScopes: true);
        }

        /// <summary>
        /// Seeds one repository node with a unique stable key for unguarded recreation validation.
        /// </summary>
        /// <param name="driver">The configured Neo4j driver used to execute seed Cypher.</param>
        /// <param name="stableKey">The unique repository stable key that identifies this test's seed data.</param>
        /// <returns>A task that completes after the seed node has been committed.</returns>
        private static async Task SeedRepositoryAsync(IDriver driver, string stableKey)
        {
            // MERGE keeps repeated local debugging runs safe if a previous attempt left a node with the same generated key.
            await using IAsyncSession session = driver.AsyncSession(sessionBuilder => sessionBuilder.WithDatabase("neo4j"));
            await session.ExecuteWriteAsync(
                async transaction =>
                {
                    IResultCursor cursor = await transaction.RunAsync(
                        $"MERGE (r:{Neo4jSchemaNames.Labels.Repository} {{ {Neo4jSchemaNames.Properties.StableKey}: $stableKey }})",
                        new { stableKey }).ConfigureAwait(false);
                    await cursor.ConsumeAsync().ConfigureAwait(false);
                }).ConfigureAwait(false);
        }

        /// <summary>
        /// Seeds a small connected Archon graph spanning repository, snapshot, architecture node, and evidence labels.
        /// </summary>
        /// <param name="driver">The configured Neo4j driver used to execute seed Cypher.</param>
        /// <param name="suffix">The unique suffix used to isolate seed stable keys for this test invocation.</param>
        /// <returns>A task that completes after representative data has been committed.</returns>
        private static async Task SeedRepresentativeArchonGraphAsync(IDriver driver, string suffix)
        {
            // The seed graph uses representative labels and a supporting evidence relationship so DETACH DELETE must remove both nodes and edges.
            await using IAsyncSession session = driver.AsyncSession(sessionBuilder => sessionBuilder.WithDatabase("neo4j"));
            await session.ExecuteWriteAsync(
                async transaction =>
                {
                    IResultCursor cursor = await transaction.RunAsync(
                        $@"CREATE (r:{Neo4jSchemaNames.Labels.Repository} {{ {Neo4jSchemaNames.Properties.StableKey}: $repositoryStableKey }})
CREATE (s:{Neo4jSchemaNames.Labels.Snapshot} {{ {Neo4jSchemaNames.Properties.StableKey}: $snapshotStableKey, {Neo4jSchemaNames.Properties.RepositoryStableKey}: $repositoryStableKey }})
CREATE (n:{Neo4jSchemaNames.Labels.Node} {{ {Neo4jSchemaNames.Properties.SnapshotStableKey}: $snapshotStableKey, {Neo4jSchemaNames.Properties.StableKey}: $nodeStableKey, {Neo4jSchemaNames.Properties.NodeKind}: $nodeKind }})
CREATE (e:{Neo4jSchemaNames.Labels.Evidence} {{ {Neo4jSchemaNames.Properties.SnapshotStableKey}: $snapshotStableKey, {Neo4jSchemaNames.Properties.StableKey}: $evidenceStableKey, {Neo4jSchemaNames.Properties.Fingerprint}: $fingerprint }})
CREATE (n)-[:{Neo4jSchemaNames.Relationships.SupportedByEvidence}]->(e)",
                        new
                        {
                            repositoryStableKey = $"repository://recreation-{suffix}",
                            snapshotStableKey = $"snapshot://recreation-{suffix}",
                            nodeStableKey = $"project://recreation-{suffix}",
                            evidenceStableKey = $"evidence://recreation-{suffix}",
                            nodeKind = "Project",
                            fingerprint = $"sha256:{suffix}"
                        }).ConfigureAwait(false);
                    await cursor.ConsumeAsync().ConfigureAwait(false);
                }).ConfigureAwait(false);
        }

        /// <summary>
        /// Counts repository nodes with the specified stable key after an unauthorized recreation attempt.
        /// </summary>
        /// <param name="driver">The configured Neo4j driver used to execute the count query.</param>
        /// <param name="label">The whitelisted label to count for the test assertion.</param>
        /// <param name="stableKey">The stable key of the seed node that should remain present.</param>
        /// <returns>The number of matching nodes present in Neo4j.</returns>
        private static async Task<long> CountLabelAsync(IDriver driver, string label, string stableKey)
        {
            // The label value comes from the schema-name catalog, not from user input, so interpolation stays within the test's closed vocabulary.
            await using IAsyncSession session = driver.AsyncSession(sessionBuilder => sessionBuilder.WithDatabase("neo4j"));
            IResultCursor cursor = await session.RunAsync(
                $"MATCH (n:{label} {{ {Neo4jSchemaNames.Properties.StableKey}: $stableKey }}) RETURN count(n) AS nodeCount",
                new { stableKey });
            IRecord record = await cursor.SingleAsync();
            return record["nodeCount"].As<long>();
        }

        /// <summary>
        /// Counts every node carrying any Archon-owned label after recreation.
        /// </summary>
        /// <param name="driver">The configured Neo4j driver used to execute the count query.</param>
        /// <returns>The number of remaining Archon-owned nodes.</returns>
        private static async Task<long> CountAllArchonNodesAsync(IDriver driver)
        {
            // The query mirrors the production recreator's label-based ownership model to prove all Archon-owned records were removed.
            string[] labels =
            {
                Neo4jSchemaNames.Labels.Repository,
                Neo4jSchemaNames.Labels.Solution,
                Neo4jSchemaNames.Labels.Snapshot,
                Neo4jSchemaNames.Labels.Node,
                Neo4jSchemaNames.Labels.Relationship,
                Neo4jSchemaNames.Labels.Evidence,
                Neo4jSchemaNames.Labels.Rule,
                Neo4jSchemaNames.Labels.Finding,
                Neo4jSchemaNames.Labels.Metric,
                Neo4jSchemaNames.Labels.GeneratedSummary
            };

            await using IAsyncSession session = driver.AsyncSession(sessionBuilder => sessionBuilder.WithDatabase("neo4j"));
            IResultCursor cursor = await session.RunAsync(
                "MATCH (n) WHERE any(label IN labels(n) WHERE label IN $labels) RETURN count(DISTINCT n) AS nodeCount",
                new { labels });
            IRecord record = await cursor.SingleAsync();
            return record["nodeCount"].As<long>();
        }

        /// <summary>
        /// Reads constraint and index names from Neo4j metadata after graph recreation.
        /// </summary>
        /// <param name="driver">The configured Neo4j driver used to execute metadata queries.</param>
        /// <returns>A metadata object containing schema object names observed in the database.</returns>
        private static async Task<SchemaMetadata> ReadSchemaMetadataAsync(IDriver driver)
        {
            // SHOW commands verify schema state directly and prove recreation leaves the database ready for later persistence slices.
            await using IAsyncSession session = driver.AsyncSession(sessionBuilder => sessionBuilder.WithDatabase("neo4j"));
            IResultCursor constraintsCursor = await session.RunAsync("SHOW CONSTRAINTS YIELD name RETURN name");
            List<IRecord> constraintRecords = await constraintsCursor.ToListAsync();
            IResultCursor indexesCursor = await session.RunAsync("SHOW INDEXES YIELD name RETURN name");
            List<IRecord> indexRecords = await indexesCursor.ToListAsync();

            HashSet<string> constraintNames = constraintRecords
                .Select(record => record["name"].As<string>())
                .ToHashSet(StringComparer.Ordinal);
            HashSet<string> indexNames = indexRecords
                .Select(record => record["name"].As<string>())
                .ToHashSet(StringComparer.Ordinal);

            return new SchemaMetadata(constraintNames, indexNames);
        }

        /// <summary>
        /// Captures schema metadata read from Neo4j for integration assertions.
        /// </summary>
        /// <param name="constraintNames">The constraint names present in Neo4j.</param>
        /// <param name="indexNames">The index names present in Neo4j.</param>
        private sealed record SchemaMetadata(IReadOnlySet<string> ConstraintNames, IReadOnlySet<string> IndexNames);
    }
}
