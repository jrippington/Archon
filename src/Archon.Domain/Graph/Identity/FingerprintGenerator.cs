using System.Security.Cryptography;
using System.Text;
using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Metadata;

namespace Archon.Domain.Graph.Identity
{
    /// <summary>
    /// Generates deterministic fingerprints for graph record categories from normalized diff-relevant input.
    /// </summary>
    /// <remarks>
    /// The generator hashes canonical UTF-8 input with SHA-256 and prefixes the result with <c>sha256:</c>. It accepts only logical content fields supplied by callers and therefore excludes database-local, process-local, and machine-local values unless a caller incorrectly provides them as diff-relevant content.
    /// </remarks>
    public static class FingerprintGenerator
    {
        /// <summary>
        /// Generates a fingerprint from a canonical fingerprint input builder.
        /// </summary>
        /// <param name="input">The canonical fingerprint input containing record category, fields, and metadata.</param>
        /// <returns>A deterministic SHA-256 fingerprint.</returns>
        public static Fingerprint FromInput(FingerprintInput input)
        {
            // Hashing one canonical text representation keeps the algorithm deterministic and easy to inspect in tests.
            ArgumentNullException.ThrowIfNull(input);
            byte[] bytes = Encoding.UTF8.GetBytes(input.ToCanonicalText());
            byte[] hash = SHA256.HashData(bytes);
            string hex = Convert.ToHexString(hash).ToLowerInvariant();
            return new Fingerprint($"sha256:{hex}");
        }

        /// <summary>
        /// Generates an architecture-node fingerprint from normalized node fields.
        /// </summary>
        /// <param name="nodeKind">The node kind.</param>
        /// <param name="displayName">The node display name.</param>
        /// <param name="qualifiedName">The optional qualified name for the node.</param>
        /// <param name="searchName">The normalized search name for the node.</param>
        /// <param name="knowledgeKind">The knowledge classification for the node.</param>
        /// <param name="metadata">The canonical metadata payload for diff-relevant node details.</param>
        /// <returns>A deterministic node fingerprint.</returns>
        public static Fingerprint ForNode(NodeKind nodeKind, string? displayName, string? qualifiedName, string? searchName, KnowledgeKind knowledgeKind, GraphMetadata metadata)
        {
            // Node fingerprints include normalized user-visible and classification fields plus canonical metadata.
            return FromInput(FingerprintInput.Create("Node")
                .AddField("nodeKind", RequireValue(nodeKind, nameof(nodeKind)).Value)
                .AddField("displayName", RequireText(displayName, nameof(displayName)))
                .AddField("qualifiedName", qualifiedName)
                .AddField("searchName", RequireText(searchName, nameof(searchName)))
                .AddField("knowledgeKind", RequireValue(knowledgeKind, nameof(knowledgeKind)).Value)
                .AddMetadata(metadata));
        }

        /// <summary>
        /// Generates an architecture-edge fingerprint from normalized edge fields.
        /// </summary>
        /// <param name="edgeKind">The edge relationship kind.</param>
        /// <param name="sourceNodeStableKey">The stable key of the source node.</param>
        /// <param name="targetNodeStableKey">The stable key of the target node.</param>
        /// <param name="isDirect">A value indicating whether the edge is directly observed or derived through an indirect relationship.</param>
        /// <param name="knowledgeKind">The knowledge classification for the edge.</param>
        /// <param name="metadata">The canonical metadata payload for diff-relevant edge details.</param>
        /// <returns>A deterministic edge fingerprint.</returns>
        public static Fingerprint ForEdge(EdgeKind edgeKind, StableKey sourceNodeStableKey, StableKey targetNodeStableKey, bool isDirect, KnowledgeKind knowledgeKind, GraphMetadata metadata)
        {
            // Edge fingerprints include relationship semantics and endpoints, but not database relationship IDs.
            return FromInput(FingerprintInput.Create("Edge")
                .AddField("edgeKind", RequireValue(edgeKind, nameof(edgeKind)).Value)
                .AddField("sourceNodeStableKey", sourceNodeStableKey)
                .AddField("targetNodeStableKey", targetNodeStableKey)
                .AddField("isDirect", isDirect)
                .AddField("knowledgeKind", RequireValue(knowledgeKind, nameof(knowledgeKind)).Value)
                .AddMetadata(metadata));
        }

        /// <summary>
        /// Generates an evidence fingerprint from normalized evidence fields.
        /// </summary>
        /// <param name="evidenceKind">The evidence kind.</param>
        /// <param name="filePath">The repository-relative evidence file path.</param>
        /// <param name="startLine">The optional start line for source evidence.</param>
        /// <param name="endLine">The optional end line for source evidence.</param>
        /// <param name="symbolName">The optional symbol name associated with the evidence.</param>
        /// <param name="knowledgeKind">The knowledge classification for the evidence.</param>
        /// <param name="metadata">The canonical metadata payload for diff-relevant evidence details.</param>
        /// <returns>A deterministic evidence fingerprint.</returns>
        public static Fingerprint ForEvidence(EvidenceKind evidenceKind, string? filePath, int? startLine, int? endLine, string? symbolName, KnowledgeKind knowledgeKind, GraphMetadata metadata)
        {
            // Evidence fingerprints include source location and symbol details because they explain where a fact came from.
            return FromInput(FingerprintInput.Create("Evidence")
                .AddField("evidenceKind", RequireValue(evidenceKind, nameof(evidenceKind)).Value)
                .AddField("filePath", RepositoryRelativePath.Parse(filePath).Value)
                .AddField("startLine", startLine)
                .AddField("endLine", endLine)
                .AddField("symbolName", symbolName)
                .AddField("knowledgeKind", RequireValue(knowledgeKind, nameof(knowledgeKind)).Value)
                .AddMetadata(metadata));
        }

