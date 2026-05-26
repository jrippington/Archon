namespace ArchonMcp.McpSearch
{
    /// <summary>
    /// Represents one deterministic group of <c>archon.search</c> results that share the same result kind.
    /// </summary>
    /// <param name="ResultKind">The controlled result kind for the group.</param>
    /// <param name="Results">The ordered search results that belong to the group.</param>
    public sealed record ArchonMcpSearchResultGroup(string ResultKind, IReadOnlyList<ArchonMcpSearchResultItem> Results)
    {
        // Grouping keeps MCP responses readable for AI clients while preserving deterministic ordering inside each group.
    }
}
