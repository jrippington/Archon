using Archon.Application.Extraction.Contracts;
using Archon.Application.Extraction.Runs;
using Archon.Application.Graph.Persistence;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;
using Archon.Domain.Graph.Model;
using Archon.Infrastructure.Neo4j.DependencyInjection;
using Archon.Infrastructure.Neo4j.Tests.Testcontainers;
using Microsoft.Extensions.DependencyInjection;
using Neo4j.Driver;
using Xunit;

namespace Archon.Infrastructure.Neo4j.Tests.Persistence
{
    /// <summary>
    /// Verifies Neo4j-backed delete-one snapshot behavior against a real Neo4j Testcontainers database.
    /// </summary>
    public sealed class Neo4jSnapshotDeletionStoreTests : Neo4jIntegrationTestBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Neo4jSnapshotDeletionStoreTests"/> class.
        /// </summary>
        /// <param name="fixture">The Neo4j Testcontainers fixture that supplies a real database for deletion validation.</param>
        public Neo4jSnapshotDeletionStoreTests(Neo4jContainerFixture fixture)
            : base(fixture)
        {
            // Shared fixture construction keeps each test focused on deletion behavior rather than container lifecycle setup.
        }

        /// <summary>
        /// Confirms delete-one removes the target snapshot and snapshot-scoped subgraph while preserving shared records and run history.
        /// </summary>
        /// <returns>A task that completes after graph cleanup and preservation assertions finish.</returns>
        [Fact]
        public async Task DeleteSnapshotAsync_WhenSnapshotExists_ShouldDeleteScopedSubgraphAndPreserveSharedRecordsAndRuns()
        {
            // The scenario writes two snapshots in the same repository, links a completed run to the deleted snapshot, and verifies only the
            // target snapshot scope is removed while repository, solution, rule, and run records remain queryable.
            await using ServiceProvider serviceProvider = CreateServiceProvider();
            IArchitectureGraphRecreator recreator = serviceProvider.GetRequiredService<IArchitectureGraphRecreator>();
            IArchitectureSnapshotWriter writer = serviceProvider.GetRequiredService<IArchitectureSnapshotWriter>();
            ISnapshotDeletionStore deletionStore = serviceProvider.GetRequiredService<ISnapshotDeletionStore>();
            IExtractionRunHistory runHistory = serviceProvider.GetRequiredService<IExtractionRunHistory>();
            IDriver driver = serviceProvider.GetRequiredService<IDriver>();
            await recreator.RecreateGraphAsync(GraphRecreationRequest.CreateAuthorized("snapshot delete-one preservation test"));
            await writer.WriteSnapshotAsync(CreateSnapshot("delete-target"), CancellationToken.None);
            await writer.WriteSnapshotAsync(CreateSnapshot("delete-other"), CancellationToken.None);
            ExtractionRun run = CreateCompletedRun("snapshot://delete-target");
            await runHistory.UpdateAsync(run, CancellationToken.None);

            SnapshotDeletionResult result = await deletionStore.DeleteSnapshotAsync(new SnapshotDeletionRequest("snapshot://delete-target"), CancellationToken.None);
            GraphDeletionSnapshot graph = await ReadGraphDeletionSnapshotAsync(driver, "snapshot://delete-target", "snapshot://delete-other", run.RunId.ToString());
            ExtractionRun? preservedRun = await runHistory.GetAsync(run.RunId, CancellationToken.None);

            Assert.True(result.SnapshotDeleted);
            Assert.Equal(1, result.DeletedSnapshotCount);
            Assert.True(result.DeletedNodeCount >= 5);
            Assert.True(result.DeletedRelationshipCount >= 1);
            Assert.Equal(1, result.AffectedRunCount);
            Assert.NotNull(preservedRun);
            Assert.Equal("snapshot://delete-target", preservedRun.SnapshotIdentity);
            Assert.Equal(2, graph.Repositories);
            Assert.Equal(2, graph.Solutions);
            Assert.Equal(0, graph.Rules);
            Assert.Equal(1, graph.Runs);
            Assert.Equal(0, graph.TargetSnapshots);
            Assert.Equal(0, graph.TargetScopedNodes);
            Assert.Equal(0, graph.TargetSnapshotProducedRelationships);
            Assert.Equal(1, graph.OtherSnapshots);
            Assert.True(graph.OtherScopedNodes > 0);
        }

