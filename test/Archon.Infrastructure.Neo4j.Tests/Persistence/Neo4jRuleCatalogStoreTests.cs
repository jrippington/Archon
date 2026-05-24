using Archon.Infrastructure.Neo4j.Persistence;
using Xunit;

namespace Archon.Infrastructure.Neo4j.Tests.Persistence
{
    /// <summary>
    /// Verifies the Neo4j rule catalog store uses safe, versioned, non-destructive upsert semantics.
    /// </summary>
    public sealed class Neo4jRuleCatalogStoreTests
    {
        /// <summary>
        /// Confirms rule catalog upsert Cypher merges by rule code and version and never deletes omitted or disabled rules.
        /// </summary>
        [Fact]
        public void RuleMergeCypher_ShouldUseCodeVersionMergeAndAvoidDestructiveDeletes()
        {
            // Work Item 3 requires historical rule versions and removed-on-disk records to survive later catalog loads.
            string cypher = Neo4jRuleCatalogStore.RuleMergeCypher;

            Assert.Contains("MERGE (rule:ArchonRule { ruleCode: $ruleCode, ruleVersion: $ruleVersion })", cypher, StringComparison.Ordinal);
            Assert.Contains("rule.definitionJson = $definitionJson", cypher, StringComparison.Ordinal);
            Assert.Contains("rule.enabled = $enabled", cypher, StringComparison.Ordinal);
            Assert.DoesNotContain("DELETE", cypher, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DETACH", cypher, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Confirms finding persistence Cypher merges by snapshot and stable key, stores history and suppression fields, and avoids destructive deletes.
        /// </summary>
        [Fact]
        public void FindingMergeCypher_ShouldUseSnapshotStableKeyMergeAndAvoidDestructiveDeletes()
        {
            // Work Item 4 requires snapshot-scoped finding identity, historical fidelity, and non-destructive persistence behavior.
            string cypher = Neo4jFindingStore.FindingMergeCypher;

            Assert.Contains("MERGE (finding:ArchonFinding { snapshotStableKey: $snapshotStableKey, stableKey: $stableKey })", cypher, StringComparison.Ordinal);
            Assert.Contains("finding.historyKey = $historyKey", cypher, StringComparison.Ordinal);
            Assert.Contains("finding.suppressionReason = $suppressionReason", cypher, StringComparison.Ordinal);
            Assert.Contains("finding.affectedNodeStableKeys = $affectedNodeStableKeys", cypher, StringComparison.Ordinal);
            Assert.Contains("finding.evidenceStableKeys = $evidenceStableKeys", cypher, StringComparison.Ordinal);
            Assert.DoesNotContain("DELETE", cypher, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DETACH", cypher, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Confirms finding relationship Cypher links findings to rule, node, and evidence records by stable logical identities.
        /// </summary>
        [Fact]
        public void FindingRelationshipCypher_ShouldUseStableLogicalIdentities()
        {
            // Relationships must not rely on Neo4j internal identifiers because API and history behavior are stable-key based.
            Assert.Contains("MATCH (rule:ArchonRule { ruleCode: $ruleCode, ruleVersion: $ruleVersion })", Neo4jFindingStore.FindingRuleRelationshipCypher, StringComparison.Ordinal);
            Assert.Contains("MERGE (finding)-[:CLASSIFIED_BY_RULE]->(rule)", Neo4jFindingStore.FindingRuleRelationshipCypher, StringComparison.Ordinal);
            Assert.Contains("MATCH (node:ArchonNode { snapshotStableKey: $snapshotStableKey, stableKey: $nodeStableKey })", Neo4jFindingStore.FindingAffectedNodeRelationshipCypher, StringComparison.Ordinal);
            Assert.Contains("MERGE (finding)-[:PRIMARY_NODE]->(node)", Neo4jFindingStore.FindingAffectedNodeRelationshipCypher, StringComparison.Ordinal);
            Assert.Contains("MATCH (evidence:ArchonEvidence { snapshotStableKey: $snapshotStableKey, stableKey: $evidenceStableKey })", Neo4jFindingStore.FindingEvidenceRelationshipCypher, StringComparison.Ordinal);
            Assert.Contains("MERGE (finding)-[:SUPPORTED_BY_EVIDENCE]->(evidence)", Neo4jFindingStore.FindingEvidenceRelationshipCypher, StringComparison.Ordinal);
        }

        /// <summary>
        /// Confirms controlled rule query Cypher uses only explicit filters, paging, and deterministic ordering.
        /// </summary>
        [Fact]
        public void RuleQueryCypher_ShouldExposeControlledFiltersPagingAndOrdering()
        {
            // Work Item 5 forbids arbitrary graph access, so rule query Cypher must remain static and parameterized.
            string cypher = Neo4jHotlistQueryStore.RuleQueryCypher;

            Assert.Contains("($ruleCode IS NULL OR rule.ruleCode = $ruleCode)", cypher, StringComparison.Ordinal);
            Assert.Contains("($version IS NULL OR rule.ruleVersion = $version)", cypher, StringComparison.Ordinal);
            Assert.Contains("($category IS NULL OR rule.category = $category)", cypher, StringComparison.Ordinal);
            Assert.Contains("($severity IS NULL OR rule.severity = $severity)", cypher, StringComparison.Ordinal);
            Assert.Contains("($enabled IS NULL OR rule.enabled = $enabled)", cypher, StringComparison.Ordinal);
            Assert.Contains("($builtIn IS NULL OR rule.isBuiltIn = $builtIn)", cypher, StringComparison.Ordinal);
            Assert.Contains("ORDER BY rule.ruleCode, rule.ruleVersion", cypher, StringComparison.Ordinal);
            Assert.Contains("SKIP $skip", cypher, StringComparison.Ordinal);
            Assert.Contains("LIMIT $take", cypher, StringComparison.Ordinal);
            Assert.DoesNotContain("apoc.cypher", cypher, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Confirms controlled hotlist query Cypher uses approved filters, paging, deterministic ordering, and no destructive operations.
        /// </summary>
        [Fact]
        public void HotlistQueryCypher_ShouldExposeControlledFiltersPagingAndOrdering()
        {
            // Hotlist queries must return persisted findings without accepting caller-provided Cypher or mutating graph state.
            string cypher = Neo4jHotlistQueryStore.HotlistQueryCypher;

            Assert.Contains("MATCH (finding:ArchonFinding)", cypher, StringComparison.Ordinal);
            Assert.Contains("OPTIONAL MATCH (finding)-[:CLASSIFIED_BY_RULE]->(rule:ArchonRule)", cypher, StringComparison.Ordinal);
            Assert.Contains("($snapshotStableKey IS NULL OR finding.snapshotStableKey = $snapshotStableKey)", cypher, StringComparison.Ordinal);
            Assert.Contains("($category IS NULL OR rule.category = $category)", cypher, StringComparison.Ordinal);
            Assert.Contains("($severity IS NULL OR finding.severity = $severity)", cypher, StringComparison.Ordinal);
            Assert.Contains("($status IS NULL OR finding.status = $status)", cypher, StringComparison.Ordinal);
            Assert.Contains("($affectedNodeStableKey IS NULL OR $affectedNodeStableKey IN coalesce(finding.affectedNodeStableKeys, []))", cypher, StringComparison.Ordinal);
            Assert.Contains("ORDER BY finding.severity DESC, finding.ruleCode, finding.stableKey", cypher, StringComparison.Ordinal);
            Assert.Contains("SKIP $skip", cypher, StringComparison.Ordinal);
            Assert.Contains("LIMIT $take", cypher, StringComparison.Ordinal);
            Assert.DoesNotContain("DELETE", cypher, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DETACH", cypher, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Confirms finding history query Cypher uses the cross-snapshot history key and deterministic snapshot ordering.
        /// </summary>
        [Fact]
        public void FindingHistoryRecordsCypher_ShouldUseHistoryKeyAndDeterministicOrdering()
        {
            // History records must be read by the stable history key rather than Neo4j internal identifiers.
            string cypher = Neo4jHotlistQueryStore.FindingHistoryRecordsCypher;

            Assert.Contains("MATCH (finding:ArchonFinding { historyKey: $historyKey })", cypher, StringComparison.Ordinal);
            Assert.Contains("ORDER BY finding.snapshotStableKey, finding.stableKey", cypher, StringComparison.Ordinal);
            Assert.DoesNotContain("id(", cypher, StringComparison.OrdinalIgnoreCase);
        }
    }
}
