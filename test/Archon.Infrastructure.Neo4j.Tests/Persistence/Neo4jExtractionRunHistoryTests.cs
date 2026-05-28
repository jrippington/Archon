using Archon.Application.Extraction.Resolution;
using Archon.Application.Extraction.Runs;
using Archon.Application.Graph.Persistence;
using Archon.Infrastructure.Neo4j.DependencyInjection;
using Archon.Infrastructure.Neo4j.Schema;
using Archon.Infrastructure.Neo4j.Tests.Testcontainers;
using Microsoft.Extensions.DependencyInjection;
using Neo4j.Driver;
using Xunit;

namespace Archon.Infrastructure.Neo4j.Tests.Persistence
{
    /// <summary>
    /// Verifies Neo4j-backed extraction run history persists accepted runs and lifecycle updates durably.
    /// </summary>
    public sealed class Neo4jExtractionRunHistoryTests : Neo4jIntegrationTestBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Neo4jExtractionRunHistoryTests"/> class.
        /// </summary>
        /// <param name="fixture">The Neo4j Testcontainers fixture that supplies a real database for persistence validation.</param>
        public Neo4jExtractionRunHistoryTests(Neo4jContainerFixture fixture)
            : base(fixture)
        {
            // Shared fixture construction keeps each test focused on run-history behavior rather than container lifecycle setup.
        }

        /// <summary>
        /// Confirms accepted run creation persists a run node and safe request summary that can be read back by identifier.
        /// </summary>
        /// <returns>A task that completes after the persisted run has been asserted.</returns>
        [Fact]
        public async Task CreateAsync_WhenInputIsValid_ShouldPersistRunAndRequestSummary()
        {
            // A fresh graph isolates this run-history test from any prior snapshot persistence tests sharing the same container.
            await using ServiceProvider serviceProvider = CreateServiceProvider();
            IArchitectureGraphRecreator recreator = serviceProvider.GetRequiredService<IArchitectureGraphRecreator>();
            IExtractionRunHistory history = serviceProvider.GetRequiredService<IExtractionRunHistory>();
            await recreator.RecreateGraphAsync(GraphRecreationRequest.CreateAuthorized("run history create test"));
            ResolvedExtractionInput input = CreateResolvedInput("create");
            DateTimeOffset startedUtc = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

            ExtractionRun created = await history.CreateAsync(input, startedUtc, CancellationToken.None);
            ExtractionRun? persisted = await history.GetAsync(created.RunId, CancellationToken.None);

            Assert.NotNull(persisted);
            Assert.Equal(created.RunId, persisted.RunId);
            Assert.Equal(ExtractionRunStatus.Queued, persisted.Status);
            Assert.Equal(startedUtc, persisted.StartedUtc);
            Assert.Null(persisted.CompletedUtc);
            Assert.Equal("Queued", persisted.Progress.Stage);
            Assert.Equal(0, persisted.Progress.Percentage);
            Assert.Equal(input.RepositoryRootDirectory, persisted.SubmittedRequest.RepositoryRootDirectory);
            Assert.Equal(input.SolutionPaths, persisted.SubmittedRequest.SolutionPaths);
            Assert.Equal(["correlation", "source"], persisted.SubmittedRequest.MetadataKeys);
        }

