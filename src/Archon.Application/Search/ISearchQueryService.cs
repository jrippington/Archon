namespace Archon.Application.Search
{
    /// <summary>
    /// Defines controlled application behavior for broad cross-domain search over one resolved architecture snapshot.
    /// </summary>
    public interface ISearchQueryService
    {
        /// <summary>
        /// Searches supported snapshot record families using stable keys, safe text fields, and bounded result limits.
        /// </summary>
        /// <param name="query">The controlled search request supplied by API or future MCP callers.</param>
        /// <param name="cancellationToken">The token that can cancel snapshot resolution and result projection.</param>
        /// <returns>A search result containing a bounded page, metadata context, or validation errors.</returns>
        Task<SearchResult> SearchAsync(SearchQuery query, CancellationToken cancellationToken);
    }
}
