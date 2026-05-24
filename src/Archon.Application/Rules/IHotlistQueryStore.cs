using Archon.Domain.Graph.Model;

namespace Archon.Application.Rules
{
    /// <summary>
    /// Provides controlled persistence-backed read access for WP012 rule catalog and finding query APIs.
    /// </summary>
    public interface IHotlistQueryStore
    {
        /// <summary>
        /// Retrieves persisted rules matching the supplied controlled catalog query.
        /// </summary>
        /// <param name="query">The controlled filter and paging contract for the catalog query.</param>
        /// <param name="cancellationToken">The cancellation token that can stop retrieval before adapter work starts.</param>
        /// <returns>A bounded page of persisted rule catalog entries.</returns>
        Task<PagedQueryResult<RuleCatalogEntry>> QueryRulesAsync(RuleCatalogQuery query, CancellationToken cancellationToken);

        /// <summary>
        /// Retrieves one persisted rule by exact rule code and version.
        /// </summary>
        /// <param name="ruleCode">The stable rule code.</param>
        /// <param name="version">The exact rule version.</param>
        /// <param name="cancellationToken">The cancellation token that can stop retrieval before adapter work starts.</param>
        /// <returns>The matching rule entry, or <see langword="null"/> when none exists.</returns>
        Task<RuleCatalogEntry?> GetRuleAsync(string ruleCode, string version, CancellationToken cancellationToken);

        /// <summary>
        /// Retrieves persisted findings matching the supplied controlled hotlist query.
        /// </summary>
        /// <param name="query">The controlled filter and paging contract for the hotlist query.</param>
        /// <param name="cancellationToken">The cancellation token that can stop retrieval before adapter work starts.</param>
        /// <returns>A bounded page of persisted finding records.</returns>
        Task<PagedQueryResult<FindingRecord>> QueryFindingsAsync(HotlistQuery query, CancellationToken cancellationToken);

        /// <summary>
        /// Retrieves historical finding records for one cross-snapshot history key.
        /// </summary>
        /// <param name="historyKey">The deterministic finding history key.</param>
        /// <param name="cancellationToken">The cancellation token that can stop retrieval before adapter work starts.</param>
        /// <returns>The historical finding records in deterministic snapshot order.</returns>
        Task<IReadOnlyList<FindingRecord>> GetFindingHistoryRecordsAsync(string historyKey, CancellationToken cancellationToken);
    }
}
