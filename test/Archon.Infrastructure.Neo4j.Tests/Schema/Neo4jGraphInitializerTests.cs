using Archon.Application.Graph.Persistence;
using Archon.Infrastructure.Neo4j.DependencyInjection;
using Archon.Infrastructure.Neo4j.Schema;
using Archon.Infrastructure.Neo4j.Tests.Testcontainers;
using Microsoft.Extensions.DependencyInjection;
using Neo4j.Driver;
using Xunit;

namespace Archon.Infrastructure.Neo4j.Tests.Schema
{
    /// <summary>
    /// Verifies graph schema initialization against a real Neo4j Testcontainers database.
    /// </summary>
    public sealed class GraphInitializationNeo4jGraphInitializerTests : Neo4jIntegrationTestBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="GraphInitializationNeo4jGraphInitializerTests"/> class.
        /// </summary>
        /// <param name="fixture">The Neo4j Testcontainers fixture that supplies a real database for schema initialization.</param>
        public GraphInitializationNeo4jGraphInitializerTests(Neo4jContainerFixture fixture)
            : base(fixture)
        {
            // Shared fixture construction keeps the test focused on schema behavior rather than container lifecycle setup.
        }

        /// <summary>
        /// Confirms graph initialization creates the required constraints and indexes and remains idempotent across repeated runs.
        /// </summary>
        /// <returns>A task that completes after real Neo4j schema metadata has been verified.</returns>
        [Fact]
        public async Task InitializeAsyncCreatesRequiredSchemaAndIsIdempotent()
        {
            // The service provider uses the same infrastructure registration a host would use, while still avoiding the Aspire AppHost.
            ServiceCollection services = new();
            services.AddLogging();
            services.AddArchonNeo4j(CreateNeo4jConfiguration());

            await using ServiceProvider serviceProvider = services.BuildServiceProvider(validateScopes: true);
            IArchitectureGraphInitializer initializer = serviceProvider.GetRequiredService<IArchitectureGraphInitializer>();
            Neo4jSchemaStatementCatalog catalog = serviceProvider.GetRequiredService<Neo4jSchemaStatementCatalog>();
            IDriver driver = serviceProvider.GetRequiredService<IDriver>();

            GraphInitializationResult firstResult = await initializer.InitializeAsync();
            GraphInitializationResult secondResult = await initializer.InitializeAsync();
            SchemaMetadata metadata = await ReadSchemaMetadataAsync(driver);

            Assert.True(firstResult.Succeeded);
            Assert.True(secondResult.Succeeded);
            Assert.Equal(catalog.GetStatements().Count, firstResult.StatementsExecuted);
            Assert.Equal(catalog.GetStatements().Count, secondResult.StatementsExecuted);
            AssertRequiredConstraints(metadata.ConstraintNames);
            AssertRequiredIndexes(metadata.IndexNames);
        }

        /// <summary>
        /// Reads constraint and index names from Neo4j metadata after initialization.
        /// </summary>
        /// <param name="driver">The configured Neo4j driver resolved from dependency injection.</param>
        /// <returns>A metadata object containing schema object names observed in the database.</returns>
        private static async Task<SchemaMetadata> ReadSchemaMetadataAsync(IDriver driver)
        {
            // Metadata queries use SHOW commands because they inspect schema state directly without relying on application data.
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
        /// Asserts that Neo4j metadata contains every required Work Item 2 constraint name.
        /// </summary>
        /// <param name="constraintNames">The constraint names read from Neo4j metadata.</param>
        private static void AssertRequiredConstraints(IReadOnlySet<string> constraintNames)
        {
            // Constraints prove stable-key uniqueness for global and snapshot-scoped graph records.
            Assert.Contains(Neo4jSchemaNames.Constraints.RepositoryStableKeyUnique, constraintNames);
            Assert.Contains(Neo4jSchemaNames.Constraints.SolutionStableKeyUnique, constraintNames);
            Assert.Contains(Neo4jSchemaNames.Constraints.SnapshotStableKeyUnique, constraintNames);
            Assert.Contains(Neo4jSchemaNames.Constraints.NodeSnapshotStableKeyUnique, constraintNames);
            Assert.Contains(Neo4jSchemaNames.Constraints.RelationshipSnapshotStableKeyUnique, constraintNames);
            Assert.Contains(Neo4jSchemaNames.Constraints.EvidenceSnapshotStableKeyUnique, constraintNames);
            Assert.Contains(Neo4jSchemaNames.Constraints.RuleCodeVersionUnique, constraintNames);
            Assert.Contains(Neo4jSchemaNames.Constraints.FindingSnapshotStableKeyUnique, constraintNames);
            Assert.Contains(Neo4jSchemaNames.Constraints.MetricSnapshotStableKeyUnique, constraintNames);
            Assert.Contains(Neo4jSchemaNames.Constraints.GeneratedSummarySnapshotStableKeyUnique, constraintNames);
        }

        /// <summary>
        /// Asserts that Neo4j metadata contains representative required Work Item 2 index names.
        /// </summary>
        /// <param name="indexNames">The index names read from Neo4j metadata.</param>
        private static void AssertRequiredIndexes(IReadOnlySet<string> indexNames)
        {
            // The assertions cover all graph record categories and the required lookup dimensions from the specification.
            Assert.Contains(Neo4jSchemaNames.Indexes.SnapshotFingerprint, indexNames);
            Assert.Contains(Neo4jSchemaNames.Indexes.NodeSnapshot, indexNames);
            Assert.Contains(Neo4jSchemaNames.Indexes.NodeKind, indexNames);
            Assert.Contains(Neo4jSchemaNames.Indexes.NodeKnowledgeKind, indexNames);
            Assert.Contains(Neo4jSchemaNames.Indexes.NodeConfidence, indexNames);
            Assert.Contains(Neo4jSchemaNames.Indexes.NodeFingerprint, indexNames);
            Assert.Contains(Neo4jSchemaNames.Indexes.RelationshipSnapshot, indexNames);
            Assert.Contains(Neo4jSchemaNames.Indexes.RelationshipEdgeKind, indexNames);
            Assert.Contains(Neo4jSchemaNames.Indexes.RelationshipFingerprint, indexNames);
            Assert.Contains(Neo4jSchemaNames.Indexes.EvidenceSnapshot, indexNames);
            Assert.Contains(Neo4jSchemaNames.Indexes.EvidenceKind, indexNames);
            Assert.Contains(Neo4jSchemaNames.Indexes.EvidenceFingerprint, indexNames);
            Assert.Contains(Neo4jSchemaNames.Indexes.RuleCode, indexNames);
            Assert.Contains(Neo4jSchemaNames.Indexes.FindingSeverity, indexNames);
            Assert.Contains(Neo4jSchemaNames.Indexes.FindingStatus, indexNames);
            Assert.Contains(Neo4jSchemaNames.Indexes.MetricKind, indexNames);
            Assert.Contains(Neo4jSchemaNames.Indexes.GeneratedSummaryKind, indexNames);
        }

        /// <summary>
        /// Captures schema metadata read from Neo4j for integration assertions.
        /// </summary>
        /// <param name="constraintNames">The constraint names present in Neo4j.</param>
        /// <param name="indexNames">The index names present in Neo4j.</param>
        private sealed record SchemaMetadata(IReadOnlySet<string> ConstraintNames, IReadOnlySet<string> IndexNames);
    }
}
