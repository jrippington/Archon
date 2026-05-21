using Archon.Application.Extraction.Runs;
using Archon.Application.Extraction.Validation;

namespace Archon.Application.Extraction.Requests
{
    /// <summary>
    /// Represents the result of attempting to accept an extraction start request.
    /// </summary>
    /// <param name="Run">The created run when the request was accepted.</param>
    /// <param name="ValidationErrors">The validation errors when the request was rejected before acceptance.</param>
    public sealed record StartExtractionResult(
        ExtractionRun? Run,
        IReadOnlyList<StartExtractionValidationError> ValidationErrors)
    {
        /// <summary>
        /// Gets a value indicating whether the request was accepted and run state was created.
        /// </summary>
        public bool Accepted => Run is not null && ValidationErrors.Count == 0;
    }
}
