namespace ArchonMcp.McpImpact
{
    /// <summary>
    /// Represents the structured facts section for <c>archon.assess_change_impact</c> responses.
    /// </summary>
    /// <param name="TargetStableKey">The supported stable target key being assessed.</param>
    /// <param name="MaximumDepth">The applied maximum impact traversal depth.</param>
    /// <param name="EdgeKindFilters">The controlled edge-kind filters applied to impact traversal.</param>
    /// <param name="IncludeTransitive">A value indicating whether transitive impacts were requested.</param>
    /// <param name="TotalRelationships">The total number of returned query-layer relationships before MCP limiting.</param>
    /// <param name="DirectImpacts">The bounded direct impact records.</param>
    /// <param name="TransitiveImpacts">The bounded transitive impact records.</param>
    /// <param name="RecommendationFraming">The safe framing statement that keeps output as investigation guidance rather than remediation instructions.</param>
    public sealed record ArchonMcpChangeImpactFacts(
        string TargetStableKey,
        int MaximumDepth,
        IReadOnlyList<string> EdgeKindFilters,
        bool IncludeTransitive,
        int TotalRelationships,
        IReadOnlyList<ArchonMcpChangeImpactRecord> DirectImpacts,
        IReadOnlyList<ArchonMcpChangeImpactRecord> TransitiveImpacts,
        string RecommendationFraming);
}
