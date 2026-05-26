namespace ArchonMcp.McpSymbols
{
    /// <summary>
    /// Represents the structured facts returned by <c>archon.describe_symbol</c>.
    /// </summary>
    /// <param name="Identity">The symbol identity and containment facts.</param>
    /// <param name="ProjectStableKey">The owning project stable key when the symbol is project-owned.</param>
    /// <param name="Source">The bounded source context associated with the symbol.</param>
    /// <param name="Relationships">The deterministic semantic relationships connected to the symbol.</param>
    /// <param name="EvidenceStableKeys">The evidence stable keys directly associated with the symbol.</param>
    /// <param name="HasUnknownData">A value indicating whether the described symbol carries explicit unknown-state context.</param>
    /// <param name="UnknownReason">The optional safe reason explaining unknown symbol data.</param>
    public sealed record ArchonMcpSymbolFacts(
        ArchonMcpSymbolIdentityFacts Identity,
        string? ProjectStableKey,
        ArchonMcpSymbolSourceFacts Source,
        IReadOnlyList<ArchonMcpSymbolRelationshipFacts> Relationships,
        IReadOnlyList<string> EvidenceStableKeys,
        bool HasUnknownData,
        string? UnknownReason);
}
