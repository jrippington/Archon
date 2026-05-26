namespace Archon.Api.Query.Contracts
{
    /// <summary>
    /// Describes response-size limiting and truncation state for bounded query responses.
    /// </summary>
    public sealed record QueryTruncationMetadataResponse
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="QueryTruncationMetadataResponse"/> record.
        /// </summary>
        /// <param name="truncated">Indicates whether response data was truncated by a configured limit.</param>
        /// <param name="limit">The configured item limit for bounded nested sections when applicable.</param>
        /// <param name="returnedCount">The number of bounded nested items returned when applicable.</param>
        /// <param name="reason">The safe explanation for truncation when truncation occurred.</param>
        public QueryTruncationMetadataResponse(bool truncated, int? limit, int? returnedCount, string? reason)
        {
            // Truncation metadata is present even when no truncation occurred so consumers can rely on one envelope shape.
            Truncated = truncated;
            Limit = limit;
            ReturnedCount = returnedCount;
            Reason = reason;
        }

        /// <summary>
        /// Gets a value indicating whether response data was truncated by a configured limit.
        /// </summary>
        public bool Truncated { get; init; }

        /// <summary>
        /// Gets the configured item limit for bounded nested sections when applicable.
        /// </summary>
        public int? Limit { get; init; }

        /// <summary>
        /// Gets the number of bounded nested items returned when applicable.
        /// </summary>
        public int? ReturnedCount { get; init; }

        /// <summary>
        /// Gets the safe explanation for truncation when truncation occurred.
        /// </summary>
        public string? Reason { get; init; }

        /// <summary>
        /// Creates a metadata value that states no truncation occurred.
        /// </summary>
        /// <param name="returnedCount">The number of bounded nested items returned.</param>
        /// <returns>A non-truncated metadata response.</returns>
        public static QueryTruncationMetadataResponse None(int returnedCount)
        {
            // A zero or positive returned count helps dashboards inspect bounded sections without separate array traversal.
            return new QueryTruncationMetadataResponse(false, null, Math.Max(0, returnedCount), null);
        }
    }
}