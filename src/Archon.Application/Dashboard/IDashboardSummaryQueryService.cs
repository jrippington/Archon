namespace Archon.Application.Dashboard
{
    /// <summary>
    /// Defines application-layer dashboard summary query behavior for API and future MCP consumers.
    /// </summary>
    public interface IDashboardSummaryQueryService
    {
        /// <summary>
        /// Retrieves a deterministic dashboard summary for the selected repository, solution, and snapshot scope.
        /// </summary>
        /// <param name="selector">The caller-supplied dashboard snapshot selector.</param>
        /// <param name="cancellationToken">The token that can cancel query work before snapshot facts are read.</param>
        /// <returns>A successful dashboard summary or deterministic validation errors.</returns>
        Task<DashboardSummaryResult> GetDashboardSummaryAsync(DashboardSnapshotSelector selector, CancellationToken cancellationToken);
    }
}