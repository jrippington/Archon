namespace Archon.Application.Symbols
{
    /// <summary>
    /// Defines controlled symbol query operations over extracted architecture snapshots.
    /// </summary>
    public interface ISymbolQueryService
    {
        /// <summary>
        /// Lists symbols from one selected snapshot using bounded filters, deterministic ordering, and paging.
        /// </summary>
        /// <param name="query">The symbol search request containing scope, filters, sort, and paging options.</param>
        /// <param name="cancellationToken">The token that cancels query execution when the caller abandons the request.</param>
        /// <returns>A symbol search result containing either a page of symbols or validation errors.</returns>
        Task<SymbolSearchResult> SearchSymbolsAsync(SymbolSearchQuery query, CancellationToken cancellationToken);

        /// <summary>
        /// Gets one symbol detail by stable key or by an exact search-text identity within a selected snapshot.
        /// </summary>
        /// <param name="query">The symbol detail request containing scope and symbol identity.</param>
        /// <param name="cancellationToken">The token that cancels query execution when the caller abandons the request.</param>
        /// <returns>A symbol detail result containing either the selected symbol or validation errors.</returns>
        Task<SymbolDetailResult> GetSymbolAsync(SymbolDetailQuery query, CancellationToken cancellationToken);

        /// <summary>
        /// Lists referencing or calling usages for a selected symbol within one selected snapshot.
        /// </summary>
        /// <param name="query">The symbol usage request containing scope, symbol identity, usage direction, and paging options.</param>
        /// <param name="cancellationToken">The token that cancels query execution when the caller abandons the request.</param>
        /// <returns>A symbol usage result containing either a page of usages or validation errors.</returns>
        Task<SymbolUsageResult> ListSymbolUsagesAsync(SymbolUsageQuery query, CancellationToken cancellationToken);
    }
}
