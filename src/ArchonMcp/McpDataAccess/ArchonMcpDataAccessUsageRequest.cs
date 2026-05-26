namespace ArchonMcp.McpDataAccess
{
    /// <summary>
    /// Represents caller-supplied filters for the <c>archon.get_data_access_usage</c> MCP tool.
    /// </summary>
    /// <param name="ProjectStableKey">The optional project stable key used to limit data-access facts to one project.</param>
    /// <param name="DataContextStableKey">The optional DbContext, ObjectContext, or LINQ to SQL data-context stable key filter.</param>
    /// <param name="Entity">The optional entity name or stable key filter.</param>
    /// <param name="Table">The optional database table name or stable key filter.</param>
    /// <param name="StoredProcedure">The optional stored procedure name or stable key filter.</param>
    /// <param name="Family">The optional controlled data-access family filter, such as EFCore, EF6, AdoNet, RawSql, or StoredProcedure.</param>
    /// <param name="Limit">The optional maximum number of usage facts to return before MCP truncation metadata is emitted.</param>
    /// <param name="SnapshotSelector">The optional snapshot selector, either <c>latest</c> or a stable snapshot key.</param>
    /// <param name="RepositoryStableKey">The repository stable key that bounds latest/current snapshot resolution.</param>
    /// <param name="SolutionStableKey">The optional solution stable key that narrows repository scope.</param>
    public sealed record ArchonMcpDataAccessUsageRequest(
        string? ProjectStableKey,
        string? DataContextStableKey,
        string? Entity,
        string? Table,
        string? StoredProcedure,
        string? Family,
        int? Limit,
        string? SnapshotSelector,
        string? RepositoryStableKey,
        string? SolutionStableKey);
}
