namespace Archon.Application.Graph.Persistence
{
    /// <summary>
    /// Captures persisted record and relationship counts for a snapshot persistence operation.
    /// </summary>
    /// <remarks>
    /// Counts are reported in application terms rather than database-specific counters. They help tests, logs, and future orchestration
    /// code understand what the writer attempted without depending on Neo4j summary objects.
    /// </remarks>
    public sealed record SnapshotPersistenceCounts
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SnapshotPersistenceCounts"/> record.
        /// </summary>
        /// <param name="repositories">The number of repository records persisted.</param>
        /// <param name="solutions">The number of solution records persisted.</param>
        /// <param name="snapshots">The number of snapshot header records persisted.</param>
        /// <param name="nodes">The number of architecture node records persisted.</param>
        /// <param name="evidence">The number of canonical evidence records persisted after snapshot-scoped deduplication.</param>
        /// <param name="architectureRelationships">The number of architecture relationship nodes persisted through the relationship-node pattern.</param>
        /// <param name="snapshotSolutionRelationships">The number of snapshot-to-solution relationships persisted.</param>
        /// <param name="nodeEvidenceRelationships">The number of node-to-evidence relationships persisted.</param>
        /// <param name="relationshipEndpointRelationships">The number of relationship-node endpoint relationships persisted to source and target nodes.</param>
        /// <param name="relationshipEvidenceRelationships">The number of relationship-node-to-evidence relationships persisted.</param>
        /// <param name="rules">The number of rule catalog records upserted by rule code and version.</param>
        /// <param name="findings">The number of snapshot-scoped finding records persisted.</param>
        /// <param name="findingRuleRelationships">The number of finding-to-rule-version relationships persisted.</param>
        /// <param name="findingNodeRelationships">The number of finding-to-primary-node relationships persisted.</param>
        /// <param name="findingEvidenceRelationships">The number of finding-to-evidence relationships persisted.</param>
        /// <param name="metrics">The number of snapshot-scoped metric records persisted.</param>
        /// <param name="metricEvidenceRelationships">The number of metric-to-evidence relationships persisted.</param>
        /// <param name="metricTargetRelationships">The number of metric-to-target relationships persisted.</param>
        /// <param name="generatedSummaries">The number of snapshot-scoped generated-summary records persisted.</param>
        /// <param name="summarySnapshotRelationships">The number of generated-summary-to-snapshot relationships persisted.</param>
        /// <param name="summaryTargetRelationships">The number of generated-summary-to-target relationships persisted.</param>
        public SnapshotPersistenceCounts(
            int repositories,
            int solutions,
            int snapshots,
            int nodes,
            int evidence,
            int architectureRelationships,
            int snapshotSolutionRelationships,
            int nodeEvidenceRelationships,
            int relationshipEndpointRelationships,
            int relationshipEvidenceRelationships,
            int rules = 0,
            int findings = 0,
            int findingRuleRelationships = 0,
            int findingNodeRelationships = 0,
            int findingEvidenceRelationships = 0,
            int metrics = 0,
            int metricEvidenceRelationships = 0,
            int metricTargetRelationships = 0,
            int generatedSummaries = 0,
            int summarySnapshotRelationships = 0,
            int summaryTargetRelationships = 0)
        {
            // Counts are normalized defensively so failed adapters cannot leak negative values into caller diagnostics.
            Repositories = Math.Max(0, repositories);
            Solutions = Math.Max(0, solutions);
            Snapshots = Math.Max(0, snapshots);
            Nodes = Math.Max(0, nodes);
            Evidence = Math.Max(0, evidence);
            ArchitectureRelationships = Math.Max(0, architectureRelationships);
            SnapshotSolutionRelationships = Math.Max(0, snapshotSolutionRelationships);
            NodeEvidenceRelationships = Math.Max(0, nodeEvidenceRelationships);
            RelationshipEndpointRelationships = Math.Max(0, relationshipEndpointRelationships);
            RelationshipEvidenceRelationships = Math.Max(0, relationshipEvidenceRelationships);
            Rules = Math.Max(0, rules);
            Findings = Math.Max(0, findings);
            FindingRuleRelationships = Math.Max(0, findingRuleRelationships);
            FindingNodeRelationships = Math.Max(0, findingNodeRelationships);
            FindingEvidenceRelationships = Math.Max(0, findingEvidenceRelationships);
            Metrics = Math.Max(0, metrics);
            MetricEvidenceRelationships = Math.Max(0, metricEvidenceRelationships);
            MetricTargetRelationships = Math.Max(0, metricTargetRelationships);
            GeneratedSummaries = Math.Max(0, generatedSummaries);
            SummarySnapshotRelationships = Math.Max(0, summarySnapshotRelationships);
            SummaryTargetRelationships = Math.Max(0, summaryTargetRelationships);
        }

