namespace Archon.Api.Extraction.Contracts
{
    /// <summary>
    /// Represents a warning or error diagnostic returned by extraction status responses.
    /// </summary>
    /// <param name="Code">The stable diagnostic category or code.</param>
    /// <param name="Message">The credential-safe diagnostic message.</param>
    /// <param name="Stage">The workflow stage that produced the diagnostic.</param>
    /// <param name="CreatedUtc">The UTC timestamp when the diagnostic was recorded.</param>
    public sealed record ExtractionRunDiagnosticResponse(
        string Code,
        string Message,
        string Stage,
        DateTimeOffset CreatedUtc);
}
