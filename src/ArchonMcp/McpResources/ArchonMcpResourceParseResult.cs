using ArchonMcp.McpEnvelope;

namespace ArchonMcp.McpResources
{
    /// <summary>
    /// Represents the result of parsing and validating an Archon MCP resource URI.
    /// </summary>
    public sealed class ArchonMcpResourceParseResult
    {
        /// <summary>
        /// Initializes a new successful parse result.
        /// </summary>
        /// <param name="request">The validated resource request produced by parsing.</param>
        private ArchonMcpResourceParseResult(ArchonMcpResourceRequest request)
        {
            // Successful results carry only a request because no public error needs to be returned.
            Request = request ?? throw new ArgumentNullException(nameof(request));
            Error = null;
        }

        /// <summary>
        /// Initializes a new failed parse result.
        /// </summary>
        /// <param name="error">The safe MCP error that explains why parsing failed.</param>
        private ArchonMcpResourceParseResult(ArchonMcpErrorResponse error)
        {
            // Failed results carry a structured MCP error so callers can return it without invoking query dependencies.
            Request = null;
            Error = error ?? throw new ArgumentNullException(nameof(error));
        }

        /// <summary>
        /// Gets a value indicating whether parsing produced a valid resource request.
        /// </summary>
        public bool Succeeded => Request is not null;

        /// <summary>
        /// Gets the validated resource request when parsing succeeded.
        /// </summary>
        public ArchonMcpResourceRequest? Request { get; }

        /// <summary>
        /// Gets the structured MCP error when parsing failed.
        /// </summary>
        public ArchonMcpErrorResponse? Error { get; }

        /// <summary>
        /// Creates a successful parse result.
        /// </summary>
        /// <param name="request">The validated resource request.</param>
        /// <returns>A successful resource parse result.</returns>
        public static ArchonMcpResourceParseResult Success(ArchonMcpResourceRequest request)
        {
            // The factory keeps constructor intent explicit at call sites.
            return new ArchonMcpResourceParseResult(request);
        }

        /// <summary>
        /// Creates a failed parse result.
        /// </summary>
        /// <param name="error">The structured MCP error returned to the caller.</param>
        /// <returns>A failed resource parse result.</returns>
        public static ArchonMcpResourceParseResult Failed(ArchonMcpErrorResponse error)
        {
            // The factory keeps constructor intent explicit at call sites.
            return new ArchonMcpResourceParseResult(error);
        }
    }
}
