namespace ArchonMcp.McpEnvelope
{
    /// <summary>
    /// Represents the stable machine-readable detail for one structured MCP failure.
    /// </summary>
    public sealed record ArchonMcpErrorDetail
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ArchonMcpErrorDetail" /> record.
        /// </summary>
        /// <param name="category">The stable error category.</param>
        /// <param name="code">The stable machine-readable error code.</param>
        /// <param name="message">The safe developer-facing error message.</param>
        /// <param name="target">The safe field, operation, or stable key associated with the error when applicable.</param>
        public ArchonMcpErrorDetail(ArchonMcpErrorCategory category, string code, string message, string? target)
        {
            // Error detail intentionally excludes exception types, stack traces, raw snippets, connection details, and graph internals.
            Category = category;
            Code = code;
            Message = message;
            Target = target;
        }

        /// <summary>
        /// Gets the stable error category.
        /// </summary>
        public ArchonMcpErrorCategory Category { get; init; }

        /// <summary>
        /// Gets the stable machine-readable error code.
        /// </summary>
        public string Code { get; init; }

        /// <summary>
        /// Gets the safe developer-facing error message.
        /// </summary>
        public string Message { get; init; }

        /// <summary>
        /// Gets the safe field, operation, or stable key associated with the error when applicable.
        /// </summary>
        public string? Target { get; init; }
    }
}
