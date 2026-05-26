namespace ArchonMcp.McpEnvelope
{
    /// <summary>
    /// Represents the common MCP success response envelope shared by Archon tools and resources.
    /// </summary>
    /// <typeparam name="TFacts">The typed facts section returned by the operation.</typeparam>
    public sealed record ArchonMcpEnvelope<TFacts>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ArchonMcpEnvelope{TFacts}" /> record.
        /// </summary>
        /// <param name="operation">The stable MCP operation name, such as a tool name, resource read, prompt retrieval, or operational capability.</param>
        /// <param name="snapshot">The snapshot identity when the response depends on persisted snapshot state.</param>
        /// <param name="summary">The concise natural-language summary grounded only in returned facts, evidence, findings, and unknowns.</param>
        /// <param name="confidence">The overall response confidence.</param>
        /// <param name="facts">The typed structured facts returned by the operation.</param>
        /// <param name="evidence">The safe evidence references that support returned facts.</param>
        /// <param name="findings">The related safe finding references.</param>
        /// <param name="unknowns">The explicit unknowns that prevent unsupported conclusions.</param>
        /// <param name="warnings">The safe warnings about partial, degraded, or truncated content.</param>
        /// <param name="limits">The applied limit and truncation metadata.</param>
        /// <param name="suggestedFollowUps">The safe suggested follow-up operations or user questions.</param>
        public ArchonMcpEnvelope(
            string operation,
            ArchonMcpSnapshotIdentity? snapshot,
            string summary,
            ArchonMcpConfidence confidence,
            TFacts facts,
            IEnumerable<ArchonMcpEvidenceReference>? evidence,
            IEnumerable<ArchonMcpFindingReference>? findings,
            IEnumerable<ArchonMcpUnknown>? unknowns,
            IEnumerable<ArchonMcpWarning>? warnings,
            ArchonMcpLimitMetadata limits,
            IEnumerable<ArchonMcpSuggestedFollowUp>? suggestedFollowUps)
        {
            // The envelope snapshots collection sections so callers receive stable response data after mapping completes.
            Operation = operation;
            Snapshot = snapshot;
            Summary = summary;
            Confidence = confidence ?? throw new ArgumentNullException(nameof(confidence));
            Facts = facts;
            Evidence = evidence?.ToArray() ?? [];
            Findings = findings?.ToArray() ?? [];
            Unknowns = unknowns?.ToArray() ?? [];
            Warnings = warnings?.ToArray() ?? [];
            Limits = limits ?? throw new ArgumentNullException(nameof(limits));
            SuggestedFollowUps = suggestedFollowUps?.ToArray() ?? [];
        }

        /// <summary>
        /// Gets the stable MCP operation name.
        /// </summary>
        public string Operation { get; init; }

        /// <summary>
        /// Gets the snapshot identity when the response depends on persisted snapshot state.
        /// </summary>
        public ArchonMcpSnapshotIdentity? Snapshot { get; init; }

        /// <summary>
        /// Gets the concise natural-language summary grounded only in returned data.
        /// </summary>
        public string Summary { get; init; }

        /// <summary>
        /// Gets the overall response confidence.
        /// </summary>
        public ArchonMcpConfidence Confidence { get; init; }

        /// <summary>
        /// Gets the typed structured facts returned by the operation.
        /// </summary>
        public TFacts Facts { get; init; }

        /// <summary>
        /// Gets the safe evidence references that support returned facts.
        /// </summary>
        public IReadOnlyList<ArchonMcpEvidenceReference> Evidence { get; init; }

        /// <summary>
        /// Gets the related safe finding references.
        /// </summary>
        public IReadOnlyList<ArchonMcpFindingReference> Findings { get; init; }

        /// <summary>
        /// Gets the explicit unknowns that prevent unsupported conclusions.
        /// </summary>
        public IReadOnlyList<ArchonMcpUnknown> Unknowns { get; init; }

        /// <summary>
        /// Gets the safe warnings about partial, degraded, or truncated content.
        /// </summary>
        public IReadOnlyList<ArchonMcpWarning> Warnings { get; init; }

        /// <summary>
        /// Gets the applied limit and truncation metadata.
        /// </summary>
        public ArchonMcpLimitMetadata Limits { get; init; }

        /// <summary>
        /// Gets the safe suggested follow-up operations or user questions.
        /// </summary>
        public IReadOnlyList<ArchonMcpSuggestedFollowUp> SuggestedFollowUps { get; init; }
    }
}