        /// <summary>
        /// Confirms lifecycle updates replace run state while preserving the original request summary.
        /// </summary>
        /// <returns>A task that completes after the updated run has been asserted.</returns>
        [Fact]
        public async Task UpdateAsync_WhenRunExists_ShouldPersistTerminalStateWithoutErasingRequest()
        {
            // The update path models orchestration progress and terminal completion after the accepted request has already returned.
            await using ServiceProvider serviceProvider = CreateServiceProvider();
            IArchitectureGraphRecreator recreator = serviceProvider.GetRequiredService<IArchitectureGraphRecreator>();
            IExtractionRunHistory history = serviceProvider.GetRequiredService<IExtractionRunHistory>();
            await recreator.RecreateGraphAsync(GraphRecreationRequest.CreateAuthorized("run history update test"));
            ExtractionRun created = await history.CreateAsync(CreateResolvedInput("update"), new DateTimeOffset(2026, 1, 3, 3, 4, 5, TimeSpan.Zero), CancellationToken.None);
            DateTimeOffset completedUtc = new(2026, 1, 3, 3, 5, 5, TimeSpan.Zero);
            ExtractionRun updated = created.WithStatus(
                    ExtractionRunStatus.Completed,
                    new ExtractionRunProgress("Completed", "Extraction snapshot persisted successfully.", 100, completedUtc),
                    completedUtc,
                    "snapshot://run-history-update")
                .WithDiagnostics(
                    [new ExtractionRunWarning("Warning.Code", "Safe warning", "Persistence", completedUtc)],
                    [new ExtractionRunError("Error.Code", "Safe error", "Persistence", completedUtc)])
                .WithTimings([new ExtractionRunTiming("Total", 1234, completedUtc)])
                .WithPersistenceDiagnostics(new ExtractionRunPersistenceDiagnostics(
                    [new ExtractionRunTiming("Persistence.Commit", 456, completedUtc)],
                    new ExtractionRunPersistenceCounts(1, 1, 1, 0, 2, 0, 1, 0, 1, 1, 0, 0, null, 4, 1, 2048),
                    completed: true));

            await history.UpdateAsync(updated, CancellationToken.None);
            ExtractionRun? persisted = await history.GetAsync(created.RunId, CancellationToken.None);

            Assert.NotNull(persisted);
            Assert.Equal(ExtractionRunStatus.Completed, persisted.Status);
            Assert.Equal(completedUtc, persisted.CompletedUtc);
            Assert.Equal("snapshot://run-history-update", persisted.SnapshotIdentity);
            Assert.Single(persisted.Warnings);
            Assert.Single(persisted.Errors);
            Assert.Single(persisted.Timings);
            Assert.NotNull(persisted.PersistenceDiagnostics);
            Assert.Equal(created.SubmittedRequest.RepositoryRootDirectory, persisted.SubmittedRequest.RepositoryRootDirectory);
            Assert.Equal(created.SubmittedRequest.SolutionPaths, persisted.SubmittedRequest.SolutionPaths);
        }

