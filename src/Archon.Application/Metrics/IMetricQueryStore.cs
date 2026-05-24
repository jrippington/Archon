using Archon.Application.Rules;
using Archon.Domain.Graph.Model;

namespace Archon.Application.Metrics
{
    /// <summary>
    /// Provides controlled persistence-backed read access for snapshot metrics.
    /// </summary>
    public interface IMetricQueryStore
    {
        /// <summary>
        /// Retrieves persisted metrics matching the supplied controlled snapshot metric query.
        /// </summary>
        /// <param name="query">The controlled filter and paging contract for the metric query.</param>
        /// <param name="cancellationToken">The cancellation token that can stop retrieval before adapter work starts.</param>
        /// <returns>A bounded page of persisted metric records.</returns>
        Task<PagedQueryResult<MetricRecord>> QueryMetricsAsync(MetricQuery query, CancellationToken cancellationToken);
    }
}
