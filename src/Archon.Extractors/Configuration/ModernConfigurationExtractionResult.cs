using Archon.Application.Extraction.Contracts;

namespace Archon.Extractors.Configuration
{
    /// <summary>
    /// Represents graph snapshot contributions and diagnostics produced by configuration extraction.
    /// </summary>
    public sealed class ModernConfigurationExtractionResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ModernConfigurationExtractionResult"/> class.
        /// </summary>
        /// <param name="snapshot">The shared architecture snapshot containing configuration graph contributions and diagnostics.</param>
        public ModernConfigurationExtractionResult(ExtractedArchitectureSnapshot snapshot)
        {
            // The result wraps the shared snapshot contract so callers can merge configuration facts without learning an extractor-specific model.
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        /// <summary>
        /// Gets the shared architecture snapshot containing configuration graph contributions.
        /// </summary>
        public ExtractedArchitectureSnapshot Snapshot { get; }

        /// <summary>
        /// Gets the non-fatal warnings emitted during configuration extraction.
        /// </summary>
        public IReadOnlyList<string> Warnings
        {
            get
            {
                // Warnings are exposed directly for focused tests and later orchestration diagnostics.
                return Snapshot.Warnings;
            }
        }

        /// <summary>
        /// Gets the fatal errors emitted during configuration extraction.
        /// </summary>
        public IReadOnlyList<string> Errors
        {
            get
            {
                // Errors mirror the shared snapshot error stream without adding extractor-specific wrapping.
                return Snapshot.Errors;
            }
        }
    }
}
