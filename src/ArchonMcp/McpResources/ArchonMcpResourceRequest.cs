namespace ArchonMcp.McpResources
{
    /// <summary>
    /// Represents a validated Archon MCP resource URI request.
    /// </summary>
    /// <param name="OriginalUri">The original URI text supplied by the caller.</param>
    /// <param name="CanonicalUri">The canonical resource URI without query parameters.</param>
    /// <param name="Family">The supported resource family selected by the URI host.</param>
    /// <param name="Selector">The selector segment requested for the resource family.</param>
    /// <param name="RepositoryStableKey">The repository stable key that scopes current resource resolution or optionally narrows parameterized resources.</param>
    /// <param name="SolutionStableKey">The optional solution stable key that narrows current resource resolution.</param>
    /// <param name="Limit">The optional caller-requested resource item limit.</param>
    /// <param name="Category">The optional category filter for list resources.</param>
    /// <param name="Severity">The optional severity filter for hotlist resources.</param>
    /// <param name="Status">The optional status filter for hotlist resources.</param>
    /// <param name="ProjectStableKey">The optional project stable key selected by a parameterized project resource URI.</param>
    /// <param name="SymbolStableKey">The optional symbol stable key selected by a parameterized symbol resource URI.</param>
    /// <param name="CurrentSnapshotStableKey">The optional current snapshot stable key selected by a parameterized snapshot diff resource URI.</param>
    /// <param name="PreviousSnapshotStableKey">The optional previous snapshot stable key selected by a parameterized snapshot diff resource URI.</param>
    /// <param name="IncludeDetails">The optional flag indicating whether bounded snapshot diff details should be returned.</param>
    public sealed record ArchonMcpResourceRequest(
        string OriginalUri,
        string CanonicalUri,
        ArchonMcpResourceFamily Family,
        string Selector,
        string? RepositoryStableKey,
        string? SolutionStableKey,
        int? Limit,
        string? Category,
        string? Severity,
        string? Status,
        string? ProjectStableKey = null,
        string? SymbolStableKey = null,
        string? CurrentSnapshotStableKey = null,
        string? PreviousSnapshotStableKey = null,
        bool? IncludeDetails = null);
}
