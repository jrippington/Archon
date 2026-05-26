namespace ArchonMcp.McpEnvelope
{
    /// <summary>
    /// Describes the response-size, traversal, evidence, path, or serialized-context limit applied to an MCP response.
    /// </summary>
    public sealed record ArchonMcpLimitMetadata
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ArchonMcpLimitMetadata" /> record.
        /// </summary>
        /// <param name="truncated">Indicates whether the response was truncated by the applied limit.</param>
        /// <param name="limitKind">The kind of limit applied, such as result count, traversal depth, evidence count, path count, or context budget.</param>
        /// <param name="appliedLimit">The effective limit after configuration and request values were considered.</param>
        /// <param name="requestedLimit">The caller-requested limit when one was supplied.</param>
        /// <param name="originalCount">The original count before truncation when known.</param>
        /// <param name="returnedCount">The count returned after limit enforcement when known.</param>
        /// <param name="reason">The safe explanation for truncation or limit selection.</param>
        public ArchonMcpLimitMetadata(
            bool truncated,
            string limitKind,
            int? appliedLimit,
            int? requestedLimit,
            int? originalCount,
            int? returnedCount,
            string? reason)
        {
            // Limit metadata is always present so later tools expose a predictable envelope even when no truncation occurs.
            Truncated = truncated;
            LimitKind = limitKind;
            AppliedLimit = appliedLimit;
            RequestedLimit = requestedLimit;
            OriginalCount = originalCount;
            ReturnedCount = returnedCount;
            Reason = reason;
        }

        /// <summary>
        /// Gets a value indicating whether the response was truncated by the applied limit.
        /// </summary>
        public bool Truncated { get; init; }

        /// <summary>
        /// Gets the kind of limit applied.
        /// </summary>
        public string LimitKind { get; init; }

        /// <summary>
        /// Gets the effective limit after configuration and request values were considered.
        /// </summary>
        public int? AppliedLimit { get; init; }

        /// <summary>
        /// Gets the caller-requested limit when one was supplied.
        /// </summary>
        public int? RequestedLimit { get; init; }

        /// <summary>
        /// Gets the original count before truncation when known.
        /// </summary>
        public int? OriginalCount { get; init; }

        /// <summary>
        /// Gets the count returned after limit enforcement when known.
        /// </summary>
        public int? ReturnedCount { get; init; }

        /// <summary>
        /// Gets the safe explanation for truncation or limit selection.
        /// </summary>
        public string? Reason { get; init; }

        /// <summary>
        /// Creates metadata that explicitly states no response truncation occurred.
        /// </summary>
        /// <param name="limitKind">The kind of limit that was considered.</param>
        /// <param name="returnedCount">The count returned by the response section.</param>
        /// <returns>A non-truncated limit metadata value.</returns>
        public static ArchonMcpLimitMetadata None(string limitKind, int returnedCount)
        {
            // A non-truncated metadata object avoids nullable envelope sections and keeps serialization predictable.
            return new ArchonMcpLimitMetadata(false, limitKind, null, null, Math.Max(0, returnedCount), Math.Max(0, returnedCount), null);
        }
    }
}
