namespace ArchonMcp.McpDependencies
{
    /// <summary>
    /// Represents one stable graph relationship returned by a dependency traversal MCP response.
    /// </summary>
    /// <param name="StableKey">The durable public edge stable key.</param>
    /// <param name="Kind">The controlled graph edge kind.</param>
    /// <param name="SourceNodeStableKey">The stable key of the source node.</param>
    /// <param name="TargetNodeStableKey">The stable key of the target node.</param>
    /// <param name="IsDirect">A value indicating whether the relationship was directly observed.</param>
    /// <param name="EvidenceStableKeys">The evidence stable keys associated with the relationship.</param>
    /// <param name="Confidence">The normalized relationship confidence returned by the query layer.</param>
    /// <param name="HasUnknownData">A value indicating whether the relationship carries unknown-state metadata.</param>
    /// <param name="UnknownReason">The safe reason explaining unknown relationship data when available.</param>
    public sealed record ArchonMcpTraversalRelationshipFacts(
        string StableKey,
        string Kind,
        string SourceNodeStableKey,
        string TargetNodeStableKey,
        bool IsDirect,
        IReadOnlyList<string> EvidenceStableKeys,
        decimal Confidence,
        bool HasUnknownData,
        string? UnknownReason);
}
