namespace ArchonMcp.McpEnvelope
{
    /// <summary>
    /// Describes the confidence assigned to a response element and the safe rationale for that confidence.
    /// </summary>
    public sealed record ArchonMcpConfidence
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ArchonMcpConfidence" /> record.
        /// </summary>
        /// <param name="level">The confidence classification derived from persisted facts, evidence, findings, metrics, or explicit unknowns.</param>
        /// <param name="reason">The safe explanation for the confidence level; it must not contain secrets, stack traces, or unsafe evidence snippets.</param>
        public ArchonMcpConfidence(ArchonMcpConfidenceLevel level, string reason)
        {
            // The constructor stores only safe explanatory text so summaries can reference confidence without exposing raw internals.
            Level = level;
            Reason = string.IsNullOrWhiteSpace(reason) ? "Confidence reason was not supplied." : reason;
        }

        /// <summary>
        /// Gets the confidence classification derived from persisted Archon data.
        /// </summary>
        public ArchonMcpConfidenceLevel Level { get; init; }

        /// <summary>
        /// Gets the safe explanation for why this confidence level was assigned.
        /// </summary>
        public string Reason { get; init; }
    }
}
