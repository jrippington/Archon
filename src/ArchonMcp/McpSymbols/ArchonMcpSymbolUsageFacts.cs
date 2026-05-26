namespace ArchonMcp.McpSymbols
{
    /// <summary>
    /// Represents the structured facts returned by <c>archon.find_symbol_usages</c>.
    /// </summary>
    /// <param name="SymbolStableKey">The stable symbol key whose usages were requested.</param>
    /// <param name="UsageKindFilters">The usage-kind filters applied to returned rows.</param>
    /// <param name="ProjectStableKey">The optional project filter applied to returned rows.</param>
    /// <param name="MaximumDepth">The optional usage investigation depth requested by the caller.</param>
    /// <param name="TotalCount">The total matching usage count before MCP limiting where known.</param>
    /// <param name="Usages">The bounded deterministic usage records returned to the MCP client.</param>
    public sealed record ArchonMcpSymbolUsageFacts(
        string SymbolStableKey,
        IReadOnlyList<string> UsageKindFilters,
        string? ProjectStableKey,
        int? MaximumDepth,
        int TotalCount,
        IReadOnlyList<ArchonMcpSymbolUsageRecord> Usages);
}
