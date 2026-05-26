namespace ArchonMcp.McpEnvelope
{
    /// <summary>
    /// Describes one request validation failure detected before an MCP operation reaches the application/query layer.
    /// </summary>
    public sealed record ArchonMcpValidationFailure
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ArchonMcpValidationFailure" /> record.
        /// </summary>
        /// <param name="field">The safe request field name associated with the failure.</param>
        /// <param name="message">The safe validation message that explains how the caller can correct the request.</param>
        public ArchonMcpValidationFailure(string field, string message)
        {
            // Validation failure messages must remain safe because they may be returned directly to MCP clients.
            Field = field;
            Message = message;
        }

        /// <summary>
        /// Gets the safe request field name associated with the failure.
        /// </summary>
        public string Field { get; init; }

        /// <summary>
        /// Gets the safe validation message that explains how the caller can correct the request.
        /// </summary>
        public string Message { get; init; }
    }
}
