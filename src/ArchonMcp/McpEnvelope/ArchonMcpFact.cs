namespace ArchonMcp.McpEnvelope
{
    /// <summary>
    /// Represents one structured architecture fact returned in an MCP response envelope.
    /// </summary>
    public sealed record ArchonMcpFact
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ArchonMcpFact" /> record.
        /// </summary>
        /// <param name="stableKey">The stable public identity for the fact or related graph record; Neo4j internal identifiers are not allowed.</param>
        /// <param name="kind">The controlled fact kind, such as project, symbol, endpoint, dependency, metric, or operational capability.</param>
        /// <param name="label">The concise display label for the fact.</param>
        /// <param name="summary">The safe fact summary grounded in returned data.</param>
        /// <param name="confidence">The confidence attached to the fact.</param>
        /// <param name="metadata">Safe lower-camel-case metadata values that do not contain secrets or raw graph internals.</param>
        public ArchonMcpFact(
            string stableKey,
            string kind,
            string label,
            string summary,
            ArchonMcpConfidence confidence,
            IReadOnlyDictionary<string, string>? metadata)
        {
            // Facts carry stable public identity and bounded metadata so responses remain deterministic and safe for AI context.
            StableKey = stableKey;
            Kind = kind;
            Label = label;
            Summary = summary;
            Confidence = confidence ?? throw new ArgumentNullException(nameof(confidence));
            Metadata = metadata is null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Gets the stable public identity for the fact or related graph record.
        /// </summary>
        public string StableKey { get; init; }

        /// <summary>
        /// Gets the controlled fact kind.
        /// </summary>
        public string Kind { get; init; }

        /// <summary>
        /// Gets the concise display label for the fact.
        /// </summary>
        public string Label { get; init; }

        /// <summary>
        /// Gets the safe summary grounded in returned data.
        /// </summary>
        public string Summary { get; init; }

        /// <summary>
        /// Gets the confidence attached to the fact.
        /// </summary>
        public ArchonMcpConfidence Confidence { get; init; }

        /// <summary>
        /// Gets safe lower-camel-case metadata values for the fact.
        /// </summary>
        public IReadOnlyDictionary<string, string> Metadata { get; init; }
    }
}
