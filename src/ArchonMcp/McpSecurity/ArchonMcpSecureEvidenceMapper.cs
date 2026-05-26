using ArchonMcp.McpEnvelope;

namespace ArchonMcp.McpSecurity
{
    /// <summary>
    /// Builds redacted evidence values that are explicitly labeled as untrusted repository data.
    /// </summary>
    public sealed class ArchonMcpSecureEvidenceMapper : IArchonMcpSecureEvidenceMapper
    {
        /// <summary>
        /// Identifies evidence content that came from analyzed repositories and must not be interpreted as instructions.
        /// </summary>
        public const string UntrustedRepositoryEvidenceLabel = "untrusted-repository-evidence";

        /// <summary>
        /// Stores the redactor used before evidence text is returned to clients.
        /// </summary>
        private readonly IArchonMcpSensitiveTextRedactor _redactor;

        /// <summary>
        /// Creates a secure evidence mapper.
        /// </summary>
        /// <param name="redactor">The redactor used to remove secret-like values from evidence content.</param>
        public ArchonMcpSecureEvidenceMapper(IArchonMcpSensitiveTextRedactor redactor)
        {
            // The mapper composes with the shared redactor so evidence references and prompt-aware evidence labels stay consistent.
            _redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
        }

        /// <inheritdoc />
        public ArchonMcpUntrustedEvidence CreateUntrustedEvidence(string stableKey, string kind, string? content)
        {
            // The raw content remains data only; privileged instruction text is intentionally empty and separate from evidence.
            string redacted = _redactor.Redact(content) ?? string.Empty;
            return new ArchonMcpUntrustedEvidence(
                stableKey,
                kind,
                redacted,
                UntrustedRepositoryEvidenceLabel,
                privilegedInstructionText: string.Empty);
        }
    }
}
