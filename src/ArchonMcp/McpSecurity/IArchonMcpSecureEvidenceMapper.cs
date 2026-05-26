namespace ArchonMcp.McpSecurity
{
    /// <summary>
    /// Creates prompt-injection-aware representations of untrusted MCP evidence content.
    /// </summary>
    public interface IArchonMcpSecureEvidenceMapper
    {
        /// <summary>
        /// Creates a redacted evidence value labeled as untrusted repository content.
        /// </summary>
        /// <param name="stableKey">The stable evidence identity associated with the content.</param>
        /// <param name="kind">The evidence kind supplied by the query or mapping layer.</param>
        /// <param name="content">The raw evidence content that must be redacted before return.</param>
        /// <returns>A redacted and untrusted evidence representation.</returns>
        ArchonMcpUntrustedEvidence CreateUntrustedEvidence(string stableKey, string kind, string? content);
    }
}
