namespace Archon.Application.Management
{
    /// <summary>
    /// Represents a bounded extraction run-history query result.
    /// </summary>
    /// <param name="Items">The run-history rows returned after filtering and bounds were applied.</param>
    /// <param name="TotalCount">The total number of rows matching filters before the take bound.</param>
    /// <param name="Take">The effective result-size bound.</param>
    public sealed record ExtractionRunHistoryResponse(IReadOnlyList<ExtractionRunHistoryItemResponse> Items, int TotalCount, int Take);
}
