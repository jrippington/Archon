namespace ArchonMcp.McpSearch
{
    /// <summary>
    /// Represents the external request contract accepted by the <c>archon.search</c> MCP tool.
    /// </summary>
    /// <param name="SearchText">The required text matched against persisted architecture facts and safe evidence summaries.</param>
    /// <param name="SnapshotSelector">The optional snapshot selector, such as <c>latest</c> or a <c>snapshot://</c> stable key.</param>
    /// <param name="ResultTypeFilters">The optional controlled result-type filters, such as <c>Project</c> or <c>Symbol</c>.</param>
    /// <param name="RepositoryStableKey">The optional repository stable key that bounds snapshot selection when supported by the query layer.</param>
    /// <param name="SolutionStableKey">The optional solution stable key that narrows repository scope when supported by the query layer.</param>
    /// <param name="ProjectStableKey">The optional project stable key that narrows matched records when supported by the query layer.</param>
    /// <param name="Limit">The optional maximum number of result rows to return after MCP and query-layer limits are applied.</param>
    public sealed record ArchonMcpSearchRequest(
        string? SearchText,
        string? SnapshotSelector,
        IReadOnlyList<string>? ResultTypeFilters,
        string? RepositoryStableKey,
        string? SolutionStableKey,
        string? ProjectStableKey,
        int? Limit);
}
