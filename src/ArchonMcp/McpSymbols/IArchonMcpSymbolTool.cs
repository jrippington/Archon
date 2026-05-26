namespace ArchonMcp.McpSymbols
{
    /// <summary>
    /// Defines read-only MCP symbol description and usage investigation tool operations.
    /// </summary>
    public interface IArchonMcpSymbolTool
    {
        /// <summary>
        /// Describes one symbol by stable key or exact unambiguous search text.
        /// </summary>
        /// <param name="request">The symbol description request containing identity and scope fields.</param>
        /// <param name="cancellationToken">The token that cancels symbol detail lookup before or during query-layer execution.</param>
        /// <returns>A symbol description envelope or structured MCP error response.</returns>
        Task<object> DescribeSymbolAsync(ArchonMcpDescribeSymbolRequest request, CancellationToken cancellationToken);

        /// <summary>
        /// Lists bounded usages of one symbol.
        /// </summary>
        /// <param name="request">The symbol usage request containing identity, filters, scope, and limits.</param>
        /// <param name="cancellationToken">The token that cancels symbol usage lookup before or during query-layer execution.</param>
        /// <returns>A symbol usage envelope or structured MCP error response.</returns>
        Task<object> FindSymbolUsagesAsync(ArchonMcpFindSymbolUsagesRequest request, CancellationToken cancellationToken);
    }
}
