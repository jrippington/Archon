using Archon.Domain.Graph.Model;

namespace Archon.Infrastructure.Neo4j.Persistence
{
    /// <summary>
    /// Maps WP002 graph fact contracts into Neo4j parameter dictionaries for snapshot persistence.
    /// </summary>
    /// <remarks>
    /// The mapper keeps property naming and deterministic metadata serialization in one place so the writer can focus on persistence
    /// ordering and transaction behavior. It returns plain dictionaries because the Neo4j driver accepts parameter objects without
    /// requiring domain or application contracts to reference driver-specific types.
    /// </remarks>
    public sealed class Neo4jSnapshotPersistenceMapper
    {
        /// <summary>
        /// Maps a repository model to Neo4j node properties.
        /// </summary>
        /// <param name="repository">The repository model to map.</param>
        /// <returns>A dictionary containing normalized repository properties for Cypher parameters.</returns>
        public IReadOnlyDictionary<string, object?> MapRepository(RepositoryModel repository)
        {
            // Repository properties are global and use stableKey as their upsert identity.
            ArgumentNullException.ThrowIfNull(repository);
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["stableKey"] = repository.StableKey.Value,
                ["name"] = repository.Name,
                ["rootPath"] = repository.RootPath,
                ["remoteUrl"] = repository.RemoteUrl,
                ["defaultBranch"] = repository.DefaultBranch,
                ["metadataJson"] = repository.Metadata.ToCanonicalJson()
            };
        }

