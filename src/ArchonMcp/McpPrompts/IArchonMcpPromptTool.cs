namespace ArchonMcp.McpPrompts
{
    /// <summary>
    /// Exposes authorized read-only prompt listing and retrieval behavior for MCP clients.
    /// </summary>
    public interface IArchonMcpPromptTool
    {
        /// <summary>
        /// Lists all registered prompt templates through the MCP security and audit pipeline.
        /// </summary>
        /// <param name="cancellationToken">The token that observes cancellation before the operation body executes.</param>
        /// <returns>A prompt-list envelope or a structured safe error response.</returns>
        Task<object> ListPromptsAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Retrieves one registered prompt template through the MCP security and audit pipeline.
        /// </summary>
        /// <param name="request">The prompt retrieval request containing the stable prompt name.</param>
        /// <param name="cancellationToken">The token that observes cancellation before the operation body executes.</param>
        /// <returns>A prompt envelope or a structured safe error response.</returns>
        Task<object> GetPromptAsync(ArchonMcpPromptRequest request, CancellationToken cancellationToken);
    }
}
