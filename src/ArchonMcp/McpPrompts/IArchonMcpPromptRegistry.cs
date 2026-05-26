namespace ArchonMcp.McpPrompts
{
    /// <summary>
    /// Provides read-only access to versioned MCP prompt templates registered by the host.
    /// </summary>
    public interface IArchonMcpPromptRegistry
    {
        /// <summary>
        /// Lists the prompt templates that are available for retrieval.
        /// </summary>
        /// <returns>The registered prompt descriptors ordered by stable prompt name.</returns>
        IReadOnlyList<ArchonMcpPromptDescriptor> ListPrompts();

        /// <summary>
        /// Attempts to resolve one prompt template by stable name.
        /// </summary>
        /// <param name="name">The stable prompt name requested by the caller.</param>
        /// <param name="template">The resolved prompt template when the method returns <see langword="true" />.</param>
        /// <returns><see langword="true" /> when a prompt with the requested name is registered; otherwise, <see langword="false" />.</returns>
        bool TryGetPrompt(string name, out ArchonMcpPromptTemplate? template);
    }
}
