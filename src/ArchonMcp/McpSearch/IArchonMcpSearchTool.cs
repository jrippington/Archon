using ArchonMcp.McpEnvelope;

namespace ArchonMcp.McpSearch
{
    /// <summary>
    /// Defines the read-only MCP search tool handler used by host endpoints and future protocol adapters.
    /// </summary>
    public interface IArchonMcpSearchTool
    {
        /// <summary>
        /// Executes an evidence-backed architecture search through the application/query layer.
        /// </summary>
        /// <param name="request">The caller-supplied MCP search request.</param>
        /// <param name="cancellationToken">The token that can cancel validation and query-layer execution.</param>
        /// <returns>A common MCP envelope with grouped search facts, or a structured MCP error response.</returns>
        Task<object> SearchAsync(ArchonMcpSearchRequest request, CancellationToken cancellationToken);
    }
}
