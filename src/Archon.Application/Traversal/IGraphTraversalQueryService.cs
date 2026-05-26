namespace Archon.Application.Traversal
{
    /// <summary>
    /// Defines controlled bounded graph traversal query operations for WP014 dependency exploration endpoints.
    /// </summary>
    public interface IGraphTraversalQueryService
    {
        /// <summary>
        /// Executes a bounded traversal from one stable node identity.
        /// </summary>
        /// <param name="query">The controlled traversal request that supplies scope, start node, direction, depth, edge-kind filters, and result limit.</param>
        /// <param name="cancellationToken">The cancellation token that can stop traversal before or during graph exploration.</param>
        /// <returns>A traversal result containing either stable graph nodes and edges or deterministic validation errors.</returns>
        Task<GraphTraversalResult> TraverseAsync(GraphTraversalQuery query, CancellationToken cancellationToken);

        /// <summary>
        /// Searches for a bounded dependency path between two stable node identities.
        /// </summary>
        /// <param name="query">The controlled path request that supplies scope, source, target, depth, and edge-kind filters.</param>
        /// <param name="cancellationToken">The cancellation token that can stop path search before or during graph exploration.</param>
        /// <returns>A dependency-path result containing a found path, a no-path payload, an unavailable-data payload, or deterministic validation errors.</returns>
        Task<DependencyPathResult> GetDependencyPathAsync(DependencyPathQuery query, CancellationToken cancellationToken);
    }
}
