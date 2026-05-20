namespace Archon.Infrastructure.Neo4j.Schema
{
    /// <summary>
    /// Defines stable Neo4j labels, relationship names, constraint names, index names, and property names used by Archon schema initialization.
    /// </summary>
    /// <remarks>
    /// The names are intentionally centralized and human-readable because later persistence, query, MCP, and troubleshooting slices
    /// must refer to the same graph vocabulary without inventing parallel constants or relying on Neo4j internal identifiers.
    /// </remarks>
    public static class Neo4jSchemaNames
    {
        /// <summary>
        /// Labels used by Archon's Neo4j graph schema.
        /// </summary>
        public static class Labels
        {
            /// <summary>
            /// Labels repository root records persisted by Archon.
            /// </summary>
            public const string Repository = "ArchonRepository";

            /// <summary>
            /// Labels solution records discovered within repositories.
            /// </summary>
            public const string Solution = "ArchonSolution";

            /// <summary>
            /// Labels extraction snapshot records.
            /// </summary>
            public const string Snapshot = "ArchonSnapshot";

            /// <summary>
            /// Labels architecture concept nodes such as projects, packages, endpoints, symbols, and configuration elements.
            /// </summary>
            public const string Node = "ArchonNode";

            /// <summary>
            /// Labels architecture relationship records represented with the relationship-node pattern.
            /// </summary>
            public const string Relationship = "ArchonRelationship";

            /// <summary>
            /// Labels evidence records that explain why Archon believes a graph fact.
            /// </summary>
            public const string Evidence = "ArchonEvidence";

            /// <summary>
            /// Labels versioned rule catalog entries.
            /// </summary>
            public const string Rule = "ArchonRule";

            /// <summary>
            /// Labels findings produced from rules or analysis.
            /// </summary>
            public const string Finding = "ArchonFinding";

            /// <summary>
            /// Labels metric records emitted for snapshots, nodes, relationships, or other graph scopes.
            /// </summary>
            public const string Metric = "ArchonMetric";

            /// <summary>
            /// Labels generated summaries that later slices can render or export.
            /// </summary>
            public const string GeneratedSummary = "ArchonGeneratedSummary";
        }

        /// <summary>
        /// Relationship type names reserved for later persistence edges between schema-backed nodes.
        /// </summary>
        public static class Relationships
        {
            /// <summary>
            /// Connects a snapshot to a solution included in that snapshot.
            /// </summary>
            public const string IncludesSolution = "INCLUDES_SOLUTION";

            /// <summary>
            /// Connects a relationship node to its source architecture node.
            /// </summary>
            public const string RelationshipSource = "RELATIONSHIP_SOURCE";

            /// <summary>
            /// Connects a relationship node to its target architecture node.
            /// </summary>
            public const string RelationshipTarget = "RELATIONSHIP_TARGET";

            /// <summary>
            /// Connects a graph fact to evidence that supports it.
            /// </summary>
            public const string SupportedByEvidence = "SUPPORTED_BY_EVIDENCE";

            /// <summary>
            /// Connects a finding to the rule version that produced or classified it.
            /// </summary>
            public const string ClassifiedByRule = "CLASSIFIED_BY_RULE";

            /// <summary>
            /// Connects a finding to the primary architecture node that the finding concerns.
            /// </summary>
            public const string PrimaryNode = "PRIMARY_NODE";

            /// <summary>
            /// Connects a metric or generated summary to the architecture relationship record that it targets.
            /// </summary>
            public const string PrimaryRelationship = "PRIMARY_RELATIONSHIP";

            /// <summary>
            /// Connects a generated summary to the snapshot that owns the durable generated content.
            /// </summary>
            public const string SummarizesSnapshot = "SUMMARIZES_SNAPSHOT";
        }

        /// <summary>
        /// Property names used by constraints and indexes in the initialized graph schema.
        /// </summary>
        public static class Properties
        {
            /// <summary>
            /// Stores a stable logical identity that does not depend on Neo4j internal IDs.
            /// </summary>
            public const string StableKey = "stableKey";

            /// <summary>
            /// Stores the stable key of the snapshot that scopes a graph fact.
            /// </summary>
            public const string SnapshotStableKey = "snapshotStableKey";

            /// <summary>
            /// Stores the stable key of the repository associated with a solution or snapshot.
            /// </summary>
            public const string RepositoryStableKey = "repositoryStableKey";

            /// <summary>
            /// Stores the normalized kind of an architecture node.
            /// </summary>
            public const string NodeKind = "nodeKind";

            /// <summary>
            /// Stores the normalized kind of an architecture relationship.
            /// </summary>
            public const string EdgeKind = "edgeKind";

            /// <summary>
            /// Stores the normalized kind of an evidence record.
            /// </summary>
            public const string EvidenceKind = "evidenceKind";

            /// <summary>
            /// Stores the versioned rule code for a rule or finding.
            /// </summary>
            public const string RuleCode = "ruleCode";

            /// <summary>
            /// Stores the version string for a rule catalog entry or finding reference.
            /// </summary>
            public const string RuleVersion = "ruleVersion";

            /// <summary>
            /// Stores a rule category or finding category value where later persistence slices need category lookup.
            /// </summary>
            public const string Category = "category";

            /// <summary>
            /// Stores a finding severity value.
            /// </summary>
            public const string Severity = "severity";

            /// <summary>
            /// Stores a snapshot or finding status value.
            /// </summary>
            public const string Status = "status";

            /// <summary>
            /// Stores a knowledge classification such as direct fact, inference, unknown, or human-confirmed.
            /// </summary>
            public const string KnowledgeKind = "knowledgeKind";

            /// <summary>
            /// Stores a deterministic confidence value from zero through one.
            /// </summary>
            public const string Confidence = "confidence";

            /// <summary>
            /// Stores a deterministic diff-relevant fingerprint.
            /// </summary>
            public const string Fingerprint = "fingerprint";

            /// <summary>
            /// Stores the normalized metric kind.
            /// </summary>
            public const string MetricKind = "metricKind";

            /// <summary>
            /// Stores the normalized metric scope kind.
            /// </summary>
            public const string ScopeKind = "scopeKind";

            /// <summary>
            /// Stores the normalized generated-summary kind.
            /// </summary>
            public const string SummaryKind = "summaryKind";
        }

        /// <summary>
        /// Stable constraint names created by schema initialization.
        /// </summary>
        public static class Constraints
        {
            /// <summary>
            /// Uniqueness constraint for repository stable keys.
            /// </summary>
            public const string RepositoryStableKeyUnique = "archon_repository_stable_key_unique";

            /// <summary>
            /// Uniqueness constraint for solution stable keys.
            /// </summary>
            public const string SolutionStableKeyUnique = "archon_solution_stable_key_unique";

            /// <summary>
            /// Uniqueness constraint for snapshot stable keys.
            /// </summary>
            public const string SnapshotStableKeyUnique = "archon_snapshot_stable_key_unique";

            /// <summary>
            /// Snapshot-scoped uniqueness constraint for architecture node stable keys.
            /// </summary>
            public const string NodeSnapshotStableKeyUnique = "archon_node_snapshot_stable_key_unique";

            /// <summary>
            /// Snapshot-scoped uniqueness constraint for relationship-node stable keys.
            /// </summary>
            public const string RelationshipSnapshotStableKeyUnique = "archon_relationship_snapshot_stable_key_unique";

            /// <summary>
            /// Snapshot-scoped uniqueness constraint for evidence stable keys.
            /// </summary>
            public const string EvidenceSnapshotStableKeyUnique = "archon_evidence_snapshot_stable_key_unique";

            /// <summary>
            /// Versioned uniqueness constraint for rule catalog entries.
            /// </summary>
            public const string RuleCodeVersionUnique = "archon_rule_code_version_unique";

            /// <summary>
            /// Snapshot-scoped uniqueness constraint for finding stable keys.
            /// </summary>
            public const string FindingSnapshotStableKeyUnique = "archon_finding_snapshot_stable_key_unique";

            /// <summary>
            /// Snapshot-scoped uniqueness constraint for metric stable keys.
            /// </summary>
            public const string MetricSnapshotStableKeyUnique = "archon_metric_snapshot_stable_key_unique";

            /// <summary>
            /// Snapshot-scoped uniqueness constraint for generated summary stable keys.
            /// </summary>
            public const string GeneratedSummarySnapshotStableKeyUnique = "archon_generated_summary_snapshot_stable_key_unique";
        }

        /// <summary>
        /// Stable index names created by schema initialization.
        /// </summary>
        public static class Indexes
        {
            /// <summary>
            /// Index for solutions by repository stable key.
            /// </summary>
            public const string SolutionRepositoryStableKey = "archon_solution_repository_stable_key_index";

            /// <summary>
            /// Index for snapshots by repository stable key.
            /// </summary>
            public const string SnapshotRepositoryStableKey = "archon_snapshot_repository_stable_key_index";

            /// <summary>
            /// Index for snapshots by status.
            /// </summary>
            public const string SnapshotStatus = "archon_snapshot_status_index";

            /// <summary>
            /// Index for snapshots by fingerprint.
            /// </summary>
            public const string SnapshotFingerprint = "archon_snapshot_fingerprint_index";

            /// <summary>
            /// Index for architecture nodes by snapshot stable key.
            /// </summary>
            public const string NodeSnapshot = "archon_node_snapshot_index";

            /// <summary>
            /// Index for architecture nodes by node kind.
            /// </summary>
            public const string NodeKind = "archon_node_kind_index";

            /// <summary>
            /// Index for architecture nodes by knowledge kind.
            /// </summary>
            public const string NodeKnowledgeKind = "archon_node_knowledge_kind_index";

            /// <summary>
            /// Index for architecture nodes by confidence.
            /// </summary>
            public const string NodeConfidence = "archon_node_confidence_index";

            /// <summary>
            /// Index for architecture nodes by fingerprint.
            /// </summary>
            public const string NodeFingerprint = "archon_node_fingerprint_index";

            /// <summary>
            /// Index for relationship nodes by snapshot stable key.
            /// </summary>
            public const string RelationshipSnapshot = "archon_relationship_snapshot_index";

            /// <summary>
            /// Index for relationship nodes by edge kind.
            /// </summary>
            public const string RelationshipEdgeKind = "archon_relationship_edge_kind_index";

            /// <summary>
            /// Index for relationship nodes by knowledge kind.
            /// </summary>
            public const string RelationshipKnowledgeKind = "archon_relationship_knowledge_kind_index";

            /// <summary>
            /// Index for relationship nodes by confidence.
            /// </summary>
            public const string RelationshipConfidence = "archon_relationship_confidence_index";

            /// <summary>
            /// Index for relationship nodes by fingerprint.
            /// </summary>
            public const string RelationshipFingerprint = "archon_relationship_fingerprint_index";

            /// <summary>
            /// Index for evidence by snapshot stable key.
            /// </summary>
            public const string EvidenceSnapshot = "archon_evidence_snapshot_index";

            /// <summary>
            /// Index for evidence by evidence kind.
            /// </summary>
            public const string EvidenceKind = "archon_evidence_kind_index";

            /// <summary>
            /// Index for evidence by knowledge kind.
            /// </summary>
            public const string EvidenceKnowledgeKind = "archon_evidence_knowledge_kind_index";

            /// <summary>
            /// Index for evidence by confidence.
            /// </summary>
            public const string EvidenceConfidence = "archon_evidence_confidence_index";

            /// <summary>
            /// Index for evidence by fingerprint.
            /// </summary>
            public const string EvidenceFingerprint = "archon_evidence_fingerprint_index";

            /// <summary>
            /// Index for rules by rule code.
            /// </summary>
            public const string RuleCode = "archon_rule_code_index";

            /// <summary>
            /// Index for rules by category.
            /// </summary>
            public const string RuleCategory = "archon_rule_category_index";

            /// <summary>
            /// Index for findings by snapshot stable key.
            /// </summary>
            public const string FindingSnapshot = "archon_finding_snapshot_index";

            /// <summary>
            /// Index for findings by severity.
            /// </summary>
            public const string FindingSeverity = "archon_finding_severity_index";

            /// <summary>
            /// Index for findings by status.
            /// </summary>
            public const string FindingStatus = "archon_finding_status_index";

            /// <summary>
            /// Index for findings by knowledge kind.
            /// </summary>
            public const string FindingKnowledgeKind = "archon_finding_knowledge_kind_index";

            /// <summary>
            /// Index for findings by confidence.
            /// </summary>
            public const string FindingConfidence = "archon_finding_confidence_index";

            /// <summary>
            /// Index for findings by fingerprint.
            /// </summary>
            public const string FindingFingerprint = "archon_finding_fingerprint_index";

            /// <summary>
            /// Index for metrics by snapshot stable key.
            /// </summary>
            public const string MetricSnapshot = "archon_metric_snapshot_index";

            /// <summary>
            /// Index for metrics by metric kind.
            /// </summary>
            public const string MetricKind = "archon_metric_kind_index";

            /// <summary>
            /// Index for metrics by scope kind.
            /// </summary>
            public const string MetricScopeKind = "archon_metric_scope_kind_index";

            /// <summary>
            /// Index for metrics by fingerprint.
            /// </summary>
            public const string MetricFingerprint = "archon_metric_fingerprint_index";

            /// <summary>
            /// Index for generated summaries by snapshot stable key.
            /// </summary>
            public const string GeneratedSummarySnapshot = "archon_generated_summary_snapshot_index";

            /// <summary>
            /// Index for generated summaries by summary kind.
            /// </summary>
            public const string GeneratedSummaryKind = "archon_generated_summary_kind_index";

            /// <summary>
            /// Index for generated summaries by fingerprint.
            /// </summary>
            public const string GeneratedSummaryFingerprint = "archon_generated_summary_fingerprint_index";
        }
    }
}
