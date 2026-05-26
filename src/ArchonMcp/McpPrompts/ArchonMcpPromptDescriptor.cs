namespace ArchonMcp.McpPrompts
{
    /// <summary>
    /// Provides prompt list metadata without returning the full template body.
    /// </summary>
    /// <param name="Name">The stable prompt name that can be passed to prompt retrieval.</param>
    /// <param name="Version">The prompt template version currently registered by the host.</param>
    /// <param name="Summary">A short description of the read-only workflow covered by the prompt.</param>
    public sealed record ArchonMcpPromptDescriptor(
        string Name,
        int Version,
        string Summary);
}
