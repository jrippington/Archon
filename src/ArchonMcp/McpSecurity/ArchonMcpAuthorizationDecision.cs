using ArchonMcp.McpEnvelope;

namespace ArchonMcp.McpSecurity
{
    /// <summary>
    /// Represents the result of an MCP authorization and allow-list check.
    /// </summary>
    public sealed record ArchonMcpAuthorizationDecision
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ArchonMcpAuthorizationDecision" /> record.
        /// </summary>
        /// <param name="allowed">Indicates whether the operation may execute.</param>
        /// <param name="errorCategory">The structured error category to return when the operation is denied.</param>
        /// <param name="safeReason">The safe denial reason that can be returned to callers and audit logs.</param>
        public ArchonMcpAuthorizationDecision(bool allowed, ArchonMcpErrorCategory? errorCategory, string? safeReason)
        {
            // Decisions carry only safe categories and reasons so authorization failures can be audited without leaking internals.
            Allowed = allowed;
            ErrorCategory = errorCategory;
            SafeReason = safeReason;
        }

        /// <summary>
        /// Gets a value indicating whether the operation may execute.
        /// </summary>
        public bool Allowed { get; init; }

        /// <summary>
        /// Gets the structured error category to return when the operation is denied.
        /// </summary>
        public ArchonMcpErrorCategory? ErrorCategory { get; init; }

        /// <summary>
        /// Gets the safe denial reason that can be returned to callers and audit logs.
        /// </summary>
        public string? SafeReason { get; init; }

        /// <summary>
        /// Creates an allowed authorization decision.
        /// </summary>
        /// <returns>An allowed authorization decision.</returns>
        public static ArchonMcpAuthorizationDecision Allow()
        {
            // A shared factory keeps allowed decisions free from accidental denial metadata.
            return new ArchonMcpAuthorizationDecision(true, null, null);
        }

        /// <summary>
        /// Creates a denied authorization decision with a safe structured error category.
        /// </summary>
        /// <param name="errorCategory">The structured category that explains why execution was denied.</param>
        /// <param name="safeReason">The safe denial reason for callers and audit logs.</param>
        /// <returns>A denied authorization decision.</returns>
        public static ArchonMcpAuthorizationDecision Deny(ArchonMcpErrorCategory errorCategory, string safeReason)
        {
            // Denied decisions preserve fail-closed behavior while avoiding provider-specific diagnostics.
            return new ArchonMcpAuthorizationDecision(false, errorCategory, safeReason);
        }
    }
}
