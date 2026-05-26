namespace ArchonMcp.McpRuntime
{
    /// <summary>
    /// Configures conservative response and traversal limits for the Archon MCP runtime surface.
    /// </summary>
    /// <remarks>
    /// These defaults establish the baseline safety posture before concrete tools and resources exist. Later slices should consume
    /// the same options when enforcing result counts, evidence counts, traversal depth, path counts, and serialized context budgets.
    /// </remarks>
    public sealed class ArchonMcpLimitsOptions
    {
        /// <summary>
        /// Gets the configuration section name used to bind MCP limit settings.
        /// </summary>
        public const string SectionName = "Archon:Mcp:Limits";

        /// <summary>
        /// Gets or sets the maximum number of result items that a single MCP response should include by default.
        /// </summary>
        public int MaxResultCount { get; set; } = 25;

        /// <summary>
        /// Gets or sets the maximum graph traversal depth that future MCP dependency tools should allow by default.
        /// </summary>
        public int MaxTraversalDepth { get; set; } = 3;

        /// <summary>
        /// Gets or sets the maximum number of evidence references that a single MCP response should include by default.
        /// </summary>
        public int MaxEvidenceCount { get; set; } = 10;

        /// <summary>
        /// Gets or sets the maximum number of dependency paths that a single MCP path response should include by default.
        /// </summary>
        public int MaxPathCount { get; set; } = 5;

        /// <summary>
        /// Gets or sets the approximate serialized character budget for a single MCP response payload.
        /// </summary>
        public int MaxSerializedContextCharacters { get; set; } = 24000;
    }
}
