namespace Archon.Application.Extraction.Validation
{
    /// <summary>
    /// Describes one user-actionable validation failure that prevents an extraction request from being accepted.
    /// </summary>
    /// <param name="Code">The stable validation error code used by API responses and tests.</param>
    /// <param name="Message">The credential-safe validation message that can be returned to callers.</param>
    public sealed record StartExtractionValidationError(string Code, string Message);
}
