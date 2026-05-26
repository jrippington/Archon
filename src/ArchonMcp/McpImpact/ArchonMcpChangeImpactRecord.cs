namespace ArchonMcp.McpImpact
{
    /// <summary>
    /// Represents one direct or transitive impact relationship returned by <c>archon.assess_change_impact</c>.
    /// </summary>
    /// <param name="RelationshipStableKey">The stable public relationship identity.</param>
    /// <param name="RelationshipKind">The controlled relationship kind that connects the impacted node to the target or intermediate node.</param>
    /// <param name="ImpactedStableKey">The stable key of the impacted project, symbol, endpoint, worker, data-access fact, integration, configuration key, rule, finding, or metric node.</param>
    /// <param name="ImpactedKind">The controlled graph kind of the impacted node.</param>
    /// <param name="ImpactedName">The safe developer-facing impacted node name.</param>
    /// <param name="ProjectStableKey">The owning project stable key when known.</param>
    /// <param name="Depth">The inferred impact depth, where one means direct impact and greater than one means transitive impact.</param>
    /// <param name="EvidenceStableKeys">The stable evidence keys associated with the impact relationship or impacted node.</param>
    /// <param name="Confidence">The normalized confidence value assigned by persisted graph data.</param>
    /// <param name="HasUnknownData">A value indicating whether explicit unknown-state context applies to this impact record.</param>
    /// <param name="UnknownReason">The safe reason explaining unknown impact data.</param>
    public sealed record ArchonMcpChangeImpactRecord(
        string RelationshipStableKey,
        string RelationshipKind,
        string ImpactedStableKey,
        string ImpactedKind,
        string ImpactedName,
        string? ProjectStableKey,
        int Depth,
        IReadOnlyList<string> EvidenceStableKeys,
        decimal Confidence,
        bool HasUnknownData,
        string? UnknownReason);
}
