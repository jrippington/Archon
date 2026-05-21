namespace Archon.Application.Extraction.Runs
{
    /// <summary>
    /// Describes a non-blocking warning captured during extraction request handling or execution.
    /// </summary>
    /// <param name="Code">The stable warning category or code.</param>
    /// <param name="Message">The credential-safe warning message.</param>
    /// <param name="Stage">The workflow stage that produced the warning.</param>
    /// <param name="CreatedUtc">The UTC timestamp when the warning was recorded.</param>
    public sealed record ExtractionRunWarning(
        string Code,
        string Message,
        string Stage,
        DateTimeOffset CreatedUtc);
}
