using Archon.Application.Rules;

namespace Archon.Application.Metrics
{
    /// <summary>
    /// Provides application-level query operations for persisted snapshot metrics.
    /// </summary>
    public interface IMetricQueryService
    {
        /// <summary>
        /// Lists persisted snapshot metrics using controlled filters and bounded paging.
        /// </summary>
        /// <param name="query">The controlled metric filter and paging contract.</param>
        /// <param name="cancellationToken">The cancellation token that can stop retrieval before store work starts.</param>
        /// <returns>A page of stable metric DTOs.</returns>
        Task<PagedQueryResult<MetricItemDto>> ListMetricsAsync(MetricQuery query, CancellationToken cancellationToken);
    }
}
