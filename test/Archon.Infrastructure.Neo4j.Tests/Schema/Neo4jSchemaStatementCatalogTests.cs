using Archon.Infrastructure.Neo4j.Schema;
using Xunit;

namespace Archon.Infrastructure.Neo4j.Tests.Schema
{
    /// <summary>
    /// Verifies the schema statement catalog contains all required constraints and indexes for Work Item 2.
    /// </summary>
    public sealed class GraphSchemaStatementCatalogTests
    {
        /// <summary>
        /// Confirms the catalog includes every required uniqueness constraint with idempotent Cypher.
        /// </summary>
        [Fact]
        public void GetStatementsIncludesRequiredConstraints()
        {
            // Constraint names are the operational contract for troubleshooting and schema introspection.
            IReadOnlyList<Neo4jSchemaStatement> statements = new Neo4jSchemaStatementCatalog().GetStatements();
            HashSet<string> names = statements.Select(statement => statement.Name).ToHashSet(StringComparer.Ordinal);

            Assert.Contains(Neo4jSchemaNames.Constraints.RepositoryStableKeyUnique, names);
            Assert.Contains(Neo4jSchemaNames.Constraints.SolutionStableKeyUnique, names);
            Assert.Contains(Neo4jSchemaNames.Constraints.SnapshotStableKeyUnique, names);
            Assert.Contains(Neo4jSchemaNames.Constraints.NodeSnapshotStableKeyUnique, names);
            Assert.Contains(Neo4jSchemaNames.Constraints.RelationshipSnapshotStableKeyUnique, names);
            Assert.Contains(Neo4jSchemaNames.Constraints.EvidenceSnapshotStableKeyUnique, names);
            Assert.Contains(Neo4jSchemaNames.Constraints.RuleCodeVersionUnique, names);
            Assert.Contains(Neo4jSchemaNames.Constraints.FindingSnapshotStableKeyUnique, names);
            Assert.Contains(Neo4jSchemaNames.Constraints.MetricSnapshotStableKeyUnique, names);
            Assert.Contains(Neo4jSchemaNames.Constraints.GeneratedSummarySnapshotStableKeyUnique, names);
            Assert.All(statements.Where(statement => statement.Kind == "constraint"), statement => Assert.Contains("IF NOT EXISTS", statement.Cypher, StringComparison.Ordinal));
        }

        /// <summary>
        /// Confirms the catalog includes lookup indexes for stable keys, snapshot scope, kind, status, confidence, knowledge kind, and fingerprints.
        /// </summary>
        [Fact]
        public void GetStatementsIncludesRequiredIndexes()
        {
            // Index coverage is intentionally broad because later query, MCP, diff, and report slices need stable entry points.
            IReadOnlyList<Neo4jSchemaStatement> statements = new Neo4jSchemaStatementCatalog().GetStatements();
            HashSet<string> names = statements.Select(statement => statement.Name).ToHashSet(StringComparer.Ordinal);

            Assert.Contains(Neo4jSchemaNames.Indexes.SnapshotFingerprint, names);
            Assert.Contains(Neo4jSchemaNames.Indexes.NodeSnapshot, names);
            Assert.Contains(Neo4jSchemaNames.Indexes.NodeKind, names);
            Assert.Contains(Neo4jSchemaNames.Indexes.NodeKnowledgeKind, names);
            Assert.Contains(Neo4jSchemaNames.Indexes.NodeConfidence, names);
            Assert.Contains(Neo4jSchemaNames.Indexes.NodeFingerprint, names);
            Assert.Contains(Neo4jSchemaNames.Indexes.RelationshipSnapshot, names);
            Assert.Contains(Neo4jSchemaNames.Indexes.RelationshipEdgeKind, names);
            Assert.Contains(Neo4jSchemaNames.Indexes.RelationshipFingerprint, names);
            Assert.Contains(Neo4jSchemaNames.Indexes.EvidenceKind, names);
            Assert.Contains(Neo4jSchemaNames.Indexes.FindingSeverity, names);
            Assert.Contains(Neo4jSchemaNames.Indexes.FindingStatus, names);
            Assert.Contains(Neo4jSchemaNames.Indexes.MetricKind, names);
            Assert.Contains(Neo4jSchemaNames.Indexes.GeneratedSummaryKind, names);
            Assert.All(statements.Where(statement => statement.Kind == "index"), statement => Assert.Contains("IF NOT EXISTS", statement.Cypher, StringComparison.Ordinal));
        }

        /// <summary>
        /// Confirms statement names are unique so the initializer logs and metadata checks remain unambiguous.
        /// </summary>
        [Fact]
        public void GetStatementsUsesUniqueNames()
        {
            // Duplicate schema object names would make Neo4j initialization ambiguous and could mask missing catalog entries.
            IReadOnlyList<Neo4jSchemaStatement> statements = new Neo4jSchemaStatementCatalog().GetStatements();

            Assert.Equal(statements.Count, statements.Select(statement => statement.Name).Distinct(StringComparer.Ordinal).Count());
        }
    }
}