        /// <summary>
        /// Generates a finding fingerprint from normalized finding fields.
        /// </summary>
        /// <param name="ruleCode">The rule code that produced the finding.</param>
        /// <param name="ruleVersion">The rule version that produced the finding.</param>
        /// <param name="severity">The finding severity.</param>
        /// <param name="status">The finding status.</param>
        /// <param name="title">The finding title.</param>
        /// <param name="knowledgeKind">The knowledge classification for the finding.</param>
        /// <param name="metadata">The canonical metadata payload for diff-relevant finding details.</param>
        /// <returns>A deterministic finding fingerprint.</returns>
        public static Fingerprint ForFinding(string? ruleCode, string? ruleVersion, FindingSeverity severity, FindingStatus status, string? title, KnowledgeKind knowledgeKind, GraphMetadata metadata)
        {
            // Finding fingerprints include rule identity and visible state, but not transient first/last seen database IDs.
            return FromInput(FingerprintInput.Create("Finding")
                .AddField("ruleCode", RequireText(ruleCode, nameof(ruleCode)))
                .AddField("ruleVersion", RequireText(ruleVersion, nameof(ruleVersion)))
                .AddField("severity", RequireValue(severity, nameof(severity)).Value)
                .AddField("status", RequireValue(status, nameof(status)).Value)
                .AddField("title", RequireText(title, nameof(title)))
                .AddField("knowledgeKind", RequireValue(knowledgeKind, nameof(knowledgeKind)).Value)
                .AddMetadata(metadata));
        }

        /// <summary>
        /// Generates a metric fingerprint from normalized metric fields.
        /// </summary>
        /// <param name="metricName">The metric name.</param>
        /// <param name="scopeKind">The metric scope kind.</param>
        /// <param name="scopeIdentity">The stable scope identity or discriminator.</param>
        /// <param name="metadata">The canonical metadata payload for diff-relevant metric details.</param>
        /// <returns>A deterministic metric fingerprint.</returns>
        public static Fingerprint ForMetric(string? metricName, MetricScopeKind scopeKind, string? scopeIdentity, GraphMetadata metadata)
        {
            // Metric fingerprints include scope and metadata so changed computed values or classifications can be detected later.
            return FromInput(FingerprintInput.Create("Metric")
                .AddField("metricName", RequireText(metricName, nameof(metricName)))
                .AddField("scopeKind", RequireValue(scopeKind, nameof(scopeKind)).Value)
                .AddField("scopeIdentity", RequireText(scopeIdentity, nameof(scopeIdentity)))
                .AddMetadata(metadata));
        }

        /// <summary>
        /// Generates a generated-summary fingerprint from normalized summary fields.
        /// </summary>
        /// <param name="summaryKind">The generated-summary kind.</param>
        /// <param name="title">The summary title.</param>
        /// <param name="format">The generated content format.</param>
        /// <param name="content">The generated summary content.</param>
        /// <param name="metadata">The canonical metadata payload for diff-relevant summary details.</param>
        /// <returns>A deterministic generated-summary fingerprint.</returns>
        public static Fingerprint ForGeneratedSummary(SummaryKind summaryKind, string? title, string? format, string? content, GraphMetadata metadata)
        {
            // Summary fingerprints include generated content so changed narrative output can be diffed by later packages.
            return FromInput(FingerprintInput.Create("GeneratedSummary")
                .AddField("summaryKind", RequireValue(summaryKind, nameof(summaryKind)).Value)
                .AddField("title", RequireText(title, nameof(title)))
                .AddField("format", RequireText(format, nameof(format)))
                .AddField("content", RequireText(content, nameof(content)))
                .AddMetadata(metadata));
        }

        /// <summary>
        /// Requires a non-empty text field for fingerprint input.
        /// </summary>
        /// <param name="value">The candidate text value.</param>
        /// <param name="parameterName">The parameter name to report in validation failures.</param>
        /// <returns>The trimmed text value.</returns>
        private static string RequireText(string? value, string parameterName)
        {
            // Required fingerprint fields must be explicit so missing content cannot hash as a meaningful fact.
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Fingerprint input fields cannot be null, empty, or whitespace.", parameterName);
            }

            return value.Trim();
        }

        /// <summary>
        /// Requires a non-null reference value for fingerprint input.
        /// </summary>
        /// <typeparam name="TValue">The reference type being validated.</typeparam>
        /// <param name="value">The candidate reference value.</param>
        /// <param name="parameterName">The parameter name to report in validation failures.</param>
        /// <returns>The non-null reference value.</returns>
        private static TValue RequireValue<TValue>(TValue? value, string parameterName)
            where TValue : class
        {
            // Controlled values and metadata must be supplied explicitly so fingerprints describe complete logical content.
            return value ?? throw new ArgumentNullException(parameterName);
        }
    }
}
