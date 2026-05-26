namespace ArchonMcp.McpDataAccess
{
    /// <summary>
    /// Represents one data-access usage fact returned by <c>archon.get_data_access_usage</c>.
    /// </summary>
    /// <param name="StableKey">The stable public identity of the data-access fact.</param>
    /// <param name="Family">The data-access family, such as EF Core, EF6, LINQ to SQL, ADO.NET, raw SQL, typed DataSet, or stored procedure.</param>
    /// <param name="Name">The safe developer-facing data-access fact name.</param>
    /// <param name="ProjectStableKey">The owning project stable key when known.</param>
    /// <param name="DataContextStableKey">The data-context stable key when the fact is associated with a context type.</param>
    /// <param name="EntityStableKey">The entity stable key when the fact is entity-backed.</param>
    /// <param name="TableStableKey">The table stable key when the fact is table-backed.</param>
    /// <param name="StoredProcedureStableKey">The stored procedure stable key when the fact is procedure-backed.</param>
    /// <param name="UsageSites">The stable usage-site identities or safe display names associated with the fact.</param>
    /// <param name="OperationKinds">The normalized operation kinds, such as read, write, execute, or unknown.</param>
    /// <param name="DynamicSqlIndicator">A value indicating whether persisted metadata marks the fact as dynamic SQL or unresolved SQL composition.</param>
    /// <param name="EvidenceStableKeys">The stable evidence keys that support the fact.</param>
    /// <param name="Confidence">The normalized confidence value assigned by persisted extraction data.</param>
    /// <param name="HasUnknownData">A value indicating whether this fact includes explicit unknown-state context.</param>
    /// <param name="UnknownReason">The safe reason explaining unknown data for this fact.</param>
    public sealed record ArchonMcpDataAccessUsageRecord(
        string StableKey,
        string Family,
        string Name,
        string? ProjectStableKey,
        string? DataContextStableKey,
        string? EntityStableKey,
        string? TableStableKey,
        string? StoredProcedureStableKey,
        IReadOnlyList<string> UsageSites,
        IReadOnlyList<string> OperationKinds,
        bool DynamicSqlIndicator,
        IReadOnlyList<string> EvidenceStableKeys,
        decimal Confidence,
        bool HasUnknownData,
        string? UnknownReason);
}
