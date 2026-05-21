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
                Neo4jSchemaNames.Indexes.NodeKind,
                Neo4jSchemaNames.Indexes.RelationshipEdgeKind,
                Neo4jSchemaNames.Indexes.GeneratedSummaryFingerprint
            };

            Assert.All(names, name => Assert.StartsWith("archon_", name, StringComparison.Ordinal));
        }
    }
}
