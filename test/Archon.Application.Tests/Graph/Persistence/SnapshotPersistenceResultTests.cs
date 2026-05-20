using Archon.Application.Graph.Persistence;
using Xunit;

namespace Archon.Application.Tests.Graph.Persistence
{
    /// <summary>
    /// Verifies application-layer snapshot persistence result contracts remain deterministic and infrastructure-neutral.
    /// </summary>
    public sealed class SnapshotPersistenceResultTests
    {
        /// <summary>
        /// Confirms count construction normalizes negative values so failed adapters cannot expose impossible metrics.
        /// </summary>
        [Fact]
        public void CountsNormalizeNegativeValues()
        {
            // Negative counts are defensive input from adapter error paths; public result contracts should always expose zero or positive counts.
            SnapshotPersistenceCounts counts = new(-1, -2, -3, -4, -5, -6, -7, -8, -9, -10);

            Assert.Equal(0, counts.Repositories);
            Assert.Equal(0, counts.Solutions);
            Assert.Equal(0, counts.Snapshots);
            Assert.Equal(0, counts.Nodes);
            Assert.Equal(0, counts.Evidence);
            Assert.Equal(0, counts.ArchitectureRelationships);
            Assert.Equal(0, counts.SnapshotSolutionRelationships);
            Assert.Equal(0, counts.NodeEvidenceRelationships);
            Assert.Equal(0, counts.RelationshipEndpointRelationships);
            Assert.Equal(0, counts.RelationshipEvidenceRelationships);
        }

        /// <summary>
        /// Confirms successful results preserve snapshot identity, counts, warnings, and an empty fatal-error list.
        /// </summary>
        [Fact]
        public void SuccessCreatesSuccessfulResultWithCountsAndWarnings()
        {
            // Successful persistence should expose aggregate counts without leaking Neo4j transaction summary details.
            SnapshotPersistenceCounts counts = new(1, 1, 1, 2, 1, 2, 1, 2, 4, 2);
            PersistenceWarning warning = new(PersistenceStage.SnapshotPersistence, "OutOfScopeSectionsIgnored", "Only minimal snapshot sections were persisted.");

            SnapshotPersistenceResult result = SnapshotPersistenceResult.Success(" snapshot://example ", counts, new[] { warning });

            Assert.True(result.Succeeded);
            Assert.Equal("snapshot://example", result.SnapshotStableKey);
            Assert.Same(counts, result.Counts);
            Assert.Single(result.Warnings);
            Assert.Empty(result.Errors);
        }

        /// <summary>
        /// Confirms failed results carry safe error details and do not report partial persistence counts as completed work.
        /// </summary>
        [Fact]
        public void FailureCreatesFailedResultWithEmptyCountsAndSafeError()
        {
            // A failed persistence operation must not make partial transaction work look like completed snapshot output.
            PersistenceError error = new(PersistenceStage.SnapshotPersistence, "MissingSnapshotHeader", "Snapshot persistence requires a snapshot header.");

            SnapshotPersistenceResult result = SnapshotPersistenceResult.Failure("snapshot://example", error);

            Assert.False(result.Succeeded);
            Assert.Equal("snapshot://example", result.SnapshotStableKey);
            Assert.Same(SnapshotPersistenceCounts.Empty, result.Counts);
            Assert.Empty(result.Warnings);
            Assert.Equal("MissingSnapshotHeader", Assert.Single(result.Errors).Code);
        }
    }
}
