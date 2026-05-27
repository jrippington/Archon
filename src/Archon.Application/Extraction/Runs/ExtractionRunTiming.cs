namespace Archon.Application.Extraction.Runs
{
    /// <summary>
    /// Represents one measured extraction run step duration for status diagnostics.
    /// </summary>
    /// <param name="Stage">The major stage or pipeline stage identifier that was measured.</param>
    /// <param name="ElapsedMilliseconds">The elapsed duration in milliseconds.</param>
    /// <param name="CompletedUtc">The UTC timestamp when the measured step completed.</param>
    public sealed record ExtractionRunTiming(
        string Stage,
        long ElapsedMilliseconds,
        DateTimeOffset CompletedUtc);
}