        /// <summary>
        /// Confirms delete-all removes every snapshot-scoped subgraph while preserving shared records and extraction run history.
        /// </summary>
        /// <returns>A task that completes after aggregate graph cleanup and preservation assertions finish.</returns>
        [Fact]
        public async Task DeleteAllSnapshotsAsync_WhenSnapshotsExist_ShouldDeleteAllScopedSubgraphsAndPreserveSharedRecordsAndRuns()
        {
            // The scenario writes two snapshots and two completed runs, then verifies global snapshot cleanup removes only snapshot-owned graph data.
            await using ServiceProvider serviceProvider = CreateServiceProvider();
            IArchitectureGraphRecreator recreator = serviceProvider.GetRequiredService<IArchitectureGraphRecreator>();
            IArchitectureSnapshotWriter writer = serviceProvider.GetRequiredService<IArchitectureSnapshotWriter>();
            ISnapshotDeletionStore deletionStore = serviceProvider.GetRequiredService<ISnapshotDeletionStore>();
            IExtractionRunHistory runHistory = serviceProvider.GetRequiredService<IExtractionRunHistory>();
            IDriver driver = serviceProvider.GetRequiredService<IDriver>();
            await recreator.RecreateGraphAsync(GraphRecreationRequest.CreateAuthorized("snapshot delete-all preservation test"));
            await writer.WriteSnapshotAsync(CreateSnapshot("delete-all-one"), CancellationToken.None);
            await writer.WriteSnapshotAsync(CreateSnapshot("delete-all-two"), CancellationToken.None);
            ExtractionRun firstRun = CreateCompletedRun("snapshot://delete-all-one");
            ExtractionRun secondRun = CreateCompletedRun("snapshot://delete-all-two");
            await runHistory.UpdateAsync(firstRun, CancellationToken.None);
            await runHistory.UpdateAsync(secondRun, CancellationToken.None);

            SnapshotDeleteAllResult result = await deletionStore.DeleteAllSnapshotsAsync(new SnapshotDeleteAllRequest("delete-all-snapshots"), CancellationToken.None);
            GraphDeleteAllSnapshot graph = await ReadGraphDeleteAllSnapshotAsync(driver, firstRun.RunId.ToString(), secondRun.RunId.ToString());
            ExtractionRun? preservedFirstRun = await runHistory.GetAsync(firstRun.RunId, CancellationToken.None);
            ExtractionRun? preservedSecondRun = await runHistory.GetAsync(secondRun.RunId, CancellationToken.None);

            Assert.Equal(2, result.DeletedSnapshotCount);
            Assert.True(result.DeletedNodeCount >= 10);
            Assert.True(result.DeletedRelationshipCount >= 2);
            Assert.Equal(2, result.AffectedRunCount);
            Assert.NotNull(preservedFirstRun);
            Assert.NotNull(preservedSecondRun);
            Assert.Equal("snapshot://delete-all-one", preservedFirstRun.SnapshotIdentity);
            Assert.Equal("snapshot://delete-all-two", preservedSecondRun.SnapshotIdentity);
            Assert.Equal(2, graph.Repositories);
            Assert.Equal(2, graph.Solutions);
            Assert.Equal(0, graph.Rules);
            Assert.Equal(2, graph.Runs);
            Assert.Equal(0, graph.Snapshots);
            Assert.Equal(0, graph.ScopedNodes);
            Assert.Equal(0, graph.ProducedRelationships);
        }

        /// <summary>
        /// Confirms delete-all returns zero aggregate counts when no snapshots are persisted.
        /// </summary>
        /// <returns>A task that completes after no-op deletion assertions finish.</returns>
        [Fact]
        public async Task DeleteAllSnapshotsAsync_WhenNoSnapshotsExist_ShouldReturnZeroCounts()
        {
            // A clean database should accept confirmed cleanup as an idempotent no-op rather than a not-found error.
            await using ServiceProvider serviceProvider = CreateServiceProvider();
            IArchitectureGraphRecreator recreator = serviceProvider.GetRequiredService<IArchitectureGraphRecreator>();
            ISnapshotDeletionStore deletionStore = serviceProvider.GetRequiredService<ISnapshotDeletionStore>();
            await recreator.RecreateGraphAsync(GraphRecreationRequest.CreateAuthorized("snapshot delete-all no-op test"));

            SnapshotDeleteAllResult result = await deletionStore.DeleteAllSnapshotsAsync(new SnapshotDeleteAllRequest("delete-all-snapshots"), CancellationToken.None);

            Assert.Equal(0, result.DeletedSnapshotCount);
            Assert.Equal(0, result.DeletedNodeCount);
            Assert.Equal(0, result.DeletedRelationshipCount);
            Assert.Equal(0, result.AffectedRunCount);
            Assert.Empty(result.Warnings);
        }

