using Archon.Application.Rules;

namespace Archon.Application.Hotspots
{
    /// <summary>
    /// Defines controlled application query behavior for architecture hotspots.
    /// </summary>
    public interface IHotspotQueryService
    {
        /// <summary>
        /// Lists detected hotspots using controlled snapshot, target, category, and paging filters.
        /// </summary>
        /// <param name="query">The controlled hotspot query contract.</param>
        /// <param name="cancellationToken">The cancellation token that can stop retrieval before graph work starts.</param>
        /// <returns>A bounded page of stable hotspot DTOs.</returns>
        Task<PagedQueryResult<HotspotItemDto>> ListHotspotsAsync(HotspotQuery query, CancellationToken cancellationToken);
    }
}
