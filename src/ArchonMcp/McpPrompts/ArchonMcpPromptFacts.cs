namespace ArchonMcp.McpPrompts
{
    /// <summary>
    /// Carries the full text and metadata for one retrieved MCP prompt template.
    /// </summary>
    /// <param name="Name">The stable prompt name returned by the registry.</param>
    /// <param name="Version">The version number parsed from the embedded prompt asset.</param>
    /// <param name="Summary">A secret-safe summary of the prompt workflow.</param>
    /// <param name="Content">The read-only markdown template content.</param>
    public sealed record ArchonMcpPromptFacts(
        string Name,
        int Version,
        string Summary,
        string Content);
}
