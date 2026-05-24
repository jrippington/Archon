using Archon.Application.Rules;

namespace Archon.Application.Metrics
{
    /// <summary>
    /// Represents controlled filters and paging options for snapshot metric queries.
    /// </summary>
    public sealed class MetricQuery
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MetricQuery"/> class.
        /// </summary>
        /// <param name="snapshotStableKey">The required snapshot stable key whose metrics should be queried.</param>
        /// <param name="metricKind">The optional exact metric kind filter.</param>
        /// <param name="scopeKind">The optional exact metric scope kind filter.</param>
        /// <param name="projectStableKey">The optional exact project or node stable-key filter for project-scoped and graph node-scoped metrics.</param>
        /// <param name="skip">The optional number of sorted records to skip.</param>
        /// <param name="take">The optional maximum number of sorted records to return.</param>
        public MetricQuery(string? snapshotStableKey, string? metricKind, string? scopeKind, string? projectStableKey, int? skip, int? take)
        {
            // Query construction validates contract-level paging and leaves exact filter text to persistence adapters.
            SnapshotStableKey = string.IsNullOrWhiteSpace(snapshotStableKey) ? throw new ArgumentException("A snapshot stable key is required for metric queries.", nameof(snapshotStableKey)) : snapshotStableKey.Trim();
            MetricKind = string.IsNullOrWhiteSpace(metricKind) ? null : metricKind.Trim();
            ScopeKind = string.IsNullOrWhiteSpace(scopeKind) ? null : scopeKind.Trim();
            ProjectStableKey = string.IsNullOrWhiteSpace(projectStableKey) ? null : projectStableKey.Trim();
            Skip = ValidateSkip(skip);
            Take = ValidateTake(take);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MetricQuery"/> class without a project stable-key filter.
        /// </summary>
        /// <param name="snapshotStableKey">The required snapshot stable key whose metrics should be queried.</param>
        /// <param name="metricKind">The optional exact metric kind filter.</param>
        /// <param name="scopeKind">The optional exact metric scope kind filter.</param>
        /// <param name="skip">The optional number of sorted records to skip.</param>
        /// <param name="take">The optional maximum number of sorted records to return.</param>
        public MetricQuery(string? snapshotStableKey, string? metricKind, string? scopeKind, int? skip, int? take)
            : this(snapshotStableKey, metricKind, scopeKind, projectStableKey: null, skip, take)
        {
            // This overload preserves the Work Item 1 snapshot metric query shape while later slices add node-target filtering.
        }

        /// <summary>
        /// Gets the required snapshot stable key whose metrics should be queried.
        /// </summary>
        public string SnapshotStableKey { get; }

        /// <summary>
        /// Gets the optional exact metric kind filter.
        /// </summary>
        public string? MetricKind { get; }

        /// <summary>
        /// Gets the optional exact metric scope kind filter.
        /// </summary>
        public string? ScopeKind { get; }

        /// <summary>
        /// Gets the optional exact project or node stable-key filter for project-scoped and graph node-scoped metrics.
        /// </summary>
        public string? ProjectStableKey { get; }

        /// <summary>
        /// Gets the number of sorted records to skip.
        /// </summary>
        public int Skip { get; }

        /// <summary>
        /// Gets the maximum number of sorted records to return.
        /// </summary>
        public int Take { get; }

        /// <summary>
        /// Validates the optional skip value used by a public metric query.
        /// </summary>
        /// <param name="skip">The optional caller-provided skip value.</param>
        /// <returns>The validated non-negative skip value.</returns>
        private static int ValidateSkip(int? skip)
        {
            // Negative paging values indicate a malformed continuation request and should be surfaced to callers explicitly.
            if (skip.GetValueOrDefault(0) < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(skip), skip, "Skip must be greater than or equal to zero.");
            }

            return skip.GetValueOrDefault(0);
        }

        /// <summary>
        /// Validates the optional take value used by a public metric query.
        /// </summary>
        /// <param name="take">The optional caller-provided take value.</param>
        /// <returns>The validated page size.</returns>
        private static int ValidateTake(int? take)
        {
            // Bounded page sizes protect API consumers and storage adapters from accidental unbounded reads.
            int value = take.GetValueOrDefault(QueryPagingOptions.DefaultPageSize);
            if (value < 1 || value > QueryPagingOptions.MaximumPageSize)
            {
                throw new ArgumentOutOfRangeException(nameof(take), take, "Take must be between 1 and 500.");
            }

            return value;
        }
    }
}
