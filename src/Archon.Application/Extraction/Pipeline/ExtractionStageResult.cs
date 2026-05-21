namespace Archon.Application.Extraction.Pipeline
{
    /// <summary>
    /// Describes whether an extraction stage completed successfully or produced a blocking error.
    /// </summary>
    public sealed class ExtractionStageResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ExtractionStageResult"/> class.
        /// </summary>
        /// <param name="hasBlockingError">Whether the stage produced an error that must stop later stages.</param>
        /// <param name="errorMessage">The optional credential-safe blocking error message.</param>
        private ExtractionStageResult(bool hasBlockingError, string? errorMessage)
        {
            // The result keeps stage execution flow explicit without throwing for controlled stage failures.
            HasBlockingError = hasBlockingError;
            ErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? null : errorMessage.Trim();
        }

        /// <summary>
        /// Gets a value indicating whether the stage produced an error that must stop later stages.
        /// </summary>
        public bool HasBlockingError { get; }

        /// <summary>
        /// Gets the optional credential-safe blocking error message.
        /// </summary>
        public string? ErrorMessage { get; }

        /// <summary>
        /// Creates a successful stage result that allows the pipeline to continue.
        /// </summary>
        /// <returns>A successful stage result.</returns>
        public static ExtractionStageResult Success()
        {
            // A singleton is unnecessary because the result object is tiny and immutable.
            return new ExtractionStageResult(hasBlockingError: false, errorMessage: null);
        }

        /// <summary>
        /// Creates a blocking stage result that stops the pipeline after the current stage.
        /// </summary>
        /// <param name="errorMessage">The credential-safe error message that explains why the stage stopped the pipeline.</param>
        /// <returns>A blocking stage result.</returns>
        public static ExtractionStageResult BlockingError(string errorMessage)
        {
            // Controlled blocking errors become snapshot diagnostics rather than raw exceptions in API responses.
            ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
            return new ExtractionStageResult(hasBlockingError: true, errorMessage);
        }
    }
}
