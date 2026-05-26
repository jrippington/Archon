namespace ArchonMcp.McpDependencies
{
    /// <summary>
    /// Represents the structured facts section returned by dependency and dependent traversal MCP tools.
    /// </summary>
    /// <param name="StartNodeStableKey">The stable key of the traversal start node.</param>
    /// <param name="Direction">The normalized traversal direction applied by the query layer.</param>
    /// <param name="Mode">The query-layer traversal mode used for diagnostics and response metadata.</param>
    /// <param name="DirectOnly">A value indicating whether the response contains direct relationships only.</param>
    /// <param name="MaximumDepth">The maximum traversal depth applied to the request.</param>
    /// <param name="EdgeKindFilters">The normalized edge-kind filters applied by the query layer.</param>
    /// <param name="DataAvailable">A value indicating whether dependency graph data was available for the selected scope.</param>
    /// <param name="Nodes">The stable graph nodes needed to explain returned relationships.</param>
    /// <param name="Relationships">The stable graph relationships returned by the traversal.</param>
    public sealed record ArchonMcpDependencyTraversalFacts(
        string StartNodeStableKey,
        string Direction,
        string Mode,
        bool DirectOnly,
        int MaximumDepth,
        IReadOnlyList<string> EdgeKindFilters,
        bool DataAvailable,
        IReadOnlyList<ArchonMcpTraversalNodeFacts> Nodes,
        IReadOnlyList<ArchonMcpTraversalRelationshipFacts> Relationships);
}
