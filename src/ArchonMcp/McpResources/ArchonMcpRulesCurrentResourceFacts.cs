using ArchonMcp.McpRules;

namespace ArchonMcp.McpResources
{
    /// <summary>
    /// Carries structured facts returned by the current architecture rules MCP resource.
    /// </summary>
    /// <param name="ResourceUri">The canonical resource URI that produced these facts.</param>
    /// <param name="SnapshotStableKey">The selected current snapshot stable key.</param>
    /// <param name="RepositoryStableKey">The repository stable key that scoped current selection.</param>
    /// <param name="SolutionStableKey">The optional solution stable key that scoped current selection.</param>
    /// <param name="CategoryFilter">The optional rule category filter applied to the resource.</param>
    /// <param name="TotalMatchingRules">The total number of matching rules before MCP limiting.</param>
    /// <param name="Rules">The bounded architecture rule records returned by the resource.</param>
    public sealed record ArchonMcpRulesCurrentResourceFacts(
        string ResourceUri,
        string SnapshotStableKey,
        string RepositoryStableKey,
        string? SolutionStableKey,
        string? CategoryFilter,
        int TotalMatchingRules,
        IReadOnlyList<ArchonMcpArchitectureRuleRecord> Rules);
}
