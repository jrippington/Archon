namespace Archon.Application.Rules
{
    /// <summary>
    /// Describes controlled filters and paging for WP012 rule catalog list queries.
    /// </summary>
    public sealed class RuleCatalogQuery
    {
        /// <summary>
        /// Defines the default number of rule records returned when callers omit a page size.
        /// </summary>
        public const int DefaultPageSize = 50;

        /// <summary>
        /// Defines the maximum number of rule records returned by one controlled catalog query.
        /// </summary>
        public const int MaximumPageSize = 200;

        /// <summary>
        /// Initializes a new instance of the <see cref="RuleCatalogQuery"/> class.
        /// </summary>
        /// <param name="ruleCode">The optional exact rule code filter.</param>
        /// <param name="version">The optional exact rule version filter.</param>
        /// <param name="category">The optional exact rule category filter.</param>
        /// <param name="severity">The optional exact default severity filter.</param>
        /// <param name="enabled">The optional enabled-state filter.</param>
        /// <param name="builtIn">The optional built-in-state filter.</param>
        /// <param name="ownerScope">The optional exact owner-scope filter.</param>
        /// <param name="skip">The number of sorted records to skip before returning results.</param>
        /// <param name="take">The maximum number of sorted records to return.</param>
        public RuleCatalogQuery(
            string? ruleCode,
            string? version,
            string? category,
            string? severity,
            bool? enabled,
            bool? builtIn,
            string? ownerScope,
            int? skip,
            int? take)
        {
            // Query contracts normalize text at the boundary so application services and adapters can compare deterministically.
            RuleCode = NormalizeOptionalText(ruleCode);
            Version = NormalizeOptionalText(version);
            Category = NormalizeOptionalText(category);
            Severity = NormalizeOptionalText(severity);
            Enabled = enabled;
            BuiltIn = builtIn;
            OwnerScope = NormalizeOptionalText(ownerScope);
            Skip = Math.Max(0, skip.GetValueOrDefault(0));
            Take = Math.Clamp(take.GetValueOrDefault(DefaultPageSize), 1, MaximumPageSize);
        }

        /// <summary>
        /// Gets the optional exact rule code filter.
        /// </summary>
        public string? RuleCode { get; }

        /// <summary>
        /// Gets the optional exact rule version filter.
        /// </summary>
        public string? Version { get; }

        /// <summary>
        /// Gets the optional exact rule category filter.
        /// </summary>
        public string? Category { get; }

        /// <summary>
        /// Gets the optional exact default severity filter.
        /// </summary>
        public string? Severity { get; }

        /// <summary>
        /// Gets the optional enabled-state filter.
        /// </summary>
        public bool? Enabled { get; }

        /// <summary>
        /// Gets the optional built-in-state filter.
        /// </summary>
        public bool? BuiltIn { get; }

        /// <summary>
        /// Gets the optional exact owner-scope filter.
        /// </summary>
        public string? OwnerScope { get; }

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
            // Treating whitespace as absent avoids surprising zero-result filters from accidental spaces.
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
