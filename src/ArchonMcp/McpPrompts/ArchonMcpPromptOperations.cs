namespace ArchonMcp.McpPrompts
{
    /// <summary>
    /// Defines stable operation names used by the MCP prompt retrieval and listing pipeline.
    /// </summary>
    public static class ArchonMcpPromptOperations
    {
        /// <summary>
        /// Gets the stable operation name used to retrieve one read-only prompt template.
        /// </summary>
        public const string GetPrompt = "archon.get_prompt";

        /// <summary>
        /// Gets the stable operation name used to list registered read-only prompt templates.
        /// </summary>
        public const string ListPrompts = "archon.list_prompts";
    }
}