        /// <summary>
        /// Confirms terminal run updates persist compact diagnostic details that reconstruct the public run status contract.
        /// </summary>
        /// <returns>A task that completes after terminal diagnostics have been read back from Neo4j.</returns>
        [Fact]
        public async Task UpdateAsync_WhenRunCompletesWithDiagnostics_ShouldReconstructTerminalDiagnostics()
        {
            // WP019 stores warning, error, timing, and persistence diagnostic details compactly on the run node because the current API
            // consumes the complete diagnostic collection for one run rather than querying diagnostics independently by category.
            await using ServiceProvider serviceProvider = CreateServiceProvider();
            IArchitectureGraphRecreator recreator = serviceProvider.GetRequiredService<IArchitectureGraphRecreator>();
            IExtractionRunHistory history = serviceProvider.GetRequiredService<IExtractionRunHistory>();
            await recreator.RecreateGraphAsync(GraphRecreationRequest.CreateAuthorized("run history terminal diagnostics test"));
            DateTimeOffset completedUtc = new(2026, 1, 4, 3, 5, 5, TimeSpan.Zero);
            ExtractionRun created = await history.CreateAsync(CreateResolvedInput("terminal-diagnostics"), completedUtc.AddMinutes(-1), CancellationToken.None);
            ExtractionRun terminal = created.WithStatus(
                    ExtractionRunStatus.Completed,
                    new ExtractionRunProgress("Completed", "Extraction snapshot persisted successfully.", 100, completedUtc),
                    completedUtc,
                    "snapshot://terminal-diagnostics")
                .WithDiagnostics(
                    [new ExtractionRunWarning("Warning.Terminal", "Safe terminal warning", "Persistence", completedUtc)],
                    [new ExtractionRunError("Error.Terminal", "Safe terminal error", "Persistence", completedUtc)])
                .WithTimings(
                    [
                        new ExtractionRunTiming("Persistence", 42, completedUtc),
                        new ExtractionRunTiming("Total", 84, completedUtc.AddMilliseconds(84))
                    ])
                .WithPersistenceDiagnostics(CreatePersistenceDiagnostics(completedUtc, completed: true));

            await history.UpdateAsync(terminal, CancellationToken.None);
            ExtractionRun? persisted = await history.GetAsync(created.RunId, CancellationToken.None);

            Assert.NotNull(persisted);
            Assert.Equal(ExtractionRunStatus.Completed, persisted.Status);
            Assert.Equal(completedUtc, persisted.CompletedUtc);
            Assert.Equal("snapshot://terminal-diagnostics", persisted.SnapshotIdentity);
            ExtractionRunWarning warning = Assert.Single(persisted.Warnings);
            Assert.Equal("Warning.Terminal", warning.Code);
            ExtractionRunError error = Assert.Single(persisted.Errors);
            Assert.Equal("Error.Terminal", error.Code);
            Assert.Collection(
                persisted.Timings,
                timing => Assert.Equal("Persistence", timing.Stage),
                timing => Assert.Equal("Total", timing.Stage));
            Assert.NotNull(persisted.PersistenceDiagnostics);
            Assert.True(persisted.PersistenceDiagnostics.Completed);
            Assert.Equal(14, persisted.PersistenceDiagnostics.Counts.PersistenceOperationCount);
            Assert.Collection(
                persisted.PersistenceDiagnostics.Timings,
                timing => Assert.Equal("Persistence.PrepareSnapshot", timing.Stage),
                timing => Assert.Equal("Persistence.Commit", timing.Stage));
        }

        /// <summary>
        /// Confirms failed terminal runs remain queryable with diagnostics and no produced snapshot identity.
        /// </summary>
        /// <returns>A task that completes after the failed run has been read back from durable storage.</returns>
        [Fact]
        public async Task UpdateAsync_WhenRunFailsWithoutSnapshot_ShouldPersistDiagnosticsWithoutSnapshotLink()
        {
            // Failed runs are operational records even when no snapshot was produced, so persistence must retain terminal diagnostics without
            // requiring snapshot identity or a graph relationship to exist.
            await using ServiceProvider serviceProvider = CreateServiceProvider();
            IArchitectureGraphRecreator recreator = serviceProvider.GetRequiredService<IArchitectureGraphRecreator>();
            IExtractionRunHistory history = serviceProvider.GetRequiredService<IExtractionRunHistory>();
            IDriver driver = serviceProvider.GetRequiredService<IDriver>();
            await recreator.RecreateGraphAsync(GraphRecreationRequest.CreateAuthorized("run history failed diagnostics test"));
            DateTimeOffset failedUtc = new(2026, 1, 5, 6, 7, 8, TimeSpan.Zero);
            ExtractionRun created = await history.CreateAsync(CreateResolvedInput("failed-diagnostics"), failedUtc.AddMinutes(-2), CancellationToken.None);
            ExtractionRun failed = created.WithStatus(
                    ExtractionRunStatus.Failed,
                    new ExtractionRunProgress("Failed", "Extraction failed before snapshot persistence completed.", 100, failedUtc),
                    failedUtc)
                .WithDiagnostics(
                    warnings: null,
                    [new ExtractionRunError("PersistenceUnavailable", "Snapshot persistence failed safely.", "Persistence", failedUtc)])
                .WithPersistenceDiagnostics(CreatePersistenceDiagnostics(failedUtc, completed: false));

            await history.UpdateAsync(failed, CancellationToken.None);
            ExtractionRun? persisted = await history.GetAsync(created.RunId, CancellationToken.None);
            int relationshipCount = await ReadProducedSnapshotRelationshipCountAsync(driver, created.RunId.ToString());

            Assert.NotNull(persisted);
            Assert.Equal(ExtractionRunStatus.Failed, persisted.Status);
            Assert.Equal(failedUtc, persisted.CompletedUtc);
            Assert.Null(persisted.SnapshotIdentity);
            Assert.Empty(persisted.Warnings);
            Assert.Single(persisted.Errors);
            Assert.NotNull(persisted.PersistenceDiagnostics);
            Assert.False(persisted.PersistenceDiagnostics.Completed);
            Assert.Equal(0, relationshipCount);
        }

