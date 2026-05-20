using Archon.Infrastructure.Neo4j.Schema;
using Xunit;

namespace Archon.Infrastructure.Neo4j.Tests.Schema
{
    /// <summary>
    /// Verifies stable schema names that later Cypher, tests, documentation, and operations depend on.
    /// </summary>
    public sealed class GraphSchemaNamesTests
    {
        /// <summary>
        /// Confirms the architecture relationship-node label is documented as a first-class schema label.
        /// </summary>
        [Fact]
        public void RelationshipNodeLabelIsStable()
        {
            // Work Item 2 chooses a relationship-node pattern so architecture edges can have stable keys, fingerprints, metadata,
            // and evidence links that are directly constrained and indexed.
            Assert.Equal("ArchonRelationship", Neo4jSchemaNames.Labels.Relationship);
        }

        /// <summary>
        /// Confirms the finding relationship names used by rules and findings persistence are stable.
        /// </summary>
        [Fact]
        public void FindingRelationshipNamesAreStable()
        {
            // Work Item 6 creates explicit finding links so later query and MCP packages do not infer associations from string fields only.
            Assert.Equal("CLASSIFIED_BY_RULE", Neo4jSchemaNames.Relationships.ClassifiedByRule);
            Assert.Equal("PRIMARY_NODE", Neo4jSchemaNames.Relationships.PrimaryNode);
            Assert.Equal("SUPPORTED_BY_EVIDENCE", Neo4jSchemaNames.Relationships.SupportedByEvidence);
        }

        /// <summary>
        /// Confirms the metric and generated-summary relationship names used by Work Item 7 persistence are stable.
        /// </summary>
        [Fact]
        public void MetricAndGeneratedSummaryRelationshipNamesAreStable()
        {
            // Metrics and summaries use stable relationship names so later query, MCP, report, and diff packages can traverse targets.
            Assert.Equal("PRIMARY_RELATIONSHIP", Neo4jSchemaNames.Relationships.PrimaryRelationship);
            Assert.Equal("SUMMARIZES_SNAPSHOT", Neo4jSchemaNames.Relationships.SummarizesSnapshot);
        }

        /// <summary>
        /// Confirms every schema object name follows the operational naming convention used by Archon.
        /// </summary>
        [Fact]
        public void ConstraintAndIndexNamesUseArchonPrefix()
        {
            // Stable prefixes make Neo4j Browser and operational troubleshooting output easy to distinguish from unrelated schema.
            IEnumerable<string> names = new[]
            {
                Neo4jSchemaNames.Constraints.RepositoryStableKeyUnique,
                Neo4jSchemaNames.Constraints.RelationshipSnapshotStableKeyUnique,
                Neo4jSchemaNames.Constraints.RuleCodeVersionUnique,
                Neo4jSchemaNames.Constraints.FindingSnapshotStableKeyUnique,
                Neo4jSchemaNames.Indexes.NodeKind,
                Neo4jSchemaNames.Indexes.RelationshipEdgeKind,
                Neo4jSchemaNames.Indexes.RuleCode,
                Neo4jSchemaNames.Indexes.FindingSeverity,
                Neo4jSchemaNames.Indexes.MetricKind,
                Neo4jSchemaNames.Indexes.GeneratedSummaryFingerprint
            };

            Assert.All(names, name => Assert.StartsWith("archon_", name, StringComparison.Ordinal));
        }
    }
}
