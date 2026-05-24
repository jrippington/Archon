namespace Archon.Application.Rules
{
    /// <summary>
    /// Describes the outcome of persisting and applying finding suppressions through an application-layer port.
    /// </summary>
    public sealed class SuppressionPersistenceResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SuppressionPersistenceResult"/> class.
        /// </summary>
        /// <param name="succeeded">A value indicating whether suppression persistence completed without blocking errors.</param>
        /// <param name="suppressedFindingCount">The number of findings that received suppression overlays.</param>
        /// <param name="validationErrors">The validation errors that prevented one or more suppressions.</param>
        /// <param name="warnings">The non-blocking diagnostics produced while applying suppressions.</param>
        /// <param name="errors">The blocking diagnostics produced while applying suppressions.</param>
        private SuppressionPersistenceResult(
            bool succeeded,
            int suppressedFindingCount,
            IEnumerable<SuppressFindingValidationError> validationErrors,
            IEnumerable<string> warnings,
            IEnumerable<string> errors)
        {
            // Validation errors are separate from adapter errors so callers can distinguish bad requests from persistence failures.
            if (suppressedFindingCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(suppressedFindingCount), "The suppressed finding count cannot be negative.");
            }

            Succeeded = succeeded;
            SuppressedFindingCount = suppressedFindingCount;
            ValidationErrors = (validationErrors ?? throw new ArgumentNullException(nameof(validationErrors))).OrderBy(static error => error.Code, StringComparer.Ordinal).ThenBy(static error => error.Message, StringComparer.Ordinal).ToArray();
            Warnings = NormalizeDiagnostics(warnings);
            Errors = NormalizeDiagnostics(errors);
        }

        /// <summary>
        /// Gets a value indicating whether suppression persistence completed without blocking errors.
        /// </summary>
        public bool Succeeded { get; }

        /// <summary>
        /// Gets the number of findings that received suppression overlays.
        /// </summary>
        public int SuppressedFindingCount { get; }

        /// <summary>
        /// Gets validation errors that prevented one or more suppressions.
        /// </summary>
        public IReadOnlyList<SuppressFindingValidationError> ValidationErrors { get; }

        /// <summary>
        /// Gets the non-blocking diagnostics produced while applying suppressions.
        /// </summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>
        /// Gets the blocking diagnostics produced while applying suppressions.
        /// </summary>
        public IReadOnlyList<string> Errors { get; }

        /// <summary>
        /// Creates a successful suppression persistence result.
        /// </summary>
        /// <param name="suppressedFindingCount">The number of findings that received suppression overlays.</param>
        /// <param name="warnings">The optional non-blocking diagnostics produced while applying suppressions.</param>
        /// <returns>A successful suppression persistence result.</returns>
        public static SuppressionPersistenceResult Success(int suppressedFindingCount, IEnumerable<string>? warnings = null)
        {
            // Successful suppression can still carry warnings, such as unmatched suppression requests.
            return new SuppressionPersistenceResult(succeeded: true, suppressedFindingCount, [], warnings ?? [], []);
        }

        /// <summary>
        /// Creates a validation-failed suppression persistence result.
        /// </summary>
        /// <param name="validationErrors">The validation errors that prevented suppression.</param>
        /// <param name="warnings">The optional non-blocking diagnostics produced while processing suppressions.</param>
        /// <returns>A validation-failed suppression result.</returns>
        public static SuppressionPersistenceResult ValidationFailure(IEnumerable<SuppressFindingValidationError> validationErrors, IEnumerable<string>? warnings = null)
        {
            // Validation failures are not adapter errors, but the operation is not successful because suppression was not applied.
            ArgumentNullException.ThrowIfNull(validationErrors);
            return new SuppressionPersistenceResult(succeeded: false, suppressedFindingCount: 0, validationErrors, warnings ?? [], []);
        }

        /// <summary>
        /// Creates a failed suppression persistence result.
        /// </summary>
        /// <param name="errors">The blocking diagnostics that explain why persistence failed.</param>
        /// <param name="warnings">The optional non-blocking diagnostics produced before the failure.</param>
        /// <returns>A failed suppression persistence result.</returns>
        public static SuppressionPersistenceResult Failure(IEnumerable<string> errors, IEnumerable<string>? warnings = null)
        {
            // Adapter failures use the error collection while leaving validation errors empty.
            ArgumentNullException.ThrowIfNull(errors);
            return new SuppressionPersistenceResult(succeeded: false, suppressedFindingCount: 0, [], warnings ?? [], errors);
        }

        /// <summary>
        /// Normalizes diagnostic text into a deterministic immutable list.
        /// </summary>
        /// <param name="diagnostics">The diagnostic messages to normalize.</param>
        /// <returns>A list of trimmed non-empty diagnostic messages.</returns>
        private static IReadOnlyList<string> NormalizeDiagnostics(IEnumerable<string> diagnostics)
        {
            // Blank diagnostics do not explain suppression behavior and are omitted.
            ArgumentNullException.ThrowIfNull(diagnostics);
            return diagnostics.Where(static diagnostic => !string.IsNullOrWhiteSpace(diagnostic)).Select(static diagnostic => diagnostic.Trim()).ToArray();
        }
    }
}
