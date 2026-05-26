namespace ArchonMcp.McpHotlist
{
    /// <summary>
    /// Carries the structured facts returned by the hotlist findings MCP tool.
    /// </summary>
    /// <param name="SnapshotSelector">The requested snapshot selector or stable snapshot key.</param>
    /// <param name="ProjectStableKey">The affected project stable-key filter applied when supplied.</param>
    /// <param name="RuleCodeFilter">The rule-code filter applied when supplied.</param>
    /// <param name="CategoryFilter">The category filter applied when supplied.</param>
    /// <param name="SeverityFilter">The severity filter applied when supplied.</param>
    /// <param name="StatusFilter">The lifecycle status filter applied when supplied.</param>
    /// <param name="SearchText">The MCP-side safe text search filter applied when supplied.</param>
    /// <param name="SortBy">The deterministic sort field used for returned findings.</param>
    /// <param name="TotalMatchingFindings">The total number of matching findings before MCP response limiting.</param>
    /// <param name="Findings">The bounded deterministic hotlist finding records returned to the caller.</param>
    public sealed record ArchonMcpHotlistFindingsFacts(
        string? SnapshotSelector,
        string? ProjectStableKey,
        string? RuleCodeFilter,
        string? CategoryFilter,
        string? SeverityFilter,
        string? StatusFilter,
        string? SearchText,
        string SortBy,
        int TotalMatchingFindings,
        IReadOnlyList<ArchonMcpHotlistFindingRecord> Findings);
}