        /// <summary>
        /// Confirms delete-one returns a not-found result without deleting unrelated graph records.
        /// </summary>
        /// <returns>A task that completes after not-found and preservation assertions finish.</returns>
        [Fact]
        public async Task DeleteSnapshotAsync_WhenSnapshotDoesNotExist_ShouldReturnNotFoundWithoutDeletingOtherSnapshots()
        {
            // A missing stable key should produce a no-delete result and leave existing graph data intact.
            await using ServiceProvider serviceProvider = CreateServiceProvider();
            IArchitectureGraphRecreator recreator = serviceProvider.GetRequiredService<IArchitectureGraphRecreator>();
            IArchitectureSnapshotWriter writer = serviceProvider.GetRequiredService<IArchitectureSnapshotWriter>();
            ISnapshotDeletionStore deletionStore = serviceProvider.GetRequiredService<ISnapshotDeletionStore>();
            IDriver driver = serviceProvider.GetRequiredService<IDriver>();
            await recreator.RecreateGraphAsync(GraphRecreationRequest.CreateAuthorized("snapshot delete-one not-found test"));
            await writer.WriteSnapshotAsync(CreateSnapshot("not-found-other"), CancellationToken.None);

            SnapshotDeletionResult result = await deletionStore.DeleteSnapshotAsync(new SnapshotDeletionRequest("snapshot://missing"), CancellationToken.None);
            GraphDeletionSnapshot graph = await ReadGraphDeletionSnapshotAsync(driver, "snapshot://missing", "snapshot://not-found-other", "run://unused");

            Assert.False(result.SnapshotDeleted);
            Assert.Equal(0, result.DeletedSnapshotCount);
            Assert.Equal(0, result.DeletedNodeCount);
            Assert.Equal(0, result.DeletedRelationshipCount);
            Assert.Equal(0, result.AffectedRunCount);
            Assert.Equal(1, graph.OtherSnapshots);
            Assert.True(graph.OtherScopedNodes > 0);
        }

        /// <summary>
        /// Creates a service provider with Neo4j infrastructure registration for deletion tests.
        /// </summary>
        /// <returns>A service provider configured for the shared Neo4j container.</returns>
        private ServiceProvider CreateServiceProvider()
        {
            // Tests use production registration so deletion behavior includes the same DI graph as a Neo4j-composed host.
            ServiceCollection services = new();
            services.AddLogging();
            services.AddArchonNeo4j(CreateNeo4jConfiguration());
            return services.BuildServiceProvider(validateScopes: true);
        }

        /// <summary>
        /// Creates a representative snapshot with repository, solution, nodes, evidence, metrics, rule, finding, and summaries.
        /// </summary>
        /// <param name="suffix">The unique suffix used to isolate stable identities for one test snapshot.</param>
        /// <returns>A representative extracted snapshot suitable for deletion tests.</returns>
        private static ExtractedArchitectureSnapshot CreateSnapshot(string suffix)
        {
            // The full mixed fixture exercises every snapshot-scoped label currently persisted by the Neo4j writer and related stores.
            return FullMixedSnapshotTestDataBuilder.Create(suffix);
        }

        /// <summary>
        /// Creates a completed extraction run that references the supplied snapshot stable key.
        /// </summary>
        /// <param name="snapshotStableKey">The produced snapshot stable key stored on the run.</param>
        /// <returns>A completed extraction run ready for run-history persistence.</returns>
        private static ExtractionRun CreateCompletedRun(string snapshotStableKey)
        {
            // The run is persisted after the snapshot so the run-history adapter creates a produced-snapshot relationship that deletion must remove.
            return new ExtractionRun(
                ExtractionRunId.New(),
                ExtractionRunStatus.Completed,
                new ExtractionRunRequestSummary("D:/src/delete-target", ["Archon.slnx"], "main", "abc123", "tester", ["ticket"]),
                DateTimeOffset.Parse("2026-05-20T08:00:00Z"),
                DateTimeOffset.Parse("2026-05-20T08:05:00Z"),
                new ExtractionRunProgress("Completed", "Snapshot persisted.", 100, DateTimeOffset.Parse("2026-05-20T08:05:00Z")),
                [],
                [],
                [],
                snapshotStableKey);
        }

