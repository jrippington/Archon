using Archon.Application.Extraction.Contracts;
using Archon.Application.Extraction.Runs;
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
using System.Globalization;
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
            Assert.Equal(1, result.Counts.Metrics);
            Assert.Equal(1, result.Counts.SnapshotSolutionRelationships);
            Assert.Equal(1, result.Counts.NodeEvidenceRelationships);
            Assert.Equal(1, result.Counts.MetricEvidenceRelationships);
            Assert.Equal(1, result.Counts.MetricTargetRelationships);
            Assert.Equal(1, counts.Repositories);
            Assert.Equal(1, counts.Solutions);
            Assert.Equal(1, counts.Snapshots);
            Assert.Equal(1, counts.Nodes);
            Assert.Equal(1, counts.Evidence);
            Assert.Equal(1, counts.Metrics);
            Assert.Equal(1, counts.SnapshotSolutionRelationships);
            Assert.Equal(1, counts.NodeEvidenceRelationships);
            Assert.Equal(1, counts.MetricEvidenceRelationships);
            Assert.Equal(1, counts.MetricTargetRelationships);
            Assert.Equal("sha256:node-minimal-one", nodeFingerprint);
        }

        /// <summary>
        /// Confirms the optimized batched writer preserves the public graph shape, diagnostics, and idempotency of a mixed snapshot.
        /// </summary>
        /// <returns>A task that completes after the representative graph has been written twice and queried through stable-key paths.</returns>
        [Fact]
        public async Task WriteSnapshotAsyncOptimizedPersistencePreservesGraphEquivalenceDiagnosticsAndIdempotency()
        {
            // The fixture deliberately combines repositories, solutions, snapshots, nodes, evidence, metrics, and all support relationship
            // families affected by WP017 batching. A batch size of two forces multiple Cypher executions for node and metric record groups
            // while the repeated write proves the optimized MERGE paths keep the graph equivalent and idempotent by stable keys.
            await using ServiceProvider serviceProvider = CreateServiceProvider(persistenceBatchSize: 2);
            IArchitectureGraphRecreator recreator = serviceProvider.GetRequiredService<IArchitectureGraphRecreator>();
            IArchitectureSnapshotWriter writer = serviceProvider.GetRequiredService<IArchitectureSnapshotWriter>();
            IDriver driver = serviceProvider.GetRequiredService<IDriver>();
            await recreator.RecreateGraphAsync(GraphRecreationRequest.CreateAuthorized("optimized graph equivalence persistence test"));
            ExtractedArchitectureSnapshot snapshot = FullMixedSnapshotTestDataBuilder.Create("optimized-equivalence");

            SnapshotPersistenceResult first = await writer.WriteSnapshotAsync(snapshot);
            SnapshotPersistenceResult second = await writer.WriteSnapshotAsync(snapshot);
            GraphEquivalenceSnapshot graph = await ReadGraphEquivalenceSnapshotAsync(driver, "snapshot://optimized-equivalence");

            Assert.True(first.Succeeded);
            Assert.True(second.Succeeded);
            Assert.Equal(1, second.Counts.Repositories);
            Assert.Equal(1, second.Counts.Solutions);
            Assert.Equal(1, second.Counts.Snapshots);
            Assert.Equal(3, second.Counts.Nodes);
            Assert.Equal(3, second.Counts.Evidence);
            Assert.Equal(2, second.Counts.Metrics);
            Assert.Equal(1, second.Counts.SnapshotSolutionRelationships);
            Assert.Equal(3, second.Counts.NodeEvidenceRelationships);
            Assert.Equal(2, second.Counts.MetricEvidenceRelationships);
            Assert.Equal(2, second.Counts.MetricTargetRelationships);
            Assert.Equal(1, graph.Repositories);
            Assert.Equal(1, graph.Solutions);
            Assert.Equal(1, graph.Snapshots);
            Assert.Equal(3, graph.Nodes);
            Assert.Equal(3, graph.Evidence);
            Assert.Equal(2, graph.Metrics);
            Assert.Equal(1, graph.SnapshotSolutionRelationships);
            Assert.Equal(3, graph.NodeEvidenceRelationships);
            Assert.Equal(2, graph.MetricEvidenceRelationships);
            Assert.Equal(2, graph.MetricTargetRelationships);
            Assert.Equal(new[] { "endpoint://optimized-equivalence/health", "package://optimized-equivalence/neo4j", "project://optimized-equivalence/app" }, graph.NodeStableKeys);
            Assert.Equal(new[] { "evidence://optimized-equivalence/endpoint", "evidence://optimized-equivalence/package-reference", "evidence://optimized-equivalence/project-file" }, graph.EvidenceStableKeys);
            Assert.Equal(new[] { "metric://optimized-equivalence/dependency-count", "metric://optimized-equivalence/project-health" }, graph.MetricStableKeys);
            Assert.Equal(new[]
            {
                "metric://optimized-equivalence/dependency-count->evidence://optimized-equivalence/package-reference",
                "metric://optimized-equivalence/project-health->evidence://optimized-equivalence/project-file"
            }, graph.MetricEvidencePairs);
            Assert.Equal(new[]
            {
                "metric://optimized-equivalence/dependency-count->project://optimized-equivalence/app",
                "metric://optimized-equivalence/project-health->project://optimized-equivalence/app"
            }, graph.MetricTargetPairs);
            Assert.NotNull(second.Diagnostics);
            Assert.True(second.Diagnostics.Completed);
            Assert.Equal(1, second.Diagnostics.Counts.PersistenceBatchCount);
            Assert.Equal(13, second.Diagnostics.Counts.PersistenceOperationCount);
            Assert.Contains(second.Diagnostics.Timings, static timing => StringComparer.Ordinal.Equals("Persistence.Total", timing.Stage));
            Assert.Contains(second.Diagnostics.Timings, static timing => StringComparer.Ordinal.Equals("Persistence.Commit", timing.Stage));
            Assert.Contains(second.Diagnostics.Timings, static timing => StringComparer.Ordinal.Equals("Persistence.WriteRelationships", timing.Stage));
            Assert.Contains(second.Diagnostics.Timings, static timing => StringComparer.Ordinal.Equals("Persistence.WriteSnapshotSolutionRelationships", timing.Stage));
            Assert.Contains(second.Diagnostics.Timings, static timing => StringComparer.Ordinal.Equals("Persistence.WriteNodeEvidenceRelationships", timing.Stage));
            Assert.Contains(second.Diagnostics.Timings, static timing => StringComparer.Ordinal.Equals("Persistence.WriteMetricEvidenceRelationships", timing.Stage));
            Assert.Contains(second.Diagnostics.Timings, static timing => StringComparer.Ordinal.Equals("Persistence.WriteMetricTargetRelationships", timing.Stage));
            Assert.True(FindTiming(second.Diagnostics.Timings, "Persistence.Total").ElapsedMilliseconds >= FindTiming(second.Diagnostics.Timings, "Persistence.Commit").ElapsedMilliseconds);
            Assert.DoesNotContain(second.Diagnostics.Timings, static timing => timing.Stage.Contains("MATCH", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(second.Errors, static error => error.Message.Contains("MATCH", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Confirms successful Neo4j snapshot persistence returns the WP016 diagnostic timing and count breakdown.
        /// </summary>
        /// <returns>A task that completes after diagnostic details have been collected from a real Neo4j write.</returns>
        [Fact]
        public async Task WriteSnapshotAsyncReturnsPersistenceDiagnosticsForSuccessfulWrite()
        {
            // The diagnostic contract is asserted from the public writer result so tests do not couple to internal collector types.
            await using ServiceProvider serviceProvider = CreateServiceProvider();
            IArchitectureGraphRecreator recreator = serviceProvider.GetRequiredService<IArchitectureGraphRecreator>();
            IArchitectureSnapshotWriter writer = serviceProvider.GetRequiredService<IArchitectureSnapshotWriter>();
            await recreator.RecreateGraphAsync(GraphRecreationRequest.CreateAuthorized("persistence diagnostics success test"));
            ExtractedArchitectureSnapshot snapshot = CreateMinimalSnapshot("diagnostics-success", duplicateEvidence: true);

            SnapshotPersistenceResult result = await writer.WriteSnapshotAsync(snapshot);

            Assert.True(result.Succeeded);
            Assert.NotNull(result.Diagnostics);
            Assert.True(result.Diagnostics.Completed);
            Assert.Equal(1, result.Diagnostics.Counts.RepositoryCount);
            Assert.Equal(1, result.Diagnostics.Counts.SolutionCount);
            Assert.Equal(2, result.Diagnostics.Counts.ProjectCount);
            Assert.Equal(2, result.Diagnostics.Counts.FileCount);
            Assert.Equal(2, result.Diagnostics.Counts.NodeCount);
            Assert.Equal(1, result.Diagnostics.Counts.EvidenceCount);
            Assert.Equal(1, result.Diagnostics.Counts.MetricCount);
            Assert.Equal(0, result.Diagnostics.Counts.WarningCount);
            Assert.Equal(1, result.Diagnostics.Counts.MetadataEntryCount);
            Assert.Equal(10, result.Diagnostics.Counts.PersistenceOperationCount);
            Assert.Equal(1, result.Diagnostics.Counts.PersistenceBatchCount);
            Assert.Null(result.Diagnostics.Counts.SerializedPayloadBytes);
            Assert.Contains(result.Diagnostics.Timings, timing => StringComparer.Ordinal.Equals("Persistence.Total", timing.Stage));
            Assert.Contains(result.Diagnostics.Timings, timing => StringComparer.Ordinal.Equals("Persistence.PrepareSnapshot", timing.Stage));
            Assert.Contains(result.Diagnostics.Timings, timing => StringComparer.Ordinal.Equals("Persistence.Indexing", timing.Stage));
            Assert.Contains(result.Diagnostics.Timings, timing => StringComparer.Ordinal.Equals("Persistence.MaterializePayload", timing.Stage));
            Assert.Contains(result.Diagnostics.Timings, timing => StringComparer.Ordinal.Equals("Persistence.NormalizeIdentities", timing.Stage));
            Assert.Contains(result.Diagnostics.Timings, timing => StringComparer.Ordinal.Equals("Persistence.WriteRepositories", timing.Stage));
            Assert.Contains(result.Diagnostics.Timings, timing => StringComparer.Ordinal.Equals("Persistence.WriteSolutions", timing.Stage));
            Assert.Contains(result.Diagnostics.Timings, timing => StringComparer.Ordinal.Equals("Persistence.WriteSnapshotHeader", timing.Stage));
            Assert.Contains(result.Diagnostics.Timings, timing => StringComparer.Ordinal.Equals("Persistence.WriteNodes", timing.Stage));
            Assert.Contains(result.Diagnostics.Timings, timing => StringComparer.Ordinal.Equals("Persistence.WriteEvidence", timing.Stage));
            Assert.Contains(result.Diagnostics.Timings, timing => StringComparer.Ordinal.Equals("Persistence.WriteMetrics", timing.Stage));
            Assert.Contains(result.Diagnostics.Timings, timing => StringComparer.Ordinal.Equals("Persistence.WriteRelationships", timing.Stage));
            Assert.Contains(result.Diagnostics.Timings, timing => StringComparer.Ordinal.Equals("Persistence.WriteSnapshotSolutionRelationships", timing.Stage));
            Assert.Contains(result.Diagnostics.Timings, timing => StringComparer.Ordinal.Equals("Persistence.WriteNodeEvidenceRelationships", timing.Stage));
            Assert.Contains(result.Diagnostics.Timings, timing => StringComparer.Ordinal.Equals("Persistence.WriteMetricEvidenceRelationships", timing.Stage));
            Assert.Contains(result.Diagnostics.Timings, timing => StringComparer.Ordinal.Equals("Persistence.WriteMetricTargetRelationships", timing.Stage));
            Assert.Contains(result.Diagnostics.Timings, timing => StringComparer.Ordinal.Equals("Persistence.Commit", timing.Stage));
            Assert.All(result.Diagnostics.Timings, timing => Assert.Equal(TimeSpan.Zero, timing.CompletedUtc.Offset));
            Assert.DoesNotContain(result.Diagnostics.Timings, timing => timing.Stage.Contains("MATCH", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(result.Warnings, warning => warning.Message.Contains("bolt", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Confirms metrics are persisted through bounded list-parameter batches while retaining diagnostic operation-count semantics.
        /// </summary>
        /// <returns>A task that completes after the forced metric batches and persisted metric properties have been asserted.</returns>
        [Fact]
        public async Task WriteSnapshotAsyncBatchesMetricPersistenceAndPreservesRepresentativeMetricValues()
        {
            // A batch size of two turns three metric records into two metric Cypher executions, proving operation count follows
            // statement executions instead of metric row count while still preserving numeric, text, node-target, edge-target, and
            // evidence values on the stored metric nodes.
            await using ServiceProvider serviceProvider = CreateServiceProvider(persistenceBatchSize: 2);
            IArchitectureGraphRecreator recreator = serviceProvider.GetRequiredService<IArchitectureGraphRecreator>();
            IArchitectureSnapshotWriter writer = serviceProvider.GetRequiredService<IArchitectureSnapshotWriter>();
            IDriver driver = serviceProvider.GetRequiredService<IDriver>();
            await recreator.RecreateGraphAsync(GraphRecreationRequest.CreateAuthorized("metric batching persistence test"));
            ExtractedArchitectureSnapshot snapshot = CreateMetricRichSnapshot("metric-batching");

            SnapshotPersistenceResult result = await writer.WriteSnapshotAsync(snapshot);
            MetricPropertySnapshot numericMetric = await ReadMetricPropertiesAsync(driver, "snapshot://metric-batching", "metric://metric-batching/numeric");
            MetricPropertySnapshot textMetric = await ReadMetricPropertiesAsync(driver, "snapshot://metric-batching", "metric://metric-batching/text");
            MetricPropertySnapshot mixedMetric = await ReadMetricPropertiesAsync(driver, "snapshot://metric-batching", "metric://metric-batching/mixed");

            Assert.True(result.Succeeded);
            Assert.Equal(3, result.Counts.Metrics);
            Assert.Equal(3, result.Counts.MetricEvidenceRelationships);
            Assert.Equal(2, result.Counts.MetricTargetRelationships);
            Assert.NotNull(result.Diagnostics);
            Assert.Equal(12, result.Diagnostics.Counts.PersistenceOperationCount);
            Assert.Equal(1, result.Diagnostics.Counts.PersistenceBatchCount);
            Assert.Contains(result.Diagnostics.Timings, timing => StringComparer.Ordinal.Equals("Persistence.WriteMetricEvidenceRelationships", timing.Stage));
            Assert.Contains(result.Diagnostics.Timings, timing => StringComparer.Ordinal.Equals("Persistence.WriteMetricTargetRelationships", timing.Stage));
            Assert.Equal("NodeCount", numericMetric.MetricKind);
            Assert.Equal("Snapshot", numericMetric.ScopeKind);
            Assert.Equal(7m, numericMetric.NumericValue);
            Assert.Null(numericMetric.TextValue);
            Assert.Equal("nodes", numericMetric.Unit);
            Assert.Equal("project://metric-batching", numericMetric.NodeStableKey);
            Assert.Null(numericMetric.EdgeStableKey);
            Assert.Equal("evidence://metric-batching", numericMetric.PrimaryEvidenceStableKey);
            Assert.Equal("sha256:metric-metric-batching-numeric", numericMetric.Fingerprint);
            Assert.Equal("RiskCategory", textMetric.MetricKind);
            Assert.Null(textMetric.NumericValue);
            Assert.Equal("Elevated", textMetric.TextValue);
            Assert.Null(textMetric.NodeStableKey);
            Assert.Null(textMetric.EdgeStableKey);
            Assert.Equal("RelationshipWeight", mixedMetric.MetricKind);
            Assert.Equal(2.5m, mixedMetric.NumericValue);
            Assert.Equal("two point five", mixedMetric.TextValue);
            Assert.Equal("edge://metric-batching/project-uses-package", mixedMetric.EdgeStableKey);
        }

        /// <summary>
        /// Confirms repeated metric-rich snapshot writes merge by stable key instead of creating duplicate metric nodes.
        /// </summary>
        /// <returns>A task that completes after two writes and graph-count verification have finished.</returns>
        [Fact]
        public async Task WriteSnapshotAsyncBatchedMetricsRemainIdempotentByStableKey()
        {
            // Writing the same metric-rich snapshot twice exercises the MERGE identity under the batched Cypher path without relying on
            // Neo4j internal IDs, which must stay hidden from application and API contracts.
            await using ServiceProvider serviceProvider = CreateServiceProvider(persistenceBatchSize: 2);
            IArchitectureGraphRecreator recreator = serviceProvider.GetRequiredService<IArchitectureGraphRecreator>();
            IArchitectureSnapshotWriter writer = serviceProvider.GetRequiredService<IArchitectureSnapshotWriter>();
            IDriver driver = serviceProvider.GetRequiredService<IDriver>();
            await recreator.RecreateGraphAsync(GraphRecreationRequest.CreateAuthorized("metric batching idempotency test"));
            ExtractedArchitectureSnapshot snapshot = CreateMetricRichSnapshot("metric-idempotent");

            SnapshotPersistenceResult first = await writer.WriteSnapshotAsync(snapshot);
            SnapshotPersistenceResult second = await writer.WriteSnapshotAsync(snapshot);
            GraphCounts counts = await ReadGraphCountsAsync(driver);

            Assert.True(first.Succeeded);
            Assert.True(second.Succeeded);
            Assert.Equal(3, counts.Metrics);
            Assert.Equal(3, counts.MetricEvidenceRelationships);
            Assert.Equal(2, counts.MetricTargetRelationships);
            Assert.Equal(12, second.Diagnostics?.Counts.PersistenceOperationCount);
        }

        /// <summary>
        /// Confirms snapshot-to-solution support relationships use bounded batches and remain idempotent under repeated writes.
        /// </summary>
        /// <returns>A task that completes after multi-solution relationship batching and graph-count assertions finish.</returns>
        [Fact]
        public async Task WriteSnapshotAsyncBatchesSnapshotSolutionRelationshipsAndPreservesIdempotency()
        {
            // Three solution rows with a batch size of two force the snapshot-to-solution relationship family to execute one full batch
            // and one final partial batch, proving this low-volume family follows the same bounded relationship path as evidence links.
            await using ServiceProvider serviceProvider = CreateServiceProvider(persistenceBatchSize: 2);
            IArchitectureGraphRecreator recreator = serviceProvider.GetRequiredService<IArchitectureGraphRecreator>();
            IArchitectureSnapshotWriter writer = serviceProvider.GetRequiredService<IArchitectureSnapshotWriter>();
            IDriver driver = serviceProvider.GetRequiredService<IDriver>();
            await recreator.RecreateGraphAsync(GraphRecreationRequest.CreateAuthorized("snapshot solution relationship batching test"));
            ExtractedArchitectureSnapshot snapshot = CreateMultiSolutionSnapshot("snapshot-solution-batching");

            SnapshotPersistenceResult first = await writer.WriteSnapshotAsync(snapshot);
            SnapshotPersistenceResult second = await writer.WriteSnapshotAsync(snapshot);
            GraphCounts counts = await ReadGraphCountsAsync(driver);

            Assert.True(first.Succeeded);
            Assert.True(second.Succeeded);
            Assert.Equal(3, second.Counts.Solutions);
            Assert.Equal(3, second.Counts.SnapshotSolutionRelationships);
            Assert.Equal(3, counts.Solutions);
            Assert.Equal(3, counts.SnapshotSolutionRelationships);
            Assert.Equal(12, second.Diagnostics?.Counts.PersistenceOperationCount);
            Assert.Contains(second.Diagnostics!.Timings, timing => StringComparer.Ordinal.Equals("Persistence.WriteSnapshotSolutionRelationships", timing.Stage));
        }

        /// <summary>
        /// Confirms architecture nodes and canonical evidence records are persisted through bounded batches while preserving normalized properties.
        /// </summary>
        /// <returns>A task that completes after node and evidence batching behavior has been asserted against Neo4j.</returns>
        [Fact]
        public async Task WriteSnapshotAsyncBatchesNodeAndEvidencePersistenceAndPreservesRepresentativeProperties()
        {
            // A batch size of two turns three node records and three canonical evidence records into two node executions and two evidence
            // executions. The assertion therefore proves operation count follows Cypher batch executions while stable-key lookups prove the
            // batched statements preserved nullable node/evidence properties and deterministic fingerprints.
            await using ServiceProvider serviceProvider = CreateServiceProvider(persistenceBatchSize: 2);
            IArchitectureGraphRecreator recreator = serviceProvider.GetRequiredService<IArchitectureGraphRecreator>();
            IArchitectureSnapshotWriter writer = serviceProvider.GetRequiredService<IArchitectureSnapshotWriter>();
            IDriver driver = serviceProvider.GetRequiredService<IDriver>();
            await recreator.RecreateGraphAsync(GraphRecreationRequest.CreateAuthorized("node evidence batching persistence test"));
            ExtractedArchitectureSnapshot snapshot = CreateNodeAndEvidenceRichSnapshot("node-evidence-batching");

            SnapshotPersistenceResult result = await writer.WriteSnapshotAsync(snapshot);
            NodePropertySnapshot projectNode = await ReadNodePropertiesAsync(driver, "snapshot://node-evidence-batching", "project://node-evidence-batching/app");
            NodePropertySnapshot packageNode = await ReadNodePropertiesAsync(driver, "snapshot://node-evidence-batching", "package://node-evidence-batching/neo4j");
            EvidencePropertySnapshot nullableEvidence = await ReadEvidencePropertiesAsync(driver, "snapshot://node-evidence-batching", "evidence://node-evidence-batching/nullable");

            Assert.True(result.Succeeded);
            Assert.Equal(3, result.Counts.Nodes);
            Assert.Equal(3, result.Counts.Evidence);
            Assert.NotNull(result.Diagnostics);
            Assert.Equal(10, result.Diagnostics.Counts.PersistenceOperationCount);
            Assert.Equal(1, result.Diagnostics.Counts.PersistenceBatchCount);
            Assert.Contains(result.Diagnostics.Timings, timing => StringComparer.Ordinal.Equals("Persistence.WriteNodeEvidenceRelationships", timing.Stage));
            Assert.Equal("Project", projectNode.NodeKind);
            Assert.Equal("Application Project", projectNode.DisplayName);
            Assert.Equal("Application.Project", projectNode.QualifiedName);
            Assert.Equal("application project", projectNode.SearchName);
            Assert.Equal("C#", projectNode.Language);
            Assert.Null(projectNode.ProjectStableKey);
            Assert.Null(projectNode.ParentNodeStableKey);
            Assert.Equal("Architecture", projectNode.Ownership);
            Assert.Equal("Internal", projectNode.ExternalCategory);
            Assert.Equal("evidence://node-evidence-batching/project", projectNode.PrimaryEvidenceStableKey);
            Assert.Equal("sha256:node-node-evidence-batching-project", projectNode.Fingerprint);
            Assert.Equal("Package", packageNode.NodeKind);
            Assert.Equal("project://node-evidence-batching/app", packageNode.ProjectStableKey);
            Assert.Equal("project://node-evidence-batching/app", packageNode.ParentNodeStableKey);
            Assert.Equal("ThirdParty", packageNode.ExternalCategory);
            Assert.Equal("SourceCode", nullableEvidence.EvidenceKind);
            Assert.Equal("src/node-evidence-batching/Nullable.cs", nullableEvidence.FilePath);
            Assert.Null(nullableEvidence.StartLine);
            Assert.Null(nullableEvidence.EndLine);
            Assert.Null(nullableEvidence.SymbolName);
            Assert.Null(nullableEvidence.ContainingSymbol);
            Assert.Equal("sha256:evidence-node-evidence-batching-nullable", nullableEvidence.Fingerprint);
        }

        /// <summary>
        /// Confirms repeated node-and-evidence-rich writes merge by stable key instead of duplicating records.
        /// </summary>
        /// <returns>A task that completes after repeated writes and graph counts have been verified.</returns>
        [Fact]
        public async Task WriteSnapshotAsyncBatchedNodesAndEvidenceRemainIdempotentByStableKey()
        {
            // Writing the same node/evidence rich snapshot twice exercises both new MERGE batch identities: snapshot plus node stable key
            // for ArchonNode records and snapshot plus canonical evidence stable key for ArchonEvidence records.
            await using ServiceProvider serviceProvider = CreateServiceProvider(persistenceBatchSize: 2);
            IArchitectureGraphRecreator recreator = serviceProvider.GetRequiredService<IArchitectureGraphRecreator>();
            IArchitectureSnapshotWriter writer = serviceProvider.GetRequiredService<IArchitectureSnapshotWriter>();
            IDriver driver = serviceProvider.GetRequiredService<IDriver>();
            await recreator.RecreateGraphAsync(GraphRecreationRequest.CreateAuthorized("node evidence batching idempotency test"));
            ExtractedArchitectureSnapshot snapshot = CreateNodeAndEvidenceRichSnapshot("node-evidence-idempotent");

            SnapshotPersistenceResult first = await writer.WriteSnapshotAsync(snapshot);
            SnapshotPersistenceResult second = await writer.WriteSnapshotAsync(snapshot);
            GraphCounts counts = await ReadGraphCountsAsync(driver);

            Assert.True(first.Succeeded);
            Assert.True(second.Succeeded);
            Assert.Equal(3, counts.Nodes);
            Assert.Equal(3, counts.Evidence);
            Assert.Equal(3, counts.NodeEvidenceRelationships);
            Assert.Equal(10, second.Diagnostics?.Counts.PersistenceOperationCount);
        }

        /// <summary>
        /// Confirms canonical evidence remapping still occurs before batched evidence persistence and relationship creation.
        /// </summary>
        /// <returns>A task that completes after duplicate evidence inputs have been written and queried.</returns>
        [Fact]
        public async Task WriteSnapshotAsyncBatchedEvidenceUsesCanonicalDeduplicationBeforeWrites()
        {
            // Duplicate evidence inputs use distinct stable keys but identical payloads. The batched evidence write should persist only the
            // first canonical evidence node, and relationship creation should remap both node support links to that canonical stable key.
            await using ServiceProvider serviceProvider = CreateServiceProvider(persistenceBatchSize: 2);
            IArchitectureGraphRecreator recreator = serviceProvider.GetRequiredService<IArchitectureGraphRecreator>();
            IArchitectureSnapshotWriter writer = serviceProvider.GetRequiredService<IArchitectureSnapshotWriter>();
            IDriver driver = serviceProvider.GetRequiredService<IDriver>();
            await recreator.RecreateGraphAsync(GraphRecreationRequest.CreateAuthorized("batched evidence deduplication test"));
            ExtractedArchitectureSnapshot snapshot = CreateMinimalSnapshot("batched-dedupe", duplicateEvidence: true);

            SnapshotPersistenceResult result = await writer.WriteSnapshotAsync(snapshot);
            GraphCounts counts = await ReadGraphCountsAsync(driver);
            IReadOnlyList<string> evidenceStableKeys = await ReadEvidenceStableKeysAsync(driver, "snapshot://batched-dedupe");

            Assert.True(result.Succeeded);
            Assert.Equal(2, result.Counts.Nodes);
            Assert.Equal(1, result.Counts.Evidence);
            Assert.Equal(2, result.Counts.NodeEvidenceRelationships);
            Assert.Equal(1, counts.Evidence);
            Assert.Equal(2, counts.NodeEvidenceRelationships);
            Assert.Equal(new[] { "evidence://batched-dedupe/first" }, evidenceStableKeys);
            Assert.Equal(10, result.Diagnostics?.Counts.PersistenceOperationCount);
        }

        /// <summary>
        /// Confirms validation failures still return partial, safe diagnostics without pretending persistence completed.
        /// </summary>
        /// <returns>A task that completes after failed persistence diagnostics have been asserted.</returns>
        [Fact]
        public async Task WriteSnapshotAsyncReturnsPartialDiagnosticsForValidationFailure()
        {
            // Validation happens before Neo4j statements are executed, but the total and preparation timings should still explain the attempt.
            await using ServiceProvider serviceProvider = CreateServiceProvider();
            IArchitectureGraphRecreator recreator = serviceProvider.GetRequiredService<IArchitectureGraphRecreator>();
            IArchitectureSnapshotWriter writer = serviceProvider.GetRequiredService<IArchitectureSnapshotWriter>();
            await recreator.RecreateGraphAsync(GraphRecreationRequest.CreateAuthorized("persistence diagnostics failure test"));
            ExtractedArchitectureSnapshot snapshot = CreateSnapshotWithMissingEvidence("diagnostics-failure");

            SnapshotPersistenceResult result = await writer.WriteSnapshotAsync(snapshot);

            Assert.False(result.Succeeded);
            Assert.NotNull(result.Diagnostics);
            Assert.False(result.Diagnostics.Completed);
            Assert.Equal(1, result.Diagnostics.Counts.RepositoryCount);
            Assert.Equal(1, result.Diagnostics.Counts.SolutionCount);
            Assert.Equal(1, result.Diagnostics.Counts.ProjectCount);
            Assert.Equal(0, result.Diagnostics.Counts.FileCount);
            Assert.Equal(1, result.Diagnostics.Counts.NodeCount);
            Assert.Equal(0, result.Diagnostics.Counts.EvidenceCount);
            Assert.Equal(1, result.Diagnostics.Counts.ErrorCount);
            Assert.Null(result.Diagnostics.Counts.PersistenceOperationCount);
            Assert.Contains(result.Diagnostics.Timings, timing => StringComparer.Ordinal.Equals("Persistence.PrepareSnapshot", timing.Stage));
            Assert.Contains(result.Diagnostics.Timings, timing => StringComparer.Ordinal.Equals("Persistence.Total", timing.Stage));
            Assert.DoesNotContain(result.Diagnostics.Timings, timing => StringComparer.Ordinal.Equals("Persistence.WriteNodes", timing.Stage));
            Assert.DoesNotContain(result.Errors, error => error.Message.Contains("MATCH", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Confirms early Neo4j initialization failures return partial diagnostics without exposing database endpoint or driver details.
        /// </summary>
        /// <returns>A task that completes after initialization failure diagnostics have been asserted.</returns>
        [Fact]
        public async Task WriteSnapshotAsync_WhenSchemaInitializationFails_ShouldReturnSafePartialDiagnosticsWithoutWriteStages()
        {
            // The graph recreation step is intentionally skipped so the closed driver produces an initialization failure before any write transaction starts.
            await using ServiceProvider serviceProvider = CreateServiceProvider();
            IArchitectureSnapshotWriter writer = serviceProvider.GetRequiredService<IArchitectureSnapshotWriter>();
            IDriver driver = serviceProvider.GetRequiredService<IDriver>();
            await driver.DisposeAsync();
            ExtractedArchitectureSnapshot snapshot = CreateMinimalSnapshot("diagnostics-initialization-failure", duplicateEvidence: false);

            SnapshotPersistenceResult result = await writer.WriteSnapshotAsync(snapshot);

            Assert.False(result.Succeeded);
            Assert.NotNull(result.Diagnostics);
            Assert.False(result.Diagnostics.Completed);
            Assert.Equal(1, result.Diagnostics.Counts.ErrorCount);
            Assert.Null(result.Diagnostics.Counts.PersistenceOperationCount);
            Assert.Contains(result.Diagnostics.Timings, timing => timing.Stage == "Persistence.PrepareSnapshot");
            Assert.Contains(result.Diagnostics.Timings, timing => timing.Stage == "Persistence.Indexing");
            Assert.Contains(result.Diagnostics.Timings, timing => timing.Stage == "Persistence.Total");
            Assert.DoesNotContain(result.Diagnostics.Timings, timing => timing.Stage == "Persistence.WriteNodes");
            Assert.DoesNotContain(result.Diagnostics.Timings, timing => timing.Stage.Contains("bolt", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(result.Errors, error => error.Message.Contains("bolt", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(result.Errors, error => error.Message.Contains("Neo4j.Driver", StringComparison.OrdinalIgnoreCase));
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
        /// Confirms missing metric node targets are rejected before batched relationship matching can silently drop endpoint rows.
        /// </summary>
        /// <returns>A task that completes after the controlled validation failure has been asserted.</returns>
        [Fact]
        public async Task WriteSnapshotAsyncReturnsErrorForMissingMetricNodeReference()
        {
            // Metric target validation mirrors primary evidence validation so invalid endpoint payloads fail before the write transaction starts.
            await using ServiceProvider serviceProvider = CreateServiceProvider();
            IArchitectureGraphRecreator recreator = serviceProvider.GetRequiredService<IArchitectureGraphRecreator>();
            IArchitectureSnapshotWriter writer = serviceProvider.GetRequiredService<IArchitectureSnapshotWriter>();
            IDriver driver = serviceProvider.GetRequiredService<IDriver>();
            await recreator.RecreateGraphAsync(GraphRecreationRequest.CreateAuthorized("missing metric node target test"));
            ExtractedArchitectureSnapshot snapshot = CreateSnapshotWithMissingMetricNodeTarget("missing-metric-node");

            SnapshotPersistenceResult result = await writer.WriteSnapshotAsync(snapshot);
            GraphCounts counts = await ReadGraphCountsAsync(driver);

            Assert.False(result.Succeeded);
            Assert.Equal("MissingMetricNodeReference", Assert.Single(result.Errors).Code);
            Assert.Equal(0, counts.Snapshots);
            Assert.Equal(0, counts.Nodes);
            Assert.Equal(0, counts.Metrics);
            Assert.DoesNotContain(result.Errors, error => error.Message.Contains("MATCH", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Creates a service provider using production Neo4j infrastructure registrations and container-derived configuration.
        /// </summary>
        /// <returns>A service provider ready to resolve Neo4j infrastructure services for integration tests.</returns>
        private ServiceProvider CreateServiceProvider(int? persistenceBatchSize = null)
        {
            // The provider mirrors host composition while avoiding the Aspire AppHost, which must not run during automated validation.
            ServiceCollection services = new();
            services.AddLogging();
            services.AddArchonNeo4j(CreateNeo4jConfiguration(persistenceBatchSize));
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
            MetricRecord metric = CreateMetric(snapshotStableKey, new StableKey($"metric://{suffix}/SnapshotNodeCount/Snapshot"), firstNode.StableKey, firstEvidenceStableKey, suffix, duplicateEvidence ? 2 : 1);

            List<ArchitectureNode> nodes = [firstNode];
            List<EvidenceRecord> evidence = [firstEvidence];
            if (duplicateEvidence)
            {
                evidence.Add(CreateEvidence(snapshotStableKey, secondEvidenceStableKey, suffix));
                nodes.Add(CreateNode(snapshotStableKey, new StableKey($"project://{suffix}/second"), secondEvidenceStableKey, suffix, "Second Project"));
            }

            return new ExtractedArchitectureSnapshot(header, new[] { repository }, new[] { solution }, nodes, Array.Empty<ArchitectureEdge>(), evidence, Array.Empty<RuleDefinition>(), Array.Empty<FindingRecord>(), new[] { metric }, Array.Empty<GeneratedSummary>(), Array.Empty<string>(), Array.Empty<string>());
        }

        /// <summary>
        /// Creates a snapshot with three representative metric value shapes for batching validation.
        /// </summary>
        /// <param name="suffix">The unique suffix used to isolate stable keys for the test.</param>
        /// <returns>An extracted architecture snapshot with numeric-only, text-only, and mixed metric records.</returns>
        private static ExtractedArchitectureSnapshot CreateMetricRichSnapshot(string suffix)
        {
            // The fixture stays intentionally small but uses three metrics so a batch size of two proves final partial-batch execution.
            StableKey repositoryStableKey = new($"repository://{suffix}");
            StableKey solutionStableKey = new($"solution://{suffix}");
            StableKey snapshotStableKey = new($"snapshot://{suffix}");
            StableKey evidenceStableKey = new($"evidence://{suffix}");
            StableKey projectNodeStableKey = new($"project://{suffix}");
            StableKey packageNodeStableKey = new($"package://{suffix}/neo4j");
            StableKey relationshipStableKey = new($"edge://{suffix}/project-uses-package");

            RepositoryModel repository = CreateRepository(repositoryStableKey, suffix);
            SolutionModel solution = CreateSolution(repositoryStableKey, solutionStableKey, suffix);
            SnapshotHeader header = CreateHeader(repositoryStableKey, snapshotStableKey, suffix);
            EvidenceRecord evidence = CreateEvidence(snapshotStableKey, evidenceStableKey, suffix);
            ArchitectureNode projectNode = CreateNode(snapshotStableKey, projectNodeStableKey, evidenceStableKey, suffix, "Project");
            ArchitectureNode packageNode = CreateNode(snapshotStableKey, packageNodeStableKey, evidenceStableKey, suffix, "Neo4j Package");
            MetricRecord numericMetric = CreateMetricWithValues(snapshotStableKey, new StableKey($"metric://{suffix}/numeric"), "NodeCount", MetricScopeKind.Snapshot, projectNodeStableKey, null, evidenceStableKey, "Snapshot node count", 7m, null, "nodes", suffix, "numeric");
            MetricRecord textMetric = CreateMetricWithValues(snapshotStableKey, new StableKey($"metric://{suffix}/text"), "RiskCategory", MetricScopeKind.Snapshot, null, null, evidenceStableKey, "Risk category", null, "Elevated", null, suffix, "text");
            MetricRecord mixedMetric = CreateMetricWithValues(snapshotStableKey, new StableKey($"metric://{suffix}/mixed"), "RelationshipWeight", MetricScopeKind.Edge, projectNodeStableKey, relationshipStableKey, evidenceStableKey, "Relationship weight", 2.5m, "two point five", "weight", suffix, "mixed");

            return new ExtractedArchitectureSnapshot(
                header,
                new[] { repository },
                new[] { solution },
                new[] { projectNode, packageNode },
                Array.Empty<ArchitectureEdge>(),
                new[] { evidence },
                Array.Empty<RuleDefinition>(),
                Array.Empty<FindingRecord>(),
                new[] { numericMetric, textMetric, mixedMetric },
                Array.Empty<GeneratedSummary>(),
                Array.Empty<string>(),
                Array.Empty<string>());
        }

        /// <summary>
        /// Creates a snapshot with three nodes and three distinct evidence records for node/evidence batching validation.
        /// </summary>
        /// <param name="suffix">The unique suffix used to isolate stable keys for the test.</param>
        /// <returns>An extracted architecture snapshot with representative nullable and non-null node/evidence properties.</returns>
        private static ExtractedArchitectureSnapshot CreateNodeAndEvidenceRichSnapshot(string suffix)
        {
            // Three records in each section force a final partial batch with batch size two while keeping the graph small enough for clear
            // stable-key based assertions.
            StableKey repositoryStableKey = new($"repository://{suffix}");
            StableKey solutionStableKey = new($"solution://{suffix}");
            StableKey snapshotStableKey = new($"snapshot://{suffix}");
            StableKey projectEvidenceStableKey = new($"evidence://{suffix}/project");
            StableKey packageEvidenceStableKey = new($"evidence://{suffix}/package");
            StableKey nullableEvidenceStableKey = new($"evidence://{suffix}/nullable");
            StableKey projectNodeStableKey = new($"project://{suffix}/app");
            StableKey packageNodeStableKey = new($"package://{suffix}/neo4j");
            StableKey fileNodeStableKey = new($"file://{suffix}/nullable");

            RepositoryModel repository = CreateRepository(repositoryStableKey, suffix);
            SolutionModel solution = CreateSolution(repositoryStableKey, solutionStableKey, suffix);
            SnapshotHeader header = CreateHeader(repositoryStableKey, snapshotStableKey, suffix);
            EvidenceRecord projectEvidence = CreateEvidenceWithValues(snapshotStableKey, projectEvidenceStableKey, suffix, "project", $"src/{suffix}/App.csproj", 1, 8, "Application.Project", "Application", "project-snippet", "<Project />");
            EvidenceRecord packageEvidence = CreateEvidenceWithValues(snapshotStableKey, packageEvidenceStableKey, suffix, "package", $"src/{suffix}/App.csproj", 9, 9, "Neo4j.Driver", "Application.Project", "package-snippet", "<PackageReference Include=\"Neo4j.Driver\" />");
            EvidenceRecord nullableEvidence = CreateEvidenceWithValues(snapshotStableKey, nullableEvidenceStableKey, suffix, "nullable", $"src/{suffix}/Nullable.cs", null, null, null, null, null, null);
            ArchitectureNode projectNode = CreateNodeWithValues(snapshotStableKey, projectNodeStableKey, projectEvidenceStableKey, NodeKind.Project, "Application Project", "Application.Project", "application project", "C#", null, null, "Architecture", "Internal", suffix, "project");
            ArchitectureNode packageNode = CreateNodeWithValues(snapshotStableKey, packageNodeStableKey, packageEvidenceStableKey, NodeKind.Package, "Neo4j Package", "Neo4j.Driver", "neo4j.driver", null, projectNodeStableKey, projectNodeStableKey, "Vendor", "ThirdParty", suffix, "package");
            ArchitectureNode fileNode = CreateNodeWithValues(snapshotStableKey, fileNodeStableKey, nullableEvidenceStableKey, NodeKind.FilePath, "Nullable.cs", "Nullable", "nullable", "C#", projectNodeStableKey, null, null, null, suffix, "file");

            return new ExtractedArchitectureSnapshot(
                header,
                new[] { repository },
                new[] { solution },
                new[] { projectNode, packageNode, fileNode },
                Array.Empty<ArchitectureEdge>(),
                new[] { projectEvidence, packageEvidence, nullableEvidence },
                Array.Empty<RuleDefinition>(),
                Array.Empty<FindingRecord>(),
                Array.Empty<MetricRecord>(),
                Array.Empty<GeneratedSummary>(),
                Array.Empty<string>(),
                Array.Empty<string>());
        }

        /// <summary>
        /// Creates a snapshot with three solutions so snapshot-to-solution relationships can exercise exact and partial batches.
        /// </summary>
        /// <param name="suffix">The unique suffix used to isolate stable keys for the test.</param>
        /// <returns>An extracted architecture snapshot with three solution records and one representative metric support chain.</returns>
        private static ExtractedArchitectureSnapshot CreateMultiSolutionSnapshot(string suffix)
        {
            // The fixture keeps node, evidence, and metric volume minimal so the operation-count assertion can isolate the extra solution
            // and snapshot-to-solution relationship batch executions introduced by the multi-solution shape.
            StableKey repositoryStableKey = new($"repository://{suffix}");
            StableKey snapshotStableKey = new($"snapshot://{suffix}");
            StableKey evidenceStableKey = new($"evidence://{suffix}");
            ArchitectureNode node = CreateNode(snapshotStableKey, new StableKey($"project://{suffix}"), evidenceStableKey, suffix, "Project");

            return new ExtractedArchitectureSnapshot(
                CreateHeader(repositoryStableKey, snapshotStableKey, suffix),
                new[] { CreateRepository(repositoryStableKey, suffix) },
                new[]
                {
                    CreateSolution(repositoryStableKey, new StableKey($"solution://{suffix}/one"), suffix),
                    CreateSolution(repositoryStableKey, new StableKey($"solution://{suffix}/two"), suffix),
                    CreateSolution(repositoryStableKey, new StableKey($"solution://{suffix}/three"), suffix)
                },
                new[] { node },
                Array.Empty<ArchitectureEdge>(),
                new[] { CreateEvidence(snapshotStableKey, evidenceStableKey, suffix) },
                Array.Empty<RuleDefinition>(),
                Array.Empty<FindingRecord>(),
                new[] { CreateMetric(snapshotStableKey, new StableKey($"metric://{suffix}/node-count"), node.StableKey, evidenceStableKey, suffix, 1) },
                Array.Empty<GeneratedSummary>(),
                Array.Empty<string>(),
                Array.Empty<string>());
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
        /// Creates a snapshot whose metric targets an architecture node that is not supplied.
        /// </summary>
        /// <param name="suffix">The unique suffix used to isolate stable keys for the test.</param>
        /// <returns>An invalid extracted snapshot for metric target validation testing.</returns>
        private static ExtractedArchitectureSnapshot CreateSnapshotWithMissingMetricNodeTarget(string suffix)
        {
            // The metric's evidence reference is valid, but its node target is absent so validation can prove missing relationship endpoints
            // do not become silent no-op rows in batched Cypher.
            StableKey repositoryStableKey = new($"repository://{suffix}");
            StableKey solutionStableKey = new($"solution://{suffix}");
            StableKey snapshotStableKey = new($"snapshot://{suffix}");
            StableKey evidenceStableKey = new($"evidence://{suffix}");
            RepositoryModel repository = CreateRepository(repositoryStableKey, suffix);
            SolutionModel solution = CreateSolution(repositoryStableKey, solutionStableKey, suffix);
            SnapshotHeader header = CreateHeader(repositoryStableKey, snapshotStableKey, suffix);
            EvidenceRecord evidence = CreateEvidence(snapshotStableKey, evidenceStableKey, suffix);
            MetricRecord metric = CreateMetric(snapshotStableKey, new StableKey($"metric://{suffix}/missing-node"), new StableKey($"project://{suffix}/missing"), evidenceStableKey, suffix, 1);

            return new ExtractedArchitectureSnapshot(header, new[] { repository }, new[] { solution }, Array.Empty<ArchitectureNode>(), Array.Empty<ArchitectureEdge>(), new[] { evidence }, Array.Empty<RuleDefinition>(), Array.Empty<FindingRecord>(), new[] { metric }, Array.Empty<GeneratedSummary>(), Array.Empty<string>(), Array.Empty<string>());
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
        /// Creates an architecture node with caller-controlled nullable and classification properties for batching assertions.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that scopes the node.</param>
        /// <param name="stableKey">The stable key that identifies the node.</param>
        /// <param name="evidenceStableKey">The primary evidence stable key referenced by the node.</param>
        /// <param name="nodeKind">The controlled node kind persisted as a normalized property.</param>
        /// <param name="displayName">The display name persisted for developer-facing graph queries.</param>
        /// <param name="qualifiedName">The optional qualified name persisted for symbol-oriented graph queries.</param>
        /// <param name="searchName">The optional normalized search name persisted for lookup scenarios.</param>
        /// <param name="language">The optional programming or artifact language persisted for filtering.</param>
        /// <param name="projectStableKey">The optional owning project stable key persisted on the node.</param>
        /// <param name="parentNodeStableKey">The optional parent node stable key persisted on the node.</param>
        /// <param name="ownership">The optional ownership classification persisted on the node.</param>
        /// <param name="externalCategory">The optional external category persisted on the node.</param>
        /// <param name="suffix">The unique suffix used in fingerprints.</param>
        /// <param name="fingerprintSuffix">The unique suffix used to distinguish node fingerprints.</param>
        /// <returns>An architecture node with representative nullable and non-null properties.</returns>
        private static ArchitectureNode CreateNodeWithValues(
            StableKey snapshotStableKey,
            StableKey stableKey,
            StableKey evidenceStableKey,
            NodeKind nodeKind,
            string displayName,
            string? qualifiedName,
            string? searchName,
            string? language,
            StableKey? projectStableKey,
            StableKey? parentNodeStableKey,
            string? ownership,
            string? externalCategory,
            string suffix,
            string fingerprintSuffix)
        {
            // The node is intentionally richer than the minimal fixture so batch Cypher must preserve nullable target and classification fields.
            return new ArchitectureNode(
                snapshotStableKey,
                stableKey,
                nodeKind,
                displayName,
                qualifiedName,
                searchName,
                language,
                projectStableKey,
                parentNodeStableKey,
                KnowledgeKind.Fact,
                ownership,
                externalCategory,
                Confidence.Certain,
                UnknownState.Known,
                evidenceStableKey,
                GraphMetadata.Empty,
                new Fingerprint($"sha256:node-{suffix}-{fingerprintSuffix}"));
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
        /// Creates an evidence record with caller-controlled nullable source-location and symbol properties.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that scopes the evidence.</param>
        /// <param name="stableKey">The stable key that identifies the evidence.</param>
        /// <param name="suffix">The unique suffix used in persisted path content and fingerprints.</param>
        /// <param name="fingerprintSuffix">The unique suffix used to distinguish evidence fingerprints.</param>
        /// <param name="path">The repository-relative file path persisted on the evidence.</param>
        /// <param name="startLine">The optional starting source line persisted on the evidence.</param>
        /// <param name="endLine">The optional ending source line persisted on the evidence.</param>
        /// <param name="symbolName">The optional source symbol persisted on the evidence.</param>
        /// <param name="containingSymbol">The optional containing source symbol persisted on the evidence.</param>
        /// <param name="snippetHash">The optional snippet hash persisted on the evidence.</param>
        /// <param name="snippetPreview">The optional source preview persisted on the evidence.</param>
        /// <returns>An evidence record with representative nullable and non-null properties.</returns>
        private static EvidenceRecord CreateEvidenceWithValues(
            StableKey snapshotStableKey,
            StableKey stableKey,
            string suffix,
            string fingerprintSuffix,
            string path,
            int? startLine,
            int? endLine,
            string? symbolName,
            string? containingSymbol,
            string? snippetHash,
            string? snippetPreview)
        {
            // The evidence is used to prove the batched statement preserves both populated source spans and nullable source context.
            return new EvidenceRecord(
                snapshotStableKey,
                stableKey,
                EvidenceKind.SourceCode,
                RepositoryRelativePath.Parse(path),
                startLine,
                endLine,
                symbolName,
                containingSymbol,
                snippetHash,
                snippetPreview,
                KnowledgeKind.Fact,
                Confidence.Certain,
                UnknownState.Known,
                GraphMetadata.Empty,
                new Fingerprint($"sha256:evidence-{suffix}-{fingerprintSuffix}"));
        }

        /// <summary>
        /// Creates a snapshot-owned metric for a persistence test snapshot.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that scopes the metric.</param>
        /// <param name="stableKey">The stable key that identifies the metric.</param>
        /// <param name="nodeStableKey">The node stable key targeted by the metric.</param>
        /// <param name="evidenceStableKey">The primary evidence stable key explaining the metric.</param>
        /// <param name="suffix">The unique suffix used in fingerprints.</param>
        /// <param name="nodeCount">The node count value represented by the metric.</param>
        /// <returns>A metric record suitable for persistence validation.</returns>
        private static MetricRecord CreateMetric(StableKey snapshotStableKey, StableKey stableKey, StableKey nodeStableKey, StableKey evidenceStableKey, string suffix, decimal nodeCount)
        {
            // The metric targets the first node so persistence can validate metric-to-evidence and metric-to-target relationships.
            return new MetricRecord(
                snapshotStableKey,
                stableKey,
                "SnapshotNodeCount",
                MetricScopeKind.Snapshot,
                nodeStableKey,
                edgeStableKey: null,
                evidenceStableKey,
                "Snapshot node count",
                nodeCount,
                textValue: null,
                "nodes",
                Confidence.Certain,
                UnknownState.Known,
                GraphMetadata.Empty,
                new Fingerprint($"sha256:metric-{suffix}"));
        }

        /// <summary>
        /// Creates a metric with caller-controlled target and value shapes for representative batching assertions.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that scopes the metric.</param>
        /// <param name="stableKey">The stable key that identifies the metric.</param>
        /// <param name="metricKind">The metric kind stored as a first-class graph property.</param>
        /// <param name="scopeKind">The controlled scope kind for the metric.</param>
        /// <param name="nodeStableKey">The optional node target stable key.</param>
        /// <param name="edgeStableKey">The optional relationship target stable key.</param>
        /// <param name="evidenceStableKey">The primary evidence stable key explaining the metric.</param>
        /// <param name="name">The developer-facing metric name.</param>
        /// <param name="numericValue">The optional numeric value to persist.</param>
        /// <param name="textValue">The optional text value to persist.</param>
        /// <param name="unit">The optional value unit to persist.</param>
        /// <param name="suffix">The unique suffix used in fingerprints.</param>
        /// <param name="fingerprintSuffix">The unique suffix used to distinguish metric fingerprints.</param>
        /// <returns>A metric record suitable for batched persistence validation.</returns>
        private static MetricRecord CreateMetricWithValues(
            StableKey snapshotStableKey,
            StableKey stableKey,
            string metricKind,
            MetricScopeKind scopeKind,
            StableKey? nodeStableKey,
            StableKey? edgeStableKey,
            StableKey evidenceStableKey,
            string name,
            decimal? numericValue,
            string? textValue,
            string? unit,
            string suffix,
            string fingerprintSuffix)
        {
            // Representative value combinations ensure the batched parameter shape does not accidentally drop nullable target or value fields.
            return new MetricRecord(
                snapshotStableKey,
                stableKey,
                metricKind,
                scopeKind,
                nodeStableKey,
                edgeStableKey,
                evidenceStableKey,
                name,
                numericValue,
                textValue,
                unit,
                Confidence.Certain,
                UnknownState.Known,
                GraphMetadata.Empty,
                new Fingerprint($"sha256:metric-{suffix}-{fingerprintSuffix}"));
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
CALL { MATCH (metric:ArchonMetric) RETURN count(metric) AS metrics }
CALL { MATCH (:ArchonSnapshot)-[includes:INCLUDES_SOLUTION]->(:ArchonSolution) RETURN count(includes) AS snapshotSolutionRelationships }
CALL { MATCH (:ArchonNode)-[supported:SUPPORTED_BY_EVIDENCE]->(:ArchonEvidence) RETURN count(supported) AS nodeEvidenceRelationships }
CALL { MATCH (:ArchonMetric)-[supported:SUPPORTED_BY_EVIDENCE]->(:ArchonEvidence) RETURN count(supported) AS metricEvidenceRelationships }
CALL { MATCH (:ArchonMetric)-[measures:MEASURES_NODE]->(:ArchonNode) RETURN count(measures) AS metricTargetRelationships }
RETURN repositories, solutions, snapshots, nodes, evidence, metrics, snapshotSolutionRelationships, nodeEvidenceRelationships, metricEvidenceRelationships, metricTargetRelationships");
            IRecord record = await cursor.SingleAsync();
            return new GraphCounts(
                record["repositories"].As<long>(),
                record["solutions"].As<long>(),
                record["snapshots"].As<long>(),
                record["nodes"].As<long>(),
                record["evidence"].As<long>(),
                record["metrics"].As<long>(),
                record["snapshotSolutionRelationships"].As<long>(),
                record["nodeEvidenceRelationships"].As<long>(),
                record["metricEvidenceRelationships"].As<long>(),
                record["metricTargetRelationships"].As<long>());
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
        /// Reads normalized architecture node properties by stable key without exposing Neo4j internal identifiers.
        /// </summary>
        /// <param name="driver">The configured Neo4j driver used to query the database.</param>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the node.</param>
        /// <param name="nodeStableKey">The stable key of the node to look up.</param>
        /// <returns>The normalized node properties required by batching assertions.</returns>
        private static async Task<NodePropertySnapshot> ReadNodePropertiesAsync(IDriver driver, string snapshotStableKey, string nodeStableKey)
        {
            // The query returns only stable public properties so tests do not normalize around Neo4j-local node IDs.
            await using IAsyncSession session = driver.AsyncSession(sessionBuilder => sessionBuilder.WithDatabase("neo4j"));
            IResultCursor cursor = await session.RunAsync(
                @"MATCH (node:ArchonNode { snapshotStableKey: $snapshotStableKey, stableKey: $nodeStableKey })
RETURN node.nodeKind AS nodeKind,
       node.displayName AS displayName,
       node.qualifiedName AS qualifiedName,
       node.searchName AS searchName,
       node.language AS language,
       node.projectStableKey AS projectStableKey,
       node.parentNodeStableKey AS parentNodeStableKey,
       node.ownership AS ownership,
       node.externalCategory AS externalCategory,
       node.primaryEvidenceStableKey AS primaryEvidenceStableKey,
       node.fingerprint AS fingerprint",
                new { snapshotStableKey, nodeStableKey });
            IRecord record = await cursor.SingleAsync();

            return new NodePropertySnapshot(
                record["nodeKind"].As<string>(),
                record["displayName"].As<string>(),
                record["qualifiedName"].As<string?>(),
                record["searchName"].As<string?>(),
                record["language"].As<string?>(),
                record["projectStableKey"].As<string?>(),
                record["parentNodeStableKey"].As<string?>(),
                record["ownership"].As<string?>(),
                record["externalCategory"].As<string?>(),
                record["primaryEvidenceStableKey"].As<string?>(),
                record["fingerprint"].As<string>());
        }

        /// <summary>
        /// Reads normalized evidence properties by stable key without exposing Neo4j internal identifiers.
        /// </summary>
        /// <param name="driver">The configured Neo4j driver used to query the database.</param>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the evidence.</param>
        /// <param name="evidenceStableKey">The stable key of the evidence to look up.</param>
        /// <returns>The normalized evidence properties required by batching assertions.</returns>
        private static async Task<EvidencePropertySnapshot> ReadEvidencePropertiesAsync(IDriver driver, string snapshotStableKey, string evidenceStableKey)
        {
            // Stable-key lookup proves the evidence row was merged through canonical public identity rather than through a database-local ID.
            await using IAsyncSession session = driver.AsyncSession(sessionBuilder => sessionBuilder.WithDatabase("neo4j"));
            IResultCursor cursor = await session.RunAsync(
                @"MATCH (evidence:ArchonEvidence { snapshotStableKey: $snapshotStableKey, stableKey: $evidenceStableKey })
RETURN evidence.evidenceKind AS evidenceKind,
       evidence.filePath AS filePath,
       evidence.startLine AS startLine,
       evidence.endLine AS endLine,
       evidence.symbolName AS symbolName,
       evidence.containingSymbol AS containingSymbol,
       evidence.fingerprint AS fingerprint",
                new { snapshotStableKey, evidenceStableKey });
            IRecord record = await cursor.SingleAsync();

            return new EvidencePropertySnapshot(
                record["evidenceKind"].As<string>(),
                record["filePath"].As<string>(),
                record["startLine"].As<int?>(),
                record["endLine"].As<int?>(),
                record["symbolName"].As<string?>(),
                record["containingSymbol"].As<string?>(),
                record["fingerprint"].As<string>());
        }

        /// <summary>
        /// Reads persisted evidence stable keys for one snapshot in deterministic order.
        /// </summary>
        /// <param name="driver">The configured Neo4j driver used to query the database.</param>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the evidence nodes.</param>
        /// <returns>The persisted evidence stable keys ordered by stable key.</returns>
        private static async Task<IReadOnlyList<string>> ReadEvidenceStableKeysAsync(IDriver driver, string snapshotStableKey)
        {
            // Reading only stable keys keeps canonicalization assertions focused on public graph identity.
            await using IAsyncSession session = driver.AsyncSession(sessionBuilder => sessionBuilder.WithDatabase("neo4j"));
            IResultCursor cursor = await session.RunAsync(
                @"MATCH (evidence:ArchonEvidence { snapshotStableKey: $snapshotStableKey })
RETURN evidence.stableKey AS stableKey
ORDER BY stableKey",
                new { snapshotStableKey });
            List<IRecord> records = await cursor.ToListAsync();
            return records.Select(static record => record["stableKey"].As<string>()).ToArray();
        }

        /// <summary>
        /// Reads the stable public graph shape needed to prove optimized persistence remains equivalent after repeated writes.
        /// </summary>
        /// <param name="driver">The configured Neo4j driver used to query the database.</param>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes snapshot-owned graph records.</param>
        /// <returns>A stable-key based graph snapshot for equivalence and idempotency assertions.</returns>
        private static async Task<GraphEquivalenceSnapshot> ReadGraphEquivalenceSnapshotAsync(IDriver driver, string snapshotStableKey)
        {
            // The query intentionally returns counts and ordered stable-key pairs rather than Neo4j internal identifiers. That keeps the test
            // aligned with Archon's public graph semantics and makes repeated-write idempotency visible as unchanged stable-key sets.
            await using IAsyncSession session = driver.AsyncSession(sessionBuilder => sessionBuilder.WithDatabase("neo4j"));
            IResultCursor cursor = await session.RunAsync(@"
CALL { MATCH (repository:ArchonRepository) RETURN count(repository) AS repositories }
CALL { MATCH (solution:ArchonSolution) RETURN count(solution) AS solutions }
CALL { MATCH (snapshot:ArchonSnapshot { stableKey: $snapshotStableKey }) RETURN count(snapshot) AS snapshots }
CALL { MATCH (node:ArchonNode { snapshotStableKey: $snapshotStableKey }) RETURN count(node) AS nodes, collect(node.stableKey) AS nodeStableKeys }
CALL { MATCH (evidence:ArchonEvidence { snapshotStableKey: $snapshotStableKey }) RETURN count(evidence) AS evidence, collect(evidence.stableKey) AS evidenceStableKeys }
CALL { MATCH (metric:ArchonMetric { snapshotStableKey: $snapshotStableKey }) RETURN count(metric) AS metrics, collect(metric.stableKey) AS metricStableKeys }
CALL { MATCH (:ArchonSnapshot { stableKey: $snapshotStableKey })-[includes:INCLUDES_SOLUTION]->(:ArchonSolution) RETURN count(includes) AS snapshotSolutionRelationships }
CALL { MATCH (:ArchonNode { snapshotStableKey: $snapshotStableKey })-[supported:SUPPORTED_BY_EVIDENCE]->(:ArchonEvidence { snapshotStableKey: $snapshotStableKey }) RETURN count(supported) AS nodeEvidenceRelationships }
CALL { MATCH (metric:ArchonMetric { snapshotStableKey: $snapshotStableKey })-[supported:SUPPORTED_BY_EVIDENCE]->(evidence:ArchonEvidence { snapshotStableKey: $snapshotStableKey }) RETURN count(supported) AS metricEvidenceRelationships, collect(metric.stableKey + '->' + evidence.stableKey) AS metricEvidencePairs }
CALL { MATCH (metric:ArchonMetric { snapshotStableKey: $snapshotStableKey })-[measures:MEASURES_NODE]->(node:ArchonNode { snapshotStableKey: $snapshotStableKey }) RETURN count(measures) AS metricTargetRelationships, collect(metric.stableKey + '->' + node.stableKey) AS metricTargetPairs }
RETURN repositories, solutions, snapshots, nodes, evidence, metrics, snapshotSolutionRelationships, nodeEvidenceRelationships, metricEvidenceRelationships, metricTargetRelationships, nodeStableKeys, evidenceStableKeys, metricStableKeys, metricEvidencePairs, metricTargetPairs",
                new { snapshotStableKey });
            IRecord record = await cursor.SingleAsync();

            return new GraphEquivalenceSnapshot(
                record["repositories"].As<long>(),
                record["solutions"].As<long>(),
                record["snapshots"].As<long>(),
                record["nodes"].As<long>(),
                record["evidence"].As<long>(),
                record["metrics"].As<long>(),
                record["snapshotSolutionRelationships"].As<long>(),
                record["nodeEvidenceRelationships"].As<long>(),
                record["metricEvidenceRelationships"].As<long>(),
                record["metricTargetRelationships"].As<long>(),
                SortStableKeys(record["nodeStableKeys"].As<List<string>>()),
                SortStableKeys(record["evidenceStableKeys"].As<List<string>>()),
                SortStableKeys(record["metricStableKeys"].As<List<string>>()),
                SortStableKeys(record["metricEvidencePairs"].As<List<string>>()),
                SortStableKeys(record["metricTargetPairs"].As<List<string>>()));
        }

        /// <summary>
        /// Sorts stable keys or stable-key pairs using ordinal comparison for deterministic test assertions.
        /// </summary>
        /// <param name="values">The unordered stable-key values returned by Neo4j collection aggregation.</param>
        /// <returns>The values ordered with ordinal comparison.</returns>
        private static IReadOnlyList<string> SortStableKeys(IEnumerable<string> values)
        {
            // Neo4j collection order is not the contract under test, so assertions normalize ordering before comparing public identities.
            return values.Order(StringComparer.Ordinal).ToArray();
        }

        /// <summary>
        /// Finds a diagnostic timing by its stable stage name.
        /// </summary>
        /// <param name="timings">The diagnostic timings returned by the persistence writer.</param>
        /// <param name="stage">The stable stage name to locate.</param>
        /// <returns>The matching diagnostic timing.</returns>
        private static ExtractionRunTiming FindTiming(IReadOnlyList<ExtractionRunTiming> timings, string stage)
        {
            // Single finds keep duration assertions readable while relying on xUnit to report a clear failure when a required stage is absent.
            return Assert.Single(timings, timing => StringComparer.Ordinal.Equals(stage, timing.Stage));
        }

        /// <summary>
        /// Reads representative persisted metric properties by stable key without exposing Neo4j internal identifiers.
        /// </summary>
        /// <param name="driver">The configured Neo4j driver used to query the database.</param>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the metric.</param>
        /// <param name="metricStableKey">The stable key of the metric to look up.</param>
        /// <returns>The normalized metric properties required by batching assertions.</returns>
        private static async Task<MetricPropertySnapshot> ReadMetricPropertiesAsync(IDriver driver, string snapshotStableKey, string metricStableKey)
        {
            // The query deliberately returns only public stable properties so tests do not normalize around Neo4j-local node IDs.
            await using IAsyncSession session = driver.AsyncSession(sessionBuilder => sessionBuilder.WithDatabase("neo4j"));
            IResultCursor cursor = await session.RunAsync(
                @"MATCH (metric:ArchonMetric { snapshotStableKey: $snapshotStableKey, stableKey: $metricStableKey })
RETURN metric.metricKind AS metricKind,
       metric.scopeKind AS scopeKind,
       metric.nodeStableKey AS nodeStableKey,
       metric.edgeStableKey AS edgeStableKey,
       metric.primaryEvidenceStableKey AS primaryEvidenceStableKey,
       metric.numericValue AS numericValue,
       metric.textValue AS textValue,
       metric.unit AS unit,
       metric.fingerprint AS fingerprint",
                new { snapshotStableKey, metricStableKey });
            IRecord record = await cursor.SingleAsync();
            object? numericValue = record["numericValue"];

            return new MetricPropertySnapshot(
                record["metricKind"].As<string>(),
                record["scopeKind"].As<string>(),
                record["nodeStableKey"].As<string?>(),
                record["edgeStableKey"].As<string?>(),
                record["primaryEvidenceStableKey"].As<string?>(),
                numericValue is null ? null : Convert.ToDecimal(numericValue, System.Globalization.CultureInfo.InvariantCulture),
                record["textValue"].As<string?>(),
                record["unit"].As<string?>(),
                record["fingerprint"].As<string>());
        }

        /// <summary>
        /// Captures minimal graph counts read from Neo4j.
        /// </summary>
        /// <param name="Repositories">The repository node count.</param>
        /// <param name="Solutions">The solution node count.</param>
        /// <param name="Snapshots">The snapshot node count.</param>
        /// <param name="Nodes">The architecture node count.</param>
        /// <param name="Evidence">The evidence node count.</param>
        /// <param name="Metrics">The metric node count.</param>
        /// <param name="SnapshotSolutionRelationships">The snapshot-to-solution relationship count.</param>
        /// <param name="NodeEvidenceRelationships">The node-to-evidence relationship count.</param>
        /// <param name="MetricEvidenceRelationships">The metric-to-evidence relationship count.</param>
        /// <param name="MetricTargetRelationships">The metric-to-target relationship count.</param>
        private sealed record GraphCounts(long Repositories, long Solutions, long Snapshots, long Nodes, long Evidence, long Metrics, long SnapshotSolutionRelationships, long NodeEvidenceRelationships, long MetricEvidenceRelationships, long MetricTargetRelationships);

        /// <summary>
        /// Captures stable-key based graph shape read from Neo4j for optimized persistence equivalence assertions.
        /// </summary>
        /// <param name="Repositories">The repository node count.</param>
        /// <param name="Solutions">The solution node count.</param>
        /// <param name="Snapshots">The snapshot node count.</param>
        /// <param name="Nodes">The architecture node count.</param>
        /// <param name="Evidence">The evidence node count.</param>
        /// <param name="Metrics">The metric node count.</param>
        /// <param name="SnapshotSolutionRelationships">The snapshot-to-solution relationship count.</param>
        /// <param name="NodeEvidenceRelationships">The node-to-evidence relationship count.</param>
        /// <param name="MetricEvidenceRelationships">The metric-to-evidence relationship count.</param>
        /// <param name="MetricTargetRelationships">The metric-to-target relationship count.</param>
        /// <param name="NodeStableKeys">The ordered persisted architecture node stable keys.</param>
        /// <param name="EvidenceStableKeys">The ordered persisted evidence stable keys.</param>
        /// <param name="MetricStableKeys">The ordered persisted metric stable keys.</param>
        /// <param name="MetricEvidencePairs">The ordered metric-to-evidence stable-key endpoint pairs.</param>
        /// <param name="MetricTargetPairs">The ordered metric-to-node stable-key endpoint pairs.</param>
        private sealed record GraphEquivalenceSnapshot(
            long Repositories,
            long Solutions,
            long Snapshots,
            long Nodes,
            long Evidence,
            long Metrics,
            long SnapshotSolutionRelationships,
            long NodeEvidenceRelationships,
            long MetricEvidenceRelationships,
            long MetricTargetRelationships,
            IReadOnlyList<string> NodeStableKeys,
            IReadOnlyList<string> EvidenceStableKeys,
            IReadOnlyList<string> MetricStableKeys,
            IReadOnlyList<string> MetricEvidencePairs,
            IReadOnlyList<string> MetricTargetPairs);

        /// <summary>
        /// Captures normalized architecture node properties read from Neo4j for stable-key based assertions.
        /// </summary>
        /// <param name="NodeKind">The persisted node kind.</param>
        /// <param name="DisplayName">The persisted display name.</param>
        /// <param name="QualifiedName">The optional persisted qualified name.</param>
        /// <param name="SearchName">The optional persisted search name.</param>
        /// <param name="Language">The optional persisted language.</param>
        /// <param name="ProjectStableKey">The optional persisted owning project stable key.</param>
        /// <param name="ParentNodeStableKey">The optional persisted parent node stable key.</param>
        /// <param name="Ownership">The optional persisted ownership classification.</param>
        /// <param name="ExternalCategory">The optional persisted external category.</param>
        /// <param name="PrimaryEvidenceStableKey">The optional persisted primary evidence stable key.</param>
        /// <param name="Fingerprint">The persisted node fingerprint.</param>
        private sealed record NodePropertySnapshot(string NodeKind, string DisplayName, string? QualifiedName, string? SearchName, string? Language, string? ProjectStableKey, string? ParentNodeStableKey, string? Ownership, string? ExternalCategory, string? PrimaryEvidenceStableKey, string Fingerprint);

        /// <summary>
        /// Captures normalized evidence properties read from Neo4j for stable-key based assertions.
        /// </summary>
        /// <param name="EvidenceKind">The persisted evidence kind.</param>
        /// <param name="FilePath">The persisted repository-relative file path.</param>
        /// <param name="StartLine">The optional persisted source start line.</param>
        /// <param name="EndLine">The optional persisted source end line.</param>
        /// <param name="SymbolName">The optional persisted source symbol name.</param>
        /// <param name="ContainingSymbol">The optional persisted containing source symbol.</param>
        /// <param name="Fingerprint">The persisted evidence fingerprint.</param>
        private sealed record EvidencePropertySnapshot(string EvidenceKind, string FilePath, int? StartLine, int? EndLine, string? SymbolName, string? ContainingSymbol, string Fingerprint);

        /// <summary>
        /// Captures normalized metric properties read from Neo4j for stable-key based assertions.
        /// </summary>
        /// <param name="MetricKind">The persisted metric kind.</param>
        /// <param name="ScopeKind">The persisted metric scope kind.</param>
        /// <param name="NodeStableKey">The optional architecture node target stable key.</param>
        /// <param name="EdgeStableKey">The optional architecture relationship target stable key.</param>
        /// <param name="PrimaryEvidenceStableKey">The optional primary evidence stable key.</param>
        /// <param name="NumericValue">The optional numeric metric value.</param>
        /// <param name="TextValue">The optional text metric value.</param>
        /// <param name="Unit">The optional metric unit.</param>
        /// <param name="Fingerprint">The persisted metric fingerprint.</param>
        private sealed record MetricPropertySnapshot(string MetricKind, string ScopeKind, string? NodeStableKey, string? EdgeStableKey, string? PrimaryEvidenceStableKey, decimal? NumericValue, string? TextValue, string? Unit, string Fingerprint);
    }
}
