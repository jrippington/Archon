namespace ArchonMcp.McpPrompts
{
    /// <summary>
    /// Describes one read-only MCP prompt template loaded from the Archon MCP assembly resources.
    /// </summary>
    /// <param name="Name">The stable prompt name requested by MCP clients.</param>
    /// <param name="Version">The prompt asset version used for compatibility and audit context.</param>
    /// <param name="Summary">A short, secret-safe description of the prompt workflow.</param>
    /// <param name="ResourceName">The embedded manifest resource name used to load the template content.</param>
    /// <param name="Content">The markdown template text returned to authorized MCP clients.</param>
    public sealed record ArchonMcpPromptTemplate(
        string Name,
        int Version,
        string Summary,
        string ResourceName,
        string Content);
}
