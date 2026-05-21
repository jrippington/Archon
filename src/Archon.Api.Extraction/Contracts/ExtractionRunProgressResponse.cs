namespace Archon.Api.Extraction.Contracts
{
    /// <summary>
    /// Represents the current progress details returned by extraction status responses.
    /// </summary>
    /// <param name="Stage">The current lifecycle or workflow stage name.</param>
    /// <param name="Message">The credential-safe human-readable progress message.</param>
    /// <param name="Percentage">The optional progress percentage when available.</param>
    /// <param name="LastUpdatedUtc">The UTC timestamp when progress was last updated.</param>
    public sealed record ExtractionRunProgressResponse(
        string Stage,
        string Message,
        int? Percentage,
        DateTimeOffset LastUpdatedUtc);
}
