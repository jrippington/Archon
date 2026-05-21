using Archon.Application.Extraction.Resolution;

namespace Archon.Application.Extraction.Validation
{
    /// <summary>
    /// Carries either a resolved extraction input or the validation errors that prevented acceptance.
    /// </summary>
    /// <param name="ResolvedInput">The normalized execution input when validation succeeds.</param>
    /// <param name="Errors">The validation errors when validation fails.</param>
    public sealed record StartExtractionValidationResult(
        ResolvedExtractionInput? ResolvedInput,
        IReadOnlyList<StartExtractionValidationError> Errors)
    {
        /// <summary>
        /// Gets a value indicating whether validation produced a normalized input and no blocking errors.
        /// </summary>
        public bool IsValid => ResolvedInput is not null && Errors.Count == 0;
    }
}
