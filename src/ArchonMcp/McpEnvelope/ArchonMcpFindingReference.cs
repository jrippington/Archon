namespace ArchonMcp.McpEnvelope
{
    /// <summary>
    /// Represents a safe finding reference related to an MCP response.
    /// </summary>
    public sealed record ArchonMcpFindingReference
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ArchonMcpFindingReference" /> record.
        /// </summary>
        /// <param name="stableKey">The stable finding key returned by the query layer.</param>
        /// <param name="ruleCode">The stable rule code that produced or classifies the finding.</param>
        /// <param name="ruleVersion">The rule version associated with the finding when available.</param>
        /// <param name="severity">The safe severity or review classification for the finding.</param>
        /// <param name="status">The current finding status, such as active or suppressed, when available.</param>
        /// <param name="confidence">The confidence attached to the finding reference.</param>
        /// <param name="affectedStableKeys">The stable public identities for affected nodes, edges, or records.</param>
        /// <param name="evidenceStableKeys">The stable evidence identities that support the finding.</param>
        public ArchonMcpFindingReference(
            string stableKey,
            string ruleCode,
            string? ruleVersion,
            string severity,
            string status,
            ArchonMcpConfidence confidence,
            IEnumerable<string>? affectedStableKeys,
            IEnumerable<string>? evidenceStableKeys)
        {
            // Finding references keep rule and target identity explicit while avoiding rule internals or raw evidence content.
            StableKey = stableKey;
            RuleCode = ruleCode;
            RuleVersion = ruleVersion;
            Severity = severity;
            Status = status;
            Confidence = confidence ?? throw new ArgumentNullException(nameof(confidence));
            AffectedStableKeys = affectedStableKeys?.ToArray() ?? [];
            EvidenceStableKeys = evidenceStableKeys?.ToArray() ?? [];
        }

        /// <summary>
        /// Gets the stable finding key returned by the query layer.
        /// </summary>
        public string StableKey { get; init; }

        /// <summary>
        /// Gets the stable rule code that produced or classifies the finding.
        /// </summary>
        public string RuleCode { get; init; }

        /// <summary>
        /// Gets the rule version associated with the finding when available.
        /// </summary>
        public string? RuleVersion { get; init; }

        /// <summary>
        /// Gets the safe severity or review classification for the finding.
        /// </summary>
        public string Severity { get; init; }

        /// <summary>
        /// Gets the current finding status when available.
        /// </summary>
        public string Status { get; init; }

        /// <summary>
        /// Gets the confidence attached to the finding reference.
        /// </summary>
        public ArchonMcpConfidence Confidence { get; init; }

        /// <summary>
        /// Gets the stable public identities for affected nodes, edges, or records.
        /// </summary>
        public IReadOnlyList<string> AffectedStableKeys { get; init; }

        /// <summary>
        /// Gets the stable evidence identities that support the finding.
        /// </summary>
        public IReadOnlyList<string> EvidenceStableKeys { get; init; }
    }
}
