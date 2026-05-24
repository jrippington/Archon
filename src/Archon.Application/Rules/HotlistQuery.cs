namespace Archon.Application.Rules
{
    /// <summary>
    /// Describes controlled filters and paging for WP012 hotlist finding queries.
    /// </summary>
    public sealed class HotlistQuery
    {
        /// <summary>
        /// Defines the default number of finding records returned when callers omit a page size.
        /// </summary>
        public const int DefaultPageSize = 50;

        /// <summary>
        /// Defines the maximum number of finding records returned by one controlled hotlist query.
        /// </summary>
        public const int MaximumPageSize = 200;

        /// <summary>
        /// Initializes a new instance of the <see cref="HotlistQuery"/> class.
        /// </summary>
        /// <param name="snapshotStableKey">The optional snapshot stable key that scopes finding results.</param>
        /// <param name="category">The optional exact rule category filter.</param>
        /// <param name="severity">The optional exact finding severity filter.</param>
        /// <param name="status">The optional exact finding lifecycle status filter.</param>
        /// <param name="projectStableKey">The optional affected project stable-key filter.</param>
        /// <param name="affectedNodeStableKey">The optional affected node stable-key filter.</param>
        /// <param name="skip">The number of sorted records to skip before returning results.</param>
        /// <param name="take">The maximum number of sorted records to return.</param>
        public HotlistQuery(
            string? snapshotStableKey,
            string? category,
            string? severity,
            string? status,
            string? projectStableKey,
            string? affectedNodeStableKey,
            int? skip,
            int? take)
        {
            // The hotlist intentionally accepts only specific fields so callers cannot submit arbitrary graph predicates.
            SnapshotStableKey = NormalizeOptionalText(snapshotStableKey);
            Category = NormalizeOptionalText(category);
            Severity = NormalizeOptionalText(severity);
            Status = NormalizeOptionalText(status);
            ProjectStableKey = NormalizeOptionalText(projectStableKey);
            AffectedNodeStableKey = NormalizeOptionalText(affectedNodeStableKey);
            Skip = Math.Max(0, skip.GetValueOrDefault(0));
            Take = Math.Clamp(take.GetValueOrDefault(DefaultPageSize), 1, MaximumPageSize);
        }

        /// <summary>
        /// Gets the optional snapshot stable key that scopes finding results.
        /// </summary>
        public string? SnapshotStableKey { get; }

        /// <summary>
        /// Gets the optional exact rule category filter.
        /// </summary>
        public string? Category { get; }

        /// <summary>
        /// Gets the optional exact finding severity filter.
        /// </summary>
        public string? Severity { get; }

        /// <summary>
        /// Gets the optional exact finding lifecycle status filter.
        /// </summary>
        public string? Status { get; }

        /// <summary>
        /// Gets the optional affected project stable-key filter.
        /// </summary>
        public string? ProjectStableKey { get; }

        /// <summary>
        /// Gets the optional affected node stable-key filter.
        /// </summary>
        public string? AffectedNodeStableKey { get; }

        /// <summary>
        /// Gets the number of sorted records to skip.
        /// </summary>
        public int Skip { get; }

        /// <summary>
        /// Gets the maximum number of sorted records to return.
        /// </summary>
        public int Take { get; }

        /// <summary>
        /// Normalizes optional query text by trimming blanks to <see langword="null"/>.
        /// </summary>
        /// <param name="value">The optional query text supplied by a caller.</param>
        /// <returns>The trimmed value, or <see langword="null"/> when no meaningful text was supplied.</returns>
        private static string? NormalizeOptionalText(string? value)
        {
            // Whitespace-only filters are treated as absent rather than as literal filter values.
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
