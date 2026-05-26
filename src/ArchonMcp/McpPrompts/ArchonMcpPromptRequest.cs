namespace ArchonMcp.McpPrompts
{
    /// <summary>
    /// Represents a request to retrieve one registered read-only MCP prompt template.
    /// </summary>
    public sealed record ArchonMcpPromptRequest
    {
        /// <summary>
        /// Gets the stable prompt name requested by the caller.
        /// </summary>
        public string? Name { get; init; }
    }
}
