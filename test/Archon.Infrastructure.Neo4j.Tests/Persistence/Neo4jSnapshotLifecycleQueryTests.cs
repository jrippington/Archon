using Archon.Application.Extraction.Contracts;
using Archon.Application.Graph.Persistence;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;
using Archon.Infrastructure.Neo4j.DependencyInjection;
using Archon.Infrastructure.Neo4j.Tests.Testcontainers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Archon.Infrastructure.Neo4j.Tests.Persistence
{
    /// <summary>
    /// Verifies Neo4j-backed snapshot lifecycle listing reads persisted snapshot rows through the application query port.
    /// </summary>
    public sealed class Neo4jSnapshotLifecycleQueryTests : Neo4jIntegrationTestBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Neo4jSnapshotLifecycleQueryTests"/> class.
        /// </summary>
        /// <param name="fixture">The Neo4j Testcontainers fixture that supplies a real database for lifecycle validation.</param>
        public Neo4jSnapshotLifecycleQueryTests(Neo4jContainerFixture fixture)
            : base(fixture)
        {
            // Shared fixture construction keeps each test focused on lifecycle behavior rather than container lifecycle setup.
        }

        /// <summary>
        /// Confirms lifecycle listing reads filtered rows from persisted Neo4j snapshot data in deterministic newest-first order.
        /// </summary>
        /// <returns>A task that completes after persisted lifecycle rows have been queried and asserted.</returns>
        [Fact]
        public async Task ListSnapshotsAsync_WhenSnapshotsExist_ShouldApplyFiltersAndReturnNewestFirstRows()
        {
            // The test writes snapshots through the public writer, then reads through the lifecycle port to prove the query is graph-backed.
            await using ServiceProvider serviceProvider = CreateServiceProvider();
            IArchitectureGraphRecreator recreator = serviceProvider.GetRequiredService<IArchitectureGraphRecreator>();
            IArchitectureSnapshotWriter writer = serviceProvider.GetRequiredService<IArchitectureSnapshotWriter>();
            ISnapshotLifecycleQuery lifecycleQuery = serviceProvider.GetRequiredService<ISnapshotLifecycleQuery>();
            await recreator.RecreateGraphAsync(GraphRecreationRequest.CreateAuthorized("snapshot lifecycle query filter test"));
            await writer.WriteSnapshotAsync(CreateSnapshot("old", "repository://lifecycle", "solution://lifecycle/main", "old", DateTimeOffset.Parse("2026-05-18T08:00:00Z")));
            await writer.WriteSnapshotAsync(CreateSnapshot("new", "repository://lifecycle", "solution://lifecycle/main", "new", DateTimeOffset.Parse("2026-05-20T08:00:00Z")));
            await writer.WriteSnapshotAsync(CreateSnapshot("other", "repository://other-lifecycle", "solution://other-lifecycle/main", "other", DateTimeOffset.Parse("2026-05-21T08:00:00Z")));

            SnapshotLifecycleQueryResult result = await lifecycleQuery.ListSnapshotsAsync(
                new SnapshotLifecycleQueryRequest("repository://lifecycle", "solution://lifecycle/main", "Completed", DateTimeOffset.Parse("2026-05-18T00:00:00Z"), DateTimeOffset.Parse("2026-05-21T00:00:00Z"), null, 10),
                CancellationToken.None);

            Assert.Equal(2, result.TotalCount);
            Assert.Collection(
                result.Items,
                row =>
                {
                    Assert.Equal("snapshot://new", row.SnapshotStableKey);
                    Assert.Equal("new", row.CommitSha);
                },
                row =>
                {
                    Assert.Equal("snapshot://old", row.SnapshotStableKey);
                    Assert.Equal("old", row.CommitSha);
                });
            Assert.Empty(result.Warnings);
        }

        /// <summary>
        /// Confirms lifecycle listing reports truncation without exposing graph internals when the take limit removes matching rows.
        /// </summary>
        /// <returns>A task that completes after truncation metadata is asserted.</returns>
        [Fact]
        public async Task ListSnapshotsAsync_WhenTakeLimitTruncatesRows_ShouldReturnSafeWarning()
        {
            // The total count is calculated by the query so callers can see truncation without receiving unbounded lifecycle data.
            await using ServiceProvider serviceProvider = CreateServiceProvider();
            IArchitectureGraphRecreator recreator = serviceProvider.GetRequiredService<IArchitectureGraphRecreator>();
            IArchitectureSnapshotWriter writer = serviceProvider.GetRequiredService<IArchitectureSnapshotWriter>();
            ISnapshotLifecycleQuery lifecycleQuery = serviceProvider.GetRequiredService<ISnapshotLifecycleQuery>();
            await recreator.RecreateGraphAsync(GraphRecreationRequest.CreateAuthorized("snapshot lifecycle query truncation test"));
            await writer.WriteSnapshotAsync(CreateSnapshot("take-one", "repository://take", "solution://take/main", "one", DateTimeOffset.Parse("2026-05-20T08:00:00Z")));
            await writer.WriteSnapshotAsync(CreateSnapshot("take-two", "repository://take", "solution://take/main", "two", DateTimeOffset.Parse("2026-05-21T08:00:00Z")));

            SnapshotLifecycleQueryResult result = await lifecycleQuery.ListSnapshotsAsync(new SnapshotLifecycleQueryRequest("repository://take", null, null, null, null, null, 1), CancellationToken.None);

            SnapshotLifecycleQueryRow item = Assert.Single(result.Items);
            Assert.Equal("snapshot://take-two", item.SnapshotStableKey);
            Assert.Equal(2, result.TotalCount);
            string warning = Assert.Single(result.Warnings);
            Assert.Contains("truncated", warning, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("MATCH", warning, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Neo4j", warning, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Creates a service provider with Neo4j infrastructure registration for lifecycle tests.
        /// </summary>
        /// <returns>A service provider configured for the shared Neo4j container.</returns>
        private ServiceProvider CreateServiceProvider()
        {
            // Tests use production registration so lifecycle query behavior includes the same DI graph as a Neo4j-composed host.
            ServiceCollection services = new();
            services.AddLogging();
            services.AddArchonNeo4j(CreateNeo4jConfiguration());
            return services.BuildServiceProvider(validateScopes: true);
        }

        /// <summary>
        /// Creates a minimal extracted snapshot with repository, solution, and header lifecycle data.
        /// </summary>
        /// <param name="suffix">The unique suffix used to isolate stable keys for one snapshot.</param>
        /// <param name="repositoryStableKeyValue">The repository stable key value stored on the snapshot header.</param>
        /// <param name="solutionStableKeyValue">The solution stable key value related to the snapshot.</param>
        /// <param name="commitSha">The commit SHA stored on the snapshot header.</param>
        /// <param name="startedUtc">The UTC timestamp stored as the snapshot start time.</param>
        /// <returns>A minimal extracted snapshot suitable for graph-backed lifecycle tests.</returns>
        private static ExtractedArchitectureSnapshot CreateSnapshot(string suffix, string repositoryStableKeyValue, string solutionStableKeyValue, string commitSha, DateTimeOffset startedUtc)
        {
            // Lifecycle listing needs only the persisted header and snapshot-to-solution relationship, so other graph sections stay empty.
            StableKey repositoryStableKey = new(repositoryStableKeyValue);
            StableKey solutionStableKey = new(solutionStableKeyValue);
            StableKey snapshotStableKey = new($"snapshot://{suffix}");
            RepositoryModel repository = new(repositoryStableKey, $"Repository {suffix}", $"D:/src/{suffix}", null, "main", GraphMetadata.Empty);
            SolutionModel solution = new(repositoryStableKey, solutionStableKey, $"{suffix}.slnx", RepositoryRelativePath.Parse($"{suffix}.slnx"), GraphMetadata.Empty);
            SnapshotHeader header = new(snapshotStableKey, repositoryStableKey, "main", commitSha, startedUtc, startedUtc.AddMinutes(5), "test", "Completed", [], [], GraphMetadata.Empty);
            return new ExtractedArchitectureSnapshot(header, [repository], [solution], [], [], [], [], [], [], [], [], []);
        }
    }
}
