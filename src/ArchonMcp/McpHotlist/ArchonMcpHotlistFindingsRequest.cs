namespace ArchonMcp.McpHotlist
{
    /// <summary>
    /// Describes read-only filters for the <c>archon.get_hotlist_findings</c> MCP tool.
    /// </summary>
    /// <param name="ProjectStableKey">The optional affected project stable-key filter.</param>
    /// <param name="RuleCode">The optional exact rule-code filter.</param>
    /// <param name="Category">The optional exact rule category filter.</param>
    /// <param name="Severity">The optional exact finding severity filter.</param>
    /// <param name="Status">The optional exact finding lifecycle status filter.</param>
    /// <param name="SnapshotSelector">The optional snapshot selector or stable snapshot key.</param>
    /// <param name="SearchText">The optional text search filter applied by MCP over returned safe summary fields.</param>
    /// <param name="SortBy">The optional deterministic sort field; supported values include severity, latestSeen, ruleCode, and stableKey.</param>
    /// <param name="Limit">The optional maximum number of hotlist finding records returned by MCP.</param>
    /// <param name="RepositoryStableKey">The optional repository stable key retained for audit and future query scopes.</param>
    /// <param name="SolutionStableKey">The optional solution stable key retained for audit and future query scopes.</param>
    public sealed record ArchonMcpHotlistFindingsRequest(
        string? ProjectStableKey,
        string? RuleCode,
        string? Category,
        string? Severity,
        string? Status,
        string? SnapshotSelector,
        string? SearchText,
        string? SortBy,
        int? Limit,
        string? RepositoryStableKey,
        string? SolutionStableKey);
}
