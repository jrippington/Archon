namespace ArchonMcp.McpEnvelope
{
    /// <summary>
    /// Represents a bounded list returned by MCP limit enforcement together with truncation metadata and follow-ups.
    /// </summary>
    /// <typeparam name="TItem">The item type contained in the bounded list.</typeparam>
    public sealed record ArchonMcpLimitedList<TItem>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ArchonMcpLimitedList{TItem}" /> record.
        /// </summary>
        /// <param name="items">The bounded items returned after limit enforcement.</param>
        /// <param name="limits">The limit metadata describing the applied bound and truncation state.</param>
        /// <param name="suggestedFollowUps">The safe suggested follow-ups for narrowing or continuing investigation.</param>
        public ArchonMcpLimitedList(
            IEnumerable<TItem> items,
            ArchonMcpLimitMetadata limits,
            IEnumerable<ArchonMcpSuggestedFollowUp>? suggestedFollowUps)
        {
            // The limited list snapshots items and follow-ups so mappers can safely reuse the value while building envelopes.
            Items = items.ToArray();
            Limits = limits ?? throw new ArgumentNullException(nameof(limits));
            SuggestedFollowUps = suggestedFollowUps?.ToArray() ?? [];
        }

        /// <summary>
        /// Gets the bounded items returned after limit enforcement.
        /// </summary>
        public IReadOnlyList<TItem> Items { get; init; }

        /// <summary>
        /// Gets the limit metadata describing the applied bound and truncation state.
        /// </summary>
        public ArchonMcpLimitMetadata Limits { get; init; }

        /// <summary>
        /// Gets the safe suggested follow-ups for narrowing or continuing investigation.
        /// </summary>
        public IReadOnlyList<ArchonMcpSuggestedFollowUp> SuggestedFollowUps { get; init; }
    }
}
