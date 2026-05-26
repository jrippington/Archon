namespace ArchonMcp.McpEnvelope
{
    /// <summary>
    /// Maps query-layer values into MCP envelope records while preserving stable keys and redacting untrusted evidence text.
    /// </summary>
    public sealed class ArchonMcpResponseMapper : IArchonMcpResponseMapper
    {
        /// <summary>
        /// Stores the redactor used to sanitize untrusted snippet previews and metadata text.
        /// </summary>
        private readonly IArchonMcpSensitiveTextRedactor _redactor;

        /// <summary>
        /// Creates a response mapper with a sensitive text redactor.
        /// </summary>
        /// <param name="redactor">The redactor used before evidence snippet previews enter MCP response contracts.</param>
        public ArchonMcpResponseMapper(IArchonMcpSensitiveTextRedactor redactor)
        {
            // Response mapping centralizes redaction so individual tool handlers do not accidentally return raw evidence snippets.
            _redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
        }

        /// <inheritdoc />
        public ArchonMcpEvidenceReference MapEvidence(
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
            // Evidence snippets are untrusted source/configuration content, so the mapper redacts them before envelope creation.
            string? redactedPreview = _redactor.Redact(snippetPreview);

            return new ArchonMcpEvidenceReference(
                stableKey,
                kind,
                sourcePath,
                startLine,
                endLine,
                symbolName,
                containingSymbol,
                redactedPreview,
                snippetHash,
                confidence,
                snapshot);
        }
    }
}
