namespace ArchonMcp.McpHotlist
{
    /// <summary>
    /// Defines the read-only MCP hotlist findings tool contract.
    /// </summary>
    public interface IArchonMcpHotlistTool
    {
        /// <summary>
        /// Lists hotlist findings through controlled query abstractions.
        /// </summary>
        /// <param name="request">The read-only hotlist findings request.</param>
        /// <param name="cancellationToken">The token that can cancel query-layer execution before finding data is read.</param>
        /// <returns>A common MCP success envelope or a structured MCP error response.</returns>
        Task<object> GetHotlistFindingsAsync(ArchonMcpHotlistFindingsRequest request, CancellationToken cancellationToken);
    }
}
