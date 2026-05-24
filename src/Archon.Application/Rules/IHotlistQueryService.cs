namespace Archon.Application.Rules
{
    /// <summary>
    /// Provides application-level query operations for WP012 rule catalog, hotlist, finding detail, history, and suppression APIs.
    /// </summary>
    public interface IHotlistQueryService
    {
        /// <summary>
        /// Lists persisted rule catalog entries using controlled filters and bounded paging.
        /// </summary>
        /// <param name="query">The controlled catalog filter and paging contract.</param>
        /// <param name="cancellationToken">The cancellation token that can stop retrieval before store work starts.</param>
        /// <returns>A page of stable rule catalog DTOs.</returns>
        Task<PagedQueryResult<RuleCatalogItemDto>> ListRulesAsync(RuleCatalogQuery query, CancellationToken cancellationToken);

        /// <summary>
        /// Retrieves one persisted rule detail by exact rule code and version.
        /// </summary>
        /// <param name="ruleCode">The stable rule code.</param>
        /// <param name="version">The exact rule version.</param>
        /// <param name="cancellationToken">The cancellation token that can stop retrieval before store work starts.</param>
        /// <returns>The matching rule detail DTO, or <see langword="null"/> when none exists.</returns>
        Task<RuleDetailDto?> GetRuleAsync(string ruleCode, string version, CancellationToken cancellationToken);

        /// <summary>
        /// Lists persisted findings using controlled hotlist filters and bounded paging.
        /// </summary>
        /// <param name="query">The controlled hotlist filter and paging contract.</param>
        /// <param name="cancellationToken">The cancellation token that can stop retrieval before store work starts.</param>
        /// <returns>A page of stable hotlist item DTOs.</returns>
        Task<PagedQueryResult<HotlistItemDto>> ListHotlistAsync(HotlistQuery query, CancellationToken cancellationToken);

        /// <summary>
        /// Retrieves one persisted finding detail by snapshot stable key and finding stable key.
        /// </summary>
        /// <param name="snapshotStableKey">The snapshot stable key that scopes the finding.</param>
        /// <param name="findingStableKey">The finding stable key.</param>
        /// <param name="cancellationToken">The cancellation token that can stop retrieval before store work starts.</param>
        /// <returns>The matching finding detail DTO, or <see langword="null"/> when none exists.</returns>
        Task<FindingDetailDto?> GetFindingAsync(string snapshotStableKey, string findingStableKey, CancellationToken cancellationToken);

        /// <summary>
        /// Retrieves cross-snapshot history for one finding history key.
        /// </summary>
        /// <param name="historyKey">The deterministic finding history key.</param>
        /// <param name="cancellationToken">The cancellation token that can stop retrieval before store work starts.</param>
        /// <returns>The matching finding history DTO, or <see langword="null"/> when no history exists.</returns>
        Task<FindingHistoryDto?> GetFindingHistoryAsync(string historyKey, CancellationToken cancellationToken);

        /// <summary>
        /// Validates and persists a suppression overlay through the configured finding store.
        /// </summary>
        /// <param name="command">The suppression command supplied by the API boundary.</param>
        /// <param name="cancellationToken">The cancellation token that can stop validation or persistence before store work starts.</param>
        /// <returns>A command result describing success, validation errors, warnings, or persistence errors.</returns>
        Task<SuppressionCommandResult> SuppressFindingAsync(SuppressFindingCommand command, CancellationToken cancellationToken);
    }
}
