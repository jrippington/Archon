namespace Archon.Application.Evidence
{
    /// <summary>
    /// Defines controlled WP014 evidence drill-down operations over persisted architecture snapshots.
    /// </summary>
    public interface IEvidenceQueryService
    {
        /// <summary>
        /// Resolves one evidence record by stable key for a bounded repository, solution, and snapshot scope.
        /// </summary>
        /// <param name="query">The controlled evidence detail query supplied by the API layer.</param>
        /// <param name="cancellationToken">The token that cancels the query when the HTTP request is aborted.</param>
        /// <returns>A successful evidence detail payload or deterministic validation errors.</returns>
        Task<EvidenceDetailResult> GetEvidenceAsync(EvidenceDetailQuery query, CancellationToken cancellationToken);

        /// <summary>
        /// Lists evidence records related to a node, edge, finding, metric, or rule result stable identity.
        /// </summary>
        /// <param name="query">The controlled related-evidence query supplied by the API layer.</param>
        /// <param name="cancellationToken">The token that cancels the query when the HTTP request is aborted.</param>
        /// <returns>A successful bounded evidence page or deterministic validation errors.</returns>
        Task<RelatedEvidenceResult> ListRelatedEvidenceAsync(RelatedEvidenceQuery query, CancellationToken cancellationToken);
    }
}