        /// <summary>
        /// Confirms successful terminal updates create a produced-snapshot relationship when the target snapshot exists.
        /// </summary>
        /// <returns>A task that completes after the relationship has been asserted in Neo4j.</returns>
        [Fact]
        public async Task UpdateAsync_WhenCompletedRunReferencesExistingSnapshot_ShouldCreateProducedSnapshotRelationship()
        {
            // The produced-snapshot edge connects operational history to the durable architecture snapshot while keeping both identities public
            // and stable rather than relying on Neo4j internal node ids.
            await using ServiceProvider serviceProvider = CreateServiceProvider();
            IArchitectureGraphRecreator recreator = serviceProvider.GetRequiredService<IArchitectureGraphRecreator>();
            IExtractionRunHistory history = serviceProvider.GetRequiredService<IExtractionRunHistory>();
            IDriver driver = serviceProvider.GetRequiredService<IDriver>();
            await recreator.RecreateGraphAsync(GraphRecreationRequest.CreateAuthorized("run history produced snapshot relationship test"));
            string snapshotStableKey = "snapshot://produced-snapshot-link";
            await CreateSnapshotNodeAsync(driver, snapshotStableKey);
            DateTimeOffset completedUtc = new(2026, 1, 6, 6, 7, 8, TimeSpan.Zero);
            ExtractionRun created = await history.CreateAsync(CreateResolvedInput("produced-snapshot-link"), completedUtc.AddMinutes(-3), CancellationToken.None);
            ExtractionRun completed = created.WithStatus(
                ExtractionRunStatus.Completed,
                new ExtractionRunProgress("Completed", "Extraction snapshot persisted successfully.", 100, completedUtc),
                completedUtc,
                snapshotStableKey);

            await history.UpdateAsync(completed, CancellationToken.None);
            await history.UpdateAsync(completed, CancellationToken.None);
            int relationshipCount = await ReadProducedSnapshotRelationshipCountAsync(driver, created.RunId.ToString());
            ExtractionRun? persisted = await history.GetAsync(created.RunId, CancellationToken.None);

            Assert.NotNull(persisted);
            Assert.Equal(snapshotStableKey, persisted.SnapshotIdentity);
            Assert.Equal(1, relationshipCount);
        }

