namespace Archon.Application.Projects
{
    /// <summary>
    /// Represents a safe reference to persisted evidence without expanding source content.
    /// </summary>
    /// <param name="StableKey">The stable key of the evidence record.</param>
    /// <param name="EvidenceKind">The controlled evidence kind when available.</param>
    /// <param name="FilePath">The repository-relative evidence path when available.</param>
    /// <param name="StartLine">The optional starting source line for source-backed evidence.</param>
    /// <param name="EndLine">The optional ending source line for source-backed evidence.</param>
    /// <param name="SymbolName">The optional symbol name associated with the evidence.</param>
    /// <param name="SnippetHash">The optional hash of the evidence snippet.</param>
    public sealed record EvidenceReferenceDto(string StableKey, string? EvidenceKind, string? FilePath, int? StartLine, int? EndLine, string? SymbolName, string? SnippetHash)
    {
        // The reference intentionally excludes snippet preview text so public query responses cannot expose source or secret material accidentally.
    }
}
