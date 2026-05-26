namespace ArchonMcp.McpEnvelope
{
    /// <summary>
    /// Represents a safe evidence reference that supports one or more MCP facts, findings, or unknowns.
    /// </summary>
    public sealed record ArchonMcpEvidenceReference
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ArchonMcpEvidenceReference" /> record.
        /// </summary>
        /// <param name="stableKey">The stable evidence identity; it must not be a Neo4j internal node or relationship identifier.</param>
        /// <param name="kind">The evidence kind provided by the query layer, such as source code, configuration, markdown, or generated metadata.</param>
        /// <param name="sourcePath">The repository-relative source path when one is available and safe to expose.</param>
        /// <param name="startLine">The one-based starting line when line information is available.</param>
        /// <param name="endLine">The one-based ending line when line information is available.</param>
        /// <param name="symbolName">The related symbol name when the evidence is symbol-scoped.</param>
        /// <param name="containingSymbol">The containing symbol when available and safe.</param>
        /// <param name="snippetPreview">The redacted bounded snippet preview, or <see langword="null" /> when omitted for safety.</param>
        /// <param name="snippetHash">The deterministic snippet hash when a preview is omitted or shortened.</param>
        /// <param name="confidence">The confidence attached to the evidence reference.</param>
        /// <param name="snapshot">The snapshot context for this evidence reference.</param>
        public ArchonMcpEvidenceReference(
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
            ArchonMcpSnapshotIdentity? snapshot)
        {
            // Evidence is untrusted repository data; callers receive only stable identity, safe location metadata, and redacted preview text.
            StableKey = stableKey;
            Kind = kind;
            SourcePath = sourcePath;
            StartLine = startLine;
            EndLine = endLine;
            SymbolName = symbolName;
            ContainingSymbol = containingSymbol;
            SnippetPreview = snippetPreview;
            SnippetHash = snippetHash;
            Confidence = confidence ?? throw new ArgumentNullException(nameof(confidence));
            Snapshot = snapshot;
        }

        /// <summary>
        /// Gets the stable evidence identity.
        /// </summary>
        public string StableKey { get; init; }

        /// <summary>
        /// Gets the evidence kind provided by the query layer.
        /// </summary>
        public string Kind { get; init; }

        /// <summary>
        /// Gets the repository-relative source path when one is available and safe to expose.
        /// </summary>
        public string? SourcePath { get; init; }

        /// <summary>
        /// Gets the one-based starting line when line information is available.
        /// </summary>
        public int? StartLine { get; init; }

        /// <summary>
        /// Gets the one-based ending line when line information is available.
        /// </summary>
        public int? EndLine { get; init; }

        /// <summary>
        /// Gets the related symbol name when the evidence is symbol-scoped.
        /// </summary>
        public string? SymbolName { get; init; }

        /// <summary>
        /// Gets the containing symbol when available and safe.
        /// </summary>
        public string? ContainingSymbol { get; init; }

        /// <summary>
        /// Gets the redacted bounded snippet preview, or <see langword="null" /> when omitted for safety.
        /// </summary>
        public string? SnippetPreview { get; init; }

        /// <summary>
        /// Gets the deterministic snippet hash when a preview is omitted or shortened.
        /// </summary>
        public string? SnippetHash { get; init; }

        /// <summary>
        /// Gets the confidence attached to the evidence reference.
        /// </summary>
        public ArchonMcpConfidence Confidence { get; init; }

        /// <summary>
        /// Gets the snapshot context for this evidence reference.
        /// </summary>
        public ArchonMcpSnapshotIdentity? Snapshot { get; init; }
    }
}
