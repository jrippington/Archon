using Archon.Application.Rules;

namespace Archon.Application.Cycles
{
    /// <summary>
    /// Provides controlled application behavior for dependency cycle query surfaces.
    /// </summary>
    public interface ICycleQueryService
    {
        /// <summary>
        /// Lists detected dependency cycles using controlled snapshot, node, and paging filters.
        /// </summary>
        /// <param name="query">The controlled cycle query contract.</param>
        /// <param name="cancellationToken">The cancellation token that can stop retrieval before graph work starts.</param>
        /// <returns>A bounded page of stable cycle DTOs.</returns>
        Task<PagedQueryResult<CycleItemDto>> ListCyclesAsync(CycleQuery query, CancellationToken cancellationToken);
    }
}
