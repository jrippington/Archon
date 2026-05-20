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
    /// Verifies snapshot persistence against a real Neo4j Testcontainers database.
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
        /// Confirms mixed architecture relationship kinds persist as traversal-ready relationship nodes with endpoint links.
        /// </summary>
        /// <returns>A task that completes after the snapshot has been written and relationship traversal has been verified.</returns>
        [Fact]
        public async Task WriteSnapshotAsyncPersistsArchitectureRelationshipsForTraversal()
        {
            // Relationship nodes preserve stable identity and create explicit source/target links so queries can traverse from source node,
            // through the relationship fact, to target node without relying on Neo4j internal relationship identifiers.
            await using ServiceProvider serviceProvider = CreateServiceProvider();
            IArchitectureGraphRecreator recreator = serviceProvider.GetRequiredService<IArchitectureGraphRecreator>();
            IArchitectureSnapshotWriter writer = serviceProvider.GetRequiredService<IArchitectureSnapshotWriter>();
            IDriver driver = serviceProvider.GetRequiredService<IDriver>();
            await recreator.RecreateGraphAsync(GraphRecreationRequest.CreateAuthorized("architecture relationship persistence test"));
            ExtractedArchitectureSnapshot snapshot = CreateRelationshipSnapshot("relationship-one", includeMissingSource: false, includeMissingTarget: false, includeMissingEvidence: false);

            SnapshotPersistenceResult result = await writer.WriteSnapshotAsync(snapshot);
            RelationshipGraphCounts counts = await ReadRelationshipGraphCountsAsync(driver);
            string? relationshipFingerprint = await ReadRelationshipFingerprintAsync(driver, "snapshot://relationship-one", "edge://relationship-one/project-references-package");
            string? traversedTarget = await ReadTraversedTargetAsync(driver, "snapshot://relationship-one", "project://relationship-one/app", "REFERENCES");

            Assert.True(result.Succeeded);
            Assert.Equal(2, result.Counts.ArchitectureRelationships);
            Assert.Equal(4, result.Counts.RelationshipEndpointRelationships);
            Assert.Equal(2, result.Counts.RelationshipEvidenceRelationships);
            Assert.Equal(2, counts.Relationships);
            Assert.Equal(4, counts.EndpointRelationships);
            Assert.Equal(2, counts.RelationshipEvidenceRelationships);
            Assert.Equal("sha256:edge-relationship-one-references", relationshipFingerprint);
            Assert.Equal("package://relationship-one/neo4j", traversedTarget);
        }

        /// <summary>
        /// Confirms multiple relationships between the same source and target remain distinct by stable key and edge kind.
        /// </summary>
        /// <returns>A task that completes after same-endpoint relationship persistence has been verified.</returns>
        [Fact]
        public async Task WriteSnapshotAsyncPersistsMultipleSameEndpointRelationships()
        {
            // The relationship-node pattern allows parallel facts between the same endpoints because the node stable key is the merge
            // identity and edgeKind is a queryable property rather than a single endpoint pair constraint.
            await using ServiceProvider serviceProvider = CreateServiceProvider();
            IArchitectureGraphRecreator recreator = serviceProvider.GetRequiredService<IArchitectureGraphRecreator>();
            IArchitectureSnapshotWriter writer = serviceProvider.GetRequiredService<IArchitectureSnapshotWriter>();
            IDriver driver = serviceProvider.GetRequiredService<IDriver>();
            await recreator.RecreateGraphAsync(GraphRecreationRequest.CreateAuthorized("same endpoint relationship test"));
            ExtractedArchitectureSnapshot snapshot = CreateRelationshipSnapshot("same-endpoint", includeMissingSource: false, includeMissingTarget: false, includeMissingEvidence: false);

            SnapshotPersistenceResult result = await writer.WriteSnapshotAsync(snapshot);
            long sameEndpointRelationships = await ReadSameEndpointRelationshipCountAsync(driver, "snapshot://same-endpoint", "project://same-endpoint/app", "package://same-endpoint/neo4j");

            Assert.True(result.Succeeded);
            Assert.Equal(2, sameEndpointRelationships);
        }

        /// <summary>
        /// Confirms missing architecture relationship references are returned as explicit errors before a transaction writes data.
        /// </summary>
        /// <param name="includeMissingSource">A value indicating whether the test snapshot should reference a missing source node.</param>
        /// <param name="includeMissingTarget">A value indicating whether the test snapshot should reference a missing target node.</param>
        /// <param name="includeMissingEvidence">A value indicating whether the test snapshot should reference missing edge evidence.</param>
        /// <param name="expectedCode">The stable persistence error code expected from validation.</param>
        /// <returns>A task that completes after validation failure has been asserted.</returns>
        [Theory]
        [InlineData(true, false, false, "MissingRelationshipSourceNodeReference")]
        [InlineData(false, true, false, "MissingRelationshipTargetNodeReference")]
        [InlineData(false, false, true, "MissingRelationshipEvidenceReference")]
        public async Task WriteSnapshotAsyncReturnsErrorForMissingArchitectureRelationshipReferences(bool includeMissingSource, bool includeMissingTarget, bool includeMissingEvidence, string expectedCode)
        {
            // Invalid relationship snapshots fail during validation so the writer never reports a completed snapshot with dangling graph facts.
            await using ServiceProvider serviceProvider = CreateServiceProvider();
            IArchitectureGraphRecreator recreator = serviceProvider.GetRequiredService<IArchitectureGraphRecreator>();
            IArchitectureSnapshotWriter writer = serviceProvider.GetRequiredService<IArchitectureSnapshotWriter>();
            IDriver driver = serviceProvider.GetRequiredService<IDriver>();
            await recreator.RecreateGraphAsync(GraphRecreationRequest.CreateAuthorized("missing relationship reference test"));
            ExtractedArchitectureSnapshot snapshot = CreateRelationshipSnapshot($"missing-{expectedCode}", includeMissingSource, includeMissingTarget, includeMissingEvidence);

            SnapshotPersistenceResult result = await writer.WriteSnapshotAsync(snapshot);
            RelationshipGraphCounts counts = await ReadRelationshipGraphCountsAsync(driver);

            Assert.False(result.Succeeded);
            Assert.Equal(expectedCode, Assert.Single(result.Errors).Code);
            Assert.Equal(0, counts.Relationships);
        }

        /// <summary>
        /// Confirms global rule catalog entries are upserted by rule code and version rather than duplicated per snapshot.
        /// </summary>
        /// <returns>A task that completes after the same rule has been written through two snapshots and counted once.</returns>
        [Fact]
        public async Task WriteSnapshotAsyncUpsertsRuleCatalogByCodeAndVersion()
        {
            // Two snapshots contain the same rule definition; the global ArchonRule node should remain one versioned catalog entry.
            await using ServiceProvider serviceProvider = CreateServiceProvider();
            IArchitectureGraphRecreator recreator = serviceProvider.GetRequiredService<IArchitectureGraphRecreator>();
            IArchitectureSnapshotWriter writer = serviceProvider.GetRequiredService<IArchitectureSnapshotWriter>();
            IDriver driver = serviceProvider.GetRequiredService<IDriver>();
            await recreator.RecreateGraphAsync(GraphRecreationRequest.CreateAuthorized("rule catalog upsert test"));

            SnapshotPersistenceResult first = await writer.WriteSnapshotAsync(CreateRulesAndFindingsSnapshot("rule-upsert-one", "1.0.0", includeFinding: false, includeMissingRule: false, includeMissingNode: false, includeMissingEvidence: false));
            SnapshotPersistenceResult second = await writer.WriteSnapshotAsync(CreateRulesAndFindingsSnapshot("rule-upsert-two", "1.0.0", includeFinding: false, includeMissingRule: false, includeMissingNode: false, includeMissingEvidence: false));
            RuleFindingGraphCounts counts = await ReadRuleFindingGraphCountsAsync(driver);

            Assert.True(first.Succeeded);
            Assert.True(second.Succeeded);
            Assert.Equal(1, first.Counts.Rules);
            Assert.Equal(1, second.Counts.Rules);
            Assert.Equal(1, counts.Rules);
        }

        /// <summary>
        /// Confirms multiple versions of the same rule code coexist for historical finding fidelity.
        /// </summary>
        /// <returns>A task that completes after two rule versions have been persisted and counted separately.</returns>
        [Fact]
        public async Task WriteSnapshotAsyncPreservesMultipleRuleVersions()
        {
            // Version is part of the catalog identity so later historical queries can explain which rule version produced each finding.
            await using ServiceProvider serviceProvider = CreateServiceProvider();
            IArchitectureGraphRecreator recreator = serviceProvider.GetRequiredService<IArchitectureGraphRecreator>();
            IArchitectureSnapshotWriter writer = serviceProvider.GetRequiredService<IArchitectureSnapshotWriter>();
            IDriver driver = serviceProvider.GetRequiredService<IDriver>();
            await recreator.RecreateGraphAsync(GraphRecreationRequest.CreateAuthorized("rule version coexistence test"));

            SnapshotPersistenceResult first = await writer.WriteSnapshotAsync(CreateRulesAndFindingsSnapshot("rule-version-one", "1.0.0", includeFinding: false, includeMissingRule: false, includeMissingNode: false, includeMissingEvidence: false));
            SnapshotPersistenceResult second = await writer.WriteSnapshotAsync(CreateRulesAndFindingsSnapshot("rule-version-two", "2.0.0", includeFinding: false, includeMissingRule: false, includeMissingNode: false, includeMissingEvidence: false));
            RuleFindingGraphCounts counts = await ReadRuleFindingGraphCountsAsync(driver);

            Assert.True(first.Succeeded);
            Assert.True(second.Succeeded);
            Assert.Equal(2, counts.Rules);
        }

        /// <summary>
        /// Confirms findings persist their properties and links to rule versions, primary nodes, and evidence.
        /// </summary>
        /// <returns>A task that completes after finding properties and graph links have been queried back from Neo4j.</returns>
        [Fact]
        public async Task WriteSnapshotAsyncPersistsFindingsWithRuleNodeAndEvidenceLinks()
        {
            // This test covers the Work Item 6 graph shape: global rule version, snapshot-scoped finding, and explicit support links.
            await using ServiceProvider serviceProvider = CreateServiceProvider();
            IArchitectureGraphRecreator recreator = serviceProvider.GetRequiredService<IArchitectureGraphRecreator>();
            IArchitectureSnapshotWriter writer = serviceProvider.GetRequiredService<IArchitectureSnapshotWriter>();
            IDriver driver = serviceProvider.GetRequiredService<IDriver>();
            await recreator.RecreateGraphAsync(GraphRecreationRequest.CreateAuthorized("finding persistence test"));
            ExtractedArchitectureSnapshot snapshot = CreateRulesAndFindingsSnapshot("finding-one", "1.0.0", includeFinding: true, includeMissingRule: false, includeMissingNode: false, includeMissingEvidence: false);

            SnapshotPersistenceResult result = await writer.WriteSnapshotAsync(snapshot);
            RuleFindingGraphCounts counts = await ReadRuleFindingGraphCountsAsync(driver);
            PersistedFindingDetails? details = await ReadPersistedFindingDetailsAsync(driver, "snapshot://finding-one", "finding://finding-one/invalid-dependency");

            Assert.True(result.Succeeded);
            Assert.Equal(1, result.Counts.Rules);
            Assert.Equal(1, result.Counts.Findings);
            Assert.Equal(1, result.Counts.FindingRuleRelationships);
            Assert.Equal(1, result.Counts.FindingNodeRelationships);
            Assert.Equal(1, result.Counts.FindingEvidenceRelationships);
            Assert.Equal(1, counts.Findings);
            Assert.Equal(1, counts.FindingRuleRelationships);
            Assert.Equal(1, counts.FindingNodeRelationships);
            Assert.Equal(1, counts.FindingEvidenceRelationships);
            Assert.NotNull(details);
            Assert.Equal("ARCHON001", details.RuleCode);
            Assert.Equal("1.0.0", details.RuleVersion);
            Assert.Equal("High", details.Severity);
            Assert.Equal("Suppressed", details.Status);
            Assert.Equal("Accepted risk", details.SuppressionReason);
            Assert.Equal("sha256:finding-finding-one", details.Fingerprint);
        }

        /// <summary>
        /// Confirms missing finding references are returned as explicit errors before any partial finding data is written.
        /// </summary>
        /// <param name="includeMissingRule">A value indicating whether the finding should reference a missing rule version.</param>
        /// <param name="includeMissingNode">A value indicating whether the finding should reference a missing primary node.</param>
        /// <param name="includeMissingEvidence">A value indicating whether the finding should reference missing primary evidence.</param>
        /// <param name="expectedCode">The stable persistence error code expected from validation.</param>
        /// <returns>A task that completes after validation failure has been asserted.</returns>
        [Theory]
        [InlineData(true, false, false, "MissingFindingRuleReference")]
        [InlineData(false, true, false, "MissingFindingNodeReference")]
        [InlineData(false, false, true, "MissingFindingEvidenceReference")]
        public async Task WriteSnapshotAsyncReturnsErrorForMissingFindingReferences(bool includeMissingRule, bool includeMissingNode, bool includeMissingEvidence, string expectedCode)
        {
            // Invalid finding snapshots fail before the transaction so no historical finding data is partially persisted.
            await using ServiceProvider serviceProvider = CreateServiceProvider();
            IArchitectureGraphRecreator recreator = serviceProvider.GetRequiredService<IArchitectureGraphRecreator>();
            IArchitectureSnapshotWriter writer = serviceProvider.GetRequiredService<IArchitectureSnapshotWriter>();
            IDriver driver = serviceProvider.GetRequiredService<IDriver>();
            await recreator.RecreateGraphAsync(GraphRecreationRequest.CreateAuthorized("missing finding reference test"));
            ExtractedArchitectureSnapshot snapshot = CreateRulesAndFindingsSnapshot($"missing-{expectedCode}", "1.0.0", includeFinding: true, includeMissingRule, includeMissingNode, includeMissingEvidence);

            SnapshotPersistenceResult result = await writer.WriteSnapshotAsync(snapshot);
            RuleFindingGraphCounts counts = await ReadRuleFindingGraphCountsAsync(driver);

            Assert.False(result.Succeeded);
            Assert.Equal(expectedCode, Assert.Single(result.Errors).Code);
            Assert.Equal(0, counts.Findings);
        }

        /// <summary>
        /// Confirms metrics persist first-class values and link to evidence, node targets, and relationship targets.
        /// </summary>
        /// <returns>A task that completes after metric properties and graph links have been queried back from Neo4j.</returns>
        [Fact]
        public async Task WriteSnapshotAsyncPersistsMetricPersistenceRecordsWithEvidenceAndTargets()
        {
            // This test covers the Work Item 7 metric shape: a durable metric node plus evidence, node, and relationship support links.
            await using ServiceProvider serviceProvider = CreateServiceProvider();
            IArchitectureGraphRecreator recreator = serviceProvider.GetRequiredService<IArchitectureGraphRecreator>();
            IArchitectureSnapshotWriter writer = serviceProvider.GetRequiredService<IArchitectureSnapshotWriter>();
            IDriver driver = serviceProvider.GetRequiredService<IDriver>();
            await recreator.RecreateGraphAsync(GraphRecreationRequest.CreateAuthorized("metric persistence test"));
            ExtractedArchitectureSnapshot snapshot = CreateMetricsAndSummariesSnapshot("metric-persistence", includeMetrics: true, includeSummaries: false, includeMissingMetricNode: false, includeMissingMetricRelationship: false, includeMissingMetricEvidence: false, includeMissingSummaryTarget: false);

            SnapshotPersistenceResult result = await writer.WriteSnapshotAsync(snapshot);
            MetricSummaryGraphCounts counts = await ReadMetricSummaryGraphCountsAsync(driver);
            PersistedMetricDetails? details = await ReadPersistedMetricDetailsAsync(driver, "snapshot://metric-persistence", "metric://metric-persistence/dependency-count");

            Assert.True(result.Succeeded);
            Assert.Equal(1, result.Counts.Metrics);
            Assert.Equal(1, result.Counts.MetricEvidenceRelationships);
            Assert.Equal(2, result.Counts.MetricTargetRelationships);
            Assert.Equal(1, counts.Metrics);
            Assert.Equal(1, counts.MetricEvidenceRelationships);
            Assert.Equal(1, counts.MetricNodeRelationships);
            Assert.Equal(1, counts.MetricRelationshipRelationships);
            Assert.NotNull(details);
            Assert.Equal("DependencyCount", details.MetricKind);
            Assert.Equal("Edge", details.ScopeKind);
            Assert.Equal(12.5m, details.NumericValue);
            Assert.Equal("relationships", details.Unit);
            Assert.Equal("sha256:metric-metric-persistence", details.Fingerprint);
        }

        /// <summary>
        /// Confirms generated summaries persist content fields and link to their snapshot and target graph record.
        /// </summary>
        /// <returns>A task that completes after generated-summary properties and graph links have been queried back from Neo4j.</returns>
        [Fact]
        public async Task WriteSnapshotAsyncPersistsGeneratedSummaryRecordsWithSnapshotAndTargetLinks()
        {
            // Generated summaries are durable narrative outputs that should be traversable from the snapshot and target record.
            await using ServiceProvider serviceProvider = CreateServiceProvider();
            IArchitectureGraphRecreator recreator = serviceProvider.GetRequiredService<IArchitectureGraphRecreator>();
            IArchitectureSnapshotWriter writer = serviceProvider.GetRequiredService<IArchitectureSnapshotWriter>();
            IDriver driver = serviceProvider.GetRequiredService<IDriver>();
            await recreator.RecreateGraphAsync(GraphRecreationRequest.CreateAuthorized("generated summary persistence test"));
            ExtractedArchitectureSnapshot snapshot = CreateMetricsAndSummariesSnapshot("generated-summary", includeMetrics: false, includeSummaries: true, includeMissingMetricNode: false, includeMissingMetricRelationship: false, includeMissingMetricEvidence: false, includeMissingSummaryTarget: false);

            SnapshotPersistenceResult result = await writer.WriteSnapshotAsync(snapshot);
            MetricSummaryGraphCounts counts = await ReadMetricSummaryGraphCountsAsync(driver);
            PersistedSummaryDetails? details = await ReadPersistedSummaryDetailsAsync(driver, "snapshot://generated-summary", "summary://generated-summary/project-summary");

            Assert.True(result.Succeeded);
            Assert.Equal(1, result.Counts.GeneratedSummaries);
            Assert.Equal(1, result.Counts.SummarySnapshotRelationships);
            Assert.Equal(1, result.Counts.SummaryTargetRelationships);
            Assert.Equal(1, counts.GeneratedSummaries);
            Assert.Equal(1, counts.SummarySnapshotRelationships);
            Assert.Equal(1, counts.SummaryNodeRelationships);
            Assert.NotNull(details);
            Assert.Equal("Node", details.SummaryKind);
            Assert.Equal("Markdown", details.Format);
            Assert.Equal("Application project summary", details.Title);
            Assert.Equal("sha256:summary-generated-summary", details.Fingerprint);
        }

        /// <summary>
        /// Confirms a mixed metric and generated-summary snapshot reports every Work Item 7 count together.
        /// </summary>
        /// <returns>A task that completes after a mixed snapshot has been persisted and counted.</returns>
        [Fact]
        public async Task WriteSnapshotAsyncPersistsMetricPersistenceAndGeneratedSummaryInMixedSnapshot()
        {
            // A mixed snapshot proves metrics and summaries can be written in the same coordinated transaction as the existing graph facts.
            await using ServiceProvider serviceProvider = CreateServiceProvider();
            IArchitectureGraphRecreator recreator = serviceProvider.GetRequiredService<IArchitectureGraphRecreator>();
            IArchitectureSnapshotWriter writer = serviceProvider.GetRequiredService<IArchitectureSnapshotWriter>();
            IDriver driver = serviceProvider.GetRequiredService<IDriver>();
            await recreator.RecreateGraphAsync(GraphRecreationRequest.CreateAuthorized("mixed metric and summary test"));
            ExtractedArchitectureSnapshot snapshot = CreateMetricsAndSummariesSnapshot("mixed-metric-summary", includeMetrics: true, includeSummaries: true, includeMissingMetricNode: false, includeMissingMetricRelationship: false, includeMissingMetricEvidence: false, includeMissingSummaryTarget: false);

            SnapshotPersistenceResult result = await writer.WriteSnapshotAsync(snapshot);
            MetricSummaryGraphCounts counts = await ReadMetricSummaryGraphCountsAsync(driver);

            Assert.True(result.Succeeded);
            Assert.Equal(1, result.Counts.Metrics);
            Assert.Equal(1, result.Counts.GeneratedSummaries);
            Assert.Equal(1, counts.Metrics);
            Assert.Equal(1, counts.GeneratedSummaries);
        }

        /// <summary>
        /// Confirms missing metric and generated-summary references are returned as explicit validation errors before partial writes.
        /// </summary>
        /// <param name="includeMissingMetricNode">A value indicating whether the metric should reference a missing node target.</param>
        /// <param name="includeMissingMetricRelationship">A value indicating whether the metric should reference a missing relationship target.</param>
        /// <param name="includeMissingMetricEvidence">A value indicating whether the metric should reference missing evidence.</param>
        /// <param name="includeMissingSummaryTarget">A value indicating whether the generated summary should reference a missing target.</param>
        /// <param name="expectedCode">The stable persistence error code expected from validation.</param>
        /// <returns>A task that completes after validation failure has been asserted.</returns>
        [Theory]
        [InlineData(true, false, false, false, "MissingMetricNodeReference")]
        [InlineData(false, true, false, false, "MissingMetricRelationshipReference")]
        [InlineData(false, false, true, false, "MissingMetricEvidenceReference")]
        [InlineData(false, false, false, true, "MissingGeneratedSummaryTargetReference")]
        public async Task WriteSnapshotAsyncReturnsErrorForMissingMetricPersistenceOrGeneratedSummaryReferences(bool includeMissingMetricNode, bool includeMissingMetricRelationship, bool includeMissingMetricEvidence, bool includeMissingSummaryTarget, string expectedCode)
        {
            // Invalid Work Item 7 snapshots fail during validation so metrics and summaries are never partially persisted with dangling links.
            await using ServiceProvider serviceProvider = CreateServiceProvider();
            IArchitectureGraphRecreator recreator = serviceProvider.GetRequiredService<IArchitectureGraphRecreator>();
            IArchitectureSnapshotWriter writer = serviceProvider.GetRequiredService<IArchitectureSnapshotWriter>();
            IDriver driver = serviceProvider.GetRequiredService<IDriver>();
            await recreator.RecreateGraphAsync(GraphRecreationRequest.CreateAuthorized("missing metric summary reference test"));
            ExtractedArchitectureSnapshot snapshot = CreateMetricsAndSummariesSnapshot($"missing-{expectedCode}", includeMetrics: true, includeSummaries: true, includeMissingMetricNode, includeMissingMetricRelationship, includeMissingMetricEvidence, includeMissingSummaryTarget);

            SnapshotPersistenceResult result = await writer.WriteSnapshotAsync(snapshot);
            MetricSummaryGraphCounts counts = await ReadMetricSummaryGraphCountsAsync(driver);

            Assert.False(result.Succeeded);
            Assert.Equal(expectedCode, Assert.Single(result.Errors).Code);
            Assert.Equal(0, counts.Metrics);
            Assert.Equal(0, counts.GeneratedSummaries);
        }

        /// <summary>
        /// Confirms a representative full mixed snapshot persists every WP003 graph section in one coordinated workflow.
        /// </summary>
        /// <returns>A task that completes after the full mixed snapshot has been written and queried back from Neo4j.</returns>
        [Fact]
        public async Task WriteSnapshotAsyncPersistsFullMixedSnapshotWithSupportingRelationships()
        {
            // Work Item 8 needs more than isolated per-section tests: this scenario proves repositories, solutions, snapshots,
            // nodes, relationships, evidence, rules, findings, metrics, summaries, and all supporting links coexist in one write.
            await using ServiceProvider serviceProvider = CreateServiceProvider();
            IArchitectureGraphRecreator recreator = serviceProvider.GetRequiredService<IArchitectureGraphRecreator>();
            IArchitectureSnapshotWriter writer = serviceProvider.GetRequiredService<IArchitectureSnapshotWriter>();
            IDriver driver = serviceProvider.GetRequiredService<IDriver>();
            await recreator.RecreateGraphAsync(GraphRecreationRequest.CreateAuthorized("full mixed snapshot persistence test"));
            ExtractedArchitectureSnapshot snapshot = FullMixedSnapshotTestDataBuilder.Create("full-mixed");

            SnapshotPersistenceResult result = await writer.WriteSnapshotAsync(snapshot);
            FullMixedGraphCounts counts = await ReadFullMixedGraphCountsAsync(driver);
            FullMixedStableLookups stableLookups = await ReadFullMixedStableLookupsAsync(driver, "snapshot://full-mixed");

            Assert.True(result.Succeeded);
            Assert.Equal("snapshot://full-mixed", result.SnapshotStableKey);
            Assert.Equal(1, result.Counts.Repositories);
            Assert.Equal(1, result.Counts.Solutions);
            Assert.Equal(1, result.Counts.Snapshots);
            Assert.Equal(3, result.Counts.Nodes);
            Assert.Equal(3, result.Counts.Evidence);
            Assert.Equal(2, result.Counts.ArchitectureRelationships);
            Assert.Equal(1, result.Counts.Rules);
            Assert.Equal(1, result.Counts.Findings);
            Assert.Equal(2, result.Counts.Metrics);
            Assert.Equal(3, result.Counts.GeneratedSummaries);
            Assert.Equal(1, result.Counts.SnapshotSolutionRelationships);
            Assert.Equal(3, result.Counts.NodeEvidenceRelationships);
            Assert.Equal(4, result.Counts.RelationshipEndpointRelationships);
            Assert.Equal(2, result.Counts.RelationshipEvidenceRelationships);
            Assert.Equal(1, result.Counts.FindingRuleRelationships);
            Assert.Equal(1, result.Counts.FindingNodeRelationships);
            Assert.Equal(1, result.Counts.FindingEvidenceRelationships);
            Assert.Equal(2, result.Counts.MetricEvidenceRelationships);
            Assert.Equal(3, result.Counts.MetricTargetRelationships);
            Assert.Equal(3, result.Counts.SummarySnapshotRelationships);
            Assert.Equal(2, result.Counts.SummaryTargetRelationships);
            Assert.Equal(new FullMixedGraphCounts(1, 1, 1, 3, 2, 3, 1, 1, 2, 3), counts with { });
            Assert.Equal("sha256:node-full-mixed-project", stableLookups.ProjectNodeFingerprint);
            Assert.Equal("sha256:edge-full-mixed-uses-package", stableLookups.PackageRelationshipFingerprint);
            Assert.Equal("sha256:metric-full-mixed-dependency-count", stableLookups.DependencyMetricFingerprint);
            Assert.Equal("sha256:summary-full-mixed-relationship", stableLookups.RelationshipSummaryFingerprint);
        }

        /// <summary>
        /// Confirms the full mixed snapshot writes every required supporting relationship shape with queryable stable-key paths.
        /// </summary>
        /// <returns>A task that completes after supporting relationship queryability has been verified.</returns>
        [Fact]
        public async Task WriteSnapshotAsyncMakesFullMixedSupportingRelationshipsQueryable()
        {
            // The assertions intentionally query relationship paths rather than only node counts so later query and MCP packages can rely
            // on stable traversal shapes for evidence, rules, metric targets, and generated-summary targets.
            await using ServiceProvider serviceProvider = CreateServiceProvider();
            IArchitectureGraphRecreator recreator = serviceProvider.GetRequiredService<IArchitectureGraphRecreator>();
            IArchitectureSnapshotWriter writer = serviceProvider.GetRequiredService<IArchitectureSnapshotWriter>();
            IDriver driver = serviceProvider.GetRequiredService<IDriver>();
            await recreator.RecreateGraphAsync(GraphRecreationRequest.CreateAuthorized("full mixed supporting relationship test"));
            ExtractedArchitectureSnapshot snapshot = FullMixedSnapshotTestDataBuilder.Create("supporting-relationship");

            SnapshotPersistenceResult result = await writer.WriteSnapshotAsync(snapshot);
            FullMixedSupportingRelationships relationships = await ReadFullMixedSupportingRelationshipsAsync(driver, "snapshot://supporting-relationship");

            Assert.True(result.Succeeded);
            Assert.True(relationships.SnapshotIncludesSolution);
            Assert.True(relationships.NodeHasEvidence);
            Assert.True(relationships.RelationshipHasEvidence);
            Assert.True(relationships.FindingHasRule);
            Assert.True(relationships.FindingHasNode);
            Assert.True(relationships.FindingHasEvidence);
            Assert.True(relationships.MetricHasEvidence);
            Assert.True(relationships.MetricHasNodeTarget);
            Assert.True(relationships.MetricHasRelationshipTarget);
            Assert.True(relationships.SummaryHasSnapshotTarget);
            Assert.True(relationships.SummaryHasNodeTarget);
            Assert.True(relationships.SummaryHasRelationshipTarget);
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
        /// Creates a snapshot containing architecture relationships for Work Item 5 persistence validation.
        /// </summary>
        /// <param name="suffix">The unique suffix used to isolate stable keys for the test.</param>
        /// <param name="includeMissingSource">A value indicating whether the first edge should reference a missing source node.</param>
        /// <param name="includeMissingTarget">A value indicating whether the first edge should reference a missing target node.</param>
        /// <param name="includeMissingEvidence">A value indicating whether the first edge should reference missing primary evidence.</param>
        /// <returns>An extracted architecture snapshot containing relationship records.</returns>
        private static ExtractedArchitectureSnapshot CreateRelationshipSnapshot(string suffix, bool includeMissingSource, bool includeMissingTarget, bool includeMissingEvidence)
        {
            // The snapshot includes two relationship facts between the same endpoints with different kinds, proving parallel relationship
            // facts are preserved while still allowing validation flags to introduce one missing reference at a time.
            StableKey repositoryStableKey = new($"repository://{suffix}");
            StableKey solutionStableKey = new($"solution://{suffix}");
            StableKey snapshotStableKey = new($"snapshot://{suffix}");
            StableKey evidenceStableKey = new($"evidence://{suffix}/relationship");
            StableKey sourceNodeStableKey = new($"project://{suffix}/app");
            StableKey targetNodeStableKey = new($"package://{suffix}/neo4j");
            StableKey edgeSourceStableKey = includeMissingSource ? new StableKey($"project://{suffix}/missing") : sourceNodeStableKey;
            StableKey edgeTargetStableKey = includeMissingTarget ? new StableKey($"package://{suffix}/missing") : targetNodeStableKey;
            StableKey edgeEvidenceStableKey = includeMissingEvidence ? new StableKey($"evidence://{suffix}/missing") : evidenceStableKey;

            RepositoryModel repository = CreateRepository(repositoryStableKey, suffix);
            SolutionModel solution = CreateSolution(repositoryStableKey, solutionStableKey, suffix);
            SnapshotHeader header = CreateHeader(repositoryStableKey, snapshotStableKey, suffix);
            EvidenceRecord evidence = CreateEvidence(snapshotStableKey, evidenceStableKey, suffix);
            ArchitectureNode sourceNode = CreateNode(snapshotStableKey, sourceNodeStableKey, evidenceStableKey, suffix, "Application Project");
            ArchitectureNode targetNode = CreateNode(snapshotStableKey, targetNodeStableKey, evidenceStableKey, suffix, "Neo4j Package");
            ArchitectureEdge referencesEdge = CreateEdge(snapshotStableKey, new StableKey($"edge://{suffix}/project-references-package"), EdgeKind.References, edgeSourceStableKey, edgeTargetStableKey, true, edgeEvidenceStableKey, suffix, "references");
            ArchitectureEdge usesPackageEdge = CreateEdge(snapshotStableKey, new StableKey($"edge://{suffix}/project-uses-package"), EdgeKind.UsesPackage, edgeSourceStableKey, edgeTargetStableKey, true, edgeEvidenceStableKey, suffix, "uses-package");

            return new ExtractedArchitectureSnapshot(header, new[] { repository }, new[] { solution }, new[] { sourceNode, targetNode }, new[] { referencesEdge, usesPackageEdge }, new[] { evidence }, Array.Empty<RuleDefinition>(), Array.Empty<FindingRecord>(), Array.Empty<MetricRecord>(), Array.Empty<GeneratedSummary>(), Array.Empty<string>(), Array.Empty<string>());
        }

        /// <summary>
        /// Creates a snapshot containing rule catalog entries and optional finding records for Work Item 6 validation.
        /// </summary>
        /// <param name="suffix">The unique suffix used to isolate stable keys for the test.</param>
        /// <param name="ruleVersion">The rule version contributed by the snapshot.</param>
        /// <param name="includeFinding">A value indicating whether a finding should be included.</param>
        /// <param name="includeMissingRule">A value indicating whether the finding should reference a missing rule version.</param>
        /// <param name="includeMissingNode">A value indicating whether the finding should reference a missing node.</param>
        /// <param name="includeMissingEvidence">A value indicating whether the finding should reference missing evidence.</param>
        /// <returns>An extracted architecture snapshot containing rule and optional finding records.</returns>
        private static ExtractedArchitectureSnapshot CreateRulesAndFindingsSnapshot(string suffix, string ruleVersion, bool includeFinding, bool includeMissingRule, bool includeMissingNode, bool includeMissingEvidence)
        {
            // The snapshot reuses the minimal repository, solution, node, and evidence shape, then layers rule and finding facts on top.
            StableKey repositoryStableKey = new($"repository://{suffix}");
            StableKey solutionStableKey = new($"solution://{suffix}");
            StableKey snapshotStableKey = new($"snapshot://{suffix}");
            StableKey evidenceStableKey = new($"evidence://{suffix}/finding");
            StableKey nodeStableKey = new($"project://{suffix}/app");

            RepositoryModel repository = CreateRepository(repositoryStableKey, suffix);
            SolutionModel solution = CreateSolution(repositoryStableKey, solutionStableKey, suffix);
            SnapshotHeader header = CreateHeader(repositoryStableKey, snapshotStableKey, suffix);
            EvidenceRecord evidence = CreateEvidence(snapshotStableKey, evidenceStableKey, suffix);
            ArchitectureNode node = CreateNode(snapshotStableKey, nodeStableKey, evidenceStableKey, suffix, "Application Project");
            RuleDefinition rule = CreateRuleDefinition("ARCHON001", ruleVersion, suffix);
            FindingRecord[] findings = includeFinding
                ? new[] { CreateFinding(snapshotStableKey, new StableKey($"finding://{suffix}/invalid-dependency"), includeMissingRule ? "ARCHON404" : "ARCHON001", ruleVersion, includeMissingNode ? new StableKey($"project://{suffix}/missing") : nodeStableKey, includeMissingEvidence ? new StableKey($"evidence://{suffix}/missing") : evidenceStableKey, suffix) }
                : Array.Empty<FindingRecord>();

            return new ExtractedArchitectureSnapshot(header, new[] { repository }, new[] { solution }, new[] { node }, Array.Empty<ArchitectureEdge>(), new[] { evidence }, new[] { rule }, findings, Array.Empty<MetricRecord>(), Array.Empty<GeneratedSummary>(), Array.Empty<string>(), Array.Empty<string>());
        }

        /// <summary>
        /// Creates a snapshot containing metric and generated-summary records for Work Item 7 validation.
        /// </summary>
        /// <param name="suffix">The unique suffix used to isolate stable keys for the test.</param>
        /// <param name="includeMetrics">A value indicating whether metric records should be included.</param>
        /// <param name="includeSummaries">A value indicating whether generated-summary records should be included.</param>
        /// <param name="includeMissingMetricNode">A value indicating whether the metric should reference a missing node target.</param>
        /// <param name="includeMissingMetricRelationship">A value indicating whether the metric should reference a missing relationship target.</param>
        /// <param name="includeMissingMetricEvidence">A value indicating whether the metric should reference missing evidence.</param>
        /// <param name="includeMissingSummaryTarget">A value indicating whether the summary should reference a missing target.</param>
        /// <returns>An extracted architecture snapshot containing metric and generated-summary records.</returns>
        private static ExtractedArchitectureSnapshot CreateMetricsAndSummariesSnapshot(string suffix, bool includeMetrics, bool includeSummaries, bool includeMissingMetricNode, bool includeMissingMetricRelationship, bool includeMissingMetricEvidence, bool includeMissingSummaryTarget)
        {
            // The snapshot includes one relationship so metrics and summaries can prove both node-target and relationship-target links.
            StableKey repositoryStableKey = new($"repository://{suffix}");
            StableKey solutionStableKey = new($"solution://{suffix}");
            StableKey snapshotStableKey = new($"snapshot://{suffix}");
            StableKey evidenceStableKey = new($"evidence://{suffix}/metric-summary");
            StableKey sourceNodeStableKey = new($"project://{suffix}/app");
            StableKey targetNodeStableKey = new($"package://{suffix}/neo4j");
            StableKey relationshipStableKey = new($"edge://{suffix}/project-references-package");

            RepositoryModel repository = CreateRepository(repositoryStableKey, suffix);
            SolutionModel solution = CreateSolution(repositoryStableKey, solutionStableKey, suffix);
            SnapshotHeader header = CreateHeader(repositoryStableKey, snapshotStableKey, suffix);
            EvidenceRecord evidence = CreateEvidence(snapshotStableKey, evidenceStableKey, suffix);
            ArchitectureNode sourceNode = CreateNode(snapshotStableKey, sourceNodeStableKey, evidenceStableKey, suffix, "Application Project");
            ArchitectureNode targetNode = CreateNode(snapshotStableKey, targetNodeStableKey, evidenceStableKey, suffix, "Neo4j Package");
            ArchitectureEdge edge = CreateEdge(snapshotStableKey, relationshipStableKey, EdgeKind.References, sourceNodeStableKey, targetNodeStableKey, true, evidenceStableKey, suffix, "metric-summary-reference");
            MetricRecord[] metrics = includeMetrics
                ? new[] { CreateMetric(snapshotStableKey, new StableKey($"metric://{suffix}/dependency-count"), includeMissingMetricNode ? new StableKey($"project://{suffix}/missing") : sourceNodeStableKey, includeMissingMetricRelationship ? new StableKey($"edge://{suffix}/missing") : relationshipStableKey, includeMissingMetricEvidence ? new StableKey($"evidence://{suffix}/missing") : evidenceStableKey, suffix) }
                : Array.Empty<MetricRecord>();
            GeneratedSummary[] generatedSummaries = includeSummaries
                ? new[] { CreateGeneratedSummary(snapshotStableKey, new StableKey($"summary://{suffix}/project-summary"), includeMissingSummaryTarget ? new StableKey($"project://{suffix}/missing") : sourceNodeStableKey, suffix) }
                : Array.Empty<GeneratedSummary>();

            return new ExtractedArchitectureSnapshot(header, new[] { repository }, new[] { solution }, new[] { sourceNode, targetNode }, new[] { edge }, new[] { evidence }, Array.Empty<RuleDefinition>(), Array.Empty<FindingRecord>(), metrics, generatedSummaries, Array.Empty<string>(), Array.Empty<string>());
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
        /// Creates an architecture relationship for a persistence test snapshot.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that scopes the edge.</param>
        /// <param name="stableKey">The stable key that identifies the edge.</param>
        /// <param name="edgeKind">The controlled kind used to classify the edge.</param>
        /// <param name="sourceNodeStableKey">The stable key of the source architecture node.</param>
        /// <param name="targetNodeStableKey">The stable key of the target architecture node.</param>
        /// <param name="isDirect">A value indicating whether the edge was directly observed.</param>
        /// <param name="evidenceStableKey">The primary evidence stable key referenced by the edge.</param>
        /// <param name="suffix">The unique suffix used in fingerprints.</param>
        /// <param name="fingerprintSuffix">The unique suffix used to distinguish relationship fingerprints.</param>
        /// <returns>An architecture edge.</returns>
        private static ArchitectureEdge CreateEdge(StableKey snapshotStableKey, StableKey stableKey, EdgeKind edgeKind, StableKey sourceNodeStableKey, StableKey targetNodeStableKey, bool isDirect, StableKey evidenceStableKey, string suffix, string fingerprintSuffix)
        {
            // Relationship metadata is deliberately sparse because Work Item 5 validates first-class edge properties and traversal links.
            return new ArchitectureEdge(
                snapshotStableKey,
                stableKey,
                edgeKind,
                sourceNodeStableKey,
                targetNodeStableKey,
                isDirect,
                KnowledgeKind.Fact,
                Confidence.Certain,
                UnknownState.Known,
                evidenceStableKey,
                GraphMetadata.Empty,
                new Fingerprint($"sha256:edge-{suffix}-{fingerprintSuffix}"));
        }

        /// <summary>
        /// Creates a rule definition for a persistence test snapshot.
        /// </summary>
        /// <param name="ruleCode">The stable code that identifies the rule family.</param>
        /// <param name="version">The version that identifies the rule catalog entry.</param>
        /// <param name="suffix">The unique suffix used in descriptive fields.</param>
        /// <returns>A rule definition with deterministic catalog properties.</returns>
        private static RuleDefinition CreateRuleDefinition(string ruleCode, string version, string suffix)
        {
            // Rule definitions are global catalog data and deliberately do not carry snapshot stable keys.
            return new RuleDefinition(
                ruleCode,
                $"Layering rule {suffix}",
                RuleCategory.ArchitectureLayering,
                FindingSeverity.High,
                FindingStatus.Open,
                true,
                version,
                "Detects invalid architecture layering.",
                "{\"type\":\"layering\"}",
                new[] { "https://example.invalid/rules/ARCHON001" },
                true,
                "platform",
                GraphMetadata.From(new Dictionary<string, object?> { ["testSuffix"] = suffix }));
        }

        /// <summary>
        /// Creates a finding record for a persistence test snapshot.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the finding.</param>
        /// <param name="findingStableKey">The stable key that identifies the finding within the snapshot.</param>
        /// <param name="ruleCode">The rule code referenced by the finding.</param>
        /// <param name="ruleVersion">The rule version referenced by the finding.</param>
        /// <param name="nodeStableKey">The primary node stable key associated with the finding.</param>
        /// <param name="evidenceStableKey">The primary evidence stable key associated with the finding.</param>
        /// <param name="suffix">The unique suffix used in fingerprints.</param>
        /// <returns>A finding record with rule, node, evidence, suppression, and fingerprint data populated.</returns>
        private static FindingRecord CreateFinding(StableKey snapshotStableKey, StableKey findingStableKey, string ruleCode, string ruleVersion, StableKey nodeStableKey, StableKey evidenceStableKey, string suffix)
        {
            // The finding references catalog, node, and evidence identities rather than Neo4j internal IDs.
            return new FindingRecord(
                snapshotStableKey,
                findingStableKey,
                ruleCode,
                ruleVersion,
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
                new Fingerprint($"sha256:finding-{suffix}"));
        }

        /// <summary>
        /// Creates a metric record for a persistence test snapshot.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the metric.</param>
        /// <param name="metricStableKey">The stable key that identifies the metric within the snapshot.</param>
        /// <param name="nodeStableKey">The optional node stable key targeted by the metric.</param>
        /// <param name="edgeStableKey">The optional relationship stable key targeted by the metric.</param>
        /// <param name="evidenceStableKey">The optional primary evidence stable key associated with the metric.</param>
        /// <param name="suffix">The unique suffix used in fingerprints.</param>
        /// <returns>A metric record with deterministic value fields populated.</returns>
        private static MetricRecord CreateMetric(StableKey snapshotStableKey, StableKey metricStableKey, StableKey nodeStableKey, StableKey edgeStableKey, StableKey evidenceStableKey, string suffix)
        {
            // The metric carries both numeric and textual values because Work Item 7 requires both value shapes to persist.
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
                new Fingerprint($"sha256:metric-{suffix}"));
        }

        /// <summary>
        /// Creates a generated summary for a persistence test snapshot.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the summary.</param>
        /// <param name="summaryStableKey">The stable key that identifies the summary within the snapshot.</param>
        /// <param name="targetStableKey">The stable key of the graph record described by the summary.</param>
        /// <param name="suffix">The unique suffix used in fingerprints.</param>
        /// <returns>A generated summary with deterministic content fields populated.</returns>
        private static GeneratedSummary CreateGeneratedSummary(StableKey snapshotStableKey, StableKey summaryStableKey, StableKey targetStableKey, string suffix)
        {
            // The summary targets an architecture node and stores Markdown content for later reporting and markdown-export packages.
            return new GeneratedSummary(
                snapshotStableKey,
                summaryStableKey,
                SummaryKind.Node,
                targetStableKey,
                "Markdown",
                "Application project summary",
                "The application project depends on Neo4j.",
                GraphMetadata.Empty,
                new Fingerprint($"sha256:summary-{suffix}"));
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
        /// Reads persisted relationship-node counts needed by Work Item 5 integration assertions.
        /// </summary>
        /// <param name="driver">The configured Neo4j driver used to query the database.</param>
        /// <returns>Counts for architecture relationship nodes and supporting relationship links.</returns>
        private static async Task<RelationshipGraphCounts> ReadRelationshipGraphCountsAsync(IDriver driver)
        {
            // The query separates endpoint links from evidence links so tests prove both traversal and support evidence behavior.
            await using IAsyncSession session = driver.AsyncSession(sessionBuilder => sessionBuilder.WithDatabase("neo4j"));
            IResultCursor cursor = await session.RunAsync(@"
CALL { MATCH (relationship:ArchonRelationship) RETURN count(relationship) AS relationships }
CALL { MATCH (:ArchonRelationship)-[source:RELATIONSHIP_SOURCE]->(:ArchonNode) RETURN count(source) AS sourceRelationships }
CALL { MATCH (:ArchonRelationship)-[target:RELATIONSHIP_TARGET]->(:ArchonNode) RETURN count(target) AS targetRelationships }
CALL { MATCH (:ArchonRelationship)-[supported:SUPPORTED_BY_EVIDENCE]->(:ArchonEvidence) RETURN count(supported) AS relationshipEvidenceRelationships }
RETURN relationships, sourceRelationships + targetRelationships AS endpointRelationships, relationshipEvidenceRelationships");
            IRecord record = await cursor.SingleAsync();
            return new RelationshipGraphCounts(
                record["relationships"].As<long>(),
                record["endpointRelationships"].As<long>(),
                record["relationshipEvidenceRelationships"].As<long>());
        }

        /// <summary>
        /// Reads the fingerprint for a persisted architecture relationship node by stable key.
        /// </summary>
        /// <param name="driver">The configured Neo4j driver used to query the database.</param>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the relationship.</param>
        /// <param name="relationshipStableKey">The stable key of the relationship to look up.</param>
        /// <returns>The relationship fingerprint when found; otherwise, <see langword="null" />.</returns>
        private static async Task<string?> ReadRelationshipFingerprintAsync(IDriver driver, string snapshotStableKey, string relationshipStableKey)
        {
            // The lookup uses the same snapshot-scoped stable-key identity that the writer uses for relationship-node merge semantics.
            await using IAsyncSession session = driver.AsyncSession(sessionBuilder => sessionBuilder.WithDatabase("neo4j"));
            IResultCursor cursor = await session.RunAsync(
                "MATCH (relationship:ArchonRelationship { snapshotStableKey: $snapshotStableKey, stableKey: $relationshipStableKey }) RETURN relationship.fingerprint AS fingerprint",
                new { snapshotStableKey, relationshipStableKey });
            IRecord? record = await cursor.SingleOrDefaultAsync();
            return record?["fingerprint"].As<string>();
        }

        /// <summary>
        /// Reads the target stable key reached by traversing a relationship node from a source node and edge kind.
        /// </summary>
        /// <param name="driver">The configured Neo4j driver used to query the database.</param>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the traversal.</param>
        /// <param name="sourceNodeStableKey">The source architecture node stable key.</param>
        /// <param name="edgeKind">The edge kind used to choose the relationship fact.</param>
        /// <returns>The target node stable key when a traversal match is found; otherwise, <see langword="null" />.</returns>
        private static async Task<string?> ReadTraversedTargetAsync(IDriver driver, string snapshotStableKey, string sourceNodeStableKey, string edgeKind)
        {
            // This traversal demonstrates the relationship-node model: source node <- relationship fact -> target node.
            await using IAsyncSession session = driver.AsyncSession(sessionBuilder => sessionBuilder.WithDatabase("neo4j"));
            IResultCursor cursor = await session.RunAsync(@"
MATCH (relationship:ArchonRelationship { snapshotStableKey: $snapshotStableKey, edgeKind: $edgeKind })-[:RELATIONSHIP_SOURCE]->(:ArchonNode { snapshotStableKey: $snapshotStableKey, stableKey: $sourceNodeStableKey })
MATCH (relationship)-[:RELATIONSHIP_TARGET]->(target:ArchonNode { snapshotStableKey: $snapshotStableKey })
RETURN target.stableKey AS targetStableKey",
                new { snapshotStableKey, sourceNodeStableKey, edgeKind });
            IRecord? record = await cursor.SingleOrDefaultAsync();
            return record?["targetStableKey"].As<string>();
        }

        /// <summary>
        /// Reads how many relationship nodes connect the same source and target pair in one snapshot.
        /// </summary>
        /// <param name="driver">The configured Neo4j driver used to query the database.</param>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the relationship facts.</param>
        /// <param name="sourceNodeStableKey">The source architecture node stable key.</param>
        /// <param name="targetNodeStableKey">The target architecture node stable key.</param>
        /// <returns>The number of same-endpoint relationship nodes.</returns>
        private static async Task<long> ReadSameEndpointRelationshipCountAsync(IDriver driver, string snapshotStableKey, string sourceNodeStableKey, string targetNodeStableKey)
        {
            // Counting relationship nodes proves parallel edge facts are not collapsed solely because endpoints match.
            await using IAsyncSession session = driver.AsyncSession(sessionBuilder => sessionBuilder.WithDatabase("neo4j"));
            IResultCursor cursor = await session.RunAsync(@"
MATCH (relationship:ArchonRelationship { snapshotStableKey: $snapshotStableKey })-[:RELATIONSHIP_SOURCE]->(:ArchonNode { snapshotStableKey: $snapshotStableKey, stableKey: $sourceNodeStableKey })
MATCH (relationship)-[:RELATIONSHIP_TARGET]->(:ArchonNode { snapshotStableKey: $snapshotStableKey, stableKey: $targetNodeStableKey })
RETURN count(relationship) AS relationshipCount",
                new { snapshotStableKey, sourceNodeStableKey, targetNodeStableKey });
            IRecord record = await cursor.SingleAsync();
            return record["relationshipCount"].As<long>();
        }

        /// <summary>
        /// Reads persisted rule and finding graph counts needed by Work Item 6 integration assertions.
        /// </summary>
        /// <param name="driver">The configured Neo4j driver used to query the database.</param>
        /// <returns>Counts for rule nodes, finding nodes, and finding support links.</returns>
        private static async Task<RuleFindingGraphCounts> ReadRuleFindingGraphCountsAsync(IDriver driver)
        {
            // The query separates finding links by relationship type and endpoint label to verify every Work Item 6 link shape.
            await using IAsyncSession session = driver.AsyncSession(sessionBuilder => sessionBuilder.WithDatabase("neo4j"));
            IResultCursor cursor = await session.RunAsync(@"
CALL { MATCH (rule:ArchonRule) RETURN count(rule) AS rules }
CALL { MATCH (finding:ArchonFinding) RETURN count(finding) AS findings }
CALL { MATCH (:ArchonFinding)-[classified:CLASSIFIED_BY_RULE]->(:ArchonRule) RETURN count(classified) AS findingRuleRelationships }
CALL { MATCH (:ArchonFinding)-[primaryNode:PRIMARY_NODE]->(:ArchonNode) RETURN count(primaryNode) AS findingNodeRelationships }
CALL { MATCH (:ArchonFinding)-[supported:SUPPORTED_BY_EVIDENCE]->(:ArchonEvidence) RETURN count(supported) AS findingEvidenceRelationships }
RETURN rules, findings, findingRuleRelationships, findingNodeRelationships, findingEvidenceRelationships");
            IRecord record = await cursor.SingleAsync();
            return new RuleFindingGraphCounts(
                record["rules"].As<long>(),
                record["findings"].As<long>(),
                record["findingRuleRelationships"].As<long>(),
                record["findingNodeRelationships"].As<long>(),
                record["findingEvidenceRelationships"].As<long>());
        }

        /// <summary>
        /// Reads normalized finding details by snapshot and finding stable key.
        /// </summary>
        /// <param name="driver">The configured Neo4j driver used to query the database.</param>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the finding.</param>
        /// <param name="findingStableKey">The stable key of the finding to look up.</param>
        /// <returns>The persisted finding details when present; otherwise, <see langword="null" />.</returns>
        private static async Task<PersistedFindingDetails?> ReadPersistedFindingDetailsAsync(IDriver driver, string snapshotStableKey, string findingStableKey)
        {
            // Details are read from first-class properties so the test verifies query-ready finding fields, not only graph counts.
            await using IAsyncSession session = driver.AsyncSession(sessionBuilder => sessionBuilder.WithDatabase("neo4j"));
            IResultCursor cursor = await session.RunAsync(@"
MATCH (finding:ArchonFinding { snapshotStableKey: $snapshotStableKey, stableKey: $findingStableKey })
RETURN finding.ruleCode AS ruleCode,
       finding.ruleVersion AS ruleVersion,
       finding.severity AS severity,
       finding.status AS status,
       finding.suppressionReason AS suppressionReason,
       finding.fingerprint AS fingerprint",
                new { snapshotStableKey, findingStableKey });
            IRecord? record = await cursor.SingleOrDefaultAsync();
            return record is null
                ? null
                : new PersistedFindingDetails(
                    record["ruleCode"].As<string>(),
                    record["ruleVersion"].As<string>(),
                    record["severity"].As<string>(),
                    record["status"].As<string>(),
                    record["suppressionReason"].As<string>(),
                    record["fingerprint"].As<string>());
        }

        /// <summary>
        /// Reads persisted metric and generated-summary graph counts needed by Work Item 7 integration assertions.
        /// </summary>
        /// <param name="driver">The configured Neo4j driver used to query the database.</param>
        /// <returns>Counts for metric nodes, generated-summary nodes, and their support links.</returns>
        private static async Task<MetricSummaryGraphCounts> ReadMetricSummaryGraphCountsAsync(IDriver driver)
        {
            // The query separates metric and summary link shapes to prove evidence, target, and snapshot traversal independently.
            await using IAsyncSession session = driver.AsyncSession(sessionBuilder => sessionBuilder.WithDatabase("neo4j"));
            IResultCursor cursor = await session.RunAsync(@"
CALL { MATCH (metric:ArchonMetric) RETURN count(metric) AS metrics }
CALL { MATCH (summary:ArchonGeneratedSummary) RETURN count(summary) AS generatedSummaries }
CALL { MATCH (:ArchonMetric)-[supported:SUPPORTED_BY_EVIDENCE]->(:ArchonEvidence) RETURN count(supported) AS metricEvidenceRelationships }
CALL { MATCH (:ArchonMetric)-[primaryNode:PRIMARY_NODE]->(:ArchonNode) RETURN count(primaryNode) AS metricNodeRelationships }
CALL { MATCH (:ArchonMetric)-[primaryRelationship:PRIMARY_RELATIONSHIP]->(:ArchonRelationship) RETURN count(primaryRelationship) AS metricRelationshipRelationships }
CALL { MATCH (:ArchonGeneratedSummary)-[summarizes:SUMMARIZES_SNAPSHOT]->(:ArchonSnapshot) RETURN count(summarizes) AS summarySnapshotRelationships }
CALL { MATCH (:ArchonGeneratedSummary)-[primaryNode:PRIMARY_NODE]->(:ArchonNode) RETURN count(primaryNode) AS summaryNodeRelationships }
CALL { MATCH (:ArchonGeneratedSummary)-[primaryRelationship:PRIMARY_RELATIONSHIP]->(:ArchonRelationship) RETURN count(primaryRelationship) AS summaryRelationshipRelationships }
RETURN metrics, generatedSummaries, metricEvidenceRelationships, metricNodeRelationships, metricRelationshipRelationships, summarySnapshotRelationships, summaryNodeRelationships, summaryRelationshipRelationships");
            IRecord record = await cursor.SingleAsync();
            return new MetricSummaryGraphCounts(
                record["metrics"].As<long>(),
                record["generatedSummaries"].As<long>(),
                record["metricEvidenceRelationships"].As<long>(),
                record["metricNodeRelationships"].As<long>(),
                record["metricRelationshipRelationships"].As<long>(),
                record["summarySnapshotRelationships"].As<long>(),
                record["summaryNodeRelationships"].As<long>(),
                record["summaryRelationshipRelationships"].As<long>());
        }

        /// <summary>
        /// Reads normalized metric details by snapshot and metric stable key.
        /// </summary>
        /// <param name="driver">The configured Neo4j driver used to query the database.</param>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the metric.</param>
        /// <param name="metricStableKey">The stable key of the metric to look up.</param>
        /// <returns>The persisted metric details when present; otherwise, <see langword="null" />.</returns>
        private static async Task<PersistedMetricDetails?> ReadPersistedMetricDetailsAsync(IDriver driver, string snapshotStableKey, string metricStableKey)
        {
            // Details are read from first-class metric properties so the test proves query-ready values rather than metadata parsing.
            await using IAsyncSession session = driver.AsyncSession(sessionBuilder => sessionBuilder.WithDatabase("neo4j"));
            IResultCursor cursor = await session.RunAsync(@"
MATCH (metric:ArchonMetric { snapshotStableKey: $snapshotStableKey, stableKey: $metricStableKey })
RETURN metric.metricKind AS metricKind,
       metric.scopeKind AS scopeKind,
       metric.numericValue AS numericValue,
       metric.unit AS unit,
       metric.fingerprint AS fingerprint",
                new { snapshotStableKey, metricStableKey });
            IRecord? record = await cursor.SingleOrDefaultAsync();
            return record is null
                ? null
                : new PersistedMetricDetails(
                    record["metricKind"].As<string>(),
                    record["scopeKind"].As<string>(),
                    record["numericValue"].As<decimal>(),
                    record["unit"].As<string>(),
                    record["fingerprint"].As<string>());
        }

        /// <summary>
        /// Reads normalized generated-summary details by snapshot and summary stable key.
        /// </summary>
        /// <param name="driver">The configured Neo4j driver used to query the database.</param>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the summary.</param>
        /// <param name="summaryStableKey">The stable key of the generated summary to look up.</param>
        /// <returns>The persisted generated-summary details when present; otherwise, <see langword="null" />.</returns>
        private static async Task<PersistedSummaryDetails?> ReadPersistedSummaryDetailsAsync(IDriver driver, string snapshotStableKey, string summaryStableKey)
        {
            // Details are read from first-class summary properties so content retrieval remains independent of Neo4j internal IDs.
            await using IAsyncSession session = driver.AsyncSession(sessionBuilder => sessionBuilder.WithDatabase("neo4j"));
            IResultCursor cursor = await session.RunAsync(@"
MATCH (generatedSummary:ArchonGeneratedSummary { snapshotStableKey: $snapshotStableKey, stableKey: $summaryStableKey })
RETURN generatedSummary.summaryKind AS summaryKind,
       generatedSummary.format AS format,
       generatedSummary.title AS title,
       generatedSummary.fingerprint AS fingerprint",
                new { snapshotStableKey, summaryStableKey });
            IRecord? record = await cursor.SingleOrDefaultAsync();
            return record is null
                ? null
                : new PersistedSummaryDetails(
                    record["summaryKind"].As<string>(),
                    record["format"].As<string>(),
                    record["title"].As<string>(),
                    record["fingerprint"].As<string>());
        }

        /// <summary>
        /// Reads persisted full mixed graph node counts needed by Work Item 8 assertions.
        /// </summary>
        /// <param name="driver">The configured Neo4j driver used to query the database.</param>
        /// <returns>Counts for every WP003 graph record category.</returns>
        private static async Task<FullMixedGraphCounts> ReadFullMixedGraphCountsAsync(IDriver driver)
        {
            // Count queries verify that every first-class label receives data in the same coordinated full mixed write.
            await using IAsyncSession session = driver.AsyncSession(sessionBuilder => sessionBuilder.WithDatabase("neo4j"));
            IResultCursor cursor = await session.RunAsync(@"
CALL { MATCH (repository:ArchonRepository) RETURN count(repository) AS repositories }
CALL { MATCH (solution:ArchonSolution) RETURN count(solution) AS solutions }
CALL { MATCH (snapshot:ArchonSnapshot) RETURN count(snapshot) AS snapshots }
CALL { MATCH (node:ArchonNode) RETURN count(node) AS nodes }
CALL { MATCH (relationship:ArchonRelationship) RETURN count(relationship) AS relationships }
CALL { MATCH (evidence:ArchonEvidence) RETURN count(evidence) AS evidence }
CALL { MATCH (rule:ArchonRule) RETURN count(rule) AS rules }
CALL { MATCH (finding:ArchonFinding) RETURN count(finding) AS findings }
CALL { MATCH (metric:ArchonMetric) RETURN count(metric) AS metrics }
CALL { MATCH (summary:ArchonGeneratedSummary) RETURN count(summary) AS summaries }
RETURN repositories, solutions, snapshots, nodes, relationships, evidence, rules, findings, metrics, summaries");
            IRecord record = await cursor.SingleAsync();
            return new FullMixedGraphCounts(
                record["repositories"].As<long>(),
                record["solutions"].As<long>(),
                record["snapshots"].As<long>(),
                record["nodes"].As<long>(),
                record["relationships"].As<long>(),
                record["evidence"].As<long>(),
                record["rules"].As<long>(),
                record["findings"].As<long>(),
                record["metrics"].As<long>(),
                record["summaries"].As<long>());
        }

        /// <summary>
        /// Reads representative stable-key and fingerprint lookups from the full mixed snapshot.
        /// </summary>
        /// <param name="driver">The configured Neo4j driver used to query the database.</param>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the lookup.</param>
        /// <returns>Representative fingerprints for node, relationship, metric, and generated-summary records.</returns>
        private static async Task<FullMixedStableLookups> ReadFullMixedStableLookupsAsync(IDriver driver, string snapshotStableKey)
        {
            // Fingerprint lookup by stable key proves indexes and normalized properties support later diff and query workflows.
            await using IAsyncSession session = driver.AsyncSession(sessionBuilder => sessionBuilder.WithDatabase("neo4j"));
            IResultCursor cursor = await session.RunAsync(@"
MATCH (project:ArchonNode { snapshotStableKey: $snapshotStableKey, stableKey: 'project://full-mixed/app' })
MATCH (relationship:ArchonRelationship { snapshotStableKey: $snapshotStableKey, stableKey: 'edge://full-mixed/project-uses-package' })
MATCH (metric:ArchonMetric { snapshotStableKey: $snapshotStableKey, stableKey: 'metric://full-mixed/dependency-count' })
MATCH (summary:ArchonGeneratedSummary { snapshotStableKey: $snapshotStableKey, stableKey: 'summary://full-mixed/relationship' })
RETURN project.fingerprint AS projectNodeFingerprint,
       relationship.fingerprint AS packageRelationshipFingerprint,
       metric.fingerprint AS dependencyMetricFingerprint,
       summary.fingerprint AS relationshipSummaryFingerprint",
                new { snapshotStableKey });
            IRecord record = await cursor.SingleAsync();
            return new FullMixedStableLookups(
                record["projectNodeFingerprint"].As<string>(),
                record["packageRelationshipFingerprint"].As<string>(),
                record["dependencyMetricFingerprint"].As<string>(),
                record["relationshipSummaryFingerprint"].As<string>());
        }

        /// <summary>
        /// Reads whether every required Work Item 8 supporting relationship can be traversed by stable keys.
        /// </summary>
        /// <param name="driver">The configured Neo4j driver used to query the database.</param>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the relationship paths.</param>
        /// <returns>Boolean flags indicating whether each required supporting relationship path exists.</returns>
        private static async Task<FullMixedSupportingRelationships> ReadFullMixedSupportingRelationshipsAsync(IDriver driver, string snapshotStableKey)
        {
            // EXISTS subqueries keep the assertion result compact while still proving every required link shape is queryable.
            await using IAsyncSession session = driver.AsyncSession(sessionBuilder => sessionBuilder.WithDatabase("neo4j"));
            IResultCursor cursor = await session.RunAsync(@"
RETURN EXISTS { MATCH (:ArchonSnapshot { stableKey: $snapshotStableKey })-[:INCLUDES_SOLUTION]->(:ArchonSolution { stableKey: 'solution://supporting-relationship' }) } AS snapshotIncludesSolution,
       EXISTS { MATCH (:ArchonNode { snapshotStableKey: $snapshotStableKey, stableKey: 'project://supporting-relationship/app' })-[:SUPPORTED_BY_EVIDENCE]->(:ArchonEvidence { snapshotStableKey: $snapshotStableKey, stableKey: 'evidence://supporting-relationship/project-file' }) } AS nodeHasEvidence,
       EXISTS { MATCH (:ArchonRelationship { snapshotStableKey: $snapshotStableKey, stableKey: 'edge://supporting-relationship/project-uses-package' })-[:SUPPORTED_BY_EVIDENCE]->(:ArchonEvidence { snapshotStableKey: $snapshotStableKey, stableKey: 'evidence://supporting-relationship/package-reference' }) } AS relationshipHasEvidence,
       EXISTS { MATCH (:ArchonFinding { snapshotStableKey: $snapshotStableKey, stableKey: 'finding://supporting-relationship/invalid-dependency' })-[:CLASSIFIED_BY_RULE]->(:ArchonRule { ruleCode: 'ARCHON001', ruleVersion: '1.0.0' }) } AS findingHasRule,
       EXISTS { MATCH (:ArchonFinding { snapshotStableKey: $snapshotStableKey, stableKey: 'finding://supporting-relationship/invalid-dependency' })-[:PRIMARY_NODE]->(:ArchonNode { snapshotStableKey: $snapshotStableKey, stableKey: 'project://supporting-relationship/app' }) } AS findingHasNode,
       EXISTS { MATCH (:ArchonFinding { snapshotStableKey: $snapshotStableKey, stableKey: 'finding://supporting-relationship/invalid-dependency' })-[:SUPPORTED_BY_EVIDENCE]->(:ArchonEvidence { snapshotStableKey: $snapshotStableKey, stableKey: 'evidence://supporting-relationship/project-file' }) } AS findingHasEvidence,
       EXISTS { MATCH (:ArchonMetric { snapshotStableKey: $snapshotStableKey, stableKey: 'metric://supporting-relationship/dependency-count' })-[:SUPPORTED_BY_EVIDENCE]->(:ArchonEvidence { snapshotStableKey: $snapshotStableKey, stableKey: 'evidence://supporting-relationship/package-reference' }) } AS metricHasEvidence,
       EXISTS { MATCH (:ArchonMetric { snapshotStableKey: $snapshotStableKey, stableKey: 'metric://supporting-relationship/dependency-count' })-[:PRIMARY_NODE]->(:ArchonNode { snapshotStableKey: $snapshotStableKey, stableKey: 'project://supporting-relationship/app' }) } AS metricHasNodeTarget,
       EXISTS { MATCH (:ArchonMetric { snapshotStableKey: $snapshotStableKey, stableKey: 'metric://supporting-relationship/dependency-count' })-[:PRIMARY_RELATIONSHIP]->(:ArchonRelationship { snapshotStableKey: $snapshotStableKey, stableKey: 'edge://supporting-relationship/project-uses-package' }) } AS metricHasRelationshipTarget,
       EXISTS { MATCH (:ArchonGeneratedSummary { snapshotStableKey: $snapshotStableKey, stableKey: 'summary://supporting-relationship/snapshot' })-[:SUMMARIZES_SNAPSHOT]->(:ArchonSnapshot { stableKey: $snapshotStableKey }) } AS summaryHasSnapshotTarget,
       EXISTS { MATCH (:ArchonGeneratedSummary { snapshotStableKey: $snapshotStableKey, stableKey: 'summary://supporting-relationship/project' })-[:PRIMARY_NODE]->(:ArchonNode { snapshotStableKey: $snapshotStableKey, stableKey: 'project://supporting-relationship/app' }) } AS summaryHasNodeTarget,
       EXISTS { MATCH (:ArchonGeneratedSummary { snapshotStableKey: $snapshotStableKey, stableKey: 'summary://supporting-relationship/relationship' })-[:PRIMARY_RELATIONSHIP]->(:ArchonRelationship { snapshotStableKey: $snapshotStableKey, stableKey: 'edge://supporting-relationship/project-uses-package' }) } AS summaryHasRelationshipTarget",
                new { snapshotStableKey });
            IRecord record = await cursor.SingleAsync();
            return new FullMixedSupportingRelationships(
                record["snapshotIncludesSolution"].As<bool>(),
                record["nodeHasEvidence"].As<bool>(),
                record["relationshipHasEvidence"].As<bool>(),
                record["findingHasRule"].As<bool>(),
                record["findingHasNode"].As<bool>(),
                record["findingHasEvidence"].As<bool>(),
                record["metricHasEvidence"].As<bool>(),
                record["metricHasNodeTarget"].As<bool>(),
                record["metricHasRelationshipTarget"].As<bool>(),
                record["summaryHasSnapshotTarget"].As<bool>(),
                record["summaryHasNodeTarget"].As<bool>(),
                record["summaryHasRelationshipTarget"].As<bool>());
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

        /// <summary>
        /// Captures relationship graph counts read from Neo4j.
        /// </summary>
        /// <param name="Relationships">The architecture relationship node count.</param>
        /// <param name="EndpointRelationships">The relationship-node endpoint link count.</param>
        /// <param name="RelationshipEvidenceRelationships">The relationship-node evidence link count.</param>
        private sealed record RelationshipGraphCounts(long Relationships, long EndpointRelationships, long RelationshipEvidenceRelationships);

        /// <summary>
        /// Captures rule and finding graph counts read from Neo4j.
        /// </summary>
        /// <param name="Rules">The rule catalog node count.</param>
        /// <param name="Findings">The finding node count.</param>
        /// <param name="FindingRuleRelationships">The finding-to-rule relationship count.</param>
        /// <param name="FindingNodeRelationships">The finding-to-primary-node relationship count.</param>
        /// <param name="FindingEvidenceRelationships">The finding-to-evidence relationship count.</param>
        private sealed record RuleFindingGraphCounts(long Rules, long Findings, long FindingRuleRelationships, long FindingNodeRelationships, long FindingEvidenceRelationships);

        /// <summary>
        /// Captures metric and generated-summary graph counts read from Neo4j.
        /// </summary>
        /// <param name="Metrics">The metric node count.</param>
        /// <param name="GeneratedSummaries">The generated-summary node count.</param>
        /// <param name="MetricEvidenceRelationships">The metric-to-evidence relationship count.</param>
        /// <param name="MetricNodeRelationships">The metric-to-primary-node relationship count.</param>
        /// <param name="MetricRelationshipRelationships">The metric-to-primary-relationship relationship count.</param>
        /// <param name="SummarySnapshotRelationships">The generated-summary-to-snapshot relationship count.</param>
        /// <param name="SummaryNodeRelationships">The generated-summary-to-primary-node relationship count.</param>
        /// <param name="SummaryRelationshipRelationships">The generated-summary-to-primary-relationship relationship count.</param>
        private sealed record MetricSummaryGraphCounts(long Metrics, long GeneratedSummaries, long MetricEvidenceRelationships, long MetricNodeRelationships, long MetricRelationshipRelationships, long SummarySnapshotRelationships, long SummaryNodeRelationships, long SummaryRelationshipRelationships);

        /// <summary>
        /// Captures persisted finding properties read from Neo4j.
        /// </summary>
        /// <param name="RuleCode">The rule code stored on the finding.</param>
        /// <param name="RuleVersion">The rule version stored on the finding.</param>
        /// <param name="Severity">The severity stored on the finding.</param>
        /// <param name="Status">The lifecycle status stored on the finding.</param>
        /// <param name="SuppressionReason">The suppression reason stored on the finding.</param>
        /// <param name="Fingerprint">The deterministic finding fingerprint.</param>
        private sealed record PersistedFindingDetails(string RuleCode, string RuleVersion, string Severity, string Status, string SuppressionReason, string Fingerprint);

        /// <summary>
        /// Captures persisted metric properties read from Neo4j.
        /// </summary>
        /// <param name="MetricKind">The metric kind stored on the metric.</param>
        /// <param name="ScopeKind">The scope kind stored on the metric.</param>
        /// <param name="NumericValue">The numeric value stored on the metric.</param>
        /// <param name="Unit">The metric unit stored on the metric.</param>
        /// <param name="Fingerprint">The deterministic metric fingerprint.</param>
        private sealed record PersistedMetricDetails(string MetricKind, string ScopeKind, decimal NumericValue, string Unit, string Fingerprint);

        /// <summary>
        /// Captures persisted generated-summary properties read from Neo4j.
        /// </summary>
        /// <param name="SummaryKind">The generated-summary kind stored on the summary.</param>
        /// <param name="Format">The content format stored on the summary.</param>
        /// <param name="Title">The title stored on the summary.</param>
        /// <param name="Fingerprint">The deterministic generated-summary fingerprint.</param>
        private sealed record PersistedSummaryDetails(string SummaryKind, string Format, string Title, string Fingerprint);

        /// <summary>
        /// Captures full mixed graph record counts read from Neo4j.
        /// </summary>
        /// <param name="Repositories">The repository node count.</param>
        /// <param name="Solutions">The solution node count.</param>
        /// <param name="Snapshots">The snapshot node count.</param>
        /// <param name="Nodes">The architecture node count.</param>
        /// <param name="Relationships">The architecture relationship-node count.</param>
        /// <param name="Evidence">The evidence node count.</param>
        /// <param name="Rules">The rule catalog node count.</param>
        /// <param name="Findings">The finding node count.</param>
        /// <param name="Metrics">The metric node count.</param>
        /// <param name="Summaries">The generated-summary node count.</param>
        private sealed record FullMixedGraphCounts(long Repositories, long Solutions, long Snapshots, long Nodes, long Relationships, long Evidence, long Rules, long Findings, long Metrics, long Summaries);

        /// <summary>
        /// Captures representative full mixed stable-key lookup results.
        /// </summary>
        /// <param name="ProjectNodeFingerprint">The project node fingerprint read by snapshot-scoped stable key.</param>
        /// <param name="PackageRelationshipFingerprint">The package relationship fingerprint read by snapshot-scoped stable key.</param>
        /// <param name="DependencyMetricFingerprint">The dependency metric fingerprint read by snapshot-scoped stable key.</param>
        /// <param name="RelationshipSummaryFingerprint">The relationship summary fingerprint read by snapshot-scoped stable key.</param>
        private sealed record FullMixedStableLookups(string ProjectNodeFingerprint, string PackageRelationshipFingerprint, string DependencyMetricFingerprint, string RelationshipSummaryFingerprint);

        /// <summary>
        /// Captures the existence of every full mixed supporting relationship path required by Work Item 8.
        /// </summary>
        /// <param name="SnapshotIncludesSolution">A value indicating whether the snapshot-to-solution relationship exists.</param>
        /// <param name="NodeHasEvidence">A value indicating whether an architecture node links to evidence.</param>
        /// <param name="RelationshipHasEvidence">A value indicating whether an architecture relationship node links to evidence.</param>
        /// <param name="FindingHasRule">A value indicating whether a finding links to its rule version.</param>
        /// <param name="FindingHasNode">A value indicating whether a finding links to its primary node.</param>
        /// <param name="FindingHasEvidence">A value indicating whether a finding links to evidence.</param>
        /// <param name="MetricHasEvidence">A value indicating whether a metric links to evidence.</param>
        /// <param name="MetricHasNodeTarget">A value indicating whether a metric links to a node target.</param>
        /// <param name="MetricHasRelationshipTarget">A value indicating whether a metric links to a relationship-node target.</param>
        /// <param name="SummaryHasSnapshotTarget">A value indicating whether a generated summary links to its owning snapshot.</param>
        /// <param name="SummaryHasNodeTarget">A value indicating whether a generated summary links to a node target.</param>
        /// <param name="SummaryHasRelationshipTarget">A value indicating whether a generated summary links to a relationship-node target.</param>
        private sealed record FullMixedSupportingRelationships(bool SnapshotIncludesSolution, bool NodeHasEvidence, bool RelationshipHasEvidence, bool FindingHasRule, bool FindingHasNode, bool FindingHasEvidence, bool MetricHasEvidence, bool MetricHasNodeTarget, bool MetricHasRelationshipTarget, bool SummaryHasSnapshotTarget, bool SummaryHasNodeTarget, bool SummaryHasRelationshipTarget);
    }
}
