using Archon.Application.Rules;

namespace Archon.Application.Diff
{
    /// <summary>
    /// Represents controlled filters and bounds for a snapshot diff request.
    /// </summary>
    public sealed class SnapshotDiffQuery
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SnapshotDiffQuery"/> class.
        /// </summary>
        /// <param name="currentSnapshotStableKey">The required current snapshot stable key.</param>
        /// <param name="previousSnapshotStableKey">The required previous snapshot stable key.</param>
        /// <param name="domains">The optional controlled domain filters.</param>
        /// <param name="changeKinds">The optional controlled change-kind filters.</param>
        /// <param name="projectStableKey">The optional owning or related project stable-key filter.</param>
        /// <param name="targetStableKey">The optional target node, edge endpoint, finding target, or metric target stable-key filter.</param>
        /// <param name="recordKind">The optional domain-specific kind filter.</param>
        /// <param name="severity">The optional finding severity filter.</param>
        /// <param name="includeUnchangedDetails">Indicates whether unchanged detail rows should be returned.</param>
        /// <param name="skip">The optional number of matching detail rows to skip.</param>
        /// <param name="take">The optional maximum number of matching detail rows to return.</param>
        public SnapshotDiffQuery(
            string? currentSnapshotStableKey,
            string? previousSnapshotStableKey,
            IEnumerable<string>? domains,
            IEnumerable<string>? changeKinds,
            bool includeUnchangedDetails,
            string? projectStableKey = null,
            string? targetStableKey = null,
            string? recordKind = null,
            string? severity = null,
            int? skip = null,
            int? take = null)
        {
            // The query records validation errors instead of throwing so endpoint handlers can return all deterministic request problems together.
            CurrentSnapshotStableKey = string.IsNullOrWhiteSpace(currentSnapshotStableKey) ? string.Empty : currentSnapshotStableKey.Trim();
            PreviousSnapshotStableKey = string.IsNullOrWhiteSpace(previousSnapshotStableKey) ? string.Empty : previousSnapshotStableKey.Trim();
            Domains = NormalizeFilters(domains);
            ChangeKinds = NormalizeFilters(changeKinds);
            ProjectStableKey = NormalizeOptional(projectStableKey);
            TargetStableKey = NormalizeOptional(targetStableKey);
            RecordKind = NormalizeOptional(recordKind);
            Severity = NormalizeOptional(severity);
            IncludeUnchangedDetails = includeUnchangedDetails;
            Skip = skip.GetValueOrDefault(0);
            Take = take.GetValueOrDefault(QueryPagingOptions.DefaultPageSize);
        }

        /// <summary>
        /// Gets the required current snapshot stable key.
        /// </summary>
        public string CurrentSnapshotStableKey { get; }

        /// <summary>
        /// Gets the required previous snapshot stable key.
        /// </summary>
        public string PreviousSnapshotStableKey { get; }

        /// <summary>
        /// Gets optional controlled domain filters.
        /// </summary>
        public IReadOnlyList<string> Domains { get; }

        /// <summary>
        /// Gets optional controlled change-kind filters.
        /// </summary>
        public IReadOnlyList<string> ChangeKinds { get; }

        /// <summary>
        /// Gets the optional owning or related project stable-key filter.
        /// </summary>
        public string? ProjectStableKey { get; }

        /// <summary>
        /// Gets the optional target node, edge endpoint, finding target, or metric target stable-key filter.
        /// </summary>
        public string? TargetStableKey { get; }

        /// <summary>
        /// Gets the optional domain-specific kind filter.
        /// </summary>
        public string? RecordKind { get; }

        /// <summary>
        /// Gets the optional finding severity filter.
        /// </summary>
        public string? Severity { get; }

        /// <summary>
        /// Gets a value indicating whether unchanged detail rows should be returned.
        /// </summary>
        public bool IncludeUnchangedDetails { get; }

        /// <summary>
        /// Gets the number of matching detail rows to skip.
        /// </summary>
        public int Skip { get; }

        /// <summary>
        /// Gets the maximum number of matching detail rows to return.
        /// </summary>
        public int Take { get; }

        /// <summary>
        /// Splits and normalizes filter values from repeated values or comma-separated API query strings.
        /// </summary>
        /// <param name="filters">The raw filter values.</param>
        /// <returns>A deterministic read-only list of trimmed unique filters.</returns>
        private static IReadOnlyList<string> NormalizeFilters(IEnumerable<string>? filters)
        {
            // Minimal APIs bind comma-containing query strings as a single value, so this helper accepts both repeated and comma-separated forms.
            return filters is null
                ? []
                : filters
                    .SelectMany(static filter => (filter ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    .Where(static filter => !string.IsNullOrWhiteSpace(filter))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static filter => filter, StringComparer.Ordinal)
                    .ToArray();
        }

        /// <summary>
        /// Normalizes optional scalar filters into trimmed values or null.
        /// </summary>
        /// <param name="value">The optional filter value supplied by the caller.</param>
        /// <returns>The trimmed filter value, or <see langword="null"/> when no meaningful value was supplied.</returns>
        private static string? NormalizeOptional(string? value)
        {
            // Blank filters should behave like omitted filters rather than invisible whitespace predicates.
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
