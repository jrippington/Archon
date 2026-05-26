namespace ArchonMcp.McpEnvelope
{
    /// <summary>
    /// Carries the common MCP request fields that shared validators can inspect before query execution.
    /// </summary>
    /// <param name="StableKey">The optional stable key to validate, when a request targets an existing graph, evidence, rule, or finding record.</param>
    /// <param name="SnapshotSelector">The optional snapshot selector or snapshot stable key.</param>
    /// <param name="SearchText">The optional search text to validate for bounded query tools.</param>
    /// <param name="Filters">The optional filter values supplied by the request.</param>
    /// <param name="RequestedCount">The optional requested result count.</param>
    /// <param name="RequestedDepth">The optional requested traversal depth.</param>
    /// <param name="PageNumber">The optional one-based page number.</param>
    /// <param name="PageSize">The optional page size.</param>
    public sealed record ArchonMcpValidationRequest(
        string? StableKey,
        string? SnapshotSelector,
        string? SearchText,
        IReadOnlyList<string>? Filters,
        int? RequestedCount,
        int? RequestedDepth,
        int? PageNumber,
        int? PageSize);
}
