using Archon.Application.Rules;
using Archon.Domain.Graph.Model;

namespace Archon.Application.Metrics
{
    /// <summary>
    /// Implements controlled snapshot metric query behavior for API and future MCP consumers.
    /// </summary>
    public sealed class MetricQueryService : IMetricQueryService
    {
        /// <summary>
        /// Executes controlled persistence-backed metric queries.
        /// </summary>
        private readonly IMetricQueryStore _queryStore;

        /// <summary>
        /// Initializes a new instance of the <see cref="MetricQueryService"/> class.
        /// </summary>
        /// <param name="queryStore">The controlled query store used for metric reads.</param>
        public MetricQueryService(IMetricQueryStore queryStore)
        {
            // Keeping metric DTO shaping in Application allows API and future MCP surfaces to share one controlled response model.
            _queryStore = queryStore ?? throw new ArgumentNullException(nameof(queryStore));
        }

        /// <summary>
        /// Lists persisted snapshot metrics using controlled filters and bounded paging.
        /// </summary>
        /// <param name="query">The controlled metric filter and paging contract.</param>
        /// <param name="cancellationToken">The cancellation token that can stop retrieval before store work starts.</param>
        /// <returns>A page of stable metric DTOs.</returns>
        public async Task<PagedQueryResult<MetricItemDto>> ListMetricsAsync(MetricQuery query, CancellationToken cancellationToken)
        {
            // The store handles persistence-specific filtering; this service maps domain records into sanitized public DTOs.
            ArgumentNullException.ThrowIfNull(query);
            PagedQueryResult<MetricRecord> result = await _queryStore.QueryMetricsAsync(query, cancellationToken).ConfigureAwait(false);
            return new PagedQueryResult<MetricItemDto>(result.Items.Select(ToMetricItem), result.TotalCount, result.Skip, result.Take);
        }

        /// <summary>
        /// Maps a persisted metric record to a public metric item DTO.
        /// </summary>
        /// <param name="metric">The persisted metric record.</param>
        /// <returns>The stable metric item DTO.</returns>
        private static MetricItemDto ToMetricItem(MetricRecord metric)
        {
            // Public metric responses expose stable identities and sanitized metadata but never database-local identifiers.
            return new MetricItemDto(
                metric.SnapshotStableKey.Value,
                metric.StableKey.Value,
                metric.MetricKind,
                metric.ScopeKind.Value,
                metric.NodeStableKey?.Value,
                metric.EdgeStableKey?.Value,
                metric.PrimaryEvidenceStableKey?.Value,
                metric.Name,
                metric.NumericValue,
                metric.TextValue,
                metric.Unit,
                metric.Confidence.Value,
                metric.UnknownState.HasUnknownData,
                metric.UnknownState.UnknownReason,
                PublicMetadataSanitizer.Sanitize(metric.Metadata),
                metric.Fingerprint.Value);
        }
    }
}
