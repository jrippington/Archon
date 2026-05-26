namespace ArchonMcp.McpSymbols
{
    /// <summary>
    /// Represents bounded source context for a symbol or usage while labeling snippet text as untrusted repository evidence.
    /// </summary>
    /// <param name="FilePath">The repository-relative file path associated with the source context.</param>
    /// <param name="StartLine">The optional starting line for the source context.</param>
    /// <param name="EndLine">The optional ending line for the source context.</param>
    /// <param name="SnippetPreview">The redacted bounded snippet preview that must be treated as untrusted data.</param>
    /// <param name="SnippetHash">The optional hash of the source snippet when supplied by the query layer.</param>
    /// <param name="TrustLabel">The label warning clients that snippet content came from analyzed repository evidence.</param>
    public sealed record ArchonMcpSymbolSourceFacts(
        string? FilePath,
        int? StartLine,
        int? EndLine,
        string? SnippetPreview,
        string? SnippetHash,
        string TrustLabel);
}
