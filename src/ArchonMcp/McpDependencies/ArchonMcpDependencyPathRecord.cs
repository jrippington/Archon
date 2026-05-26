namespace ArchonMcp.McpDependencies
{
    /// <summary>
    /// Represents one deterministic dependency path between the requested source and target nodes.
    /// </summary>
    /// <param name="StableKey">The stable synthetic path identity built from the path endpoints and ordered edges.</param>
    /// <param name="HopCount">The number of graph edges in the path.</param>
    /// <param name="Nodes">The stable graph nodes that participate in the path order.</param>
    /// <param name="Edges">The stable graph edges that connect the path nodes in order.</param>
    public sealed record ArchonMcpDependencyPathRecord(
        string StableKey,
        int HopCount,
        IReadOnlyList<ArchonMcpTraversalNodeFacts> Nodes,
        IReadOnlyList<ArchonMcpTraversalRelationshipFacts> Edges);
}
