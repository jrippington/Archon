namespace Archon.Api.Extraction.Contracts
{
    /// <summary>
    /// Represents the API response returned by the recent extraction run history endpoint.
    /// </summary>
    /// <param name="Runs">The recent run summaries ordered in deterministic newest-first order.</param>
    public sealed record ExtractionRunHistoryResponse(IReadOnlyList<ExtractionRunSummaryResponse> Runs);
}
