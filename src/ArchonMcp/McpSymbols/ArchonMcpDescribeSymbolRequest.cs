namespace ArchonMcp.McpSymbols
{
    /// <summary>
    /// Represents an MCP request to describe one symbol by stable key or exact unambiguous search text.
    /// </summary>
    /// <param name="SymbolStableKey">The optional exact stable symbol key.</param>
    /// <param name="SearchText">The optional exact symbol search text used when a stable key is unavailable.</param>
    /// <param name="SnapshotSelector">The optional snapshot selector, either <c>latest</c> or a stable snapshot key.</param>
    /// <param name="RepositoryStableKey">The repository stable key that bounds snapshot resolution.</param>
    /// <param name="SolutionStableKey">The optional solution stable key that narrows repository scope.</param>
    public sealed record ArchonMcpDescribeSymbolRequest(
        string? SymbolStableKey,
        string? SearchText,
        string? SnapshotSelector,
        string? RepositoryStableKey,
        string? SolutionStableKey);
}
