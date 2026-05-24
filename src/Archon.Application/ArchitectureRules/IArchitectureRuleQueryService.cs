using Archon.Application.Rules;

namespace Archon.Application.ArchitectureRules
{
    /// <summary>
    /// Defines controlled query behavior for WP013 architecture-rule results.
    /// </summary>
    public interface IArchitectureRuleQueryService
    {
        /// <summary>
        /// Lists architecture-rule results using fixed snapshot, category, status, target, and paging filters.
        /// </summary>
        /// <param name="query">The controlled architecture-rule query contract.</param>
        /// <param name="cancellationToken">The cancellation token that can stop retrieval before evaluation starts.</param>
        /// <returns>A bounded page of stable architecture-rule result DTOs.</returns>
        Task<PagedQueryResult<ArchitectureRuleItemDto>> ListArchitectureRulesAsync(ArchitectureRuleQuery query, CancellationToken cancellationToken);
    }
}
