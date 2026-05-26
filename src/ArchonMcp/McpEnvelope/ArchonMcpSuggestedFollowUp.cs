namespace ArchonMcp.McpEnvelope
{
    /// <summary>
    /// Represents a safe next investigation step suggested by an MCP response.
    /// </summary>
    public sealed record ArchonMcpSuggestedFollowUp
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ArchonMcpSuggestedFollowUp" /> record.
        /// </summary>
        /// <param name="label">The human-readable follow-up label.</param>
        /// <param name="operation">The supported Archon MCP operation, supported resource, API route, or safe user-question marker.</param>
        /// <param name="parameters">Safe stable-key-based parameters for the suggested follow-up, when available.</param>
        public ArchonMcpSuggestedFollowUp(string label, string operation, IReadOnlyDictionary<string, string>? parameters)
        {
            // Follow-ups are constrained to safe investigation paths instead of arbitrary shell, database, or filesystem actions.
            Label = label;
            Operation = operation;
            Parameters = parameters is null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(parameters, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Gets the human-readable follow-up label.
        /// </summary>
        public string Label { get; init; }

        /// <summary>
        /// Gets the supported Archon MCP operation, supported resource, API route, or safe user-question marker.
        /// </summary>
        public string Operation { get; init; }

        /// <summary>
        /// Gets safe stable-key-based parameters for the suggested follow-up.
        /// </summary>
        public IReadOnlyDictionary<string, string> Parameters { get; init; }
    }
}
