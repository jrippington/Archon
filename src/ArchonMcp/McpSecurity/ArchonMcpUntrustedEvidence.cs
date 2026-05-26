namespace ArchonMcp.McpSecurity
{
    /// <summary>
    /// Represents redacted evidence content explicitly labeled as untrusted repository data.
    /// </summary>
    public sealed record ArchonMcpUntrustedEvidence
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ArchonMcpUntrustedEvidence" /> record.
        /// </summary>
        /// <param name="stableKey">The stable evidence identity associated with the content.</param>
        /// <param name="kind">The evidence kind supplied by the query or mapping layer.</param>
        /// <param name="redactedContent">The redacted content that must be treated as untrusted data by AI clients.</param>
        /// <param name="trustLabel">The label that warns clients the content came from analyzed repository evidence.</param>
        /// <param name="privilegedInstructionText">Privileged instruction text intentionally kept separate from untrusted evidence content.</param>
        public ArchonMcpUntrustedEvidence(
            string stableKey,
            string kind,
            string redactedContent,
            string trustLabel,
            string privilegedInstructionText)
        {
            // The record separates evidence text from privileged instructions to reduce prompt-injection confusion downstream.
            StableKey = stableKey;
            Kind = kind;
            RedactedContent = redactedContent;
            TrustLabel = trustLabel;
            PrivilegedInstructionText = privilegedInstructionText;
        }

        /// <summary>
        /// Gets the stable evidence identity associated with the content.
        /// </summary>
        public string StableKey { get; init; }

        /// <summary>
        /// Gets the evidence kind supplied by the query or mapping layer.
        /// </summary>
        public string Kind { get; init; }

        /// <summary>
        /// Gets the redacted content that must be treated as untrusted data by AI clients.
        /// </summary>
        public string RedactedContent { get; init; }

        /// <summary>
        /// Gets the label that warns clients the content came from analyzed repository evidence.
        /// </summary>
        public string TrustLabel { get; init; }

        /// <summary>
        /// Gets privileged instruction text intentionally kept separate from untrusted evidence content.
        /// </summary>
        public string PrivilegedInstructionText { get; init; }
    }
}
