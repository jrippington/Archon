namespace ArchonMcp.McpRuntime
{
    /// <summary>
    /// Identifies the category of a registered Archon MCP capability.
    /// </summary>
    /// <remarks>
    /// The baseline runtime uses capability kinds to validate the required catalog without exposing arbitrary tool, resource,
    /// or prompt surfaces. Later WP015 slices can add concrete entries while preserving this stable categorization.
    /// </remarks>
    public enum ArchonMcpCapabilityKind
    {
        /// <summary>
        /// Represents an MCP tool that can be invoked by a client through an approved read-only operation.
        /// </summary>
        Tool,

        /// <summary>
        /// Represents an MCP resource that can be addressed by a stable Archon resource URI.
        /// </summary>
        Resource,

        /// <summary>
        /// Represents an MCP prompt template that guides a client through an approved analysis workflow.
        /// </summary>
        Prompt,

        /// <summary>
        /// Represents an operational host capability that proves the MCP runtime is composed safely.
        /// </summary>
        Operational
    }
}