        /// <summary>
        /// Maps a solution model to Neo4j node properties.
        /// </summary>
        /// <param name="solution">The solution model to map.</param>
        /// <returns>A dictionary containing normalized solution properties for Cypher parameters.</returns>
        public IReadOnlyDictionary<string, object?> MapSolution(SolutionModel solution)
        {
            // Solution identity is global in the current schema, while repositoryStableKey preserves repository association.
            ArgumentNullException.ThrowIfNull(solution);
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["repositoryStableKey"] = solution.RepositoryStableKey.Value,
                ["stableKey"] = solution.StableKey.Value,
                ["name"] = solution.Name,
                ["path"] = solution.Path.Value,
                ["metadataJson"] = solution.Metadata.ToCanonicalJson()
            };
        }

        /// <summary>
        /// Maps a snapshot header to Neo4j node properties.
        /// </summary>
        /// <param name="snapshotHeader">The snapshot header to map.</param>
        /// <returns>A dictionary containing normalized snapshot properties for Cypher parameters.</returns>
        public IReadOnlyDictionary<string, object?> MapSnapshot(SnapshotHeader snapshotHeader)
        {
            // Snapshot diagnostics are persisted as deterministic arrays so later query/API work can surface extraction context.
            ArgumentNullException.ThrowIfNull(snapshotHeader);
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["stableKey"] = snapshotHeader.StableKey.Value,
                ["repositoryStableKey"] = snapshotHeader.RepositoryStableKey.Value,
                ["branchName"] = snapshotHeader.BranchName,
                ["commitSha"] = snapshotHeader.CommitSha,
                ["startedUtc"] = snapshotHeader.StartedUtc.UtcDateTime,
                ["completedUtc"] = snapshotHeader.CompletedUtc?.UtcDateTime,
                ["extractionVersion"] = snapshotHeader.ExtractionVersion,
                ["status"] = snapshotHeader.Status,
                ["warningsJson"] = SerializeStringArray(snapshotHeader.Warnings),
                ["errorsJson"] = SerializeStringArray(snapshotHeader.Errors),
                ["metadataJson"] = snapshotHeader.Metadata.ToCanonicalJson()
            };
        }

        /// <summary>
        /// Maps an architecture node to Neo4j node properties.
        /// </summary>
        /// <param name="node">The architecture node to map.</param>
        /// <returns>A dictionary containing normalized architecture node properties for Cypher parameters.</returns>
        public IReadOnlyDictionary<string, object?> MapNode(ArchitectureNode node)
        {
            // Node properties retain query-critical values as first-class fields and place only extension data in metadataJson.
            ArgumentNullException.ThrowIfNull(node);
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["snapshotStableKey"] = node.SnapshotStableKey.Value,
                ["stableKey"] = node.StableKey.Value,
                ["nodeKind"] = node.NodeKind.Value,
                ["displayName"] = node.DisplayName,
                ["qualifiedName"] = node.QualifiedName,
                ["searchName"] = node.SearchName,
                ["language"] = node.Language,
                ["projectStableKey"] = node.ProjectStableKey?.Value,
                ["parentNodeStableKey"] = node.ParentNodeStableKey?.Value,
                ["knowledgeKind"] = node.KnowledgeKind.Value,
                ["ownership"] = node.Ownership,
                ["externalCategory"] = node.ExternalCategory,
                ["confidence"] = node.Confidence.Value,
                ["hasUnknownData"] = node.UnknownState.HasUnknownData,
                ["unknownReason"] = node.UnknownState.UnknownReason,
                ["primaryEvidenceStableKey"] = node.PrimaryEvidenceStableKey?.Value,
                ["metadataJson"] = node.Metadata.ToCanonicalJson(),
                ["fingerprint"] = node.Fingerprint.Value
            };
        }

        /// <summary>
        /// Maps an architecture edge to Neo4j relationship-node properties.
        /// </summary>
        /// <param name="edge">The architecture edge to map.</param>
        /// <returns>A dictionary containing normalized architecture relationship properties for Cypher parameters.</returns>
        public IReadOnlyDictionary<string, object?> MapRelationship(ArchitectureEdge edge)
        {
            // Neo4j relationships cannot point to evidence nodes, so edge facts are materialized as ArchonRelationship nodes that can
            // carry stable keys, metadata, fingerprints, endpoint links, and supporting evidence links consistently.
            ArgumentNullException.ThrowIfNull(edge);
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["snapshotStableKey"] = edge.SnapshotStableKey.Value,
                ["stableKey"] = edge.StableKey.Value,
                ["edgeKind"] = edge.EdgeKind.Value,
                ["sourceNodeStableKey"] = edge.SourceNodeStableKey.Value,
                ["targetNodeStableKey"] = edge.TargetNodeStableKey.Value,
                ["isDirect"] = edge.IsDirect,
                ["knowledgeKind"] = edge.KnowledgeKind.Value,
                ["confidence"] = edge.Confidence.Value,
                ["hasUnknownData"] = edge.UnknownState.HasUnknownData,
                ["unknownReason"] = edge.UnknownState.UnknownReason,
                ["primaryEvidenceStableKey"] = edge.PrimaryEvidenceStableKey?.Value,
                ["metadataJson"] = edge.Metadata.ToCanonicalJson(),
                ["fingerprint"] = edge.Fingerprint.Value
            };
        }

        /// <summary>
        /// Maps a rule definition to Neo4j rule catalog node properties.
        /// </summary>
        /// <param name="rule">The rule definition to map.</param>
        /// <returns>A dictionary containing normalized versioned rule catalog properties for Cypher parameters.</returns>
        public IReadOnlyDictionary<string, object?> MapRule(RuleDefinition rule)
        {
            // Rule catalog entries are global rather than snapshot-scoped. The writer merges them by ruleCode and ruleVersion so
            // historical findings can continue to reference the exact rule version that classified them.
            ArgumentNullException.ThrowIfNull(rule);
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["ruleCode"] = rule.RuleCode,
                ["ruleVersion"] = rule.Version,
                ["name"] = rule.Name,
                ["category"] = rule.Category.Value,
                ["severity"] = rule.Severity.Value,
                ["defaultStatus"] = rule.DefaultStatus.Value,
                ["enabled"] = rule.Enabled,
                ["description"] = rule.Description,
                ["definitionJson"] = rule.DefinitionJson,
                ["sourceUrlsJson"] = SerializeStringArray(rule.SourceUrls),
                ["isBuiltIn"] = rule.IsBuiltIn,
                ["ownerScope"] = rule.OwnerScope,
                ["metadataJson"] = rule.Metadata.ToCanonicalJson()
            };
        }

        /// <summary>
        /// Maps a finding record to Neo4j snapshot-scoped finding node properties.
        /// </summary>
        /// <param name="finding">The finding record to map.</param>
        /// <returns>A dictionary containing normalized finding properties for Cypher parameters.</returns>
        public IReadOnlyDictionary<string, object?> MapFinding(FindingRecord finding)
        {
            // Findings keep their rule reference as first-class properties as well as graph links so direct lookups and traversals both
            // preserve historical rule-version fidelity.
            ArgumentNullException.ThrowIfNull(finding);
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["snapshotStableKey"] = finding.SnapshotStableKey.Value,
                ["stableKey"] = finding.StableKey.Value,
                ["ruleCode"] = finding.RuleCode,
                ["ruleVersion"] = finding.RuleVersion,
                ["severity"] = finding.Severity.Value,
                ["status"] = finding.Status.Value,
                ["title"] = finding.Title,
                ["description"] = finding.Description,
                ["knowledgeKind"] = finding.KnowledgeKind.Value,
                ["confidence"] = finding.Confidence.Value,
                ["hasUnknownData"] = finding.UnknownState.HasUnknownData,
                ["unknownReason"] = finding.UnknownState.UnknownReason,
                ["primaryNodeStableKey"] = finding.PrimaryNodeStableKey?.Value,
                ["primaryEvidenceStableKey"] = finding.PrimaryEvidenceStableKey?.Value,
                ["firstSeenSnapshotStableKey"] = finding.FirstSeenSnapshotStableKey?.Value,
                ["latestSeenSnapshotStableKey"] = finding.LatestSeenSnapshotStableKey?.Value,
                ["suppressionReason"] = finding.SuppressionReason,
                ["suppressedBy"] = finding.SuppressedBy,
                ["affectedNodeStableKeys"] = finding.AffectedNodeStableKeys.Select(static stableKey => stableKey.Value).ToArray(),
                ["evidenceStableKeys"] = finding.EvidenceStableKeys.Select(static stableKey => stableKey.Value).ToArray(),
                ["historyKey"] = finding.HistoryKey,
                ["metadataJson"] = finding.Metadata.ToCanonicalJson(),
                ["fingerprint"] = finding.Fingerprint.Value
            };
        }

        /// <summary>
        /// Maps a metric record to Neo4j snapshot-scoped metric node properties.
        /// </summary>
        /// <param name="metric">The metric record to map.</param>
        /// <returns>A dictionary containing normalized metric properties for Cypher parameters.</returns>
        public IReadOnlyDictionary<string, object?> MapMetric(MetricRecord metric)
        {
            // Metrics preserve scope, target, value, evidence, and fingerprint as first-class fields because later diff and report work
            // needs to compare metric values without parsing extension metadata.
            ArgumentNullException.ThrowIfNull(metric);
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["snapshotStableKey"] = metric.SnapshotStableKey.Value,
                ["stableKey"] = metric.StableKey.Value,
                ["metricKind"] = metric.MetricKind,
                ["scopeKind"] = metric.ScopeKind.Value,
                ["nodeStableKey"] = metric.NodeStableKey?.Value,
                ["edgeStableKey"] = metric.EdgeStableKey?.Value,
                ["primaryEvidenceStableKey"] = metric.PrimaryEvidenceStableKey?.Value,
                ["name"] = metric.Name,
                ["numericValue"] = metric.NumericValue,
                ["textValue"] = metric.TextValue,
                ["unit"] = metric.Unit,
                ["confidence"] = metric.Confidence.Value,
                ["hasUnknownData"] = metric.UnknownState.HasUnknownData,
                ["unknownReason"] = metric.UnknownState.UnknownReason,
                ["metadataJson"] = metric.Metadata.ToCanonicalJson(),
                ["fingerprint"] = metric.Fingerprint.Value
            };
        }

        /// <summary>
        /// Maps a generated summary to Neo4j snapshot-scoped generated-summary node properties.
        /// </summary>
        /// <param name="generatedSummary">The generated summary to map.</param>
        /// <returns>A dictionary containing normalized generated-summary properties for Cypher parameters.</returns>
        public IReadOnlyDictionary<string, object?> MapGeneratedSummary(GeneratedSummary generatedSummary)
        {
            // Generated summaries persist narrative output as graph data so API, MCP, markdown, and reporting slices can retrieve a
            // durable summary by stable key instead of regenerating content on every read.
            ArgumentNullException.ThrowIfNull(generatedSummary);
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["snapshotStableKey"] = generatedSummary.SnapshotStableKey.Value,
                ["stableKey"] = generatedSummary.StableKey.Value,
                ["summaryKind"] = generatedSummary.SummaryKind.Value,
                ["targetStableKey"] = generatedSummary.TargetStableKey?.Value,
                ["format"] = generatedSummary.Format,
                ["title"] = generatedSummary.Title,
                ["content"] = generatedSummary.Content,
                ["metadataJson"] = generatedSummary.Metadata.ToCanonicalJson(),
                ["fingerprint"] = generatedSummary.Fingerprint.Value
            };
        }

        /// <summary>
        /// Maps an evidence record to Neo4j node properties.
        /// </summary>
        /// <param name="evidence">The evidence record to map.</param>
        /// <returns>A dictionary containing normalized evidence properties for Cypher parameters.</returns>
        public IReadOnlyDictionary<string, object?> MapEvidence(EvidenceRecord evidence)
        {
            // Evidence properties are snapshot-scoped and include both source-location details and deterministic confidence fields.
            ArgumentNullException.ThrowIfNull(evidence);
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["snapshotStableKey"] = evidence.SnapshotStableKey.Value,
                ["stableKey"] = evidence.StableKey.Value,
                ["evidenceKind"] = evidence.EvidenceKind.Value,
                ["filePath"] = evidence.FilePath.Value,
                ["startLine"] = evidence.StartLine,
                ["endLine"] = evidence.EndLine,
                ["symbolName"] = evidence.SymbolName,
                ["containingSymbol"] = evidence.ContainingSymbol,
                ["snippetHash"] = evidence.SnippetHash,
                ["snippetPreview"] = evidence.SnippetPreview,
                ["knowledgeKind"] = evidence.KnowledgeKind.Value,
                ["confidence"] = evidence.Confidence.Value,
                ["hasUnknownData"] = evidence.UnknownState.HasUnknownData,
                ["unknownReason"] = evidence.UnknownState.UnknownReason,
                ["metadataJson"] = evidence.Metadata.ToCanonicalJson(),
                ["fingerprint"] = evidence.Fingerprint.Value
            };
        }

        /// <summary>
        /// Builds a snapshot-scoped canonical evidence identity used for Work Item 4 deduplication.
        /// </summary>
        /// <param name="evidence">The evidence record whose canonical identity should be calculated.</param>
        /// <returns>A deterministic identity string that is only valid within the evidence snapshot scope.</returns>
        public string GetEvidenceDeduplicationKey(EvidenceRecord evidence)
        {
            // Work Item 4 deduplicates identical evidence payloads within one snapshot while keeping the snapshot key in the identity
            // so identical evidence in different snapshots remains separate.
            ArgumentNullException.ThrowIfNull(evidence);
            return string.Join(
                "\u001F",
                evidence.SnapshotStableKey.Value,
                evidence.EvidenceKind.Value,
                evidence.FilePath.Value,
                evidence.StartLine?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                evidence.EndLine?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                evidence.SymbolName ?? string.Empty,
                evidence.ContainingSymbol ?? string.Empty,
                evidence.SnippetHash ?? string.Empty,
                evidence.SnippetPreview ?? string.Empty,
                evidence.KnowledgeKind.Value,
                evidence.Confidence.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                evidence.UnknownState.HasUnknownData.ToString(System.Globalization.CultureInfo.InvariantCulture),
                evidence.UnknownState.UnknownReason ?? string.Empty,
                evidence.Metadata.ToCanonicalJson(),
                evidence.Fingerprint.Value);
        }

        /// <summary>
        /// Serializes diagnostic strings as deterministic JSON arrays.
        /// </summary>
        /// <param name="values">The diagnostic values to serialize.</param>
        /// <returns>A compact JSON array string.</returns>
        private static string SerializeStringArray(IReadOnlyList<string> values)
        {
            // System.Text.Json preserves input order for arrays, which matches snapshot diagnostic ordering semantics.
            return System.Text.Json.JsonSerializer.Serialize(values);
        }
    }
}
