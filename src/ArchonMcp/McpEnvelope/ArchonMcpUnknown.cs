namespace ArchonMcp.McpEnvelope
{
    /// <summary>
    /// Represents an explicit unknown so MCP clients can distinguish unavailable or uncertain data from known absence.
    /// </summary>
    public sealed record ArchonMcpUnknown
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ArchonMcpUnknown" /> record.
        /// </summary>
        /// <param name="kind">The controlled unknown kind, such as unavailable snapshot, unresolved symbol, truncated result, or missing evidence.</param>
        /// <param name="affectedStableKey">The stable key affected by the unknown when one is available.</param>
        /// <param name="reason">The safe reason that explains what is unknown.</param>
        /// <param name="confidenceImpact">The safe explanation of how the unknown affects response confidence.</param>
        /// <param name="suggestedFollowUp">The optional safe follow-up that can reduce or investigate the unknown.</param>
        public ArchonMcpUnknown(
            string kind,
            string? affectedStableKey,
            string reason,
            string confidenceImpact,
            ArchonMcpSuggestedFollowUp? suggestedFollowUp)
        {
            // Unknowns are first-class response data so later summaries do not treat missing information as a proven negative.
            Kind = kind;
            AffectedStableKey = affectedStableKey;
            Reason = reason;
            ConfidenceImpact = confidenceImpact;
            SuggestedFollowUp = suggestedFollowUp;
        }

        /// <summary>
        /// Gets the controlled unknown kind.
        /// </summary>
        public string Kind { get; init; }

        /// <summary>
        /// Gets the stable key affected by the unknown when one is available.
        /// </summary>
        public string? AffectedStableKey { get; init; }

        /// <summary>
        /// Gets the safe reason that explains what is unknown.
        /// </summary>
        public string Reason { get; init; }

        /// <summary>
        /// Gets the safe explanation of how the unknown affects response confidence.
        /// </summary>
        public string ConfidenceImpact { get; init; }

        /// <summary>
        /// Gets the optional safe follow-up that can reduce or investigate the unknown.
        /// </summary>
        public ArchonMcpSuggestedFollowUp? SuggestedFollowUp { get; init; }
    }
}
