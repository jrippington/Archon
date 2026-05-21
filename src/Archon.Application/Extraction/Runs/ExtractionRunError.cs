namespace Archon.Application.Extraction.Runs
{
    /// <summary>
    /// Describes a blocking or terminal error captured for an extraction run.
    /// </summary>
    /// <param name="Code">The stable error category or code.</param>
    /// <param name="Message">The credential-safe error message.</param>
    /// <param name="Stage">The workflow stage that produced the error.</param>
    /// <param name="CreatedUtc">The UTC timestamp when the error was recorded.</param>
    public sealed record ExtractionRunError(
        string Code,
        string Message,
        string Stage,
        DateTimeOffset CreatedUtc);
}
