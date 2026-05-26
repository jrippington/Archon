namespace ArchonMcp.McpDataAccess
{
    /// <summary>
    /// Represents the structured facts section for <c>archon.get_data_access_usage</c> responses.
    /// </summary>
    /// <param name="ProjectStableKey">The requested project stable-key filter, when supplied.</param>
    /// <param name="DataContextStableKey">The requested data-context stable-key filter, when supplied.</param>
    /// <param name="Entity">The requested entity filter, when supplied.</param>
    /// <param name="Table">The requested table filter, when supplied.</param>
    /// <param name="StoredProcedure">The requested stored-procedure filter, when supplied.</param>
    /// <param name="Family">The requested data-access family filter, when supplied.</param>
    /// <param name="TotalMatches">The total number of query-layer matches before MCP limiting.</param>
    /// <param name="Usages">The bounded data-access usage records returned to the MCP client.</param>
    public sealed record ArchonMcpDataAccessUsageFacts(
        string? ProjectStableKey,
        string? DataContextStableKey,
        string? Entity,
        string? Table,
        string? StoredProcedure,
        string? Family,
        int TotalMatches,
        IReadOnlyList<ArchonMcpDataAccessUsageRecord> Usages);
}
