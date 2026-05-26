namespace ArchonMcp.McpSymbols
{
    /// <summary>
    /// Represents one bounded symbol usage, reference, call, injection, endpoint use, configuration use, or data-access use.
    /// </summary>
    /// <param name="UsageStableKey">The stable relationship identity for the usage.</param>
    /// <param name="UsageKind">The controlled usage relationship kind.</param>
    /// <param name="SourceSymbolStableKey">The stable source symbol or node key.</param>
    /// <param name="TargetSymbolStableKey">The stable target symbol or node key.</param>
    /// <param name="SourceName">The developer-facing source symbol name when known.</param>
    /// <param name="TargetName">The developer-facing target symbol name when known.</param>
    /// <param name="FilePath">The repository-relative file path associated with usage evidence.</param>
    /// <param name="StartLine">The optional starting line for usage evidence.</param>
    /// <param name="EndLine">The optional ending line for usage evidence.</param>
    /// <param name="SnippetPreview">The redacted bounded snippet preview that must be treated as untrusted data.</param>
    /// <param name="TrustLabel">The label warning clients that snippet content came from analyzed repository evidence.</param>
    /// <param name="EvidenceStableKeys">The evidence stable keys associated with the usage relationship.</param>
    /// <param name="Confidence">The normalized confidence assigned to the usage relationship.</param>
    /// <param name="HasUnknownData">A value indicating whether the usage carries explicit unknown semantic data.</param>
    /// <param name="UnknownReason">The optional safe reason explaining unknown usage data.</param>
    public sealed record ArchonMcpSymbolUsageRecord(
        string UsageStableKey,
        string UsageKind,
        string SourceSymbolStableKey,
        string TargetSymbolStableKey,
        string? SourceName,
        string? TargetName,
        string? FilePath,
        int? StartLine,
        int? EndLine,
        string? SnippetPreview,
        string TrustLabel,
        IReadOnlyList<string> EvidenceStableKeys,
        decimal Confidence,
        bool HasUnknownData,
        string? UnknownReason);
}
