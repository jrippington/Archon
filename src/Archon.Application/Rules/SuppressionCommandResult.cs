namespace Archon.Application.Rules
{
    /// <summary>
    /// Represents the public application result of a controlled suppression command.
    /// </summary>
    public sealed class SuppressionCommandResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SuppressionCommandResult"/> class.
        /// </summary>
        /// <param name="succeeded">Indicates whether suppression validation and persistence succeeded.</param>
        /// <param name="suppressedCount">The number of currently persisted findings updated by the suppression.</param>
        /// <param name="validationErrors">The stable validation errors returned for invalid commands.</param>
        /// <param name="warnings">The non-fatal warnings returned by persistence.</param>
        /// <param name="errors">The fatal persistence errors returned by the store.</param>
        private SuppressionCommandResult(bool succeeded, int suppressedCount, IEnumerable<SuppressFindingValidationError> validationErrors, IEnumerable<string> warnings, IEnumerable<string> errors)
        {
            // The result separates validation errors from persistence errors so API endpoints can return the correct HTTP shape.
            Succeeded = succeeded;
            SuppressedCount = Math.Max(0, suppressedCount);
            ValidationErrors = validationErrors.ToArray();
            Warnings = warnings.Where(static warning => !string.IsNullOrWhiteSpace(warning)).Select(static warning => warning.Trim()).ToArray();
            Errors = errors.Where(static error => !string.IsNullOrWhiteSpace(error)).Select(static error => error.Trim()).ToArray();
        }

        /// <summary>Gets a value indicating whether suppression validation and persistence succeeded.</summary>
        public bool Succeeded { get; }

        /// <summary>Gets the number of currently persisted findings updated by the suppression.</summary>
        public int SuppressedCount { get; }

        /// <summary>Gets stable validation errors returned for invalid commands.</summary>
        public IReadOnlyList<SuppressFindingValidationError> ValidationErrors { get; }

        /// <summary>Gets non-fatal warnings returned by persistence.</summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>Gets fatal persistence errors returned by the store.</summary>
        public IReadOnlyList<string> Errors { get; }

        /// <summary>
        /// Creates a successful suppression command result.
        /// </summary>
        /// <param name="suppressedCount">The number of currently persisted findings updated by the suppression.</param>
        /// <param name="warnings">The non-fatal warnings returned by persistence.</param>
        /// <returns>A successful command result.</returns>
        public static SuppressionCommandResult Success(int suppressedCount, IEnumerable<string> warnings)
        {
            // Success keeps warnings so API consumers can display non-blocking infrastructure diagnostics.
            return new SuppressionCommandResult(true, suppressedCount, [], warnings, []);
        }

        /// <summary>
        /// Creates a validation-failure suppression command result.
        /// </summary>
        /// <param name="validationErrors">The validation errors to expose.</param>
        /// <param name="warnings">The non-fatal warnings returned while validating or persisting.</param>
        /// <returns>A failed command result with validation diagnostics.</returns>
        public static SuppressionCommandResult ValidationFailure(IEnumerable<SuppressFindingValidationError> validationErrors, IEnumerable<string> warnings)
        {
            // Validation failures are caller-actionable and should map to HTTP validation problem responses.
            return new SuppressionCommandResult(false, 0, validationErrors, warnings, []);
        }

        /// <summary>
        /// Creates a persistence-failure suppression command result.
        /// </summary>
        /// <param name="errors">The fatal persistence errors to expose.</param>
        /// <param name="warnings">The non-fatal warnings returned while persisting.</param>
        /// <returns>A failed command result with fatal diagnostics.</returns>
        public static SuppressionCommandResult Failure(IEnumerable<string> errors, IEnumerable<string> warnings)
        {
            // Persistence failures are not validation problems and should map to controlled server errors.
            return new SuppressionCommandResult(false, 0, [], warnings, errors);
        }
    }
}