        /// <summary>
        /// Gets an empty count set for failed operations that did not persist records.
        /// </summary>
        public static SnapshotPersistenceCounts Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        /// <summary>
        /// Gets the number of repository records persisted.
        /// </summary>
        public int Repositories { get; }

        /// <summary>
        /// Gets the number of solution records persisted.
        /// </summary>
        public int Solutions { get; }

        /// <summary>
        /// Gets the number of snapshot header records persisted.
        /// </summary>
        public int Snapshots { get; }

        /// <summary>
        /// Gets the number of architecture node records persisted.
        /// </summary>
        public int Nodes { get; }

        /// <summary>
        /// Gets the number of canonical evidence records persisted after snapshot-scoped deduplication.
        /// </summary>
        public int Evidence { get; }

        /// <summary>
        /// Gets the number of architecture relationship nodes persisted through the relationship-node pattern.
        /// </summary>
        public int ArchitectureRelationships { get; }

        /// <summary>
        /// Gets the number of snapshot-to-solution relationships persisted.
        /// </summary>
        public int SnapshotSolutionRelationships { get; }

        /// <summary>
        /// Gets the number of node-to-evidence relationships persisted.
        /// </summary>
        public int NodeEvidenceRelationships { get; }

        /// <summary>
        /// Gets the number of endpoint relationships from relationship nodes to source and target architecture nodes.
        /// </summary>
        public int RelationshipEndpointRelationships { get; }

        /// <summary>
        /// Gets the number of relationship-node-to-evidence relationships persisted.
        /// </summary>
        public int RelationshipEvidenceRelationships { get; }

        /// <summary>
        /// Gets the number of rule catalog records upserted by rule code and version.
        /// </summary>
        public int Rules { get; }

        /// <summary>
        /// Gets the number of snapshot-scoped finding records persisted.
        /// </summary>
        public int Findings { get; }

        /// <summary>
        /// Gets the number of finding-to-rule-version relationships persisted.
        /// </summary>
        public int FindingRuleRelationships { get; }

        /// <summary>
        /// Gets the number of finding-to-primary-node relationships persisted.
        /// </summary>
        public int FindingNodeRelationships { get; }

        /// <summary>
        /// Gets the number of finding-to-evidence relationships persisted.
        /// </summary>
        public int FindingEvidenceRelationships { get; }

        /// <summary>
        /// Gets the number of snapshot-scoped metric records persisted.
        /// </summary>
        public int Metrics { get; }

        /// <summary>
        /// Gets the number of metric-to-evidence relationships persisted.
        /// </summary>
        public int MetricEvidenceRelationships { get; }

        /// <summary>
        /// Gets the number of metric-to-target relationships persisted.
        /// </summary>
        public int MetricTargetRelationships { get; }

        /// <summary>
        /// Gets the number of snapshot-scoped generated-summary records persisted.
        /// </summary>
        public int GeneratedSummaries { get; }

        /// <summary>
        /// Gets the number of generated-summary-to-snapshot relationships persisted.
        /// </summary>
        public int SummarySnapshotRelationships { get; }

        /// <summary>
        /// Gets the number of generated-summary-to-target relationships persisted.
        /// </summary>
        public int SummaryTargetRelationships { get; }
    }
}
