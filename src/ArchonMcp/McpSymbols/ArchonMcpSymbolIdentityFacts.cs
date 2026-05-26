namespace ArchonMcp.McpSymbols
{
    /// <summary>
    /// Represents stable identity and containment facts for one described symbol.
    /// </summary>
    /// <param name="StableKey">The stable public symbol identity.</param>
    /// <param name="Name">The developer-facing symbol name.</param>
    /// <param name="FullyQualifiedName">The fully qualified symbol name when extraction supplied one.</param>
    /// <param name="Kind">The controlled symbol kind.</param>
    /// <param name="Namespace">The namespace containing the symbol when known.</param>
    /// <param name="ContainingType">The containing type when known.</param>
    /// <param name="Language">The programming language associated with the symbol.</param>
    public sealed record ArchonMcpSymbolIdentityFacts(
        string StableKey,
        string Name,
        string? FullyQualifiedName,
        string Kind,
        string? Namespace,
        string? ContainingType,
        string? Language);
}
