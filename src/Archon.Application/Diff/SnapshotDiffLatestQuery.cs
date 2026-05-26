using Archon.Application.Rules;

namespace Archon.Application.Diff
{
    /// <summary>
    /// Represents controlled scope, filters, and bounds for latest-to-previous snapshot diff requests.
    /// </summary>
    public sealed class SnapshotDiffLatestQuery
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SnapshotDiffLatestQuery"/> class.
        /// </summary>
        /// <param name="repositoryStableKey">The repository stable key that bounds latest and previous snapshot resolution.</param>
        /// <param name="solutionStableKey">The optional solution stable key that narrows repository scope.</param>
        /// <param name="domains">The optional controlled domain filters.</param>
        /// <param name="changeKinds">The optional controlled change-kind filters.</param>
        /// <param name="projectStableKey">The optional owning or related project stable-key filter.</param>
        /// <param name="targetStableKey">The optional target node, edge endpoint, finding target, or metric target stable-key filter.</param>
        /// <param name="recordKind">The optional domain-specific kind filter.</param>
        /// <param name="severity">The optional finding severity filter.</param>
        /// <param name="includeUnchangedDetails">Indicates whether unchanged detail rows should be returned.</param>
        /// <param name="skip">The optional number of matching detail rows to skip.</param>
        /// <param name="take">The optional maximum number of matching detail rows to return.</param>
        public SnapshotDiffLatestQuery(
            string? repositoryStableKey,
            string? solutionStableKey,
            IEnumerable<string>? domains,
            IEnumerable<string>? changeKinds,
            string? projectStableKey,
            string? targetStableKey,
            string? recordKind,
            string? severity,
            bool includeUnchangedDetails,
            int? skip = null,
            int? take = null)
        {
            // The latest query delays snapshot-key selection until the service resolves the two newest comparable snapshots deterministically.
            RepositoryStableKey = NormalizeOptional(repositoryStableKey);
            SolutionStableKey = NormalizeOptional(solutionStableKey);
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
        /// Gets the repository stable key that bounds latest and previous snapshot resolution.
        /// </summary>
        public string? RepositoryStableKey { get; }

        /// <summary>
        /// Gets the optional solution stable key that narrows repository scope.
        /// </summary>
        public string? SolutionStableKey { get; }

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
        /// Converts this resolved-scope request into an explicit snapshot diff request.
        /// </summary>
        /// <param name="currentSnapshotStableKey">The resolved current snapshot stable key.</param>
        /// <param name="previousSnapshotStableKey">The resolved previous snapshot stable key.</param>
        /// <returns>An explicit snapshot diff query carrying the same filters and bounds.</returns>
        public SnapshotDiffQuery ToExplicitQuery(string currentSnapshotStableKey, string previousSnapshotStableKey)
        {
            // The explicit query reuses the main comparison path so latest-to-previous behavior cannot drift from explicit diff behavior.
            return new SnapshotDiffQuery(
                currentSnapshotStableKey,
                previousSnapshotStableKey,
                Domains,
                ChangeKinds,
                IncludeUnchangedDetails,
                ProjectStableKey,
                TargetStableKey,
                RecordKind,
                Severity,
                Skip,
                Take);
        }

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
