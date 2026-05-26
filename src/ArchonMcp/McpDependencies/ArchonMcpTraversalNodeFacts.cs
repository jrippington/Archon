namespace ArchonMcp.McpDependencies
{
    /// <summary>
    /// Represents one stable graph node returned by a dependency traversal MCP response.
    /// </summary>
    /// <param name="StableKey">The durable public node stable key.</param>
    /// <param name="Kind">The controlled graph node kind.</param>
    /// <param name="DisplayName">The developer-facing display name for the node.</param>
    /// <param name="ProjectStableKey">The owning project stable key when the node is project-owned.</param>
    /// <param name="EvidenceStableKeys">The evidence stable keys associated with the node.</param>
    /// <param name="Confidence">The normalized node confidence returned by the query layer.</param>
    /// <param name="HasUnknownData">A value indicating whether the node carries unknown-state metadata.</param>
    /// <param name="UnknownReason">The safe reason explaining unknown node data when available.</param>
    public sealed record ArchonMcpTraversalNodeFacts(
        string StableKey,
        string Kind,
        string DisplayName,
        string? ProjectStableKey,
        IReadOnlyList<string> EvidenceStableKeys,
        decimal Confidence,
        bool HasUnknownData,
        string? UnknownReason);
}