        /// <summary>
        /// Reads public count-based graph state after a deletion attempt.
        /// </summary>
        /// <param name="driver">The configured Neo4j driver used to query the database.</param>
        /// <param name="targetSnapshotStableKey">The deleted or missing snapshot stable key.</param>
        /// <param name="otherSnapshotStableKey">The unrelated snapshot stable key expected to remain.</param>
        /// <param name="runId">The public run identifier expected to remain when supplied.</param>
        /// <returns>A graph snapshot containing public counts for deletion assertions.</returns>
        private static async Task<GraphDeletionSnapshot> ReadGraphDeletionSnapshotAsync(IDriver driver, string targetSnapshotStableKey, string otherSnapshotStableKey, string runId)
        {
            // Counts use stable-key predicates and public labels so tests verify observable graph semantics rather than internal node IDs.
            await using IAsyncSession session = driver.AsyncSession(sessionBuilder => sessionBuilder.WithDatabase("neo4j"));
            IResultCursor cursor = await session.RunAsync(@"
CALL { MATCH (repository:ArchonRepository) RETURN count(repository) AS repositories }
CALL { MATCH (solution:ArchonSolution) RETURN count(solution) AS solutions }
CALL { MATCH (rule:ArchonRule) RETURN count(rule) AS rules }
CALL { MATCH (run:ArchonExtractionRun { runId: $runId }) RETURN count(run) AS runs }
CALL { MATCH (snapshot:ArchonSnapshot { stableKey: $targetSnapshotStableKey }) RETURN count(snapshot) AS targetSnapshots }
CALL { MATCH (snapshot:ArchonSnapshot { stableKey: $otherSnapshotStableKey }) RETURN count(snapshot) AS otherSnapshots }
CALL {
    MATCH (record)
    WHERE (record:ArchonNode OR record:ArchonRelationship OR record:ArchonEvidence OR record:ArchonFinding OR record:ArchonMetric OR record:ArchonGeneratedSummary)
      AND record.snapshotStableKey = $targetSnapshotStableKey
    RETURN count(record) AS targetScopedNodes
}
CALL {
    MATCH (record)
    WHERE (record:ArchonNode OR record:ArchonRelationship OR record:ArchonEvidence OR record:ArchonFinding OR record:ArchonMetric OR record:ArchonGeneratedSummary)
      AND record.snapshotStableKey = $otherSnapshotStableKey
    RETURN count(record) AS otherScopedNodes
}
CALL { MATCH (:ArchonExtractionRun { runId: $runId })-[produced:PRODUCED_SNAPSHOT]->(:ArchonSnapshot { stableKey: $targetSnapshotStableKey }) RETURN count(produced) AS targetSnapshotProducedRelationships }
RETURN repositories,
       solutions,
       rules,
       runs,
       targetSnapshots,
       otherSnapshots,
       targetScopedNodes,
       otherScopedNodes,
       targetSnapshotProducedRelationships",
                new { targetSnapshotStableKey, otherSnapshotStableKey, runId });
            IRecord record = await cursor.SingleAsync();
            return new GraphDeletionSnapshot(
                record["repositories"].As<long>(),
                record["solutions"].As<long>(),
                record["rules"].As<long>(),
                record["runs"].As<long>(),
                record["targetSnapshots"].As<long>(),
                record["otherSnapshots"].As<long>(),
                record["targetScopedNodes"].As<long>(),
                record["otherScopedNodes"].As<long>(),
                record["targetSnapshotProducedRelationships"].As<long>());
        }