        /// <summary>
        /// Confirms snapshot identity remains durable when the target snapshot node is not yet available for relationship creation.
        /// </summary>
        /// <returns>A task that completes after missing-snapshot relationship behavior has been asserted.</returns>
        [Fact]
        public async Task UpdateAsync_WhenCompletedRunReferencesMissingSnapshot_ShouldPersistSnapshotIdentityWithoutRelationship()
        {
            // Snapshot persistence can fail after a run has collected a candidate identity, so the run node keeps the public identity while
            // the relationship is created only when the corresponding snapshot node exists.
            await using ServiceProvider serviceProvider = CreateServiceProvider();
            IArchitectureGraphRecreator recreator = serviceProvider.GetRequiredService<IArchitectureGraphRecreator>();
            IExtractionRunHistory history = serviceProvider.GetRequiredService<IExtractionRunHistory>();
            IDriver driver = serviceProvider.GetRequiredService<IDriver>();
            await recreator.RecreateGraphAsync(GraphRecreationRequest.CreateAuthorized("run history missing snapshot relationship test"));
            string snapshotStableKey = "snapshot://missing-produced-snapshot";
            DateTimeOffset completedUtc = new(2026, 1, 7, 6, 7, 8, TimeSpan.Zero);
            ExtractionRun created = await history.CreateAsync(CreateResolvedInput("missing-produced-snapshot"), completedUtc.AddMinutes(-3), CancellationToken.None);
            ExtractionRun completed = created.WithStatus(
                ExtractionRunStatus.Completed,
                new ExtractionRunProgress("Completed", "Extraction snapshot identity was recorded but graph linkage is pending.", 100, completedUtc),
                completedUtc,
                snapshotStableKey);

            await history.UpdateAsync(completed, CancellationToken.None);
            int relationshipCount = await ReadProducedSnapshotRelationshipCountAsync(driver, created.RunId.ToString());
            ExtractionRun? persisted = await history.GetAsync(created.RunId, CancellationToken.None);

            Assert.NotNull(persisted);
            Assert.Equal(snapshotStableKey, persisted.SnapshotIdentity);
            Assert.Equal(0, relationshipCount);
        }

        /// <summary>
        /// Confirms recent run queries use deterministic newest-first ordering with run identifier tie-breaking.
        /// </summary>
        /// <returns>A task that completes after recent run ordering has been asserted.</returns>
        [Fact]
        public async Task GetRecentAsync_WhenRunsExist_ShouldReturnNewestRunsFirst()
        {
            // The ordering contract matches the in-memory implementation so API history responses remain stable after persistence migration.
            await using ServiceProvider serviceProvider = CreateServiceProvider();
            IArchitectureGraphRecreator recreator = serviceProvider.GetRequiredService<IArchitectureGraphRecreator>();
            IExtractionRunHistory history = serviceProvider.GetRequiredService<IExtractionRunHistory>();
            await recreator.RecreateGraphAsync(GraphRecreationRequest.CreateAuthorized("run history recent test"));
            ExtractionRun older = await history.CreateAsync(CreateResolvedInput("older"), new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero), CancellationToken.None);
            ExtractionRun newer = await history.CreateAsync(CreateResolvedInput("newer"), new DateTimeOffset(2026, 1, 2, 12, 0, 0, TimeSpan.Zero), CancellationToken.None);

            IReadOnlyList<ExtractionRun> recent = await history.GetRecentAsync(100, CancellationToken.None);
            ExtractionRun? recentMatchingRun = recent
                .Where(run => run.SubmittedRequest.RepositoryRootDirectory.Contains("/newer", StringComparison.Ordinal))
                .FirstOrDefault();
            List<ExtractionRun> recentList = recent.ToList();

            Assert.NotNull(recentMatchingRun);
            Assert.Equal(newer.RunId, recentMatchingRun.RunId);
            Assert.True(
                recentList.IndexOf(recentMatchingRun) < recentList.IndexOf(recentList.Single(run => run.RunId == older.RunId)),
                "The newer run should appear before the older run among matching test records even when shared-container data exists.");
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
        /// Creates deterministic normalized extraction input for run-history persistence tests.
        /// </summary>
        /// <param name="suffix">The unique suffix used to isolate request paths.</param>
        /// <returns>A resolved extraction input containing safe metadata keys and no sensitive metadata values.</returns>
        private static ResolvedExtractionInput CreateResolvedInput(string suffix)
        {
            // The metadata values intentionally differ from the keys so tests prove only keys are retained in request summaries.
            Dictionary<string, string> metadata = new(StringComparer.Ordinal)
            {
                ["source"] = "unit-test",
                ["correlation"] = "secret-value-not-persisted-in-summary"
            };

            return new ResolvedExtractionInput(
                $"D:/Repos/{suffix}",
                [$"D:/Repos/{suffix}/{suffix}.slnx"],
                BranchName: "main",
                CommitSha: "abcdef1234567890",
                RequestedBy: "developer@example.invalid",
                Metadata: metadata);
        }

