namespace Archon.Api.Extraction.Contracts
{
    /// <summary>
    /// Represents one extraction run timing measurement returned by the status endpoint.
    /// </summary>
    /// <param name="Stage">The measured major stage or pipeline stage identifier.</param>
    /// <param name="ElapsedMilliseconds">The elapsed duration in milliseconds.</param>
    /// <param name="CompletedUtc">The UTC timestamp when the measured step completed.</param>
    public sealed record ExtractionRunTimingResponse(
        string Stage,
        long ElapsedMilliseconds,
        DateTimeOffset CompletedUtc);
}
