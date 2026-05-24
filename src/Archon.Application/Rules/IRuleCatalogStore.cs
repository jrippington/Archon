namespace Archon.Application.Rules
{
    /// <summary>
    /// Persists and retrieves validated WP012 rule catalog records without exposing infrastructure details to application services.
    /// </summary>
    public interface IRuleCatalogStore
    {
        /// <summary>
        /// Upserts validated rule catalog entries by stable rule code and exact rule version.
        /// </summary>
        /// <param name="rules">The validated catalog entries to persist as versioned catalog records.</param>
        /// <param name="cancellationToken">The cancellation token that can stop persistence before adapter work starts.</param>
        /// <returns>A result describing the catalog upsert outcome and credential-safe diagnostics.</returns>
        Task<RuleCatalogUpsertResult> UpsertRulesAsync(IEnumerable<RuleCatalogEntry> rules, CancellationToken cancellationToken);

        /// <summary>
        /// Retrieves query-friendly persisted rule catalog entries in deterministic rule code and version order.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token that can stop retrieval before adapter work starts.</param>
        /// <returns>The persisted rule catalog entries available through the configured adapter.</returns>
        Task<IReadOnlyList<RuleCatalogEntry>> GetRulesAsync(CancellationToken cancellationToken);
    }
}
