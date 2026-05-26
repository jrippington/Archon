namespace Archon.Application.Facts
{
    /// <summary>
    /// Defines controlled WP014 fact-query operations for data-access, configuration, integration, and UI-technology facts.
    /// </summary>
    public interface IFactQueryService
    {
        /// <summary>
        /// Lists data-access architecture facts for one bounded repository, solution, and snapshot scope.
        /// </summary>
        /// <param name="query">The controlled data-access fact query supplied by the API layer.</param>
        /// <param name="cancellationToken">The token that cancels the query when the HTTP request is aborted.</param>
        /// <returns>A successful bounded data-access fact page or deterministic validation errors.</returns>
        Task<DataAccessFactResult> ListDataAccessFactsAsync(DataAccessFactQuery query, CancellationToken cancellationToken);

        /// <summary>
        /// Lists secret-safe configuration usage facts for one bounded repository, solution, and snapshot scope.
        /// </summary>
        /// <param name="query">The controlled configuration usage query supplied by the API layer.</param>
        /// <param name="cancellationToken">The token that cancels the query when the HTTP request is aborted.</param>
        /// <returns>A successful bounded configuration usage page or deterministic validation errors.</returns>
        Task<ConfigurationUsageResult> ListConfigurationUsageAsync(ConfigurationUsageQuery query, CancellationToken cancellationToken);

        /// <summary>
        /// Lists secret-safe external integration facts for one bounded repository, solution, and snapshot scope.
        /// </summary>
        /// <param name="query">The controlled integration fact query supplied by the API layer.</param>
        /// <param name="cancellationToken">The token that cancels the query when the HTTP request is aborted.</param>
        /// <returns>A successful bounded integration fact page or deterministic validation errors.</returns>
        Task<IntegrationFactResult> ListIntegrationFactsAsync(IntegrationFactQuery query, CancellationToken cancellationToken);

        /// <summary>
        /// Lists backend UI-technology architecture facts for one bounded repository, solution, and snapshot scope.
        /// </summary>
        /// <param name="query">The controlled UI-technology fact query supplied by the API layer.</param>
        /// <param name="cancellationToken">The token that cancels the query when the HTTP request is aborted.</param>
        /// <returns>A successful bounded UI-technology fact page or deterministic validation errors.</returns>
        Task<UiTechnologyFactResult> ListUiTechnologyFactsAsync(UiTechnologyFactQuery query, CancellationToken cancellationToken);
    }
}