        /// <summary>
        /// Reads public count-based graph state after a delete-all operation.
        /// </summary>
        /// <param name="driver">The configured Neo4j driver used to query the database.</param>
        /// <param name="firstRunId">The first public run identifier expected to remain.</param>
        /// <param name="secondRunId">The second public run identifier expected to remain.</param>
        /// <returns>A graph snapshot containing public counts for delete-all assertions.</returns>
        private static async Task<GraphDeleteAllSnapshot> ReadGraphDeleteAllSnapshotAsync(IDriver driver, string firstRunId, string secondRunId)
        {
            // Counts verify the cleanup boundary: snapshot-scoped labels disappear while shared and operational labels remain.
            await using IAsyncSession session = driver.AsyncSession(sessionBuilder => sessionBuilder.WithDatabase("neo4j"));
            IResultCursor cursor = await session.RunAsync(@"
CALL { MATCH (repository:ArchonRepository) RETURN count(repository) AS repositories }
CALL { MATCH (solution:ArchonSolution) RETURN count(solution) AS solutions }
CALL { MATCH (rule:ArchonRule) RETURN count(rule) AS rules }
CALL { MATCH (run:ArchonExtractionRun) WHERE run.runId IN [$firstRunId, $secondRunId] RETURN count(run) AS runs }
CALL { MATCH (snapshot:ArchonSnapshot) RETURN count(snapshot) AS snapshots }
CALL {
    MATCH (record)
    WHERE record:ArchonNode OR record:ArchonRelationship OR record:ArchonEvidence OR record:ArchonFinding OR record:ArchonMetric OR record:ArchonGeneratedSummary
    RETURN count(record) AS scopedNodes
}
CALL { MATCH (:ArchonExtractionRun)-[produced:PRODUCED_SNAPSHOT]->(:ArchonSnapshot) RETURN count(produced) AS producedRelationships }
RETURN repositories,
       solutions,
       rules,
       runs,
       snapshots,
       scopedNodes,
       producedRelationships",
                new { firstRunId, secondRunId });
            IRecord record = await cursor.SingleAsync();
            return new GraphDeleteAllSnapshot(
                record["repositories"].As<long>(),
                record["solutions"].As<long>(),
                record["rules"].As<long>(),
                record["runs"].As<long>(),
                record["snapshots"].As<long>(),
                record["scopedNodes"].As<long>(),
                record["producedRelationships"].As<long>());
        }

        /// <summary>
        /// Captures public graph counts used to verify delete-one preservation and cleanup behavior.
        /// </summary>
        /// <param name="Repositories">The number of preserved repository records.</param>
        /// <param name="Solutions">The number of preserved solution records.</param>
        /// <param name="Rules">The number of preserved rule records.</param>
        /// <param name="Runs">The number of preserved extraction run records matching the requested run id.</param>
        /// <param name="TargetSnapshots">The number of target snapshot headers that remain.</param>
        /// <param name="OtherSnapshots">The number of unrelated snapshot headers that remain.</param>
        /// <param name="TargetScopedNodes">The number of target snapshot-scoped data nodes that remain.</param>
        /// <param name="OtherScopedNodes">The number of unrelated snapshot-scoped data nodes that remain.</param>
        /// <param name="TargetSnapshotProducedRelationships">The number of produced-snapshot links from the preserved run to the deleted snapshot.</param>
        private sealed record GraphDeletionSnapshot(
            long Repositories,
            long Solutions,
            long Rules,
            long Runs,
            long TargetSnapshots,
            long OtherSnapshots,
            long TargetScopedNodes,
            long OtherScopedNodes,
            long TargetSnapshotProducedRelationships);

        /// <summary>
        /// Captures public graph counts used to verify delete-all preservation and cleanup behavior.
        /// </summary>
        /// <param name="Repositories">The number of preserved repository records.</param>
        /// <param name="Solutions">The number of preserved solution records.</param>
        /// <param name="Rules">The number of preserved rule records.</param>
        /// <param name="Runs">The number of preserved extraction run records matching the requested run identifiers.</param>
        /// <param name="Snapshots">The number of snapshot headers that remain.</param>
        /// <param name="ScopedNodes">The number of snapshot-scoped data nodes that remain.</param>
        /// <param name="ProducedRelationships">The number of produced-snapshot links from preserved runs to deleted snapshots.</param>
        private sealed record GraphDeleteAllSnapshot(
            long Repositories,
            long Solutions,
            long Rules,
            long Runs,
            long Snapshots,
            long ScopedNodes,
            long ProducedRelationships);
    }
}
