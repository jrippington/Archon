namespace ArchonMcp.McpSymbols
{
    /// <summary>
    /// Represents one semantic relationship connected to a described symbol.
    /// </summary>
    /// <param name="StableKey">The stable relationship identity.</param>
    /// <param name="Kind">The controlled relationship kind.</param>
    /// <param name="SourceSymbolStableKey">The stable source symbol or node key.</param>
    /// <param name="TargetSymbolStableKey">The stable target symbol or node key.</param>
    /// <param name="EvidenceStableKeys">The evidence stable keys that support the relationship.</param>
    /// <param name="Confidence">The normalized confidence assigned to the relationship.</param>
    public sealed record ArchonMcpSymbolRelationshipFacts(
        string StableKey,
        string Kind,
        string SourceSymbolStableKey,
        string TargetSymbolStableKey,
        IReadOnlyList<string> EvidenceStableKeys,
        decimal Confidence);
}
