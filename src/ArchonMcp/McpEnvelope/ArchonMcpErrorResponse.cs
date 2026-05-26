namespace ArchonMcp.McpEnvelope
{
    /// <summary>
    /// Represents the common MCP error envelope returned for validation, authorization, lookup, dependency, and server failures.
    /// </summary>
    public sealed record ArchonMcpErrorResponse
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ArchonMcpErrorResponse" /> record.
        /// </summary>
        /// <param name="operation">The operation that failed.</param>
        /// <param name="error">The stable structured error detail.</param>
        /// <param name="warnings">The safe warnings that accompany the failure.</param>
        /// <param name="suggestedFollowUps">The safe follow-ups that may help the caller recover or narrow the request.</param>
        public ArchonMcpErrorResponse(
            string operation,
            ArchonMcpErrorDetail error,
            IEnumerable<ArchonMcpWarning>? warnings,
            IEnumerable<ArchonMcpSuggestedFollowUp>? suggestedFollowUps)
        {
            // The error envelope mirrors success-envelope safety by keeping follow-ups bounded and omitting sensitive internals.
            Operation = operation;
            Error = error ?? throw new ArgumentNullException(nameof(error));
            Warnings = warnings?.ToArray() ?? [];
            SuggestedFollowUps = suggestedFollowUps?.ToArray() ?? [];
        }

        /// <summary>
        /// Gets the operation that failed.
        /// </summary>
        public string Operation { get; init; }

        /// <summary>
        /// Gets the stable structured error detail.
        /// </summary>
        public ArchonMcpErrorDetail Error { get; init; }

        /// <summary>
        /// Gets the safe warnings that accompany the failure.
        /// </summary>
        public IReadOnlyList<ArchonMcpWarning> Warnings { get; init; }

        /// <summary>
        /// Gets the safe follow-ups that may help the caller recover or narrow the request.
        /// </summary>
        public IReadOnlyList<ArchonMcpSuggestedFollowUp> SuggestedFollowUps { get; init; }

        /// <summary>
        /// Creates a structured MCP error envelope with a standard code for the supplied category.
        /// </summary>
        /// <param name="operation">The operation that failed.</param>
        /// <param name="category">The stable error category.</param>
        /// <param name="message">The safe developer-facing error message.</param>
        /// <param name="suggestedFollowUps">The safe follow-ups that may help the caller recover or narrow the request.</param>
        /// <returns>A structured MCP error response.</returns>
        public static ArchonMcpErrorResponse Create(
            string operation,
            ArchonMcpErrorCategory category,
            string message,
            IEnumerable<ArchonMcpSuggestedFollowUp>? suggestedFollowUps)
        {
            // Category-to-code mapping is deterministic so all handlers use one stable failure vocabulary.
            string code = category switch
            {
                ArchonMcpErrorCategory.Validation => "mcp.validation_failed",
                ArchonMcpErrorCategory.UnsupportedOperation => "mcp.unsupported_operation",
                ArchonMcpErrorCategory.NotFound => "mcp.not_found",
                ArchonMcpErrorCategory.Ambiguous => "mcp.ambiguous_request",
                ArchonMcpErrorCategory.Unauthorized => "mcp.unauthorized",
                ArchonMcpErrorCategory.Forbidden => "mcp.forbidden",
                ArchonMcpErrorCategory.DependencyUnavailable => "mcp.dependency_unavailable",
                ArchonMcpErrorCategory.QueryLayerFailure => "mcp.query_layer_failure",
                _ => "mcp.server_error"
            };

            return new ArchonMcpErrorResponse(
                operation,
                new ArchonMcpErrorDetail(category, code, message, target: null),
                warnings: null,
                suggestedFollowUps);
        }
    }
}