        /// <summary>
        /// Creates deterministic persistence diagnostics for terminal run-history tests.
        /// </summary>
        /// <param name="completedUtc">The timestamp used for deterministic diagnostic timing records.</param>
        /// <param name="completed">A value indicating whether the diagnostic set represents a completed persistence attempt.</param>
        /// <returns>A persistence diagnostics object with stable timing and count values.</returns>
        private static ExtractionRunPersistenceDiagnostics CreatePersistenceDiagnostics(DateTimeOffset completedUtc, bool completed)
        {
            // The distinct counts make round-trip assertions sensitive to field mapping errors while remaining small and easy to inspect.
            return new ExtractionRunPersistenceDiagnostics(
                [
                    new ExtractionRunTiming("Persistence.PrepareSnapshot", 12, completedUtc),
                    new ExtractionRunTiming("Persistence.Commit", 34, completedUtc.AddMilliseconds(34))
                ],
                new ExtractionRunPersistenceCounts(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16),
                completed);
        }

        /// <summary>
        /// Creates a minimal snapshot node that can be linked from a completed extraction run.
        /// </summary>
        /// <param name="driver">The Neo4j driver connected to the integration-test database.</param>
        /// <param name="snapshotStableKey">The public snapshot stable key to store on the snapshot node.</param>
        /// <returns>A task that completes after the snapshot node has been committed.</returns>
        private static async Task CreateSnapshotNodeAsync(IDriver driver, string snapshotStableKey)
        {
            // The test only needs the existing snapshot identity node because produced-snapshot linkage matches by the stable key property.
            await using IAsyncSession session = driver.AsyncSession();
            await session.ExecuteWriteAsync(async transaction =>
            {
                IResultCursor cursor = await transaction.RunAsync(
                    $@"
MERGE (snapshot:{Neo4jSchemaNames.Labels.Snapshot} {{{Neo4jSchemaNames.Properties.StableKey}: $snapshotStableKey}})
SET snapshot.{Neo4jSchemaNames.Properties.Status} = $status,
    snapshot.{Neo4jSchemaNames.Properties.StartedUtc} = $startedUtc",
                    new
                    {
                        snapshotStableKey,
                        status = "Completed",
                        startedUtc = "2026-01-06T06:00:00.0000000+00:00"
                    }).ConfigureAwait(false);
                await cursor.ConsumeAsync().ConfigureAwait(false);
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads the number of produced-snapshot relationships attached to one extraction run.
        /// </summary>
        /// <param name="driver">The Neo4j driver connected to the integration-test database.</param>
        /// <param name="runId">The public extraction run identifier to inspect.</param>
        /// <returns>The number of produced-snapshot relationships for the run.</returns>
        private static async Task<int> ReadProducedSnapshotRelationshipCountAsync(IDriver driver, string runId)
        {
            // Counting the relationship directly proves graph linkage without depending on internal Neo4j node ids or driver-specific ids.
            await using IAsyncSession session = driver.AsyncSession(o => o.WithDefaultAccessMode(AccessMode.Read));
            return await session.ExecuteReadAsync(async transaction =>
            {
                IResultCursor cursor = await transaction.RunAsync(
                    $@"
MATCH (run:{Neo4jSchemaNames.Labels.ExtractionRun} {{{Neo4jSchemaNames.Properties.RunId}: $runId}})
OPTIONAL MATCH (run)-[relationship:{Neo4jSchemaNames.Relationships.ProducedSnapshot}]->(:{Neo4jSchemaNames.Labels.Snapshot})
RETURN count(relationship) AS relationshipCount",
                    new { runId }).ConfigureAwait(false);
                IRecord record = await cursor.SingleAsync().ConfigureAwait(false);
                return record["relationshipCount"].As<int>();
            }).ConfigureAwait(false);
        }
    }
}
