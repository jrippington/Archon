using ArchonMcp.McpHotlist;

namespace ArchonMcp.McpResources
{
    /// <summary>
    /// Carries structured facts returned by the current hotlist MCP resource.
    /// </summary>
    /// <param name="ResourceUri">The canonical resource URI that produced these facts.</param>
    /// <param name="SnapshotStableKey">The selected current snapshot stable key.</param>
    /// <param name="RepositoryStableKey">The repository stable key that scoped current selection.</param>
    /// <param name="SolutionStableKey">The optional solution stable key that scoped current selection.</param>
    /// <param name="CategoryFilter">The optional category filter applied to the resource.</param>
    /// <param name="SeverityFilter">The optional severity filter applied to the resource.</param>
    /// <param name="StatusFilter">The optional status filter applied to the resource.</param>
    /// <param name="TotalMatchingFindings">The total number of matching findings before MCP limiting.</param>
    /// <param name="Findings">The bounded hotlist finding records returned by the resource.</param>
    public sealed record ArchonMcpHotlistCurrentResourceFacts(
        string ResourceUri,
        string SnapshotStableKey,
        string RepositoryStableKey,
        string? SolutionStableKey,
        string? CategoryFilter,
        string? SeverityFilter,
        string? StatusFilter,
        int TotalMatchingFindings,
        IReadOnlyList<ArchonMcpHotlistFindingRecord> Findings);
}
