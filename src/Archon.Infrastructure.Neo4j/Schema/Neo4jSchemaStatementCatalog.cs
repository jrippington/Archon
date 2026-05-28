namespace Archon.Infrastructure.Neo4j.Schema
{
    /// <summary>
    /// Provides the ordered, idempotent Cypher catalog that initializes the Archon Neo4j graph schema.
    /// </summary>
    /// <remarks>
    /// The catalog is explicit rather than generated at runtime so constraint and index names remain visible in code review,
    /// documentation, tests, and operational troubleshooting. Dynamic labels and relationship types are deliberately avoided.
    /// </remarks>
    public sealed class Neo4jSchemaStatementCatalog
    {
        private readonly IReadOnlyList<Neo4jSchemaStatement> _statements;

        /// <summary>
        /// Initializes a new instance of the <see cref="Neo4jSchemaStatementCatalog"/> class.
        /// </summary>
        public Neo4jSchemaStatementCatalog()
        {
            // Ordering puts uniqueness constraints first so later persistence slices get deterministic identity guarantees before
            // secondary lookup indexes are created.
            _statements = CreateStatements();
        }

        /// <summary>
        /// Gets the ordered schema statements that should be executed during initialization.
        /// </summary>
        /// <returns>The ordered immutable list of schema statements.</returns>
        public IReadOnlyList<Neo4jSchemaStatement> GetStatements()
        {
            // Returning the immutable list prevents callers from changing catalog order or skipping required schema objects.
            return _statements;
        }

        /// <summary>
        /// Creates the full ordered schema statement list.
        /// </summary>
        /// <returns>An ordered list of idempotent constraint and index statements.</returns>
        private static IReadOnlyList<Neo4jSchemaStatement> CreateStatements()
        {
            // Each statement uses IF NOT EXISTS so initialization can safely run against a clean database or one already initialized.
            List<Neo4jSchemaStatement> statements = new()
            {
                Constraint(Neo4jSchemaNames.Constraints.RepositoryStableKeyUnique, Neo4jSchemaNames.Labels.Repository, Neo4jSchemaNames.Properties.StableKey),
                Constraint(Neo4jSchemaNames.Constraints.SolutionStableKeyUnique, Neo4jSchemaNames.Labels.Solution, Neo4jSchemaNames.Properties.StableKey),
                Constraint(Neo4jSchemaNames.Constraints.SnapshotStableKeyUnique, Neo4jSchemaNames.Labels.Snapshot, Neo4jSchemaNames.Properties.StableKey),
                CompositeConstraint(Neo4jSchemaNames.Constraints.NodeSnapshotStableKeyUnique, Neo4jSchemaNames.Labels.Node, Neo4jSchemaNames.Properties.SnapshotStableKey, Neo4jSchemaNames.Properties.StableKey),
                CompositeConstraint(Neo4jSchemaNames.Constraints.RelationshipSnapshotStableKeyUnique, Neo4jSchemaNames.Labels.Relationship, Neo4jSchemaNames.Properties.SnapshotStableKey, Neo4jSchemaNames.Properties.StableKey),
                CompositeConstraint(Neo4jSchemaNames.Constraints.EvidenceSnapshotStableKeyUnique, Neo4jSchemaNames.Labels.Evidence, Neo4jSchemaNames.Properties.SnapshotStableKey, Neo4jSchemaNames.Properties.StableKey),
                CompositeConstraint(Neo4jSchemaNames.Constraints.RuleCodeVersionUnique, Neo4jSchemaNames.Labels.Rule, Neo4jSchemaNames.Properties.RuleCode, Neo4jSchemaNames.Properties.RuleVersion),
                CompositeConstraint(Neo4jSchemaNames.Constraints.FindingSnapshotStableKeyUnique, Neo4jSchemaNames.Labels.Finding, Neo4jSchemaNames.Properties.SnapshotStableKey, Neo4jSchemaNames.Properties.StableKey),
                CompositeConstraint(Neo4jSchemaNames.Constraints.MetricSnapshotStableKeyUnique, Neo4jSchemaNames.Labels.Metric, Neo4jSchemaNames.Properties.SnapshotStableKey, Neo4jSchemaNames.Properties.StableKey),
                CompositeConstraint(Neo4jSchemaNames.Constraints.GeneratedSummarySnapshotStableKeyUnique, Neo4jSchemaNames.Labels.GeneratedSummary, Neo4jSchemaNames.Properties.SnapshotStableKey, Neo4jSchemaNames.Properties.StableKey),
                Constraint(Neo4jSchemaNames.Constraints.ExtractionRunRunIdUnique, Neo4jSchemaNames.Labels.ExtractionRun, Neo4jSchemaNames.Properties.RunId),
                Constraint(Neo4jSchemaNames.Constraints.ExtractionRunRequestRunIdUnique, Neo4jSchemaNames.Labels.ExtractionRunRequest, Neo4jSchemaNames.Properties.RunId),
                Index(Neo4jSchemaNames.Indexes.SolutionRepositoryStableKey, Neo4jSchemaNames.Labels.Solution, Neo4jSchemaNames.Properties.RepositoryStableKey),
                Index(Neo4jSchemaNames.Indexes.SnapshotRepositoryStableKey, Neo4jSchemaNames.Labels.Snapshot, Neo4jSchemaNames.Properties.RepositoryStableKey),
                Index(Neo4jSchemaNames.Indexes.SnapshotStatus, Neo4jSchemaNames.Labels.Snapshot, Neo4jSchemaNames.Properties.Status),
                Index(Neo4jSchemaNames.Indexes.SnapshotFingerprint, Neo4jSchemaNames.Labels.Snapshot, Neo4jSchemaNames.Properties.Fingerprint),
                Index(Neo4jSchemaNames.Indexes.NodeSnapshot, Neo4jSchemaNames.Labels.Node, Neo4jSchemaNames.Properties.SnapshotStableKey),
                Index(Neo4jSchemaNames.Indexes.NodeKind, Neo4jSchemaNames.Labels.Node, Neo4jSchemaNames.Properties.NodeKind),
                Index(Neo4jSchemaNames.Indexes.NodeKnowledgeKind, Neo4jSchemaNames.Labels.Node, Neo4jSchemaNames.Properties.KnowledgeKind),
                Index(Neo4jSchemaNames.Indexes.NodeConfidence, Neo4jSchemaNames.Labels.Node, Neo4jSchemaNames.Properties.Confidence),
                Index(Neo4jSchemaNames.Indexes.NodeFingerprint, Neo4jSchemaNames.Labels.Node, Neo4jSchemaNames.Properties.Fingerprint),
                Index(Neo4jSchemaNames.Indexes.RelationshipSnapshot, Neo4jSchemaNames.Labels.Relationship, Neo4jSchemaNames.Properties.SnapshotStableKey),
                Index(Neo4jSchemaNames.Indexes.RelationshipEdgeKind, Neo4jSchemaNames.Labels.Relationship, Neo4jSchemaNames.Properties.EdgeKind),
                Index(Neo4jSchemaNames.Indexes.RelationshipKnowledgeKind, Neo4jSchemaNames.Labels.Relationship, Neo4jSchemaNames.Properties.KnowledgeKind),
                Index(Neo4jSchemaNames.Indexes.RelationshipConfidence, Neo4jSchemaNames.Labels.Relationship, Neo4jSchemaNames.Properties.Confidence),
                Index(Neo4jSchemaNames.Indexes.RelationshipFingerprint, Neo4jSchemaNames.Labels.Relationship, Neo4jSchemaNames.Properties.Fingerprint),
                Index(Neo4jSchemaNames.Indexes.EvidenceSnapshot, Neo4jSchemaNames.Labels.Evidence, Neo4jSchemaNames.Properties.SnapshotStableKey),
                Index(Neo4jSchemaNames.Indexes.EvidenceKind, Neo4jSchemaNames.Labels.Evidence, Neo4jSchemaNames.Properties.EvidenceKind),
                Index(Neo4jSchemaNames.Indexes.EvidenceKnowledgeKind, Neo4jSchemaNames.Labels.Evidence, Neo4jSchemaNames.Properties.KnowledgeKind),
                Index(Neo4jSchemaNames.Indexes.EvidenceConfidence, Neo4jSchemaNames.Labels.Evidence, Neo4jSchemaNames.Properties.Confidence),
                Index(Neo4jSchemaNames.Indexes.EvidenceFingerprint, Neo4jSchemaNames.Labels.Evidence, Neo4jSchemaNames.Properties.Fingerprint),
                Index(Neo4jSchemaNames.Indexes.RuleCode, Neo4jSchemaNames.Labels.Rule, Neo4jSchemaNames.Properties.RuleCode),
                Index(Neo4jSchemaNames.Indexes.RuleCategory, Neo4jSchemaNames.Labels.Rule, Neo4jSchemaNames.Properties.Category),
                Index(Neo4jSchemaNames.Indexes.FindingSnapshot, Neo4jSchemaNames.Labels.Finding, Neo4jSchemaNames.Properties.SnapshotStableKey),
                Index(Neo4jSchemaNames.Indexes.FindingSeverity, Neo4jSchemaNames.Labels.Finding, Neo4jSchemaNames.Properties.Severity),
                Index(Neo4jSchemaNames.Indexes.FindingStatus, Neo4jSchemaNames.Labels.Finding, Neo4jSchemaNames.Properties.Status),
                Index(Neo4jSchemaNames.Indexes.FindingKnowledgeKind, Neo4jSchemaNames.Labels.Finding, Neo4jSchemaNames.Properties.KnowledgeKind),
                Index(Neo4jSchemaNames.Indexes.FindingConfidence, Neo4jSchemaNames.Labels.Finding, Neo4jSchemaNames.Properties.Confidence),
                Index(Neo4jSchemaNames.Indexes.FindingFingerprint, Neo4jSchemaNames.Labels.Finding, Neo4jSchemaNames.Properties.Fingerprint),
                Index(Neo4jSchemaNames.Indexes.MetricSnapshot, Neo4jSchemaNames.Labels.Metric, Neo4jSchemaNames.Properties.SnapshotStableKey),
                Index(Neo4jSchemaNames.Indexes.MetricKind, Neo4jSchemaNames.Labels.Metric, Neo4jSchemaNames.Properties.MetricKind),
                Index(Neo4jSchemaNames.Indexes.MetricScopeKind, Neo4jSchemaNames.Labels.Metric, Neo4jSchemaNames.Properties.ScopeKind),
                Index(Neo4jSchemaNames.Indexes.MetricFingerprint, Neo4jSchemaNames.Labels.Metric, Neo4jSchemaNames.Properties.Fingerprint),
                Index(Neo4jSchemaNames.Indexes.GeneratedSummarySnapshot, Neo4jSchemaNames.Labels.GeneratedSummary, Neo4jSchemaNames.Properties.SnapshotStableKey),
                Index(Neo4jSchemaNames.Indexes.GeneratedSummaryKind, Neo4jSchemaNames.Labels.GeneratedSummary, Neo4jSchemaNames.Properties.SummaryKind),
                Index(Neo4jSchemaNames.Indexes.GeneratedSummaryFingerprint, Neo4jSchemaNames.Labels.GeneratedSummary, Neo4jSchemaNames.Properties.Fingerprint),
                Index(Neo4jSchemaNames.Indexes.ExtractionRunStatus, Neo4jSchemaNames.Labels.ExtractionRun, Neo4jSchemaNames.Properties.Status),
                Index(Neo4jSchemaNames.Indexes.ExtractionRunStartedUtc, Neo4jSchemaNames.Labels.ExtractionRun, Neo4jSchemaNames.Properties.StartedUtc),
                Index(Neo4jSchemaNames.Indexes.ExtractionRunSnapshotStableKey, Neo4jSchemaNames.Labels.ExtractionRun, Neo4jSchemaNames.Properties.SnapshotStableKey)
            };

            return statements;
        }

        /// <summary>
        /// Creates an idempotent single-property uniqueness constraint statement.
        /// </summary>
        /// <param name="name">The stable constraint name.</param>
        /// <param name="label">The whitelisted node label the constraint targets.</param>
        /// <param name="property">The whitelisted property the constraint targets.</param>
        /// <returns>A schema statement that creates the requested uniqueness constraint.</returns>
        private static Neo4jSchemaStatement Constraint(string name, string label, string property)
        {
            // Constraint names, labels, and properties come from Neo4jSchemaNames constants only, avoiding untrusted dynamic Cypher.
            return new Neo4jSchemaStatement(name, "constraint", $"CREATE CONSTRAINT {name} IF NOT EXISTS FOR (n:{label}) REQUIRE n.{property} IS UNIQUE");
        }

        /// <summary>
        /// Creates an idempotent two-property uniqueness constraint statement.
        /// </summary>
        /// <param name="name">The stable constraint name.</param>
        /// <param name="label">The whitelisted node label the constraint targets.</param>
        /// <param name="firstProperty">The first whitelisted property in the composite key.</param>
        /// <param name="secondProperty">The second whitelisted property in the composite key.</param>
        /// <returns>A schema statement that creates the requested composite uniqueness constraint.</returns>
        private static Neo4jSchemaStatement CompositeConstraint(string name, string label, string firstProperty, string secondProperty)
        {
            // Composite constraints enforce snapshot-scoped identity for records whose stable keys are only unique within a snapshot.
            return new Neo4jSchemaStatement(name, "constraint", $"CREATE CONSTRAINT {name} IF NOT EXISTS FOR (n:{label}) REQUIRE (n.{firstProperty}, n.{secondProperty}) IS UNIQUE");
        }

        /// <summary>
        /// Creates an idempotent single-property range index statement.
        /// </summary>
        /// <param name="name">The stable index name.</param>
        /// <param name="label">The whitelisted node label the index targets.</param>
        /// <param name="property">The whitelisted property the index targets.</param>
        /// <returns>A schema statement that creates the requested lookup index.</returns>
        private static Neo4jSchemaStatement Index(string name, string label, string property)
        {
            // Indexes support follow-on query packages for lookup, filtering, diff, and traversal entry points.
            return new Neo4jSchemaStatement(name, "index", $"CREATE INDEX {name} IF NOT EXISTS FOR (n:{label}) ON (n.{property})");
        }
    }
}
