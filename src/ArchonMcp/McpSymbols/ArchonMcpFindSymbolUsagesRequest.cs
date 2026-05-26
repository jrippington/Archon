namespace ArchonMcp.McpSymbols
{
    /// <summary>
    /// Represents an MCP request to find bounded usages of one symbol.
    /// </summary>
    /// <param name="SymbolStableKey">The optional stable symbol key whose usages should be listed.</param>
    /// <param name="SearchText">The optional exact symbol search text reserved for future unambiguous lookup support.</param>
    /// <param name="UsageKindFilters">The optional usage-kind filters applied to returned usage relationships.</param>
    /// <param name="ProjectStableKey">The optional owning project stable-key filter applied to usage rows.</param>
    /// <param name="MaximumDepth">The optional depth hint retained in the MCP contract for usage investigation workflows.</param>
    /// <param name="Limit">The optional maximum number of usage rows to return after MCP limiting.</param>
    /// <param name="SnapshotSelector">The optional snapshot selector, either <c>latest</c> or a stable snapshot key.</param>
    /// <param name="RepositoryStableKey">The repository stable key that bounds snapshot resolution.</param>
    /// <param name="SolutionStableKey">The optional solution stable key that narrows repository scope.</param>
    public sealed record ArchonMcpFindSymbolUsagesRequest(
        string? SymbolStableKey,
        string? SearchText,
        IReadOnlyList<string>? UsageKindFilters,
        string? ProjectStableKey,
        int? MaximumDepth,
        int? Limit,
        string? SnapshotSelector,
        string? RepositoryStableKey,
        string? SolutionStableKey);
}
