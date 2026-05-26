namespace ArchonMcp.McpEnvelope
{
    /// <summary>
    /// Represents the result of validating a common MCP request before invoking application/query dependencies.
    /// </summary>
    public sealed record ArchonMcpValidationResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ArchonMcpValidationResult" /> record.
        /// </summary>
        /// <param name="failures">The validation failures detected for the request.</param>
        public ArchonMcpValidationResult(IEnumerable<ArchonMcpValidationFailure>? failures)
        {
            // The result snapshots failures so callers can enumerate them more than once while mapping structured errors.
            Failures = failures?.ToArray() ?? [];
        }

        /// <summary>
        /// Gets a value indicating whether validation succeeded.
        /// </summary>
        public bool IsValid => Failures.Count == 0;

        /// <summary>
        /// Gets the validation failures detected for the request.
        /// </summary>
        public IReadOnlyList<ArchonMcpValidationFailure> Failures { get; init; }

        /// <summary>
        /// Creates a successful validation result.
        /// </summary>
        /// <returns>A validation result with no failures.</returns>
        public static ArchonMcpValidationResult Success()
        {
            // Returning a shared shape keeps validators simple for later tool-specific composition.
            return new ArchonMcpValidationResult((IEnumerable<ArchonMcpValidationFailure>?)null);
        }
    }
}
