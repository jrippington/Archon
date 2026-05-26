namespace ArchonMcp.McpPrompts
{
    /// <summary>
    /// Carries the registered prompt inventory returned by the prompt-listing operation.
    /// </summary>
    /// <param name="Prompts">The stable prompt descriptors available to authorized MCP clients.</param>
    /// <param name="TotalPromptCount">The total number of registered read-only prompt templates.</param>
    public sealed record ArchonMcpPromptListFacts(
        IReadOnlyList<ArchonMcpPromptDescriptor> Prompts,
        int TotalPromptCount);
}
