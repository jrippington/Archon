namespace ArchonMcp.McpEnvelope
{
    /// <summary>
    /// Maps query-layer data into safe MCP response contracts without exposing secrets or unsupported claims.
    /// </summary>
    public interface IArchonMcpResponseMapper
    {
        /// <summary>
        /// Maps one evidence reference while redacting untrusted snippet text.
        /// </summary>
        /// <param name="stableKey">The stable evidence identity.</param>
        /// <param name="kind">The evidence kind.</param>
        /// <param name="sourcePath">The repository-relative source path when available.</param>
        /// <param name="startLine">The one-based starting line when available.</param>
        /// <param name="endLine">The one-based ending line when available.</param>
        /// <param name="symbolName">The related symbol name when available.</param>
        /// <param name="containingSymbol">The containing symbol when available.</param>
        /// <param name="snippetPreview">The untrusted snippet preview to redact.</param>
        /// <param name="snippetHash">The deterministic snippet hash when available.</param>
        /// <param name="confidence">The confidence attached to the evidence reference.</param>
        /// <param name="snapshot">The snapshot context for the evidence reference.</param>
        /// <returns>A safe MCP evidence reference.</returns>
        ArchonMcpEvidenceReference MapEvidence(
            string stableKey,
            string kind,
            string? sourcePath,
            int? startLine,
            int? endLine,
            string? symbolName,
            string? containingSymbol,
            string? snippetPreview,
            string? snippetHash,
            ArchonMcpConfidence confidence,
            ArchonMcpSnapshotIdentity? snapshot);
    }
}
