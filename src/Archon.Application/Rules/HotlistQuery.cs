namespace Archon.Application.Rules
{
    /// <summary>
    /// Describes controlled filters and paging for WP012 and WP014 hotlist finding queries.
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
            : this(snapshotStableKey, category, severity, status, projectStableKey, affectedNodeStableKey, criticalOnly: null, legacyDataAccess: null, outOfSupport: null, securitySensitive: null, frameworkOnly: null, technology: null, ruleCode: null, skip, take)
        {
            // This overload preserves existing callers while routing all normalization through the expanded WP014 query contract.
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="HotlistQuery"/> class.
        /// </summary>
        /// <param name="snapshotStableKey">The optional snapshot stable key that scopes finding results.</param>
        /// <param name="category">The optional exact rule category filter.</param>
        /// <param name="severity">The optional exact finding severity filter.</param>
        /// <param name="status">The optional exact finding lifecycle status filter.</param>
        /// <param name="projectStableKey">The optional affected project stable-key filter.</param>
        /// <param name="affectedNodeStableKey">The optional affected node stable-key filter.</param>
        /// <param name="criticalOnly">Indicates whether the query should return only critical-severity findings.</param>
        /// <param name="legacyDataAccess">Indicates whether the query should return data-access modernization findings.</param>
        /// <param name="outOfSupport">Indicates whether the query should return out-of-support lifecycle findings.</param>
        /// <param name="securitySensitive">Indicates whether the query should return security-sensitive findings.</param>
        /// <param name="frameworkOnly">Indicates whether the query should return framework-only modernization findings.</param>
        /// <param name="technology">The optional exact technology or technology-family filter from finding metadata.</param>
        /// <param name="ruleCode">The optional exact rule-code filter.</param>
        /// <param name="skip">The number of sorted records to skip before returning results.</param>
        /// <param name="take">The maximum number of sorted records to return.</param>
        public HotlistQuery(
            string? snapshotStableKey,
            string? category,
            string? severity,
            string? status,
            string? projectStableKey,
            string? affectedNodeStableKey,
            bool? criticalOnly,
            bool? legacyDataAccess,
            bool? outOfSupport,
            bool? securitySensitive,
            bool? frameworkOnly,
            string? technology,
            string? ruleCode,
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
            CriticalOnly = criticalOnly;
            LegacyDataAccess = legacyDataAccess;
            OutOfSupport = outOfSupport;
            SecuritySensitive = securitySensitive;
            FrameworkOnly = frameworkOnly;
            Technology = NormalizeOptionalText(technology);
            RuleCode = NormalizeOptionalText(ruleCode);
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
        /// Gets a value indicating whether only critical-severity findings should be returned.
        /// </summary>
        public bool? CriticalOnly { get; }

        /// <summary>
        /// Gets a value indicating whether only legacy data-access findings should be returned.
        /// </summary>
        public bool? LegacyDataAccess { get; }

        /// <summary>
        /// Gets a value indicating whether only out-of-support lifecycle findings should be returned.
        /// </summary>
        public bool? OutOfSupport { get; }

        /// <summary>
        /// Gets a value indicating whether only security-sensitive findings should be returned.
        /// </summary>
        public bool? SecuritySensitive { get; }

        /// <summary>
        /// Gets a value indicating whether only framework-only modernization findings should be returned.
        /// </summary>
        public bool? FrameworkOnly { get; }

        /// <summary>
        /// Gets the optional exact technology or technology-family filter from finding metadata.
        /// </summary>
        public string? Technology { get; }

        /// <summary>
        /// Gets the optional exact rule-code filter.
        /// </summary>
        public string? RuleCode { get; }

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
