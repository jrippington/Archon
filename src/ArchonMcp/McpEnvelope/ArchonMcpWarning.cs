namespace ArchonMcp.McpEnvelope
{
    /// <summary>
    /// Represents a safe warning about partial, truncated, unavailable, or data-quality-limited MCP response content.
    /// </summary>
    public sealed record ArchonMcpWarning
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ArchonMcpWarning" /> record.
        /// </summary>
        /// <param name="code">The stable machine-readable warning code.</param>
        /// <param name="message">The safe warning message suitable for AI context.</param>
        /// <param name="affectedStableKey">The affected stable key when the warning applies to a specific record.</param>
        public ArchonMcpWarning(string code, string message, string? affectedStableKey)
        {
            // Warnings avoid raw exception details and keep warning text safe for direct MCP client display.
            Code = code;
            Message = message;
            AffectedStableKey = affectedStableKey;
        }

        /// <summary>
        /// Gets the stable machine-readable warning code.
        /// </summary>
        public string Code { get; init; }

        /// <summary>
        /// Gets the safe warning message suitable for AI context.
        /// </summary>
        public string Message { get; init; }

        /// <summary>
        /// Gets the affected stable key when the warning applies to a specific record.
        /// </summary>
        public string? AffectedStableKey { get; init; }
    }
}
