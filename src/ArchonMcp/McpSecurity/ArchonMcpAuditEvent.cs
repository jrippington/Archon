using ArchonMcp.McpEnvelope;

namespace ArchonMcp.McpSecurity
{
    /// <summary>
    /// Represents sanitized audit metadata for one MCP operation attempt.
    /// </summary>
    public sealed record ArchonMcpAuditEvent
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ArchonMcpAuditEvent" /> record.
        /// </summary>
        /// <param name="operationName">The stable MCP operation name that was requested.</param>
        /// <param name="callerId">The safe caller identity when one is available.</param>
        /// <param name="safeParameters">The normalized request parameters with sensitive values removed.</param>
        /// <param name="status">The final status of the operation attempt.</param>
        /// <param name="truncated">Indicates whether the response payload reported truncation.</param>
        /// <param name="duration">The measured operation duration.</param>
        /// <param name="errorCategory">The structured error category when the operation failed or was denied.</param>
        public ArchonMcpAuditEvent(
            string operationName,
            string? callerId,
            IReadOnlyDictionary<string, string> safeParameters,
            ArchonMcpAuditResultStatus status,
            bool truncated,
            TimeSpan duration,
            ArchonMcpErrorCategory? errorCategory)
        {
            // Audit metadata is normalized at construction so every sink receives the same safe event shape.
            OperationName = operationName;
            CallerId = callerId;
            SafeParameters = safeParameters ?? throw new ArgumentNullException(nameof(safeParameters));
            Status = status;
            Truncated = truncated;
            Duration = duration;
            ErrorCategory = errorCategory;
        }

        /// <summary>
        /// Gets the stable MCP operation name that was requested.
        /// </summary>
        public string OperationName { get; init; }

        /// <summary>
        /// Gets the safe caller identity when one is available.
        /// </summary>
        public string? CallerId { get; init; }

        /// <summary>
        /// Gets the normalized request parameters with sensitive values removed.
        /// </summary>
        public IReadOnlyDictionary<string, string> SafeParameters { get; init; }

        /// <summary>
        /// Gets the final status of the operation attempt.
        /// </summary>
        public ArchonMcpAuditResultStatus Status { get; init; }

        /// <summary>
        /// Gets a value indicating whether the response payload reported truncation.
        /// </summary>
        public bool Truncated { get; init; }

        /// <summary>
        /// Gets the measured operation duration.
        /// </summary>
        public TimeSpan Duration { get; init; }

        /// <summary>
        /// Gets the structured error category when the operation failed or was denied.
        /// </summary>
        public ArchonMcpErrorCategory? ErrorCategory { get; init; }
    }
}
